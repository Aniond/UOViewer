using UnityEngine;

namespace UOHD2D
{
    /*
     * Live A/B switch for the character art style. F3 cycles:
     *
     *   Full 3D        - pixelizer off; the character renders crisp like the rest of the world
     *   Stylized       - pixelizer on at full resolution; no chunking, just the posterize/
     *                    saturation grade (a hint of sprite without losing detail)
     *   Pixel 2x/4x    - the HD-2D pixel-sprite looks, fine and chunky
     *
     * Attaches itself to whatever camera carries the UOHD2DPixelizer at scene load, so no scene
     * wiring is needed. This is a playtest tool for deciding the art direction; whichever mode
     * wins becomes the serialized default and this can go away (or ship as a player option).
     */
    public class UOHD2DPixelModeCycler : MonoBehaviour
    {
        public KeyCode CycleKey = KeyCode.F3;

        private static readonly string[] ModeNames =
            { "Full 3D", "Stylized (full-res grade)", "Pixel sprite 2x", "Pixel sprite 4x" };

        private UOHD2DPixelizer _pixelizer;
        private int _mode;
        private float _toastUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var pixelizer = Object.FindAnyObjectByType<UOHD2DPixelizer>(FindObjectsInactive.Include);
            if (pixelizer != null && pixelizer.GetComponent<UOHD2DPixelModeCycler>() == null)
                pixelizer.gameObject.AddComponent<UOHD2DPixelModeCycler>();
        }

        private void Awake()
        {
            _pixelizer = GetComponent<UOHD2DPixelizer>();

            // Start from whatever the scene is currently set to.
            if (_pixelizer == null || !_pixelizer.enabled)
                _mode = 0;
            else if (_pixelizer.PixelScale <= 1)
                _mode = 1;
            else if (_pixelizer.PixelScale <= 2)
                _mode = 2;
            else
                _mode = 3;
        }

        private void Update()
        {
            if (_pixelizer == null || !Input.GetKeyDown(CycleKey))
                return;

            _mode = (_mode + 1) % ModeNames.Length;
            Apply();
            _toastUntil = Time.unscaledTime + 2.5f;
            Debug.Log("[UOHD2D] Pixel mode -> " + ModeNames[_mode]);
        }

        private void Apply()
        {
            switch (_mode)
            {
                case 0:
                    _pixelizer.enabled = false; // OnDisable restores the main camera's culling mask
                    break;
                case 1:
                    _pixelizer.enabled = true;
                    _pixelizer.PixelScale = 1;
                    break;
                case 2:
                    _pixelizer.enabled = true;
                    _pixelizer.PixelScale = 2;
                    break;
                case 3:
                    _pixelizer.enabled = true;
                    _pixelizer.PixelScale = 4;
                    break;
            }
        }

        private void OnGUI()
        {
            if (Time.unscaledTime >= _toastUntil)
                return;

            const int w = 360;
            var rect = new Rect((Screen.width - w) / 2f, 24, w, 28);
            GUI.Box(rect, "Art style: " + ModeNames[_mode] + "   (F3 to cycle)");
        }
    }
}
