using System;
using ModularStoryFlow.Runtime.Events;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Channels
{
    public abstract class StoryEventChannelBase<TPayload> : ScriptableObject
    {
        public event Action<TPayload> Raised;

        public void Raise(TPayload payload)
        {
            Raised?.Invoke(payload);
        }

        public void Register(Action<TPayload> listener)
        {
            Raised += listener;
        }

        public void Unregister(Action<TPayload> listener)
        {
            Raised -= listener;
        }
    }

    [CreateAssetMenu(fileName = "DialogueRequests", menuName = "Story Flow/Channels/Dialogue Request Channel")]
    public sealed partial class StoryDialogueRequestChannel : StoryEventChannelBase<StoryDialogueRequest> { }

    [CreateAssetMenu(fileName = "AdvanceCommands", menuName = "Story Flow/Channels/Advance Command Channel")]
    public sealed partial class StoryAdvanceCommandChannel : StoryEventChannelBase<StoryAdvanceCommand> { }

    [CreateAssetMenu(fileName = "ChoiceRequests", menuName = "Story Flow/Channels/Choice Request Channel")]
    public sealed partial class StoryChoiceRequestChannel : StoryEventChannelBase<StoryChoiceRequest> { }

    [CreateAssetMenu(fileName = "ChoiceSelections", menuName = "Story Flow/Channels/Choice Selection Channel")]
    public sealed partial class StoryChoiceSelectionChannel : StoryEventChannelBase<StoryChoiceSelection> { }

    [CreateAssetMenu(fileName = "TimelineRequests", menuName = "Story Flow/Channels/Timeline Request Channel")]
    public sealed partial class StoryTimelineRequestChannel : StoryEventChannelBase<StoryTimelineRequest> { }

    [CreateAssetMenu(fileName = "TimelineResults", menuName = "Story Flow/Channels/Timeline Result Channel")]
    public sealed partial class StoryTimelineResultChannel : StoryEventChannelBase<StoryTimelineResult> { }

    [CreateAssetMenu(fileName = "RaisedSignals", menuName = "Story Flow/Channels/Signal Raised Channel")]
    public sealed partial class StorySignalRaisedChannel : StoryEventChannelBase<StorySignalPayload> { }

    [CreateAssetMenu(fileName = "ExternalSignals", menuName = "Story Flow/Channels/External Signal Channel")]
    public sealed partial class StoryExternalSignalChannel : StoryEventChannelBase<StoryExternalSignal> { }

    [CreateAssetMenu(fileName = "StateChanged", menuName = "Story Flow/Channels/State Changed Channel")]
    public sealed partial class StoryStateChangedChannel : StoryEventChannelBase<StoryStateChangedPayload> { }

    [CreateAssetMenu(fileName = "NodeNotifications", menuName = "Story Flow/Channels/Node Notification Channel")]
    public sealed partial class StoryNodeNotificationChannel : StoryEventChannelBase<StoryNodeNotification> { }

    [CreateAssetMenu(fileName = "GraphNotifications", menuName = "Story Flow/Channels/Graph Notification Channel")]
    public sealed partial class StoryGraphNotificationChannel : StoryEventChannelBase<StoryGraphNotification> { }

    [CreateAssetMenu(fileName = "StoryFlowChannels", menuName = "Story Flow/Project/Channels")]
    public sealed partial class StoryFlowChannels : ScriptableObject
    {
        [SerializeField] private StoryDialogueRequestChannel dialogueRequests;
        [SerializeField] private StoryAdvanceCommandChannel advanceCommands;
        [SerializeField] private StoryChoiceRequestChannel choiceRequests;
        [SerializeField] private StoryChoiceSelectionChannel choiceSelections;
        [SerializeField] private StoryTimelineRequestChannel timelineRequests;
        [SerializeField] private StoryTimelineResultChannel timelineResults;
        [SerializeField] private StorySignalRaisedChannel raisedSignals;
        [SerializeField] private StoryExternalSignalChannel externalSignals;
        [SerializeField] private StoryStateChangedChannel stateChanged;
        [SerializeField] private StoryNodeNotificationChannel nodeNotifications;
        [SerializeField] private StoryGraphNotificationChannel graphNotifications;

        public StoryDialogueRequestChannel DialogueRequests => dialogueRequests;
        public StoryAdvanceCommandChannel AdvanceCommands => advanceCommands;
        public StoryChoiceRequestChannel ChoiceRequests => choiceRequests;
        public StoryChoiceSelectionChannel ChoiceSelections => choiceSelections;
        public StoryTimelineRequestChannel TimelineRequests => timelineRequests;
        public StoryTimelineResultChannel TimelineResults => timelineResults;
        public StorySignalRaisedChannel RaisedSignals => raisedSignals;
        public StoryExternalSignalChannel ExternalSignals => externalSignals;
        public StoryStateChangedChannel StateChanged => stateChanged;
        public StoryNodeNotificationChannel NodeNotifications => nodeNotifications;
        public StoryGraphNotificationChannel GraphNotifications => graphNotifications;

        public void Configure(
            StoryDialogueRequestChannel dialogueRequestChannel,
            StoryAdvanceCommandChannel advanceCommandChannel,
            StoryChoiceRequestChannel choiceRequestChannel,
            StoryChoiceSelectionChannel choiceSelectionChannel,
            StoryTimelineRequestChannel timelineRequestChannel,
            StoryTimelineResultChannel timelineResultChannel,
            StorySignalRaisedChannel signalRaisedChannel,
            StoryExternalSignalChannel externalSignalChannel,
            StoryStateChangedChannel stateChangedChannel,
            StoryNodeNotificationChannel nodeNotificationChannel,
            StoryGraphNotificationChannel graphNotificationChannel)
        {
            dialogueRequests = dialogueRequestChannel;
            advanceCommands = advanceCommandChannel;
            choiceRequests = choiceRequestChannel;
            choiceSelections = choiceSelectionChannel;
            timelineRequests = timelineRequestChannel;
            timelineResults = timelineResultChannel;
            raisedSignals = signalRaisedChannel;
            externalSignals = externalSignalChannel;
            stateChanged = stateChangedChannel;
            nodeNotifications = nodeNotificationChannel;
            graphNotifications = graphNotificationChannel;
        }
    }
}
