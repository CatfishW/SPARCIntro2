using UnityEngine;
using UnityEngine.Playables;

namespace ModularStoryFlow.Samples.QuickStart
{
    [CreateAssetMenu(fileName = "QuickStartPulsePlayable", menuName = "Story Flow/Samples/Quick Start Pulse Playable")]
    public sealed class QuickStartPulsePlayableAsset : PlayableAsset
    {
        [SerializeField] private double durationSeconds = 1.25d;
        [SerializeField] private string logMessage = "Quick Start timeline played.";

        public override double duration => durationSeconds;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<QuickStartPulsePlayableBehaviour>.Create(graph);
            playable.GetBehaviour().LogMessage = logMessage;
            return playable;
        }
    }

    public sealed class QuickStartPulsePlayableBehaviour : PlayableBehaviour
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
