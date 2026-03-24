using System.Collections;
using System.Reflection;
using System.Collections.Generic;
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
using UnityEngine.SceneManagement;
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
        [SerializeField] private bool enableWebGlLitFallback = true;

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

#if UNITY_WEBGL && !UNITY_EDITOR
            if (enableWebGlLitFallback)
            {
                StartCoroutine(ApplyWebGlMaterialFallbackRoutine());
            }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
            StartCoroutine(LogWebBootstrapDiagnosticsRoutine());
#endif
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
            yield return StorySceneLocalPlayerSpawner.EnsureLocalPlayerAtPoseRoutine(
                gameObject.scene,
                TryComputeWakeUpPose,
                timeoutSeconds: 12f);

            // In WebGL/local-offline mode there may be no spawned Netcode player object.
            // Force a sane gameplay camera pose so players never start in a skybox-only view.
            if (!StorySceneLocalPlayerSpawner.TryResolveSceneLocalPlayerMovement(gameObject.scene, out _))
            {
                if (TryComputeBedroomCameraPose(out var cameraPosition, out var cameraRotation))
                {
                    ApplyFallbackCameraPose(cameraPosition, cameraRotation);
                }
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private IEnumerator ApplyWebGlMaterialFallbackRoutine()
        {
            ApplyWebGlMaterialFallback("t+0");
            yield return null;
            yield return new WaitForSeconds(1.5f);
            ApplyWebGlMaterialFallback("t+1.5");
        }

        private void ApplyWebGlMaterialFallback(string stamp)
        {
            var fallbackShader = FindSupportedWebGlFallbackShader();
            if (fallbackShader == null)
            {
                Debug.LogWarning("[BedroomWebDiag] No supported fallback shader found. Cannot apply lit fallback.");
                return;
            }

            var replacements = 0;
            replacements += ApplyFallbackToRenderers(FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None), fallbackShader);
            replacements += ApplyFallbackToRenderers(FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None), fallbackShader);
            Debug.Log($"[BedroomWebDiag {stamp}] Applied WebGL lit fallback materials: {replacements} using '{fallbackShader.name}'");
        }

        private int ApplyFallbackToRenderers(Renderer[] renderers, Shader fallbackShader)
        {
            var replacements = 0;
            if (renderers == null || fallbackShader == null)
            {
                return replacements;
            }

            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (renderer == null || renderer.gameObject.scene != gameObject.scene)
                {
                    continue;
                }

                var sharedMaterials = renderer.sharedMaterials;
                if (sharedMaterials == null || sharedMaterials.Length == 0)
                {
                    continue;
                }

                List<Material> rewrittenMaterials = null;
                for (var materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                {
                    var material = sharedMaterials[materialIndex];
                    if (!ShouldReplaceWebGlMaterial(material))
                    {
                        continue;
                    }

                    rewrittenMaterials ??= new List<Material>(sharedMaterials);
                    var replacement = new Material(fallbackShader)
                    {
                        name = $"{material.name}_WebGlFallback"
                    };

                    if (material.HasProperty("_BaseMap"))
                    {
                        var baseMap = material.GetTexture("_BaseMap");
                        if (replacement.HasProperty("_BaseMap"))
                        {
                            replacement.SetTexture("_BaseMap", baseMap);
                        }

                        if (replacement.HasProperty("_MainTex"))
                        {
                            replacement.SetTexture("_MainTex", baseMap);
                        }
                    }
                    else if (material.HasProperty("_MainTex"))
                    {
                        var mainTexture = material.GetTexture("_MainTex");
                        if (replacement.HasProperty("_MainTex"))
                        {
                            replacement.SetTexture("_MainTex", mainTexture);
                        }

                        if (replacement.HasProperty("_BaseMap"))
                        {
                            replacement.SetTexture("_BaseMap", mainTexture);
                        }
                    }

                    if (material.HasProperty("_BaseColor"))
                    {
                        var baseColor = material.GetColor("_BaseColor");
                        if (replacement.HasProperty("_BaseColor"))
                        {
                            replacement.SetColor("_BaseColor", baseColor);
                        }

                        if (replacement.HasProperty("_Color"))
                        {
                            replacement.SetColor("_Color", baseColor);
                        }
                    }
                    else if (material.HasProperty("_Color"))
                    {
                        var color = material.GetColor("_Color");
                        if (replacement.HasProperty("_Color"))
                        {
                            replacement.SetColor("_Color", color);
                        }

                        if (replacement.HasProperty("_BaseColor"))
                        {
                            replacement.SetColor("_BaseColor", color);
                        }
                    }

                    rewrittenMaterials[materialIndex] = replacement;
                    replacements++;
                }

                if (rewrittenMaterials != null)
                {
                    renderer.sharedMaterials = rewrittenMaterials.ToArray();
                }
            }

            return replacements;
        }

        private static bool ShouldReplaceWebGlMaterial(Material material)
        {
            if (material == null)
            {
                return false;
            }

            var shader = material.shader;
            if (shader == null)
            {
                return true;
            }

            if (!shader.isSupported)
            {
                return true;
            }

            var shaderName = shader.name ?? string.Empty;
            return shaderName.StartsWith("Universal Render Pipeline/", System.StringComparison.OrdinalIgnoreCase)
                   && shaderName.IndexOf("Unlit", System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static Shader FindSupportedWebGlFallbackShader()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("Unlit/Texture");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("Unlit/Color");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("Sprites/Default");
            return shader != null && shader.isSupported ? shader : null;
        }

        private IEnumerator LogWebBootstrapDiagnosticsRoutine()
        {
            yield return new WaitForSeconds(3f);
            LogWebBootstrapDiagnostics("t+3");
            yield return new WaitForSeconds(12f);
            LogWebBootstrapDiagnostics("t+15");
        }

        private void LogWebBootstrapDiagnostics(string stamp)
        {
            var scene = gameObject.scene;
            var cameraSummary = string.Empty;
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < cameras.Length; index++)
            {
                var cam = cameras[index];
                if (cam == null)
                {
                    continue;
                }

                cameraSummary += $"{cam.name}@{cam.gameObject.scene.name} pos={cam.transform.position} rot={cam.transform.eulerAngles} depth={cam.depth} enabled={cam.enabled} mask={cam.cullingMask}; ";
            }

            var rendererCount = 0;
            var activeRendererCount = 0;
            var meshRenderers = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < meshRenderers.Length; index++)
            {
                var meshRenderer = meshRenderers[index];
                if (meshRenderer == null || meshRenderer.gameObject.scene != scene)
                {
                    continue;
                }

                rendererCount++;
                if (meshRenderer.enabled && meshRenderer.gameObject.activeInHierarchy)
                {
                    activeRendererCount++;
                }
            }

            var skinnedCount = 0;
            var skinnedRenderers = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < skinnedRenderers.Length; index++)
            {
                var skinned = skinnedRenderers[index];
                if (skinned == null || skinned.gameObject.scene != scene)
                {
                    continue;
                }

                skinnedCount++;
            }

            var bed = FindTransformByPath(bedObjectPath) ?? GameObject.Find("Bed")?.transform;
            var laptop = FindTransformByPath(laptopObjectPath) ?? GameObject.Find("MacBook")?.transform;
            var hasPlayer = StorySceneLocalPlayerSpawner.TryResolveSceneLocalPlayerMovement(scene, out var movement);

            Debug.Log($"[BedroomWebDiag {stamp}] scene={scene.name} hasPlayer={hasPlayer} player={(movement != null ? movement.transform.position.ToString() : "null")} bed={(bed != null ? bed.position.ToString() : "null")} laptop={(laptop != null ? laptop.position.ToString() : "null")} meshRenderers={rendererCount} activeMeshRenderers={activeRendererCount} skinnedRenderers={skinnedCount} cameras={cameraSummary}");
        }
