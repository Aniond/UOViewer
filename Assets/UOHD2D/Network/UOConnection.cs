using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace UOHD2D.Network
{
    // Raw TCP transport to a ServUO login or game socket. No encryption: ServUO's
    // MessagePump exempts the modern login opcodes (0xEF/0x80/0x91/...) from its
    // "encrypted client required" check, and ships no Blowfish implementation by
    // default, so a plain client using the 0xEF seed handshake works unencrypted.
    public class UOConnection : IDisposable
    {
        private const int FixedLengthTableSize = 0x100;

        private static readonly int[] FixedLength = BuildFixedLengthTable();

        // Header size to skip to reach the payload: opcode-only (1) for fixed-length
        // packets, opcode+2-byte-length (3) for variable-length ones.
        public static int HeaderSize(byte opcode)
        {
            return FixedLength[opcode] > 0 ? 1 : 3;
        }

        private TcpClient _tcp;
        private NetworkStream _stream;
        private Thread _receiveThread;
        private volatile bool _running;

        private readonly byte[] _recvBuffer = new byte[ 8192 ];
        private int _recvLength;

        // Post-GameLogin, ServUO Huffman-compresses every outgoing packet
        // (Server/Network/PacketHandlers.cs GameLogin sets state.CompressionEnabled
        // = true right before sending SupportedFeatures/CharacterList). Enable this
        // once the game-server handshake completes so subsequent bytes get decoded
        // before framing. Not used on the login-server socket.
        public bool CompressionEnabled;
        private readonly HuffmanDecompressor _decompressor = new HuffmanDecompressor();
        private readonly byte[] _plainBuffer = new byte[16384];
        private int _plainLength;
        private readonly byte[] _decodeScratch = new byte[8192];

        private readonly object _queueLock = new object();
        private readonly Queue<byte[]> _incoming = new Queue<byte[]>();

        public bool IsConnected { get { return _tcp != null && _tcp.Connected; } }
        public string LastError { get; private set; }
        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }
        public int PacketsQueued { get; private set; }

        // Debug-only: snapshot of the unparsed head of the receive buffer.
        public byte[] PeekUnparsed(int max)
        {
            lock (_queueLock)
            {
                var n = Math.Min(max, _recvLength);
                var result = new byte[n];
                Array.Copy(_recvBuffer, result, n);
                return result;
            }
        }

        public void Connect(string host, int port)
        {
            Disconnect();

            _tcp = new TcpClient();
            _tcp.NoDelay = true;
            _tcp.Connect(host, port);
            _stream = _tcp.GetStream();

            _running = true;
            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            _receiveThread.Start();
        }

        public void Disconnect()
        {
            _running = false;

            try { _stream?.Close(); } catch { }
            try { _tcp?.Close(); } catch { }

            _stream = null;
            _tcp = null;
            _recvLength = 0;

            lock (_queueLock)
                _incoming.Clear();
        }

        public void Send(byte[] data)
        {
            if (_stream == null)
                return;

            try
            {
                _stream.Write(data, 0, data.Length);
                BytesSent += data.Length;
            }
            catch (Exception ex)
            {
                LastError = "Send: " + ex.Message;
                _running = false;
            }
        }

        // Call once per frame from the main thread. Invokes handler(opcode, ByteReader-over-payload).
        public void PumpMessages(Action<byte, byte[]> onPacket)
        {
            byte[] packet;

            while (true)
            {
                lock (_queueLock)
                {
                    if (_incoming.Count == 0)
                        return;

                    packet = _incoming.Dequeue();
                }

                onPacket(packet[0], packet);
            }
        }

        private void ReceiveLoop()
        {
            try
            {
                while (_running)
                {
                    var n = _stream.Read(_recvBuffer, _recvLength, _recvBuffer.Length - _recvLength);

                    if (n <= 0)
                        break;

                    BytesReceived += n;
                    _recvLength += n;
                    ExtractPackets();
                }
            }
            catch (Exception ex)
            {
                LastError = "Receive: " + ex.Message;
            }
            finally
            {
                _running = false;
            }
        }

        private void ExtractPackets()
        {
            if (CompressionEnabled)
            {
                DecompressAvailable();
                FramePackets(_plainBuffer, ref _plainLength);
            }
            else
            {
                FramePackets(_recvBuffer, ref _recvLength);
            }
        }

        // Drains as many whole compressed packets as currently available out of
        // _recvBuffer into _plainBuffer, advancing/compacting _recvBuffer past
        // whatever got consumed. Each Huffman unit decodes to exactly one packet's
        // plaintext bytes (the encoder emits one terminal code per Packet.Compile
        // call), so a partial/undecodable tail is left in _recvBuffer for the next read.
        private void DecompressAvailable()
        {
            var offset = 0;

            while (offset < _recvLength)
            {
                if (!_decompressor.TryDecodeOne(_recvBuffer, offset, _recvLength - offset, _decodeScratch, out var outLen, out var consumed))
                    break;

                if (_plainLength + outLen > _plainBuffer.Length)
                    break; // back off; caller will retry once PumpMessages drains _plainBuffer via FramePackets

                Array.Copy(_decodeScratch, 0, _plainBuffer, _plainLength, outLen);
                _plainLength += outLen;
                offset += consumed;
            }

            if (offset > 0)
            {
                var remaining = _recvLength - offset;
                Array.Copy(_recvBuffer, offset, _recvBuffer, 0, remaining);
                _recvLength = remaining;
            }
        }

        // Splits a plaintext buffer into whole packets. Fixed-length opcodes use the
        // table below; variable-length opcodes are prefixed with a big-endian ushort
        // total length (opcode byte included), matching ServUO's PacketHandler.Length == 0 case.
        private void FramePackets(byte[] buffer, ref int length)
        {
            var offset = 0;

            while (true)
            {
                if (length - offset < 1)
                    break;

                var opcode = buffer[offset];
                var fixedLen = FixedLength[opcode];
                int packetLen;

                if (fixedLen > 0)
                {
                    packetLen = fixedLen;
                }
                else
                {
                    if (length - offset < 3)
                        break;

                    packetLen = (buffer[offset + 1] << 8) | buffer[offset + 2];

                    if (packetLen < 3)
                    {
                        // Malformed variable-length header; drop the rest of the buffer.
                        offset = length;
                        break;
                    }
                }

                if (length - offset < packetLen)
                    break;

                var packet = new byte[packetLen];
                Array.Copy(buffer, offset, packet, 0, packetLen);

                lock (_queueLock)
                    _incoming.Enqueue(packet);

                offset += packetLen;
            }

            if (offset > 0)
            {
                var remaining = length - offset;
                Array.Copy(buffer, offset, buffer, 0, remaining);
                length = remaining;
            }
        }

        private static int[] BuildFixedLengthTable()
        {
            // Every opcode ServUO's Server/Network/Packets.cs can send, with fixed sizes
            // (opcode byte included) or 0 for variable-length (2-byte big-endian length
            // follows the opcode, per Packet.EnsureCapacity). Five opcodes (0x24, 0x25,
            // 0x99, 0xBA, 0xF3) have version-dependent fixed sizes in stock ServUO; we
            // sidestep that by declaring client version 7.0.61.0+ in the seed packet
            // (LoginFlow.ClientVersion), which pins NetState to its newest protocol
            // branch (Version70610) and therefore the largest/newest fixed size below.
            var t = new int[FixedLengthTableSize];

            t[0x0B] = 7;    // DamagePacket
            t[0x15] = 9;    // FollowMessage
            t[0x1B] = 37;   // LoginConfirm
            t[0x1D] = 5;    // RemoveItem / RemoveMobile
            t[0x20] = 19;   // MobileUpdate
            t[0x21] = 8;    // MovementRej
            t[0x22] = 3;    // MovementAck
            t[0x23] = 26;   // DragEffect
            t[0x24] = 9;    // ContainerDisplayHS (HS-client fixed size)
            t[0x25] = 21;   // ContainerContentUpdate6017 (HS-client fixed size)
            t[0x27] = 2;    // LiftRej
            t[0x2B] = 2;    // GodModeReply
            t[0x2C] = 2;    // DeathStatus
            t[0x2D] = 17;   // MobileAttributes
            t[0x2E] = 15;   // EquipUpdate
            t[0x2F] = 10;   // Swing
            t[0x38] = 7;    // PathfindMessage
            t[0x3B] = 8;    // EndVendorSell / EndVendorBuy
            t[0x4E] = 6;    // PersonalLightLevel
            t[0x4F] = 2;    // GlobalLightLevel
            t[0x53] = 2;    // PopupMessage
            t[0x54] = 12;   // PlaySound
            t[0x55] = 1;    // LoginComplete
            t[0x5B] = 4;    // CurrentTime
            t[0x65] = 4;    // Weather
            t[0x6C] = 19;   // TargetReq / CancelTarget
            t[0x6D] = 3;    // PlayMusic
            t[0x6E] = 14;   // MobileAnimation
            t[0x70] = 28;   // GraphicalEffect / ScreenEffect / BoltEffectNew
            t[0x72] = 5;    // SetWarMode
            t[0x73] = 2;    // PingAck
            t[0x76] = 16;   // ServerChange
            t[0x77] = 17;   // MobileMoving
            t[0x82] = 2;    // AccountLoginRej
            t[0x85] = 2;    // DeleteResult
            t[0x88] = 66;   // DisplayPaperdoll
            t[0x8C] = 11;   // PlayServerAck
            t[0x95] = 9;    // DisplayHuePicker
            t[0x97] = 2;    // PlayerMove
            t[0x99] = 30;   // MultiTargetReqHS (HS-client fixed size)
            t[0xA1] = 9;    // MobileHits
            t[0xA2] = 9;    // MobileMana
            t[0xA3] = 9;    // MobileStam
            t[0xAA] = 5;    // ChangeCombatant
            t[0xAF] = 13;   // DeathAnimation
            t[0xB9] = 5;    // SupportedFeatures (ExtendedSupportedFeatures branch, guaranteed by our declared client version)
            t[0xBA] = 10;   // SetArrowHS / CancelArrowHS (HS-client fixed size)
            t[0xBC] = 3;    // SeasonChange
            t[0xC0] = 36;   // HuedEffect / MovingEffect / LocationEffect / BoltEffect
            t[0xC6] = 1;    // InvalidMapEnable
            t[0xC7] = 49;   // ParticleEffect
            t[0xC8] = 2;    // ChangeUpdateRange
            t[0xC9] = 6;    // TripTimeResponse
            t[0xCA] = 6;    // UTripTimeResponse
            t[0xCB] = 7;    // GQCount
            t[0xD1] = 2;    // LogoutAck
            t[0xDC] = 9;    // OPLInfo (hash-only property list)
            t[0xE2] = 10;   // NewMobileAnimation
            t[0xF3] = 26;   // WorldItemHS (HS-client fixed size)

            // All other opcodes we might receive (0x11 status, 0x1A/0xF3-variants n/a,
            // 0x3A skill update, 0x3C container/vendor/spellbook content, 0x6F secure
            // trade, 0x74 vendor buy list, 0x78 mobile incoming, 0x81 change character,
            // 0x86 char list update, 0x8B sign gump, 0x8B/0xA5/0xA6/0xA8/0xA9/0xAE/0xB0/
            // 0xB7/0xB8/0xBD/0xBE/0xBF/0xC1/0xC2/0xC3/0xCC/0xD3/0xD6/0xDD) are all
            // variable-length (2-byte length prefix) and default to 0 in this table.

            return t;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
