using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomNpcRuntimeVoiceoverService : MonoBehaviour
    {
        [SerializeField] private string ttsBaseUrl = "https://mc.agaii.org/TTS/api/v1";
        [SerializeField, Min(1f)] private float pollIntervalSeconds = 0.55f;
        [SerializeField, Min(2f)] private float requestTimeoutSeconds = 24f;
        [SerializeField, Min(1)] private int maxPollCount = 50;

        private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Action<AudioClip>>> pendingCallbacks = new Dictionary<string, List<Action<AudioClip>>>(StringComparer.Ordinal);

        public bool TryGetCachedClip(string cacheKey, out AudioClip clip)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                clip = null;
                return false;
            }

            return clipCache.TryGetValue(cacheKey, out clip) && clip != null;
        }

        public string BuildCacheKey(string speakerDisplayName, string text)
        {
            var normalizedSpeaker = NormalizeSpeakerToken(speakerDisplayName);
            var normalizedBody = NormalizeForHash(text);
            if (string.IsNullOrWhiteSpace(normalizedBody))
            {
                return string.Empty;
            }

            var hash = ComputeHashHex(string.Concat(normalizedSpeaker, "|", normalizedBody));
            return string.Concat("runtime_", normalizedSpeaker, "_", hash);
        }

        public void RequestVoiceClip(
            string speakerDisplayName,
            string text,
            Action<AudioClip> onReady,
            Action<string> onFailed = null)
        {
            var cacheKey = BuildCacheKey(speakerDisplayName, text);
            if (string.IsNullOrWhiteSpace(cacheKey) || string.IsNullOrWhiteSpace(text))
            {
                onFailed?.Invoke("No text available for runtime voice generation.");
                return;
            }

            if (TryGetCachedClip(cacheKey, out var cached))
            {
                onReady?.Invoke(cached);
                return;
            }

            if (pendingCallbacks.TryGetValue(cacheKey, out var callbacks))
            {
                callbacks.Add(onReady);
                return;
            }

            pendingCallbacks[cacheKey] = new List<Action<AudioClip>> { onReady };
            StartCoroutine(RequestClipRoutine(cacheKey, speakerDisplayName, text, onFailed));
        }

        private IEnumerator RequestClipRoutine(string cacheKey, string speakerDisplayName, string text, Action<string> onFailed)
        {
            var voiceConfig = ResolveVoiceConfig(speakerDisplayName, text);
            var requestPayload = JsonUtility.ToJson(new VoiceRequestPayload
            {
                text = text,
                speaker = voiceConfig.Speaker,
                language = "Auto",
                instruct = voiceConfig.Instruction
            });

            using var createRequest = BuildJsonPostRequest(BuildUrl("tts/custom-voice"), requestPayload);
            yield return createRequest.SendWebRequest();
            if (!IsSuccess(createRequest))
            {
                Fail(cacheKey, onFailed, $"TTS create request failed: {createRequest.error}");
                yield break;
            }

            var createResponse = JsonUtility.FromJson<JobCreatedPayload>(createRequest.downloadHandler.text);
            if (createResponse == null || string.IsNullOrWhiteSpace(createResponse.job_id))
            {
                Fail(cacheKey, onFailed, "TTS did not return a job id.");
                yield break;
            }

            var polls = 0;
            JobStatusPayload status = null;
            while (polls < maxPollCount)
            {
                polls++;
                using var statusRequest = UnityWebRequest.Get(BuildUrl($"jobs/{createResponse.job_id}/status"));
                statusRequest.timeout = Mathf.CeilToInt(requestTimeoutSeconds);
                yield return statusRequest.SendWebRequest();
                if (!IsSuccess(statusRequest))
                {
                    Fail(cacheKey, onFailed, $"TTS status request failed: {statusRequest.error}");
                    yield break;
                }

                status = JsonUtility.FromJson<JobStatusPayload>(statusRequest.downloadHandler.text);
                if (status != null && string.Equals(status.status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (status != null && (string.Equals(status.status, "failed", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(status.status, "cancelled", StringComparison.OrdinalIgnoreCase)))
                {
                    Fail(cacheKey, onFailed, string.IsNullOrWhiteSpace(status.error) ? "TTS job failed." : status.error);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(pollIntervalSeconds);
            }

            if (status == null || string.IsNullOrWhiteSpace(status.audio_url))
            {
                Fail(cacheKey, onFailed, "TTS job timed out before returning audio.");
                yield break;
            }

            var clipUrl = BuildAudioUrl(status.audio_url);
            var audioType = ResolveAudioType(clipUrl);
            using var audioRequest = UnityWebRequestMultimedia.GetAudioClip(clipUrl, audioType);
            audioRequest.timeout = Mathf.CeilToInt(requestTimeoutSeconds);
            yield return audioRequest.SendWebRequest();
            if (!IsSuccess(audioRequest))
            {
                Fail(cacheKey, onFailed, $"TTS audio download failed: {audioRequest.error}");
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(audioRequest);
            if (clip == null)
            {
                Fail(cacheKey, onFailed, "TTS audio clip decode failed.");
                yield break;
            }

            clip.name = cacheKey;
            clipCache[cacheKey] = clip;
            ClassroomStoryRuntimeVoiceCache.StoreClip(cacheKey, clip);
            Resolve(cacheKey, clip);
        }

        private void Resolve(string cacheKey, AudioClip clip)
        {
            if (!pendingCallbacks.TryGetValue(cacheKey, out var callbacks))
            {
                return;
            }

            pendingCallbacks.Remove(cacheKey);
            for (var index = 0; index < callbacks.Count; index++)
            {
                callbacks[index]?.Invoke(clip);
            }
        }

        private void Fail(string cacheKey, Action<string> onFailed, string message)
        {
            pendingCallbacks.Remove(cacheKey);
            onFailed?.Invoke(message);
            Debug.LogWarning($"[ClassroomNpcRuntimeVoiceoverService] {message}", this);
        }

        private string BuildUrl(string relativePath)
        {
            var root = string.IsNullOrWhiteSpace(ttsBaseUrl)
                ? "https://mc.agaii.org/TTS/api/v1"
                : ttsBaseUrl.Trim();
            if (!root.EndsWith("/", StringComparison.Ordinal))
            {
                root += "/";
            }

            return root + relativePath.TrimStart('/');
        }

        private string BuildAudioUrl(string audioUrl)
        {
            if (string.IsNullOrWhiteSpace(audioUrl))
            {
                return string.Empty;
            }

            if (audioUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return audioUrl;
            }

            var host = ttsBaseUrl;
            var apiIndex = host.IndexOf("/api", StringComparison.OrdinalIgnoreCase);
            if (apiIndex > 0)
            {
                host = host.Substring(0, apiIndex);
            }

            if (!host.EndsWith("/", StringComparison.Ordinal))
            {
                host += "/";
            }

            return host + audioUrl.TrimStart('/');
        }

        private static UnityWebRequest BuildJsonPostRequest(string url, string json)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var bodyRaw = Encoding.UTF8.GetBytes(json ?? string.Empty);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 30;
            return request;
        }

        private static bool IsSuccess(UnityWebRequest request)
        {
            return request != null && request.result is UnityWebRequest.Result.Success;
        }

        private static AudioType ResolveAudioType(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return AudioType.UNKNOWN;
            }

            if (url.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                return AudioType.MPEG;
            }

            if (url.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            {
                return AudioType.OGGVORBIS;
            }

            if (url.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                return AudioType.WAV;
            }

            return AudioType.UNKNOWN;
        }

        private static (string Speaker, string Instruction) ResolveVoiceConfig(string speakerDisplayName, string text)
        {
            var normalizedSpeaker = NormalizeSpeakerToken(speakerDisplayName);
            var lowerText = (text ?? string.Empty).ToLowerInvariant();

            if (normalizedSpeaker.Contains("mira", StringComparison.Ordinal))
            {
                return ("serena", "Speak in a calm, precise teacher tone. Keep sentences grounded.");
            }

            if (normalizedSpeaker.Contains("nia", StringComparison.Ordinal))
            {
                return ("vivian", "Speak warmly with a hint of nervous energy. Keep it brief.");
            }

            if (normalizedSpeaker.Contains("theo", StringComparison.Ordinal))
            {
                return ("ryan", "Speak with playful sarcasm and confidence. Keep lines short.");
            }

            if (normalizedSpeaker.Contains("you", StringComparison.Ordinal))
            {
                return ("aiden", "Speak clearly and focused, like a student making quick decisions.");
            }

            if (lowerText.Contains("surprise", StringComparison.Ordinal) || lowerText.Contains("wow", StringComparison.Ordinal))
            {
                return ("serena", "Speak with mild surprise but stay concise.");
            }

            return ("serena", "Speak naturally and briefly.");
        }

        private static string NormalizeForHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            var previousWasWhitespace = false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsWhiteSpace(character))
                {
                    if (previousWasWhitespace)
                    {
                        continue;
                    }

                    builder.Append(' ');
                    previousWasWhitespace = true;
                    continue;
                }

                builder.Append(char.ToLowerInvariant(character));
                previousWasWhitespace = false;
            }

            return builder.ToString().Trim();
        }

        private static string NormalizeSpeakerToken(string speaker)
        {
            var source = string.IsNullOrWhiteSpace(speaker) ? "narrator" : speaker.Trim();
            var builder = new StringBuilder(source.Length);
            var previousWasSeparator = false;

            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                    previousWasSeparator = false;
                    continue;
                }

                if (previousWasSeparator)
                {
                    continue;
                }

                builder.Append('_');
                previousWasSeparator = true;
            }

            var token = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(token) ? "narrator" : token;
        }

        private static string ComputeHashHex(string source)
        {
            using var algorithm = SHA1.Create();
            var bytes = Encoding.UTF8.GetBytes(source ?? string.Empty);
            var hash = algorithm.ComputeHash(bytes);
            var builder = new StringBuilder(12);
            for (var index = 0; index < hash.Length && builder.Length < 12; index++)
            {
                builder.Append(hash[index].ToString("x2"));
            }

            return builder.ToString();
        }

        [Serializable]
        private sealed class VoiceRequestPayload
        {
            public string text;
            public string speaker;
            public string language;
            public string instruct;
        }

        [Serializable]
        private sealed class JobCreatedPayload
        {
            public string job_id;
        }

        [Serializable]
        private sealed class JobStatusPayload
        {
            public string status;
            public string error;
            public string audio_url;
        }
    }
}