#endif

        private bool TryComputeWakeUpPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var bedObject = FindTransformByPath(bedObjectPath)?.gameObject ?? GameObject.Find(bedObjectPath) ?? GameObject.Find("Bed");
            var wakeUpAnchor = FindTransformByPath(wakeUpAnchorPath);
            if (wakeUpAnchor != null)
            {
                var anchorIsPlausible = true;
                if (bedObject != null && TryGetObjectBounds(bedObject, out var bedAnchorBounds))
                {
                    // Some authored spawn pads are intentionally disabled, but others are far outside the playable room.
                    // Validate distance so WebGL never boots into an empty-horizon camera.
                    anchorIsPlausible = Vector3.Distance(wakeUpAnchor.position, bedAnchorBounds.center) <= 6.5f;
                }

                if (anchorIsPlausible)
                {
                    position = wakeUpAnchor.position;
                    rotation = wakeUpAnchor.rotation;
                    return true;
                }
            }

            if (bedObject == null || !TryGetObjectBounds(bedObject, out var bedBounds))
            {
                var fallbackCamera = Camera.main;
                if (fallbackCamera == null)
                {
                    var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    if (cameras != null && cameras.Length > 0)
                    {
                        fallbackCamera = cameras[0];
                    }
                }

                if (fallbackCamera != null)
                {
                    position = fallbackCamera.transform.position;
                    rotation = fallbackCamera.transform.rotation;
                    return true;
                }

                position = new Vector3(0f, wakeUpVerticalOffset, 0f);
                rotation = Quaternion.identity;
                return true;
            }

            var laptopObject = FindTransformByPath(laptopObjectPath)?.gameObject ?? GameObject.Find(laptopObjectPath) ?? GameObject.Find("MacBook");
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

            var horizontalExtent = Mathf.Clamp(Mathf.Max(bedBounds.extents.x, bedBounds.extents.z), 0.35f, 1.55f);
            position = bedBounds.center + (facingDirection * (horizontalExtent + wakeUpSpawnClearance));
            position.y = bedBounds.min.y + wakeUpVerticalOffset;
            // Face back toward the bed so the third-person camera opens into the room instead of clipping into furniture.
            rotation = Quaternion.LookRotation(-facingDirection, Vector3.up);
            return true;
        }

        private bool TryComputeBedroomCameraPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var laptop = FindTransformByPath(laptopObjectPath) ?? GameObject.Find(laptopObjectPath)?.transform ?? GameObject.Find("MacBook")?.transform;
            if (laptop != null)
            {
                position = laptop.position + new Vector3(-1.35f, 1.45f, -1.15f);
                var lookDirection = laptop.position - position;
                if (lookDirection.sqrMagnitude > 0.0001f)
                {
                    rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                    return true;
                }
            }

            if (TryComputeWakeUpPose(out var wakePosition, out var wakeRotation))
            {
                position = wakePosition + new Vector3(0f, 1.45f, 0f);
                rotation = wakeRotation;
                return true;
            }

            return false;
        }

        private void ApplyFallbackCameraPose(Vector3 position, Quaternion rotation)
        {
            Camera fallbackCamera = null;

            if (Camera.main != null && Camera.main.gameObject.scene == gameObject.scene)
            {
                fallbackCamera = Camera.main;
            }

            if (fallbackCamera == null)
            {
                var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (var index = 0; index < cameras.Length; index++)
                {
                    var candidate = cameras[index];
                    if (candidate != null && candidate.gameObject.scene == gameObject.scene)
                    {
                        fallbackCamera = candidate;
                        break;
                    }
                }
            }

            if (fallbackCamera == null)
            {
                return;
            }

            fallbackCamera.transform.SetPositionAndRotation(position, rotation);
            fallbackCamera.cullingMask = ~0;
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
                    if (!NameMatches(root.name, segments[0]))
                    {
                        continue;
                    }

                    var current = root.transform;
                    for (var segmentIndex = 1; segmentIndex < segments.Length && current != null; segmentIndex++)
                    {
                        current = FindChildBySegment(current, segments[segmentIndex]);
                    }

                    if (current != null)
                    {
                        return current;
                    }
                }
            }

            return null;
        }

        private static Transform FindChildBySegment(Transform parent, string segment)
        {
            if (parent == null || string.IsNullOrWhiteSpace(segment))
            {
                return null;
            }

            var direct = parent.Find(segment);
            if (direct != null)
            {
                return direct;
            }

            for (var index = 0; index < parent.childCount; index++)
            {
                var child = parent.GetChild(index);
                if (NameMatches(child.name, segment))
                {
                    return child;
                }
            }

            return null;
        }

        private static bool NameMatches(string actualName, string expectedSegment)
        {
            if (string.IsNullOrWhiteSpace(actualName) || string.IsNullOrWhiteSpace(expectedSegment))
            {
                return false;
            }

            if (string.Equals(actualName, expectedSegment, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return actualName.StartsWith(expectedSegment + " ", System.StringComparison.OrdinalIgnoreCase)
                || actualName.StartsWith(expectedSegment + "(", System.StringComparison.OrdinalIgnoreCase)
                || actualName.StartsWith(expectedSegment + "_", System.StringComparison.OrdinalIgnoreCase)
                || actualName.StartsWith(expectedSegment, System.StringComparison.OrdinalIgnoreCase);
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
