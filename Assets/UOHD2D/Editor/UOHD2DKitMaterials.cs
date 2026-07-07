#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

namespace UOHD2D
{
	/*
	 * Shared per-family kit materials, independent of any region so texture
	 * iteration never requires a reimport. Until a family gets its generated
	 * texture, it renders in a distinct flat debug color.
	 */
	public static class UOHD2DKitMaterials
	{
		private const string MaterialsFolder = "Assets/UOHD2D/Kit/Materials";

		private static readonly Dictionary<string, Color> DebugColors = new Dictionary<string, Color>
		{
			{ "stone_wall", new Color(0.62f, 0.62f, 0.60f) },
			{ "brick_wall", new Color(0.65f, 0.38f, 0.30f) },
			{ "wood_wall", new Color(0.55f, 0.40f, 0.24f) },
			{ "plaster_wall", new Color(0.85f, 0.80f, 0.68f) },
			{ "thatch_roof", new Color(0.78f, 0.65f, 0.35f) },
			{ "shingle_roof", new Color(0.48f, 0.33f, 0.26f) },
			{ "slate_roof", new Color(0.35f, 0.38f, 0.45f) },
			{ "tile_roof", new Color(0.62f, 0.30f, 0.22f) },
			{ "stone_roof", new Color(0.55f, 0.55f, 0.52f) }
		};

		public static Material EnsureFamilyMaterial(string family)
		{
			var path = MaterialsFolder + "/" + family + ".mat";
			var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

			if (mat != null)
				return mat;

			EnsureFolder();

			var shader = Shader.Find("Universal Render Pipeline/Lit");
			mat = new Material(shader) { name = family };

			Color color;

			if (!DebugColors.TryGetValue(family, out color))
				color = Color.magenta;

			mat.SetColor("_BaseColor", color);
			mat.SetFloat("_Smoothness", 0f);
			mat.SetFloat("_Metallic", 0f);

			AssetDatabase.CreateAsset(mat, path);
			return mat;
		}

		private static void EnsureFolder()
		{
			if (!AssetDatabase.IsValidFolder("Assets/UOHD2D/Kit"))
				AssetDatabase.CreateFolder("Assets/UOHD2D", "Kit");

			if (!AssetDatabase.IsValidFolder(MaterialsFolder))
				AssetDatabase.CreateFolder("Assets/UOHD2D/Kit", "Materials");
		}
	}
}
#endif
