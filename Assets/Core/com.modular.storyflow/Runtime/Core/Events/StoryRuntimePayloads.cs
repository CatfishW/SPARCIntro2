using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Events
{
    public enum StoryExecutionStatus
    {
        Continue = 0,
        Wait = 1,
        Stop = 2
    }

    public enum StoryWaitKind
    {
        None = 0,
        DialogueAdvance = 1,
        ChoiceSelection = 2,
        TimelineCompletion = 3,
        ExternalSignal = 4,
        Delay = 5
    }

    public enum StoryGraphNotificationKind
    {
        Started = 0,
        Completed = 1,
        Saved = 2,
        Loaded = 3,
        Failed = 4
    }

    public enum StoryNodeNotificationKind
    {
        Entered = 0,
        Exited = 1
    }

    [Serializable]
    public sealed class StoryChoiceOption
    {
        [SerializeField] private string portId = string.Empty;
        [SerializeField] private string label = "Choice";
        [SerializeField] private bool isAvailable = true;

        public string PortId
        {
            get => portId;
            set => portId = value;
        }

        public string Label
        {
            get => label;
            set => label = value;
        }

        public bool IsAvailable
        {
            get => isAvailable;
            set => isAvailable = value;
        }
    }

    [Serializable]
    public sealed class StoryDialogueRequest
    {
        public string SessionId;
        public string RequestId;
        public string GraphId;
        public string NodeId;
        public string SpeakerId;
        public string SpeakerDisplayName;
        [TextArea] public string Body;
        public string PortraitKey;
        public string MoodTag;
        public bool AutoAdvance;
        public float AutoAdvanceDelaySeconds;
    }

    [Serializable]
    public sealed class StoryChoiceRequest
    {
        public string SessionId;
        public string RequestId;
        public string GraphId;
        public string NodeId;
        public string Prompt;
        public List<StoryChoiceOption> Options = new List<StoryChoiceOption>();
    }

    [Serializable]
    public sealed class StoryChoiceSelection
    {
        public string SessionId;
        public string RequestId;
        public string PortId;
        public int OptionIndex = -1;
    }

    [Serializable]
    public sealed class StoryAdvanceCommand
    {
        public string SessionId;
        public string RequestId;
    }

    [Serializable]
    public sealed class StoryTimelineRequest
    {
        public string SessionId;
        public string RequestId;
        public string GraphId;
        public string NodeId;
        public string CueId;
        public string CueDisplayName;
        public bool WaitForCompletion;
    }

    [Serializable]
    public sealed class StoryTimelineResult
    {
        public string SessionId;
        public string RequestId;
        public bool Completed = true;
        public string Message;
    }

    [Serializable]
    public sealed class StorySignalPayload
    {
        public string SessionId;
        public string GraphId;
        public string NodeId;
        public string SignalId;
        public string SignalDisplayName;
        public string Payload;
    }

    [Serializable]
    public sealed class StoryExternalSignal
    {
        public string SessionId;
        public string SignalId;
        public string Payload;
    }

    [Serializable]
    public sealed class StoryNodeNotification
    {
        public string SessionId;
        public string GraphId;
        public string NodeId;
        public string NodeTitle;
        public string NodeType;
        public StoryNodeNotificationKind Kind;
    }

    [Serializable]
    public sealed class StoryGraphNotification
    {
        public string SessionId;
        public string GraphId;
        public string GraphName;
        public StoryGraphNotificationKind Kind;
        public string Message;
    }

    [Serializable]
    public sealed class StoryStateChangedPayload
    {
        public string SessionId;
        public string MachineId;
        public string PreviousStateId;
        public string NextStateId;
    }

    /// <summary>
    /// Serializable wait descriptor that can be cached, saved, and restored.
    /// </summary>
    [Serializable]
    public sealed class StoryWaitDescriptor
    {
        public StoryWaitKind WaitKind;
        public string RequestId;
        public string DefaultPortId;
        public string AlternatePortId;
        public string SignalId;
        public float RemainingSeconds;
        public StoryDialogueRequest DialogueRequest;
        public StoryChoiceRequest ChoiceRequest;
        public StoryTimelineRequest TimelineRequest;

        public static StoryWaitDescriptor ForDialogue(StoryDialogueRequest request, string continuePortId)
        {
            return new StoryWaitDescriptor
            {
                WaitKind = StoryWaitKind.DialogueAdvance,
                RequestId = request.RequestId,
                DefaultPortId = continuePortId,
                RemainingSeconds = request.AutoAdvance ? Mathf.Max(0f, request.AutoAdvanceDelaySeconds) : -1f,
                DialogueRequest = request
            };
        }

        public static StoryWaitDescriptor ForChoice(StoryChoiceRequest request)
        {
            return new StoryWaitDescriptor
            {
                WaitKind = StoryWaitKind.ChoiceSelection,
                RequestId = request.RequestId,
                ChoiceRequest = request
            };
        }

        public static StoryWaitDescriptor ForTimeline(StoryTimelineRequest request, string completedPortId, string cancelledPortId)
        {
            return new StoryWaitDescriptor
            {
                WaitKind = StoryWaitKind.TimelineCompletion,
                RequestId = request.RequestId,
                DefaultPortId = completedPortId,
                AlternatePortId = cancelledPortId,
                TimelineRequest = request
            };
        }

        public static StoryWaitDescriptor ForExternalSignal(string signalId, string defaultPortId)
        {
            return new StoryWaitDescriptor
            {
                WaitKind = StoryWaitKind.ExternalSignal,
                RequestId = System.Guid.NewGuid().ToString("N"),
                DefaultPortId = defaultPortId,
                SignalId = signalId
            };
        }

        public static StoryWaitDescriptor ForDelay(float seconds, string defaultPortId)
        {
            return new StoryWaitDescriptor
            {
                WaitKind = StoryWaitKind.Delay,
                RequestId = System.Guid.NewGuid().ToString("N"),
                DefaultPortId = defaultPortId,
                RemainingSeconds = Mathf.Max(0f, seconds)
            };
        }

        public void RebindSession(string sessionId)
        {
            if (DialogueRequest != null)
            {
                DialogueRequest.SessionId = sessionId;
            }

            if (ChoiceRequest != null)
            {
                ChoiceRequest.SessionId = sessionId;
            }

            if (TimelineRequest != null)
            {
                TimelineRequest.SessionId = sessionId;
            }
        }
    }

    /// <summary>
    /// Runtime return value produced by a node execution step.
    /// </summary>
    public sealed class StoryExecutionResult
    {
        private StoryExecutionResult(StoryExecutionStatus status, string portId, StoryWaitDescriptor waitDescriptor, bool completeGraph)
        {
            Status = status;
            PortId = portId;
            WaitDescriptor = waitDescriptor;
            CompleteGraph = completeGraph;
        }

        public StoryExecutionStatus Status { get; }
        public string PortId { get; }
        public StoryWaitDescriptor WaitDescriptor { get; }
        public bool CompleteGraph { get; }

        public static StoryExecutionResult Continue(string portId)
        {
            return new StoryExecutionResult(StoryExecutionStatus.Continue, portId, null, false);
        }

        public static StoryExecutionResult Wait(StoryWaitDescriptor waitDescriptor)
        {
            return new StoryExecutionResult(StoryExecutionStatus.Wait, null, waitDescriptor, false);
        }

        public static StoryExecutionResult Stop(bool completeGraph = false)
        {
            return new StoryExecutionResult(StoryExecutionStatus.Stop, null, null, completeGraph);
        }
    }
}
