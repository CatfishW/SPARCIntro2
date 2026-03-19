using UnityEngine;

namespace Blocks.Gameplay.Core.Customization
{
    internal static class CharacterRigUtility
    {
        public static bool TryApplyPreset(
            Transform modelRoot,
            Animator targetAnimator,
            GameObject sourcePrefab,
            out GameObject instantiatedModel,
            int previewLayer = -1,
            bool stripLights = true)
        {
            instantiatedModel = null;

            if (modelRoot == null || targetAnimator == null || sourcePrefab == null)
            {
                return false;
            }

            ClearChildren(modelRoot);

            instantiatedModel = Object.Instantiate(sourcePrefab, modelRoot, false);
            instantiatedModel.name = "ModelData";
            instantiatedModel.transform.localPosition = Vector3.zero;
            instantiatedModel.transform.localRotation = Quaternion.identity;
            instantiatedModel.transform.localScale = Vector3.one;

            if (stripLights)
            {
                StripLightingComponents(instantiatedModel.transform);
            }

            if (previewLayer >= 0)
            {
                SetLayerRecursively(instantiatedModel.transform, previewLayer);
                ConfigureRenderers(instantiatedModel.transform);
            }

            var sourceAnimator = instantiatedModel.GetComponent<Animator>();
            if (sourceAnimator == null)
            {
                sourceAnimator = instantiatedModel.GetComponentInChildren<Animator>(true);
            }

            if (sourceAnimator != null)
            {
                if (sourceAnimator.avatar != null)
                {
                    targetAnimator.avatar = sourceAnimator.avatar;
                }

                if (targetAnimator.runtimeAnimatorController == null && sourceAnimator.runtimeAnimatorController != null)
                {
                    targetAnimator.runtimeAnimatorController = sourceAnimator.runtimeAnimatorController;
                }

                DestroySmart(sourceAnimator);
            }

            targetAnimator.enabled = true;
            targetAnimator.Rebind();
            targetAnimator.Update(0f);
            return true;
        }

        public static bool TryCalculateBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root != null ? root.GetComponentsInChildren<Renderer>(true) : null;
            if (renderers == null || renderers.Length == 0)
            {
                bounds = root != null ? new Bounds(root.transform.position, Vector3.one * 0.25f) : new Bounds(Vector3.zero, Vector3.one * 0.25f);
                return false;
            }

            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return true;
        }

        public static void ClearChildren(Transform root)
        {
            if (root == null)
            {
                return;
            }

            for (int index = root.childCount - 1; index >= 0; index--)
            {
                var child = root.GetChild(index);
                if (child != null)
                {
                    DestroySmart(child.gameObject);
                }
            }
        }

        public static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null)
            {
                return;
            }

            root.gameObject.layer = layer;
            for (int index = 0; index < root.childCount; index++)
            {
                SetLayerRecursively(root.GetChild(index), layer);
            }
        }

        public static void ConfigureRenderers(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
        }

        public static void StripLightingComponents(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var lights = root.GetComponentsInChildren<Light>(true);
            for (int index = lights.Length - 1; index >= 0; index--)
            {
                var light = lights[index];
                if (light != null)
                {
                    DestroySmart(light);
                }
            }
        }

        private static void DestroySmart(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
