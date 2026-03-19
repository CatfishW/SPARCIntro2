using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blocks.Gameplay.Core.Customization
{
    public static class CharacterCustomizationDefaults
    {
        public const string CatalogResourcePath = "CharacterCustomizationCatalog";
        public const string PlayerPrefsKey = "Blocks.CharacterCustomization.SelectedPresetId";
    }

    [Serializable]
    public sealed class CharacterCustomizationPreset
    {
        [SerializeField] private string presetId;
        [SerializeField] private string presetDisplayName;
        [SerializeField] private GameObject presetCharacterPrefab;
        [SerializeField] private bool defaultPreset;
        [SerializeField] private string presetSourceAssetPath;

        public string id => presetId;
        public string displayName => presetDisplayName;
        public GameObject characterPrefab => presetCharacterPrefab;
        public bool isDefault => defaultPreset;
        public string sourceAssetPath => presetSourceAssetPath;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(presetDisplayName))
                {
                    return presetDisplayName;
                }

                if (presetCharacterPrefab != null)
                {
                    return presetCharacterPrefab.name;
                }

                return string.IsNullOrWhiteSpace(presetId) ? "Character" : presetId;
            }
        }

#if UNITY_EDITOR
            public void SetEditorData(string idValue, string displayNameValue, GameObject prefabValue, bool defaultValue, string assetPath)
            {
                presetId = idValue;
                presetDisplayName = displayNameValue;
                presetCharacterPrefab = prefabValue;
                defaultPreset = defaultValue;
                presetSourceAssetPath = assetPath;
            }
#endif
    }

    [CreateAssetMenu(menuName = "Blocks/Character Customization/Catalog", fileName = "CharacterCustomizationCatalog")]
    public sealed class CharacterCustomizationCatalog : ScriptableObject
    {
        [SerializeField] private List<CharacterCustomizationPreset> presets = new List<CharacterCustomizationPreset>();
        [SerializeField] private string defaultPresetId = string.Empty;

        public IReadOnlyList<CharacterCustomizationPreset> Presets => presets;
        public string DefaultPresetId => defaultPresetId;

        public bool TryGetPreset(string presetId, out CharacterCustomizationPreset preset)
        {
            preset = null;
            if (string.IsNullOrWhiteSpace(presetId) || presets == null)
            {
                return false;
            }

            for (int index = 0; index < presets.Count; index++)
            {
                var candidate = presets[index];
                if (candidate != null && string.Equals(candidate.id, presetId, StringComparison.OrdinalIgnoreCase))
                {
                    preset = candidate;
                    return true;
                }
            }

            return false;
        }

        public int IndexOf(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId) || presets == null)
            {
                return -1;
            }

            for (int index = 0; index < presets.Count; index++)
            {
                var candidate = presets[index];
                if (candidate != null && string.Equals(candidate.id, presetId, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        public CharacterCustomizationPreset GetDefaultPreset()
        {
            if (presets == null || presets.Count == 0)
            {
                return null;
            }

            if (TryGetPreset(defaultPresetId, out var explicitDefault))
            {
                return explicitDefault;
            }

            for (int index = 0; index < presets.Count; index++)
            {
                var candidate = presets[index];
                if (candidate != null && candidate.isDefault)
                {
                    return candidate;
                }
            }

            return presets[0];
        }

        public CharacterCustomizationPreset GetPresetOrFirst(string presetId)
        {
            if (TryGetPreset(presetId, out var preset))
            {
                return preset;
            }

            return GetDefaultPreset();
        }

        public CharacterCustomizationPreset GetRandomPreset(int seed = -1)
        {
            if (presets == null || presets.Count == 0)
            {
                return null;
            }

            var random = seed >= 0 ? new System.Random(seed) : new System.Random();
            var attempts = presets.Count;
            while (attempts-- > 0)
            {
                var candidate = presets[random.Next(0, presets.Count)];
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return GetDefaultPreset();
        }

#if UNITY_EDITOR
        public void SetPresets(List<CharacterCustomizationPreset> newPresets, string newDefaultPresetId)
        {
            presets = newPresets != null ? newPresets : new List<CharacterCustomizationPreset>();
            defaultPresetId = newDefaultPresetId ?? string.Empty;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
