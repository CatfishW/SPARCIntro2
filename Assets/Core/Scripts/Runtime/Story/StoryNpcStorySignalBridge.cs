using ItemInteraction;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class StoryNpcStorySignalBridge : MonoBehaviour
    {
        [SerializeField] private StoryFlowChannels channels;
        [SerializeField] private StoryNpcRegistry registry;
        [SerializeField] private bool raiseOnlyForRegisteredNpcs = true;
        [SerializeField] private string sessionId = string.Empty;

        private void Awake()
        {
            registry = registry != null ? registry : FindFirstObjectByType<StoryNpcRegistry>();
        }

        private void OnEnable()
        {
            StoryNpcAgent.AnyInteractionTriggered -= HandleNpcInteractionTriggered;
            StoryNpcAgent.AnyInteractionTriggered += HandleNpcInteractionTriggered;
        }

        private void OnDisable()
        {
            StoryNpcAgent.AnyInteractionTriggered -= HandleNpcInteractionTriggered;
        }

        public void Configure(StoryFlowChannels storyChannels, string activeSessionId)
        {
            channels = storyChannels;
            sessionId = activeSessionId ?? string.Empty;
            registry = registry != null ? registry : FindFirstObjectByType<StoryNpcRegistry>();
        }

        public void SetSessionId(string activeSessionId)
        {
            sessionId = activeSessionId ?? string.Empty;
        }

        private void HandleNpcInteractionTriggered(StoryNpcInteractionPayload payload)
        {
            if (payload == null || channels == null || string.IsNullOrWhiteSpace(payload.InteractionId))
            {
                return;
            }

            if (raiseOnlyForRegisteredNpcs)
            {
                registry = registry != null ? registry : FindFirstObjectByType<StoryNpcRegistry>();
                if (registry == null || registry.GetNpc(payload.NpcId) != payload.Agent)
                {
                    return;
                }
            }

            channels.ExternalSignals?.Raise(new StoryExternalSignal
            {
                SessionId = sessionId,
                SignalId = payload.InteractionId,
                Payload = payload.NpcId
            });
        }
    }
}
