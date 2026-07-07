#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UOHD2D
{
	/*
	 * Imports an HD2DExport bundle (see ServUO/Tools/HD2DExport) into the scene:
	 *   - 3D terrain mesh built from the UO heightmap, textured with client texmaps
	 *   - all statics as upright sprite quads, atlas-packed, one draw call per atlas
	 *   - generated meshes/materials/textures persisted under Assets/UOHD2D/Imported
	 *
	 * Scale convention: 1 UO tile = 1 world unit, 44 art pixels = 1 world unit,
	 * 1 UO z-step = 4/44 world units (matches the classic client's 4px per z).
	 */
	public static class UOHD2DImporter
	{
		private const float PPU = 44f;
		private const float ZScale = 4f / 44f;

		[Serializable] private class Manifest { public int map, x0, y0, width, height, terrainWidth, terrainHeight, staticCount, artCount; }
		[Serializable] private class LandInfo { public int id; public string file; }
		[Serializable] private class LandInfoList { public LandInfo[] items; }
		[Serializable] private class StaticEntry { public int x, y, z, id, hue; }
		[Serializable] private class StaticList { public StaticEntry[] items; }
		[Serializable] private class ArtInfo { public string key; public int id, hue, w, h; public bool flat; public string name; }
		[Serializable] private class ArtInfoList { public ArtInfo[] items; }

		[MenuItem("UO HD2D/Import Export Folder...")]
		public static void ImportMenu()
		{
			var dir = EditorUtility.OpenFolderPanel("Select HD2DExport output folder", "", "");

			if (string.IsNullOrEmpty(dir))
				return;

			try
			{
				ImportFolder(dir);
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		[MenuItem("UO HD2D/Create Camera Rig")]
		public static void CreateCameraRig()
		{
			var rig = new GameObject("UO Camera Rig");
			rig.AddComponent<UOHD2DCameraRig>();
			Selection.activeGameObject = rig;
		}

		public static void ImportFolder(string dir)
		{
			var manifestPath = Path.Combine(dir, "manifest.json");

			if (!File.Exists(manifestPath))
			{
				EditorUtility.DisplayDialog("UO HD2D", "manifest.json not found in:\n" + dir, "OK");
				return;
			}

			var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
			var landInfos = ListFromJson<LandInfoList>(Path.Combine(dir, "landinfo.json")).items;
			var statics = ListFromJson<StaticList>(Path.Combine(dir, "statics.json")).items;
			var artInfos = ListFromJson<ArtInfoList>(Path.Combine(dir, "artinfo.json")).items;

			var regionName = string.Format("map{0}_{1}_{2}", manifest.map, manifest.x0, manifest.y0);
			var assetDir = "Assets/UOHD2D/Imported/" + regionName;
			EnsureFolder(assetDir);

			// Reimporting replaces the existing region in place instead of duplicating it.
			var existing = GameObject.Find("UO_" + regionName);

			if (existing != null)
				UnityEngine.Object.DestroyImmediate(existing);

			var root = new GameObject("UO_" + regionName);

			var origin = root.AddComponent<UOHD2D.Game.RegionOrigin>();
			origin.Map = manifest.map;
			origin.X0 = manifest.x0;
			origin.Y0 = manifest.y0;

			EditorUtility.DisplayProgressBar("UO HD2D", "Building terrain...", 0.1f);
			BuildTerrain(dir, manifest, landInfos, root.transform, assetDir);

			EditorUtility.DisplayProgressBar("UO HD2D", "Building statics...", 0.5f);
			BuildStatics(dir, statics, artInfos, root.transform, assetDir);

			AssetDatabase.SaveAssets();

			if (UnityEngine.Object.FindObjectOfType<UOHD2DCameraRig>() == null)
			{
				var rig = new GameObject("UO Camera Rig");
				var comp = rig.AddComponent<UOHD2DCameraRig>();
				comp.transform.position = new Vector3(manifest.width * 0.5f, 0f, -manifest.height * 0.5f);
			}

			Selection.activeGameObject = root;
			Debug.Log(string.Format("[UOHD2D] Imported {0}: {1} statics, {2}x{3} tiles.", regionName, statics.Length, manifest.width, manifest.height));
		}

		private static void BuildTerrain(string dir, Manifest m, LandInfo[] landInfos, Transform root, string assetDir)
		{
			var tw = m.terrainWidth;
			var th = m.terrainHeight;

			var ids = new ushort[tw * th];
			var zs = new sbyte[tw * th];

			using (var br = new BinaryReader(File.OpenRead(Path.Combine(dir, "terrain.bin"))))
			{
				for (var i = 0; i < tw * th; i++)
				{
					ids[i] = br.ReadUInt16();
					zs[i] = br.ReadSByte();
				}
			}

			var fileByLand = new Dictionary<int, string>();

			foreach (var li in landInfos)
				fileByLand[li.id] = li.file;

			// Group tiles by land texture so each texture becomes one mesh.
			var groups = new Dictionary<string, List<int>>();

			for (var y = 0; y < m.height; y++)
			{
				for (var x = 0; x < m.width; x++)
				{
					string file;

					if (!fileByLand.TryGetValue(ids[y * tw + x], out file))
						continue;

					List<int> list;

					if (!groups.TryGetValue(file, out list))
						groups[file] = list = new List<int>();

					list.Add(y * tw + x);
				}
			}

			var terrainGo = new GameObject("Terrain");
			terrainGo.transform.SetParent(root, false);

			var terrainShader = Shader.Find("Universal Render Pipeline/Lit");
			var isUrp = terrainShader != null;

			if (!isUrp)
				terrainShader = Shader.Find("Standard");

			foreach (var kv in groups)
			{
				var tex = LoadTexture(Path.Combine(dir, kv.Key.Replace('/', Path.DirectorySeparatorChar)));

				if (tex == null)
					continue;

				tex.name = Path.GetFileNameWithoutExtension(kv.Key);
				tex.filterMode = FilterMode.Point;
				AssetDatabase.CreateAsset(tex, assetDir + "/land_" + tex.name + ".asset");

				var mat = new Material(terrainShader) { name = "land_" + tex.name };

				if (isUrp)
				{
					mat.SetTexture("_BaseMap", tex);
					mat.SetFloat("_Smoothness", 0f);
				}
				else
				{
					mat.mainTexture = tex;
					mat.SetFloat("_Glossiness", 0f);
				}

				AssetDatabase.CreateAsset(mat, assetDir + "/land_" + tex.name + ".mat");

				var verts = new List<Vector3>();
				var uvs = new List<Vector2>();
				var tris = new List<int>();

				foreach (var idx in kv.Value)
				{
					var x = idx % tw;
					var y = idx / tw;

					var z00 = zs[y * tw + x] * ZScale;
					var z10 = zs[y * tw + (x + 1)] * ZScale;
					var z01 = zs[(y + 1) * tw + x] * ZScale;
					var z11 = zs[(y + 1) * tw + (x + 1)] * ZScale;

					var b = verts.Count;

					verts.Add(new Vector3(x, z00, -y));           // NW
					verts.Add(new Vector3(x + 1, z10, -y));       // NE
					verts.Add(new Vector3(x, z01, -(y + 1)));     // SW
					verts.Add(new Vector3(x + 1, z11, -(y + 1))); // SE

					uvs.Add(new Vector2(0, 1));
					uvs.Add(new Vector2(1, 1));
					uvs.Add(new Vector2(0, 0));
					uvs.Add(new Vector2(1, 0));

					tris.Add(b); tris.Add(b + 1); tris.Add(b + 3);
					tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
				}

				var mesh = new Mesh { name = "terrain_" + tex.name, indexFormat = IndexFormat.UInt32 };
				mesh.SetVertices(verts);
				mesh.SetUVs(0, uvs);
				mesh.SetTriangles(tris, 0);
				mesh.RecalculateNormals();
				mesh.RecalculateBounds();
				AssetDatabase.CreateAsset(mesh, assetDir + "/terrain_" + tex.name + ".asset");

				var go = new GameObject(tex.name);
				go.transform.SetParent(terrainGo.transform, false);
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				go.AddComponent<MeshRenderer>().sharedMaterial = mat;
			}
		}

		private static void BuildStatics(string dir, StaticEntry[] statics, ArtInfo[] artInfos, Transform root, string assetDir)
		{
			// Load every unique art sprite, then pack them into one big atlas.
			var infoByKey = new Dictionary<string, ArtInfo>();
			var texByKey = new Dictionary<string, Texture2D>();
			var keys = new List<string>();

			for (var i = 0; i < artInfos.Length; i++)
			{
				var info = artInfos[i];

				if (infoByKey.ContainsKey(info.key))
					continue;

				var tex = LoadTexture(Path.Combine(dir, "art", info.key + ".png"));

				if (tex == null)
					continue;

				infoByKey[info.key] = info;
				texByKey[info.key] = tex;
				keys.Add(info.key);

				if (i % 400 == 0)
					EditorUtility.DisplayProgressBar("UO HD2D", "Loading art " + i + "/" + artInfos.Length, 0.5f + 0.2f * i / Mathf.Max(1, artInfos.Length));
			}

			var textures = new Texture2D[keys.Count];

			for (var i = 0; i < keys.Count; i++)
				textures[i] = texByKey[keys[i]];

			EditorUtility.DisplayProgressBar("UO HD2D", "Packing atlas...", 0.75f);

			var atlas = new Texture2D(8192, 8192, TextureFormat.RGBA32, false);
			var rects = atlas.PackTextures(textures, 2, 8192, false);

			if (rects == null)
			{
				Debug.LogError("[UOHD2D] Atlas packing failed.");
				return;
			}

			atlas.name = "statics_atlas";
			atlas.filterMode = FilterMode.Point;
			AssetDatabase.CreateAsset(atlas, assetDir + "/statics_atlas.asset");

			var rectByKey = new Dictionary<string, Rect>();

			for (var i = 0; i < keys.Count; i++)
				rectByKey[keys[i]] = rects[i];

			foreach (var tex in textures)
				UnityEngine.Object.DestroyImmediate(tex);

			// Statics must receive scene lighting for the HD-2D look. Prefer the
			// custom sprite-lit graph when it exists, else URP/Lit as alpha-clipped
			// cutout with specular fully disabled so sprites shade flat like Octopath.
			Material mat;
			var spriteLit = Shader.Find("Shader Graphs/UOHD2D_SpriteLit");
			var urpLit = Shader.Find("Universal Render Pipeline/Lit");

			if (spriteLit != null)
			{
				mat = new Material(spriteLit);
				mat.SetTexture("_BaseMap", atlas);
			}
			else if (urpLit != null)
			{
				mat = new Material(urpLit);
				mat.SetTexture("_BaseMap", atlas);
				mat.SetFloat("_Smoothness", 0f);
				mat.SetFloat("_Metallic", 0f);
				mat.SetFloat("_AlphaClip", 1f);
				mat.SetFloat("_Cutoff", 0.5f);
				mat.SetFloat("_Cull", (float)CullMode.Off);
				mat.SetFloat("_SpecularHighlights", 0f);
				mat.SetFloat("_EnvironmentReflections", 0f);
				mat.EnableKeyword("_ALPHATEST_ON");
				mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
				mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
				mat.renderQueue = (int)RenderQueue.AlphaTest;
			}
			else
			{
				mat = new Material(Shader.Find("Unlit/Transparent Cutout"));
				mat.mainTexture = atlas;
			}

			mat.name = "statics_mat";
			AssetDatabase.CreateAsset(mat, assetDir + "/statics_mat.mat");

			EditorUtility.DisplayProgressBar("UO HD2D", "Building static quads...", 0.85f);

			var verts = new List<Vector3>();
			var uvs = new List<Vector2>();
			var normals = new List<Vector3>();
			var tris = new List<int>();
			var skipped = 0;

			// Upright sprites get a normal tilted halfway between "facing camera"
			// and "up" so they shade like scenery instead of going black when the
			// sun grazes the billboard plane (the Octopath billboard trick).
			var uprightNormal = new Vector3(0f, 0.7071f, -0.7071f);

			var waterSkipped = 0;

			foreach (var s in statics)
			{
				var key = "s_" + s.id + "_" + s.hue;
				ArtInfo info;
				Rect r;

				if (!infoByKey.TryGetValue(key, out info) || !rectByKey.TryGetValue(key, out r))
				{
					skipped++;
					continue;
				}

				// Water sprites are not baked: a real water surface (Stylized Water 3)
				// is placed at sea level instead and reads far better in HD-2D.
				if (info.name == "water")
				{
					waterSkipped++;
					continue;
				}

				var b = verts.Count;

				if (info.flat)
				{
					// Wet tiles (water) lie flat on the tile square like land. The art is a
					// 44x44 diamond, so the tile's corners map to the diamond's vertices:
					// the quad samples the inscribed diamond of the sprite rect.
					var yFlat = s.z * ZScale + 0.02f; // sit just above the terrain

					verts.Add(new Vector3(s.x, yFlat, -s.y));
					verts.Add(new Vector3(s.x + 1, yFlat, -s.y));
					verts.Add(new Vector3(s.x, yFlat, -(s.y + 1)));
					verts.Add(new Vector3(s.x + 1, yFlat, -(s.y + 1)));

					uvs.Add(new Vector2(r.xMin + 0.5f * r.width, r.yMax));  // (x,   y)   -> diamond top
					uvs.Add(new Vector2(r.xMax, r.yMin + 0.5f * r.height)); // (x+1, y)   -> diamond right
					uvs.Add(new Vector2(r.xMin, r.yMin + 0.5f * r.height)); // (x,   y+1) -> diamond left
					uvs.Add(new Vector2(r.xMin + 0.5f * r.width, r.yMin));  // (x+1, y+1) -> diamond bottom

					normals.Add(Vector3.up);
					normals.Add(Vector3.up);
					normals.Add(Vector3.up);
					normals.Add(Vector3.up);

					tris.Add(b); tris.Add(b + 1); tris.Add(b + 3);
					tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
				}
				else
				{
					var halfW = info.w / PPU * 0.5f;
					var height = info.h / PPU;
					var baseY = s.z * ZScale;
					var cx = s.x + 0.5f;
					var cz = -(s.y + 0.5f);

					verts.Add(new Vector3(cx - halfW, baseY, cz));          // bottom-left
					verts.Add(new Vector3(cx + halfW, baseY, cz));          // bottom-right
					verts.Add(new Vector3(cx - halfW, baseY + height, cz)); // top-left
					verts.Add(new Vector3(cx + halfW, baseY + height, cz)); // top-right

					uvs.Add(new Vector2(r.xMin, r.yMin));
					uvs.Add(new Vector2(r.xMax, r.yMin));
					uvs.Add(new Vector2(r.xMin, r.yMax));
					uvs.Add(new Vector2(r.xMax, r.yMax));

					normals.Add(uprightNormal);
					normals.Add(uprightNormal);
					normals.Add(uprightNormal);
					normals.Add(uprightNormal);

					tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
					tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
				}
			}

			if (skipped > 0)
				Debug.LogWarning("[UOHD2D] Skipped " + skipped + " statics with missing art.");

			if (waterSkipped > 0)
				Debug.Log("[UOHD2D] Skipped " + waterSkipped + " water sprites (replaced by water surface).");

			var mesh = new Mesh { name = "statics", indexFormat = IndexFormat.UInt32 };
			mesh.SetVertices(verts);
			mesh.SetUVs(0, uvs);
			mesh.SetNormals(normals);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			AssetDatabase.CreateAsset(mesh, assetDir + "/statics.asset");

			var go = new GameObject("Statics");
			go.transform.SetParent(root, false);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;

			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = mat;
			renderer.shadowCastingMode = ShadowCastingMode.TwoSided; // thin quads must cast from both faces
		}

		private static T ListFromJson<T>(string path)
		{
			return JsonUtility.FromJson<T>("{\"items\":" + File.ReadAllText(path) + "}");
		}

		private static Texture2D LoadTexture(string path)
		{
			if (!File.Exists(path))
				return null;

			var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

			if (!tex.LoadImage(File.ReadAllBytes(path)))
			{
				UnityEngine.Object.DestroyImmediate(tex);
				return null;
			}

			return tex;
		}

		private static void EnsureFolder(string assetPath)
		{
			var parts = assetPath.Split('/');
			var current = parts[0];

			for (var i = 1; i < parts.Length; i++)
			{
				var next = current + "/" + parts[i];

				if (!AssetDatabase.IsValidFolder(next))
					AssetDatabase.CreateFolder(current, parts[i]);

				current = next;
			}
		}
	}
}
#endif
