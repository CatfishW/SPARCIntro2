using Blocks.Gameplay.Core;
using UnityEngine;

namespace Blocks.Gameplay.Core.Customization
{
    internal static class CharacterCustomizationModelSwapper
    {
        public static bool TryApplyPreset(CoreAnimator coreAnimator, CharacterCustomizationPreset preset)
        {
            if (coreAnimator == null || preset.characterPrefab == null)
            {
                return false;
            }

            Transform modelContainer = coreAnimator.transform;
            Animator targetAnimator = coreAnimator.BoundAnimator;
            if (targetAnimator == null)
            {
                targetAnimator = coreAnimator.GetComponent<Animator>();
                if (targetAnimator == null)
                {
                    targetAnimator = coreAnimator.gameObject.AddComponent<Animator>();
                }
            }

            RuntimeAnimatorController currentController = targetAnimator.runtimeAnimatorController;

            GameObject retiredHierarchy = DetachExistingChildren(modelContainer);

            GameObject instance = Object.Instantiate(preset.characterPrefab, modelContainer);
            instance.name = "ModelData";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            StripToVisuals(instance.transform);
            SetLayerRecursively(instance.transform, modelContainer.gameObject.layer);

            Animator sourceAnimator = instance.GetComponent<Animator>() ?? instance.GetComponentInChildren<Animator>(true);
            Avatar avatar = sourceAnimator != null ? sourceAnimator.avatar : null;
            RemoveInstanceAnimators(instance.transform);

            coreAnimator.RebindAnimator(targetAnimator, avatar, currentController);

            if (retiredHierarchy != null)
            {
                Object.Destroy(retiredHierarchy);
            }

            return true;
        }

        private static GameObject DetachExistingChildren(Transform root)
        {
            if (root == null || root.childCount == 0)
            {
                return null;
            }

            GameObject parkingRoot = new GameObject("__RetiredCharacterModel");
            parkingRoot.hideFlags = HideFlags.HideAndDontSave;

            for (int index = root.childCount - 1; index >= 0; index--)
            {
                Transform child = root.GetChild(index);
                if (child == null)
                {
                    continue;
                }

                child.SetParent(parkingRoot.transform, false);
                child.gameObject.SetActive(false);
            }

            return parkingRoot;
        }

        private static void StripToVisuals(Transform root)
        {
            var components = root.GetComponents<Component>();
            for (int index = components.Length - 1; index >= 0; index--)
            {
                var component = components[index];
                if (component == null)
                {
                    continue;
                }

                if (component is Transform ||
                    component is Renderer ||
                    component is MeshFilter ||
                    component is LODGroup ||
                    component is Animator)
                {
                    continue;
                }

                Object.Destroy(component);
            }

            for (int childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                StripToVisuals(root.GetChild(childIndex));
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

        private static void RemoveInstanceAnimators(Transform root)
        {
            var animators = root.GetComponentsInChildren<Animator>(true);
            for (int index = 0; index < animators.Length; index++)
            {
                if (animators[index] == null)
                {
                    continue;
                }

                animators[index].enabled = false;
                Object.Destroy(animators[index]);
            }
        }
    }
}
