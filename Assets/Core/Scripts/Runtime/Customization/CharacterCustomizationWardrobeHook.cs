using ItemInteraction;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Customization
{
    [DefaultExecutionOrder(120)]
    [DisallowMultipleComponent]
    public sealed class CharacterCustomizationWardrobeHook : MonoBehaviour
    {
        private const string RuntimePanelObjectName = "CharacterCustomizationPanelRuntime";

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

            if (interactableItem != null)
            {
                interactableItem.OptionTriggered -= HandleOptionTriggered;
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
            interactableItem.OptionTriggered -= HandleOptionTriggered;
            interactableItem.OptionTriggered += HandleOptionTriggered;
        }

        private void HandleInvoked()
        {
            ResolveDependencies();

            if (customizationPanel == null)
            {
                Debug.LogWarning("[CharacterCustomizationWardrobeHook] Change Character was triggered but no panel could be resolved or created.", this);
                return;
            }

            if (!customizationPanel.gameObject.activeSelf)
            {
                customizationPanel.gameObject.SetActive(true);
            }

            customizationPanel.Show();
        }

        private void HandleOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != interactableItem)
            {
                return;
            }

            if (!string.Equals(invocation.OptionId, optionId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            HandleInvoked();
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
                if (customizationPanel == null)
                {
                    customizationPanel = CreateRuntimePanel();
                }
            }
        }

        private CharacterCustomizationPanel CreateRuntimePanel()
        {
            var panelObject = GameObject.Find(RuntimePanelObjectName);
            if (panelObject == null)
            {
                panelObject = new GameObject(RuntimePanelObjectName);
            }

            if (panelObject.GetComponent<UIDocument>() == null)
            {
                panelObject.AddComponent<UIDocument>();
            }

            var panel = panelObject.GetComponent<CharacterCustomizationPanel>();
            if (panel == null)
            {
                panel = panelObject.AddComponent<CharacterCustomizationPanel>();
            }

            if (!panelObject.activeSelf)
            {
                panelObject.SetActive(true);
            }

            return panel;
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
