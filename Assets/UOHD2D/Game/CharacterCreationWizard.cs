using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UOHD2D.Game
{
    /*
     * The pre-spawn character creation wizard: a live mannequin you dress before entering the
     * world. Skin tone, hair style/color, and starting-clothes hues cycle through the
     * CharacterAppearanceOptions catalog and paint onto the preview instantly (same wardrobe
     * pipeline the game uses). Done saves the choices locally; GameBootstrap applies them at
     * every spawn.
     *
     * Opened from the login screen ("Customize Character"). The preview character is a real
     * CharacterFactory spawn parked far above the world, so what you see is exactly what spawns.
     */
    public class CharacterCreationWizard : MonoBehaviour
    {
        public CharacterProfile Profile;
        public CharacterAppearanceOptions Options;

        // Creation mode: when set, the wizard also asks for a name + class and the confirm
        // button hands (appearance, name, profession) to this callback - GameBootstrap sends
        // the UO character creation packet from it. The server grants the class's starting
        // armor/gear as real items. When null, the wizard just saves local appearance (Done).
        public System.Action<CharacterAppearance, string, int> OnCreate;
        public string CharacterName = "Adventurer";

        // ServUO profession ids; 0 = no class, basic clothes only. 8 = Ranger, our custom
        // profession block in ServUO's CharacterCreation.cs (green studded suite + bow).
        private static readonly string[] Professions =
            { "Adventurer", "Warrior", "Magician", "Blacksmith", "Necromancer", "Paladin", "Samurai", "Ninja", "Ranger" };
        private int _profession;

        // True while any wizard is on screen - gameplay input (PlayerAvatar) is locked out.
        public static bool IsOpen { get; private set; }

        private CharacterAppearance _appearance;
        private CharacterRig _preview;
        private Camera _cam;
        private RenderTexture _rt;
        private GameObject _light;
        private float _orbitYaw;

        private const int PreviewW = 380;
        private const int PreviewH = 600;

        public static CharacterCreationWizard Open(GameObject host, CharacterProfile profile, CharacterAppearanceOptions options)
        {
            var wizard = host.GetComponent<CharacterCreationWizard>();
            if (wizard == null)
                wizard = host.AddComponent<CharacterCreationWizard>();

            wizard.Profile = profile;
            wizard.Options = options;
            return wizard;
        }

        private void OnEnable()
        {
            IsOpen = true;
        }

        private void Start()
        {
            _appearance = CharacterAppearance.Load();

            // Authentic UO: every new character starts wearing basic clothes (the server
            // creates real shirt/pants items), so the wizard offers colors, not toggles.
            _appearance.WearTunic = true;
            _appearance.WearPants = true;

            CreatePreview();
            ApplyToPreview();
        }

        private void CreatePreview()
        {
            _preview = CharacterFactory.Create(Profile, LayerMask.NameToLayer("Character"));
            _preview.gameObject.name = "WizardPreview";
            _preview.transform.position = new Vector3(0f, 800f, 0f);

            var camGo = new GameObject("Wizard Camera") { hideFlags = HideFlags.HideAndDontSave };
            _cam = camGo.AddComponent<Camera>();
            _cam.cullingMask = 1 << _preview.gameObject.layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.1f, 0.09f, 0.08f, 1f);
            _cam.fieldOfView = 32f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 10f;

            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.renderShadows = false;

            _rt = new RenderTexture(PreviewW, PreviewH, 24, RenderTextureFormat.ARGB32)
            {
                name = "UOHD2D_WizardRT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _rt.Create();
            _cam.targetTexture = _rt;

            // The preview floats far above the world, out of reach of the scene's sun - give it
            // its own key light.
            _light = new GameObject("Wizard Light") { hideFlags = HideFlags.HideAndDontSave };
            var light = _light.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            _light.transform.rotation = Quaternion.Euler(40f, 160f, 0f);
        }

        private void Update()
        {
            if (_preview == null || _cam == null)
                return;

            var t = _preview.transform;
            var facingYaw = t.eulerAngles.y - _preview.ModelYawOffset + _orbitYaw;
            var forward = Quaternion.Euler(0f, facingYaw, 0f) * Vector3.forward;

            var focus = t.position + Vector3.up * 0.92f;
            _cam.transform.position = focus + forward * 3.1f + Vector3.up * 0.1f;
            _cam.transform.LookAt(focus);
        }

        private void ApplyToPreview()
        {
            if (_preview != null)
                _appearance.Apply(_preview, Options);
        }

        public void Close()
        {
            Cleanup();
            Destroy(this);
        }

        private void OnDestroy()
        {
            IsOpen = false;
            Cleanup();
        }

        private void Cleanup()
        {
            if (_preview != null)
                Destroy(_preview.gameObject);
            if (_cam != null)
            {
                // Unbind before releasing the RT - Destroy is deferred, Release is immediate,
                // and releasing a bound targetTexture logs an error.
                _cam.targetTexture = null;
                Destroy(_cam.gameObject);
            }
            if (_light != null)
                Destroy(_light);
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        private void OnGUI()
        {
            if (Options == null)
            {
                GUI.Box(new Rect(16, 16, 380, 40), "No CharacterAppearanceOptions assigned - run 'UO HD2D/Wardrobe/Build Appearance Options'.");
                return;
            }

            // ClassicUO's creation layout: name up top, front-facing preview center stage,
            // style/class pickers on the left, color swatches on the right, gender + confirm
            // along the bottom.
            const int w = 780;
            const int h = 660;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

            GUILayout.BeginArea(rect, GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(OnCreate != null ? "Character Name" : "Customize Character");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (OnCreate != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                CharacterName = GUILayout.TextField(CharacterName, 16, GUILayout.Width(300));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            var changed = false;

            // LEFT: hair style + class.
            GUILayout.BeginVertical(GUILayout.Width(190));
            GUILayout.Label("Hair Style");

            var hairCount = Options.HairStyles != null ? Options.HairStyles.Length : 0;
            var hairIndex = _appearance.HairStyle + 1; // 0 = bald
            if (CycleRow(ref hairIndex, hairCount + 1, i => i == 0 ? "Bald" : Options.HairStyles[i - 1].Name))
            {
                _appearance.HairStyle = hairIndex - 1;
                changed = true;
            }

            if (OnCreate != null)
            {
                GUILayout.Space(14);
                GUILayout.Label("Class");
                CycleRow(ref _profession, Professions.Length, i => Professions[i]);
                GUILayout.Label(ProfessionBlurb(_profession), GUI.skin.box);
            }

            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // CENTER: front-facing live preview, rotate underneath.
            GUILayout.BeginVertical(GUILayout.Width(300));
            var previewRect = GUILayoutUtility.GetRect(300, 440, GUILayout.Width(300), GUILayout.Height(440));
            if (_rt != null)
                GUI.DrawTexture(previewRect, _rt, ScaleMode.ScaleAndCrop);

            GUILayout.BeginHorizontal();
            if (GUILayout.RepeatButton("< Rotate"))
                _orbitYaw -= 120f * Time.deltaTime;
            if (GUILayout.RepeatButton("Rotate >"))
                _orbitYaw += 120f * Time.deltaTime;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // RIGHT: color swatches - click one to cycle to its next color.
            GUILayout.BeginVertical(GUILayout.Width(190));
            changed |= Swatch("Skin Tone", Options.SkinTones, ref _appearance.SkinTone);
            changed |= Swatch("Shirt Color", Options.ClothHues, ref _appearance.TunicHue);
            changed |= Swatch("Pants Color", Options.ClothHues, ref _appearance.PantsHue);

            if (_appearance.HairStyle >= 0)
                changed |= Swatch("Hair Color", Options.HairColors, ref _appearance.HairColor);

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            if (changed)
                ApplyToPreview();

            GUILayout.FlexibleSpace();

            // Gender row - female body still pending, so male only for now.
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Toggle(true, " Male", GUI.skin.button, GUILayout.Width(100));
            GUI.enabled = false;
            GUILayout.Toggle(false, " Female (soon)", GUI.skin.button, GUILayout.Width(130));
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (OnCreate != null)
            {
                var name = CharacterName.Trim();
                GUI.enabled = name.Length >= 2;

                if (GUILayout.Button(name.Length >= 2 ? "Create \"" + name + "\"" : "Create (name too short)", GUILayout.Width(300), GUILayout.Height(34)))
                {
                    _appearance.Save();
                    var callback = OnCreate;
                    var prof = _profession;
                    Close();
                    callback(_appearance, name, prof);
                }

                GUI.enabled = true;
            }
            else if (GUILayout.Button("Done", GUILayout.Width(300), GUILayout.Height(34)))
            {
                _appearance.Save();
                Close();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        // What the server hands each ServUO profession at creation (real items).
        private static string ProfessionBlurb(int prof)
        {
            switch (prof)
            {
                case 1: return "Leather armor,\nsword & shield";
                case 2: return "Robe, staff\n& spellbook";
                case 3: return "Smith hammer,\ntools & apron";
                case 4: return "Dyed leather,\nbone helm & book";
                case 5: return "Ringmail, helm\n& broadsword";
                case 6: return "Hakama\n& bokuto";
                case 7: return "Ninja garb\n& leafblade";
                case 8: return "Green studded\nsuite, bow\n& 50 arrows";
                default: return "Basic clothes\nin your colors";
            }
        }

        private static bool CycleRow(ref int index, int count, System.Func<int, string> nameOf)
        {
            if (count <= 0)
                return false;

            GUILayout.BeginHorizontal();

            var changed = false;
            if (GUILayout.Button("<", GUILayout.Width(28)))
            {
                index = (index - 1 + count) % count;
                changed = true;
            }

            GUILayout.Label(nameOf(Mathf.Clamp(index, 0, count - 1)), GUILayout.ExpandWidth(true));

            if (GUILayout.Button(">", GUILayout.Width(28)))
            {
                index = (index + 1) % count;
                changed = true;
            }

            GUILayout.EndHorizontal();
            return changed;
        }

        private static GUIStyle _swatchStyle;

        // A ClassicUO-style color block: shows the current color, click to cycle.
        private static bool Swatch(string label, CharacterAppearanceOptions.NamedColor[] list, ref int index)
        {
            if (list == null || list.Length == 0)
                return false;

            if (_swatchStyle == null)
            {
                _swatchStyle = new GUIStyle(GUI.skin.button);
                _swatchStyle.normal.background = Texture2D.whiteTexture;
                _swatchStyle.hover.background = Texture2D.whiteTexture;
                _swatchStyle.active.background = Texture2D.whiteTexture;
            }

            GUILayout.Label(label);

            index = Mathf.Clamp(index, 0, list.Length - 1);
            var color = list[index].Color;

            // Keep the color's name readable on any swatch.
            var lum = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
            var text = lum > 0.5f ? Color.black : Color.white;
            _swatchStyle.normal.textColor = text;
            _swatchStyle.hover.textColor = text;
            _swatchStyle.active.textColor = text;

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = color;
            var clicked = GUILayout.Button(list[index].Name, _swatchStyle, GUILayout.Height(28));
            GUI.backgroundColor = prev;

            if (!clicked)
                return false;

            index = (index + 1) % list.Length;
            return true;
        }
    }
}
