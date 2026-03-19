using System;
using System.Collections.Generic;
using System.IO;
using Blocks.Gameplay.Core.Customization;
using UnityEditor;
using UnityEngine;

namespace Blocks.Gameplay.Core.Customization.Editor
{
    public static class CharacterCustomizationCatalogGenerator
    {
        private const string CharacterPrefabFolder = "Assets/Core/Art/3D Casual Character/3D Characters Pro - Casual/Prefabs/Characters";
        private const string ResourcesFolder = "Assets/Core/Resources";
        private const string CatalogAssetPath = ResourcesFolder + "/CharacterCustomizationCatalog.asset";
        private const string PreferredDefaultPrefabName = "Characters_5";

        [MenuItem("Tools/Blocks/Character Customization/Regenerate Catalog", priority = 340)]
        public static void RegenerateCatalogMenu()
        {
            EnsureCatalogAsset(forceRegenerate: true);
        }

        public static CharacterCustomizationCatalog EnsureCatalogAsset(bool forceRegenerate = false)
        {
            EnsureFolder("Assets/Core");
            EnsureFolder(ResourcesFolder);

            var catalog = AssetDatabase.LoadAssetAtPath<CharacterCustomizationCatalog>(CatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CharacterCustomizationCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
            }

            if (!forceRegenerate && catalog.Presets != null && catalog.Presets.Count > 0)
            {
                return catalog;
            }

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { CharacterPrefabFolder });
            var prefabPaths = new List<string>(prefabGuids.Length);
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrWhiteSpace(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    prefabPaths.Add(path);
                }
            }

            prefabPaths.Sort(StringComparer.OrdinalIgnoreCase);

            var presets = new List<CharacterCustomizationPreset>(prefabPaths.Count);
            string defaultPresetId = string.Empty;

            for (var index = 0; index < prefabPaths.Count; index++)
            {
                var path = prefabPaths[index];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                var presetId = BuildPresetId(prefab.name, index);
                var preset = new CharacterCustomizationPreset();
                var displayName = BuildDisplayName(prefab.name);
                var isDefault = string.Equals(prefab.name, PreferredDefaultPrefabName, StringComparison.OrdinalIgnoreCase);
                preset.SetEditorData(presetId, displayName, prefab, isDefault, path);
                presets.Add(preset);

                if (isDefault)
                {
                    defaultPresetId = presetId;
                }
            }

            if (string.IsNullOrWhiteSpace(defaultPresetId) && presets.Count > 0)
            {
                defaultPresetId = presets[0].id;
            }

            catalog.SetPresets(presets, defaultPresetId);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(CatalogAssetPath, ImportAssetOptions.ForceUpdate);

            return catalog;
        }

        private static string BuildPresetId(string prefabName, int index)
        {
            var raw = string.IsNullOrWhiteSpace(prefabName) ? $"preset_{index}" : prefabName.Trim();
            var chars = raw.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                {
                    chars[i] = '_';
                }
                else
                {
                    chars[i] = char.ToLowerInvariant(chars[i]);
                }
            }

            return $"casual_{new string(chars)}";
        }

        private static string BuildDisplayName(string prefabName)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                return "Character";
            }

            var normalized = prefabName.Replace('_', ' ').Trim();
            return string.IsNullOrWhiteSpace(normalized) ? prefabName : normalized;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            if (string.IsNullOrWhiteSpace(parent))
            {
                return;
            }

            var folderName = Path.GetFileName(assetPath);
            if (!string.IsNullOrWhiteSpace(folderName) && !AssetDatabase.IsValidFolder(assetPath))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }
    }
}
