using System;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Save;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Player
{
    /// <summary>
    /// MonoBehaviour wrapper that integrates the pure runner with ScriptableObject event channels.
    /// </summary>
    public sealed class StoryFlowPlayer : MonoBehaviour
    {
        [SerializeField] private StoryFlowProjectConfig projectConfig;
        [SerializeField] private StoryGraphAsset initialGraph;
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private bool autoLoadSaveOnStart;
        [SerializeField] private string saveSlot = "quick";
        [SerializeField] private string sessionId = string.Empty;

        private StoryFlowRunner runner;
        private bool runnerEventsHooked;
        private bool commandChannelsHooked;

        public event Action<StoryDialogueRequest> DialogueRequested;
        public event Action<StoryChoiceRequest> ChoiceRequested;
        public event Action<StoryTimelineRequest> TimelineRequested;
        public event Action<StorySignalPayload> SignalRaised;
        public event Action<StoryStateChangedPayload> StateChanged;
        public event Action<StoryNodeNotification> NodeNotified;
        public event Action<StoryGraphNotification> GraphNotified;

        public StoryFlowProjectConfig ProjectConfig
        {
            get => projectConfig;
            set
            {
                if (projectConfig == value)
                {
                    return;
                }

                UnhookCommandChannels();
                projectConfig = value;
                RebuildRunner();
                if (isActiveAndEnabled)
                {
                    HookCommandChannels();
                }
            }
        }

        public StoryGraphAsset InitialGraph
        {
            get => initialGraph;
            set => initialGraph = value;
        }

        public bool PlayOnStart
        {
            get => playOnStart;
            set => playOnStart = value;
        }

        public string SaveSlot
        {
            get => saveSlot;
            set => saveSlot = value;
        }

        public string SessionId => runner != null ? runner.SessionId : sessionId;
        public StoryFlowRunner Runner => runner;

        private void Awake()
        {
            EnsureRunner();
        }

        private void Start()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (autoLoadSaveOnStart && !string.IsNullOrWhiteSpace(saveSlot) && Load(saveSlot))
            {
                return;
            }

            if (playOnStart && initialGraph != null)
            {
                StartStory(initialGraph);
            }
        }

        private void OnEnable()
        {
            EnsureRunner();
            HookCommandChannels();
        }

        private void OnDisable()
        {
            UnhookCommandChannels();
        }

        private void OnDestroy()
        {
            if (runner != null && runnerEventsHooked)
            {
                runner.DialogueRequested -= ForwardDialogueRequested;
                runner.ChoiceRequested -= ForwardChoiceRequested;
                runner.TimelineRequested -= ForwardTimelineRequested;
                runner.SignalRaised -= ForwardSignalRaised;
                runner.StateChanged -= ForwardStateChanged;
                runner.NodeNotified -= ForwardNodeNotification;
                runner.GraphNotified -= ForwardGraphNotification;
                runnerEventsHooked = false;
            }
        }

        private void Update()
        {
            runner?.Tick(Time.deltaTime);
        }

        public void StartStory(StoryGraphAsset graph, bool resetRuntimeState = true)
        {
            EnsureRunner();
            runner.StartGraph(graph, resetRuntimeState);
        }

        public void RestartStory()
        {
            if (initialGraph != null)
            {
                StartStory(initialGraph);
            }
        }

        public void ContinueStory(string requestId = null)
        {
            runner?.Continue(requestId);
        }

        public void SelectChoice(StoryChoiceSelection selection)
        {
            runner?.SelectChoice(selection);
        }

        public void ResolveTimeline(StoryTimelineResult result)
        {
            runner?.ResolveTimeline(result);
        }

        public void EmitExternalSignal(StoryExternalSignal signal)
        {
            runner?.ReceiveExternalSignal(signal);
        }

        public bool Save(string slot = null)
        {
            EnsureRunner();

            var provider = projectConfig != null ? projectConfig.SaveProvider : null;
            if (provider == null)
            {
                return false;
            }

            var resolvedSlot = string.IsNullOrWhiteSpace(slot) ? saveSlot : slot;
            provider.Save(resolvedSlot, runner.CreateSnapshot());

            var notification = new StoryGraphNotification
            {
                SessionId = SessionId,
                GraphId = runner.Graph != null ? runner.Graph.GraphId : string.Empty,
                GraphName = runner.Graph != null ? runner.Graph.name : string.Empty,
                Kind = StoryGraphNotificationKind.Saved,
                Message = provider.GetDebugPath(resolvedSlot)
            };

            GraphNotified?.Invoke(notification);
            projectConfig?.Channels?.GraphNotifications?.Raise(notification);
            return true;
        }

        public bool Load(string slot = null)
        {
            EnsureRunner();

            var provider = projectConfig != null ? projectConfig.SaveProvider : null;
            if (provider == null)
            {
                return false;
            }

            var resolvedSlot = string.IsNullOrWhiteSpace(slot) ? saveSlot : slot;
            if (!provider.TryLoad(resolvedSlot, out var saveData) || saveData == null)
            {
                return false;
            }

            var targetGraph = ResolveGraph(saveData.GraphId);
            if (targetGraph == null)
            {
                return false;
            }

            return runner.RestoreSnapshot(saveData, targetGraph);
        }

        public void DeleteSave(string slot = null)
        {
            var provider = projectConfig != null ? projectConfig.SaveProvider : null;
            if (provider == null)
            {
                return;
            }

            provider.Delete(string.IsNullOrWhiteSpace(slot) ? saveSlot : slot);
        }

        private StoryGraphAsset ResolveGraph(string graphId)
        {
            if (!string.IsNullOrWhiteSpace(graphId) && projectConfig != null && projectConfig.GraphRegistry != null)
            {
                var graph = projectConfig.GraphRegistry.Resolve(graphId);
                if (graph != null)
                {
                    return graph;
                }
            }

            if (initialGraph != null && (string.IsNullOrWhiteSpace(graphId) || initialGraph.GraphId == graphId))
            {
                return initialGraph;
            }

            return null;
        }

        private void EnsureRunner()
        {
            if (runner != null)
            {
                return;
            }

            var variables = projectConfig != null && projectConfig.VariableCatalog != null
                ? projectConfig.VariableCatalog.Variables
                : Array.Empty<StoryVariableDefinition>();

            var states = projectConfig != null && projectConfig.StateMachineCatalog != null
                ? projectConfig.StateMachineCatalog.StateMachines
                : Array.Empty<StoryStateMachineDefinition>();

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = StoryIds.NewId();
            }

            runner = new StoryFlowRunner(sessionId, variables, states);
            HookRunnerEvents();
        }

        private void RebuildRunner()
        {
            if (runner != null && runnerEventsHooked)
            {
                runner.DialogueRequested -= ForwardDialogueRequested;
                runner.ChoiceRequested -= ForwardChoiceRequested;
                runner.TimelineRequested -= ForwardTimelineRequested;
                runner.SignalRaised -= ForwardSignalRaised;
                runner.StateChanged -= ForwardStateChanged;
                runner.NodeNotified -= ForwardNodeNotification;
                runner.GraphNotified -= ForwardGraphNotification;
                runnerEventsHooked = false;
            }

            runner = null;
            EnsureRunner();
        }

        private void HookRunnerEvents()
        {
            if (runner == null || runnerEventsHooked)
            {
                return;
            }

            runner.DialogueRequested += ForwardDialogueRequested;
            runner.ChoiceRequested += ForwardChoiceRequested;
            runner.TimelineRequested += ForwardTimelineRequested;
            runner.SignalRaised += ForwardSignalRaised;
            runner.StateChanged += ForwardStateChanged;
            runner.NodeNotified += ForwardNodeNotification;
            runner.GraphNotified += ForwardGraphNotification;
            runnerEventsHooked = true;
        }

        private void HookCommandChannels()
        {
            if (commandChannelsHooked)
            {
                return;
            }

            var channels = projectConfig != null ? projectConfig.Channels : null;
            if (channels == null)
            {
                return;
            }

            channels.AdvanceCommands?.Register(HandleAdvanceCommand);
            channels.ChoiceSelections?.Register(HandleChoiceSelection);
            channels.TimelineResults?.Register(HandleTimelineResult);
            channels.ExternalSignals?.Register(HandleExternalSignal);

            commandChannelsHooked = true;
        }

        private void UnhookCommandChannels()
        {
            if (!commandChannelsHooked)
            {
                return;
            }

            var channels = projectConfig != null ? projectConfig.Channels : null;
            if (channels != null)
            {
                channels.AdvanceCommands?.Unregister(HandleAdvanceCommand);
                channels.ChoiceSelections?.Unregister(HandleChoiceSelection);
                channels.TimelineResults?.Unregister(HandleTimelineResult);
                channels.ExternalSignals?.Unregister(HandleExternalSignal);
            }

            commandChannelsHooked = false;
        }

        private void HandleAdvanceCommand(StoryAdvanceCommand command)
        {
            if (command == null || !IsSessionMatch(command.SessionId))
            {
                return;
            }

            ContinueStory(command.RequestId);
        }

        private void HandleChoiceSelection(StoryChoiceSelection selection)
        {
            if (selection == null || !IsSessionMatch(selection.SessionId))
            {
                return;
            }

            SelectChoice(selection);
        }

        private void HandleTimelineResult(StoryTimelineResult result)
        {
            if (result == null || !IsSessionMatch(result.SessionId))
            {
                return;
            }

            ResolveTimeline(result);
        }

        private void HandleExternalSignal(StoryExternalSignal signal)
        {
            if (signal == null || !IsSessionMatch(signal.SessionId))
            {
                return;
            }

            EmitExternalSignal(signal);
        }

        private bool IsSessionMatch(string incomingSessionId)
        {
            return string.IsNullOrWhiteSpace(incomingSessionId) || string.Equals(incomingSessionId, SessionId, StringComparison.Ordinal);
        }

        private void ForwardDialogueRequested(StoryDialogueRequest request)
        {
            DialogueRequested?.Invoke(request);
            projectConfig?.Channels?.DialogueRequests?.Raise(request);
        }

        private void ForwardChoiceRequested(StoryChoiceRequest request)
        {
            ChoiceRequested?.Invoke(request);
            projectConfig?.Channels?.ChoiceRequests?.Raise(request);
        }

        private void ForwardTimelineRequested(StoryTimelineRequest request)
        {
            TimelineRequested?.Invoke(request);
            projectConfig?.Channels?.TimelineRequests?.Raise(request);
        }

        private void ForwardSignalRaised(StorySignalPayload payload)
        {
            SignalRaised?.Invoke(payload);
            projectConfig?.Channels?.RaisedSignals?.Raise(payload);
        }

        private void ForwardStateChanged(StoryStateChangedPayload payload)
        {
            StateChanged?.Invoke(payload);
            projectConfig?.Channels?.StateChanged?.Raise(payload);
        }

        private void ForwardNodeNotification(StoryNodeNotification payload)
        {
            NodeNotified?.Invoke(payload);
            projectConfig?.Channels?.NodeNotifications?.Raise(payload);
        }

        private void ForwardGraphNotification(StoryGraphNotification payload)
        {
            GraphNotified?.Invoke(payload);
            projectConfig?.Channels?.GraphNotifications?.Raise(payload);
        }
    }
}
