using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    public static class ClassroomStoryVoiceLibrary
    {
        private const string ResourceRoot = "Audio/Story/Classroom";

        private static readonly Dictionary<string, AudioClip> ClipCache = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private static readonly Dictionary<string, float> DurationCache = new Dictionary<string, float>(StringComparer.Ordinal);

        public static string BuildClipKey(string speakerDisplayName, string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            var normalizedSpeaker = NormalizeSpeakerToken(speakerDisplayName);
            var normalizedBody = NormalizeForHash(body);
            var hash = ComputeHashHex(string.Concat(normalizedSpeaker, "|", normalizedBody));
            return string.Concat(normalizedSpeaker, "_", hash);
        }

        public static string ResolveMoodTag(string speakerDisplayName, string body)
        {
            var speaker = NormalizeSpeakerToken(speakerDisplayName);
            if (speaker.Contains("mira", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(body) && body.Contains("Lab door", StringComparison.OrdinalIgnoreCase))
                {
                    return "teacher_decisive";
                }

                return "teacher_calm";
            }

            if (speaker.Contains("nia", StringComparison.Ordinal))
            {
                return "friend_warm";
            }

            if (speaker.Contains("theo", StringComparison.Ordinal))
            {
                return "skeptic_wry";
            }

            if (speaker.Contains("you", StringComparison.Ordinal))
            {
                return "player_internal";
            }

            return "neutral";
        }

        public static bool TryGetClip(string clipKey, out AudioClip clip)
        {
            clip = null;
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                return false;
            }

            if (ClipCache.TryGetValue(clipKey, out clip) && clip != null)
            {
                return true;
            }

            clip = Resources.Load<AudioClip>(string.Concat(ResourceRoot, "/", clipKey));
            ClipCache[clipKey] = clip;
            return clip != null;
        }

        public static bool TryGetClipDuration(string clipKey, out float durationSeconds)
        {
            durationSeconds = 0f;
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                return false;
            }

            if (DurationCache.TryGetValue(clipKey, out durationSeconds) && durationSeconds > 0f)
            {
                return true;
            }

            if (!TryGetClip(clipKey, out var clip) || clip == null)
            {
                return false;
            }

            durationSeconds = Mathf.Max(0f, clip.length);
            DurationCache[clipKey] = durationSeconds;
            return durationSeconds > 0f;
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
    }
}
