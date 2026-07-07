#if UNITY_EDITOR
using System;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace UOHD2D
{
	/*
	 * Wraps a generated GLB (Assets/UOHD2D/Generated/Models/{slug}/{slug}.glb +
	 * model.json sidecar) into a placement-ready prefab: origin at the bottom
	 * center of its bounds, scaled per the sidecar's fit rule, shadows on, and
	 * AI-glossy materials clamped so props sit in the same light response as the
	 * sprite world. Idempotent: re-running updates the same wrapper prefab in
	 * place, so scene references and GUIDs survive regeneration.
	 */
	public static class UOHD2DModelIntake
	{
		private const string ModelsRoot = "Assets/UOHD2D/Generated/Models";
		private const float MaxSmoothness = 0.4f;

		[Serializable]
		private class ModelSidecar
		{
			public string id;
			public int[] artIds;
			public string fit = "explicit";
			public float scale = 1f;
			public float rotYDeg;
			public float tilesX = 1f;
			public float tilesZ = 1f;
			public float artHeightWorld; // written by workbench finalize when fit == "artHeight"
		}

		[MenuItem("UO HD2D/Intake/Process All Models")]
		public static void ProcessAll()
		{
			if (!AssetDatabase.IsValidFolder(ModelsRoot))
			{
				Debug.Log("[UOHD2D] No generated models folder yet.");
				return;
			}

			foreach (var dir in Directory.GetDirectories(ModelsRoot))
				Process(Path.GetFileName(dir));
		}

		public static void Process(string slug)
		{
			var dir = ModelsRoot + "/" + slug;
			var glbPath = dir + "/" + slug + ".glb";
			var sidecarPath = dir + "/model.json";

			var source = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);

			if (source == null)
			{
				Debug.LogWarning("[UOHD2D] Model intake: no GLB at " + glbPath);
				return;
			}

			var sidecar = new ModelSidecar { id = slug };
			var sidecarAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(sidecarPath);

			if (sidecarAsset != null)
				sidecar = JsonUtility.FromJson<ModelSidecar>(sidecarAsset.text);

			// Measure the source model.
			var temp = (GameObject)UnityEngine.Object.Instantiate(source);
			var bounds = ComputeBounds(temp);
			UnityEngine.Object.DestroyImmediate(temp);

			var scale = ComputeScale(sidecar, bounds);

			// Build the wrapper: origin at bottom-center so props rest on their tile.
			var wrapper = new GameObject(slug);
			var child = (GameObject)PrefabUtility.InstantiatePrefab(source);
			child.transform.SetParent(wrapper.transform, false);
			child.transform.localScale = Vector3.one * scale;
			child.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
			wrapper.transform.rotation = Quaternion.Euler(0f, sidecar.rotYDeg, 0f);

			foreach (var renderer in wrapper.GetComponentsInChildren<Renderer>())
			{
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

				foreach (var mat in renderer.sharedMaterials)
				{
					if (mat == null || !mat.HasProperty("_Smoothness"))
						continue;

					if (mat.GetFloat("_Smoothness") > MaxSmoothness)
						mat.SetFloat("_Smoothness", MaxSmoothness);
				}
			}

			var prefabPath = dir + "/" + slug + "_p.prefab";
			PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
			UnityEngine.Object.DestroyImmediate(wrapper);

			Debug.Log("[UOHD2D] Model intake: " + prefabPath + " (fit=" + sidecar.fit + ", scale=" + scale.ToString("F3") + ", bounds=" + bounds.size + ")");
		}

		private static float ComputeScale(ModelSidecar sidecar, Bounds bounds)
		{
			switch (sidecar.fit)
			{
				case "artHeight":
					return sidecar.artHeightWorld > 0f && bounds.size.y > 0.001f
						? sidecar.artHeightWorld / bounds.size.y * sidecar.scale
						: sidecar.scale;

				case "footprint":
					var current = Mathf.Max(bounds.size.x, 0.001f);
					var currentZ = Mathf.Max(bounds.size.z, 0.001f);
					return Mathf.Min(sidecar.tilesX / current, sidecar.tilesZ / currentZ) * sidecar.scale;

				default:
					return sidecar.scale;
			}
		}

		private static Bounds ComputeBounds(GameObject go)
		{
			var renderers = go.GetComponentsInChildren<Renderer>();

			if (renderers.Length == 0)
				return new Bounds(go.transform.position, Vector3.one);

			var bounds = renderers[0].bounds;

			foreach (var r in renderers)
				bounds.Encapsulate(r.bounds);

			return bounds;
		}
	}
}
#endif
