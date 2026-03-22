using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class BedroomStoryMorningAmbience : MonoBehaviour
    {
        [SerializeField] private AudioClip ambienceClip;
        [SerializeField] private string fallbackResourcesPath = "Audio/forest-window-light";
        [SerializeField] private string fallbackEditorAssetPath = "Assets/Core/Audio/forest-window-light.mp3";
        [SerializeField, Range(0f, 1f)] private float volume = 0.16f;
        [SerializeField] private bool playOnAwake = true;

        private AudioSource audioSource;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            ConfigureAudioSource();
        }

        private void Start()
        {
            var clip = ResolveAmbienceClip();
            if (clip == null)
            {
                Debug.LogWarning($"[BedroomStoryMorningAmbience] Missing ambience clip. Assign one or provide fallback at '{fallbackEditorAssetPath}'.", this);
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

        private void OnValidate()
        {
            if (audioSource != null)
            {
                ConfigureAudioSource();
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
    }
}
