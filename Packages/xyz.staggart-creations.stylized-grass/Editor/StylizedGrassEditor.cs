// Stylized Grass Shader © Staggart Creations (http://staggart.xyz)
// COPYRIGHT PROTECTED UNDER THE UNITY ASSET STORE EULA (https://unity.com/legal/as-terms)
//
// ⚠️ WARNING: UNAUTHORIZED USE OR DISTRIBUTION IS STRICTLY PROHIBITED
// • Copying, referencing, or reverse-engineering this source code for the creation of new Asset Store or derivative products,
//   or any other publicly distributed content is strictly forbidden and will result in legal action.
// • Studying this file for the purpose of reproducing its functionality in your own assets or tools is not permitted.
// • If you are viewing this file as a reference, please close it immediately to avoid unintentional design influence or potential EULA violations.
// • Uploading this file or any derivative of it to a public GitHub or similar repository will trigger an automated DMCA takedown request.
// • Studying to understand for personal, educational or integration purposes is allowed, studying to reproduce is not.

using sc.stylizedgrass.runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if URP
using UnityEngine.Rendering.Universal;
#endif
#if COZY3
using System.Collections.Generic;
using DistantLands.Cozy;
using DistantLands.Cozy.Data;
#endif

namespace sc.stylizedgrass.editor
{
    public class StylizedGrassEditor : Editor
    {
        [MenuItem("GameObject/Effects/Grass Bender")]
        public static void CreateGrassBender()
        {
            GrassBender gb = new GameObject().AddComponent<GrassBender>();
            gb.gameObject.name = "Grass Bender";

            Selection.activeGameObject = gb.gameObject;
            EditorApplication.ExecuteMenuItem("GameObject/Move To View");
        }
        
        [MenuItem("GameObject/Effects/Grass Wind Controller")]
        public static void CreateWindController()
        {
            WindController gb = new GameObject().AddComponent<WindController>();
            gb.gameObject.name = "Grass Wind Controller";

            Selection.activeGameObject = gb.gameObject;
        }
        
        [MenuItem("GameObject/Effects/Grass Weather Controller")]
        public static void CreateWeatherController()
        {
            WeatherShadingController gb = new GameObject().AddComponent<WeatherShadingController>();
            gb.gameObject.name = "Grass Weather Controller";

            Selection.activeGameObject = gb.gameObject;
        }
        
        [MenuItem("Window/Stylized Grass/Open demo scene", false, 1004)]
        public static void OpenDemoScene()
        {
            string path = AssetDatabase.GUIDToAssetPath("8e1f93bc9f41c9040a419138bf1796d4");
            
            EditorSceneManager.OpenScene(path);
        }
        
        [MenuItem("Window/Stylized Grass/Setup Render Feature", false, 1003)]
        public static void SetupRenderFeature()
        {
            GrassRenderFeature renderFeature = GrassRenderFeature.GetDefault();
            
            if(!renderFeature)
            {
                Installer.SetupRenderFeature();
            }
            else
            {
                ScriptableRendererData renderer = PipelineUtilities.GetDefaultRenderer();
                Selection.activeObject = renderer;
            }
        }

        #region Context menus
        public static void AddGrassBender(GameObject gameObject)
        {
            if (!gameObject.GetComponent<GrassBender>())
            {
                GrassBender bender = gameObject.AddComponent<GrassBender>();
                bender.OnEnable();
            }
        }
        
        [MenuItem("CONTEXT/MeshFilter/Convert to grass bender")]
        public static void ConvertMeshToBender(MenuCommand cmd)
        {
            MeshFilter mf = (MeshFilter)cmd.context;
            AddGrassBender(mf.gameObject);
        }

        [MenuItem("CONTEXT/TrailRenderer/Convert to grass bender")]
        public static void ConvertTrailToBender(MenuCommand cmd)
        {
            TrailRenderer t = (TrailRenderer)cmd.context;
            AddGrassBender(t.gameObject);
        }

        [MenuItem("CONTEXT/ParticleSystem/Convert to grass bender")]
        public static void ConvertParticleToBender(MenuCommand cmd)
        {
            ParticleSystem ps = (ParticleSystem)cmd.context;
            AddGrassBender(ps.gameObject);
        }
        
        [MenuItem("CONTEXT/LineRenderer/Convert to grass bender")]
        public static void ConvertLineToBender(MenuCommand cmd)
        {
            LineRenderer line = (LineRenderer)cmd.context;
            AddGrassBender(line.gameObject);
        }
        
