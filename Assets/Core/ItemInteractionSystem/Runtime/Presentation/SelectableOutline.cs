using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ItemInteraction
{
    [DisallowMultipleComponent]
    public class SelectableOutline : MonoBehaviour
    {
        [SerializeField] private Color outlineColor = Color.white;
        [SerializeField, Min(0.001f)] private float outlineWidth = 0.02f;
        [SerializeField] private bool visibleOnStart;

        private readonly List<Renderer> outlineRenderers = new List<Renderer>();
        private readonly List<GameObject> cloneObjects = new List<GameObject>();
        private Material outlineMaterial;
        private MaterialPropertyBlock propertyBlock;
        private bool built;

        public Color OutlineColor
        {
            get => outlineColor;
            set
            {
                outlineColor = value;
                ApplyProperties();
            }
        }

        public float OutlineWidth
        {
            get => outlineWidth;
            set
            {
                outlineWidth = Mathf.Max(0.001f, value);
                ApplyProperties();
            }
        }

        private void Awake()
        {
            EnsureBuilt();
            SetVisible(visibleOnStart);
        }

        private void OnEnable()
        {
            EnsureBuilt();
        }

        private void OnValidate()
        {
            outlineWidth = Mathf.Max(0.001f, outlineWidth);
            ApplyProperties();
        }

        public void SetVisible(bool visible)
        {
            EnsureBuilt();
            for (int index = 0; index < outlineRenderers.Count; index++)
            {
                if (outlineRenderers[index] != null)
                {
                    outlineRenderers[index].enabled = visible;
                }
            }
        }

        public void Rebuild()
        {
            CleanupClones();
            built = false;
            EnsureBuilt();
        }

        private void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            var shader = Shader.Find("ItemInteraction/InteractionOutline");
            if (shader == null)
            {
                Debug.LogError("ItemInteraction: outline shader not found.", this);
                return;
            }

            outlineMaterial = new Material(shader)
            {
                name = $"{name}_OutlineRuntimeMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };

            propertyBlock = new MaterialPropertyBlock();

            var meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int index = 0; index < meshRenderers.Length; index++)
            {
                var sourceRenderer = meshRenderers[index];
                if (sourceRenderer == null || sourceRenderer.GetComponent<OutlineCloneMarker>() != null)
                {
                    continue;
                }

                var meshFilter = sourceRenderer.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    continue;
                }

                var clone = new GameObject("__Outline", typeof(OutlineCloneMarker), typeof(MeshFilter), typeof(MeshRenderer));
                clone.hideFlags = HideFlags.HideAndDontSave;
                clone.transform.SetParent(sourceRenderer.transform, false);
                clone.layer = sourceRenderer.gameObject.layer;

                var cloneFilter = clone.GetComponent<MeshFilter>();
                cloneFilter.sharedMesh = meshFilter.sharedMesh;

                var cloneRenderer = clone.GetComponent<MeshRenderer>();
                cloneRenderer.sharedMaterial = outlineMaterial;
                cloneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cloneRenderer.receiveShadows = false;
                cloneRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                cloneRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                cloneRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                cloneRenderer.enabled = false;

                outlineRenderers.Add(cloneRenderer);
                cloneObjects.Add(clone);
            }

            var skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int index = 0; index < skinnedRenderers.Length; index++)
            {
                var sourceRenderer = skinnedRenderers[index];
                if (sourceRenderer == null || sourceRenderer.GetComponent<OutlineCloneMarker>() != null || sourceRenderer.sharedMesh == null)
                {
                    continue;
                }

                var clone = new GameObject("__OutlineSkinned", typeof(OutlineCloneMarker), typeof(SkinnedMeshRenderer));
                clone.hideFlags = HideFlags.HideAndDontSave;
                clone.transform.SetParent(sourceRenderer.transform, false);
                clone.layer = sourceRenderer.gameObject.layer;

                var cloneRenderer = clone.GetComponent<SkinnedMeshRenderer>();
                cloneRenderer.sharedMesh = sourceRenderer.sharedMesh;
                cloneRenderer.bones = sourceRenderer.bones;
                cloneRenderer.rootBone = sourceRenderer.rootBone;
                cloneRenderer.sharedMaterial = outlineMaterial;
                cloneRenderer.localBounds = sourceRenderer.localBounds;
                cloneRenderer.updateWhenOffscreen = true;
                cloneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cloneRenderer.receiveShadows = false;
                cloneRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                cloneRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                cloneRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                cloneRenderer.enabled = false;

                outlineRenderers.Add(cloneRenderer);
                cloneObjects.Add(clone);
            }

            ApplyProperties();
            built = true;
        }

        private void ApplyProperties()
        {
            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            for (int index = 0; index < outlineRenderers.Count; index++)
            {
                var renderer = outlineRenderers[index];
                if (renderer == null)
                {
                    continue;
                }

                propertyBlock.Clear();
                propertyBlock.SetColor("_OutlineColor", outlineColor);
                propertyBlock.SetFloat("_OutlineWidth", outlineWidth);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void CleanupClones()
        {
            for (int index = cloneObjects.Count - 1; index >= 0; index--)
            {
                var clone = cloneObjects[index];
                if (clone == null)
                {
                    continue;
                }

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(clone);
                }
                else
#endif
                {
                    Destroy(clone);
                }
            }

            cloneObjects.Clear();
            outlineRenderers.Clear();

            if (outlineMaterial != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(outlineMaterial);
                }
                else
#endif
                {
                    Destroy(outlineMaterial);
                }
            }
        }

        private void OnDestroy()
        {
            CleanupClones();
        }
    }

    internal sealed class OutlineCloneMarker : MonoBehaviour
    {
    }
}
