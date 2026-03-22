using UnityEngine;
using Blocks.Gameplay.Core;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class NpcAnimationEventRelay : MonoBehaviour
    {
        [SerializeField] private SoundDef footstepSound;
        [SerializeField] private SoundDef landSound;
        [SerializeField, Min(0.05f)] private float minStepIntervalSeconds = 0.16f;
        [SerializeField, Range(0f, 1f)] private float walkStepVolume = 0.45f;
        [SerializeField, Range(0f, 1f)] private float runStepVolume = 0.58f;
        [SerializeField, Range(0f, 1f)] private float landVolume = 0.7f;

        private float lastStepTime = -999f;

        public void OnFootstepWalk()
        {
            TryPlayStep(isRun: false);
        }

        public void OnFootstepRun()
        {
            TryPlayStep(isRun: true);
        }

        public void OnLand()
        {
            var sound = landSound != null ? landSound : footstepSound;
            if (sound == null)
            {
                return;
            }

            CoreDirector.RequestAudio(sound)
                .AttachedTo(transform)
                .AsReserved(SoundEmitter.ReservedInfo.ReservedEmitterAndAudioSources)
                .Play(landVolume);
        }

        public void Configure(SoundDef sharedFootstep, SoundDef sharedLand = null)
        {
            if (sharedFootstep != null)
            {
                footstepSound = sharedFootstep;
            }

            if (sharedLand != null)
            {
                landSound = sharedLand;
            }
        }

        private void TryPlayStep(bool isRun)
        {
            if (footstepSound == null)
            {
                return;
            }

            if (Time.time - lastStepTime < minStepIntervalSeconds)
            {
                return;
            }

            var overrideData = new SoundEmitter.SoundDefOverrideData
            {
                BasePitchInCents = isRun ? 260f : 40f,
                VolumeScale = isRun ? 0.95f : 0.78f,
                BaseLowPassCutoff = isRun ? 1200f : 600f
            };

            lastStepTime = Time.time;
            CoreDirector.RequestAudio(footstepSound)
                .AttachedTo(transform)
                .WithOverrides(overrideData)
                .AsReserved(SoundEmitter.ReservedInfo.ReservedEmitterAndAudioSources)
                .Play(isRun ? runStepVolume : walkStepVolume);
        }
    }
}
