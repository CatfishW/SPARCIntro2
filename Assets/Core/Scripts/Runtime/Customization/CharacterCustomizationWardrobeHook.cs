using ItemInteraction;
using UnityEngine;
using UnityEngine.Serialization;

namespace Blocks.Gameplay.Core.Customization
{
    [DisallowMultipleComponent]
    public sealed class CharacterCustomizationWardrobeHook : MonoBehaviour
    {
        [SerializeField] private InteractableItem interactableItem;
        [SerializeField] private CharacterCustomizationPanel customizationPanel;
        [SerializeField] private string optionId = "change_character";
        [SerializeField] private string optionLabel = "Change Character";
        [SerializeField] private InteractionOptionSlot slot = InteractionOptionSlot.Top;
        [SerializeField] private bool visible = true;
        [FormerlySerializedAs("enabled")]
        [SerializeField] private bool optionEnabled = true;
        [SerializeField] private bool installOnAwake = true;

        private void Reset()
        {
            interactableItem = GetComponent<InteractableItem>();
        }

        private void Awake()
        {
            if (installOnAwake)
            {
                InstallOption();
            }
        }

        private void OnEnable()
        {
            if (installOnAwake)
            {
                InstallOption();
            }
        }

        private void OnDisable()
        {
            if (interactableItem == null)
            {
                interactableItem = GetComponent<InteractableItem>();
            }

            var option = interactableItem != null ? interactableItem.FindOption(optionId) : null;
            if (option != null)
            {
                option.onInvoked.RemoveListener(HandleInvoked);
            }
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                InstallOption();
            }
        }

        public void InstallOption()
        {
            ResolveDependencies();

            if (interactableItem == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(optionId))
            {
                optionId = "change_character";
            }

            if (string.IsNullOrWhiteSpace(optionLabel))
            {
                optionLabel = "Change Character";
            }

            if (interactableItem.options == null)
            {
                interactableItem.options = new System.Collections.Generic.List<InteractionOption>();
            }

            var option = interactableItem.FindOption(optionId);
            if (option == null)
            {
                option = new InteractionOption
                {
                    id = optionId,
                    label = optionLabel,
                    slot = slot,
                    visible = visible,
                    enabled = optionEnabled
                };
                interactableItem.options.Add(option);
            }
            else
            {
                option.label = optionLabel;
                option.slot = slot;
                option.visible = visible;
                option.enabled = optionEnabled;
            }

            NormalizeLookOptionSlot(option);
            EnsurePrimaryOptionFirst(option);
            option.onInvoked.RemoveListener(HandleInvoked);
            option.onInvoked.AddListener(HandleInvoked);
        }

        private void HandleInvoked()
        {
            ResolveDependencies();

            if (customizationPanel != null)
            {
                customizationPanel.Show();
            }
        }

        private void ResolveDependencies()
        {
            if (interactableItem == null)
            {
                interactableItem = GetComponent<InteractableItem>();
            }

            if (customizationPanel == null)
            {
                customizationPanel = FindFirstObjectByType<CharacterCustomizationPanel>(FindObjectsInactive.Include);
            }
        }

        private void NormalizeLookOptionSlot(InteractionOption primaryOption)
        {
            if (interactableItem == null || interactableItem.options == null || primaryOption == null)
            {
                return;
            }

            var lookOption = interactableItem.FindOption("look");
            if (lookOption == null || lookOption == primaryOption || lookOption.slot != primaryOption.slot)
            {
                return;
            }

            lookOption.slot = primaryOption.slot == InteractionOptionSlot.Top
                ? InteractionOptionSlot.Left
                : InteractionOptionSlot.Top;
        }

        private void EnsurePrimaryOptionFirst(InteractionOption primaryOption)
        {
            if (interactableItem == null || interactableItem.options == null || primaryOption == null)
            {
                return;
            }

            var currentIndex = interactableItem.options.IndexOf(primaryOption);
            if (currentIndex <= 0)
            {
                return;
            }

            interactableItem.options.RemoveAt(currentIndex);
            interactableItem.options.Insert(0, primaryOption);
        }
    }
}
