#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UOHD2D
{
    /*
     * One-click setup for the character-only pixelation pass:
     *  - ensures the "Character" layer exists,
     *  - creates the composite material from the UO HD2D/PixelComposite shader,
     *  - adds a UOHD2DPixelizer to the gameplay camera ("UO Camera") and wires the material.
     * Idempotent. Menu: UO HD2D/Setup Character Pixelation.
     */
    public static class UOHD2DPixelSetup
    {
        private const string MatPath = "Assets/UOHD2D/Shaders/UOHD2D_PixelComposite.mat";

        [MenuItem("UO HD2D/Setup Character Pixelation")]
        public static void Run()
        {
            EnsureCharacterLayer();
            var mat = EnsureCompositeMaterial();
            AttachToCamera(mat);
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[UOHD2D] Character pixelation set up. Tune PixelScale on the 'UO Camera' UOHD2DPixelizer.");
        }

        private static void EnsureCharacterLayer()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");

            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == "Character")
                    return;

            for (int i = 8; i < layers.arraySize; i++) // 0-7 reserved by Unity
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue))
                {
                    el.stringValue = "Character";
                    tagManager.ApplyModifiedProperties();
                    Debug.Log("[UOHD2D] Added 'Character' layer at slot " + i + ".");
                    return;
                }
            }

            Debug.LogWarning("[UOHD2D] No free layer slot for 'Character' - add it manually in Tags & Layers.");
        }

        private static Material EnsureCompositeMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            var shader = Shader.Find("UO HD2D/PixelComposite");

            if (shader == null)
            {
                Debug.LogError("[UOHD2D] PixelComposite shader not found - is UOHD2D_PixelComposite.shader present?");
                return mat;
            }

            if (mat == null)
            {
                mat = new Material(shader) { name = "UOHD2D_PixelComposite" };
                AssetDatabase.CreateAsset(mat, MatPath);
                AssetDatabase.SaveAssets();
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }

            return mat;
        }

        private static void AttachToCamera(Material mat)
        {
            var camGo = GameObject.Find("UO Camera Rig/UO Camera");
            if (camGo == null)
            {
                // Fall back to a broad search for the gameplay camera.
                foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (c.name == "UO Camera") { camGo = c.gameObject; break; }
            }

            if (camGo == null)
            {
                Debug.LogWarning("[UOHD2D] 'UO Camera' not found in the open scene - open the gameplay scene, then re-run.");
                return;
            }

            var pix = camGo.GetComponent<UOHD2DPixelizer>();
            if (pix == null)
                pix = camGo.AddComponent<UOHD2DPixelizer>();

            pix.CompositeMaterial = mat;
            EditorUtility.SetDirty(pix);
            Debug.Log("[UOHD2D] UOHD2DPixelizer attached to '" + camGo.name + "'.");
        }
    }
}
#endif
