using System.Collections;
using System.Reflection;
using Blocks.Gameplay.Core;
using ModularStoryFlow.Runtime.Actions;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.Player;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-900)]
    public sealed class ClassroomStoryRuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private StoryFlowPlayer player;
        [SerializeField] private ClassroomStoryUiRoot uiRoot;
        [SerializeField] private ClassroomStoryInteractionBridge interactionBridge;
        [SerializeField] private ClassroomStoryConversationDirector conversationDirector;
        [SerializeField] private ClassroomStorySceneTransition sceneTransition;
        [SerializeField] private ClassroomLlmService llmService;
        [SerializeField] private ClassroomNpcRuntimeVoiceoverService runtimeVoiceoverService;
        [SerializeField] private ClassroomNpcActionExecutor npcActionExecutor;
        [SerializeField] private ClassroomNpcChatBubblePresenter chatBubblePresenter;
        [SerializeField] private ClassroomNpcAmbientChatterLoop ambientChatterLoop;
        [SerializeField] private ClassroomNpcFreeChatUi freeChatUi;
        [SerializeField] private ClassroomBodyKnowledgeBookUi bookUi;
        [SerializeField] private ClassroomBodyKnowledgeQuizUi quizUi;
        [SerializeField] private bool restartStoryOnBootstrap = true;
        [SerializeField] private string spawnAnchorPath = "_Spawns/Pfb_SpawnPad";
        [SerializeField, Min(0f)] private float spawnVerticalOffset = 0.08f;
        [SerializeField] private string spawnLookTargetPath = "Classroom/teacherDesk.001";

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

        private void Awake()
        {
            player = player != null ? player : GetComponent<StoryFlowPlayer>();
            if (player == null)
            {
                player = gameObject.AddComponent<StoryFlowPlayer>();
            }

            uiRoot = uiRoot != null ? uiRoot : GetComponent<ClassroomStoryUiRoot>();
            if (uiRoot == null)
            {
                uiRoot = gameObject.AddComponent<ClassroomStoryUiRoot>();
            }

            interactionBridge = interactionBridge != null ? interactionBridge : GetComponent<ClassroomStoryInteractionBridge>();
            if (interactionBridge == null)
            {
                interactionBridge = gameObject.AddComponent<ClassroomStoryInteractionBridge>();
            }

            conversationDirector = conversationDirector != null ? conversationDirector : GetComponent<ClassroomStoryConversationDirector>();
            if (conversationDirector == null)
            {
                conversationDirector = gameObject.AddComponent<ClassroomStoryConversationDirector>();
            }

            sceneTransition = sceneTransition != null ? sceneTransition : GetComponent<ClassroomStorySceneTransition>();
            if (sceneTransition == null)
            {
                sceneTransition = gameObject.AddComponent<ClassroomStorySceneTransition>();
            }

            EnsureStoryServices();

            BuildRuntimeProject();
            InjectRuntimeProject();
        }

        private void Start()
        {
            StartCoroutine(PositionPlayerAtSpawnRoutine());

            var graphToStart = runtimeGraph != null ? runtimeGraph : player != null ? player.InitialGraph : null;
            if (restartStoryOnBootstrap && player != null && graphToStart != null)
            {
                player.StartStory(graphToStart);
            }
        }

        private void OnDestroy()
        {
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
            var labClearanceEarned = CreateBooleanVariable("classroom.labClearanceEarned", false);
            variables.SetVariables(new StoryVariableDefinition[] { labClearanceEarned });

            var states = ScriptableObject.CreateInstance<StoryStateMachineCatalog>();
            var progression = CreateStateMachine(
                "ClassroomStory.Progress",
                "FreshSpawn",
                "FreshSpawn",
                "BriefingActive",
                "ExplorationActive",
                "DoorReady",
                "TransitionCommitted");
            states.SetStateMachines(new[] { progression });

            var timelineCatalog = ScriptableObject.CreateInstance<StoryTimelineCatalog>();

            var graphRegistry = ScriptableObject.CreateInstance<StoryGraphRegistry>();
            runtimeGraph = BuildGraph(labClearanceEarned, progression);
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
            conversationDirector?.Configure(runtimeChannels);

            if (interactionBridge != null)
            {
                if (sceneTransition != null)
                {
                    sceneTransition.Configure("Assets/Core/TestScenes/LabMiniEntryScene.unity", "LabMiniEntryScene");
                }

                interactionBridge.Configure(runtimeChannels, null, null, sceneTransition);
                interactionBridge.SetSessionId(player != null ? player.SessionId : string.Empty);
            }
        }

        private void EnsureStoryServices()
        {
            llmService = llmService != null ? llmService : FindFirstObjectByType<ClassroomLlmService>(FindObjectsInactive.Include);
            if (llmService == null)
            {
                llmService = gameObject.AddComponent<ClassroomLlmService>();
            }

            runtimeVoiceoverService = runtimeVoiceoverService != null
                ? runtimeVoiceoverService
                : FindFirstObjectByType<ClassroomNpcRuntimeVoiceoverService>(FindObjectsInactive.Include);
            if (runtimeVoiceoverService == null)
            {
                runtimeVoiceoverService = gameObject.AddComponent<ClassroomNpcRuntimeVoiceoverService>();
            }

            npcActionExecutor = npcActionExecutor != null
                ? npcActionExecutor
                : FindFirstObjectByType<ClassroomNpcActionExecutor>(FindObjectsInactive.Include);
            if (npcActionExecutor == null)
            {
                npcActionExecutor = gameObject.AddComponent<ClassroomNpcActionExecutor>();
            }

            chatBubblePresenter = chatBubblePresenter != null
                ? chatBubblePresenter
                : FindFirstObjectByType<ClassroomNpcChatBubblePresenter>(FindObjectsInactive.Include);
            if (chatBubblePresenter == null)
            {
                chatBubblePresenter = EnsureComponentObject<ClassroomNpcChatBubblePresenter>("ClassroomNpcChatBubblePresenter");
            }

            ambientChatterLoop = ambientChatterLoop != null
                ? ambientChatterLoop
                : FindFirstObjectByType<ClassroomNpcAmbientChatterLoop>(FindObjectsInactive.Include);
            if (ambientChatterLoop == null)
            {
                ambientChatterLoop = gameObject.AddComponent<ClassroomNpcAmbientChatterLoop>();
            }

            freeChatUi = freeChatUi != null
                ? freeChatUi
                : FindFirstObjectByType<ClassroomNpcFreeChatUi>(FindObjectsInactive.Include);
            if (freeChatUi == null)
            {
                freeChatUi = EnsureUiOverlayObject<ClassroomNpcFreeChatUi>("ClassroomNpcFreeChatUiRoot");
            }

            bookUi = bookUi != null
                ? bookUi
                : FindFirstObjectByType<ClassroomBodyKnowledgeBookUi>(FindObjectsInactive.Include);
            if (bookUi == null)
            {
                bookUi = EnsureUiOverlayObject<ClassroomBodyKnowledgeBookUi>("ClassroomBookUiRoot");
            }

            quizUi = quizUi != null
                ? quizUi
                : FindFirstObjectByType<ClassroomBodyKnowledgeQuizUi>(FindObjectsInactive.Include);
            if (quizUi == null)
            {
                quizUi = EnsureUiOverlayObject<ClassroomBodyKnowledgeQuizUi>("ClassroomQuizUiRoot");
            }
        }

        private static T EnsureUiOverlayObject<T>(string objectName)
            where T : Component
        {
            var existing = GameObject.Find(objectName);
            if (existing == null)
            {
                existing = new GameObject(objectName);
            }

            var document = existing.GetComponent<UnityEngine.UIElements.UIDocument>();
            if (document == null)
            {
                document = existing.AddComponent<UnityEngine.UIElements.UIDocument>();
            }

            var component = existing.GetComponent<T>();
            if (component == null)
            {
                component = existing.AddComponent<T>();
            }

            return component;
        }

        private static T EnsureComponentObject<T>(string objectName)
            where T : Component
        {
            var existing = GameObject.Find(objectName);
            if (existing == null)
            {
                existing = new GameObject(objectName);
            }

            var component = existing.GetComponent<T>();
            if (component == null)
            {
                component = existing.AddComponent<T>();
            }

            return component;
        }

        private StoryGraphAsset BuildGraph(
            StoryBooleanVariableDefinition labClearanceEarned,
            StoryStateMachineDefinition progression)
        {
            var graph = ScriptableObject.CreateInstance<StoryGraphAsset>();
            graph.name = "ClassroomIntroStoryRuntime";

            var start = CreateNode<StartNodeAsset>();
            var settleDelay = CreateDelayNode(0.4f);
            var introDialogue = CreateDialogueNode(string.Empty, "Classroom briefing starts now. Talk to Dr. Mira, check the room evidence, and earn lab clearance.", true, 3.5f);
            var objectiveAction = CreateActionNode(CreateSetStateAction(progression, "BriefingActive"));
            var waitTeacherTalk = CreateWaitSignalNode(CreateSignal(ClassroomStorySignals.TeacherTalked));
            var explorationAction = CreateActionNode(CreateSetStateAction(progression, "ExplorationActive"));
            var readyDialogue = CreateDialogueNode(string.Empty, "Collect enough science evidence from classmates and classroom props, then confirm your volunteer decision.", true, 3.9f);
            var waitClearance = CreateWaitSignalNode(CreateSignal(ClassroomStorySignals.LabClearanceEarned));
            var doorReadyAction = CreateActionNode(CreateCompositeAction(
                CreateSetVariableAction(labClearanceEarned, true),
                CreateSetStateAction(progression, "DoorReady")));
            var waitDoor = CreateWaitSignalNode(CreateSignal(ClassroomStorySignals.DoorConfirmed));
            var exitDialogue = CreateDialogueNode(string.Empty, "Lab door. Next stop: shrink, rocket, mouth entry, and the inside of a human body.", true, 3.2f);
            var transitionAction = CreateActionNode(CreateSetStateAction(progression, "TransitionCommitted"));
            var end = CreateNode<EndNodeAsset>();

            AddNode(graph, start, true);
            AddNode(graph, settleDelay);
            AddNode(graph, introDialogue);
            AddNode(graph, objectiveAction);
            AddNode(graph, waitTeacherTalk);
            AddNode(graph, explorationAction);
            AddNode(graph, readyDialogue);
            AddNode(graph, waitClearance);
            AddNode(graph, doorReadyAction);
            AddNode(graph, waitDoor);
            AddNode(graph, exitDialogue);
            AddNode(graph, transitionAction);
            AddNode(graph, end);

            AddConnection(graph, start.NodeId, StoryNodeAsset.DefaultOutputPortId, settleDelay.NodeId);
            AddConnection(graph, settleDelay.NodeId, StoryNodeAsset.DefaultOutputPortId, introDialogue.NodeId);
            AddConnection(graph, introDialogue.NodeId, StoryNodeAsset.DefaultOutputPortId, objectiveAction.NodeId);
            AddConnection(graph, objectiveAction.NodeId, StoryNodeAsset.DefaultOutputPortId, waitTeacherTalk.NodeId);
            AddConnection(graph, waitTeacherTalk.NodeId, StoryNodeAsset.DefaultOutputPortId, explorationAction.NodeId);
            AddConnection(graph, explorationAction.NodeId, StoryNodeAsset.DefaultOutputPortId, readyDialogue.NodeId);
            AddConnection(graph, readyDialogue.NodeId, StoryNodeAsset.DefaultOutputPortId, waitClearance.NodeId);
            AddConnection(graph, waitClearance.NodeId, StoryNodeAsset.DefaultOutputPortId, doorReadyAction.NodeId);
            AddConnection(graph, doorReadyAction.NodeId, StoryNodeAsset.DefaultOutputPortId, waitDoor.NodeId);
            AddConnection(graph, waitDoor.NodeId, StoryNodeAsset.DefaultOutputPortId, exitDialogue.NodeId);
            AddConnection(graph, exitDialogue.NodeId, StoryNodeAsset.DefaultOutputPortId, transitionAction.NodeId);
            AddConnection(graph, transitionAction.NodeId, StoryNodeAsset.DefaultOutputPortId, end.NodeId);

            graph.EnsureStableIds();
            return graph;
        }

        private IEnumerator PositionPlayerAtSpawnRoutine()
        {
            const float timeoutSeconds = 5f;
            var remainingSeconds = timeoutSeconds;

            while (remainingSeconds > 0f)
            {
                if (TryResolveLocalPlayerMovement(out var movement) && TryComputeSpawnPose(out var position, out var rotation))
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

        private bool TryComputeSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var spawnAnchor = FindTransformByPath(spawnAnchorPath);
            if (spawnAnchor != null)
            {
                position = spawnAnchor.position + new Vector3(0f, spawnVerticalOffset, 0f);
                rotation = spawnAnchor.rotation;
                return true;
            }

            var fallback = FindTransformByPath(spawnLookTargetPath) ?? FindTransformByPath("Classroom/teacherDesk.001");
            if (fallback == null)
            {
                return false;
            }

            var fallbackPosition = fallback.position;
            position = fallbackPosition + new Vector3(0f, spawnVerticalOffset, -1.75f);
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

        private static void DestroyRuntimeAsset(Object asset)
        {
            if (asset == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(asset);
            }
            else
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
