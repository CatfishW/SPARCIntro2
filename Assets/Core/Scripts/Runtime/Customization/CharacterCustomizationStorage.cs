using UnityEngine;

namespace Blocks.Gameplay.Core.Customization
{
    public static class CharacterCustomizationStorage
    {
        private static string SelectedPresetKey => CharacterCustomizationDefaults.PlayerPrefsKey;

        public static string LoadSelectedPresetId()
        {
            return PlayerPrefs.GetString(SelectedPresetKey, string.Empty);
        }

        public static void SaveSelectedPresetId(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                PlayerPrefs.DeleteKey(SelectedPresetKey);
            }
            else
            {
                PlayerPrefs.SetString(SelectedPresetKey, presetId);
            }

            PlayerPrefs.Save();
        }
    }
}
