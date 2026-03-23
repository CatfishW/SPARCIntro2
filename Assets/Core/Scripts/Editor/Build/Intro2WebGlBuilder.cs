using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Blocks.Gameplay.Core.Editor.Build
{
    public static class Intro2WebGlBuilder
    {
        private static readonly string[] RequiredScenePaths =
        {
            "Assets/Core/TestScenes/BedRoomIntroScene.unity",
            "Assets/Core/TestScenes/ClassroomArrivalScene.unity",
            "Assets/LabScene.unity",
            "Assets/Core/TestScenes/LabTransitionScene.unity"
        };

        private const string OutputRelativePath = "Builds/WebGL/Intro2";

        [MenuItem("Tools/Build/Build Intro2 WebGL Release")]
        public static void BuildIntro2WebGLRelease()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var outputPath = Path.Combine(projectRoot, OutputRelativePath);
            BuildIntro2WebGLReleaseInternal(outputPath);
        }

        public static void BuildIntro2WebGLReleaseCli()
        {
            BuildIntro2WebGLRelease();
        }

        private static void BuildIntro2WebGLReleaseInternal(string outputPath)
        {
            EnsureRequiredScenesEnabled();
            ConfigureWebGlPlayerSettings();
            ConfigureReleaseQuality();

            Directory.CreateDirectory(outputPath);

            var enabledScenes = RequiredScenePaths
                .Where(File.Exists)
                .ToArray();

            if (enabledScenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes are available for WebGL build.");
            }

            var buildOptions = new BuildPlayerOptions
            {
                scenes = enabledScenes,
                target = BuildTarget.WebGL,
                locationPathName = outputPath,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(buildOptions);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"WebGL build failed: {report.summary.result}");
            }

            Debug.Log($"[Intro2WebGlBuilder] WebGL build succeeded: {outputPath}");
        }

        private static void EnsureRequiredScenesEnabled()
        {
            var scenes = EditorBuildSettings.scenes?.ToList() ?? new System.Collections.Generic.List<EditorBuildSettingsScene>();

            for (var index = 0; index < RequiredScenePaths.Length; index++)
            {
                var requiredPath = RequiredScenePaths[index];
                if (!File.Exists(requiredPath))
                {
                    throw new FileNotFoundException($"Required scene missing: {requiredPath}", requiredPath);
                }

                var existingIndex = scenes.FindIndex(scene =>
                    scene != null &&
                    string.Equals(scene.path, requiredPath, StringComparison.OrdinalIgnoreCase));

                if (existingIndex >= 0)
                {
                    scenes[existingIndex].enabled = true;
                }
                else
                {
                    scenes.Add(new EditorBuildSettingsScene(requiredPath, true));
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void ConfigureWebGlPlayerSettings()
        {
            PlayerSettings.defaultWebScreenWidth = 1920;
            PlayerSettings.defaultWebScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.stripEngineCode = true;

#pragma warning disable CS0618
            PlayerSettings.SetPropertyInt("webGLCompressionFormat", 1, BuildTargetGroup.WebGL); // Brotli
            PlayerSettings.SetPropertyInt("webGLExceptionSupport", 0, BuildTargetGroup.WebGL); // None
            PlayerSettings.SetPropertyInt("webGLShowDiagnostics", 0, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLInitialMemorySize", 256, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLMaximumMemorySize", 2048, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLMemoryGrowthMode", 2, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLMemoryLinearGrowthStep", 32, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLMemoryGeometricGrowthCap", 128, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLNameFilesAsHashes", 1, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLDataCaching", 1, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLDebugSymbols", 0, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLThreadsSupport", 0, BuildTargetGroup.WebGL);
#pragma warning restore CS0618
        }

        private static void ConfigureReleaseQuality()
        {
            var qualityNames = QualitySettings.names ?? Array.Empty<string>();
            var pcQualityIndex = Array.FindIndex(qualityNames, name =>
                string.Equals(name, "PC", StringComparison.OrdinalIgnoreCase));
            if (pcQualityIndex >= 0)
            {
                QualitySettings.SetQualityLevel(pcQualityIndex, true);
            }

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 24f);
            QualitySettings.lodBias = Mathf.Max(QualitySettings.lodBias, 1.15f);
            QualitySettings.antiAliasing = Mathf.Max(QualitySettings.antiAliasing, 2);
        }
    }
}
