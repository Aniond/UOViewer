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
		[Serializable] private class LandTextureEntry { public string file; public string texture; public string normal; public float tile = 1f; }
		[Serializable] private class LandTextureMap { public LandTextureEntry[] entries; }

		private const string LandTexturesPath = "Assets/UOHD2D/Replacements/landtextures.json";
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

			// Optional upgraded painterly textures replacing raw UO texmaps.
			var upgrades = new Dictionary<string, LandTextureEntry>();

			if (File.Exists(LandTexturesPath))
			{
				var map = JsonUtility.FromJson<LandTextureMap>(File.ReadAllText(LandTexturesPath));

				if (map != null && map.entries != null)
					foreach (var e in map.entries)
						upgrades[e.file] = e;
			}

			foreach (var kv in groups)
			{
				LandTextureEntry upgrade;
				upgrades.TryGetValue(kv.Key, out upgrade);

				Texture2D baseTex = null;
				Texture2D normalTex = null;
				var uvTile = 1f;
				var texName = Path.GetFileNameWithoutExtension(kv.Key);

				if (upgrade != null)
				{
					baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(upgrade.texture);

					if (baseTex == null)
						Debug.LogWarning("[UOHD2D] Upgrade texture missing: " + upgrade.texture + " (falling back to texmap)");
					else if (!string.IsNullOrEmpty(upgrade.normal))
						normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(upgrade.normal);

					uvTile = upgrade.tile > 0f ? upgrade.tile : 1f;
				}

				if (baseTex == null)
				{
					var tex = LoadTexture(Path.Combine(dir, kv.Key.Replace('/', Path.DirectorySeparatorChar)));

					if (tex == null)
						continue;

					tex.name = texName;
					tex.filterMode = FilterMode.Point;
					AssetDatabase.CreateAsset(tex, assetDir + "/land_" + tex.name + ".asset");
					baseTex = tex;
				}

				var mat = new Material(terrainShader) { name = "land_" + texName };

				if (isUrp)
				{
					mat.SetTexture("_BaseMap", baseTex);
					mat.SetFloat("_Smoothness", 0f);

					if (normalTex != null)
					{
						mat.SetTexture("_BumpMap", normalTex);
						mat.EnableKeyword("_NORMALMAP");
					}
				}
				else
				{
					mat.mainTexture = baseTex;
					mat.SetFloat("_Glossiness", 0f);
				}

				AssetDatabase.CreateAsset(mat, assetDir + "/land_" + texName + ".mat");

				var verts = new List<Vector3>();
				var uvs = new List<Vector2>();
				var tris = new List<int>();

				var worldUV = upgrade != null && baseTex != null;

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

					if (worldUV)
					{
						// Continuous world-space UVs: painterly tileables flow
						// seamlessly across tiles instead of repeating per tile.
						uvs.Add(new Vector2(x * uvTile, -y * uvTile));
						uvs.Add(new Vector2((x + 1) * uvTile, -y * uvTile));
						uvs.Add(new Vector2(x * uvTile, -(y + 1) * uvTile));
						uvs.Add(new Vector2((x + 1) * uvTile, -(y + 1) * uvTile));
					}
					else
					{
						uvs.Add(new Vector2(0, 1));
						uvs.Add(new Vector2(1, 1));
						uvs.Add(new Vector2(0, 0));
						uvs.Add(new Vector2(1, 0));
					}

					tris.Add(b); tris.Add(b + 1); tris.Add(b + 3);
					tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
				}

				var mesh = new Mesh { name = "terrain_" + texName, indexFormat = IndexFormat.UInt32 };
				mesh.SetVertices(verts);
				mesh.SetUVs(0, uvs);
				mesh.SetTriangles(tris, 0);
				mesh.RecalculateNormals();
				mesh.RecalculateBounds();
				AssetDatabase.CreateAsset(mesh, assetDir + "/terrain_" + texName + ".asset");

				var go = new GameObject(texName);
				go.transform.SetParent(terrainGo.transform, false);
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				go.AddComponent<MeshRenderer>().sharedMaterial = mat;
			}
		}

		// While the kit is being proven one building at a time, only statics
		// inside this tile rect convert to 3D kit pieces; everything else stays
		// sprites. Widen (or disable) as families pass the capture verify loop.
		private static readonly bool KitBoundsEnabled = true;
		private static readonly RectInt KitBounds = new RectInt(60, 100, 26, 22); // test house x67-80, y107-116 + margin

		private class KitInstance
		{
			public UOHD2DKitLibrary.KitEntry Entry;
			public int X, Y, Z, Hue;
		}

		private static void BuildStatics(string dir, StaticEntry[] statics, ArtInfo[] artInfos, Transform root, string assetDir)
		{
			var kitLibrary = UOHD2DKitLibrary.Load(dir);
			var kitRecords = new List<KitInstance>();
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

				// Kit-mapped statics become real 3D geometry instead of sprite quads.
				UOHD2DKitLibrary.KitEntry kitEntry;

				if (kitLibrary.TryGetEntry(s.id, out kitEntry) && (!KitBoundsEnabled || KitBounds.Contains(new Vector2Int(s.x, s.y))))
				{
					kitRecords.Add(new KitInstance { Entry = kitEntry, X = s.x, Y = s.y, Z = s.z, Hue = s.hue });
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

			BuildKit(kitRecords, root, assetDir);
		}

		/*
		 * Turns recorded kit instances into combined per-family meshes under
		 * Kit3D/. Buildings are clustered (8-neighbor flood fill) so stories
		 * can be bucketed per building; cells where stacked gable walls sit
		 * under a roof merge into a single full-height slab.
		 */
		private static void BuildKit(List<KitInstance> records, Transform root, string assetDir)
		{
			if (records.Count == 0)
				return;

			var byCell = new Dictionary<long, List<KitInstance>>();

			foreach (var r in records)
			{
				var key = CellKey(r.X, r.Y);
				List<KitInstance> list;

				if (!byCell.TryGetValue(key, out list))
					byCell[key] = list = new List<KitInstance>();

				list.Add(r);
			}

			// Cluster cells into buildings.
			var clusterOf = new Dictionary<long, int>();
			var nextCluster = 0;

			foreach (var start in byCell.Keys)
			{
				if (clusterOf.ContainsKey(start))
					continue;

				var queue = new Queue<long>();
				queue.Enqueue(start);
				clusterOf[start] = nextCluster;

				while (queue.Count > 0)
				{
					var cell = queue.Dequeue();
					var cx = (int)(cell >> 32);
					var cy = (int)(uint)(cell & 0xffffffff);

					for (var dx = -1; dx <= 1; dx++)
						for (var dy = -1; dy <= 1; dy++)
						{
							var n = CellKey(cx + dx, cy + dy);

							if (byCell.ContainsKey(n) && !clusterOf.ContainsKey(n))
							{
								clusterOf[n] = nextCluster;
								queue.Enqueue(n);
							}
						}
				}

				nextCluster++;
			}

			// Per-cluster base z from wall-type pieces.
			var clusterBase = new Dictionary<int, int>();

			foreach (var r in records)
			{
				if (IsRoofPiece(r.Entry.piece))
					continue;

				var c = clusterOf[CellKey(r.X, r.Y)];

				if (!clusterBase.ContainsKey(c) || r.Z < clusterBase[c])
					clusterBase[c] = r.Z;
			}

			// Emit geometry per (family, bucket).
			var buffers = new Dictionary<string, KitPieceGeom>();
			var region = new GameObject("Kit3D");
			region.transform.SetParent(root, false);
			var kitRegion = region.AddComponent<Game.UOKitRegion>();
			var hueWarnings = 0;

			foreach (var cellPair in byCell)
			{
				var cell = cellPair.Value;
				var roofs = new List<KitInstance>();
				var walls = new List<KitInstance>();
				var points = new List<KitInstance>();

				foreach (var r in cell)
				{
					if (IsRoofPiece(r.Entry.piece)) roofs.Add(r);
					else if (r.Entry.piece == "wall" || r.Entry.piece == "window") walls.Add(r);
					else points.Add(r);
				}

				// Gable rule: stacked walls under a roof in the same cell merge
				// into one slab reaching the roof's top.
				if (roofs.Count > 0 && walls.Count > 0)
				{
					var minWall = walls[0];

					foreach (var w in walls)
						if (w.Z < minWall.Z)
							minWall = w;

					var roofTop = int.MinValue;

					foreach (var rf in roofs)
						if (rf.Z > roofTop)
							roofTop = rf.Z;

					var height = (roofTop + 3 - minWall.Z) * ZScale;
					Emit(buffers, kitRegion, clusterOf, clusterBase, minWall, height);
				}
				else
				{
					foreach (var w in walls)
						Emit(buffers, kitRegion, clusterOf, clusterBase, w, UOHD2DKitMeshBuilder.StoryHeight);
				}

				foreach (var p in points)
					Emit(buffers, kitRegion, clusterOf, clusterBase, p, UOHD2DKitMeshBuilder.StoryHeight);

				foreach (var rf in roofs)
					Emit(buffers, kitRegion, clusterOf, clusterBase, rf, UOHD2DKitMeshBuilder.StoryHeight);

				foreach (var r in cell)
					if (r.Hue != 0)
						hueWarnings++;
			}

			if (hueWarnings > 0)
				Debug.LogWarning("[UOHD2D] Kit: " + hueWarnings + " hued statics rendered untinted (hue support pending).");

			// Persist buffers as meshes + renderers.
			foreach (var kv in buffers)
			{
				var parts = kv.Key.Split('|');
				var family = parts[0];
				var bucket = parts[1];

				var mesh = new Mesh { name = "kit_" + family + "_" + bucket, indexFormat = IndexFormat.UInt32 };
				mesh.SetVertices(kv.Value.Verts);
				mesh.SetNormals(kv.Value.Normals);
				mesh.SetUVs(0, kv.Value.UVs);
				mesh.SetTriangles(kv.Value.Tris, 0);
				mesh.RecalculateBounds();
				AssetDatabase.CreateAsset(mesh, assetDir + "/kit_" + family + "_" + bucket + ".asset");

				var bucketGo = region.transform.Find(bucket);

				if (bucketGo == null)
				{
					var b = new GameObject(bucket);
					b.transform.SetParent(region.transform, false);
					bucketGo = b.transform;
				}

				var go = new GameObject(family);
				go.transform.SetParent(bucketGo, false);
				go.AddComponent<MeshFilter>().sharedMesh = mesh;

				var mr = go.AddComponent<MeshRenderer>();
				mr.sharedMaterial = UOHD2DKitMaterials.EnsureFamilyMaterial(family);
				mr.shadowCastingMode = ShadowCastingMode.On;
			}

			Debug.Log("[UOHD2D] Kit pass: " + records.Count + " statics converted into " + buffers.Count + " combined meshes across " + nextCluster + " building clusters.");
		}

		private static void Emit(Dictionary<string, KitPieceGeom> buffers, Game.UOKitRegion region,
			Dictionary<long, int> clusterOf, Dictionary<int, int> clusterBase, KitInstance r, float wallHeight)
		{
			var isRoof = IsRoofPiece(r.Entry.piece);
			var cluster = clusterOf[CellKey(r.X, r.Y)];
			var baseZ = clusterBase.ContainsKey(cluster) ? clusterBase[cluster] : r.Z;
			var story = isRoof ? -1 : Mathf.Max(0, (r.Z - baseZ) / 20);
			var bucket = isRoof ? "Roof" : "Story" + story;
			var bufferKey = r.Entry.family + "|" + bucket;

			KitPieceGeom buffer;

			if (!buffers.TryGetValue(bufferKey, out buffer))
				buffers[bufferKey] = buffer = new KitPieceGeom();

			var geom = UOHD2DKitMeshBuilder.Get(r.Entry.piece, wallHeight);
			var rot = Quaternion.Euler(0f, r.Entry.rotY, 0f);

			// Area pieces (roof footprints) need re-anchoring after rotation so
			// the footprint stays on the tile; edge/point pieces pivot in place.
			var correction = Vector3.zero;

			if (isRoof)
			{
				if (r.Entry.rotY == 90) correction = new Vector3(1f, 0f, 0f);
				else if (r.Entry.rotY == 180) correction = new Vector3(1f, 0f, -1f);
				else if (r.Entry.rotY == 270) correction = new Vector3(0f, 0f, -1f);
			}

			// Roof footprints in the data sit (+1,+1) tiles SE of the walls they
			// cover; pull them back over the building.
			var origin = new Vector3(
				r.X + (isRoof ? -1f : 0f),
				r.Z * ZScale,
				-r.Y + (isRoof ? 1f : 0f)) + correction;

			var baseIndex = buffer.Verts.Count;

			for (var i = 0; i < geom.Verts.Count; i++)
			{
				buffer.Verts.Add(rot * geom.Verts[i] + origin);
				buffer.Normals.Add(rot * geom.Normals[i]);
				buffer.UVs.Add(geom.UVs[i]);
			}

			foreach (var t in geom.Tris)
				buffer.Tris.Add(baseIndex + t);

			region.Records.Add(new Game.UOKitRegion.PieceRecord
			{
				ArtId = r.Entry.id,
				Family = r.Entry.family,
				Piece = r.Entry.piece,
				RotY = r.Entry.rotY,
				X = r.X,
				Y = r.Y,
				Z = r.Z,
				Story = story,
				ClusterId = cluster
			});
		}

		private static bool IsRoofPiece(string piece)
		{
			return piece == "slope" || piece == "ridge" || piece == "flat" || piece == "corner_out" || piece == "corner_in";
		}

		private static long CellKey(int x, int y)
		{
			return ((long)x << 32) ^ (uint)y;
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
