namespace UOHD2D.Game
{
    // Equipment layers a CharacterRig can wear. Head/Chest/Legs/Arms/Hands/Feet/Cloak
    // are bone-shared skinned pieces (they deform with the body skeleton). MainHand and
    // OffHand are reserved for future rigid props parented to a hand bone (weapon/shield)
    // and are not wired for skinned equip yet.
    public enum ArmorSlot
    {
        Head,
        Chest,
        Legs,
        Arms,
        Hands,
        Feet,
        Cloak,
        MainHand,
        OffHand
    }
}
