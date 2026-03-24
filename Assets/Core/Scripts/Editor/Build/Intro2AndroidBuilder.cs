using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Blocks.Gameplay.Core.Editor.Build
{
    public static class Intro2AndroidBuilder
    {
        private static readonly string[] RequiredScenePaths =
        {
            "Assets/Core/TestScenes/BedRoomIntroScene.unity",
            "Assets/Core/TestScenes/ClassroomArrivalScene.unity",
            "Assets/LabScene.unity",
            "Assets/Core/TestScenes/LabTransitionScene.unity"
        };

        private static readonly string[] OptionalScenePaths =
        {
            "Assets/Core/TestScenes/LabMiniEntryScene.unity"
        };

        private const string OutputRelativePath = "Builds/Android/Intro2/Intro2.apk";

        [MenuItem("Tools/Build/Build Intro2 Android APK Release")]
        public static void BuildIntro2AndroidRelease()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var outputPath = Path.Combine(projectRoot, OutputRelativePath);
            BuildIntro2AndroidReleaseInternal(outputPath);
        }

        public static void BuildIntro2AndroidReleaseCli()
        {
            BuildIntro2AndroidRelease();
        }

        private static void BuildIntro2AndroidReleaseInternal(string outputPath)
        {
            EnsureRequiredScenesEnabled();
            ConfigureAndroidPlayerSettings();

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var enabledScenes = RequiredScenePaths
                .Concat(OptionalScenePaths)
                .Where(File.Exists)
                .ToArray();

            if (enabledScenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes are available for Android build.");
            }

            EditorUserBuildSettings.buildAppBundle = false;

            var buildOptions = new BuildPlayerOptions
            {
                scenes = enabledScenes,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                locationPathName = outputPath,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(buildOptions);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Android build failed: {report.summary.result}");
            }

            Debug.Log($"[Intro2AndroidBuilder] Android build succeeded: {outputPath}");
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

            for (var index = 0; index < OptionalScenePaths.Length; index++)
            {
                var optionalPath = OptionalScenePaths[index];
                if (!File.Exists(optionalPath))
                {
                    continue;
                }

                var existingIndex = scenes.FindIndex(scene =>
                    scene != null &&
                    string.Equals(scene.path, optionalPath, StringComparison.OrdinalIgnoreCase));

                if (existingIndex >= 0)
                {
                    scenes[existingIndex].enabled = true;
                }
                else
                {
                    scenes.Add(new EditorBuildSettingsScene(optionalPath, true));
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void ConfigureAndroidPlayerSettings()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.stripEngineCode = true;

#pragma warning disable CS0618
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.useCustomKeystore = false;
#pragma warning restore CS0618
        }
    }
}
