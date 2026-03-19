using System;
using System.Collections.Generic;
using System.Linq;
using ModularStoryFlow.Runtime.Events;
using ModularStoryFlow.Runtime.Execution;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Save;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;

namespace ModularStoryFlow.Runtime.Player
{
    /// <summary>
    /// Pure C# story runtime. It has no dependency on scenes, UI, or specific gameplay systems.
    /// </summary>
    public sealed class StoryFlowRunner : IStoryExecutionContext
    {
        private const int MaxImmediateNodeSteps = 256;

        private readonly IReadOnlyList<StoryVariableDefinition> variableDefinitions;
        private readonly IReadOnlyList<StoryStateMachineDefinition> stateMachineDefinitions;
        private readonly StoryVariableStore variables;
        private readonly StoryStateStore states;
        private readonly List<string> history = new List<string>();

        private StoryGraphAsset graph;
        private StoryNodeAsset currentNode;
        private StoryWaitDescriptor pendingOperation;
        private bool isCompleted;

        public StoryFlowRunner(
            string sessionId,
            IReadOnlyList<StoryVariableDefinition> variableDefinitions,
            IReadOnlyList<StoryStateMachineDefinition> stateMachineDefinitions)
        {
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? StoryIds.NewId() : sessionId;
            this.variableDefinitions = variableDefinitions ?? Array.Empty<StoryVariableDefinition>();
            this.stateMachineDefinitions = stateMachineDefinitions ?? Array.Empty<StoryStateMachineDefinition>();
            variables = new StoryVariableStore();
            states = new StoryStateStore(HandleStateChanged);

            ResetRuntimeState();
        }

        public event Action<StoryDialogueRequest> DialogueRequested;
        public event Action<StoryChoiceRequest> ChoiceRequested;
        public event Action<StoryTimelineRequest> TimelineRequested;
        public event Action<StorySignalPayload> SignalRaised;
        public event Action<StoryStateChangedPayload> StateChanged;
        public event Action<StoryNodeNotification> NodeNotified;
        public event Action<StoryGraphNotification> GraphNotified;

        public string SessionId { get; private set; }
        public StoryGraphAsset Graph => graph;
        public StoryNodeAsset CurrentNode => currentNode;
        public IReadOnlyList<string> History => history;
        public IStoryVariableStore Variables => variables;
        public IStoryStateStore States => states;
        public StoryWaitDescriptor PendingOperation => pendingOperation;
        public bool IsCompleted => isCompleted;

        public void SetSessionId(string sessionId)
        {
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? StoryIds.NewId() : sessionId;
            pendingOperation?.RebindSession(SessionId);
        }

        public void StartGraph(StoryGraphAsset storyGraph, bool resetRuntimeState = true)
        {
            if (storyGraph == null)
            {
                NotifyFailure("StartGraph called without a graph.");
                return;
            }

            if (resetRuntimeState)
            {
                ResetRuntimeState();
            }
            else
            {
                pendingOperation = null;
                history.Clear();
                isCompleted = false;
            }

            graph = storyGraph;
            currentNode = null;
            NotifyGraph(StoryGraphNotificationKind.Started, string.Empty);

            if (graph.Nodes == null || graph.Nodes.Count == 0)
            {
                NotifyFailure($"Graph '{graph.name}' does not have an entry node.");
                return;
            }

            var entryNode = graph.GetEntryNode();
            if (entryNode == null)
            {
                NotifyFailure($"Graph '{graph.name}' does not have an entry node.");
                return;
            }

            AdvanceToNode(entryNode.NodeId);
        }

        public void Continue(string requestId = null)
        {
            if (pendingOperation == null)
            {
                return;
            }

            if (pendingOperation.WaitKind != StoryWaitKind.DialogueAdvance)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(requestId) && !string.Equals(requestId, pendingOperation.RequestId, StringComparison.Ordinal))
            {
                return;
            }

            var portId = pendingOperation.DefaultPortId;
            pendingOperation = null;
            AdvanceByPort(portId);
        }

        public void SelectChoice(StoryChoiceSelection selection)
        {
            if (selection == null || pendingOperation == null || pendingOperation.WaitKind != StoryWaitKind.ChoiceSelection)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(selection.RequestId) &&
                !string.Equals(selection.RequestId, pendingOperation.RequestId, StringComparison.Ordinal))
            {
                return;
            }

            var request = pendingOperation.ChoiceRequest;
            if (request == null)
            {
                return;
            }

            StoryChoiceOption selectedOption = null;

            if (!string.IsNullOrWhiteSpace(selection.PortId))
            {
                selectedOption = request.Options.FirstOrDefault(option => string.Equals(option.PortId, selection.PortId, StringComparison.Ordinal));
            }
            else if (selection.OptionIndex >= 0 && selection.OptionIndex < request.Options.Count)
            {
                selectedOption = request.Options[selection.OptionIndex];
            }

