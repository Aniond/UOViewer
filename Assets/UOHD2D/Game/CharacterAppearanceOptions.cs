using UnityEngine;

namespace UOHD2D.Game
{
    /*
     * The catalog of appearance choices the character creation wizard offers: skin tones, hair
     * styles (2D layers painted in the mannequin's UV space) and colors, and starting-clothes
     * hues. Built/refreshed by "UO HD2D/Wardrobe/Build Appearance Options"; referenced by
     * GameBootstrap so the wizard and the spawn-time apply both read the same catalog.
     */
    [CreateAssetMenu(menuName = "UO HD2D/Appearance Options")]
    public class CharacterAppearanceOptions : ScriptableObject
    {
        [System.Serializable]
        public struct NamedColor
        {
            public string Name;
            public Color Color;

            [Tooltip("The authentic UO hue number sent in the character creation packet (skin 1002-1058, hair 1102-1149, cloth 2-1001). The server clips out-of-range values.")]
            public ushort UoHue;
        }

        [System.Serializable]
        public struct HairStyle
        {
            public string Name;
            public Texture2D Texture;

            [Tooltip("The UO hair item id sent at character creation (e.g. 0x203B short, 0x203C long). The server equips this as a real hair layer.")]
            public ushort UoItemId;
        }

        [Tooltip("Skin tints multiplied over the base atlas. White = the atlas as authored.")]
        public NamedColor[] SkinTones;

        [Tooltip("Painted hair layers. The wizard offers 'Bald' in addition to these.")]
        public HairStyle[] HairStyles;

        public NamedColor[] HairColors;

        [Tooltip("Hues offered for the starting clothes.")]
        public NamedColor[] ClothHues;

        [Tooltip("Clothing layer art for the starting tunic/pants (UV-space RGBA).")]
        public Texture2D TunicTexture;
        public Texture2D PantsTexture;
    }
}
