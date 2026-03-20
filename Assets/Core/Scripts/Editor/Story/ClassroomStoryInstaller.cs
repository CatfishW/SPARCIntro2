using System;
using Blocks.Gameplay.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Blocks.Gameplay.Core.Story.Editor
{
    public static class ClassroomStoryInstaller
    {
        private const string BedroomScenePath = "Assets/Core/TestScenes/BedRoomIntroScene.unity";
        private const string ClassroomScenePath = "Assets/Core/TestScenes/ClassroomArrivalScene.unity";
        private const string PrimarySpawnPadPath = "_Spawns/Pfb_SpawnPad";
        private const string SecondarySpawnPadPath = "_Spawns/Pfb_SpawnPad (1)";
        private const string NpcIdleControllerPath = "Assets/Core/Art/3D Casual Character/3D Characters Pro - Casual/AnimationController/AnimationController_Idle.controller";
        private const string NpcInteractionControllerPath = "Assets/Core/Art/3D Casual Character/3D Characters Pro - Casual/AnimationController/AnimationController_Interaction.controller";
        private const string NpcReactionControllerPath = "Assets/Core/Art/3D Casual Character/3D Characters Pro - Casual/AnimationController/AnimationController_Reaction.controller";
        private static readonly string[] ClassroomNpcNames = { "DrMira", "NiaPark", "TheoMercer" };
        private static readonly Vector3 TeacherStagePosition = new(0.95f, 0f, 3.15f);
        private static readonly Vector3 FriendStagePosition = new(-0.55f, 0f, 1.2f);
        private static readonly Vector3 SkepticStagePosition = new(-1.55f, 0f, 0.4f);
        private const float TeacherStageYaw = 188f;
        private const float FriendStageYaw = 24f;
        private const float SkepticStageYaw = 38f;

        [MenuItem("Tools/Blocks/Story/Sync Classroom Story Scene", priority = 303)]
        public static void Install()
        {
            var originalScenePath = SceneManager.GetActiveScene().path;
            EditorSceneManager.SaveOpenScenes();

            var classroomScene = EditorSceneManager.OpenScene(ClassroomScenePath, OpenSceneMode.Single);
            var referenceScene = EditorSceneManager.OpenScene(BedroomScenePath, OpenSceneMode.Additive);

            try
            {
                SyncScene(classroomScene, referenceScene);
                EditorSceneManager.MarkSceneDirty(classroomScene);
                EditorSceneManager.SaveScene(classroomScene);
            }
            finally
            {
                if (referenceScene.IsValid() && referenceScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(referenceScene, true);
                }

                if (!string.IsNullOrWhiteSpace(originalScenePath) && originalScenePath != ClassroomScenePath)
                {
                    EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
                }
            }
        }

        private static void SyncScene(Scene classroomScene, Scene referenceScene)
        {
            var primarySpawnPad = FindTransformByPath(classroomScene, PrimarySpawnPadPath);
            if (primarySpawnPad == null)
            {
                throw new System.InvalidOperationException($"Classroom primary spawn pad '{PrimarySpawnPadPath}' is missing.");
            }

            var secondarySpawnPad = FindTransformByPath(classroomScene, SecondarySpawnPadPath);
            if (secondarySpawnPad == null)
            {
                secondarySpawnPad = Object.Instantiate(primarySpawnPad.gameObject, primarySpawnPad.parent).transform;
                secondarySpawnPad.name = "Pfb_SpawnPad (1)";

                var offsetDirection = primarySpawnPad.right.sqrMagnitude > 0.0001f ? primarySpawnPad.right.normalized : Vector3.right;
                secondarySpawnPad.SetPositionAndRotation(
                    primarySpawnPad.position + (offsetDirection * 0.75f),
                    primarySpawnPad.rotation);
            }

            var networkManagerObject = EnsureClassroomObject(referenceScene, classroomScene, "NetworkManager");
            var gameManagerObject = EnsureClassroomObject(referenceScene, classroomScene, "GameManager");

            var sessionUi = networkManagerObject != null ? networkManagerObject.GetComponent<UIDocument>() : null;
            var gameManager = gameManagerObject != null ? gameManagerObject.GetComponent<GameManager>() : null;
            if (gameManager == null)
            {
                throw new System.InvalidOperationException("Classroom GameManager could not be created or located.");
            }

            ConfigureGameManager(gameManager, sessionUi, primarySpawnPad, secondarySpawnPad);
            ConfigureRuntimeBootstrap(classroomScene);
            EnsureMainCameraAudioListener(classroomScene);
            var floorCollider = EnsureWalkableFloor(classroomScene, primarySpawnPad);
            NormalizeNpcOutfits(classroomScene);
            ApplyNpcLookPresets(classroomScene);
            ConfigureNpcAnimators(classroomScene);
            StageNpcCast(classroomScene);
            GroundNpcCast(classroomScene, floorCollider);
            ConfigureNpcAmbientDirector(classroomScene, floorCollider);
        }

        private static GameObject EnsureClassroomObject(Scene referenceScene, Scene classroomScene, string objectName)
        {
            var existing = FindGameObjectInScene(classroomScene, objectName);
            if (existing != null)
            {
                return existing;
            }

            var reference = FindGameObjectInScene(referenceScene, objectName);
            if (reference == null)
            {
                throw new System.InvalidOperationException($"Reference object '{objectName}' was not found in {BedroomScenePath}.");
            }

            var clone = Object.Instantiate(reference);
            clone.name = objectName;
            SceneManager.MoveGameObjectToScene(clone, classroomScene);
            return clone;
        }

        private static void ConfigureGameManager(GameManager gameManager, UIDocument sessionUi, Transform primarySpawnPad, Transform secondarySpawnPad)
        {
            var serialized = new SerializedObject(gameManager);

            var sessionUiProperty = serialized.FindProperty("sessionUI");
            if (sessionUiProperty != null)
            {
                sessionUiProperty.objectReferenceValue = sessionUi;
            }

            var spawnPointsProperty = serialized.FindProperty("spawnPoints");
            if (spawnPointsProperty != null)
            {
                spawnPointsProperty.arraySize = 2;
                spawnPointsProperty.GetArrayElementAtIndex(0).objectReferenceValue = primarySpawnPad;
                spawnPointsProperty.GetArrayElementAtIndex(1).objectReferenceValue = secondarySpawnPad;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(gameManager);
        }

        private static void ConfigureRuntimeBootstrap(Scene classroomScene)
        {
            var bootstrap = FindComponentInScene<ClassroomStoryRuntimeBootstrap>(classroomScene);
            if (bootstrap == null)
            {
                return;
            }

            var serialized = new SerializedObject(bootstrap);
            var spawnAnchorPath = serialized.FindProperty("spawnAnchorPath");
            if (spawnAnchorPath != null)
            {
                spawnAnchorPath.stringValue = PrimarySpawnPadPath;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrap);
        }

        private static void ConfigureNpcAnimators(Scene classroomScene)
        {
            var idleController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(NpcIdleControllerPath);
            if (idleController == null)
            {
                throw new System.InvalidOperationException($"NPC idle controller not found at '{NpcIdleControllerPath}'.");
            }

            for (var index = 0; index < ClassroomNpcNames.Length; index++)
            {
                var npc = FindGameObjectInScene(classroomScene, ClassroomNpcNames[index]);
                if (npc == null)
                {
                    continue;
                }

                var animator = npc.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    continue;
                }

                animator.runtimeAnimatorController = idleController;
                animator.applyRootMotion = false;
                animator.Rebind();
                animator.Update(0f);
                EditorUtility.SetDirty(animator);
            }
        }

        private static void ConfigureNpcAmbientDirector(Scene classroomScene, Collider floorCollider)
        {
            var actorRoot = FindGameObjectInScene(classroomScene, "ClassroomStoryActors");
            if (actorRoot == null)
            {
                return;
            }

            var teacher = FindGameObjectInScene(classroomScene, "DrMira")?.transform;
            var friend = FindGameObjectInScene(classroomScene, "NiaPark")?.transform;
            var skeptic = FindGameObjectInScene(classroomScene, "TheoMercer")?.transform;
            if (teacher == null || friend == null || skeptic == null)
            {
                return;
            }

            var idleController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(NpcIdleControllerPath);
            var interactionController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(NpcInteractionControllerPath);
            var reactionController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(NpcReactionControllerPath);
            if (idleController == null || interactionController == null || reactionController == null)
            {
                return;
            }

            var ambientDirector = actorRoot.GetComponent<ClassroomNpcAmbientController>();
            if (ambientDirector == null)
            {
                ambientDirector = actorRoot.AddComponent<ClassroomNpcAmbientController>();
            }

            ambientDirector.Configure(teacher, friend, skeptic, floorCollider, idleController, interactionController, reactionController);
            EditorUtility.SetDirty(ambientDirector);
        }

        private static void EnsureMainCameraAudioListener(Scene classroomScene)
        {
            var mainCameraObject = FindGameObjectInScene(classroomScene, "Main Camera");
            if (mainCameraObject == null || mainCameraObject.GetComponent<AudioListener>() != null)
            {
                return;
            }

            mainCameraObject.AddComponent<AudioListener>();
            EditorUtility.SetDirty(mainCameraObject);
        }

        private static BoxCollider EnsureWalkableFloor(Scene classroomScene, Transform primarySpawnPad)
        {
            var floorObject = FindGameObjectInScene(classroomScene, "ClassroomFloorCollider");
            if (floorObject == null)
            {
                floorObject = new GameObject("ClassroomFloorCollider");
                SceneManager.MoveGameObjectToScene(floorObject, classroomScene);

                var setupRoot = FindGameObjectInScene(classroomScene, "_Setup");
                if (setupRoot != null)
                {
                    floorObject.transform.SetParent(setupRoot.transform, true);
                }
            }

            var floorCollider = floorObject.GetComponent<BoxCollider>();
            if (floorCollider == null)
            {
                floorCollider = floorObject.AddComponent<BoxCollider>();
            }

            var floorCenter = primarySpawnPad.position + new Vector3(0f, -0.24f, 0f);
            var floorSize = new Vector3(12f, 0.5f, 10f);
            var classroomRoot = FindGameObjectInScene(classroomScene, "Classroom");
            if (TryGetCombinedBounds(classroomRoot, out var bounds))
            {
                floorCenter = new Vector3(bounds.center.x, bounds.min.y - 0.23f, bounds.center.z);
                floorSize = new Vector3(Mathf.Max(8f, bounds.size.x), 0.5f, Mathf.Max(8f, bounds.size.z));
            }

            floorObject.transform.SetPositionAndRotation(floorCenter, Quaternion.identity);
            floorCollider.center = Vector3.zero;
            floorCollider.size = floorSize;
            EditorUtility.SetDirty(floorObject);
            EditorUtility.SetDirty(floorCollider);
            return floorCollider;
        }

        private static void GroundNpcCast(Scene classroomScene, Collider floorCollider)
        {
            if (floorCollider == null)
            {
                return;
            }

            var floorY = floorCollider.bounds.max.y + 0.02f;
            for (var index = 0; index < ClassroomNpcNames.Length; index++)
            {
                var npc = FindGameObjectInScene(classroomScene, ClassroomNpcNames[index]);
                if (npc == null || !TryGetCombinedBounds(npc, out var bounds))
                {
                    continue;
                }

                var offset = floorY - bounds.min.y;
                if (Mathf.Abs(offset) <= 0.001f)
                {
                    continue;
                }

                npc.transform.position += Vector3.up * offset;
                EditorUtility.SetDirty(npc.transform);
            }
        }

        private static void StageNpcCast(Scene classroomScene)
        {
            StageNpc(classroomScene, "DrMira", TeacherStagePosition, TeacherStageYaw);
            StageNpc(classroomScene, "NiaPark", FriendStagePosition, FriendStageYaw);
            StageNpc(classroomScene, "TheoMercer", SkepticStagePosition, SkepticStageYaw);
        }

        private static void StageNpc(Scene classroomScene, string npcName, Vector3 stagePosition, float yawDegrees)
        {
            var npc = FindGameObjectInScene(classroomScene, npcName);
            if (npc == null)
            {
                return;
            }

            var currentPosition = npc.transform.position;
            npc.transform.SetPositionAndRotation(
                new Vector3(stagePosition.x, currentPosition.y, stagePosition.z),
                Quaternion.Euler(0f, yawDegrees, 0f));
            EditorUtility.SetDirty(npc.transform);
        }

        private static void NormalizeNpcOutfits(Scene classroomScene)
        {
            SetNpcPartActive(classroomScene, "DrMira", "Headgear", false);
            SetNpcPartActive(classroomScene, "DrMira", "Shield", false);
            SetNpcPartActive(classroomScene, "DrMira", "Mask", false);
            SetNpcPartActive(classroomScene, "DrMira", "HandAcc", false);
            SetNpcPartActive(classroomScene, "DrMira", "Bag", false);
            SetNpcPartActive(classroomScene, "DrMira", "Axe", false);
            SetNpcPartActive(classroomScene, "DrMira", "Spear", false);
            SetNpcPartActive(classroomScene, "DrMira", "Sword", false);
            SetNpcPartActive(classroomScene, "DrMira", "Glove", false);
            SetNpcPartActive(classroomScene, "DrMira", "HairAcc", false);

            SetNpcPartActive(classroomScene, "NiaPark", "Headgear", false);
            SetNpcPartActive(classroomScene, "NiaPark", "Shield", false);
            SetNpcPartActive(classroomScene, "NiaPark", "Mask", false);
            SetNpcPartActive(classroomScene, "NiaPark", "HandAcc", false);
            SetNpcPartActive(classroomScene, "NiaPark", "Bag", false);
            SetNpcPartActive(classroomScene, "NiaPark", "Axe", false);
            SetNpcPartActive(classroomScene, "NiaPark", "Spear", false);
            SetNpcPartActive(classroomScene, "NiaPark", "Sword", false);
            SetNpcPartActive(classroomScene, "NiaPark", "Glove", false);
            SetNpcPartActive(classroomScene, "NiaPark", "HairAcc", false);

            SetNpcPartActive(classroomScene, "TheoMercer", "Headgear", false);
            SetNpcPartActive(classroomScene, "TheoMercer", "Shield", false);
            SetNpcPartActive(classroomScene, "TheoMercer", "Mask", false);
            SetNpcPartActive(classroomScene, "TheoMercer", "HandAcc", false);
            SetNpcPartActive(classroomScene, "TheoMercer", "Bag", false);
            SetNpcPartActive(classroomScene, "TheoMercer", "Axe", false);
            SetNpcPartActive(classroomScene, "TheoMercer", "Spear", false);
            SetNpcPartActive(classroomScene, "TheoMercer", "Sword", false);
            SetNpcPartActive(classroomScene, "TheoMercer", "Glove", false);
            SetNpcPartActive(classroomScene, "TheoMercer", "HairAcc", false);
        }

        private static void ApplyNpcLookPresets(Scene classroomScene)
        {
            ApplyNpcLookPreset(classroomScene, "DrMira", topVariant: "Top_54", bottomVariant: "Bottom_37", hairVariant: "Hair_3");
            ApplyNpcLookPreset(classroomScene, "NiaPark", topVariant: "Top_11", bottomVariant: "Bottom_12", hairVariant: "Hair_5");
            ApplyNpcLookPreset(classroomScene, "TheoMercer", topVariant: "Top_65", bottomVariant: "Bottom_49", hairVariant: "Hair_2");
        }

        private static void ApplyNpcLookPreset(Scene classroomScene, string npcName, string topVariant, string bottomVariant, string hairVariant)
        {
            SelectNpcVariant(classroomScene, npcName, "Top", topVariant);
            SelectNpcVariant(classroomScene, npcName, "Bottom", bottomVariant);
            SelectNpcVariant(classroomScene, npcName, "Hair", hairVariant);
        }

        private static void SelectNpcVariant(Scene classroomScene, string npcName, string categoryName, string variantName)
        {
            var category = FindTransformByPath(classroomScene, $"ClassroomStoryActors/{npcName}/Parts/{categoryName}");
            if (category == null)
            {
                return;
            }

            if (!category.gameObject.activeSelf)
            {
                category.gameObject.SetActive(true);
                EditorUtility.SetDirty(category.gameObject);
            }

            for (var childIndex = 0; childIndex < category.childCount; childIndex++)
            {
                var child = category.GetChild(childIndex);
                var shouldBeActive = string.Equals(child.name, variantName, StringComparison.Ordinal);
                if (child.gameObject.activeSelf == shouldBeActive)
                {
                    continue;
                }

                child.gameObject.SetActive(shouldBeActive);
                EditorUtility.SetDirty(child.gameObject);
            }
        }

        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            var candidates = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (candidate != null && candidate.gameObject.scene == scene)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static GameObject FindGameObjectInScene(Scene scene, string objectName)
        {
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var root = roots[rootIndex];
                if (root.name == objectName)
                {
                    return root;
                }

                var child = FindInChildren(root.transform, objectName);
                if (child != null)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private static Transform FindTransformByPath(Scene scene, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var segments = path.Split('/');
            if (segments.Length == 0)
            {
                return null;
            }

            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var root = roots[rootIndex];
                if (root.name != segments[0])
                {
                    continue;
                }

                var current = root.transform;
                for (var segmentIndex = 1; segmentIndex < segments.Length && current != null; segmentIndex++)
                {
                    current = current.Find(segments[segmentIndex]);
                }

                if (current != null)
                {
                    return current;
                }
            }

            return null;
        }

        private static void SetNpcPartActive(Scene classroomScene, string npcName, string partName, bool isActive)
        {
            var part = FindTransformByPath(classroomScene, $"ClassroomStoryActors/{npcName}/Parts/{partName}");
            if (part == null || part.gameObject.activeSelf == isActive)
            {
                return;
            }

            part.gameObject.SetActive(isActive);
            EditorUtility.SetDirty(part.gameObject);
        }

        private static Transform FindInChildren(Transform root, string objectName)
        {
            if (root == null)
            {
                return null;
            }

            for (var childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                var child = root.GetChild(childIndex);
                if (child.name == objectName)
                {
                    return child;
                }

                var nested = FindInChildren(child, objectName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static bool TryGetCombinedBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
            {
                return false;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
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

            return hasBounds;
        }
    }
}
