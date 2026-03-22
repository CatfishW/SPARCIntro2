using ModularStoryFlow.Runtime.Actions;
using ModularStoryFlow.Runtime.Execution;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [CreateAssetMenu(fileName = "LabStartIntroBriefingAction", menuName = "Story Flow/Actions/Lab/Start Intro Briefing")]
    public sealed class LabStartIntroBriefingActionAsset : StoryActionAsset
    {
        public override void Execute(IStoryExecutionContext context)
        {
            var director = Object.FindFirstObjectByType<LabCapConversationDirector>(FindObjectsInactive.Include);
            director?.StartIntroBriefing();
        }
    }
}
