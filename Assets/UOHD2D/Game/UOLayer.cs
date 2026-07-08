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

        // Classic paperdoll draw order for clothing layers, innermost first: the compositor
        // paints them onto the body atlas in this sequence so a tunic covers the shirt, a robe
        // covers the tunic, and so on.
        private static readonly byte[] PaperdollOrder =
        {
            Shoes, Pants, Shirt, InnerLegs, InnerTorso, Waist, OuterLegs,
            MiddleTorso, Arms, Gloves, OuterTorso, Neck, Cloak, Helm,
        };

        // Composite order for a clothing layer - lower paints first (closer to the skin).
        // Unknown layers sort after all known ones, by their byte value.
        public static int DrawOrder(byte layer)
        {
            for (var i = 0; i < PaperdollOrder.Length; i++)
                if (PaperdollOrder[i] == layer)
                    return i;

            return 100 + layer;
        }

        // Human-readable layer name for UI (paperdoll equipment list).
        public static string LayerName(byte layer)
        {
            switch (layer)
            {
                case OneHanded:   return "Main hand";
                case TwoHanded:   return "Off hand";
                case Shoes:       return "Shoes";
                case Pants:       return "Pants";
                case Shirt:       return "Shirt";
                case Helm:        return "Head";
                case Gloves:      return "Gloves";
                case Ring:        return "Ring";
                case Neck:        return "Neck";
                case Hair:        return "Hair";
                case Waist:       return "Waist";
                case InnerTorso:  return "Chest";
                case Bracelet:    return "Bracelet";
                case FacialHair:  return "Beard";
                case MiddleTorso: return "Tunic";
                case Earrings:    return "Earrings";
                case Arms:        return "Arms";
                case Cloak:       return "Cloak";
                case OuterTorso:  return "Robe";
                case OuterLegs:   return "Outer legs";
                case InnerLegs:   return "Leggings";
                default:          return "Layer 0x" + layer.ToString("X2");
            }
        }

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
