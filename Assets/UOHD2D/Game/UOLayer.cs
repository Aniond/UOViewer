namespace UOHD2D.Game
{
    // UO equipment layer byte values (ServUO Server/Items/Layer.cs). The server tags each worn
    // item with one of these; we map the ones we render onto our ArmorSlot.
    public static class UOLayer
    {
        public const byte OneHanded = 0x01;
        public const byte TwoHanded = 0x02;   // shields + 2h weapons
        public const byte Shoes = 0x03;
        public const byte Pants = 0x04;
        public const byte Shirt = 0x05;
        public const byte Helm = 0x06;
        public const byte Gloves = 0x07;
        public const byte Ring = 0x08;
        public const byte Neck = 0x0A;
        public const byte Hair = 0x0B;
        public const byte Waist = 0x0C;
        public const byte InnerTorso = 0x0D;
        public const byte Bracelet = 0x0E;
        public const byte FacialHair = 0x10;
        public const byte MiddleTorso = 0x11; // tunics/surcoats
        public const byte Earrings = 0x12;
        public const byte Arms = 0x13;
        public const byte Cloak = 0x14;
        public const byte OuterTorso = 0x16;  // robes/chest armor
        public const byte OuterLegs = 0x17;
        public const byte InnerLegs = 0x18;

        // Map a UO layer to our equipment slot. Returns false for layers we don't render
        // (rings, hair, etc.) so the caller can skip them.
        public static bool ToSlot(byte layer, out ArmorSlot slot)
        {
            switch (layer)
            {
                case Helm:        slot = ArmorSlot.Head;    return true;
                case OneHanded:   slot = ArmorSlot.MainHand; return true;
                case TwoHanded:   slot = ArmorSlot.OffHand;  return true; // shield/2h -> off hand
                case Gloves:      slot = ArmorSlot.Hands;   return true;
                case Shoes:       slot = ArmorSlot.Feet;    return true;
                case Arms:        slot = ArmorSlot.Arms;    return true;
                case Cloak:       slot = ArmorSlot.Cloak;   return true;
                case Pants:
                case InnerLegs:
                case OuterLegs:   slot = ArmorSlot.Legs;    return true;
                case Shirt:
                case MiddleTorso:
                case InnerTorso:
                case OuterTorso:  slot = ArmorSlot.Chest;   return true;
                default:          slot = ArmorSlot.Head;    return false;
            }
        }
    }
}
