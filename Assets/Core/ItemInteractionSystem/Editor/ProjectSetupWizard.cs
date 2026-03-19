using UnityEditor;
using UnityEngine;
using Unity.Netcode.Components;
using System.Collections.Generic;
using ItemInteraction;
using Blocks.Gameplay.Core;

namespace ItemInteraction.Editor
{
    public class ProjectSetupWizard : EditorWindow
    {
        private GameObject m_InteractionTarget;

        [MenuItem("Tools/Project Setup Wizard")]
        public static void ShowWindow()
        {
            GetWindow<ProjectSetupWizard>("Setup Wizard");
        }

        [MenuItem("Tools/Fix Player and Scene")]
        public static void FixAll()
        {
            FixPlayerPrefab();
            CleanScenePlayers();
            SetupSceneItems();
            Debug.Log("Project Fix Completed!");
        }

        private void OnGUI()
        {
            GUILayout.Label("Project Setup Wizard", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Use this wizard to fix the player character and setup interactive items in the scene.", MessageType.Info);

            if (GUILayout.Button("Full Setup: Fix Player & Scene"))
            {
                FixAll();
            }

            EditorGUILayout.Space();
            GUILayout.Label("Individual Actions", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Fix Player Animator & Model"))
            {
                FixPlayerPrefab();
            }

            if (GUILayout.Button("Setup Scene Interactive Items"))
            {
                SetupSceneItems();
            }

            EditorGUILayout.Space();
            GUILayout.Label("Manual Setup", EditorStyles.boldLabel);
            m_InteractionTarget = (GameObject)EditorGUILayout.ObjectField("Target Item", m_InteractionTarget, typeof(GameObject), true);
            if (GUILayout.Button("Make Object Interactable") && m_InteractionTarget != null)
            {
                MakeInteractableForObject(m_InteractionTarget, "Item");
            }
        }

        public static void FixPlayerPrefab()
        {
            string playerPrefabPath = "Assets/Core/Prefabs/[BB] CorePlayer.prefab";
            string characterPrefabPath = "Assets/Core/Art/3D Casual Character/3D Characters Pro - Casual/Prefabs/Characters/Characters_5.prefab";
            string animatorControllerPath = "Assets/Shooter/Art/Animator/ShooterAnimator.controller";

            GameObject playerRoot = PrefabUtility.LoadPrefabContents(playerPrefabPath);
            if (playerRoot == null)
            {
                Debug.LogError($"Could not load player prefab at {playerPrefabPath}");
                return;
            }

            // 1. Aggressive Cleanup of root
            List<GameObject> toDestroy = new List<GameObject>();
            foreach (Transform child in playerRoot.transform)
            {
                // Destroy anything that looks like a model container or remnant
                if (child.name.Contains("CharacterModel") || 
                    child.name.Contains("Armature") || 
                    child.name.Contains("Characters_") ||
                    child.name == "ModelData" ||
                    child.GetComponent<Animator>() != null ||
                    child.GetComponent<SkinnedMeshRenderer>() != null)
                {
                    toDestroy.Add(child.gameObject);
                }
            }
            foreach (var obj in toDestroy) Object.DestroyImmediate(obj);

            // 2. Remove any Animator/CoreAnimator from the root itself to avoid duplicates/conflicts
            var rootAnim = playerRoot.GetComponent<Animator>();
            if (rootAnim != null) Object.DestroyImmediate(rootAnim, true);
            var rootCoreAnim = playerRoot.GetComponent<CoreAnimator>();
            if (rootCoreAnim != null) Object.DestroyImmediate(rootCoreAnim, true);

            // 3. Create fresh container
            GameObject modelContainer = new GameObject("CharacterModel");
            modelContainer.transform.SetParent(playerRoot.transform);
            modelContainer.transform.localPosition = Vector3.zero;
            modelContainer.transform.localRotation = Quaternion.identity;

            // 4. Instantiate model data
            GameObject characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(characterPrefabPath);
            if (characterPrefab != null)
            {
                GameObject modelData = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab, modelContainer.transform);
                modelData.name = "ModelData";
                modelData.transform.localPosition = Vector3.zero;
                modelData.transform.localRotation = Quaternion.identity;

                // Setup Animator on the container
                Animator animator = modelContainer.AddComponent<Animator>();
                RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(animatorControllerPath);
                if (controller != null) animator.runtimeAnimatorController = controller;

                // Sync avatar from child
                Animator childAnim = modelData.GetComponent<Animator>();
                if (childAnim != null)
                {
                    animator.avatar = childAnim.avatar;
                    Object.DestroyImmediate(childAnim);
                }

                // 5. Setup CoreAnimator on the container
                CoreAnimator coreAnimator = modelContainer.AddComponent<CoreAnimator>();
                
                // We DON'T add NetworkTransform here because the root already has one (CoreMovement)
                // and we want child to stay at zero relative to root.

                // 6. Bind references using SerializedObject for prefab persistence
                SerializedObject so = new SerializedObject(coreAnimator);
                // In Netcode NetworkAnimator, the serialized field is 'm_Animator'
                var animProp = so.FindProperty("m_Animator");
                if (animProp != null) animProp.objectReferenceValue = animator;
                
                CoreMovement movement = playerRoot.GetComponent<CoreMovement>();
                if (movement != null)
                {
                    var moveProp = so.FindProperty("coreMovement");
                    if (moveProp != null) moveProp.objectReferenceValue = movement;
                }
                
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogError($"Character prefab not found at {characterPrefabPath}");
            }

            // Save and cleanup
            PrefabUtility.SaveAsPrefabAsset(playerRoot, playerPrefabPath);
            PrefabUtility.UnloadPrefabContents(playerRoot);
            Debug.Log("Player Prefab REBUILT successfully.");
        }

        public static void CleanScenePlayers()
        {
            // Find any player instances already in the scene and remove them 
            // to ensure fresh spawn at runtime from the modified prefab.
            var allMovements = Object.FindObjectsByType<CoreMovement>(FindObjectsSortMode.None);
            int count = 0;
            foreach (var move in allMovements)
            {
                if (move.gameObject.scene.name != null) // It's in a scene, not a prefab
                {
                    Object.DestroyImmediate(move.gameObject);
                    count++;
                }
            }
            if (count > 0) Debug.Log($"Cleaned {count} stale player instances from scene.");
        }

        public static void SetupSceneItems()
        {
            MakeInteractableByName("MacBook", "MacBook");
            MakeInteractableByName("iMac", "iMac");
            MakeInteractableByName("Tablet", "Tablet");
            MakeInteractableByName("Coffee+Maker", "Coffee Maker");
            MakeInteractableByName("Bed", "Bed");

            // Also ensure collision for the room geometry if possible
            GameObject room = GameObject.Find("Room") ?? GameObject.Find("_Environment/Room");
            if (room != null)
            {
                foreach (Transform t in room.transform)
                {
                    if (t.GetComponent<Collider>() == null && t.GetComponent<MeshFilter>() != null)
                    {
                        var mc = t.gameObject.AddComponent<MeshCollider>();
                        mc.convex = false; // Room walls should be non-convex usually
                    }
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log("Scene items and collision setup completed.");
        }

        public static void MakeInteractableByName(string gameObjectName, string displayName = "Item")
        {
            GameObject go = GameObject.Find(gameObjectName);
            if (go == null) go = GameObject.Find("_Environment/Room/" + gameObjectName);
            if (go != null) MakeInteractableForObject(go, displayName);
        }

        public static void MakeInteractableForObject(GameObject go, string displayName = "Item")
        {
            if (go == null) return;

            if (go.GetComponent<Collider>() == null)
            {
                if (go.GetComponent<MeshFilter>() != null)
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.convex = true;
                }
                else
                {
                    go.AddComponent<BoxCollider>();
                }
            }

            var interactable = go.GetComponent<InteractableItem>();
            if (interactable == null) interactable = go.AddComponent<InteractableItem>();
            
            SerializedObject so = new SerializedObject(interactable);
            so.FindProperty("displayName").stringValue = displayName;
            if (so.FindProperty("promptAnchor").objectReferenceValue == null) 
                so.FindProperty("promptAnchor").objectReferenceValue = go.transform;

            var outline = go.GetComponent<SelectableOutline>();
            if (outline == null) outline = go.AddComponent<SelectableOutline>();
            so.FindProperty("outline").objectReferenceValue = outline;
            
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(interactable);
        }
    }
}
