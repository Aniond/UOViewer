using UnityEngine;

namespace UOHD2D.Game
{
    /*
     * One player's appearance choices from the creation wizard - indices into
     * CharacterAppearanceOptions plus on/off for the starting clothes. Persisted locally
     * (PlayerPrefs JSON) and applied to the rig at every spawn; the server still owns real
     * equipment, which layers over/under these choices via the normal wardrobe rules.
     */
    [System.Serializable]
    public class CharacterAppearance
    {
        public int SkinTone;
        public int HairStyle = -1;   // -1 = bald, else index into Options.HairStyles
        public int HairColor;
        public bool WearTunic = true;
        public int TunicHue;
        public bool WearPants = true;
        public int PantsHue = 1;

        private const string PrefsKey = "UOHD2D_Appearance";

        public static CharacterAppearance Load()
        {
            var json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json))
                return new CharacterAppearance();

            var loaded = JsonUtility.FromJson<CharacterAppearance>(json);
            return loaded ?? new CharacterAppearance();
        }

        public void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        // Paint these choices onto a character. Safe on any rig - without a compositor or a
        // skin atlas (stand-ins, flat-color bodies) it is a no-op.
        public void Apply(CharacterRig rig, CharacterAppearanceOptions options)
        {
            if (rig == null || options == null)
                return;

            var comp = rig.Compositor;
            if (comp == null)
                return;

            if (options.SkinTones != null && options.SkinTones.Length > 0)
                comp.SetSkinTint(options.SkinTones[Mathf.Clamp(SkinTone, 0, options.SkinTones.Length - 1)].Color);

            var hasHair = options.HairStyles != null && options.HairStyles.Length > 0 &&
                          HairStyle >= 0 && HairStyle < options.HairStyles.Length &&
                          options.HairStyles[HairStyle].Texture != null;

            if (hasHair)
            {
                var color = options.HairColors != null && options.HairColors.Length > 0
                    ? options.HairColors[Mathf.Clamp(HairColor, 0, options.HairColors.Length - 1)].Color
                    : Color.white;
                comp.SetLayer(UOLayer.Hair, options.HairStyles[HairStyle].Texture, color);
            }
            else
            {
                comp.ClearLayer(UOLayer.Hair);
            }

            // Painted on the same UO layers the server uses for starting clothes (Shirt/Pants),
            // so when a server-created character's REAL items arrive they replace these
            // stand-ins on the same compositor keys instead of stacking under them.
            ApplyClothing(comp, UOLayer.Shirt, WearTunic, options.TunicTexture, options, TunicHue);
            ApplyClothing(comp, UOLayer.Pants, WearPants, options.PantsTexture, options, PantsHue);
        }

        // The UO wire values for these choices, for the classic character creation packet.
        // The server creates REAL clothing/hair items from them, so the spawn the server
        // describes matches what the wizard previewed. Bald sends hair id 0.
        public void ToUoCreation(CharacterAppearanceOptions options,
            out ushort skinHue, out ushort hairItemId, out ushort hairHue, out ushort shirtHue, out ushort pantsHue)
        {
            skinHue = PickHue(options != null ? options.SkinTones : null, SkinTone, 1002);
            hairHue = PickHue(options != null ? options.HairColors : null, HairColor, 1102);
            shirtHue = PickHue(options != null ? options.ClothHues : null, TunicHue, 0);
            pantsHue = PickHue(options != null ? options.ClothHues : null, PantsHue, 0);

            hairItemId = 0;
            if (options != null && options.HairStyles != null && HairStyle >= 0 && HairStyle < options.HairStyles.Length)
                hairItemId = options.HairStyles[HairStyle].UoItemId;
        }

        private static ushort PickHue(CharacterAppearanceOptions.NamedColor[] list, int index, ushort fallback)
        {
            if (list == null || list.Length == 0)
                return fallback;

            var hue = list[Mathf.Clamp(index, 0, list.Length - 1)].UoHue;
            return hue != 0 ? hue : fallback;
        }

        private static void ApplyClothing(ClothingCompositor comp, byte layer, bool worn,
            Texture2D tex, CharacterAppearanceOptions options, int hueIndex)
        {
            if (!worn || tex == null)
            {
                comp.ClearLayer(layer);
                return;
            }

            var hue = options.ClothHues != null && options.ClothHues.Length > 0
                ? options.ClothHues[Mathf.Clamp(hueIndex, 0, options.ClothHues.Length - 1)].Color
                : Color.white;
            comp.SetLayer(layer, tex, hue);
        }
    }
}
