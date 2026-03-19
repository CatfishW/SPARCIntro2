using System;
using System.Collections.Generic;
using ItemInteraction;
using UnityEngine;

namespace Blocks.Gameplay.Core.UI.LaptopOS
{
    [DisallowMultipleComponent]
    public sealed class LaptopDesktopOpenRelay : MonoBehaviour
    {
        [SerializeField] private InteractableItem sourceItem;
        [SerializeField] private LaptopDesktopSystem laptopSystem;
        [SerializeField] private List<string> openOptionIds = new List<string> { "look", "open", "inspect" };

        private void Awake()
        {
            if (sourceItem == null)
            {
                sourceItem = GetComponent<InteractableItem>();
            }

            if (laptopSystem == null)
            {
                laptopSystem = FindFirstObjectByType<LaptopDesktopSystem>();
            }
        }

        private void OnEnable()
        {
            if (sourceItem == null)
            {
                sourceItem = GetComponent<InteractableItem>();
            }

            if (sourceItem != null)
            {
                sourceItem.OptionTriggered += HandleOptionTriggered;
            }
        }

        private void OnDisable()
        {
            if (sourceItem != null)
            {
                sourceItem.OptionTriggered -= HandleOptionTriggered;
            }
        }

        private void HandleOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != sourceItem)
            {
                return;
            }

            if (!IsOpenOption(invocation.OptionId))
            {
                return;
            }

            if (laptopSystem == null)
            {
                laptopSystem = FindFirstObjectByType<LaptopDesktopSystem>();
            }

            laptopSystem?.Open();
        }

        private bool IsOpenOption(string optionId)
        {
            if (string.IsNullOrWhiteSpace(optionId))
            {
                return false;
            }

            for (var index = 0; index < openOptionIds.Count; index++)
            {
                if (string.Equals(openOptionIds[index], optionId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
