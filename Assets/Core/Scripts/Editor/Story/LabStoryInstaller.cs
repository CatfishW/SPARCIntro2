using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blocks.Gameplay.Core;
using Blocks.Gameplay.Core.Story;
using ItemInteraction;
using ModularStoryFlow.Editor.Setup;
using ModularStoryFlow.Runtime.Bridges;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Blocks.Gameplay.Core.Story.Editor
{
    public static class LabStoryInstaller
    {
        private const string LabScenePath = "Assets/LabScene.unity";
        private const string ReferenceScenePath = "Assets/Core/TestScenes/BedRoomIntroScene.unity";
        private const string TransitionScenePath = "Assets/Core/TestScenes/LabTransitionScene.unity";
        private const string SpawnRootName = "_Spawns";
        private const string PrimarySpawnPadName = "Pfb_SpawnPad";
        private const string SecondarySpawnPadName = "Pfb_SpawnPad (1)";
        private const string GeneratedMaterialFolder = "Assets/Core/Generated/LabSceneMaterials";
        private const string GeneratedRoot = "Assets/StoryFlowLabGenerated";
        private const string MissionRootName = "LabMission";
        private const string CapObjectName = "CAP";
        private const string BodyTableObjectName = "Body Table";
        private const string BodyModelObjectName = "Body Model";
        private const string ShrinkMachineObjectName = "Shrink Machine";
        private const string RocketObjectName = "Mini Rocket";
        private const string LightPuzzleUiRootName = "LabLightPuzzleUiRoot";
        private const string TimelineBridgeObjectName = "LabStoryTimelineBridge";
        private const string ShrinkAnchorObjectName = "ShrinkPlayerAnchor";
        private const string RocketFocusAnchorObjectName = "RocketFocusAnchor";
        private const string PromptAnchorObjectName = "PromptAnchor";
        private const string BrokenDoorPath = "Office 1/Door Wall Opaque/Broken Door";
        private const string CapPrefabPath = "Assets/SM_Peer_Agent_Mo/PREFAB/MO_IDLE.prefab";
        private const string CapWalkPrefabPath = "Assets/SM_Peer_Agent_Mo/PREFAB/MO_WALK.prefab";
        private const string BodyPrefabPath = "Assets/Core/Art/3D Casual Character/3D Characters Pro - Casual/Prefabs/Characters/Character_Basic.prefab";
        private const string BodyTablePrefabPath = "Assets/Core/Art/Models/sci-fi-lab-machine/source/SciFi_Lab_Table.fbx";
        private const string ShrinkMachinePrefabPath = "Assets/Core/Art/Models/dna-lab-machine/source/Mecha.fbx";
        private const string RocketPrefabPath = "Assets/Core/Art/Models/Rocket/Rocket_New.fbx";
        private const string ConfigPath = GeneratedRoot + "/Config/StoryFlowProjectConfig.asset";
        private const string ChannelsPath = GeneratedRoot + "/Config/StoryFlowChannels.asset";
        private const string GraphRegistryPath = GeneratedRoot + "/Data/StoryGraphRegistry.asset";
        private const string VariableCatalogPath = GeneratedRoot + "/Data/StoryVariableCatalog.asset";
        private const string StateMachineCatalogPath = GeneratedRoot + "/Data/StoryStateMachineCatalog.asset";
        private const string TimelineCatalogPath = GeneratedRoot + "/Data/StoryTimelineCatalog.asset";
        private const string GraphPath = GeneratedRoot + "/Graphs/LabStoryRuntime.asset";
        private const string TimelinesFolderPath = GeneratedRoot + "/Data/Timelines";
        private const string ShrinkTimelinePlayablePath = TimelinesFolderPath + "/LabShrinkTimelinePlayable.asset";

        [MenuItem("Tools/Blocks/Story/Sync Lab Story Scene", priority = 305)]
        public static void Install()
        {
            var originalScenePath = SceneManager.GetActiveScene().path;
            EditorSceneManager.SaveOpenScenes();

            if (AssetDatabase.IsValidFolder(GeneratedRoot))
            {
                AssetDatabase.DeleteAsset(GeneratedRoot);
                AssetDatabase.Refresh();
            }

            var generatedProject = StoryFlowProjectGenerator.Generate(GeneratedRoot, false, false, false);
            EnsureFolder(GeneratedRoot, "Graphs");
            var config = ResolveGeneratedAsset(ConfigPath, generatedProject.Config);
            var channels = ResolveGeneratedAsset(ChannelsPath, generatedProject.Channels);
            var graphRegistry = ResolveGeneratedAsset(GraphRegistryPath, generatedProject.GraphRegistry);
            var variableCatalog = ResolveGeneratedAsset(VariableCatalogPath, generatedProject.VariableCatalog);
            var stateMachineCatalog = ResolveGeneratedAsset(StateMachineCatalogPath, generatedProject.StateMachineCatalog);
            var timelineCatalog = ResolveGeneratedAsset(TimelineCatalogPath, generatedProject.TimelineCatalog);
            if (config == null || channels == null || graphRegistry == null || variableCatalog == null || stateMachineCatalog == null || timelineCatalog == null)
            {
                throw new System.InvalidOperationException("Lab story generated assets failed to load after generation.");
            }

            var builder = new LabStoryAssetBuilder(GeneratedRoot + "/Data");
            var bundle = builder.Build();
            config = ResolveGeneratedAsset(ConfigPath, config);
            channels = ResolveGeneratedAsset(ChannelsPath, channels);
            graphRegistry = ResolveGeneratedAsset(GraphRegistryPath, graphRegistry);
            variableCatalog = ResolveGeneratedAsset(VariableCatalogPath, variableCatalog);
            stateMachineCatalog = ResolveGeneratedAsset(StateMachineCatalogPath, stateMachineCatalog);
            timelineCatalog = ResolveGeneratedAsset(TimelineCatalogPath, timelineCatalog);
            if (config == null || channels == null || graphRegistry == null || variableCatalog == null || stateMachineCatalog == null || timelineCatalog == null)
            {
                throw new System.InvalidOperationException("Lab story generated assets became unavailable during install.");
            }

            var graphBuilder = new LabStoryGraphBuilder(GraphPath);
            var graph = graphBuilder.Build(bundle);
            RepairGraphNodeReferences(GraphPath, graph);

            graphRegistry.SetGraphs(new[] { graph });
            stateMachineCatalog.SetStateMachines(new[] { bundle.ProgressionState });
            EditorUtility.SetDirty(graphRegistry);
            EditorUtility.SetDirty(stateMachineCatalog);
            EditorUtility.SetDirty(config);

            var labScene = EditorSceneManager.OpenScene(LabScenePath, OpenSceneMode.Single);
            var referenceScene = EditorSceneManager.OpenScene(ReferenceScenePath, OpenSceneMode.Additive);

            try
            {
                SyncScene(labScene, referenceScene, config, channels, graph);
                EditorSceneManager.MarkSceneDirty(labScene);
                EditorSceneManager.SaveScene(labScene);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                if (referenceScene.IsValid() && referenceScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(referenceScene, true);
                }

                if (!string.IsNullOrWhiteSpace(originalScenePath) && originalScenePath != LabScenePath)
                {
                    EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
                }
            }
        }

        private static void SyncScene(Scene labScene, Scene referenceScene, StoryFlowProjectConfig config, StoryFlowChannels channels, StoryGraphAsset graph)
        {
            var primarySpawnPad = EnsureSpawnPad(labScene, PrimarySpawnPadName, out var primarySpawnPadWasCreated);
            var secondarySpawnPad = EnsureSpawnPad(labScene, SecondarySpawnPadName, out var secondarySpawnPadWasCreated);
            PositionSpawnPads(labScene, primarySpawnPad, primarySpawnPadWasCreated, secondarySpawnPad, secondarySpawnPadWasCreated);

            var networkManagerObject = EnsureSceneObject(referenceScene, labScene, "NetworkManager");
            var gameManagerObject = EnsureSceneObject(referenceScene, labScene, "GameManager");

            var sessionUi = networkManagerObject != null ? networkManagerObject.GetComponent<UIDocument>() : null;
            var gameManager = gameManagerObject != null ? gameManagerObject.GetComponent<GameManager>() : null;
            if (gameManager == null)
            {
                throw new System.InvalidOperationException("Lab GameManager could not be created or located.");
            }

            ConfigureGameManager(gameManager, sessionUi, primarySpawnPad, secondarySpawnPad);
            EnsureWalkableFloor(labScene, primarySpawnPad);
            EnsureMainCameraAudioListener(labScene);
            var storyRoot = EnsureStoryRoot(labScene, config, channels, graph);
            EnsureMissionRig(labScene, storyRoot, sessionUi, channels);
            ConfigureLighting(labScene);
            ConvertSceneMaterialsToUrp(labScene);
            EnsureBuildSettings();
        }

        private static GameObject EnsureSceneObject(Scene referenceScene, Scene targetScene, string objectName)
        {
            var existing = FindGameObjectInScene(targetScene, objectName);
            if (existing != null)
            {
                return existing;
            }

            var reference = FindGameObjectInScene(referenceScene, objectName);
            if (reference == null)
            {
                throw new System.InvalidOperationException($"Reference object '{objectName}' was not found in {ReferenceScenePath}.");
            }

            var clone = Object.Instantiate(reference);
            clone.name = objectName;
            SceneManager.MoveGameObjectToScene(clone, targetScene);
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

        private static Transform EnsureSpawnPad(Scene labScene, string spawnName, out bool wasCreated)
        {
            wasCreated = false;
            var spawnRoot = FindGameObjectInScene(labScene, SpawnRootName);
            if (spawnRoot == null)
            {
                spawnRoot = new GameObject(SpawnRootName);
                SceneManager.MoveGameObjectToScene(spawnRoot, labScene);
            }

            var existing = spawnRoot.transform.Find(spawnName);
            if (existing != null)
            {
                return existing;
            }

            var spawn = new GameObject(spawnName).transform;
            spawn.SetParent(spawnRoot.transform, false);
            wasCreated = true;
            return spawn;
        }

        private static void PositionSpawnPads(Scene labScene, Transform primarySpawnPad, bool primarySpawnPadWasCreated, Transform secondarySpawnPad, bool secondarySpawnPadWasCreated)
        {
            if (!primarySpawnPadWasCreated && !secondarySpawnPadWasCreated)
            {
                return;
            }

            var officeRoot = FindGameObjectInScene(labScene, "Office 1");
            if (officeRoot == null || !TryGetCombinedBounds(officeRoot, out var bounds))
            {
                if (primarySpawnPadWasCreated)
                {
                    primarySpawnPad.SetPositionAndRotation(new Vector3(2.5f, 0.08f, 31.5f), Quaternion.LookRotation(Vector3.forward, Vector3.up));
                }

                if (secondarySpawnPadWasCreated)
                {
                    secondarySpawnPad.SetPositionAndRotation(new Vector3(3.2f, 0.08f, 31.5f), Quaternion.LookRotation(Vector3.forward, Vector3.up));
                }

                return;
            }

            var lookTarget = FindGameObjectInScene(labScene, "Mechanical arm 2")?.transform ?? officeRoot.transform;
            var spawnPosition = new Vector3(bounds.center.x, bounds.min.y + 0.08f, bounds.min.z + Mathf.Max(2.2f, Mathf.Min(4f, bounds.size.z * 0.18f)));
            var lookDirection = Vector3.ProjectOnPlane(lookTarget.position - spawnPosition, Vector3.up);
            if (lookDirection.sqrMagnitude < 0.001f)
            {
                lookDirection = Vector3.forward;
            }

            var rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            var lateralOffset = Vector3.Cross(Vector3.up, lookDirection.normalized);
            if (lateralOffset.sqrMagnitude < 0.001f)
            {
                lateralOffset = Vector3.right;
            }

            if (primarySpawnPadWasCreated)
            {
                primarySpawnPad.SetPositionAndRotation(spawnPosition, rotation);
                EditorUtility.SetDirty(primarySpawnPad);
            }

            if (secondarySpawnPadWasCreated)
            {
                secondarySpawnPad.SetPositionAndRotation(spawnPosition + (lateralOffset.normalized * 0.75f), rotation);
                EditorUtility.SetDirty(secondarySpawnPad);
            }
        }

        private static GameObject EnsureStoryRoot(Scene labScene, StoryFlowProjectConfig config, StoryFlowChannels channels, StoryGraphAsset graph)
        {
            var storyRoot = FindGameObjectInScene(labScene, "LabStoryFlowPlayer");
            if (storyRoot == null)
            {
                storyRoot = new GameObject("LabStoryFlowPlayer");
                SceneManager.MoveGameObjectToScene(storyRoot, labScene);
            }

            var player = storyRoot.GetComponent<ModularStoryFlow.Runtime.Player.StoryFlowPlayer>() ?? storyRoot.AddComponent<ModularStoryFlow.Runtime.Player.StoryFlowPlayer>();
            var uiRoot = storyRoot.GetComponent<LabStoryUiRoot>() ?? storyRoot.AddComponent<LabStoryUiRoot>();
            var bridge = storyRoot.GetComponent<LabStoryInteractionBridge>() ?? storyRoot.AddComponent<LabStoryInteractionBridge>();
            var sceneTransition = storyRoot.GetComponent<LabStorySceneTransition>() ?? storyRoot.AddComponent<LabStorySceneTransition>();
            var bootstrap = storyRoot.GetComponent<LabStoryRuntimeBootstrap>() ?? storyRoot.AddComponent<LabStoryRuntimeBootstrap>();
            var interactionDirector = storyRoot.GetComponent<InteractionDirector>() ?? storyRoot.AddComponent<InteractionDirector>();
            var timelineBridge = EnsureTimelineBridge(labScene, config);

            player.ProjectConfig = config;
            player.InitialGraph = graph;
            player.PlayOnStart = true;

            var serializedPlayer = new SerializedObject(player);
            SetBooleanIfPresent(serializedPlayer, "autoLoadSaveOnStart", false);
            SetStringIfPresent(serializedPlayer, "saveSlot", "quick");
            serializedPlayer.ApplyModifiedPropertiesWithoutUndo();

            uiRoot.Configure(channels);
            bridge.Configure(channels, sceneTransition);

            var serializedBootstrap = new SerializedObject(bootstrap);
            SetObjectReferenceIfPresent(serializedBootstrap, "player", player);
            SetObjectReferenceIfPresent(serializedBootstrap, "uiRoot", uiRoot);
            SetObjectReferenceIfPresent(serializedBootstrap, "interactionBridge", bridge);
            SetObjectReferenceIfPresent(serializedBootstrap, "sceneTransition", sceneTransition);
            SetObjectReferenceIfPresent(serializedBootstrap, "timelineBridge", timelineBridge);
            SetStringIfPresent(serializedBootstrap, "spawnAnchorPath", $"{SpawnRootName}/{PrimarySpawnPadName}");
            SetStringIfPresent(serializedBootstrap, "spawnLookTargetPath", "Office 1/Mechanical arm 2");
            SetStringIfPresent(serializedBootstrap, "targetScenePath", TransitionScenePath);
            SetStringIfPresent(serializedBootstrap, "targetSceneName", Path.GetFileNameWithoutExtension(TransitionScenePath));
            SetStringIfPresent(serializedBootstrap, "savedProjectConfigPath", ConfigPath);
            SetStringIfPresent(serializedBootstrap, "savedGraphPath", GraphPath);
            serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();

            var serializedTransition = new SerializedObject(sceneTransition);
            SetStringIfPresent(serializedTransition, "targetScenePath", TransitionScenePath);
            SetStringIfPresent(serializedTransition, "targetSceneName", Path.GetFileNameWithoutExtension(TransitionScenePath));
            serializedTransition.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(storyRoot);
            EditorUtility.SetDirty(bootstrap);
            EditorUtility.SetDirty(sceneTransition);
            EditorUtility.SetDirty(interactionDirector);
            if (timelineBridge != null)
            {
                EditorUtility.SetDirty(timelineBridge);
                EditorUtility.SetDirty(timelineBridge.gameObject);
            }
            return storyRoot;
        }

        private static StoryTimelineDirectorBridge EnsureTimelineBridge(Scene labScene, StoryFlowProjectConfig config)
        {
            var timelineObject = FindGameObjectInScene(labScene, TimelineBridgeObjectName);
            if (timelineObject == null)
            {
                timelineObject = new GameObject(TimelineBridgeObjectName);
                SceneManager.MoveGameObjectToScene(timelineObject, labScene);
            }

            var director = timelineObject.GetComponent<PlayableDirector>() ?? timelineObject.AddComponent<PlayableDirector>();
            var bridge = timelineObject.GetComponent<StoryTimelineDirectorBridge>() ?? timelineObject.AddComponent<StoryTimelineDirectorBridge>();

            var serializedBridge = new SerializedObject(bridge);
            SetObjectReferenceIfPresent(serializedBridge, "projectConfig", config);
            SetObjectReferenceIfPresent(serializedBridge, "director", director);
            serializedBridge.ApplyModifiedPropertiesWithoutUndo();
            return bridge;
        }

        private static void EnsureMissionRig(Scene labScene, GameObject storyRoot, UIDocument sessionUi, StoryFlowChannels channels)
        {
            var missionRoot = FindGameObjectInScene(labScene, MissionRootName);
            var missionRootWasCreated = false;
            if (missionRoot == null)
            {
                missionRoot = new GameObject(MissionRootName);
                SceneManager.MoveGameObjectToScene(missionRoot, labScene);
                missionRootWasCreated = true;
            }

            if (missionRootWasCreated)
            {
                missionRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            var storyPlayer = storyRoot != null ? storyRoot.GetComponent<StoryFlowPlayer>() : null;
            var interactionBridge = storyRoot != null ? storyRoot.GetComponent<LabStoryInteractionBridge>() : null;
            var interactionDirector = storyRoot != null ? storyRoot.GetComponent<InteractionDirector>() : null;
            var timelineBridge = Object.FindFirstObjectByType<StoryTimelineDirectorBridge>(FindObjectsInactive.Include);
            var panelSettings = sessionUi != null ? sessionUi.panelSettings : null;
            var shrinkTimelinePlayable = EnsureShrinkTimelinePlayableAsset();
            var timelineCatalog = storyPlayer != null ? storyPlayer.ProjectConfig?.TimelineCatalog : null;
            if (timelineCatalog != null && shrinkTimelinePlayable != null)
            {
                timelineCatalog.AddOrReplaceBinding(
                    LabShrinkSequenceController.DefaultShrinkTimelineCueId,
                    LabShrinkSequenceController.DefaultShrinkTimelineCueDisplayName,
                    shrinkTimelinePlayable);
                EditorUtility.SetDirty(timelineCatalog);
            }

            var registry = missionRoot.GetComponent<StoryNpcRegistry>() ?? missionRoot.AddComponent<StoryNpcRegistry>();
            var controlLock = missionRoot.GetComponent<ClassroomPlayerControlLock>() ?? missionRoot.AddComponent<ClassroomPlayerControlLock>();
            var sceneContext = missionRoot.GetComponent<LabSceneContext>() ?? missionRoot.AddComponent<LabSceneContext>();
            var conversationDirector = missionRoot.GetComponent<LabCapConversationDirector>() ?? missionRoot.AddComponent<LabCapConversationDirector>();
            var bodyUi = missionRoot.GetComponent<LabBodyInspectionUi>() ?? missionRoot.AddComponent<LabBodyInspectionUi>();
            var shrinkController = missionRoot.GetComponent<LabShrinkSequenceController>() ?? missionRoot.AddComponent<LabShrinkSequenceController>();
            var finalCutsceneController = missionRoot.GetComponent<LabFinalCutsceneController>() ?? missionRoot.AddComponent<LabFinalCutsceneController>();
            var objectivePanelUi = missionRoot.GetComponent<LabObjectivePanelUi>() ?? missionRoot.AddComponent<LabObjectivePanelUi>();
            var cameraFocusController = missionRoot.GetComponent<LabCameraFocusController>() ?? missionRoot.AddComponent<LabCameraFocusController>();
            var performanceTuner = missionRoot.GetComponent<LabPerformanceTuner>() ?? missionRoot.AddComponent<LabPerformanceTuner>();
            var puzzleUiRoot = EnsureOverlayRoot(labScene, LightPuzzleUiRootName, panelSettings, 710);
            var puzzleUi = puzzleUiRoot.GetComponent<LabLightPuzzleUi>() ?? puzzleUiRoot.AddComponent<LabLightPuzzleUi>();

            var missionPuzzleUi = missionRoot.GetComponent<LabLightPuzzleUi>();
            if (missionPuzzleUi != null)
            {
                Object.DestroyImmediate(missionPuzzleUi, true);
            }

            var missionRootDocument = missionRoot.GetComponent<UIDocument>();
            if (missionRootDocument != null)
            {
                missionRootDocument.enabled = false;
                EditorUtility.SetDirty(missionRootDocument);
            }

            var cap = EnsurePrefabInstance(labScene, missionRoot.transform, CapObjectName, CapPrefabPath, new Vector3(2.45f, 0f, 33.4f), Quaternion.Euler(0f, 180f, 0f), Vector3.one, preserveExistingTransform: true);
            var bodyTable = EnsurePrefabInstance(labScene, missionRoot.transform, BodyTableObjectName, BodyTablePrefabPath, new Vector3(2.35f, 0f, 45.65f), Quaternion.Euler(0f, 180f, 0f), new Vector3(1.2f, 1.2f, 1.2f), preserveExistingTransform: true);
            var body = EnsurePrefabInstance(labScene, bodyTable.transform, BodyModelObjectName, BodyPrefabPath, new Vector3(2.35f, 1.12f, 45.68f), Quaternion.Euler(90f, 180f, 0f), new Vector3(1f, 1f, 1f), preserveExistingTransform: true);
            var shrinkMachine = EnsurePrefabInstance(labScene, missionRoot.transform, ShrinkMachineObjectName, ShrinkMachinePrefabPath, new Vector3(-0.9f, 0f, 45.15f), Quaternion.Euler(0f, 35f, 0f), new Vector3(1.25f, 1.25f, 1.25f), preserveExistingTransform: true);
            var rocket = EnsurePrefabInstance(labScene, missionRoot.transform, RocketObjectName, RocketPrefabPath, new Vector3(6.2f, 0.12f, 44.1f), Quaternion.Euler(0f, 232f, 0f), new Vector3(1.15f, 1.15f, 1.15f), preserveExistingTransform: true);

            var shrinkAnchor = EnsureChildTransform(missionRoot.transform, ShrinkAnchorObjectName, new Vector3(5.35f, 0.08f, 43.1f), Quaternion.Euler(0f, 52f, 0f));
            var rocketFocusAnchor = EnsureChildTransform(rocket.transform, RocketFocusAnchorObjectName, new Vector3(0f, 1.15f, 0f), Quaternion.identity);
            var brokenDoor = FindGameObjectByPathInScene(labScene, BrokenDoorPath) ?? FindGameObjectInScene(labScene, "Broken Door");
            var doorController = EnsureDoorController(brokenDoor);

            var capNpc = cap.GetComponent<StoryNpcAgent>() ?? cap.AddComponent<StoryNpcAgent>();
            var capOutline = cap.GetComponent<SelectableOutline>() ?? cap.AddComponent<SelectableOutline>();
            var capController = cap.GetComponent<LabCapNpcController>() ?? cap.AddComponent<LabCapNpcController>();
            ConfigureCap(capController, capNpc, capOutline, cap);

            var bodyInteractable = body.GetComponent<InteractableItem>() ?? body.AddComponent<InteractableItem>();
            var bodyOutline = body.GetComponent<SelectableOutline>() ?? body.AddComponent<SelectableOutline>();
            ConfigureInteractable(bodyInteractable, bodyOutline, "Body Model", "lab.bodyModel", body.transform, 5f);

            var machineInteractable = shrinkMachine.GetComponent<InteractableItem>() ?? shrinkMachine.AddComponent<InteractableItem>();
            var machineOutline = shrinkMachine.GetComponent<SelectableOutline>() ?? shrinkMachine.AddComponent<SelectableOutline>();
            ConfigureInteractable(machineInteractable, machineOutline, "Shrink Machine", "lab.shrinkMachine", shrinkMachine.transform, 5.5f);

            var roboticArm = FindGameObjectInScene(labScene, "Mechanical arm 2");
            if (roboticArm != null)
            {
                var armInteractable = roboticArm.GetComponent<InteractableItem>() ?? roboticArm.AddComponent<InteractableItem>();
                var armOutline = roboticArm.GetComponent<SelectableOutline>() ?? roboticArm.AddComponent<SelectableOutline>();
                ConfigureInteractable(armInteractable, armOutline, "Robotic Arm", "lab.roboticArm", roboticArm.transform, 4.8f);
                var armController = roboticArm.GetComponent<LabMechanicalArmController>() ?? roboticArm.AddComponent<LabMechanicalArmController>();
                EditorUtility.SetDirty(armController);
            }

            var rocketInteractable = rocket.GetComponent<InteractableItem>() ?? rocket.AddComponent<InteractableItem>();
            var rocketOutline = rocket.GetComponent<SelectableOutline>() ?? rocket.AddComponent<SelectableOutline>();
            ConfigureInteractable(rocketInteractable, rocketOutline, "Mini Rocket", "lab.rocket", rocketFocusAnchor, 5f);

            EnsureCapsuleCollider(cap, 0.35f, 1.7f, new Vector3(0f, 0.85f, 0f));
            EnsureBoxCollider(body);
            EnsureBoxCollider(shrinkMachine);
            EnsureBoxCollider(rocket);

            ConfigureBodyUi(bodyUi, controlLock, panelSettings);
            ConfigurePuzzleUi(puzzleUi, controlLock, panelSettings);
            ConfigureShrinkController(shrinkController, controlLock, shrinkAnchor, interactionDirector, storyPlayer, timelineBridge, sceneContext, shrinkTimelinePlayable);
            ConfigureFinalCutscene(finalCutsceneController, controlLock);
            ConfigureCameraFocus(cameraFocusController, cap.transform);
            ConfigureObjectivePanelUi(objectivePanelUi, panelSettings);
            ConfigureConversationDirector(conversationDirector, storyPlayer, capNpc, capController, controlLock, cameraFocusController, channels);
            ConfigurePerformanceTuner(performanceTuner);
            ConfigureRegistry(registry, capNpc);
            ConfigureSceneContext(
                sceneContext,
                storyPlayer,
                registry,
                capNpc,
                bodyInteractable,
                machineInteractable,
                rocketInteractable,
                body.transform,
                shrinkAnchor,
                rocketFocusAnchor,
                controlLock,
                capController,
                conversationDirector,
                bodyUi,
                puzzleUi,
                shrinkController,
                finalCutsceneController,
                doorController,
                objectivePanelUi,
                cameraFocusController);

            ConfigureDoorController(doorController);

            if (interactionBridge != null)
            {
                var serializedBridge = new SerializedObject(interactionBridge);
                SetObjectReferenceIfPresent(serializedBridge, "sceneContext", sceneContext);
                SetObjectReferenceIfPresent(serializedBridge, "storyFlowPlayer", storyPlayer);
                serializedBridge.ApplyModifiedPropertiesWithoutUndo();
                interactionBridge.Configure(channels, storyRoot != null ? storyRoot.GetComponent<LabStorySceneTransition>() : null);
                EditorUtility.SetDirty(interactionBridge);
            }

            EditorUtility.SetDirty(missionRoot);
            EditorUtility.SetDirty(sceneContext);
            EditorUtility.SetDirty(conversationDirector);
            EditorUtility.SetDirty(bodyUi);
            EditorUtility.SetDirty(puzzleUi);
            EditorUtility.SetDirty(shrinkController);
            EditorUtility.SetDirty(finalCutsceneController);
            EditorUtility.SetDirty(objectivePanelUi);
            EditorUtility.SetDirty(cameraFocusController);
            EditorUtility.SetDirty(controlLock);
            EditorUtility.SetDirty(registry);
            EditorUtility.SetDirty(cap);
            EditorUtility.SetDirty(bodyTable);
            EditorUtility.SetDirty(body);
            EditorUtility.SetDirty(shrinkMachine);
            EditorUtility.SetDirty(rocket);
            if (doorController != null)
            {
                EditorUtility.SetDirty(doorController);
            }
        }

        private static void ConfigureCap(LabCapNpcController controller, StoryNpcAgent npcAgent, SelectableOutline outline, GameObject cap)
        {
            var promptAnchor = EnsureCapPromptAnchor(cap);
            var interactable = cap.GetComponent<InteractableItem>() ?? cap.AddComponent<InteractableItem>();
            var liveAnimator = cap.GetComponentInChildren<Animator>(true);
            var walkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CapWalkPrefabPath);
            var walkAnimator = walkPrefab != null ? walkPrefab.GetComponentInChildren<Animator>(true) : null;

            var serialized = new SerializedObject(controller);
            SetObjectReferenceIfPresent(serialized, "npcAgent", npcAgent);
            SetObjectReferenceIfPresent(serialized, "animator", liveAnimator);
            SetObjectReferenceIfPresent(serialized, "visualRoot", cap.transform);
            SetObjectReferenceIfPresent(serialized, "idleController", liveAnimator != null ? liveAnimator.runtimeAnimatorController : null);
            SetObjectReferenceIfPresent(serialized, "walkController", walkAnimator != null ? walkAnimator.runtimeAnimatorController : null);
            SetObjectReferenceIfPresent(serialized, "danceController", null);
            SetObjectReferenceIfPresent(serialized, "danceClip", ResolveDanceClip("Assets/Core/Art/3D Casual Character/Animation/Anim@Dance_1.FBX", "Dance_1"));
            SetFloatIfPresent(serialized, "followDistance", 2.3f);
            SetFloatIfPresent(serialized, "stopDistance", 1.85f);
            SetFloatIfPresent(serialized, "followSideOffset", 0.95f);
            SetFloatIfPresent(serialized, "followMoveSpeed", 1.45f);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var npcSerialized = new SerializedObject(npcAgent);
            SetObjectReferenceIfPresent(npcSerialized, "interactable", interactable);
            SetObjectReferenceIfPresent(npcSerialized, "outline", outline);
            SetObjectReferenceIfPresent(npcSerialized, "promptAnchor", promptAnchor);
            SetObjectReferenceIfPresent(npcSerialized, "visualRoot", cap.transform);
            npcSerialized.ApplyModifiedPropertiesWithoutUndo();

            interactable.promptAnchor = promptAnchor;
            EditorUtility.SetDirty(interactable);

            controller.ConfigureForLabMission();
        }

        private static AnimationClip ResolveDanceClip(string assetPath, string clipName)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(clipName))
            {
                return null;
            }

            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (var index = 0; index < assets.Length; index++)
            {
                if (assets[index] is AnimationClip clip && string.Equals(clip.name, clipName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return clip;
                }
            }

            return null;
        }

        private static Transform EnsureCapPromptAnchor(GameObject cap)
        {
            if (cap == null)
            {
                return null;
            }

            var anchor = cap.transform.Find(PromptAnchorObjectName);
            var anchorWasCreated = false;
            if (anchor == null)
            {
                var anchorObject = new GameObject(PromptAnchorObjectName);
                anchor = anchorObject.transform;
                anchor.SetParent(cap.transform, false);
                anchorWasCreated = true;
            }

            if (anchorWasCreated && TryGetCombinedBounds(cap, out var bounds))
            {
                var worldAnchorPosition = new Vector3(
                    bounds.center.x,
                    bounds.max.y + Mathf.Max(bounds.size.y * 0.045f, 0.1f),
                    bounds.center.z);
                anchor.position = worldAnchorPosition;
            }
            else if (anchorWasCreated)
            {
                anchor.localPosition = new Vector3(0f, 1.72f, 0f);
            }

            if (anchorWasCreated)
            {
                anchor.localScale = Vector3.one;
                EditorUtility.SetDirty(anchor);
            }

            return anchor;
        }

        private static void ConfigureInteractable(InteractableItem interactable, SelectableOutline outline, string displayName, string storyId, Transform promptAnchor, float maxDistance)
        {
            interactable.displayName = displayName;
            interactable.storyId = storyId;
            interactable.promptAnchor = promptAnchor;
            interactable.inspectionSourceRoot = interactable.transform;
            interactable.outline = outline;
            interactable.isInteractable = true;
            interactable.maxFocusDistanceOverride = maxDistance;
            EditorUtility.SetDirty(interactable);
        }

        private static void ConfigureRegistry(StoryNpcRegistry registry, StoryNpcAgent capNpc)
        {
            var serialized = new SerializedObject(registry);
            var npcsProperty = serialized.FindProperty("npcs");
            if (npcsProperty != null)
            {
                npcsProperty.arraySize = 1;
                npcsProperty.GetArrayElementAtIndex(0).objectReferenceValue = capNpc;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureBodyUi(LabBodyInspectionUi ui, ClassroomPlayerControlLock controlLock, PanelSettings panelSettings)
        {
            var serialized = new SerializedObject(ui);
            SetObjectReferenceIfPresent(serialized, "controlLock", controlLock);
            SetObjectReferenceIfPresent(serialized, "panelSettings", panelSettings);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigurePuzzleUi(LabLightPuzzleUi ui, ClassroomPlayerControlLock controlLock, PanelSettings panelSettings)
        {
            var serialized = new SerializedObject(ui);
            SetObjectReferenceIfPresent(serialized, "controlLock", controlLock);
            SetObjectReferenceIfPresent(serialized, "uiDocument", ui.GetComponent<UIDocument>());
            SetObjectReferenceIfPresent(serialized, "panelSettings", panelSettings);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject EnsureOverlayRoot(Scene labScene, string objectName, PanelSettings panelSettings, int sortingOrder)
        {
            var overlayRoot = FindGameObjectInScene(labScene, objectName);
            var overlayRootWasCreated = false;
            if (overlayRoot == null)
            {
                overlayRoot = new GameObject(objectName);
                SceneManager.MoveGameObjectToScene(overlayRoot, labScene);
                overlayRootWasCreated = true;
            }

            if (overlayRootWasCreated)
            {
                overlayRoot.transform.SetParent(null, false);
                overlayRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                overlayRoot.transform.localScale = Vector3.one;
            }

            var document = overlayRoot.GetComponent<UIDocument>() ?? overlayRoot.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = sortingOrder;
            EditorUtility.SetDirty(document);
            EditorUtility.SetDirty(overlayRoot);
            return overlayRoot;
        }

        private static LabShrinkTimelinePlayableAsset EnsureShrinkTimelinePlayableAsset()
        {
            EnsureFolder(GeneratedRoot + "/Data", "Timelines");
            var playable = AssetDatabase.LoadAssetAtPath<LabShrinkTimelinePlayableAsset>(ShrinkTimelinePlayablePath);
            if (playable == null)
            {
                playable = ScriptableObject.CreateInstance<LabShrinkTimelinePlayableAsset>();
                AssetDatabase.CreateAsset(playable, ShrinkTimelinePlayablePath);
            }

            EditorUtility.SetDirty(playable);
            return playable;
        }

        private static void ConfigureShrinkController(
            LabShrinkSequenceController controller,
            ClassroomPlayerControlLock controlLock,
            Transform shrinkAnchor,
            InteractionDirector interactionDirector,
            StoryFlowPlayer storyPlayer,
            StoryTimelineDirectorBridge timelineBridge,
            LabSceneContext sceneContext,
            PlayableAsset shrinkTimelinePlayable)
        {
            var serialized = new SerializedObject(controller);
            SetObjectReferenceIfPresent(serialized, "controlLock", controlLock);
            SetObjectReferenceIfPresent(serialized, "interactionDirector", interactionDirector);
            SetObjectReferenceIfPresent(serialized, "storyFlowPlayer", storyPlayer);
            SetObjectReferenceIfPresent(serialized, "timelineBridge", timelineBridge);
            SetObjectReferenceIfPresent(serialized, "sceneContext", sceneContext);
            SetObjectReferenceIfPresent(serialized, "shrinkPlayerAnchor", shrinkAnchor);
            SetObjectReferenceIfPresent(serialized, "shrinkTimelinePlayable", shrinkTimelinePlayable);
            SetStringIfPresent(serialized, "shrinkTimelineCueId", LabShrinkSequenceController.DefaultShrinkTimelineCueId);
            SetStringIfPresent(serialized, "shrinkTimelineCueDisplayName", LabShrinkSequenceController.DefaultShrinkTimelineCueDisplayName);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureFinalCutscene(LabFinalCutsceneController controller, ClassroomPlayerControlLock controlLock)
        {
            var serialized = new SerializedObject(controller);
            SetObjectReferenceIfPresent(serialized, "controlLock", controlLock);
            SetStringIfPresent(serialized, "caption", "CAP and I launch toward the mouth. Tiny mission begins now!");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureCameraFocus(LabCameraFocusController controller, Transform focusTarget)
        {
            if (controller == null)
            {
                return;
            }

            var serialized = new SerializedObject(controller);
            SetObjectReferenceIfPresent(serialized, "focusTarget", focusTarget);
            SetVector3IfPresent(serialized, "cameraSideOffset", new Vector3(-1.45f, 1.18f, 2.35f));
            SetVector3IfPresent(serialized, "lookOffset", new Vector3(0f, 1.12f, 0f));
            SetBooleanIfPresent(serialized, "hideLocalPlayerBodyInConversation", false);
            SetBooleanIfPresent(serialized, "repositionLocalPlayerDuringConversation", false);
            SetFloatIfPresent(serialized, "desiredConversationDistance", 2.1f);
            SetFloatIfPresent(serialized, "desiredConversationDistanceTolerance", 0.55f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureObjectivePanelUi(LabObjectivePanelUi objectivePanelUi, PanelSettings panelSettings)
        {
            if (objectivePanelUi == null)
            {
                return;
            }

            var serialized = new SerializedObject(objectivePanelUi);
            SetStringIfPresent(serialized, "defaultTitle", "Mission");
            SetObjectReferenceIfPresent(serialized, "panelSettings", panelSettings);
            SetObjectReferenceIfPresent(serialized, "uiDocument", null);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureConversationDirector(LabCapConversationDirector director, StoryFlowPlayer player, StoryNpcAgent capNpc, LabCapNpcController capController, ClassroomPlayerControlLock controlLock, LabCameraFocusController cameraFocusController, StoryFlowChannels channels)
        {
            var serialized = new SerializedObject(director);
            SetObjectReferenceIfPresent(serialized, "storyFlowPlayer", player);
            SetObjectReferenceIfPresent(serialized, "capNpc", capNpc);
            SetObjectReferenceIfPresent(serialized, "capController", capController);
            SetObjectReferenceIfPresent(serialized, "controlLock", controlLock);
            SetObjectReferenceIfPresent(serialized, "cameraFocusController", cameraFocusController);
            SetObjectReferenceIfPresent(serialized, "channels", channels);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            director.Configure(channels);
        }

        private static void ConfigurePerformanceTuner(LabPerformanceTuner tuner)
        {
            if (tuner == null)
            {
                return;
            }

            var serialized = new SerializedObject(tuner);
            SetBooleanIfPresent(serialized, "applyOnEnable", true);
            SetIntegerIfPresent(serialized, "targetFrameRate", 60);
            SetFloatIfPresent(serialized, "maxShadowDistance", 22f);
            SetFloatIfPresent(serialized, "lodBias", 0.8f);
            SetBooleanIfPresent(serialized, "disableRealtimeReflectionProbes", true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureSceneContext(
            LabSceneContext context,
            StoryFlowPlayer player,
            StoryNpcRegistry registry,
            StoryNpcAgent capNpc,
            InteractableItem body,
            InteractableItem dnaMachine,
            InteractableItem rocket,
            Transform bodyPreviewAnchor,
            Transform shrinkPlayerAnchor,
            Transform rocketFocusAnchor,
            ClassroomPlayerControlLock controlLock,
            LabCapNpcController capController,
            LabCapConversationDirector conversationDirector,
            LabBodyInspectionUi bodyUi,
            LabLightPuzzleUi puzzleUi,
            LabShrinkSequenceController shrinkController,
            LabFinalCutsceneController finalCutscene,
            LabDoorController doorController,
            LabObjectivePanelUi objectivePanelUi,
            LabCameraFocusController cameraFocusController)
        {
            var serialized = new SerializedObject(context);
            SetObjectReferenceIfPresent(serialized, "storyFlowPlayer", player);
            SetObjectReferenceIfPresent(serialized, "npcRegistry", registry);
            SetObjectReferenceIfPresent(serialized, "capNpc", capNpc);
            SetObjectReferenceIfPresent(serialized, "bodyInteractable", body);
            SetObjectReferenceIfPresent(serialized, "dnaMachineInteractable", dnaMachine);
            SetObjectReferenceIfPresent(serialized, "rocketInteractable", rocket);
            SetObjectReferenceIfPresent(serialized, "bodyPreviewAnchor", bodyPreviewAnchor);
            SetObjectReferenceIfPresent(serialized, "shrinkPlayerAnchor", shrinkPlayerAnchor);
            SetObjectReferenceIfPresent(serialized, "rocketFocusAnchor", rocketFocusAnchor);
            SetObjectReferenceIfPresent(serialized, "controlLock", controlLock);
            SetObjectReferenceIfPresent(serialized, "capNpcController", capController);
            SetObjectReferenceIfPresent(serialized, "capConversationDirector", conversationDirector);
            SetObjectReferenceIfPresent(serialized, "bodyInspectionUi", bodyUi);
            SetObjectReferenceIfPresent(serialized, "lightPuzzleUi", puzzleUi);
            SetObjectReferenceIfPresent(serialized, "shrinkSequenceController", shrinkController);
            SetObjectReferenceIfPresent(serialized, "finalCutsceneController", finalCutscene);
            SetObjectReferenceIfPresent(serialized, "doorController", doorController);
            SetObjectReferenceIfPresent(serialized, "objectivePanelUi", objectivePanelUi);
            SetObjectReferenceIfPresent(serialized, "cameraFocusController", cameraFocusController);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static LabDoorController EnsureDoorController(GameObject brokenDoor)
        {
            if (brokenDoor == null)
            {
                return null;
            }

            var interactable = brokenDoor.GetComponent<InteractableItem>() ?? brokenDoor.AddComponent<InteractableItem>();
            var outline = brokenDoor.GetComponent<SelectableOutline>() ?? brokenDoor.AddComponent<SelectableOutline>();
            ConfigureInteractable(interactable, outline, "Broken Door", "lab.brokenDoor", brokenDoor.transform, 5f);

            var controller = brokenDoor.GetComponent<LabDoorController>() ?? brokenDoor.AddComponent<LabDoorController>();
            return controller;
        }

        private static void ConfigureDoorController(LabDoorController controller)
        {
            if (controller == null)
            {
                return;
            }

            var serialized = new SerializedObject(controller);
            var leftDoor = controller.transform.Find("Door1");
            var rightDoor = controller.transform.Find("Door2");
            var leftDoorLeaf = leftDoor != null ? leftDoor.Find("Top") : null;
            var rightDoorLeaf = rightDoor != null ? rightDoor.Find("Top 2") : null;
            var promptAnchor = EnsureChildTransform(controller.transform, PromptAnchorObjectName, new Vector3(0f, 1.7f, 0f), Quaternion.identity);
            SetObjectReferenceIfPresent(serialized, "interactable", controller.GetComponent<InteractableItem>());
            SetObjectReferenceIfPresent(serialized, "outline", controller.GetComponent<SelectableOutline>());
            SetObjectReferenceIfPresent(serialized, "leftDoor", leftDoor);
            SetObjectReferenceIfPresent(serialized, "rightDoor", rightDoor);
            SetObjectReferenceIfPresent(serialized, "leftDoorLeaf", leftDoorLeaf);
            SetObjectReferenceIfPresent(serialized, "rightDoorLeaf", rightDoorLeaf);
            SetObjectReferenceIfPresent(serialized, "promptAnchorOverride", promptAnchor);
            SetVector3IfPresent(serialized, "leftOpenOffset", new Vector3(-1.15f, 0f, 0f));
            SetVector3IfPresent(serialized, "rightOpenOffset", new Vector3(-1.15f, 0f, 0f));
            SetVector3IfPresent(serialized, "leftLiftOffset", new Vector3(0f, 0.12f, 0f));
            SetVector3IfPresent(serialized, "rightLiftOffset", new Vector3(0f, 0.12f, 0f));
            SetVector3IfPresent(serialized, "promptAnchorLocalOffset", new Vector3(0f, 1.9f, 0f));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ClearStaticFlags(controller.gameObject);
            ClearStaticFlags(leftDoor != null ? leftDoor.gameObject : null);
            ClearStaticFlags(rightDoor != null ? rightDoor.gameObject : null);
            ClearStaticFlags(leftDoorLeaf != null ? leftDoorLeaf.gameObject : null);
            ClearStaticFlags(rightDoorLeaf != null ? rightDoorLeaf.gameObject : null);
            controller.SetUnlocked(false);
            controller.SetOpenImmediate(false);
        }

        private static void ClearStaticFlags(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            GameObjectUtility.SetStaticEditorFlags(target, 0);
            target.isStatic = false;
            EditorUtility.SetDirty(target);
        }

        private static GameObject EnsurePrefabInstance(Scene scene, Transform parent, string objectName, string assetPath, Vector3 position, Quaternion rotation, Vector3 scale, bool preserveExistingTransform = false)
        {
            var existing = FindGameObjectInScene(scene, objectName);
            var wasExisting = existing != null;
            if (existing == null)
            {
                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefabAsset == null)
                {
                    throw new System.InvalidOperationException($"Required Lab asset not found at '{assetPath}'.");
                }

                existing = PrefabUtility.InstantiatePrefab(prefabAsset, scene) as GameObject;
                if (existing == null)
                {
                    throw new System.InvalidOperationException($"Could not instantiate '{assetPath}'.");
                }
            }

            existing.name = objectName;
            existing.transform.SetParent(parent, true);
            if (!wasExisting || !preserveExistingTransform)
            {
                existing.transform.SetPositionAndRotation(position, rotation);
                existing.transform.localScale = scale;
            }
            return existing;
        }

        private static Transform EnsureChildTransform(Transform parent, string childName, Vector3 localPosition, Quaternion localRotation)
        {
            var child = parent.Find(childName);
            var childWasCreated = false;
            if (child == null)
            {
                var childObject = new GameObject(childName);
                child = childObject.transform;
                child.SetParent(parent, false);
                childWasCreated = true;
            }

            if (childWasCreated)
            {
                child.localPosition = localPosition;
                child.localRotation = localRotation;
                child.localScale = Vector3.one;
            }

            return child;
        }

        private static void EnsureCapsuleCollider(GameObject target, float radius, float height, Vector3 center)
        {
            if (target == null)
            {
                return;
            }

            var collider = target.GetComponent<CapsuleCollider>();
            if (collider == null)
            {
                collider = target.AddComponent<CapsuleCollider>();
            }

            if (collider == null)
            {
                return;
            }

            collider.radius = radius;
            collider.height = height;
            collider.center = center;
            EditorUtility.SetDirty(collider);
        }

        private static void EnsureBoxCollider(GameObject target)
        {
            if (target.GetComponent<Collider>() != null)
            {
                return;
            }

            var collider = target.AddComponent<BoxCollider>();
            if (TryGetCombinedBounds(target, out var bounds))
            {
                collider.center = target.transform.InverseTransformPoint(bounds.center);
                var size = bounds.size;
                var lossy = target.transform.lossyScale;
                collider.size = new Vector3(
                    SafeDivide(size.x, lossy.x),
                    SafeDivide(size.y, lossy.y),
                    SafeDivide(size.z, lossy.z));
            }
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Approximately(divisor, 0f) ? value : value / divisor;
        }

        private static void ConfigureLighting(Scene labScene)
        {
            var keyLight = FindGameObjectInScene(labScene, "Directional Light");
            if (keyLight != null)
            {
                var light = keyLight.GetComponent<Light>();
                if (light != null)
                {
                    light.color = new Color(0.69f, 0.79f, 1f, 1f);
                    light.intensity = 0.38f;
                    light.shadows = LightShadows.Soft;
                    keyLight.transform.rotation = Quaternion.Euler(48f, 312f, 0f);
                    EditorUtility.SetDirty(light);
                    EditorUtility.SetDirty(keyLight.transform);
                }
            }

            var fillLight = FindGameObjectInScene(labScene, "Directional Light (1)");
            if (fillLight != null)
            {
                var light = fillLight.GetComponent<Light>();
                if (light != null)
                {
                    light.color = new Color(0.52f, 0.68f, 1f, 1f);
                    light.intensity = 0.62f;
                    light.shadows = LightShadows.None;
                    fillLight.transform.rotation = Quaternion.Euler(63f, 220f, 0f);
                    EditorUtility.SetDirty(light);
                    EditorUtility.SetDirty(fillLight.transform);
                }
            }

            RenderSettings.fog = false;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = 1f;
        }

        private static void ConvertSceneMaterialsToUrp(Scene labScene)
        {
            EnsureFolder("Assets/Core", "Generated");
            EnsureFolder("Assets/Core/Generated", "LabSceneMaterials");

            var roots = labScene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var renderers = roots[rootIndex].GetComponentsInChildren<Renderer>(true);
                for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    var renderer = renderers[rendererIndex];
                    if (renderer == null)
                    {
                        continue;
                    }

                    var sharedMaterials = renderer.sharedMaterials;
                    var changed = false;
                    for (var materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                    {
                        var sourceMaterial = sharedMaterials[materialIndex];
                        if (!ShouldConvertMaterial(sourceMaterial))
                        {
                            continue;
                        }

                        sharedMaterials[materialIndex] = GetOrCreateUrpMaterial(sourceMaterial);
                        changed = true;
                    }

                    if (changed)
                    {
                        renderer.sharedMaterials = sharedMaterials;
                        EditorUtility.SetDirty(renderer);
                    }
                }
            }
        }

        private static bool ShouldConvertMaterial(Material material)
        {
            if (material == null || material.shader == null)
            {
                return false;
            }

            return string.Equals(material.shader.name, "Standard", System.StringComparison.Ordinal) ||
                   material.name.Contains("Wall Set") ||
                   material.name.Contains("Door") ||
                   material.name.Contains("Mechanical") ||
                   material.name.Contains("Techware") ||
                   material.name.Contains("Table Texture") ||
                   material.name.Contains("Chair set") ||
                   material.name.Contains("Shelf") ||
                   material.name.Contains("Vase") ||
                   material.name.Contains("Crates") ||
                   material.name.Contains("Epoxy") ||
                   material.name.Contains("Carpet") ||
                   material.name.Contains("Glass");
        }

        private static Material GetOrCreateUrpMaterial(Material source)
        {
            var safeName = source.name.Replace('/', '-');
            var assetPath = $"{GeneratedMaterialFolder}/{safeName}_URP.mat";
            var target = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (target == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    throw new System.InvalidOperationException("URP/Lit shader could not be found.");
                }

                target = new Material(shader)
                {
                    name = source.name + " URP"
                };
                AssetDatabase.CreateAsset(target, assetPath);
            }

            CopyStandardPropertiesToUrp(source, target);
            EditorUtility.SetDirty(target);
            return target;
        }

        private static void CopyStandardPropertiesToUrp(Material source, Material target)
        {
            target.shader = Shader.Find("Universal Render Pipeline/Lit");
            target.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);

            if (source.HasProperty("_MainTex"))
            {
                target.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            }

            if (source.HasProperty("_BumpMap"))
            {
                var bump = source.GetTexture("_BumpMap");
                target.SetTexture("_BumpMap", bump);
                target.SetFloat("_BumpScale", source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : 1f);
                if (bump != null)
                {
                    target.EnableKeyword("_NORMALMAP");
                }
                else
                {
                    target.DisableKeyword("_NORMALMAP");
                }
            }

            if (source.HasProperty("_MetallicGlossMap"))
            {
                var metallicGloss = source.GetTexture("_MetallicGlossMap");
                target.SetTexture("_MetallicGlossMap", metallicGloss);
                if (metallicGloss != null)
                {
                    target.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                else
                {
                    target.DisableKeyword("_METALLICSPECGLOSSMAP");
                }
            }

            if (source.HasProperty("_Metallic"))
            {
                target.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            }

            if (source.HasProperty("_Glossiness"))
            {
                target.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
            }

            if (source.HasProperty("_OcclusionMap"))
            {
                target.SetTexture("_OcclusionMap", source.GetTexture("_OcclusionMap"));
            }

            if (source.HasProperty("_OcclusionStrength"))
            {
                target.SetFloat("_OcclusionStrength", source.GetFloat("_OcclusionStrength"));
            }

            if (source.HasProperty("_EmissionMap"))
            {
                var emissionMap = source.GetTexture("_EmissionMap");
                target.SetTexture("_EmissionMap", emissionMap);
                if (emissionMap != null)
                {
                    target.EnableKeyword("_EMISSION");
                }
                else
                {
                    target.DisableKeyword("_EMISSION");
                }
            }

            if (source.HasProperty("_EmissionColor"))
            {
                target.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
            }

            if (source.HasProperty("_Cutoff"))
            {
                var cutoff = source.GetFloat("_Cutoff");
                target.SetFloat("_Cutoff", cutoff);
                target.SetFloat("_AlphaClip", cutoff > 0.001f ? 1f : 0f);
            }
        }

        private static void EnsureMainCameraAudioListener(Scene labScene)
        {
            var mainCameraObject = FindGameObjectInScene(labScene, "Main Camera");
            if (mainCameraObject == null)
            {
                return;
            }

            if (mainCameraObject.GetComponent<AudioListener>() == null)
            {
                mainCameraObject.AddComponent<AudioListener>();
            }

            mainCameraObject.transform.position = new Vector3(2.5f, 2.1f, 31.2f);
            mainCameraObject.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
            EditorUtility.SetDirty(mainCameraObject);
            EditorUtility.SetDirty(mainCameraObject.transform);
        }

        private static BoxCollider EnsureWalkableFloor(Scene labScene, Transform primarySpawnPad)
        {
            if (primarySpawnPad == null)
            {
                return null;
            }

            var floorObject = FindGameObjectInScene(labScene, "LabSpawnFloorCollider");
            if (floorObject == null)
            {
                floorObject = new GameObject("LabSpawnFloorCollider");
                SceneManager.MoveGameObjectToScene(floorObject, labScene);
            }

            var floorCollider = floorObject.GetComponent<BoxCollider>();
            if (floorCollider == null)
            {
                floorCollider = floorObject.AddComponent<BoxCollider>();
            }

            var floorCenter = primarySpawnPad.position + new Vector3(0f, -0.24f, 0f);
            var floorSize = new Vector3(8f, 0.5f, 8f);
            var officeRoot = FindGameObjectInScene(labScene, "Office 1");
            if (TryGetCombinedBounds(officeRoot, out var bounds))
            {
                floorCenter = new Vector3(primarySpawnPad.position.x, bounds.min.y - 0.23f, primarySpawnPad.position.z);
                floorSize = new Vector3(
                    Mathf.Max(8f, Mathf.Min(14f, bounds.size.x * 0.55f)),
                    0.5f,
                    Mathf.Max(8f, Mathf.Min(14f, bounds.size.z * 0.4f)));
            }

            floorObject.transform.SetPositionAndRotation(floorCenter, Quaternion.identity);
            floorCollider.center = Vector3.zero;
            floorCollider.size = floorSize;
            EditorUtility.SetDirty(floorObject);
            EditorUtility.SetDirty(floorCollider);
            return floorCollider;
        }

        private static void EnsureBuildSettings()
        {
            var existing = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            AddSceneIfMissing(existing, LabScenePath);
            AddSceneIfMissing(existing, TransitionScenePath);
            EditorBuildSettings.scenes = existing.ToArray();
        }

        private static void AddSceneIfMissing(List<EditorBuildSettingsScene> scenes, string path)
        {
            if (scenes.Exists(scene => scene.path == path))
            {
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
        }

        private static GameObject FindGameObjectInScene(Scene scene, string objectName)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            var roots = scene.GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
            {
                var root = roots[index];
                if (root.name == objectName)
                {
                    return root;
                }

                var child = FindChildRecursive(root.transform, objectName);
                if (child != null)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private static GameObject FindGameObjectByPathInScene(Scene scene, string objectPath)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrWhiteSpace(objectPath))
            {
                return null;
            }

            var parts = objectPath.Split('/');
            if (parts.Length == 0)
            {
                return null;
            }

            var roots = scene.GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
            {
                var root = roots[index];
                if (root == null || root.name != parts[0])
                {
                    continue;
                }

                var current = root.transform;
                var matched = true;
                for (var partIndex = 1; partIndex < parts.Length; partIndex++)
                {
                    current = current.Find(parts[partIndex]);
                    if (current == null)
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched && current != null)
                {
                    return current.gameObject;
                }
            }

            return null;
        }

        private static Transform FindChildRecursive(Transform root, string objectName)
        {
            for (var index = 0; index < root.childCount; index++)
            {
                var child = root.GetChild(index);
                if (child.name == objectName)
                {
                    return child;
                }

                var nested = FindChildRecursive(child, objectName);
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
                if (renderer == null)
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

            if (hasBounds)
            {
                return true;
            }

            var colliders = target.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (collider == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }

        private static string EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent))
            {
                var parts = parent.Split('/');
                var current = "Assets";
                for (var index = 1; index < parts.Length; index++)
                {
                    var next = current + "/" + parts[index];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[index]);
                    }

                    current = next;
                }
            }

            var path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }

            return path;
        }

        private static void SetObjectReferenceIfPresent(SerializedObject serializedObject, string propertyName, Object value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetStringIfPresent(SerializedObject serializedObject, string propertyName, string value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.stringValue = value;
            }
        }

        private static void SetBooleanIfPresent(SerializedObject serializedObject, string propertyName, bool value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetFloatIfPresent(SerializedObject serializedObject, string propertyName, float value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetIntegerIfPresent(SerializedObject serializedObject, string propertyName, int value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void SetVector3IfPresent(SerializedObject serializedObject, string propertyName, Vector3 value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.vector3Value = value;
            }
        }

        private static T ResolveGeneratedAsset<T>(string expectedPath, T fallback) where T : Object
        {
            if (fallback != null)
            {
                return fallback;
            }

            var asset = AssetDatabase.LoadAssetAtPath<T>(expectedPath);
            if (asset != null)
            {
                return asset;
            }

            var searchFolders = new[] { GeneratedRoot };
            var assetPath = AssetDatabase.FindAssets($"t:{typeof(T).Name}", searchFolders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => path.StartsWith(GeneratedRoot));

            return string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        private static void RepairGraphNodeReferences(string graphPath, StoryGraphAsset graph)
        {
            if (graph == null || string.IsNullOrWhiteSpace(graphPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(graphPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var nodeFolder = $"{directory}/{Path.GetFileNameWithoutExtension(graphPath)}_Nodes";
            if (!AssetDatabase.IsValidFolder(nodeFolder))
            {
                return;
            }

            var nodes = AssetDatabase.FindAssets("t:StoryNodeAsset", new[] { nodeFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<StoryNodeAsset>)
                .Where(node => node != null)
                .OrderBy(node => node.EditorPosition.x)
                .ThenBy(node => node.EditorPosition.y)
                .ThenBy(node => node.NodeId)
                .ToList();

            if (nodes.Count == 0)
            {
                return;
            }

            var serializedGraph = new SerializedObject(graph);
            var nodesProperty = serializedGraph.FindProperty("nodes");
            if (nodesProperty == null)
            {
                return;
            }

            nodesProperty.arraySize = nodes.Count;
            for (var index = 0; index < nodes.Count; index++)
            {
                nodesProperty.GetArrayElementAtIndex(index).objectReferenceValue = nodes[index];
            }

            serializedGraph.ApplyModifiedPropertiesWithoutUndo();
            graph.InvalidateCache();
            EditorUtility.SetDirty(graph);
            AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
