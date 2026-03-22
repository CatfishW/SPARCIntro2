using ModularStoryFlow.Runtime.Events;

namespace Blocks.Gameplay.Core.Story
{
    public sealed class LabStoryPresentationState
    {
        public StoryDialogueRequest ActiveDialogue { get; private set; }
        public StoryChoiceRequest ActiveChoice { get; private set; }

        public void ShowDialogue(StoryDialogueRequest request)
        {
            ActiveDialogue = request;
            ActiveChoice = null;
        }

        public void ShowChoice(StoryChoiceRequest request)
        {
            ActiveChoice = request;
            ActiveDialogue = null;
        }

        public void Clear()
        {
            ActiveDialogue = null;
            ActiveChoice = null;
        }
    }
}
