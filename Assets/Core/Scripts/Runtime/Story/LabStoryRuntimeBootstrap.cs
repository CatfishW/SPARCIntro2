using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Blocks.Gameplay.Core;
using ItemInteraction;
using ModularStoryFlow.Runtime.Actions;
using ModularStoryFlow.Runtime.Bridges;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.Player;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-900)]
    public sealed class LabStoryRuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private StoryFlowPlayer player;
        [SerializeField] private LabStoryUiRoot uiRoot;
        [SerializeField] private LabStoryInteractionBridge interactionBridge;
        [SerializeField] private LabStorySceneTransition sceneTransition;
        [SerializeField] private StoryTimelineDirectorBridge timelineBridge;
        [SerializeField] private bool restartStoryOnBootstrap = true;
        [SerializeField, Min(0f)] private float initialStoryStartDelaySeconds = 1.2f;
        [SerializeField] private string spawnAnchorPath = "_Spawns/Pfb_SpawnPad";
        [SerializeField, Min(0f)] private float spawnVerticalOffset = 0.08f;
        [SerializeField] private string spawnLookTargetPath = "Office 1/Mechanical arm 2";
        [SerializeField] private string targetScenePath = "Assets/Core/TestScenes/LabTransitionScene.unity";
        [SerializeField] private string targetSceneName = "LabTransitionScene";
        [SerializeField] private string savedProjectConfigPath = "Assets/StoryFlowLabGenerated/Config/StoryFlowProjectConfig.asset";
        [SerializeField] private string savedGraphPath = "Assets/StoryFlowLabGenerated/Graphs/LabStoryRuntime.asset";

        private StoryFlowProjectConfig runtimeConfig;
        private StoryGraphAsset runtimeGraph;
        private StoryFlowChannels runtimeChannels;
        private StoryDialogueRequestChannel dialogueRequests;
        private StoryAdvanceCommandChannel advanceCommands;
        private StoryChoiceRequestChannel choiceRequests;
        private StoryChoiceSelectionChannel choiceSelections;
        private StoryTimelineRequestChannel timelineRequests;
        private StoryTimelineResultChannel timelineResults;
        private StorySignalRaisedChannel raisedSignals;
        private StoryExternalSignalChannel externalSignals;
        private StoryStateChangedChannel stateChanged;
        private StoryNodeNotificationChannel nodeNotifications;
        private StoryGraphNotificationChannel graphNotifications;
        private bool runtimeAssetsOwned = true;
        private BoxCollider runtimeSpawnSafetyFloor;

#if UNITY_EDITOR
        private StoryFlowProjectConfig loadedPersistedConfig;
        private StoryGraphAsset loadedPersistedGraph;
