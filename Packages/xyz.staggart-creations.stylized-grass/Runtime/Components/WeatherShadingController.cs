// ⚠️ WARNING: UNAUTHORIZED USE OR DISTRIBUTION IS STRICTLY PROHIBITED
// • Copying, referencing, or reverse-engineering this source code for the creation of new Asset Store or derivative products,
//   or any other publicly distributed content is strictly forbidden and will result in legal action.
// • Studying this file for the purpose of reproducing its functionality in your own assets or tools is not permitted.
// • If you are viewing this file as a reference, please close it immediately to avoid unintentional design influence or potential EULA violations.
// • Uploading this file or any derivative of it to a public GitHub or similar repository will trigger an automated DMCA takedown request.
// • Studying to understand for personal, educational or integration purposes is allowed, studying to reproduce is not.

using System;
using UnityEngine;
#if ENVIRO_3
using Enviro;
#endif

namespace sc.stylizedgrass.runtime
{
    [ExecuteInEditMode]
    [AddComponentMenu("Stylized Grass/Stylized Grass Weather Controller")]
    public class WeatherShadingController : MonoBehaviour
    {
        public static WeatherShadingController Instance;
        
        [Range(0f, 1f)]
        public float wetness;
        [Range(0f, 1f)]
        public float snowAmount;

        private static readonly int _GrassWetness = Shader.PropertyToID("_GrassWetness");
        private static readonly int _GrassSnowCoverage = Shader.PropertyToID("_GrassSnowCoverage");

        private void OnEnable()
        {
            Instance = this;
        }

        private void Update()
        {
            SetShaderProperties();
        }
        
        public bool UsingEnviro()
        {
#if ENVIRO_3
            return EnviroManager.instance;
#else
            return false;
#endif
        }

        public void SetShaderProperties()
        {
            if(!enabled) return;
            
            var m_wetness = wetness;
            var m_snow = snowAmount;
            
            #if ENVIRO_3
            if (UsingEnviro())
            {
                m_wetness = Mathf.Min(1f, EnviroManager.instance.Environment.Settings.wetness);
                m_snow = Mathf.Min(1f, EnviroManager.instance.Environment.Settings.snow);
            }
            #endif
            
            SetWetnessAmount(m_wetness);
            SetSnowAmount(m_snow);
        }

        public static void SetWetnessAmount(float value)
        {
            Shader.SetGlobalFloat(_GrassWetness, value);
        }
        
        public static void SetSnowAmount(float value)
        {
            Shader.SetGlobalFloat(_GrassSnowCoverage, value);
        }

        public void DisableShading()
        {
            SetWetnessAmount(0);
            SetSnowAmount(0);
        }

        private void OnDisable()
        {
            DisableShading();

            Instance = null;
        }
    }
}