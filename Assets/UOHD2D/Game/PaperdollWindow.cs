using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UOHD2D.Game
{
    /*
     * The classic UO paperdoll, HD-2D style: instead of layered 2D gump art, the window shows a
     * LIVE crisp render of the player's dressed 3D mannequin (its own camera, no pixelation - the
     * paperdoll is where you admire the outfit), the classic right-hand menu buttons, and the
     * worn-equipment list the server reports. Toggle with P while in world.
     *
     * GameBootstrap creates one on world entry, points it at the player rig, and forwards each
     * self MobileIncoming equipment list via SetEquipment.
     */
    public class PaperdollWindow : MonoBehaviour
    {
        [Tooltip("The character this paperdoll views.")]
        public CharacterRig Target;

        [Tooltip("Catalog used to show item names for worn UO item ids.")]
        public GearCatalog Gear;

        public string CharacterName = "";
        public KeyCode ToggleKey = KeyCode.P;

        // Classic paperdoll menu. Placeholders until each screen exists.
        private static readonly string[] MenuButtons =
            { "Help", "Options", "Log Out", "Quests", "Skills", "Guild", "Peace", "Status" };

        private readonly List<Network.EquipEntry> _equipment = new List<Network.EquipEntry>();
        private bool _open;
        private Camera _cam;
        private RenderTexture _rt;

        private const int PreviewW = 340;
        private const int PreviewH = 560;

        public bool IsOpen => _open;

        // Replace the displayed worn list (the server's word, via MobileIncoming 0x78).
        public void SetEquipment(IEnumerable<Network.EquipEntry> equipment)
        {
            _equipment.Clear();
            if (equipment != null)
                _equipment.AddRange(equipment);
        }

        public void Toggle()
        {
            _open = !_open;

            if (_open && _cam == null)
                CreatePreviewCamera();

            if (_cam != null)
                _cam.enabled = _open; // URP renders enabled RT cameras each frame
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey))
                Toggle();

            if (_open && _cam != null && Target != null)
                PositionCamera();
        }

        private void CreatePreviewCamera()
        {
            var go = new GameObject("Paperdoll Camera") { hideFlags = HideFlags.HideAndDontSave };
            _cam = go.AddComponent<Camera>();
            _cam.cullingMask = 1 << (Target != null ? Target.gameObject.layer : 0);
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.09f, 0.08f, 0.07f, 1f); // classic dark gump backdrop
            _cam.fieldOfView = 35f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 6f; // tight so nearby characters don't photobomb the portrait

            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.renderShadows = false;

            _rt = new RenderTexture(PreviewW, PreviewH, 24, RenderTextureFormat.ARGB32)
            {
                name = "UOHD2D_PaperdollRT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _rt.Create();
            _cam.targetTexture = _rt;
        }

        // Hold the camera in front of wherever the character currently faces, framing head to toe.
        private void PositionCamera()
        {
            var t = Target.transform;
            var facingYaw = t.eulerAngles.y - Target.ModelYawOffset;
            var forward = Quaternion.Euler(0f, facingYaw, 0f) * Vector3.forward;

            var focus = t.position + Vector3.up * 0.92f;
            _cam.transform.position = focus + forward * 2.9f + Vector3.up * 0.12f;
            _cam.transform.LookAt(focus);
        }

        private void OnDestroy()
        {
            if (_cam != null)
                Destroy(_cam.gameObject);

            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
        }

        private void OnGUI()
        {
            if (!_open)
                return;

            const int w = 330;
            const int h = 600;
            var rect = new Rect(Screen.width - w - 24, 48, w, h);

            GUILayout.BeginArea(rect, GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(CharacterName) ? "Paperdoll" : CharacterName, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("X", GUILayout.Width(24)))
                Toggle();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            // Live character view on the left (the "paperdoll art").
            var previewRect = GUILayoutUtility.GetRect(190, 330, GUILayout.Width(190), GUILayout.Height(330));
            if (_rt != null)
                GUI.DrawTexture(previewRect, _rt, ScaleMode.ScaleAndCrop);

            // Classic menu column on the right.
            GUILayout.BeginVertical(GUILayout.Width(110));
            foreach (var label in MenuButtons)
            {
                if (GUILayout.Button(label))
                    Debug.Log("[UOHD2D] Paperdoll: '" + label + "' not implemented yet.");
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Worn equipment");

            if (_equipment.Count == 0)
            {
                GUILayout.Label("  (nothing reported by the server)");
            }
            else
            {
                foreach (var e in _equipment)
                {
                    var item = Gear != null ? Gear.Resolve(e.ItemId) : null;
                    var name = item != null ? item.DisplayName : "0x" + e.ItemId.ToString("X4");
                    GUILayout.Label("  " + UOLayer.LayerName(e.Layer) + ":  " + name);
                }
            }

            GUILayout.EndArea();
        }
    }
}
