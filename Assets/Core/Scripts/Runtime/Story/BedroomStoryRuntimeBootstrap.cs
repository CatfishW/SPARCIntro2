using System.Collections;
using System.Reflection;
using Blocks.Gameplay.Core;
using ModularStoryFlow.Runtime.Actions;
using ModularStoryFlow.Runtime.Bridges;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.Player;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-900)]
    public sealed class BedroomStoryRuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private StoryFlowPlayer player;
        [SerializeField] private BedroomStoryUiRoot uiRoot;
        [SerializeField] private BedroomStoryInteractionBridge interactionBridge;
        [SerializeField] private BedroomStorySceneTransition sceneTransition;
        [SerializeField] private StoryTimelineDirectorBridge timelineBridge;
        [SerializeField] private bool restartStoryOnBootstrap = true;
        [SerializeField] private string wakeUpAnchorPath = "_Spawns/Pfb_SpawnPad";
        [SerializeField, Min(0.25f)] private float wakeUpSpawnClearance = 0.85f;
        [SerializeField] private float wakeUpVerticalOffset = 0.08f;
        [SerializeField] private string bedObjectPath = "_Environment/Room/Bed";
        [SerializeField] private string laptopObjectPath = "_Environment/Room/MacBook";
        [SerializeField] private string savedProjectConfigPath = "Assets/StoryFlowBedroomGenerated/Config/StoryFlowProjectConfig.asset";
        [SerializeField] private string savedGraphPath = "Assets/StoryFlowBedroomGenerated/Graphs/BedroomIntroStory.asset";

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

        private void Awake()
        {
            player = player != null ? player : GetComponent<StoryFlowPlayer>();
            if (player == null)
            {
                player = gameObject.AddComponent<StoryFlowPlayer>();
            }

            uiRoot = uiRoot != null ? uiRoot : GetComponent<BedroomStoryUiRoot>();
            if (uiRoot == null)
            {
                uiRoot = gameObject.AddComponent<BedroomStoryUiRoot>();
            }

            interactionBridge = interactionBridge != null ? interactionBridge : GetComponent<BedroomStoryInteractionBridge>();
            if (interactionBridge == null)
            {
                interactionBridge = gameObject.AddComponent<BedroomStoryInteractionBridge>();
            }

            sceneTransition = sceneTransition != null ? sceneTransition : GetComponent<BedroomStorySceneTransition>();
            if (sceneTransition == null)
            {
                sceneTransition = gameObject.AddComponent<BedroomStorySceneTransition>();
            }

            if (GetComponent<BedroomStoryMorningAmbience>() == null)
            {
                gameObject.AddComponent<BedroomStoryMorningAmbience>();
            }

            timelineBridge = timelineBridge != null ? timelineBridge : Object.FindFirstObjectByType<StoryTimelineDirectorBridge>();

            BuildRuntimeProject();
            InjectRuntimeProject();
        }

        private void Start()
        {
            StartCoroutine(PositionPlayerAtWakeUpSpotRoutine());

            var graphToStart = runtimeGraph != null ? runtimeGraph : player != null ? player.InitialGraph : null;
            if (restartStoryOnBootstrap && player != null && graphToStart != null)
            {
                player.StartStory(graphToStart);
            }
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
            var introReady = CreateBooleanVariable("intro.readyToLeave", false);
            var laptopChecked = CreateBooleanVariable("intro.checkedLaptop", false);
            variables.SetVariables(new StoryVariableDefinition[] { introReady, laptopChecked });

            var states = ScriptableObject.CreateInstance<StoryStateMachineCatalog>();
            var progression = CreateStateMachine(
                "BedroomStory.Progress",
                "FreshSpawn",
                "FreshSpawn",
                "LaptopObjectiveActive",
                "LaptopResolved",
                "DoorReady",
                "TransitionCommitted");
            states.SetStateMachines(new[] { progression });

            var timelineCatalog = ScriptableObject.CreateInstance<StoryTimelineCatalog>();

            var graphRegistry = ScriptableObject.CreateInstance<StoryGraphRegistry>();
            runtimeGraph = BuildGraph(introReady, laptopChecked, progression);
            graphRegistry.SetGraphs(new[] { runtimeGraph });

            runtimeConfig = ScriptableObject.CreateInstance<StoryFlowProjectConfig>();
            SetField(runtimeConfig, "graphRegistry", graphRegistry);
            SetField(runtimeConfig, "variableCatalog", variables);
            SetField(runtimeConfig, "stateMachineCatalog", states);
            SetField(runtimeConfig, "timelineCatalog", timelineCatalog);
            SetField(runtimeConfig, "channels", runtimeChannels);
            SetField(runtimeConfig, "saveProvider", null);
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
            }

            uiRoot?.Configure(runtimeChannels);

            if (interactionBridge != null)
            {
                interactionBridge.Configure(runtimeChannels, null, null, sceneTransition);
                interactionBridge.SetSessionId(player != null ? player.SessionId : string.Empty);
            }
        }

        private bool TryLoadPersistedProject()
        {
#if UNITY_EDITOR
            var loadedConfig = AssetDatabase.LoadAssetAtPath<StoryFlowProjectConfig>(savedProjectConfigPath);
            var loadedGraph = AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(savedGraphPath);
            if (loadedConfig == null || loadedGraph == null)
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

        private StoryGraphAsset BuildGraph(
            StoryBooleanVariableDefinition introReady,
            StoryBooleanVariableDefinition laptopChecked,
            StoryStateMachineDefinition progression)
        {
            var graph = ScriptableObject.CreateInstance<StoryGraphAsset>();
            graph.name = "BedroomIntroStoryRuntime";

            var start = CreateNode<StartNodeAsset>();
            var fadeDelay = CreateDelayNode(0.6f);
            var introDialogue = CreateDialogueNode(string.Empty, "...What day is it today?", true, 1.85f);
            var secondDialogue = CreateDialogueNode(string.Empty, "Feels like I'm forgetting something.", true, 1.85f);
            var objectiveDialogue = CreateDialogueNode(string.Empty, "Maybe I should check my laptop... then head to school.", true, 2.25f);
            var objectiveAction = CreateActionNode(CreateSetStateAction(progression, "LaptopObjectiveActive"));
            var waitLaptop = CreateWaitSignalNode(CreateSignal(BedroomStorySignals.LaptopChecked));
            var laptopResolvedAction = CreateActionNode(CreateCompositeAction(
                CreateSetVariableAction(laptopChecked, true),
                CreateSetStateAction(progression, "LaptopResolved")));
            var readyDialogue = CreateDialogueNode(string.Empty, "Right... I need to go.", true, 1.45f);
            var leaveDialogue = CreateDialogueNode(string.Empty, "Better head out now.", true, 1.6f);
            var doorReadyAction = CreateActionNode(CreateCompositeAction(
                CreateSetVariableAction(introReady, true),
                CreateSetStateAction(progression, "DoorReady")));
            var waitDoor = CreateWaitSignalNode(CreateSignal(BedroomStorySignals.DoorConfirmed));
            var exitDialogue = CreateDialogueNode(string.Empty, "Alright... here goes.", true, 1.35f);
            var transitionAction = CreateActionNode(CreateCompositeAction(CreateSetStateAction(progression, "TransitionCommitted")));
            var end = CreateNode<EndNodeAsset>();

            AddNode(graph, start, true);
            AddNode(graph, fadeDelay);
            AddNode(graph, introDialogue);
            AddNode(graph, secondDialogue);
            AddNode(graph, objectiveDialogue);
            AddNode(graph, objectiveAction);
            AddNode(graph, waitLaptop);
            AddNode(graph, laptopResolvedAction);
            AddNode(graph, readyDialogue);
            AddNode(graph, leaveDialogue);
            AddNode(graph, doorReadyAction);
            AddNode(graph, waitDoor);
            AddNode(graph, exitDialogue);
            AddNode(graph, transitionAction);
            AddNode(graph, end);

            AddConnection(graph, start.NodeId, StoryNodeAsset.DefaultOutputPortId, fadeDelay.NodeId);
            AddConnection(graph, fadeDelay.NodeId, StoryNodeAsset.DefaultOutputPortId, introDialogue.NodeId);
            AddConnection(graph, introDialogue.NodeId, StoryNodeAsset.DefaultOutputPortId, secondDialogue.NodeId);
            AddConnection(graph, secondDialogue.NodeId, StoryNodeAsset.DefaultOutputPortId, objectiveDialogue.NodeId);
            AddConnection(graph, objectiveDialogue.NodeId, StoryNodeAsset.DefaultOutputPortId, objectiveAction.NodeId);
            AddConnection(graph, objectiveAction.NodeId, StoryNodeAsset.DefaultOutputPortId, waitLaptop.NodeId);
            AddConnection(graph, waitLaptop.NodeId, StoryNodeAsset.DefaultOutputPortId, laptopResolvedAction.NodeId);
            AddConnection(graph, laptopResolvedAction.NodeId, StoryNodeAsset.DefaultOutputPortId, readyDialogue.NodeId);
            AddConnection(graph, readyDialogue.NodeId, StoryNodeAsset.DefaultOutputPortId, leaveDialogue.NodeId);
            AddConnection(graph, leaveDialogue.NodeId, StoryNodeAsset.DefaultOutputPortId, doorReadyAction.NodeId);
            AddConnection(graph, doorReadyAction.NodeId, StoryNodeAsset.DefaultOutputPortId, waitDoor.NodeId);
            AddConnection(graph, waitDoor.NodeId, StoryNodeAsset.DefaultOutputPortId, exitDialogue.NodeId);
            AddConnection(graph, exitDialogue.NodeId, StoryNodeAsset.DefaultOutputPortId, transitionAction.NodeId);
            AddConnection(graph, transitionAction.NodeId, StoryNodeAsset.DefaultOutputPortId, end.NodeId);

            graph.EnsureStableIds();
            return graph;
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
            SetField(node, "actions", new System.Collections.Generic.List<StoryActionAsset> { action });
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
            var entries = new System.Collections.Generic.List<StoryStateDefinition>();
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
            SetField(action, "actions", new System.Collections.Generic.List<StoryActionAsset>(actions));
            return action;
        }

        private static void AddNode(StoryGraphAsset graph, StoryNodeAsset node, bool isEntry = false)
        {
            var nodes = GetField<System.Collections.Generic.List<StoryNodeAsset>>(graph, "nodes");
            nodes.Add(node);
            if (isEntry)
            {
                SetStringField(graph, "entryNodeId", node.NodeId);
            }
        }

        private static void AddConnection(StoryGraphAsset graph, string fromNodeId, string fromPortId, string toNodeId)
        {
            var connections = GetField<System.Collections.Generic.List<StoryConnection>>(graph, "connections");
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

        private IEnumerator PositionPlayerAtWakeUpSpotRoutine()
        {
            const float timeoutSeconds = 5f;
            var remainingSeconds = timeoutSeconds;

            while (remainingSeconds > 0f)
            {
                if (TryResolveLocalPlayerMovement(out var movement) && TryComputeWakeUpPose(out var position, out var rotation))
                {
                    movement.transform.rotation = rotation;
                    movement.SetPosition(position);
                    movement.ResetMovementForces();
                    yield break;
                }

                remainingSeconds -= 0.1f;
                yield return new WaitForSeconds(0.1f);
            }
        }

        private static bool TryResolveLocalPlayerMovement(out CoreMovement movement)
        {
            movement = null;

            var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                movement = taggedPlayer.GetComponent<CoreMovement>();
                if (movement != null)
                {
                    return true;
                }
            }

            var managers = FindObjectsByType<CorePlayerManager>(FindObjectsSortMode.None);
            for (var index = 0; index < managers.Length; index++)
            {
                if (managers[index] == null || !managers[index].IsOwner)
                {
                    continue;
                }

                movement = managers[index].CoreMovement;
                return movement != null;
            }

            return false;
        }

        private bool TryComputeWakeUpPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var wakeUpAnchor = GameObject.Find(wakeUpAnchorPath);
            if (wakeUpAnchor != null)
            {
                position = wakeUpAnchor.transform.position;
                rotation = wakeUpAnchor.transform.rotation;
                return true;
            }

            var bedObject = GameObject.Find(bedObjectPath) ?? GameObject.Find("Bed");
            if (bedObject == null || !TryGetObjectBounds(bedObject, out var bedBounds))
            {
                return false;
            }

            var laptopObject = GameObject.Find(laptopObjectPath) ?? GameObject.Find("MacBook");
            Vector3 facingDirection;
            if (laptopObject != null && TryGetObjectBounds(laptopObject, out var laptopBounds))
            {
                facingDirection = laptopBounds.center - bedBounds.center;
            }
            else
            {
                facingDirection = Vector3.right;
            }

            facingDirection = Vector3.ProjectOnPlane(facingDirection, Vector3.up);
            if (facingDirection.sqrMagnitude < 0.0001f)
            {
                facingDirection = Vector3.right;
            }

            facingDirection.Normalize();

            var horizontalExtent = Mathf.Max(bedBounds.extents.x, bedBounds.extents.z);
            position = bedBounds.center + (facingDirection * (horizontalExtent + wakeUpSpawnClearance));
            position.y = bedBounds.min.y + wakeUpVerticalOffset;
            // Face back toward the bed so the third-person camera opens into the room instead of clipping into furniture.
            rotation = Quaternion.LookRotation(-facingDirection, Vector3.up);
            return true;
        }

        private static bool TryGetObjectBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
            {
                return false;
            }

            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                bounds = collider.bounds;
                return true;
            }

            var renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                bounds = renderer.bounds;
                return true;
            }

            return false;
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