            if (selectedOption == null || !selectedOption.IsAvailable)
            {
                return;
            }

            pendingOperation = null;
            AdvanceByPort(selectedOption.PortId);
        }

        public void ResolveTimeline(StoryTimelineResult result)
        {
            if (result == null || pendingOperation == null || pendingOperation.WaitKind != StoryWaitKind.TimelineCompletion)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.RequestId) &&
                !string.Equals(result.RequestId, pendingOperation.RequestId, StringComparison.Ordinal))
            {
                return;
            }

            var portId = result.Completed ? pendingOperation.DefaultPortId : pendingOperation.AlternatePortId;
            pendingOperation = null;
            AdvanceByPort(string.IsNullOrWhiteSpace(portId) ? StoryNodeAsset.DefaultOutputPortId : portId);
        }

        public void ReceiveExternalSignal(StoryExternalSignal signal)
        {
            if (signal == null || pendingOperation == null || pendingOperation.WaitKind != StoryWaitKind.ExternalSignal)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(pendingOperation.SignalId) &&
                !string.Equals(signal.SignalId, pendingOperation.SignalId, StringComparison.Ordinal))
            {
                return;
            }

            var portId = pendingOperation.DefaultPortId;
            pendingOperation = null;
            AdvanceByPort(portId);
        }

        public void Tick(float deltaTime)
        {
            if (pendingOperation == null)
            {
                return;
            }

            switch (pendingOperation.WaitKind)
            {
                case StoryWaitKind.Delay:
                    pendingOperation.RemainingSeconds -= deltaTime;
                    if (pendingOperation.RemainingSeconds <= 0f)
                    {
                        var delayPort = pendingOperation.DefaultPortId;
                        pendingOperation = null;
                        AdvanceByPort(delayPort);
                    }

                    break;

                case StoryWaitKind.DialogueAdvance:
                    if (pendingOperation.DialogueRequest != null && pendingOperation.DialogueRequest.AutoAdvance)
                    {
                        pendingOperation.RemainingSeconds -= deltaTime;
                        if (pendingOperation.RemainingSeconds <= 0f)
                        {
                            Continue(pendingOperation.RequestId);
                        }
                    }

                    break;
            }
        }

        public StorySaveData CreateSnapshot()
        {
            return new StorySaveData
            {
                CreatedAtUtc = DateTime.UtcNow.ToString("O"),
                SessionId = SessionId,
                GraphId = graph != null ? graph.GraphId : string.Empty,
                GraphName = graph != null ? graph.name : string.Empty,
                CurrentNodeId = currentNode != null ? currentNode.NodeId : string.Empty,
                IsCompleted = isCompleted,
                PendingOperation = pendingOperation,
                History = new List<string>(history),
                Variables = variables.Export(),
                States = states.Export()
            };
        }

        public bool RestoreSnapshot(StorySaveData snapshot, StoryGraphAsset storyGraph)
        {
            if (snapshot == null || storyGraph == null)
            {
                NotifyFailure("RestoreSnapshot requires both a snapshot and a graph.");
                return false;
            }

            ResetRuntimeState();
            graph = storyGraph;
            isCompleted = snapshot.IsCompleted;
            currentNode = graph.GetNode(snapshot.CurrentNodeId);
            history.Clear();
            history.AddRange(snapshot.History ?? Enumerable.Empty<string>());
            variables.Import(snapshot.Variables);
            states.Import(snapshot.States);
            pendingOperation = snapshot.PendingOperation;
            pendingOperation?.RebindSession(SessionId);

            NotifyGraph(StoryGraphNotificationKind.Loaded, $"Loaded slot for graph '{graph.name}'.");

            if (pendingOperation != null)
            {
                DispatchPendingOperation(pendingOperation);
            }

            return true;
        }

        public void RaiseSignal(string signalId, string signalDisplayName, string payload = null)
        {
            SignalRaised?.Invoke(new StorySignalPayload
            {
                SessionId = SessionId,
                GraphId = graph != null ? graph.GraphId : string.Empty,
                NodeId = currentNode != null ? currentNode.NodeId : string.Empty,
                SignalId = signalId,
                SignalDisplayName = signalDisplayName,
                Payload = payload
            });
        }

        public string RequestTimeline(string cueId, string cueDisplayName, bool waitForCompletion)
        {
            var request = new StoryTimelineRequest
            {
                SessionId = SessionId,
                RequestId = StoryIds.NewId(),
                GraphId = graph != null ? graph.GraphId : string.Empty,
                NodeId = currentNode != null ? currentNode.NodeId : string.Empty,
                CueId = cueId,
                CueDisplayName = cueDisplayName,
                WaitForCompletion = waitForCompletion
            };

            TimelineRequested?.Invoke(request);
            return request.RequestId;
        }

        private void AdvanceToNode(string nodeId)
        {
            if (graph == null)
            {
                NotifyFailure("No graph is active.");
                return;
            }

            var immediateSteps = 0;
            var nextNodeId = nodeId;

            while (!string.IsNullOrWhiteSpace(nextNodeId))
            {
                immediateSteps++;
                if (immediateSteps > MaxImmediateNodeSteps)
                {
                    NotifyFailure("Execution aborted because the graph exceeded the immediate step guard.");
                    return;
                }

                currentNode = graph.GetNode(nextNodeId);
                if (currentNode == null)
                {
                    NotifyFailure($"Node '{nextNodeId}' could not be resolved.");
                    return;
                }

                history.Add(currentNode.NodeId);
                NotifyNode(StoryNodeNotificationKind.Entered, currentNode);

                var result = currentNode.Execute(this);

                NotifyNode(StoryNodeNotificationKind.Exited, currentNode);

                if (result == null)
                {
                    NotifyFailure($"Node '{currentNode.name}' returned a null execution result.");
                    return;
                }

                switch (result.Status)
                {
                    case StoryExecutionStatus.Continue:
                        if (!graph.TryResolveNextNodeId(currentNode.NodeId, result.PortId, out nextNodeId))
                        {
                            NotifyFailure($"No connection was found from node '{currentNode.name}' on port '{result.PortId}'.");
                            return;
                        }

                        break;

                    case StoryExecutionStatus.Wait:
                        pendingOperation = result.WaitDescriptor;
                        DispatchPendingOperation(pendingOperation);
                        return;

                    case StoryExecutionStatus.Stop:
                        pendingOperation = null;
                        if (result.CompleteGraph)
                        {
                            CompleteGraph();
                        }

                        return;
                }
            }

            CompleteGraph();
        }

        private void AdvanceByPort(string portId)
        {
            if (graph == null || currentNode == null)
            {
                return;
            }

            if (!graph.TryResolveNextNodeId(currentNode.NodeId, portId, out var nextNodeId))
            {
                NotifyFailure($"No connection was found from node '{currentNode.name}' on port '{portId}'.");
                return;
            }

            AdvanceToNode(nextNodeId);
        }

        private void DispatchPendingOperation(StoryWaitDescriptor waitDescriptor)
        {
            if (waitDescriptor == null)
            {
                return;
            }

            switch (waitDescriptor.WaitKind)
            {
                case StoryWaitKind.DialogueAdvance:
                    if (waitDescriptor.DialogueRequest != null)
                    {
                        DialogueRequested?.Invoke(waitDescriptor.DialogueRequest);
                    }

                    break;

                case StoryWaitKind.ChoiceSelection:
                    if (waitDescriptor.ChoiceRequest != null)
                    {
                        ChoiceRequested?.Invoke(waitDescriptor.ChoiceRequest);
                    }

                    break;

                case StoryWaitKind.TimelineCompletion:
                    if (waitDescriptor.TimelineRequest != null)
                    {
                        TimelineRequested?.Invoke(waitDescriptor.TimelineRequest);
                    }

                    break;
            }
        }

        private void HandleStateChanged(string machineId, string previousStateId, string nextStateId)
        {
            StateChanged?.Invoke(new StoryStateChangedPayload
            {
                SessionId = SessionId,
                MachineId = machineId,
                PreviousStateId = previousStateId,
                NextStateId = nextStateId
            });
        }

        private void ResetRuntimeState()
        {
            variables.Initialize(variableDefinitions);
            states.Initialize(stateMachineDefinitions);
            pendingOperation = null;
            graph = null;
            currentNode = null;
            isCompleted = false;
            history.Clear();
        }

        private void CompleteGraph()
        {
            isCompleted = true;
            pendingOperation = null;
            NotifyGraph(StoryGraphNotificationKind.Completed, string.Empty);
        }

        private void NotifyNode(StoryNodeNotificationKind kind, StoryNodeAsset node)
        {
            NodeNotified?.Invoke(new StoryNodeNotification
            {
                SessionId = SessionId,
                GraphId = graph != null ? graph.GraphId : string.Empty,
                NodeId = node != null ? node.NodeId : string.Empty,
                NodeTitle = node != null ? node.DisplayTitle : string.Empty,
                NodeType = node != null ? node.GetType().Name : string.Empty,
                Kind = kind
            });
        }

        private void NotifyGraph(StoryGraphNotificationKind kind, string message)
        {
            GraphNotified?.Invoke(new StoryGraphNotification
            {
                SessionId = SessionId,
                GraphId = graph != null ? graph.GraphId : string.Empty,
                GraphName = graph != null ? graph.name : string.Empty,
                Kind = kind,
                Message = message
            });
        }

        private void NotifyFailure(string message)
        {
            isCompleted = true;
            pendingOperation = null;
            NotifyGraph(StoryGraphNotificationKind.Failed, message);
        }
    }
}
