#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UOHD2D
{
    /*
     * Bakes a feet-at-origin pivot into a generated character prefab. Tripo models are authored
     * with the origin at the body CENTER, which breaks two things: the root sits on the tile so
     * the lower half is underground, and the humanoid Avatar computes humanScale (hip height
     * above root) as ~0, which makes retargeted clips pin the hips to the floor (half-buried,
     * squatting look). Shifting the mesh up INSIDE the prefab fixes both at the source.
     *
     * Run BEFORE building the humanoid avatar. Idempotent: measures the current bounds each run
     * and only applies the residual shift, so re-running converges to no-op.
     *
     * Menu: UO HD2D/Rig/Bake Feet Origin (base_human).
     */
    public static class UOHD2DFeetBake
    {
        private const string PrefabPath = "Assets/UOHD2D/Generated/Rig/base_human/base_human.prefab";

        [MenuItem("UO HD2D/Rig/Bake Feet Origin (base_human)")]
        public static void Bake()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);

            try
            {
                // Prefab contents load at identity, so world bounds ARE root-local bounds.
                var renderers = root.GetComponentsInChildren<Renderer>();

                if (renderers.Length == 0)
                {
                    Debug.LogError("[UOHD2D] Feet bake: no renderers in " + PrefabPath);
                    return;
                }

                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                var lift = -bounds.min.y; // raise so the soles sit at y = 0

                if (Mathf.Abs(lift) < 0.001f)
                {
                    Debug.Log("[UOHD2D] Feet bake: already at feet origin (residual " + lift.ToString("F4") + "), nothing to do.");
                    return;
                }

                foreach (Transform child in root.transform)
                    child.localPosition += new Vector3(0f, lift, 0f);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[UOHD2D] Feet bake: lifted mesh by " + lift.ToString("F4") + " so feet sit at the prefab origin. Rebuild the humanoid avatar next.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
