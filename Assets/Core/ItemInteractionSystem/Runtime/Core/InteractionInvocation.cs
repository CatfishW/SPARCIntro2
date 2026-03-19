namespace ItemInteraction
{
    public sealed class InteractionInvocation
    {
        public InteractionInvocation(InteractionDirector director, InteractableItem target, InteractionOption option)
        {
            Director = director;
            Target = target;
            Option = option;
        }

        public InteractionDirector Director { get; }
        public InteractableItem Target { get; }
        public InteractionOption Option { get; }
        public string StoryId => Target != null ? Target.storyId : string.Empty;
        public string OptionId => Option != null ? Option.id : string.Empty;
    }
}
