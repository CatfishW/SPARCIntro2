using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-850)]
    public sealed class ClassroomCollisionBootstrapper : MonoBehaviour
    {
        [SerializeField] private string classroomRootPath = "Classroom";
        [SerializeField] private string boundaryRootName = "ClassroomBoundaryBlockers";
        [SerializeField] private bool generateMeshColliders = true;
        [SerializeField] private bool generateBoundaryBlockers = true;
        [SerializeField] private bool generateSafetyFloor = true;
        [SerializeField, Min(0.05f)] private float boundaryThickness = 0.45f;
        [SerializeField, Min(1f)] private float boundaryHeight = 3f;
        [SerializeField, Min(0f)] private float boundaryInset = 0.12f;
        [SerializeField, Min(0f)] private float floorLift = 0.02f;
        [SerializeField, Min(0.05f)] private float minRenderableSize = 0.08f;
        [SerializeField] private string[] colliderNameKeywords =
        {
            "desk",
            "chair",
            "table",
            "teacherdesk",
            "etagere",
            "locker",
            "board",
            "clock",
            "door",
            "wall",
            "window",
            "floor",
            "ceiling"
        };

        private void Awake()
        {
            EnsureCollisionSetup();
        }

        public void EnsureCollisionSetup()
        {
            var classroomRoot = GameObject.Find(classroomRootPath);
            if (classroomRoot == null)
            {
                return;
            }

            if (!TryBuildBounds(classroomRoot.transform, out var classroomBounds))
            {
                return;
            }

            if (generateMeshColliders)
            {
                EnsureMeshColliders(classroomRoot.transform);
            }

            if (generateBoundaryBlockers)
            {
                EnsureBoundaryBlockers(classroomRoot.transform, classroomBounds);
            }
        }

        private void EnsureMeshColliders(Transform root)
        {
            if (ShouldSkipRuntimeMeshColliders())
            {
                return;
            }

            var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            for (var index = 0; index < meshFilters.Length; index++)
            {
                var meshFilter = meshFilters[index];
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    continue;
                }

                var gameObject = meshFilter.gameObject;
                if (!ShouldAttachMeshCollider(gameObject))
                {
                    continue;
                }

                if (gameObject.GetComponent<Collider>() != null)
                {
                    continue;
                }

                if (!meshFilter.sharedMesh.isReadable)
                {
                    continue;
                }

                var meshCollider = gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilter.sharedMesh;

                var rigidbody = gameObject.GetComponent<Rigidbody>();
                if (rigidbody != null && !rigidbody.isKinematic)
                {
                    meshCollider.convex = true;
                }
            }
        }

        private bool ShouldAttachMeshCollider(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return false;
            }

            var renderers = gameObject.GetComponents<Renderer>();
            var hasValidRenderer = false;
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                var size = renderer.bounds.size;
                if (size.x < minRenderableSize && size.y < minRenderableSize && size.z < minRenderableSize)
                {
                    continue;
                }

                hasValidRenderer = true;
                break;
            }

            if (!hasValidRenderer)
            {
                return false;
            }

            var name = gameObject.name.ToLowerInvariant();
            for (var index = 0; index < colliderNameKeywords.Length; index++)
            {
                var keyword = colliderNameKeywords[index];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (name.Contains(keyword.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldSkipRuntimeMeshColliders()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#elif UNITY_EDITOR
            return EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL;
#else
            return false;
#endif
        }

        private void EnsureBoundaryBlockers(Transform classroomRoot, Bounds bounds)
        {
            if (classroomRoot == null)
            {
                return;
            }

            var floorY = ResolveFloorY(classroomRoot, bounds);
            var blockers = classroomRoot.Find(boundaryRootName);
            if (blockers == null)
            {
                var blockerObject = new GameObject(boundaryRootName);
                blockers = blockerObject.transform;
                blockers.SetParent(classroomRoot, false);
            }

            var spanX = Mathf.Max(1f, bounds.size.x - (boundaryInset * 2f));
            var spanZ = Mathf.Max(1f, bounds.size.z - (boundaryInset * 2f));
            var wallHeight = Mathf.Max(boundaryHeight, bounds.size.y + 0.6f);
            var centerY = floorY + (wallHeight * 0.5f);

            CreateOrUpdateBlocker(
                blockers,
                "NorthWall",
                new Vector3(bounds.center.x, centerY, bounds.max.z + (boundaryThickness * 0.5f) - boundaryInset),
                new Vector3(spanX, wallHeight, boundaryThickness));

            CreateOrUpdateBlocker(
                blockers,
                "SouthWall",
                new Vector3(bounds.center.x, centerY, bounds.min.z - (boundaryThickness * 0.5f) + boundaryInset),
                new Vector3(spanX, wallHeight, boundaryThickness));

            CreateOrUpdateBlocker(
                blockers,
                "EastWall",
                new Vector3(bounds.max.x + (boundaryThickness * 0.5f) - boundaryInset, centerY, bounds.center.z),
                new Vector3(boundaryThickness, wallHeight, spanZ));

            CreateOrUpdateBlocker(
                blockers,
                "WestWall",
                new Vector3(bounds.min.x - (boundaryThickness * 0.5f) + boundaryInset, centerY, bounds.center.z),
                new Vector3(boundaryThickness, wallHeight, spanZ));

            if (generateSafetyFloor)
            {
                CreateOrUpdateBlocker(
                    blockers,
                    "SafetyFloor",
                    new Vector3(bounds.center.x, floorY - 0.15f, bounds.center.z),
                    new Vector3(bounds.size.x + 8f, 0.3f, bounds.size.z + 8f));
            }
        }

        private static void CreateOrUpdateBlocker(Transform root, string name, Vector3 worldCenter, Vector3 size)
        {
            if (root == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var blocker = root.Find(name);
            if (blocker == null)
            {
                var blockerObject = new GameObject(name);
                blocker = blockerObject.transform;
                blocker.SetParent(root, false);
            }

            blocker.position = worldCenter;
            blocker.rotation = Quaternion.identity;

            var collider = blocker.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = blocker.gameObject.AddComponent<BoxCollider>();
            }

            collider.isTrigger = false;
            collider.size = size;
            collider.center = Vector3.zero;
        }

        private static float ResolveFloorY(Transform classroomRoot, Bounds bounds)
        {
            var colliders = classroomRoot.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (collider == null)
                {
                    continue;
                }

                if (!collider.name.Contains("floor", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return collider.bounds.max.y;
            }

            return bounds.min.y;
        }

        private bool TryBuildBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
            {
                return false;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                return false;
            }

            bounds.min += new Vector3(0f, floorLift, 0f);
            return true;
        }
    }
}
