#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEditor;
using UnityEngine;

namespace UOHD2D
{
	/*
	 * Builds the corrected kitmap (v2) at Assets/UOHD2D/Replacements/kitmap.json
	 * from the bundle's draft plus data analysis:
	 *  - Wall/post/window/corner entries: draft kept, known windows/posts added
	 *    by id (full-tile 44px-wide art verified in the bundle).
	 *  - Roof entries: orientation derived from same-id adjacency. Roof rows
	 *    step +3z per tile toward the ridge, so the sign of dz along each axis
	 *    gives the downhill direction; ids with flat adjacency are ridges
	 *    (axis = the direction the id repeats) or flats.
	 */
	public static class UOHD2DKitmapAudit
	{
		private const string DefaultBundle = "C:/UnityGames/UO/UO-Export/britain";

		[Serializable] private class StaticEntry { public int x, y, z, id, hue; }
		[Serializable] private class StaticList { public StaticEntry[] items; }

		private static readonly Dictionary<int, string> WindowFamilies = new Dictionary<int, string>
		{
			{ 14, "wood_wall" }, { 15, "wood_wall" }, { 185, "wood_wall" }, { 186, "wood_wall" },
			{ 187, "wood_wall" }, { 188, "wood_wall" }, { 59, "brick_wall" }, { 60, "brick_wall" },
			{ 314, "plaster_wall" }, { 315, "plaster_wall" }, { 340, "plaster_wall" },
			{ 341, "plaster_wall" }, { 342, "plaster_wall" }
		};

		private static readonly Dictionary<int, string> PostFamilies = new Dictionary<int, string>
		{
			{ 9, "wood_wall" }, { 23, "stone_wall" }, { 169, "wood_wall" },
			{ 204, "stone_wall" }, { 223, "stone_wall" }, { 298, "plaster_wall" }
		};

		private static readonly int[] StoneRoofFlats = { 1372, 1373, 1374, 1375, 1376, 1377 };
		private static readonly int[] ExtraSlateIds = { 1407, 1408, 1409, 1410, 1411, 1412, 1413 };
		private static readonly HashSet<int> KnownFlatIds = new HashSet<int> { 1474, 1459 };

		[MenuItem("UO HD2D/Kit/Audit Kitmap")]
		public static void Run()
		{
			var bundle = Environment.GetEnvironmentVariable("UO_EXPORT_DIR");

			if (string.IsNullOrEmpty(bundle) || !File.Exists(Path.Combine(bundle, "kitmap.json")))
				bundle = DefaultBundle;

			var draft = UOHD2DKitLibraryDraft.LoadDraft(Path.Combine(bundle, "kitmap.json"));
			var statics = JsonUtility.FromJson<StaticList>("{\"items\":" + File.ReadAllText(Path.Combine(bundle, "statics.json")) + "}").items;

			// Instance positions per art id.
			var byId = new Dictionary<int, List<StaticEntry>>();

			foreach (var s in statics)
			{
				List<StaticEntry> list;

				if (!byId.TryGetValue(s.id, out list))
					byId[s.id] = list = new List<StaticEntry>();

				list.Add(s);
			}

			var final = new List<UOHD2DKitLibrary.KitEntry>();
			var report = new StringBuilder();
			var roofCount = 0;

			foreach (var e in draft)
			{
				if (e.family.EndsWith("_roof"))
				{
					var classified = ClassifyRoof(e.id, e.family, byId);
					final.Add(classified);
					roofCount++;
					report.AppendLine("roof " + e.id + " (" + e.family + ") -> " + classified.piece + " rotY " + classified.rotY);
				}
				else
				{
					final.Add(e); // walls/posts keep draft values
				}
			}

			foreach (var id in ExtraSlateIds)
				if (byId.ContainsKey(id))
				{
					var c = ClassifyRoof(id, "slate_roof", byId);
					final.Add(c);
					report.AppendLine("slate+ " + id + " -> " + c.piece + " rotY " + c.rotY);
				}

			foreach (var id in StoneRoofFlats)
				if (byId.ContainsKey(id))
					final.Add(new UOHD2DKitLibrary.KitEntry { id = id, family = "stone_roof", piece = "flat", rotY = 0 });

			foreach (var kv in WindowFamilies)
				if (byId.ContainsKey(kv.Key))
					final.Add(new UOHD2DKitLibrary.KitEntry { id = kv.Key, family = kv.Value, piece = "window", rotY = GuessWallRot(kv.Key, draft) });

			foreach (var kv in PostFamilies)
				if (byId.ContainsKey(kv.Key))
					final.Add(new UOHD2DKitLibrary.KitEntry { id = kv.Key, family = kv.Value, piece = "post", rotY = 0 });

			// De-duplicate (later entries win over draft).
			var dedup = new Dictionary<int, UOHD2DKitLibrary.KitEntry>();

			foreach (var e in final)
				dedup[e.id] = e;

			WriteKitmap(new List<UOHD2DKitLibrary.KitEntry>(dedup.Values));
			Debug.Log("[UOHD2D] Kitmap audit: " + dedup.Count + " entries (" + roofCount + " roofs classified). Report:\n" + report);
		}

