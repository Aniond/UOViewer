using System;
using System.Collections.Generic;

namespace UOHD2D.Network
{
    public enum LoginStage
    {
        Disconnected,
        AwaitingServerList,
        AwaitingRelay,
        AwaitingCharacterList,
        AwaitingWorldEntry,
        InWorld,
        Failed,
    }

    // Drives ServUO's login handshake end to end: seed -> account login -> server
    // list -> play server -> relay reconnect -> game login -> character list ->
    // play character -> world entry. One UOConnection per socket (login socket is
    // discarded and replaced by a fresh one to the game server after PlayServerAck,
    // matching ServUO's separate login/game NetState model).
    public class LoginFlow
    {
        // Declaring 7.0.61.0+ pins ServUO's NetState to its newest protocol branch
        // (Version70610), which fixes the wire size of every version-ambiguous
        // opcode (0x24/0x25/0x99/0xBA/0xF3) to their newest/largest form.
        private static readonly int[] ClientVersion = { 7, 0, 61, 0 };

        private readonly string _host;
        private readonly int _port;
        private readonly string _account;
        private readonly string _password;

        private UOConnection _conn;
        private uint _seed;
        private ServerListEntry[] _servers;
        private uint _relayAuthId;
        private ServerListEntry _selectedServer;

        public LoginStage Stage { get; private set; }
        public string FailReason { get; private set; }
        public long BytesSent { get { return _conn?.BytesSent ?? 0; } }
        public long BytesReceived { get { return _conn?.BytesReceived ?? 0; } }
        public string ConnError { get { return _conn?.LastError; } }
        public bool ConnConnected { get { return _conn?.IsConnected ?? false; } }
        public string RelayTarget { get; private set; }
        public ServerListEntry[] Servers { get { return _servers; } }
        public List<CharacterListEntry> Characters { get; } = new List<CharacterListEntry>();

        public MobileState Player;
        public int WorldMapWidth, WorldMapHeight;

        public event Action<ServerListEntry[]> OnServerList;
        public event Action<List<CharacterListEntry>> OnCharacterList;
        public event Action OnWorldEntered;
        public event Action<MobileState> OnMobileIncoming;
        public event Action<MobileState> OnMobileMoving;
        public event Action<uint> OnMobileRemoved;
        public event Action<byte, int, int, int, byte> OnMovementRejected; // seq, x, y, z, dir

        public LoginFlow(string host, int port, string account, string password)
        {
            _host = host;
            _port = port;
            _account = account;
            _password = password;
        }

        public void Start()
        {
            Stage = LoginStage.AwaitingServerList;
            _conn = new UOConnection();
            _conn.Connect(_host, _port);
            SendSeedAndLogin(_conn);
        }

        private void SendSeedAndLogin(UOConnection conn)
        {
            var seedW = new ByteWriter(21);
            seedW.WriteByte(Opcode.LoginServerSeed);
            _seed = (uint)new Random().Next(1, int.MaxValue);
            seedW.WriteUInt32(_seed);
            seedW.WriteInt32(ClientVersion[0]);
            seedW.WriteInt32(ClientVersion[1]);
            seedW.WriteInt32(ClientVersion[2]);
            seedW.WriteInt32(ClientVersion[3]);
            conn.Send(seedW.ToArray());

            var loginW = new ByteWriter(62);
            loginW.WriteByte(Opcode.AccountLogin);
            loginW.WriteAsciiFixed(_account, 30);
            loginW.WriteAsciiFixed(_password, 30);
            loginW.WriteByte(0); // trailing pad byte: Register(0x80, 62, ...) counts the opcode, so 1+30+30 needs +1 to reach 62
            conn.Send(loginW.ToArray());
        }

        public void SelectServer(int index)
        {
            if (_servers == null || index < 0 || index >= _servers.Length)
                return;

            _selectedServer = _servers[index];

            var w = new ByteWriter(3);
            w.WriteByte(Opcode.PlayServer);
            w.WriteUInt16((ushort)_selectedServer.Index);
            _conn.Send(w.ToArray());

            Stage = LoginStage.AwaitingRelay;
        }

