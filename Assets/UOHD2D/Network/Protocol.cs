namespace UOHD2D.Network
{
    // Opcodes and fixed lengths per ServUO's Server/Network/PacketHandlers.cs and Packets.cs.
    public static class Opcode
    {
        public const byte MovementReq = 0x02;
        public const byte LoginConfirm = 0x1B;
        public const byte MovementRej = 0x21;
        public const byte MovementAck = 0x22;
        public const byte MobileMoving = 0x77;
        public const byte MobileIncoming = 0x78;
        public const byte AccountLogin = 0x80;
        public const byte PlayServer = 0xA0;
        public const byte AccountLoginAck = 0xA8;
        public const byte CharacterList = 0xA9;
        public const byte PlayCharacter = 0x5D;
        public const byte GameLogin = 0x91;
        public const byte PlayServerAck = 0x8C;
        public const byte LoginComplete = 0x55;
        public const byte LoginServerSeed = 0xEF;
        public const byte ClientVersion = 0xBD;
        public const byte Ping = 0x73;
    }

    public struct ServerListEntry
    {
        public ushort Index;
        public string Name;
        public byte PercentFull;
        public sbyte TimeZone;
        public uint AddressValue; // big-endian packed IP as sent by the server
    }

    public struct CharacterListEntry
    {
        public int Slot;
        public string Name;
        public bool Occupied;
    }

    public struct MobileState
    {
        public uint Serial;
        public ushort Body;
        public int X;
        public int Y;
        public int Z;
        public byte Direction;
        public ushort Hue;
        public byte Notoriety;
    }
}
