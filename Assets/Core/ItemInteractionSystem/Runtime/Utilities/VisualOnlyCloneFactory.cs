using UnityEngine;
using UnityEngine.Rendering;

namespace ItemInteraction
{
    internal static class VisualOnlyCloneFactory
    {
        public static GameObject CreateInspectionClone(InteractableItem source, InspectionPresentation presentation, int previewLayer)
        {
            GameObject clone;

            if (presentation != null && presentation.customInspectionPrefab != null)
            {
                clone = Object.Instantiate(presentation.customInspectionPrefab);
            }
            else
            {
                var root = presentation != null && presentation.sourceRootOverride != null
                    ? presentation.sourceRootOverride.gameObject
                    : source.GetInspectionSourceRoot().gameObject;

                clone = Object.Instantiate(root);
                StripToRenderOnly(clone.transform);
            }

            // The preview rig already provides its own key light, so authored lights would overexpose the inspection clone.
            StripLightingComponents(clone.transform);
            SetLayerRecursively(clone.transform, previewLayer);
            ConfigureRenderers(clone.transform);
            clone.name = $"{source.name}_InspectionClone";
            return clone;
        }

        private static void StripToRenderOnly(Transform root)
        {
            var components = root.GetComponents<Component>();
            for (int index = components.Length - 1; index >= 0; index--)
            {
                var component = components[index];
                if (component == null)
                {
                    continue;
                }

                if (component is Transform || component is MeshFilter || component is MeshRenderer || component is SkinnedMeshRenderer)
                {
                    continue;
                }

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    Object.DestroyImmediate(component);
                }
                else
#endif
                {
                    Object.Destroy(component);
                }
            }

            for (int childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                StripToRenderOnly(root.GetChild(childIndex));
            }
        }

        private static void ConfigureRenderers(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                renderer.gameObject.hideFlags = HideFlags.DontSave;
            }
        }

        private static void StripLightingComponents(Transform root)
        {
            var lights = root.GetComponentsInChildren<Light>(true);
            for (int index = lights.Length - 1; index >= 0; index--)
            {
                var light = lights[index];
                if (light == null)
                {
                    continue;
                }

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    Object.DestroyImmediate(light);
                }
                else
#endif
                {
                    Object.Destroy(light);
                }
            }
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                SetLayerRecursively(root.GetChild(childIndex), layer);
            }
        }
    }
}
