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

            ApplyWebGlCacheBusting(outputPath);

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
            // WebGL stability is more important than aggressive stripping for this build.
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.WebGL, ManagedStrippingLevel.Low);

#pragma warning disable CS0618
            PlayerSettings.SetPropertyInt("webGLExceptionSupport", 0, BuildTargetGroup.WebGL); // None
            PlayerSettings.SetPropertyInt("webGLShowDiagnostics", 0, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLInitialMemorySize", 256, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLMaximumMemorySize", 2048, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLMemoryGrowthMode", 2, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLMemoryLinearGrowthStep", 32, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLMemoryGeometricGrowthCap", 128, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLNameFilesAsHashes", 1, BuildTargetGroup.WebGL);
            PlayerSettings.SetPropertyInt("webGLDataCaching", 0, BuildTargetGroup.WebGL);
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
            // Probe volumes rely on compute paths that are not available on WebGL and can also
            // blow up Linux batch builds before the player is even produced.
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_LightProbeSystem", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_UseAdaptivePerformance", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_GPUResidentDrawerEnableOcclusionCullingInCameras", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterWriteRenderingLayers", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterHDROutput", 0);
            pipelineChanged |= TrySetIntProperty(pipelineObject, "m_PrefilterAlphaOutput", 0);
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

        private static void ApplyWebGlCacheBusting(string outputPath)
        {
            var indexPath = Path.Combine(outputPath, "index.html");
            if (!File.Exists(indexPath))
            {
                Debug.LogWarning($"[Intro2WebGlBuilder] WebGL index file not found for cache-busting: {indexPath}");
                return;
            }

            var version = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var html = File.ReadAllText(indexPath);
            html = html.Replace("var loaderUrl = buildUrl + \"/Intro2.loader.js\";", $"var loaderUrl = buildUrl + \"/Intro2.loader.js?v={version}\";");
            html = html.Replace("dataUrl: buildUrl + \"/Intro2.data\",", $"dataUrl: buildUrl + \"/Intro2.data?v={version}\",");
            html = html.Replace("frameworkUrl: buildUrl + \"/Intro2.framework.js\",", $"frameworkUrl: buildUrl + \"/Intro2.framework.js?v={version}\",");
            html = html.Replace("codeUrl: buildUrl + \"/Intro2.wasm\",", $"codeUrl: buildUrl + \"/Intro2.wasm?v={version}\",");

            const string bannerLine = "showBanner: unityShowBanner,";
            if (html.Contains(bannerLine) && !html.Contains("cacheControl: function(url)"))
            {
                html = html.Replace(
                    bannerLine,
                    "showBanner: unityShowBanner,\n      cacheControl: function(url) { return 'no-store'; },");
            }

            File.WriteAllText(indexPath, html);
            File.WriteAllText(Path.Combine(outputPath, "build-version.txt"), version);
            Debug.Log($"[Intro2WebGlBuilder] Applied WebGL cache-busting stamp {version}.");
        }
    }
}
