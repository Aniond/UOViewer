using System.Collections.Generic;
using UnityEngine;

namespace UOHD2D.Game
{
    /*
     * "Dress the 3D mannequin with 2D art": worn clothing is 2D RGBA art authored in the body's
     * UV space, alpha-composited over the naked skin atlas in classic paperdoll draw order into
     * a single RenderTexture that becomes the body's base map. The screen-space pixel pass then
     * reads the whole dressed body as one 2D sprite. Held items (weapons, shields) can't be
     * painted onto the body silhouette, so they stay 3D props on bones (CharacterRig.EquipProp).
     *
     * Layers are keyed by the UO layer byte, so Shirt + Tunic + Robe genuinely stack like the
     * real paperdoll. Recomposition batches per frame in LateUpdate - many SetLayer calls in one
     * frame cost a single composite.
     */
    public class ClothingCompositor : MonoBehaviour
    {
        [Tooltip("The body skin whose base map receives the composite.")]
        public SkinnedMeshRenderer Body;

        [Tooltip("Layer 0: the naked-body color atlas. Null leaves the compositor inert (e.g. flat-color stand-in bodies).")]
        public Texture BaseSkin;

        [Tooltip("Tint multiplied into the base skin only (character skin tone). Clothing layers are unaffected.")]
        public Color SkinTint = Color.white;

        private struct LayerEntry
        {
            public byte UoLayer;
            public Texture2D Tex;
            public Color Tint;
        }

        private readonly List<LayerEntry> _layers = new List<LayerEntry>();
        private RenderTexture _rt;
        private Material _blendMat;
        private Material _bodyMat;
        private bool _dirty;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int TintId = Shader.PropertyToID("_Tint");

        // Add or replace the clothing art on a UO layer. A null texture clears the layer.
        public void SetLayer(byte uoLayer, Texture2D tex, Color tint)
        {
            if (tex == null)
            {
                ClearLayer(uoLayer);
                return;
            }

            for (var i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].UoLayer == uoLayer)
                {
                    _layers[i] = new LayerEntry { UoLayer = uoLayer, Tex = tex, Tint = tint };
                    _dirty = true;
                    return;
                }
            }

            _layers.Add(new LayerEntry { UoLayer = uoLayer, Tex = tex, Tint = tint });
            _dirty = true;
        }

        public void ClearLayer(byte uoLayer)
        {
            for (var i = _layers.Count - 1; i >= 0; i--)
            {
                if (_layers[i].UoLayer == uoLayer)
                {
                    _layers.RemoveAt(i);
                    _dirty = true;
                }
            }
        }

        // Change the skin tone (recomposites on the next frame).
        public void SetSkinTint(Color tint)
        {
            if (SkinTint == tint)
                return;

            SkinTint = tint;
            _dirty = true;
        }

        // Drop every layer not in the server's worn set (mirrors the prop-slot clear).
        public void SyncLayers(HashSet<byte> wornUoLayers)
        {
            for (var i = _layers.Count - 1; i >= 0; i--)
            {
                if (wornUoLayers == null || !wornUoLayers.Contains(_layers[i].UoLayer))
                {
                    _layers.RemoveAt(i);
                    _dirty = true;
                }
            }
        }

        private void LateUpdate()
        {
            if (_dirty)
                Recompose();
        }

        private void Recompose()
        {
            _dirty = false;

            if (Body == null || BaseSkin == null)
                return;

            if (!EnsureTargets())
                return;

            // Innermost first: base skin, then each layer alpha-blended in paperdoll order.
            _layers.Sort((a, b) => UOLayer.DrawOrder(a.UoLayer).CompareTo(UOLayer.DrawOrder(b.UoLayer)));

            var prev = RenderTexture.active;

            // Base skin fills the target through the blend material so SkinTint applies to the
            // body only (the atlas is fully opaque, so this overwrites every texel).
            _blendMat.SetColor(TintId, SkinTint);
            Graphics.Blit(BaseSkin, _rt, _blendMat);

            foreach (var e in _layers)
            {
                _blendMat.SetColor(TintId, e.Tint);
                Graphics.Blit(e.Tex, _rt, _blendMat);
            }

            RenderTexture.active = prev;

            // Instance the body material once and point its base map at the composite.
            if (_bodyMat == null)
            {
                _bodyMat = Body.material;
                _bodyMat.SetTexture(BaseMapId, _rt);
            }
        }

        private bool EnsureTargets()
        {
            if (_blendMat == null)
            {
                var shader = Shader.Find("UO HD2D/ClothingBlend");
                if (shader == null)
                {
                    Debug.LogError("[UOHD2D] ClothingCompositor: shader 'UO HD2D/ClothingBlend' not found.");
                    return false;
                }

                _blendMat = new Material(shader);
            }

            if (_rt == null)
            {
                // Explicit sRGB so the blit chain round-trips colors exactly in a Linear project.
                _rt = new RenderTexture(BaseSkin.width, BaseSkin.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    name = name + "_wardrobe"
                };
                _rt.Create();
            }

            return true;
        }

        private void OnDestroy()
        {
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }

            if (_blendMat != null)
                Destroy(_blendMat);

            if (_bodyMat != null)
                Destroy(_bodyMat);
        }
    }
}
