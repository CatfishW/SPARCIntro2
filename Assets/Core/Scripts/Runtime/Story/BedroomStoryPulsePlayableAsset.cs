using UnityEngine;
using UnityEngine.Playables;

namespace Blocks.Gameplay.Core.Story.Editor
{
    [CreateAssetMenu(fileName = "BedroomStoryPulsePlayable", menuName = "Story Flow/Bedroom/Pulse Playable")]
    public sealed class BedroomStoryPulsePlayableAsset : PlayableAsset
    {
        [SerializeField] private double durationSeconds = 1.1d;
        [SerializeField] private string logMessage = "Bedroom story timeline played.";

        public override double duration => durationSeconds;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<BedroomStoryPulsePlayableBehaviour>.Create(graph);
            playable.GetBehaviour().LogMessage = logMessage;
            return playable;
        }
    }

    public sealed class BedroomStoryPulsePlayableBehaviour : PlayableBehaviour
    {
        public string LogMessage;

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            if (!string.IsNullOrWhiteSpace(LogMessage))
            {
                Debug.Log(LogMessage);
            }
        }
    }
}
