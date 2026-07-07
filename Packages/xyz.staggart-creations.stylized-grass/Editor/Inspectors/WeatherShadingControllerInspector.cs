using System;
using sc.stylizedgrass.runtime;
using UnityEditor;
using UnityEngine;

namespace sc.stylizedgrass.editor
{
    [CustomEditor(typeof(WeatherShadingController))]
    public class WeatherShadingControllerInspector : Editor
    {
        private WeatherShadingController script;
        private SerializedProperty wetness;
        private SerializedProperty snowAmount;

        private void OnEnable()
        {
            script = (WeatherShadingController)target;
            wetness = serializedObject.FindProperty("wetness");
            snowAmount = serializedObject.FindProperty("snowAmount");
        }

        public override void OnInspectorGUI()
        {
            UI.DrawHeader();
            
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            
            var showProperties = true;
            #if COZY3
            EditorGUILayout.HelpBox("Cozy 3 is installed, this component is not needed.", MessageType.Info);

            if (StylizedGrassEditor.IsCozyInteractionSetup() == false)
            {
                EditorGUILayout.HelpBox("Snow/wetness hasn't been set up in Cozy's Interaction module. Use the installation window to automatically do this.", MessageType.Error);
                if (GUILayout.Button("Open installer", EditorStyles.miniButton))
                {
                    HelpWindow.ShowWindow(true);
                }
            }
            else
            {
                showProperties = false;
            }
            #elif ENVIRO_3
            if (script.UsingEnviro())
            {
                EditorGUILayout.HelpBox("Enviro is installed, wetness and snow are fetched from its weather system." +
                                        "\n\n" +
                                        "Keep this component active to sync it with the grass shader", MessageType.Info);
                showProperties = false;
            }
            else
            {
                EditorGUILayout.HelpBox("Enviro is installed, but no Enviro instance is active." +
                                        "\n\n" +
                                        "Keep this component active to sync it with the grass shader", MessageType.Info);

            }
            #endif

            if (showProperties)
            {
                EditorGUILayout.PropertyField(wetness);
                EditorGUILayout.PropertyField(snowAmount);
            }

            if (EditorGUI.EndChangeCheck())
            {
                script.SetShaderProperties();
                serializedObject.ApplyModifiedProperties();
            }
            
            UI.DrawFooter();
        }
    }
}