#endif

        private void Awake()
        {
            player = player != null ? player : GetComponent<StoryFlowPlayer>();
            if (player == null)
            {
                player = gameObject.AddComponent<StoryFlowPlayer>();
            }

            uiRoot = uiRoot != null ? uiRoot : GetComponent<LabStoryUiRoot>();
            if (uiRoot == null)
            {
                uiRoot = gameObject.AddComponent<LabStoryUiRoot>();
            }

            interactionBridge = interactionBridge != null ? interactionBridge : GetComponent<LabStoryInteractionBridge>();
            if (interactionBridge == null)
            {
                interactionBridge = gameObject.AddComponent<LabStoryInteractionBridge>();
            }

            sceneTransition = sceneTransition != null ? sceneTransition : GetComponent<LabStorySceneTransition>();
            if (sceneTransition == null)
            {
                sceneTransition = gameObject.AddComponent<LabStorySceneTransition>();
            }

            timelineBridge = timelineBridge != null ? timelineBridge : Object.FindFirstObjectByType<StoryTimelineDirectorBridge>();

            if (GetComponent<InteractionDirector>() == null)
            {
                gameObject.AddComponent<InteractionDirector>();
            }

            BuildRuntimeProject();
            InjectRuntimeProject();
        }

        private void Start()
        {
            StartCoroutine(PositionPlayerAtSpawnRoutine());
            StartCoroutine(BeginStoryAfterCooldownRoutine());
        }

        private IEnumerator BeginStoryAfterCooldownRoutine()
        {
            var sceneContext = FindFirstObjectByType<LabSceneContext>(FindObjectsInactive.Include);
            sceneContext?.SetStartupCooldownActive(true);
            interactionBridge?.RefreshScenePresentation();
            sceneContext?.ObjectivePanelUi?.ShowStage(
                LabObjectivePanelUi.ObjectiveStage.EnteringLab,
                "Get ready. CAP will brief you in a moment.",
                "Entering Lab");

            if (initialStoryStartDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(initialStoryStartDelaySeconds);
            }

            var graphToStart = runtimeGraph != null ? runtimeGraph : player != null ? player.InitialGraph : null;
            if (restartStoryOnBootstrap && player != null && graphToStart != null)
            {
                sceneContext?.SetStartupCooldownActive(false);
                interactionBridge?.RefreshScenePresentation();
                player.StartStory(graphToStart);
                yield break;
            }

            sceneContext?.SetStartupCooldownActive(false);
            interactionBridge?.RefreshScenePresentation();
        }

        private void OnDestroy()
        {
            if (!runtimeAssetsOwned)
            {
                return;
            }

            DestroyRuntimeAsset(runtimeGraph);
            DestroyRuntimeAsset(runtimeConfig);
            DestroyRuntimeAsset(runtimeChannels);
            DestroyRuntimeAsset(dialogueRequests);
            DestroyRuntimeAsset(advanceCommands);
            DestroyRuntimeAsset(choiceRequests);
            DestroyRuntimeAsset(choiceSelections);
            DestroyRuntimeAsset(timelineRequests);
            DestroyRuntimeAsset(timelineResults);
            DestroyRuntimeAsset(raisedSignals);
            DestroyRuntimeAsset(externalSignals);
            DestroyRuntimeAsset(stateChanged);
            DestroyRuntimeAsset(nodeNotifications);
            DestroyRuntimeAsset(graphNotifications);
        }

        private void BuildRuntimeProject()
        {
            if (TryLoadPersistedProject())
            {
                runtimeAssetsOwned = false;
                return;
            }

            runtimeAssetsOwned = true;

            dialogueRequests = ScriptableObject.CreateInstance<StoryDialogueRequestChannel>();
            advanceCommands = ScriptableObject.CreateInstance<StoryAdvanceCommandChannel>();
            choiceRequests = ScriptableObject.CreateInstance<StoryChoiceRequestChannel>();
            choiceSelections = ScriptableObject.CreateInstance<StoryChoiceSelectionChannel>();
            timelineRequests = ScriptableObject.CreateInstance<StoryTimelineRequestChannel>();
            timelineResults = ScriptableObject.CreateInstance<StoryTimelineResultChannel>();
            raisedSignals = ScriptableObject.CreateInstance<StorySignalRaisedChannel>();
            externalSignals = ScriptableObject.CreateInstance<StoryExternalSignalChannel>();
            stateChanged = ScriptableObject.CreateInstance<StoryStateChangedChannel>();
            nodeNotifications = ScriptableObject.CreateInstance<StoryNodeNotificationChannel>();
            graphNotifications = ScriptableObject.CreateInstance<StoryGraphNotificationChannel>();

            runtimeChannels = ScriptableObject.CreateInstance<StoryFlowChannels>();
            runtimeChannels.Configure(
                dialogueRequests,
                advanceCommands,
                choiceRequests,
                choiceSelections,
                timelineRequests,
                timelineResults,
                raisedSignals,
                externalSignals,
                stateChanged,
                nodeNotifications,
                graphNotifications);

            var variables = ScriptableObject.CreateInstance<StoryVariableCatalog>();
            var exitReady = CreateBooleanVariable("lab.exitReady", false);
            variables.SetVariables(new StoryVariableDefinition[] { exitReady });

            var states = ScriptableObject.CreateInstance<StoryStateMachineCatalog>();
            var progression = CreateStateMachine(
                "LabStory.Progress",
                "FreshSpawn",
                "FreshSpawn",
                "MeetCap",
                "BodyInspectionReady",
                "BodyInspected",
                "PuzzleReady",
                "PuzzleSolved",
                "ShrinkReady",
                "Shrunk",
                "RocketReady",
                "TransitionCommitted");
            states.SetStateMachines(new[] { progression });

            var timelineCatalog = ScriptableObject.CreateInstance<StoryTimelineCatalog>();
            var graphRegistry = ScriptableObject.CreateInstance<StoryGraphRegistry>();
            runtimeGraph = BuildGraph(exitReady, progression);
            graphRegistry.SetGraphs(new[] { runtimeGraph });

            runtimeConfig = ScriptableObject.CreateInstance<StoryFlowProjectConfig>();
            SetField(runtimeConfig, "graphRegistry", graphRegistry);
            SetField(runtimeConfig, "variableCatalog", variables);
            SetField(runtimeConfig, "stateMachineCatalog", states);
            SetField(runtimeConfig, "timelineCatalog", timelineCatalog);
            SetField(runtimeConfig, "channels", runtimeChannels);
            SetField(runtimeConfig, "saveProvider", null);
        }

        private bool TryLoadPersistedProject()
        {
#if UNITY_EDITOR
            var loadedConfig = AssetDatabase.LoadAssetAtPath<StoryFlowProjectConfig>(savedProjectConfigPath);
            var loadedGraph = AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(savedGraphPath);
            loadedPersistedConfig = loadedConfig;
            loadedPersistedGraph = loadedGraph;

            if (!IsPersistedProjectUsable(loadedConfig, loadedGraph))
            {
                return false;
            }

            runtimeConfig = loadedConfig;
            runtimeGraph = loadedGraph;
            runtimeChannels = loadedConfig.Channels;
            return runtimeChannels != null;
#else
            return false;
#endif
        }

        private void InjectRuntimeProject()
        {
            if (player != null)
            {
                if (runtimeConfig != null)
                {
                    player.ProjectConfig = runtimeConfig;
                }

                if (runtimeGraph != null)
                {
                    player.InitialGraph = runtimeGraph;
                }

                player.PlayOnStart = false;
                SetBoolField(player, "autoLoadSaveOnStart", false);

#if UNITY_EDITOR
                EditorUtility.SetDirty(player);
#endif
            }

            uiRoot?.Configure(runtimeChannels);
            if (sceneTransition != null)
            {
                sceneTransition.Configure(targetScenePath, targetSceneName);
            }

            timelineBridge?.Configure(runtimeConfig);

            interactionBridge?.Configure(runtimeChannels, sceneTransition);
            interactionBridge?.SetSessionId(player != null ? player.SessionId : string.Empty);
        }

