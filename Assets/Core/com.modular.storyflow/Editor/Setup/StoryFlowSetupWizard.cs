using UnityEditor;
using UnityEngine;

namespace ModularStoryFlow.Editor.Setup
{
    public sealed class StoryFlowSetupWizard : EditorWindow
    {
        private string rootFolder = "Assets/StoryFlow";
        private bool createPlayerPrefab = true;
        private bool createTimelineBridgePrefab = true;
        private bool scanProjectAssets = true;

        [MenuItem("Tools/Modular Story Flow/Setup Wizard", priority = 1)]
        public static void OpenWindow()
        {
            GetWindow<StoryFlowSetupWizard>("Story Flow Setup");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Modular Story Flow Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This wizard creates a ready-to-use project configuration, channels, catalogs, save provider, and optional prefabs. It can also auto-register any existing story assets in the project.",
                MessageType.Info);

            GUILayout.Space(8f);

            rootFolder = EditorGUILayout.TextField("Root Folder", rootFolder);
            createPlayerPrefab = EditorGUILayout.ToggleLeft("Create StoryFlowPlayer prefab", createPlayerPrefab);
            createTimelineBridgePrefab = EditorGUILayout.ToggleLeft("Create StoryTimelineBridge prefab", createTimelineBridgePrefab);
            scanProjectAssets = EditorGUILayout.ToggleLeft("Auto-scan and register existing Story Flow assets", scanProjectAssets);

            GUILayout.Space(12f);

            if (GUILayout.Button("One-Click Generate", GUILayout.Height(34f)))
            {
                var generated = StoryFlowProjectGenerator.Generate(rootFolder, createPlayerPrefab, createTimelineBridgePrefab, scanProjectAssets);
                Selection.activeObject = generated.Config;
                EditorGUIUtility.PingObject(generated.Config);

                EditorUtility.DisplayDialog(
                    "Story Flow Setup Complete",
                    $"Project assets were generated under '{rootFolder}'.\n\nConfig: {AssetDatabase.GetAssetPath(generated.Config)}",
                    "OK");
            }
        }
    }
}
