using UnityEngine;
using UnityEngine.Playables;

namespace Blocks.Gameplay.Core.Story
{
    [CreateAssetMenu(fileName = "LabShrinkTimelinePlayable", menuName = "Story Flow/Lab/Shrink Timeline Playable")]
    public sealed class LabShrinkTimelinePlayableAsset : PlayableAsset
    {
        [SerializeField, Min(0.5f)] private double durationSeconds = 3.6d;
        [SerializeField, Range(0f, 0.8f)] private float playerDelayNormalized = 0.08f;
        [SerializeField, Range(0f, 0.9f)] private float capDelayNormalized = 0.2f;
        [SerializeField] private AnimationCurve shrinkCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public override double duration => durationSeconds;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<LabShrinkTimelinePlayableBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.DurationSeconds = Mathf.Max(0.5f, (float)durationSeconds);
            behaviour.PlayerDelayNormalized = Mathf.Clamp01(playerDelayNormalized);
            behaviour.CapDelayNormalized = Mathf.Clamp01(capDelayNormalized);
            behaviour.ShrinkCurve = shrinkCurve ?? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            return playable;
        }
    }

    public sealed class LabShrinkTimelinePlayableBehaviour : PlayableBehaviour
    {
        public float DurationSeconds;
        public float PlayerDelayNormalized;
        public float CapDelayNormalized;
        public AnimationCurve ShrinkCurve;

        private LabShrinkSequenceController controller;
        private bool started;
        private bool finished;

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            if (started)
            {
                return;
            }

            controller = LabShrinkSequenceController.ActiveInstance;
            if (controller == null)
            {
                controller = Object.FindFirstObjectByType<LabShrinkSequenceController>(FindObjectsInactive.Include);
            }

            if (controller == null)
            {
                return;
            }

            controller.BeginTimelineDrivenShrink(DurationSeconds, PlayerDelayNormalized, CapDelayNormalized, ShrinkCurve);
            started = true;
        }

        public override void PrepareFrame(Playable playable, FrameData info)
        {
            if (!started || finished || controller == null)
            {
                return;
            }

            controller.EvaluateTimelineDrivenShrink(Normalize(playable));
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (!started || finished || controller == null)
            {
                return;
            }

            var normalized = Normalize(playable);
            controller.EndTimelineDrivenShrink(normalized >= 0.995f);
            finished = true;
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (!started || finished || controller == null)
            {
                return;
            }

            controller.EndTimelineDrivenShrink(completed: false);
            finished = true;
        }

        private float Normalize(Playable playable)
        {
            var duration = DurationSeconds > 0.01f ? DurationSeconds : (float)playable.GetDuration();
            if (duration <= 0.01f)
            {
                return 1f;
            }

            return Mathf.Clamp01((float)(playable.GetTime() / duration));
        }
    }
}
