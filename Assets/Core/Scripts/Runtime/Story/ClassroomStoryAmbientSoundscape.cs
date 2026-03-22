using System.Collections;
using Blocks.Gameplay.Core;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class ClassroomStoryAmbientSoundscape : MonoBehaviour
    {
        [SerializeField] private AudioClip ambienceClip;
        [SerializeField] private string fallbackResourcesPath = "Audio/Story/Classroom/classroom_ambience_window";
        [SerializeField] private string fallbackEditorAssetPath = "Assets/Core/Audio/forest-window-light.mp3";
        [SerializeField] private SoundDef hallwayShuffleSound;
        [SerializeField] private SoundDef clockTickSound;
        [SerializeField, Range(0f, 1f)] private float ambienceVolume = 0.18f;
        [SerializeField, Range(0f, 1f)] private float hallwayShuffleVolume = 0.15f;
        [SerializeField, Range(0f, 1f)] private float clockTickVolume = 0.1f;
        [SerializeField, Min(4f)] private float minFoleyIntervalSeconds = 8f;
        [SerializeField, Min(5f)] private float maxFoleyIntervalSeconds = 14f;
        [SerializeField] private bool playOnAwake = true;
        [SerializeField] private bool enableFoley = true;

        private AudioSource audioSource;
        private Coroutine foleyRoutine;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            ConfigureAudioSource();
        }

        private void OnEnable()
        {
            if (enableFoley && foleyRoutine == null)
            {
                foleyRoutine = StartCoroutine(FoleyLoop());
            }
        }

        private void Start()
        {
            var clip = ResolveAmbienceClip();
            if (clip == null)
            {
                Debug.LogWarning("[ClassroomStoryAmbientSoundscape] Missing ambience clip. Assign one or provide a fallback path.", this);
                return;
            }

            if (audioSource.clip != clip)
            {
                audioSource.clip = clip;
            }

            if (playOnAwake && !audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }

        private void OnDisable()
        {
            if (foleyRoutine != null)
            {
                StopCoroutine(foleyRoutine);
                foleyRoutine = null;
            }
        }

        private void OnValidate()
        {
            if (audioSource != null)
            {
                ConfigureAudioSource();
            }

            if (maxFoleyIntervalSeconds < minFoleyIntervalSeconds)
            {
                maxFoleyIntervalSeconds = minFoleyIntervalSeconds;
            }
        }

        public void ConfigureSoundDefs(SoundDef movementSound, SoundDef tickSound)
        {
            if (hallwayShuffleSound == null && movementSound != null)
            {
                hallwayShuffleSound = movementSound;
            }

            if (clockTickSound == null && tickSound != null)
            {
                clockTickSound = tickSound;
            }
        }

        private IEnumerator FoleyLoop()
        {
            yield return new WaitForSecondsRealtime(2.25f);
            while (enableFoley)
            {
                var wait = Random.Range(minFoleyIntervalSeconds, maxFoleyIntervalSeconds);
                yield return new WaitForSecondsRealtime(wait);

                var shouldPlayTick = clockTickSound != null && Random.value < 0.55f;
                if (shouldPlayTick)
                {
                    PlayClockTick();
                }
                else
                {
                    PlayHallwayShuffle();
                }
            }

            foleyRoutine = null;
        }

        private void ConfigureAudioSource()
        {
            audioSource.loop = true;
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = ambienceVolume;
            audioSource.dopplerLevel = 0f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
        }

        private AudioClip ResolveAmbienceClip()
        {
            if (ambienceClip != null)
            {
                return ambienceClip;
            }

            if (!string.IsNullOrWhiteSpace(fallbackResourcesPath))
            {
                ambienceClip = Resources.Load<AudioClip>(fallbackResourcesPath);
                if (ambienceClip != null)
                {
                    return ambienceClip;
                }
            }

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(fallbackEditorAssetPath))
            {
                ambienceClip = AssetDatabase.LoadAssetAtPath<AudioClip>(fallbackEditorAssetPath);
            }
#endif
            return ambienceClip;
        }

        private void PlayHallwayShuffle()
        {
            if (hallwayShuffleSound == null)
            {
                return;
            }

            var overrideData = new SoundEmitter.SoundDefOverrideData
            {
                BasePitchInCents = Random.Range(-220f, 85f),
                VolumeScale = Random.Range(0.42f, 0.68f),
                BaseLowPassCutoff = Random.Range(420f, 980f)
            };

            CoreDirector.RequestAudio(hallwayShuffleSound)
                .AttachedTo(transform)
                .WithOverrides(overrideData)
                .AsReserved(SoundEmitter.ReservedInfo.ReservedEmitterAndAudioSources)
                .Play(hallwayShuffleVolume);
        }

        private void PlayClockTick()
        {
            if (clockTickSound == null)
            {
                return;
            }

            var overrideData = new SoundEmitter.SoundDefOverrideData
            {
                BasePitchInCents = Random.Range(80f, 220f),
                VolumeScale = 0.48f,
                BaseLowPassCutoff = 1800f
            };

            CoreDirector.RequestAudio(clockTickSound)
                .AttachedTo(transform)
                .WithOverrides(overrideData)
                .AsReserved(SoundEmitter.ReservedInfo.ReservedEmitterAndAudioSources)
                .Play(clockTickVolume);
        }
    }
}