        public void SelectCharacter(int slot)
        {
            var w = new ByteWriter(73);
            w.WriteByte(Opcode.PlayCharacter);
            w.WriteInt32(unchecked((int)0xEDEDEDED));
            w.WriteAsciiFixed(Characters[slot].Name, 30);
            w.WriteUInt16(0); // pad
            w.WriteInt32(0);  // flags
            for (var i = 0; i < 24; i++) w.WriteByte(0); // no third-party auth
            w.WriteInt32(slot);
            w.WriteInt32(0); // client ip (unused by ServUO beyond logging)
            _conn.Send(w.ToArray());

            Stage = LoginStage.AwaitingWorldEntry;
        }

        public void SendMovement(byte direction, byte sequence)
        {
            var w = new ByteWriter(7);
            w.WriteByte(Opcode.MovementReq);
            w.WriteByte(direction);
            w.WriteByte(sequence);
            w.WriteInt32(0); // fastwalk key: unenforced unless the shard's anti-speedhack key is set
            _conn.Send(w.ToArray());
        }

        // Call once per frame from the main thread.
        public void Pump()
        {
            _conn?.PumpMessages(HandlePacket);
        }

        private void HandlePacket(byte opcode, byte[] data)
        {
            var header = UOConnection.HeaderSize(opcode);
            var r = new ByteReader(data, header, data.Length - header);

            switch (opcode)
            {
                case Opcode.AccountLoginAck:
                    HandleAccountLoginAck(r);
                    break;

                case Opcode.PlayServerAck:
                    HandlePlayServerAck(r);
                    break;

                case Opcode.CharacterList:
                    HandleCharacterList(data, r);
                    break;

                case Opcode.LoginConfirm:
                    HandleLoginConfirm(r);
                    break;

                case Opcode.MobileIncoming:
                    HandleMobileIncoming(data, r);
                    break;

                case Opcode.MobileMoving:
                    HandleMobileMoving(r);
                    break;

                case 0x1D: // RemoveItem / RemoveMobile
                    OnMobileRemoved?.Invoke((uint)r.ReadInt32());
                    break;

                case Opcode.MovementRej:
                {
                    var seq = r.ReadByte();
                    var x = r.ReadInt16();
                    var y = r.ReadInt16();
                    var dir = r.ReadByte();
                    var z = r.ReadSByte();
                    OnMovementRejected?.Invoke(seq, x, y, z, dir);
                    break;
                }

                case Opcode.LoginComplete:
                    Stage = LoginStage.InWorld;
                    OnWorldEntered?.Invoke();
                    break;

                case 0x82: // AccountLoginRej
                {
                    Stage = LoginStage.Failed;
                    var reason = r.ReadByte();
                    FailReason = "Account login rejected (reason " + reason + ")";
                    break;
                }

                default:
                    UnhandledOpcode?.Invoke(opcode, data.Length);
                    break;
            }
        }

        // Debug-only hook: fires for any opcode this client doesn't explicitly parse.
        public event System.Action<byte, int> UnhandledOpcode;

        private void HandleAccountLoginAck(ByteReader r)
        {
            r.ReadByte(); // 0x5D unknown flag
            var count = r.ReadUInt16();
            _servers = new ServerListEntry[count];

            for (var i = 0; i < count; i++)
            {
                _servers[i] = new ServerListEntry
                {
                    Index = r.ReadUInt16(),
                    Name = r.ReadAsciiFixed(32),
                    PercentFull = r.ReadByte(),
                    TimeZone = r.ReadSByte(),
                    AddressValue = r.ReadUInt32(),
                };
            }

            OnServerList?.Invoke(_servers);
        }

