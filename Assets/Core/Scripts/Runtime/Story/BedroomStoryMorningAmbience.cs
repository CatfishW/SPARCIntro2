using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class BedroomStoryMorningAmbience : MonoBehaviour
    {
        [SerializeField, Min(8f)] private float loopDurationSeconds = 18f;
        [SerializeField, Range(0f, 1f)] private float volume = 0.16f;
        [SerializeField] private bool playOnAwake = true;
        [SerializeField] private int sampleRate = 44100;

        private AudioSource audioSource;
        private AudioClip generatedClip;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            ConfigureAudioSource();

            if (generatedClip == null)
            {
                generatedClip = BuildClip();
            }

            audioSource.clip = generatedClip;

            if (playOnAwake && generatedClip != null && !audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }

        private void OnDestroy()
        {
            if (generatedClip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(generatedClip);
            }
            else
            {
                DestroyImmediate(generatedClip);
            }
        }

        private void ConfigureAudioSource()
        {
            audioSource.loop = true;
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = volume;
            audioSource.dopplerLevel = 0f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
        }

        private AudioClip BuildClip()
        {
            var clampedSampleRate = Mathf.Max(22050, sampleRate);
            var sampleCount = Mathf.Max(1, Mathf.CeilToInt(loopDurationSeconds * clampedSampleRate));
            var samples = new float[sampleCount];
            uint noiseState = 0xA53A9E1Fu;

            for (var index = 0; index < sampleCount; index++)
            {
                var time = index / (float)clampedSampleRate;
                var baseNoise = NextNoise(ref noiseState);

                var trafficRumble = 0.018f * Mathf.Sin(Mathf.PI * 2f * (37f + (Mathf.Sin(time * 0.17f) * 3f)) * time);
                trafficRumble += 0.012f * Mathf.Sin(Mathf.PI * 2f * 74f * time);
                trafficRumble += baseNoise * 0.009f;

                var air = 0.006f * Mathf.Sin(Mathf.PI * 2f * 0.11f * time);
                air += baseNoise * 0.0045f;

                var chirps = BuildBirdChirps(time);
                var tick = BuildClockTick(time);
                var phoneBuzz = BuildPhoneBuzz(time);

                var sample = trafficRumble + air + chirps + tick + phoneBuzz;
                samples[index] = Mathf.Clamp(sample, -0.09f, 0.09f);
            }

            var clip = AudioClip.Create("BedroomMorningAmbience", sampleCount, 1, clampedSampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float BuildBirdChirps(float time)
        {
            float sample = 0f;
            sample += BuildChirp(time, 2.15f, 0.11f, 1680f, 2420f, 0.015f);
            sample += BuildChirp(time, 5.4f, 0.09f, 1880f, 2740f, 0.012f);
            sample += BuildChirp(time, 9.25f, 0.13f, 1740f, 2360f, 0.014f);
            sample += BuildChirp(time, 13.1f, 0.1f, 1940f, 2860f, 0.013f);
            sample += BuildChirp(time, 15.85f, 0.12f, 1820f, 2480f, 0.011f);
            return sample;
        }

        private static float BuildClockTick(float time)
        {
            var beat = time % 1f;
            if (beat > 0.035f)
            {
                return 0f;
            }

            var envelope = 1f - (beat / 0.035f);
            return envelope * envelope * 0.006f * Mathf.Sin(Mathf.PI * 2f * 1100f * time);
        }

        private static float BuildPhoneBuzz(float time)
        {
            return BuildBuzz(time, 6.7f, 0.22f, 120f, 0.0065f) +
                   BuildBuzz(time, 6.98f, 0.17f, 160f, 0.005f);
        }

        private static float BuildChirp(float time, float centerTime, float duration, float startFrequency, float endFrequency, float amplitude)
        {
            var halfDuration = duration * 0.5f;
            var local = time - centerTime;
            if (local < -halfDuration || local > halfDuration)
            {
                return 0f;
            }

            var normalized = (local + halfDuration) / duration;
            var envelope = Mathf.Sin(normalized * Mathf.PI);
            var frequency = Mathf.Lerp(startFrequency, endFrequency, normalized);
            return envelope * envelope * amplitude * Mathf.Sin(Mathf.PI * 2f * frequency * time);
        }

        private static float BuildBuzz(float time, float startTime, float duration, float frequency, float amplitude)
        {
            var local = time - startTime;
            if (local < 0f || local > duration)
            {
                return 0f;
            }

            var normalized = local / duration;
            var envelope = Mathf.Sin(normalized * Mathf.PI);
            var wobble = 1f + (0.07f * Mathf.Sin(local * 80f));
            return envelope * amplitude * Mathf.Sin(Mathf.PI * 2f * frequency * wobble * time);
        }

        private static float NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return ((state & 0x7FFFFFFF) / 1073741824f) - 1f;
        }
    }
}
