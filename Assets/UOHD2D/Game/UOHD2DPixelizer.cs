using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UOHD2D
{
    /*
     * Character-only pixelation for the HD-2D look. A dedicated camera renders ONLY the Character
     * layer into a low-resolution, point-filtered render target; the main camera renders the world
     * at full resolution with the Character layer excluded. A full-screen composite then draws the
     * chunky character back over the crisp world - depth-tested so the character hides behind world
     * geometry (walking behind a wall).
     *
     * Drop this on the same GameObject as the gameplay Camera (the rig's "UO Camera"). It clones
     * that camera's projection every frame so it always matches the HD-2D follow view.
     *
     * PixelScale is the integer downscale: character RT height = screenHeight / PixelScale. Higher
     * = chunkier. 4 (~270p at 1080p) reads as a large HD-2D hero sprite. Tune live in the Inspector.
     */
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class UOHD2DPixelizer : MonoBehaviour
    {
        [Tooltip("Integer downscale for the character render target. Higher = chunkier pixels.")]
        [Range(1, 8)] public int PixelScale = 4;

        [Tooltip("Layer the characters live on. It is rendered pixelated; the main camera excludes it.")]
        public string CharacterLayerName = "Character";

        [Tooltip("Material using the UO HD2D/PixelComposite shader.")]
        public Material CompositeMaterial;

        private Camera _mainCam;
        private Camera _charCam;
        private int _charLayer;
        private RenderTexture _rt;
        private int _rtW, _rtH;
        private Mesh _quad;

        private static readonly int CharTexId = Shader.PropertyToID("_CharColor");

        private void OnEnable()
        {
            _mainCam = GetComponent<Camera>();
            _charLayer = LayerMask.NameToLayer(CharacterLayerName);

            if (_charLayer < 0)
            {
                Debug.LogError("[UOHD2D] Pixelizer: layer '" + CharacterLayerName + "' does not exist. Run 'UO HD2D/Setup Character Pixelation'.");
                enabled = false;
                return;
            }

            if (CompositeMaterial == null)
            {
                var sh = Shader.Find("UO HD2D/PixelComposite");
                if (sh != null)
                    CompositeMaterial = new Material(sh) { name = "UOHD2D_PixelComposite (auto)" };
            }

            _mainCam.cullingMask &= ~(1 << _charLayer); // world only
            CreateCharCamera();

            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

            if (_mainCam != null && _charLayer >= 0)
                _mainCam.cullingMask |= (1 << _charLayer); // restore

            if (_charCam != null)
                DestroyImmediate(_charCam.gameObject);

            ReleaseRT();
        }

        private void CreateCharCamera()
        {
            var go = new GameObject("UO Pixel Char Camera") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);

            _charCam = go.AddComponent<Camera>();
            _charCam.CopyFrom(_mainCam);
            _charCam.cullingMask = 1 << _charLayer;
            _charCam.clearFlags = CameraClearFlags.SolidColor;
            _charCam.backgroundColor = new Color(0, 0, 0, 0);
            _charCam.enabled = false; // driven manually

            var data = _charCam.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.renderShadows = true;         // let the char self-shadow in its own pass
            data.requiresDepthOption = CameraOverrideOption.On;
        }

        private void EnsureRT()
        {
            int h = Mathf.Max(16, Screen.height / Mathf.Max(1, PixelScale));
            int w = Mathf.Max(16, Screen.width / Mathf.Max(1, PixelScale));

            if (_rt != null && _rtW == w && _rtH == h)
                return;

            ReleaseRT();
            _rtW = w; _rtH = h;

            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = "UOHD2D_CharRT",
                filterMode = FilterMode.Point,
                useMipMap = false,
                antiAliasing = 1,
                wrapMode = TextureWrapMode.Clamp
            };
            _rt.Create();
        }

        private void ReleaseRT()
        {
            if (_rt != null)
            {
                _rt.Release();
                DestroyImmediate(_rt);
                _rt = null;
            }
        }

        private void LateUpdate()
        {
            if (_charCam == null)
                return;

            _charCam.CopyFrom(_mainCam);
            _charCam.cullingMask = 1 << _charLayer;
            _charCam.clearFlags = CameraClearFlags.SolidColor;
            _charCam.backgroundColor = new Color(0, 0, 0, 0);
            _charCam.enabled = false;

            EnsureRT();
        }

        // After the world camera renders, render the character-only low-res pass and composite it
        // over the screen with a depth test against the scene.
        private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _mainCam || _charCam == null || _rt == null || CompositeMaterial == null)
                return;

            _charCam.targetTexture = _rt;
#pragma warning disable CS0618
            UniversalRenderPipeline.RenderSingleCamera(ctx, _charCam);
#pragma warning restore CS0618
            _charCam.targetTexture = null;

            CompositeMaterial.SetTexture(CharTexId, _rt);

            var cmd = CommandBufferPool.Get("UOHD2D Pixel Composite");
            cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            cmd.DrawMesh(Quad(), Matrix4x4.identity, CompositeMaterial, 0, 0);
            ctx.ExecuteCommandBuffer(cmd);
            ctx.Submit();
            CommandBufferPool.Release(cmd);
        }

        private Mesh Quad()
        {
            if (_quad != null)
                return _quad;

            _quad = new Mesh { name = "UOHD2D Fullscreen", hideFlags = HideFlags.HideAndDontSave };
            _quad.vertices = new[]
            {
                new Vector3(-1, -1, 0), new Vector3(-1, 1, 0),
                new Vector3(1, 1, 0), new Vector3(1, -1, 0)
            };
            _quad.uv = new[]
            {
                new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(1, 1), new Vector2(1, 0)
            };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return _quad;
        }
    }
}
