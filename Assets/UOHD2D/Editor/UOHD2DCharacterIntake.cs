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
	 * Packs generated character frames (Assets/UOHD2D/Generated/Characters/{slug}/
	 * frames/{anim}_{dir}_{n}.png + character.json) into one grid spritesheet
	 * (row = animIndex * 8 + dirIndex, col = frame), then creates/updates the
	 * CharacterSpriteSet asset and its lit material. Idempotent — assets are
	 * updated in place so references survive regeneration.
	 */
	public static class UOHD2DCharacterIntake
	{
		private const string CharactersRoot = "Assets/UOHD2D/Generated/Characters";

		[Serializable]
		private class AnimManifest
		{
			public string name;
			public int frames;
			public float fps;
		}

		[Serializable]
		private class CharacterManifest
		{
			public string id;
			public int canvas = 64;
			public float ppu = 44f;
			public float[] pivot = { 0.5f, 0.06f };
			public string[] directions;
			public AnimManifest[] animations;
		}

		[MenuItem("UO HD2D/Intake/Process All Characters")]
		public static void ProcessAll()
		{
			if (!AssetDatabase.IsValidFolder(CharactersRoot))
			{
				Debug.Log("[UOHD2D] No generated characters folder yet.");
				return;
			}

			foreach (var dir in Directory.GetDirectories(CharactersRoot))
				Process(Path.GetFileName(dir));
		}

		public static void Process(string slug)
		{
			var dir = CharactersRoot + "/" + slug;
			var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/character.json");

			if (manifestAsset == null)
			{
				Debug.LogWarning("[UOHD2D] Character intake: no character.json for " + slug);
				return;
			}

			var manifest = JsonUtility.FromJson<CharacterManifest>(manifestAsset.text);

			if (manifest.directions == null || manifest.directions.Length != 8 || manifest.animations == null || manifest.animations.Length == 0)
			{
				Debug.LogError("[UOHD2D] Character intake: malformed manifest for " + slug);
				return;
			}

			var canvas = manifest.canvas;
			var columns = 0;

			foreach (var anim in manifest.animations)
				columns = Mathf.Max(columns, anim.frames);

			var rows = manifest.animations.Length * 8;
			var sheet = new Texture2D(columns * canvas, rows * canvas, TextureFormat.RGBA32, false);
			var clear = new Color32[canvas * canvas];
			var missing = 0;

			for (var a = 0; a < manifest.animations.Length; a++)
			{
				for (var d = 0; d < 8; d++)
				{
					var row = a * 8 + d;

					for (var f = 0; f < manifest.animations[a].frames; f++)
					{
						var framePath = dir + "/frames/" + manifest.animations[a].name + "_" + manifest.directions[d] + "_" + f + ".png";
						var frame = AssetDatabase.LoadAssetAtPath<Texture2D>(framePath);
						var x = f * canvas;
						var y = (rows - 1 - row) * canvas;

						if (frame == null)
						{
							sheet.SetPixels32(x, y, canvas, canvas, clear);
							missing++;
							continue;
						}

						sheet.SetPixels32(x, y, canvas, canvas, frame.GetPixels32());
					}
				}
			}

			if (missing > 0)
				Debug.LogWarning("[UOHD2D] Character intake " + slug + ": " + missing + " frames missing (cells left empty).");

			var sheetPath = dir + "/" + slug + "_sheet.png";
			File.WriteAllBytes(sheetPath, sheet.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(sheet);
			AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceUpdate);

			var sheetTex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);

			// Material: custom sprite-lit graph when present, else lit cutout.
			var matPath = dir + "/" + slug + "_mat.mat";
			var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

			if (mat == null)
			{
				var spriteLit = Shader.Find("Shader Graphs/UOHD2D_SpriteLit");
				var urpLit = Shader.Find("Universal Render Pipeline/Lit");
				mat = new Material(spriteLit != null ? spriteLit : urpLit);
				AssetDatabase.CreateAsset(mat, matPath);
			}

			mat.SetTexture("_BaseMap", sheetTex);

			if (mat.HasProperty("_Cutoff"))
			{
				mat.SetFloat("_AlphaClip", 1f);
				mat.SetFloat("_Cutoff", 0.5f);
				mat.SetFloat("_Smoothness", 0f);
				mat.SetFloat("_Cull", (float)CullMode.Off);
				mat.EnableKeyword("_ALPHATEST_ON");
				mat.renderQueue = (int)RenderQueue.AlphaTest;
			}

			// Sprite set asset.
			var setPath = dir + "/" + slug + "_set.asset";
			var set = AssetDatabase.LoadAssetAtPath<Game.CharacterSpriteSet>(setPath);

			if (set == null)
			{
				set = ScriptableObject.CreateInstance<Game.CharacterSpriteSet>();
				AssetDatabase.CreateAsset(set, setPath);
			}

			set.Sheet = sheetTex;
			set.Material = mat;
			set.Canvas = canvas;
			set.PPU = manifest.ppu;
			set.Pivot = new Vector2(manifest.pivot[0], manifest.pivot[1]);
			set.Columns = columns;
			set.Rows = rows;
			set.Animations = new Game.CharacterSpriteSet.AnimDef[manifest.animations.Length];

			for (var i = 0; i < manifest.animations.Length; i++)
			{
				set.Animations[i] = new Game.CharacterSpriteSet.AnimDef
				{
					Name = manifest.animations[i].name,
					Frames = manifest.animations[i].frames,
					Fps = manifest.animations[i].fps
				};
			}

			EditorUtility.SetDirty(set);
			EditorUtility.SetDirty(mat);
			AssetDatabase.SaveAssets();

			Debug.Log("[UOHD2D] Character intake: " + slug + " packed " + columns + "x" + rows + " cells @" + canvas + "px.");
		}
	}
}
#endif
