#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

namespace UOHD2D
{
	/*
	 * Automatic import settings + intake scheduling for everything the asset-gen
	 * workbench drops into Assets/UOHD2D/Generated/. Pixel art gets point
	 * filtering and stays readable for sheet packing; models and characters are
	 * handed to their intake processors after the import batch completes.
	 */
	public class UOHD2DAssetPostprocessor : AssetPostprocessor
	{
		private const string GeneratedRoot = "Assets/UOHD2D/Generated/";

		private void OnPreprocessTexture()
		{
			if (!assetPath.StartsWith(GeneratedRoot))
				return;

			var importer = (TextureImporter)assetImporter;
			importer.textureType = TextureImporterType.Default;
			importer.filterMode = FilterMode.Point;
			importer.mipmapEnabled = false;
			importer.textureCompression = TextureImporterCompression.Uncompressed;
			importer.sRGBTexture = true;
			importer.alphaIsTransparency = true;
			importer.npotScale = TextureImporterNPOTScale.None;
			importer.wrapMode = TextureWrapMode.Clamp;
			importer.maxTextureSize = 4096;

			// Individual frames get packed into sheets by the character intake,
			// which needs CPU access to their pixels.
			if (assetPath.Contains("/frames/"))
				importer.isReadable = true;
		}

		private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
		{
			var modelSlugs = new HashSet<string>();
			var characterSlugs = new HashSet<string>();

			foreach (var path in imported)
			{
				if (!path.StartsWith(GeneratedRoot))
					continue;

				var rest = path.Substring(GeneratedRoot.Length);
				var parts = rest.Split('/');

				if (parts.Length < 2)
					continue;

				if (parts[0] == "Models" && (path.EndsWith(".glb") || path.EndsWith("model.json")))
					modelSlugs.Add(parts[1]);
				else if (parts[0] == "Characters" && (path.EndsWith(".png") || path.EndsWith("character.json")))
					characterSlugs.Add(parts[1]);
			}

			if (modelSlugs.Count == 0 && characterSlugs.Count == 0)
				return;

			EditorApplication.delayCall += () =>
			{
				foreach (var slug in modelSlugs)
					UOHD2DModelIntake.Process(slug);

				foreach (var slug in characterSlugs)
					UOHD2DCharacterIntake.Process(slug);
			};
		}
	}
}
#endif
