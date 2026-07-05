#if UNITY_EDITOR
using System;
using System.IO;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;

namespace UOHD2D
{
	public static class UOHD2DBatchImport
	{
		// Invoked headlessly: Unity.exe -batchmode -executeMethod UOHD2D.UOHD2DBatchImport.Run
		// Reads the export folder from the UO_EXPORT_DIR environment variable,
		// imports it, and saves the result as a scene named after the region.
		public static void Run()
		{
			var dir = Environment.GetEnvironmentVariable("UO_EXPORT_DIR");

			if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "manifest.json")))
			{
				Debug.LogError("UO_EXPORT_DIR not set or manifest.json missing: " + dir);
				EditorApplication.Exit(1);
				return;
			}

			var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

			UOHD2DImporter.ImportFolder(dir);

			if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
				AssetDatabase.CreateFolder("Assets", "Scenes");

			var sceneName = new DirectoryInfo(dir).Name;

			EditorSceneManager.SaveScene(scene, "Assets/Scenes/" + sceneName + ".unity");
			AssetDatabase.SaveAssets();

			Debug.Log("UOHD2D batch import complete: " + sceneName);
			EditorApplication.Exit(0);
		}
	}
}
#endif