        private void HandlePlayServerAck(ByteReader r)
        {
            // IP is written as 4 raw bytes in host order by ServUO's Utility.GetAddressValue
            // (already-network-order octets), so read as four sequential bytes, not a BE uint.
            var b0 = r.ReadByte();
            var b1 = r.ReadByte();
            var b2 = r.ReadByte();
            var b3 = r.ReadByte();
            var port = r.ReadUInt16();
            _relayAuthId = r.ReadUInt32();

            var ip = string.Format("{0}.{1}.{2}.{3}", b0, b1, b2, b3);

            // If the shard reports itself as 0.0.0.0 (common on loopback/dev boxes),
            // fall back to the address we already successfully dialed for login.
            if (ip == "0.0.0.0")
                ip = _host;

            RelayTarget = ip + ":" + port;

            _conn.Disconnect();
            _conn = new UOConnection();
            _conn.Connect(ip, port);

            // ServUO's GameLogin requires authID == state.Seed for this connection
            // (Server/Network/PacketHandlers.cs GameLogin: "state.AuthID == 0 &&
            // authID != state.Seed" -> Dispose), so the seed we send here must be
            // the same authID PlayServerAck just gave us, not a fresh random value.
            var seedW = new ByteWriter(21);
            seedW.WriteByte(Opcode.LoginServerSeed);
            seedW.WriteUInt32(_relayAuthId);
            seedW.WriteInt32(ClientVersion[0]);
            seedW.WriteInt32(ClientVersion[1]);
            seedW.WriteInt32(ClientVersion[2]);
            seedW.WriteInt32(ClientVersion[3]);
            _conn.Send(seedW.ToArray());

            var gameLoginW = new ByteWriter(65);
            gameLoginW.WriteByte(Opcode.GameLogin);
            gameLoginW.WriteUInt32(_relayAuthId);
            gameLoginW.WriteAsciiFixed(_account, 30);
            gameLoginW.WriteAsciiFixed(_password, 30);
            _conn.Send(gameLoginW.ToArray());

            // ServUO flips state.CompressionEnabled = true the instant it accepts
            // GameLogin (Server/Network/PacketHandlers.cs), before sending anything
            // else, so every byte from here on over this socket is Huffman-coded.
            _conn.CompressionEnabled = true;

            Stage = LoginStage.AwaitingCharacterList;
        }

        private void HandleCharacterList(byte[] data, ByteReader r)
        {
            Characters.Clear();

            var count = r.ReadByte();

            for (var i = 0; i < count; i++)
            {
                var name = r.ReadAsciiFixed(30);
                r.Skip(30); // password field, unused in the response

                Characters.Add(new CharacterListEntry
                {
                    Slot = i,
                    Name = name,
                    Occupied = name.Length > 0,
                });
            }

            // City list / flags follow but aren't needed to select an existing character.
            OnCharacterList?.Invoke(Characters);
        }

        private void HandleLoginConfirm(ByteReader r)
        {
            Player.Serial = (uint)r.ReadInt32();
            r.ReadInt32(); // unknown
            Player.Body = r.ReadUInt16();
            Player.X = r.ReadInt16();
            Player.Y = r.ReadInt16();
            Player.Z = r.ReadInt16();
            Player.Direction = r.ReadByte();
            r.ReadByte();  // unknown
            r.ReadInt32(); // -1
            r.ReadInt16(); // unknown (0)
            r.ReadInt16(); // unknown (0)
            WorldMapWidth = r.ReadInt16();
            WorldMapHeight = r.ReadInt16();
        }

        private void HandleMobileIncoming(byte[] data, ByteReader r)
        {
            var m = new MobileState
            {
                Serial = (uint)r.ReadInt32(),
                Body = r.ReadUInt16(),
                X = r.ReadInt16(),
                Y = r.ReadInt16(),
                Z = r.ReadSByte(),
                Direction = r.ReadByte(),
                Hue = r.ReadUInt16(),
            };

            r.ReadByte(); // packet flags (mounted/etc) - not yet modeled client-side
            m.Notoriety = r.ReadByte();

            // Equipment list: repeated (serial, itemId, layer, [hue]) until a zero serial.
            // When itemId's high bit (0x8000) is set, a hue word follows; the id masks it off.
            m.Equipment = new System.Collections.Generic.List<EquipEntry>();

            while (true)
            {
                var serial = (uint)r.ReadInt32();
                if (serial == 0)
                    break;

                var itemId = r.ReadUInt16();
                var layer = r.ReadByte();
                ushort hue = 0;

                if ((itemId & 0x8000) != 0)
                {
                    itemId &= 0x7FFF;
                    hue = r.ReadUInt16();
                }

                m.Equipment.Add(new EquipEntry { Serial = serial, ItemId = itemId, Layer = layer, Hue = hue });
            }

            OnMobileIncoming?.Invoke(m);
        }

        private void HandleMobileMoving(ByteReader r)
        {
            var m = new MobileState
            {
                Serial = (uint)r.ReadInt32(),
                Body = r.ReadUInt16(),
                X = r.ReadInt16(),
                Y = r.ReadInt16(),
                Z = r.ReadSByte(),
                Direction = r.ReadByte(),
                Hue = r.ReadUInt16(),
            };

            r.ReadByte(); // packet flags
            m.Notoriety = r.ReadByte();

            OnMobileMoving?.Invoke(m);
        }

        public void Dispose()
        {
            _conn?.Dispose();
        }
    }
}
