using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

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

        private static readonly string[] OptionalScenePaths =
        {
            "Assets/Core/TestScenes/LabMiniEntryScene.unity"
        };

        private static readonly string[] WebGlFallbackShaderNames =
        {
            "Universal Render Pipeline/Unlit",
            "Unlit/Texture",
            "Unlit/Color",
            "Sprites/Default",
            "ItemInteraction/InteractionOutline"
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
            ConfigureWebGlRendererCompatibility();
            EnsureWebGlFallbackShadersIncluded();
            ConfigureReleaseQuality();

            Directory.CreateDirectory(outputPath);

            var enabledScenes = RequiredScenePaths
                .Concat(OptionalScenePaths)
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

        private static void ConfigureWebGlRendererCompatibility()
        {
            var renderPipeline = GraphicsSettings.defaultRenderPipeline;
            if (renderPipeline == null)
            {
                Debug.LogWarning("[Intro2WebGlBuilder] No default render pipeline configured. Skipping renderer compatibility tuning.");
                return;
            }

            var pipelineObject = new SerializedObject(renderPipeline);
            var rendererDataList = pipelineObject.FindProperty("m_RendererDataList");
            if (rendererDataList == null || !rendererDataList.isArray)
            {
                Debug.LogWarning("[Intro2WebGlBuilder] URP asset has no renderer data list. Skipping renderer compatibility tuning.");
                return;
            }

            var pipelineChanged = false;
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterWriteRenderingLayers", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterScreenSpaceIrradiance", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterNativeRenderPass", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterSSAODepthNormals", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterSSAOSourceDepthLow", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterSSAOSourceDepthMedium", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterSSAOSourceDepthHigh", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterSSAOInterleaved", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterSSAOBlueNoise", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterSSAOSampleCountLow", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterSSAOSampleCountMedium", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterSSAOSampleCountHigh", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterDBufferMRT1", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterDBufferMRT2", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterDBufferMRT3", 0);

            if (pipelineObject.hasModifiedProperties)
            {
                pipelineObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(renderPipeline);
            }

            var rendererChanged = false;

            for (var index = 0; index < rendererDataList.arraySize; index++)
            {
                var element = rendererDataList.GetArrayElementAtIndex(index);
                var rendererData = element.objectReferenceValue;
                if (rendererData == null)
                {
                    continue;
                }

                var rendererObject = new SerializedObject(rendererData);
                var renderingMode = rendererObject.FindProperty("m_RenderingMode");
                if (renderingMode != null && renderingMode.intValue != 0)
                {
                    renderingMode.intValue = 0; // Forward (WebGL-safe fallback)
                    rendererChanged = true;
                }

                var nativeRenderPass = rendererObject.FindProperty("m_UseNativeRenderPass");
                if (nativeRenderPass != null && nativeRenderPass.intValue != 0)
                {
                    nativeRenderPass.intValue = 0; // Avoid native render pass path on WebGL.
                    rendererChanged = true;
                }

                if (rendererObject.hasModifiedProperties)
                {
                    rendererObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(rendererData);
                }
            }

            if (pipelineChanged || rendererChanged)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[Intro2WebGlBuilder] Applied WebGL renderer compatibility tuning (pipeline prefilter pruning + Forward renderer + Native Render Pass disabled).");
            }
        }

        private static bool TrySetIntProperty(SerializedObject serializedObject, string propertyName, int value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Integer || property.intValue == value)
            {
                return false;
            }

            property.intValue = value;
            return true;
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

        private static void EnsureWebGlFallbackShadersIncluded()
        {
            var graphicsSettingsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")
                .FirstOrDefault();
            if (graphicsSettingsAsset == null)
            {
                Debug.LogWarning("[Intro2WebGlBuilder] Could not load GraphicsSettings.asset for fallback shader inclusion.");
                return;
            }

            var graphicsSettingsObject = new SerializedObject(graphicsSettingsAsset);
            var alwaysIncludedShaders = graphicsSettingsObject.FindProperty("m_AlwaysIncludedShaders");
            if (alwaysIncludedShaders == null || !alwaysIncludedShaders.isArray)
            {
                Debug.LogWarning("[Intro2WebGlBuilder] m_AlwaysIncludedShaders property not found. Skipping fallback shader inclusion.");
                return;
            }

            var changed = false;
            for (var shaderIndex = 0; shaderIndex < WebGlFallbackShaderNames.Length; shaderIndex++)
            {
                var shaderName = WebGlFallbackShaderNames[shaderIndex];
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"[Intro2WebGlBuilder] Fallback shader not found in editor: '{shaderName}'.");
                    continue;
                }

                var alreadyIncluded = false;
                for (var index = 0; index < alwaysIncludedShaders.arraySize; index++)
                {
                    var existing = alwaysIncludedShaders.GetArrayElementAtIndex(index).objectReferenceValue as Shader;
                    if (existing == shader)
                    {
                        alreadyIncluded = true;
                        break;
                    }
                }

                if (alreadyIncluded)
                {
                    continue;
                }

                alwaysIncludedShaders.InsertArrayElementAtIndex(alwaysIncludedShaders.arraySize);
                alwaysIncludedShaders.GetArrayElementAtIndex(alwaysIncludedShaders.arraySize - 1).objectReferenceValue = shader;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            graphicsSettingsObject.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Intro2WebGlBuilder] Added WebGL fallback shaders to GraphicsSettings always-included list.");
        }
    }
}
