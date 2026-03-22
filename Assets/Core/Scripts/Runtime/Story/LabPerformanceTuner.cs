using UnityEngine;
using UnityEngine.Rendering;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class LabPerformanceTuner : MonoBehaviour
    {
        [SerializeField] private bool applyOnEnable = true;
        [SerializeField, Min(20)] private int targetFrameRate = 120;
        [SerializeField, Range(0f, 120f)] private float maxShadowDistance = 12f;
        [SerializeField, Range(0.25f, 1f)] private float lodBias = 0.65f;
        [SerializeField] private bool disableRealtimeReflectionProbes = true;
        [SerializeField] private bool disableAdditionalPerPixelLights = true;
        [SerializeField] private bool disableRealtimeShadowsOnAdditionalLights = true;

        private void OnEnable()
        {
            if (!applyOnEnable)
            {
                return;
            }

            Apply();
        }

        public void Apply()
        {
            Application.targetFrameRate = Mathf.Max(20, targetFrameRate);
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance <= 0f ? maxShadowDistance : QualitySettings.shadowDistance, maxShadowDistance);
            QualitySettings.lodBias = Mathf.Min(QualitySettings.lodBias <= 0f ? lodBias : QualitySettings.lodBias, lodBias);
            Shader.globalMaximumLOD = Mathf.Min(Shader.globalMaximumLOD <= 0 ? 300 : Shader.globalMaximumLOD, 300);

            ApplyPipelineTuning();
            TuneLights();

            if (!disableRealtimeReflectionProbes)
            {
                return;
            }

            var probes = FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < probes.Length; index++)
            {
                var probe = probes[index];
                if (probe == null || probe.mode != UnityEngine.Rendering.ReflectionProbeMode.Realtime)
                {
                    continue;
                }

                probe.enabled = false;
            }
        }

        private void ApplyPipelineTuning()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                return;
            }

            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < cameras.Length; index++)
            {
                var camera = cameras[index];
                if (camera == null)
                {
                    continue;
                }

                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.useOcclusionCulling = true;
            }
        }

        private void TuneLights()
        {
            var lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < lights.Length; index++)
            {
                var light = lights[index];
                if (light == null)
                {
                    continue;
                }

                if (light.type == LightType.Directional)
                {
                    light.shadows = LightShadows.Hard;
                    light.shadowResolution = LightShadowResolution.Medium;
                    continue;
                }

                if (disableAdditionalPerPixelLights)
                {
                    light.renderMode = LightRenderMode.ForceVertex;
                }

                if (disableRealtimeShadowsOnAdditionalLights)
                {
                    light.shadows = LightShadows.None;
                }
            }
        }
    }
}
