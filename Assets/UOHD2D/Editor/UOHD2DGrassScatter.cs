#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;

using UOHD2D.Game;

namespace UOHD2D
{
	/*
	 * Bakes grass tufts onto grass-textured terrain tiles of the active region.
	 * Tile positions and heights come straight from the imported terrain meshes
	 * (tex_3/4/5/6 groups); tiles carrying any static (buildings, props, roads)
	 * are skipped using the export bundle's statics.json. Output: a
	 * GrassFieldData asset + a "Grass" child with a GrassField renderer.
	 * Idempotent: re-running replaces the bake.
	 */
	public static class UOHD2DGrassScatter
	{
		private static readonly string[] GrassGroups = { "tex_3", "tex_4", "tex_5", "tex_6" };
		private const string DefaultBundle = "C:/UnityGames/UO/UO-Export/britain";
		private const string GrassMatPath = "Assets/UOHD2D/Replacements/grass_mat.mat";
		private const int TuftsPerTile = 7;

		[Serializable] private class StaticEntry { public int x, y, z, id, hue; }
		[Serializable] private class StaticList { public StaticEntry[] items; }

		[MenuItem("UO HD2D/Scatter Grass (Active Region)")]
		public static void Run()
		{
			var origin = UnityEngine.Object.FindFirstObjectByType<RegionOrigin>();

			if (origin == null)
			{
				Debug.LogError("[UOHD2D] No region in scene.");
				return;
			}

			var root = origin.transform;
			var terrain = root.Find("Terrain");

			if (terrain == null)
			{
				Debug.LogError("[UOHD2D] No Terrain under region root.");
				return;
			}

			// Occupancy from the bundle's statics.
			var bundle = Environment.GetEnvironmentVariable("UO_EXPORT_DIR");

			if (string.IsNullOrEmpty(bundle) || !File.Exists(Path.Combine(bundle, "statics.json")))
				bundle = DefaultBundle;

			var occupied = new HashSet<long>();
			var staticsPath = Path.Combine(bundle, "statics.json");

			if (File.Exists(staticsPath))
			{
				var statics = JsonUtility.FromJson<StaticList>("{\"items\":" + File.ReadAllText(staticsPath) + "}").items;

				foreach (var s in statics)
					occupied.Add(((long)s.x << 32) ^ (uint)s.y);
			}
			else
			{
				Debug.LogWarning("[UOHD2D] statics.json not found at " + bundle + " — scattering without occupancy check.");
			}

			var positions = new List<Vector3>();
			var yaws = new List<byte>();
			var scales = new List<byte>();

			foreach (var groupName in GrassGroups)
			{
				// Older imports name groups "land_tex_N" (CreateAsset renames the
				// texture object); newer imports use "tex_N".
				var group = terrain.Find(groupName);

				if (group == null)
					group = terrain.Find("land_" + groupName);

				if (group == null)
					continue;

				var mesh = group.GetComponent<MeshFilter>().sharedMesh;
				var verts = mesh.vertices;

				// Importer layout: 4 verts per tile — NW, NE, SW, SE.
				for (var v = 0; v < verts.Length; v += 4)
				{
					var nw = verts[v];
					var ne = verts[v + 1];
					var sw = verts[v + 2];
					var se = verts[v + 3];

					var tileX = Mathf.RoundToInt(nw.x);
					var tileY = Mathf.RoundToInt(-nw.z);

					if (occupied.Contains(((long)tileX << 32) ^ (uint)tileY))
						continue;

					var seed = (uint)(tileX * 73856093 ^ tileY * 19349663);

					for (var t = 0; t < TuftsPerTile; t++)
					{
						var jx = Hash01(ref seed);
						var jz = Hash01(ref seed);

						var top = Vector3.Lerp(nw, ne, jx);
						var bottom = Vector3.Lerp(sw, se, jx);
						var p = Vector3.Lerp(top, bottom, jz);

						positions.Add(p);
						yaws.Add((byte)(Hash01(ref seed) * 255f));
						scales.Add((byte)(Hash01(ref seed) * 200f + 55f));
					}
				}
			}

			if (positions.Count == 0)
			{
				Debug.LogWarning("[UOHD2D] No grass tiles found to scatter.");
				return;
			}

			// Persist the bake next to the region's other assets.
			var regionName = root.name.StartsWith("UO_") ? root.name.Substring(3) : root.name;
			var dataPath = "Assets/UOHD2D/Imported/" + regionName + "/grass_data.asset";
			var data = AssetDatabase.LoadAssetAtPath<GrassFieldData>(dataPath);

			if (data == null)
			{
				data = ScriptableObject.CreateInstance<GrassFieldData>();
				AssetDatabase.CreateAsset(data, dataPath);
			}

			data.Positions = positions.ToArray();
			data.Yaws = yaws.ToArray();
			data.Scales = scales.ToArray();
			EditorUtility.SetDirty(data);

			var grassGo = root.Find("Grass") != null ? root.Find("Grass").gameObject : new GameObject("Grass");
			grassGo.transform.SetParent(root, false);

			var field = grassGo.GetComponent<GrassField>();

			if (field == null)
				field = grassGo.AddComponent<GrassField>();

			field.Data = data;
			field.Material = GetOrCreateGrassMaterial();
			field.Rebuild();

			AssetDatabase.SaveAssets();
			UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
			Debug.Log("[UOHD2D] Scattered " + positions.Count + " grass tufts.");
		}

		private static Material GetOrCreateGrassMaterial()
		{
			var mat = AssetDatabase.LoadAssetAtPath<Material>(GrassMatPath);

			if (mat != null)
				return mat;

			if (!AssetDatabase.IsValidFolder("Assets/UOHD2D/Replacements"))
				AssetDatabase.CreateFolder("Assets/UOHD2D", "Replacements");

			// Prefer the Stylized Grass Shader package when installed.
			var shader = Shader.Find("Universal Render Pipeline/Nature/Stylized Grass");

			if (shader == null)
				shader = Shader.Find("UO HD2D/Grass");

			if (shader == null)
			{
				Debug.LogError("[UOHD2D] No grass shader found.");
				return null;
			}

			mat = new Material(shader) { enableInstancing = true };
			AssetDatabase.CreateAsset(mat, GrassMatPath);
			return mat;
		}

		private static float Hash01(ref uint seed)
		{
			seed = seed * 1664525u + 1013904223u;
			return (seed >> 8) * (1f / 16777216f);
		}
	}
}
#endif