        [MenuItem("CONTEXT/MeshFilter/Bake Height Vertex Color")]
        public static void BakeHeightIntoMesh(MenuCommand cmd)
        {
            MeshFilter mf = (MeshFilter)cmd.context;

            if (mf.sharedMesh)
            {
                mf.sharedMesh = MeshBaker.BakeHeight(mf.sharedMesh);
            }
        }
        
        [MenuItem("CONTEXT/LODGroup/Bake Height Vertex Color")]
        public static void BakeHeightIntoLODS(MenuCommand cmd)
        {
            LODGroup lodGroup = (LODGroup)cmd.context;

            LOD[] lods = lodGroup.GetLODs();
            for (int i = 0; i < lods.Length; i++)
            {
                LOD lod = lods[i];

                for (int j = 0; j < lod.renderers.Length; j++)
                {
                    MeshFilter mf = lod.renderers[j].GetComponent<MeshFilter>();

                    if (mf && mf.sharedMesh)
                    {
                        mf.sharedMesh = MeshBaker.BakeHeight(mf.sharedMesh);
                    }
                }
            }
        }


        #endregion
        
        #region Integration
        public static bool IsCozyInteractionSetup()
        {
            bool hasSetup = false;
            
            #if COZY3
            if (CozyWeather.instance)
            {
                CozyInteractionsModule interactionsModule = CozyWeather.instance.interactionsModule;

                if (interactionsModule == null) return false;
                
                List<MaterialManagerProfile.ModulatedValue> values = new List<MaterialManagerProfile.ModulatedValue>(interactionsModule.profile.modulatedValues);
                
                foreach (var value in values)
                {
                    if (value.modulationTarget == MaterialManagerProfile.ModulatedValue.ModulationTarget.globalValue)
                    {
                        if (value.targetVariableName == MaterialUI.GLOBAL_WETNESS_PARAM)
                        {
                            hasSetup = true;
                            break;
                        }
                    }
                }
            }
            else
            {
                return false;
            }
            #endif

            return hasSetup;
        }
        
        public static void SetupCozyInteractions()
        {
            #if COZY3
            if (CozyWeather.instance)
            {
                CozyInteractionsModule interactionsModule = CozyWeather.instance.interactionsModule;

                if (!interactionsModule)
                {
                    interactionsModule = CozyWeather.instance.gameObject.AddComponent<CozyInteractionsModule>();
                }

                if (!interactionsModule)
                {
                    Debug.LogError("Failed to add Cozy Interactions Module to Cozy Weather instance. Please add this manually and try again...");
                    return;
                }
                List<MaterialManagerProfile.ModulatedValue> values = new List<MaterialManagerProfile.ModulatedValue>(interactionsModule.profile.modulatedValues);
                
                AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                
                MaterialManagerProfile.ModulatedValue rainOverride = new MaterialManagerProfile.ModulatedValue
                    {
                        modulationTarget = MaterialManagerProfile.ModulatedValue.ModulationTarget.globalValue,
                        modulationSource = MaterialManagerProfile.ModulatedValue.ModulationSource.precipitation,
                        mappedCurve = curve,
                        targetVariableName = MaterialUI.GLOBAL_WETNESS_PARAM
                    };

                MaterialManagerProfile.ModulatedValue snowOverride = new MaterialManagerProfile.ModulatedValue
                    {
                        modulationTarget = MaterialManagerProfile.ModulatedValue.ModulationTarget.globalValue,
                        modulationSource = MaterialManagerProfile.ModulatedValue.ModulationSource.snowAmount,
                        mappedCurve = curve,
                        targetVariableName = MaterialUI.GLOBAL_SNOW_PARAM
                    };

                values.Add(rainOverride);
                values.Add(snowOverride);
                
                interactionsModule.profile.modulatedValues = values.ToArray();
                
                EditorUtility.SetDirty(interactionsModule.profile);
            }
            else
            {
                Debug.LogError("Cozy Weather instance not found. Please make sure it is already set up in the scene.");
            }
            #endif
        }

        public static string GetCurvedWorldShaderIncludePath()
        {
            //return string.Empty;
            return AssetDatabase.GUIDToAssetPath("208a98c9ab72b9f4bb8735c6a229e807");
        }
        #endregion
    }
}