#if UNITY_EDITOR
        private static bool IsPersistedProjectUsable(StoryFlowProjectConfig config, StoryGraphAsset graph)
        {
            if (config == null || graph == null)
            {
                return false;
            }

            var channels = config.Channels;
            if (channels == null ||
                channels.DialogueRequests == null ||
                channels.AdvanceCommands == null ||
                channels.ChoiceRequests == null ||
                channels.ChoiceSelections == null ||
                channels.TimelineRequests == null ||
                channels.TimelineResults == null ||
                channels.RaisedSignals == null ||
                channels.ExternalSignals == null ||
                channels.StateChanged == null ||
                channels.NodeNotifications == null ||
                channels.GraphNotifications == null)
            {
                return false;
            }

            if (config.GraphRegistry == null)
            {
                return false;
            }

            var resolvedGraph = config.GraphRegistry.Resolve(graph.GraphId);
            if (resolvedGraph == null)
            {
                return false;
            }

            if (graph.Nodes == null || graph.Nodes.Count < 2 || graph.Connections == null || graph.Connections.Count == 0)
            {
                return false;
            }

            return graph.GetEntryNode() != null && graph.HasAnyStartNode();
        }
#endif

        private StoryGraphAsset BuildGraph(
            StoryBooleanVariableDefinition exitReady,
            StoryStateMachineDefinition progression)
        {
            var graph = ScriptableObject.CreateInstance<StoryGraphAsset>();
            graph.name = "LabStoryRuntime";

            var start = CreateNode<StartNodeAsset>();
            var settleDelay = CreateDelayNode(0.5f);
            var introBriefingAction = CreateActionNode(CreateStartIntroBriefingAction());
            var waitIntro = CreateWaitSignalNode(CreateSignal(LabStorySignals.IntroBriefingCompleted));
            var meetCapAction = CreateActionNode(CreateSetStateAction(progression, "MeetCap"));
            var waitCap = CreateWaitSignalNode(CreateSignal(LabStorySignals.CapTalked));
            var bodyReadyAction = CreateActionNode(CreateSetStateAction(progression, "BodyInspectionReady"));
            var waitBody = CreateWaitSignalNode(CreateSignal(LabStorySignals.BodyInspected));
            var bodyInspectedAction = CreateActionNode(CreateSetStateAction(progression, "BodyInspected"));
            var puzzleReadyAction = CreateActionNode(CreateCompositeAction(
                CreateSetStateAction(progression, "PuzzleReady"),
                CreateRaiseSignalAction(CreateSignal(LabStorySignals.PuzzleStarted), "puzzle-started")));
            var waitPuzzle = CreateWaitSignalNode(CreateSignal(LabStorySignals.PuzzleSolved));
            var puzzleSolvedAction = CreateActionNode(CreateSetStateAction(progression, "PuzzleSolved"));
            var shrinkReadyAction = CreateActionNode(CreateSetStateAction(progression, "ShrinkReady"));
            var waitShrink = CreateWaitSignalNode(CreateSignal(LabStorySignals.ShrinkConfirmed));
            var shrunkAction = CreateActionNode(CreateSetStateAction(progression, "Shrunk"));
            var rocketReadyAction = CreateActionNode(CreateCompositeAction(
                CreateSetVariableAction(exitReady, true),
                CreateSetStateAction(progression, "RocketReady")));
            var waitRocket = CreateWaitSignalNode(CreateSignal(LabStorySignals.RocketEntered));
            var cutsceneCommittedAction = CreateActionNode(CreateSetStateAction(progression, "CutsceneCommitted"));
            var waitCutscene = CreateWaitSignalNode(CreateSignal(LabStorySignals.CutsceneCompleted));
            var end = CreateNode<EndNodeAsset>();

            AddNode(graph, start, true);
            AddNode(graph, settleDelay);
            AddNode(graph, introBriefingAction);
            AddNode(graph, waitIntro);
            AddNode(graph, meetCapAction);
            AddNode(graph, waitCap);
            AddNode(graph, bodyReadyAction);
            AddNode(graph, waitBody);
            AddNode(graph, bodyInspectedAction);
            AddNode(graph, puzzleReadyAction);
            AddNode(graph, waitPuzzle);
            AddNode(graph, puzzleSolvedAction);
            AddNode(graph, shrinkReadyAction);
            AddNode(graph, waitShrink);
            AddNode(graph, shrunkAction);
            AddNode(graph, rocketReadyAction);
            AddNode(graph, waitRocket);
            AddNode(graph, cutsceneCommittedAction);
            AddNode(graph, waitCutscene);
            AddNode(graph, end);

            AddConnection(graph, start.NodeId, StoryNodeAsset.DefaultOutputPortId, settleDelay.NodeId);
            AddConnection(graph, settleDelay.NodeId, StoryNodeAsset.DefaultOutputPortId, introBriefingAction.NodeId);
            AddConnection(graph, introBriefingAction.NodeId, StoryNodeAsset.DefaultOutputPortId, waitIntro.NodeId);
            AddConnection(graph, waitIntro.NodeId, StoryNodeAsset.DefaultOutputPortId, meetCapAction.NodeId);
            AddConnection(graph, meetCapAction.NodeId, StoryNodeAsset.DefaultOutputPortId, waitCap.NodeId);
            AddConnection(graph, waitCap.NodeId, StoryNodeAsset.DefaultOutputPortId, bodyReadyAction.NodeId);
            AddConnection(graph, bodyReadyAction.NodeId, StoryNodeAsset.DefaultOutputPortId, waitBody.NodeId);
            AddConnection(graph, waitBody.NodeId, StoryNodeAsset.DefaultOutputPortId, bodyInspectedAction.NodeId);
            AddConnection(graph, bodyInspectedAction.NodeId, StoryNodeAsset.DefaultOutputPortId, puzzleReadyAction.NodeId);
            AddConnection(graph, puzzleReadyAction.NodeId, StoryNodeAsset.DefaultOutputPortId, waitPuzzle.NodeId);
            AddConnection(graph, waitPuzzle.NodeId, StoryNodeAsset.DefaultOutputPortId, puzzleSolvedAction.NodeId);
            AddConnection(graph, puzzleSolvedAction.NodeId, StoryNodeAsset.DefaultOutputPortId, shrinkReadyAction.NodeId);
            AddConnection(graph, shrinkReadyAction.NodeId, StoryNodeAsset.DefaultOutputPortId, waitShrink.NodeId);
            AddConnection(graph, waitShrink.NodeId, StoryNodeAsset.DefaultOutputPortId, shrunkAction.NodeId);
            AddConnection(graph, shrunkAction.NodeId, StoryNodeAsset.DefaultOutputPortId, rocketReadyAction.NodeId);
            AddConnection(graph, rocketReadyAction.NodeId, StoryNodeAsset.DefaultOutputPortId, waitRocket.NodeId);
            AddConnection(graph, waitRocket.NodeId, StoryNodeAsset.DefaultOutputPortId, cutsceneCommittedAction.NodeId);
            AddConnection(graph, cutsceneCommittedAction.NodeId, StoryNodeAsset.DefaultOutputPortId, waitCutscene.NodeId);
            AddConnection(graph, waitCutscene.NodeId, StoryNodeAsset.DefaultOutputPortId, end.NodeId);

            graph.EnsureStableIds();
            return graph;
        }

        private IEnumerator PositionPlayerAtSpawnRoutine()
        {
            const float timeoutSeconds = 12f;
            var remainingSeconds = timeoutSeconds;

            while (remainingSeconds > 0f)
            {
                if (TryResolveLocalPlayerMovement(out var movement) && TryComputeSpawnPose(out var position, out var rotation))
                {
                    EnsureRuntimeSpawnSafetyFloor(position);
                    ApplySpawnPose(movement, position, rotation);
                    yield break;
                }

                remainingSeconds -= 0.1f;
                yield return new WaitForSeconds(0.1f);
            }

            if (TryComputeSpawnPose(out var fallbackPosition, out var fallbackRotation))
            {
                EnsureRuntimeSpawnSafetyFloor(fallbackPosition);

                if (TryResolveLocalPlayerMovement(out var lateMovement))
                {
                    ApplySpawnPose(lateMovement, fallbackPosition, fallbackRotation);
                    yield break;
                }

                var mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    mainCamera.transform.SetPositionAndRotation(
                        fallbackPosition + new Vector3(0f, 1.45f, 0f),
                        fallbackRotation);
                }
            }
        }

        private static void ApplySpawnPose(CoreMovement movement, Vector3 position, Quaternion rotation)
        {
            if (movement == null)
            {
                return;
            }

            if (!movement.gameObject.activeSelf)
            {
                movement.gameObject.SetActive(true);
            }

            movement.transform.rotation = rotation;

            var localScale = movement.transform.localScale;
            if ((localScale - Vector3.one).sqrMagnitude > 0.0001f)
            {
                movement.transform.localScale = Vector3.one;
            }

            var characterController = movement.GetComponent<CharacterController>();
            if (characterController != null && !characterController.enabled)
            {
                characterController.enabled = true;
            }

            movement.SetPosition(position);
            movement.SetVerticalVelocity(0f);
            movement.ResetMovementForces();

            var manager = movement.GetComponent<CorePlayerManager>() ??
                          movement.GetComponentInParent<CorePlayerManager>();
            manager?.SetMovementInputEnabled(true);
        }

        private bool TryComputeSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var spawnAnchor = FindTransformByPath(gameObject.scene, spawnAnchorPath) ?? FindTransformByPath(spawnAnchorPath);
            if (spawnAnchor != null)
            {
                position = spawnAnchor.position + new Vector3(0f, spawnVerticalOffset, 0f);
                rotation = spawnAnchor.rotation;
                return true;
            }

            var fallback =
                FindTransformByPath(gameObject.scene, spawnLookTargetPath) ??
                FindTransformByPath(spawnLookTargetPath) ??
                FindTransformByPath(gameObject.scene, "Office 1/Mechanical arm 2") ??
                FindTransformByPath("Office 1/Mechanical arm 2");
            if (fallback == null)
            {
                return false;
            }

            var fallbackPosition = fallback.position;
            position = fallbackPosition + new Vector3(-1.6f, spawnVerticalOffset, -2.4f);
            rotation = Quaternion.LookRotation((fallbackPosition - position).normalized, Vector3.up);
            return true;
        }

        private static Transform FindTransformByPath(string hierarchyPath)
        {
            if (string.IsNullOrWhiteSpace(hierarchyPath))
            {
                return null;
            }

            var segments = hierarchyPath.Split('/');
            if (segments.Length == 0)
            {
                return null;
            }

            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
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
            }

            return null;
        }

        private void EnsureRuntimeSpawnSafetyFloor(Vector3 spawnPosition)
        {
            if (runtimeSpawnSafetyFloor == null)
            {
                var floorObject = new GameObject("LabRuntimeSpawnSafetyFloor");
                SceneManager.MoveGameObjectToScene(floorObject, gameObject.scene);
                runtimeSpawnSafetyFloor = floorObject.AddComponent<BoxCollider>();
            }

            var floorTransform = runtimeSpawnSafetyFloor.transform;
            floorTransform.position = spawnPosition + new Vector3(0f, -0.32f, 0f);
            floorTransform.rotation = Quaternion.identity;
            runtimeSpawnSafetyFloor.center = Vector3.zero;
            runtimeSpawnSafetyFloor.size = new Vector3(6f, 0.5f, 6f);
        }

        private static Transform FindTransformByPath(Scene scene, string hierarchyPath)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrWhiteSpace(hierarchyPath))
            {
                return null;
            }

            var segments = hierarchyPath.Split('/');
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

        private static bool TryResolveLocalPlayerMovement(out CoreMovement movement)
        {
            movement = null;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                var localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (localPlayerObject != null)
                {
                    movement = localPlayerObject.GetComponent<CoreMovement>() ??
                               localPlayerObject.GetComponentInChildren<CoreMovement>(true);
                    if (movement != null)
                    {
                        return true;
                    }
                }
            }

            var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                movement = taggedPlayer.GetComponent<CoreMovement>() ??
                           taggedPlayer.GetComponentInChildren<CoreMovement>(true);
                if (movement != null)
                {
                    return true;
                }
            }

            var managers = FindObjectsByType<CorePlayerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < managers.Length; index++)
            {
                if (managers[index] == null || !managers[index].IsOwner)
                {
                    continue;
                }

                movement = managers[index].CoreMovement != null
                    ? managers[index].CoreMovement
                    : managers[index].GetComponent<CoreMovement>() ??
                      managers[index].GetComponentInChildren<CoreMovement>(true);
                return movement != null;
            }

            return false;
        }

        private static T CreateNode<T>() where T : StoryNodeAsset
        {
            var node = ScriptableObject.CreateInstance<T>();
            node.name = typeof(T).Name;
            node.EnsureStableIds();
            return node;
        }

        private static DialogueNodeAsset CreateDialogueNode(string speaker, string body, bool autoAdvance, float autoAdvanceDelaySeconds)
        {
            var node = CreateNode<DialogueNodeAsset>();
            SetStringField(node, "speakerDisplayName", speaker);
            SetStringField(node, "body", body);
            SetBoolField(node, "autoAdvance", autoAdvance);
            SetFloatField(node, "autoAdvanceDelaySeconds", autoAdvanceDelaySeconds);
            return node;
        }

        private static DelayNodeAsset CreateDelayNode(float seconds)
        {
            var node = CreateNode<DelayNodeAsset>();
            SetFloatField(node, "seconds", seconds);
            return node;
        }

        private static WaitSignalNodeAsset CreateWaitSignalNode(StorySignalDefinition signal)
        {
            var node = CreateNode<WaitSignalNodeAsset>();
            SetField(node, "signal", signal);
            return node;
        }

        private static ActionNodeAsset CreateActionNode(StoryActionAsset action)
        {
            var node = CreateNode<ActionNodeAsset>();
            SetField(node, "actions", new List<StoryActionAsset> { action });
            return node;
        }

        private static StoryBooleanVariableDefinition CreateBooleanVariable(string key, bool defaultValue)
        {
            var variable = ScriptableObject.CreateInstance<StoryBooleanVariableDefinition>();
            variable.name = key;
            SetStringField(variable, "key", key);
            SetBoolField(variable, "defaultValue", defaultValue);
            return variable;
        }

        private static StoryStateMachineDefinition CreateStateMachine(string machineId, string defaultStateId, params string[] states)
        {
            var stateMachine = ScriptableObject.CreateInstance<StoryStateMachineDefinition>();
            stateMachine.name = machineId;
            SetStringField(stateMachine, "machineId", machineId);
            SetStringField(stateMachine, "defaultStateId", defaultStateId);
            var entries = new List<StoryStateDefinition>();
            foreach (var state in states)
            {
                entries.Add(new StoryStateDefinition { Id = state, DisplayName = state });
            }

            SetField(stateMachine, "states", entries);
            return stateMachine;
        }

        private static StorySignalDefinition CreateSignal(string signalId)
        {
            var signal = ScriptableObject.CreateInstance<StorySignalDefinition>();
            signal.name = signalId;
            SetStringField(signal, "signalId", signalId);
            return signal;
        }

        private static StorySetVariableActionAsset CreateSetVariableAction(StoryVariableDefinition variable, bool value)
        {
            var action = ScriptableObject.CreateInstance<StorySetVariableActionAsset>();
            SetField(action, "variable", variable);
            SetBoolField(action, "booleanValue", value);
            return action;
        }

        private static StorySetStateActionAsset CreateSetStateAction(StoryStateMachineDefinition stateMachine, string stateId)
        {
            var action = ScriptableObject.CreateInstance<StorySetStateActionAsset>();
            SetField(action, "stateMachine", stateMachine);
            SetStringField(action, "stateId", stateId);
            return action;
        }

        private static StoryCompositeActionAsset CreateCompositeAction(params StoryActionAsset[] actions)
        {
            var action = ScriptableObject.CreateInstance<StoryCompositeActionAsset>();
            SetField(action, "actions", new List<StoryActionAsset>(actions));
            return action;
        }

        private static StoryRaiseSignalActionAsset CreateRaiseSignalAction(StorySignalDefinition signal, string payload)
        {
            var action = ScriptableObject.CreateInstance<StoryRaiseSignalActionAsset>();
            SetField(action, "signal", signal);
            SetStringField(action, "payload", payload ?? string.Empty);
            return action;
        }

        private static LabStartIntroBriefingActionAsset CreateStartIntroBriefingAction()
        {
            return ScriptableObject.CreateInstance<LabStartIntroBriefingActionAsset>();
        }

        private static void AddNode(StoryGraphAsset graph, StoryNodeAsset node, bool isEntry = false)
        {
            var nodes = GetField<List<StoryNodeAsset>>(graph, "nodes");
            nodes.Add(node);
            if (isEntry)
            {
                SetStringField(graph, "entryNodeId", node.NodeId);
            }
        }

        private static void AddConnection(StoryGraphAsset graph, string fromNodeId, string fromPortId, string toNodeId)
        {
            var connections = GetField<List<StoryConnection>>(graph, "connections");
            var connection = new StoryConnection
            {
                FromNodeId = fromNodeId,
                FromPortId = fromPortId,
                ToNodeId = toNodeId,
                ToPortId = StoryNodeAsset.DefaultInputPortId
            };
            connection.EnsureStableId();
            connections.Add(connection);
        }

        private static T GetField<T>(object target, string fieldName) where T : class
        {
            return typeof(object) == typeof(T)
                ? null
                : target?.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) as T;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);
        }

        private static void SetStringField(object target, string fieldName, string value)
        {
            SetField(target, fieldName, value);
        }

        private static void SetBoolField(object target, string fieldName, bool value)
        {
            SetField(target, fieldName, value);
        }

        private static void SetFloatField(object target, string fieldName, float value)
        {
            SetField(target, fieldName, value);
        }

        private static void DestroyRuntimeAsset(Object asset)
        {
            if (asset != null)
            {
                Destroy(asset);
            }
        }
    }
}
