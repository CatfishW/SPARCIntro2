using Blocks.Gameplay.Core;
using UnityEngine;

namespace Blocks.Gameplay.Core.Customization
{
    public static class CharacterCustomizationBootstrap
    {
        public static CharacterCustomizationAddon EnsureAttached(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            var addon = target.GetComponent<CharacterCustomizationAddon>();
            if (addon == null)
            {
                addon = target.AddComponent<CharacterCustomizationAddon>();
            }

            return addon;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AttachToExistingPlayers()
        {
            var states = Object.FindObjectsByType<CorePlayerState>(FindObjectsSortMode.None);
            for (int index = 0; index < states.Length; index++)
            {
                if (states[index] != null)
                {
                    EnsureAttached(states[index].gameObject);
                }
            }
        }
    }
}
