using UnityEngine;

namespace UOHD2D.Game
{
    // Builds a live character GameObject: the real rigged model when a profile provides one,
    // else a primitive stand-in so the spawn/follow/pixel pipeline runs before any model is
    // assigned. Either way the result carries a CharacterRig and sits on the given layer, with
    // its feet at the local origin so it drops straight onto a tile position.
    public static class CharacterFactory
    {
        // Creates the visual and returns its CharacterRig. Parent under the caller's mobile
        // GameObject (Player / RemoteMobile), whose transform is the tile position.
        public static CharacterRig Create(CharacterProfile profile, int layer)
        {
            if (profile != null && profile.ResolvedPrefab != null)
                return CreateFromPrefab(profile, layer);

            return CreateStandIn(layer);
        }

        private static CharacterRig CreateFromPrefab(CharacterProfile profile, int layer)
        {
            var prefab = profile.ResolvedPrefab;
            var go = Object.Instantiate(prefab);
            go.name = prefab.name;

            NormalizeFeetAndScale(go, profile.ResolvedHeight, profile.ResolvedGroundOffset);
            SetLayerRecursive(go, layer);

            var rig = go.GetComponent<CharacterRig>();
            if (rig == null)
                rig = go.AddComponent<CharacterRig>();

            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
                rig.Body = smr;

            // Per-character skin/face texture override (individuals vary by texture on a shared body).
            if (profile.SkinTexture != null && smr != null && smr.sharedMaterial != null)
            {
                var mat = new Material(smr.sharedMaterial);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", profile.SkinTexture);
                else if (mat.HasProperty("baseColorTexture")) mat.SetTexture("baseColorTexture", profile.SkinTexture);
                smr.sharedMaterial = mat;
            }

            var controller = profile.ResolvedController;
            if (controller != null)
            {
                var animator = go.GetComponentInChildren<Animator>();
                if (animator == null)
                    animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                // GLB models import generic; apply the humanoid avatar so clips retarget.
                if (profile.ResolvedAvatar != null)
                    animator.avatar = profile.ResolvedAvatar;
                // Tile/network logic drives position; clip travel is extracted as root motion
                // and must be discarded, else the mesh strides away from its own anchor.
                animator.applyRootMotion = false;
            }

            rig.Bind();

            // Equip default gear (fitted to the template, so it fits automatically).
            if (profile.DefaultGear != null)
                foreach (var item in profile.DefaultGear)
                    if (item != null)
                        rig.EquipItem(item);

            return rig;
        }

        // Primitive humanoid stand-in: a body capsule plus bone-named empties so armor equip is
        // testable before a real rig exists. It has no SkinnedMeshRenderer, so CharacterRig.Body
        // stays null and armor equip is a no-op - but spawn, follow and facing all work.
        private static CharacterRig CreateStandIn(int layer)
        {
            var go = new GameObject("Character(StandIn)");

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Body";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = new Vector3(0.4f, 0.5f, 0.4f);
            visual.transform.localPosition = new Vector3(0f, 0.5f, 0f); // capsule is pivot-centered; base on the feet
            Object.Destroy(visual.GetComponent<Collider>());

            // A little nose so facing is visible without an animator.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing";
            nose.transform.SetParent(go.transform, false);
            nose.transform.localScale = new Vector3(0.12f, 0.12f, 0.2f);
            nose.transform.localPosition = new Vector3(0f, 0.85f, 0.22f);
            Object.Destroy(nose.GetComponent<Collider>());

            SetLayerRecursive(go, layer);

            var rig = go.AddComponent<CharacterRig>();
            rig.Bind();
            return rig;
        }

        // Character prefabs have their feet baked at the origin (UOHD2DFeetBake), so scaling
        // about the root keeps the soles on the tile - no runtime lift math. GroundOffset is a
        // small per-model world-space nudge, converted into the root's scaled local space.
        private static void NormalizeFeetAndScale(GameObject go, float targetHeight, float groundOffset)
        {
            var priorPos = go.transform.position;
            var priorRot = go.transform.rotation;
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            var bounds = ComputeBounds(go);
            if (bounds.size.y > 0.0001f)
            {
                var scale = targetHeight > 0f ? targetHeight / bounds.size.y : 1f;
                go.transform.localScale = go.transform.localScale * scale;

                if (Mathf.Abs(groundOffset) > 0.0001f)
                {
                    var rootScaleY = Mathf.Abs(go.transform.lossyScale.y);
                    var localNudge = rootScaleY > 0.0001f ? groundOffset / rootScaleY : groundOffset;

                    foreach (Transform child in go.transform)
                        child.localPosition += new Vector3(0f, localNudge, 0f);
                }
            }

            go.transform.position = priorPos;
            go.transform.rotation = priorRot;
        }

        private static Bounds ComputeBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;

            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}
