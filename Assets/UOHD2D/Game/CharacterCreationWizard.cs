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

        private void Start()
        {
            _appearance = CharacterAppearance.Load();
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

            const int w = 640;
            const int h = 640;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("Create your character");

            GUILayout.BeginHorizontal();

            // Live preview + rotate controls.
            GUILayout.BeginVertical(GUILayout.Width(300));
            var previewRect = GUILayoutUtility.GetRect(300, 500, GUILayout.Width(300), GUILayout.Height(500));
            if (_rt != null)
                GUI.DrawTexture(previewRect, _rt, ScaleMode.ScaleAndCrop);

            GUILayout.BeginHorizontal();
            if (GUILayout.RepeatButton("< Rotate"))
                _orbitYaw -= 120f * Time.deltaTime;
            if (GUILayout.RepeatButton("Rotate >"))
                _orbitYaw += 120f * Time.deltaTime;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(12);

            // Option cyclers.
            GUILayout.BeginVertical();
            var changed = false;

            changed |= CycleRow("Skin", ref _appearance.SkinTone, NameCount(Options.SkinTones), i => Options.SkinTones[i].Name);

            var hairCount = Options.HairStyles != null ? Options.HairStyles.Length : 0;
            var hairIndex = _appearance.HairStyle + 1; // 0 = bald
            if (CycleRow("Hair", ref hairIndex, hairCount + 1, i => i == 0 ? "Bald" : Options.HairStyles[i - 1].Name))
            {
                _appearance.HairStyle = hairIndex - 1;
                changed = true;
            }

            if (_appearance.HairStyle >= 0)
                changed |= CycleRow("Hair color", ref _appearance.HairColor, NameCount(Options.HairColors), i => Options.HairColors[i].Name);

            GUILayout.Space(10);

            changed |= ToggleRow("Tunic", ref _appearance.WearTunic);
            if (_appearance.WearTunic)
                changed |= CycleRow("Tunic hue", ref _appearance.TunicHue, NameCount(Options.ClothHues), i => Options.ClothHues[i].Name);

            changed |= ToggleRow("Pants", ref _appearance.WearPants);
            if (_appearance.WearPants)
                changed |= CycleRow("Pants hue", ref _appearance.PantsHue, NameCount(Options.ClothHues), i => Options.ClothHues[i].Name);

            if (changed)
                ApplyToPreview();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Done", GUILayout.Height(32)))
            {
                _appearance.Save();
                Close();
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static int NameCount<T>(T[] array)
        {
            return array != null ? array.Length : 0;
        }

        private static bool CycleRow(string label, ref int index, int count, System.Func<int, string> nameOf)
        {
            if (count <= 0)
                return false;

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(80));

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

        private static bool ToggleRow(string label, ref bool value)
        {
            var next = GUILayout.Toggle(value, " " + label);
            if (next == value)
                return false;

            value = next;
            return true;
        }
    }
}
