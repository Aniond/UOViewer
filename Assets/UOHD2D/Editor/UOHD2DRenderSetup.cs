#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UOHD2D
{
	/*
	 * One-click, idempotent configuration of the URP pipeline/renderer assets for
	 * the HD-2D look: depth + opaque textures, soft high-res shadows, Forward+,
	 * post-processing data assignment and an SSAO renderer feature.
	 */
	public static class UOHD2DRenderSetup
	{
		private const string PipelinePath = "Assets/Settings/URP-Pipeline.asset";
		private const string RendererPath = "Assets/Settings/URP-Renderer.asset";

		[MenuItem("UO HD2D/Setup Render Pipeline")]
		public static void Run()
		{
			SetupPipeline();
			SetupRenderer();
			AssetDatabase.SaveAssets();
			Debug.Log("[UOHD2D] Render pipeline configured for HD-2D.");
		}

		private static void SetupPipeline()
		{
			var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);

			if (asset == null)
			{
				Debug.LogError("[UOHD2D] Pipeline asset not found at " + PipelinePath);
				return;
			}

			var so = new SerializedObject(asset);

			SetBool(so, "m_RequireDepthTexture", true);
			SetBool(so, "m_RequireOpaqueTexture", true);
			SetBool(so, "m_SupportsHDR", true);
			SetBool(so, "m_SoftShadowsSupported", true);
			SetInt(so, "m_MainLightShadowmapResolution", 4096);
			SetFloat(so, "m_ShadowDistance", 60f);
			SetInt(so, "m_ShadowCascadeCount", 2);
			SetFloat(so, "m_ShadowDepthBias", 1.5f);
			SetBool(so, "m_AdditionalLightShadowsSupported", true);
			SetInt(so, "m_AdditionalLightsShadowmapResolution", 2048);
			SetInt(so, "m_ColorGradingMode", (int)ColorGradingMode.HighDynamicRange);
			SetInt(so, "m_ColorGradingLutSize", 64);

			so.ApplyModifiedProperties();
			EditorUtility.SetDirty(asset);
		}

		private static void SetupRenderer()
		{
			var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);

			if (data == null)
			{
				Debug.LogError("[UOHD2D] Renderer asset not found at " + RendererPath);
				return;
			}

			var so = new SerializedObject(data);

			// Post-processing runs only when the renderer has its PostProcessData assigned.
			var ppd = so.FindProperty("postProcessData");

			if (ppd.objectReferenceValue == null)
				ppd.objectReferenceValue = FindPostProcessData();

			SetInt(so, "m_RenderingMode", (int)RenderingMode.ForwardPlus);
			so.ApplyModifiedProperties();

			AddSsaoFeature(data, so);
			EditorUtility.SetDirty(data);
		}

		private static PostProcessData FindPostProcessData()
		{
			foreach (var guid in AssetDatabase.FindAssets("t:PostProcessData"))
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);

				if (path.StartsWith("Packages/com.unity.render-pipelines.universal"))
					return AssetDatabase.LoadAssetAtPath<PostProcessData>(path);
			}

			var any = AssetDatabase.FindAssets("t:PostProcessData");
			return any.Length > 0 ? AssetDatabase.LoadAssetAtPath<PostProcessData>(AssetDatabase.GUIDToAssetPath(any[0])) : null;
		}

		private static void AddSsaoFeature(UniversalRendererData data, SerializedObject so)
		{
			foreach (var feature in data.rendererFeatures)
				if (feature is ScreenSpaceAmbientOcclusion)
					return;

			var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
			ssao.name = "SSAO";
			AssetDatabase.AddObjectToAsset(ssao, data);
			AssetDatabase.SaveAssets();

			var sso = new SerializedObject(ssao);
			var settings = sso.FindProperty("m_Settings");

			if (settings != null)
			{
				SetRelativeFloat(settings, "Intensity", 0.5f);
				SetRelativeFloat(settings, "Radius", 0.35f);

				var source = settings.FindPropertyRelative("Source");

				if (source != null)
					source.enumValueIndex = 1; // DepthNormals

				sso.ApplyModifiedProperties();
			}

			so.Update();

			var features = so.FindProperty("m_RendererFeatures");
			features.arraySize++;
			features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ssao;

			string guid;
			long localId;

			if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out guid, out localId))
			{
				var map = so.FindProperty("m_RendererFeatureMap");
				map.arraySize++;
				map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
			}

			so.ApplyModifiedProperties();
		}

		private static void SetBool(SerializedObject so, string name, bool value)
		{
			var p = so.FindProperty(name);

			if (p != null)
				p.boolValue = value;
			else
				Debug.LogWarning("[UOHD2D] Property not found: " + name);
		}

		private static void SetInt(SerializedObject so, string name, int value)
		{
			var p = so.FindProperty(name);

			if (p != null)
				p.intValue = value;
			else
				Debug.LogWarning("[UOHD2D] Property not found: " + name);
		}

		private static void SetFloat(SerializedObject so, string name, float value)
		{
			var p = so.FindProperty(name);

			if (p != null)
				p.floatValue = value;
			else
				Debug.LogWarning("[UOHD2D] Property not found: " + name);
		}

		private static void SetRelativeFloat(SerializedProperty parent, string name, float value)
		{
			var p = parent.FindPropertyRelative(name);

			if (p != null)
				p.floatValue = value;
		}
	}
}
#endif
