#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UOHD2D
{
	/*
	 * Applies the HD-2D environment to the open scene, idempotently:
	 * warm key light angled off the billboard plane, cool gradient ambient,
	 * a global post-processing volume (DoF, bloom, ACES grade, vignette) and
	 * subtle distance fog that hides the region edge. Cameras get HDR,
	 * post-processing and SMAA.
	 */
	public static class UOHD2DEnvironmentSetup
	{
		private const string ProfilePath = "Assets/Settings/UOHD2D_VolumeProfile.asset";
		private const string VolumeName = "HD2D Global Volume";

		[MenuItem("UO HD2D/Apply HD2D Environment")]
		public static void Run()
		{
			SetupSun();
			SetupAmbientAndFog();
			SetupVolume();
			SetupCameras();

			EditorSceneManager.MarkAllScenesDirty();
			AssetDatabase.SaveAssets();
			Debug.Log("[UOHD2D] HD-2D environment applied to scene.");
		}

		private static void SetupSun()
		{
			Light sun = null;

			foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
			{
				if (light.type != LightType.Directional)
					continue;

				sun = light;
				break;
			}

			if (sun == null)
			{
				var go = new GameObject("Directional Light");
				sun = go.AddComponent<Light>();
				sun.type = LightType.Directional;
			}

			// Azimuth ~25 deg off the fixed camera axis keeps billboards front-lit
			// and their cast shadows diagonal, never edge-on.
			sun.transform.rotation = Quaternion.Euler(50f, 25f, 0f);
			sun.color = Hex("#FFE0B5");
			sun.intensity = 1.3f;
			sun.shadows = LightShadows.Soft;
			sun.shadowStrength = 0.9f;
		}

		private static void SetupAmbientAndFog()
		{
			RenderSettings.ambientMode = AmbientMode.Trilight;
			RenderSettings.ambientSkyColor = Hex("#9DB4CC");
			RenderSettings.ambientEquatorColor = Hex("#8A8578");
			RenderSettings.ambientGroundColor = Hex("#5A4B3C");

			RenderSettings.fog = true;
			RenderSettings.fogMode = FogMode.Linear;
			RenderSettings.fogStartDistance = 30f;
			RenderSettings.fogEndDistance = 90f;
			RenderSettings.fogColor = Hex("#B7A895");
		}

		private static void SetupVolume()
		{
			var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);

			if (profile == null)
			{
				profile = ScriptableObject.CreateInstance<VolumeProfile>();
				AssetDatabase.CreateAsset(profile, ProfilePath);
			}

			var dof = GetOrAdd<DepthOfField>(profile);
			dof.mode.Override(DepthOfFieldMode.Bokeh);
			dof.focusDistance.Override(26f); // tracks the camera distance; FocusTracker drives this at runtime later
			dof.focalLength.Override(50f);
			dof.aperture.Override(5.6f);

			var bloom = GetOrAdd<Bloom>(profile);
			bloom.threshold.Override(1.1f);
			bloom.intensity.Override(0.45f);
			bloom.scatter.Override(0.65f);

			var color = GetOrAdd<ColorAdjustments>(profile);
			color.postExposure.Override(0.2f);
			color.contrast.Override(15f);
			color.saturation.Override(8f);

			var wb = GetOrAdd<WhiteBalance>(profile);
			wb.temperature.Override(8f);

			var tone = GetOrAdd<Tonemapping>(profile);
			tone.mode.Override(TonemappingMode.ACES);

			var vignette = GetOrAdd<Vignette>(profile);
			vignette.intensity.Override(0.28f);
			vignette.smoothness.Override(0.4f);

			EditorUtility.SetDirty(profile);

			var go = GameObject.Find(VolumeName);

			if (go == null)
				go = new GameObject(VolumeName);

			var volume = go.GetComponent<Volume>();

			if (volume == null)
				volume = go.AddComponent<Volume>();

			volume.isGlobal = true;
			volume.sharedProfile = profile;
		}

		private static void SetupCameras()
		{
			foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
			{
				cam.allowHDR = true;
				cam.nearClipPlane = 0.5f;
				cam.farClipPlane = 150f;

				var extra = cam.GetUniversalAdditionalCameraData();
				extra.renderPostProcessing = true;
				extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
				extra.antialiasingQuality = AntialiasingQuality.High;
				extra.dithering = true;
			}
		}

		private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
		{
			T component;

			if (profile.TryGet(out component))
				return component;

			return profile.Add<T>(false);
		}

		private static Color Hex(string hex)
		{
			Color color;
			ColorUtility.TryParseHtmlString(hex, out color);
			return color;
		}
	}
}
#endif

