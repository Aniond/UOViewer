#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace UOHD2D
{
	/*
	 * Loads the art-id -> kit-piece mapping. The corrected map versioned with
	 * the project (Assets/UOHD2D/Replacements/kitmap.json) wins; the export
	 * bundle's draft is the fallback. "slab" is accepted as a legacy alias of
	 * "flat".
	 */
	public class UOHD2DKitLibrary
	{
		public const string AssetsKitmapPath = "Assets/UOHD2D/Replacements/kitmap.json";

		[Serializable]
		public class KitEntry
		{
			public int id;
			public string family;
			public string piece;
			public int rotY;
		}

		[Serializable]
		private class KitMap
		{
			public int version;
			public KitEntry[] entries;
		}

		private readonly Dictionary<int, KitEntry> _byId = new Dictionary<int, KitEntry>();

		public int Count => _byId.Count;

		public static UOHD2DKitLibrary Load(string bundleDir)
		{
			var lib = new UOHD2DKitLibrary();
			var path = File.Exists(AssetsKitmapPath) ? AssetsKitmapPath : Path.Combine(bundleDir, "kitmap.json");

			if (!File.Exists(path))
			{
				Debug.LogWarning("[UOHD2D] No kitmap found (looked at " + AssetsKitmapPath + " and bundle). Kit pass disabled.");
				return lib;
			}

			var map = JsonUtility.FromJson<KitMap>(File.ReadAllText(path));

			if (map == null || map.entries == null)
				return lib;

			foreach (var e in map.entries)
			{
				if (e.piece == "slab")
					e.piece = "flat";

				lib._byId[e.id] = e;
			}

			Debug.Log("[UOHD2D] Kit library: " + lib._byId.Count + " entries from " + path);
			return lib;
		}

		public bool TryGetEntry(int artId, out KitEntry entry)
		{
			return _byId.TryGetValue(artId, out entry);
		}
	}
}
#endif
