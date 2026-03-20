using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    public static class ClassroomStoryRuntimeVoiceCache
    {
        private static readonly Dictionary<string, AudioClip> ClipCache = new Dictionary<string, AudioClip>(StringComparer.Ordinal);

        public static void StoreClip(string clipKey, AudioClip clip)
        {
            if (string.IsNullOrWhiteSpace(clipKey) || clip == null)
            {
                return;
            }

            ClipCache[clipKey] = clip;
        }

        public static bool TryGetClip(string clipKey, out AudioClip clip)
        {
            clip = null;
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                return false;
            }

            return ClipCache.TryGetValue(clipKey, out clip) && clip != null;
        }

        public static bool TryGetClipDuration(string clipKey, out float durationSeconds)
        {
            durationSeconds = 0f;
            if (!TryGetClip(clipKey, out var clip) || clip == null)
            {
                return false;
            }

            durationSeconds = Mathf.Max(0f, clip.length);
            return durationSeconds > 0f;
        }
    }
}