		private static UOHD2DKitLibrary.KitEntry ClassifyRoof(int id, string family, Dictionary<int, List<StaticEntry>> byId)
		{
			if (KnownFlatIds.Contains(id))
				return new UOHD2DKitLibrary.KitEntry { id = id, family = family, piece = "flat", rotY = 0 };

			var instances = byId.ContainsKey(id) ? byId[id] : new List<StaticEntry>();
			var pos = new Dictionary<long, int>(); // (x,y) -> z

			foreach (var s in instances)
				pos[Key(s.x, s.y)] = s.z;

			var dzxSum = 0; var dzxN = 0;
			var dzySum = 0; var dzyN = 0;

			foreach (var s in instances)
			{
				int zx;

				if (pos.TryGetValue(Key(s.x + 1, s.y), out zx)) { dzxSum += zx - s.z; dzxN++; }

				int zy;

				if (pos.TryGetValue(Key(s.x, s.y + 1), out zy)) { dzySum += zy - s.z; dzyN++; }
			}

			var dzx = dzxN > 0 ? (float)dzxSum / dzxN : 0f;
			var dzy = dzyN > 0 ? (float)dzySum / dzyN : 0f;

			// Slopes: dz of -3 along an axis means the NEXT tile that way is lower.
			if (Mathf.Abs(dzx) > 1.5f && Mathf.Abs(dzx) >= Mathf.Abs(dzy))
				return new UOHD2DKitLibrary.KitEntry { id = id, family = family, piece = "slope", rotY = dzx < 0 ? 90 : 270 }; // east : west

			if (Mathf.Abs(dzy) > 1.5f)
				return new UOHD2DKitLibrary.KitEntry { id = id, family = family, piece = "slope", rotY = dzy < 0 ? 180 : 0 }; // south : north

			// Flat adjacency: ridge if it forms runs, flat/corner otherwise.
			if (instances.Count >= 20)
				return new UOHD2DKitLibrary.KitEntry { id = id, family = family, piece = "ridge", rotY = dzxN >= dzyN ? 0 : 90 };

			// Low-use leftovers are probably hip/valley corners; render as slopes for now.
			return new UOHD2DKitLibrary.KitEntry { id = id, family = family, piece = "slope", rotY = 0 };
		}

		private static int GuessWallRot(int id, List<UOHD2DKitLibrary.KitEntry> draft)
		{
			// Windows share orientation with their odd/even wall neighbors; default 0.
			foreach (var e in draft)
				if (e.id == id - 1 || e.id == id + 1)
					return e.rotY;

			return 0;
		}

		private static long Key(int x, int y)
		{
			return ((long)x << 32) ^ (uint)y;
		}

		private static void WriteKitmap(List<UOHD2DKitLibrary.KitEntry> entries)
		{
			entries.Sort((a, b) => a.id.CompareTo(b.id));

			var sb = new StringBuilder();
			sb.AppendLine("{");
			sb.AppendLine("  \"version\": 2,");
			sb.AppendLine("  \"entries\": [");

			for (var i = 0; i < entries.Count; i++)
			{
				var e = entries[i];
				sb.Append("    { \"id\": " + e.id + ", \"family\": \"" + e.family + "\", \"piece\": \"" + e.piece + "\", \"rotY\": " + e.rotY + " }");
				sb.AppendLine(i < entries.Count - 1 ? "," : "");
			}

			sb.AppendLine("  ]");
			sb.AppendLine("}");

			if (!AssetDatabase.IsValidFolder("Assets/UOHD2D/Replacements"))
				AssetDatabase.CreateFolder("Assets/UOHD2D", "Replacements");

			File.WriteAllText(UOHD2DKitLibrary.AssetsKitmapPath, sb.ToString());
			AssetDatabase.ImportAsset(UOHD2DKitLibrary.AssetsKitmapPath);
		}
	}

	// Loads the bundle draft without the Assets-first preference of the library.
	internal static class UOHD2DKitLibraryDraft
	{
		[Serializable]
		private class KitMap { public UOHD2DKitLibrary.KitEntry[] entries; }

		public static List<UOHD2DKitLibrary.KitEntry> LoadDraft(string path)
		{
			var list = new List<UOHD2DKitLibrary.KitEntry>();

			if (!File.Exists(path))
				return list;

			var map = JsonUtility.FromJson<KitMap>(File.ReadAllText(path));

			if (map != null && map.entries != null)
				foreach (var e in map.entries)
				{
					if (e.piece == "slab")
						e.piece = "flat";

					list.Add(e);
				}

			return list;
		}
	}
}
#endif
