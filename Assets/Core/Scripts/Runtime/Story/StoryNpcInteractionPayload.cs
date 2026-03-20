using ItemInteraction;

namespace Blocks.Gameplay.Core.Story
{
    public sealed class StoryNpcInteractionPayload
    {
        public StoryNpcInteractionPayload(StoryNpcAgent agent, InteractionInvocation invocation, string interactionId)
        {
            Agent = agent;
            Invocation = invocation;
            InteractionId = interactionId ?? string.Empty;
        }

        public StoryNpcAgent Agent { get; }
        public InteractionInvocation Invocation { get; }
        public string InteractionId { get; }
        public string NpcId => Agent != null ? Agent.NpcId : string.Empty;
        public string OptionId => Invocation != null ? Invocation.OptionId : string.Empty;
        public InteractableItem Interactable => Invocation != null ? Invocation.Target : null;
    }
}
