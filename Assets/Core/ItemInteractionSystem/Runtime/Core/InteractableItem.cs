using System;
using System.Collections.Generic;
using ModularStoryFlow.Runtime.Events;
using ModularStoryFlow.Runtime.Graph;
using UnityEngine;

namespace ItemInteraction
{
    [DisallowMultipleComponent]
    public class InteractableItem : MonoBehaviour
    {
        private static readonly List<InteractableItem> ActiveInteractablesInternal = new List<InteractableItem>(32);

        [Header("Identity")]
        [Tooltip("Display name shown in the interaction prompt.")]
        public string displayName = "Item";

        [Tooltip("Optional external identifier for your story flow system.")]
        public string storyId = "item.story.id";

        [Header("Prompt")]
        [Tooltip("Optional prompt anchor. Defaults to this transform.")]
        public Transform promptAnchor;

        [Tooltip("Optional per-item focus distance. Set to 0 to use the director default.")]
        [Min(0f)]
        public float maxFocusDistanceOverride;

        [Header("Inspection")]
        [Tooltip("Optional root used when creating the visual-only inspection clone.")]
        public Transform inspectionSourceRoot;

        [Tooltip("Default inspection presentation used when an option opens inspection and does not override it.")]
        public InspectionPresentation defaultInspection = new InspectionPresentation();

        [Header("Look Dialogue")]
        [Tooltip("Speaker name used when the Look option emits story dialogue. Defaults to 'You' when left empty.")]
        public string lookDialogueSpeaker = "You";

        [Tooltip("Optional subtitle/body text emitted when the Look option is used.")]
        [TextArea]
        public string lookDialogueBody = string.Empty;

        [Tooltip("How long the Look dialogue stays visible before auto-closing, in seconds.")]
        [Min(0.1f)]
        public float lookDialogueDisplayDurationSeconds = 2.5f;

        [Header("Visual Feedback")]
        [Tooltip("Optional outline component toggled automatically while this item is focused.")]
        public SelectableOutline outline;

        [Tooltip("Whether the item can currently receive focus and input.")]
        public bool isInteractable = true;

        [Header("Options")]
        public List<InteractionOption> options = new List<InteractionOption>
        {
            new InteractionOption { id = "look", label = "Look", slot = InteractionOptionSlot.Top },
            new InteractionOption { id = "inspect", label = "Inspect", slot = InteractionOptionSlot.Bottom, opensInspection = true }
        };

        public static event Action<InteractableItem, StoryDialogueRequest> AnyStoryDialogueRequested;

        public static IReadOnlyList<InteractableItem> ActiveInteractables => ActiveInteractablesInternal;

        public event Action<InteractableItem, bool> FocusChanged;
        public event Action<InteractionInvocation> OptionTriggered;
        public event Action<StoryDialogueRequest> StoryDialogueRequested;

        public float EffectiveMaxDistance(float directorDefault)
        {
            return maxFocusDistanceOverride > 0f ? maxFocusDistanceOverride : directorDefault;
        }

        public Vector3 GetPromptWorldPosition()
        {
            return promptAnchor != null ? promptAnchor.position : transform.position;
        }

        public Transform GetInspectionSourceRoot()
        {
            return inspectionSourceRoot != null ? inspectionSourceRoot : transform;
        }

        public bool HasAnyVisibleOptions()
        {
            for (int index = 0; index < options.Count; index++)
            {
                if (options[index] != null && options[index].visible)
                {
                    return true;
                }
            }

            return false;
        }

        public void CollectVisibleOptions(List<InteractionOption> results)
        {
            results.Clear();
            for (int index = 0; index < options.Count; index++)
            {
                var option = options[index];
                if (option != null && option.visible)
                {
                    results.Add(option);
                }
            }
        }

        public bool TryGetOption(InteractionOptionSlot slot, out InteractionOption option)
        {
            for (int index = 0; index < options.Count; index++)
            {
                var candidate = options[index];
                if (candidate != null && candidate.visible && candidate.slot == slot)
                {
                    option = candidate;
                    return true;
                }
            }

            option = null;
            return false;
        }

        public InspectionPresentation ResolveInspectionPresentation(InteractionOption option)
        {
            if (option != null && option.useInspectionOverride && option.inspectionOverride != null)
            {
                return option.inspectionOverride;
            }

            return defaultInspection;
        }

        public void SetFocused(bool focused)
        {
            if (outline != null)
            {
                outline.SetVisible(focused);
            }

            FocusChanged?.Invoke(this, focused);
        }

        public void InvokeOption(InteractionDirector director, InteractionOption option)
        {
            if (option == null || !option.enabled)
            {
                return;
            }

            option.onInvoked?.Invoke();
            TryRaiseLookDialogue(option);
            OptionTriggered?.Invoke(new InteractionInvocation(director, this, option));
        }

        private void TryRaiseLookDialogue(InteractionOption option)
        {
            if (option == null || !string.Equals(option.id, "look", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(lookDialogueBody))
            {
                return;
            }

            var request = new StoryDialogueRequest
            {
                RequestId = StoryIds.NewId(),
                SpeakerDisplayName = string.IsNullOrWhiteSpace(lookDialogueSpeaker) ? "You" : lookDialogueSpeaker,
                Body = lookDialogueBody,
                SpeakerId = storyId,
                NodeId = option.id,
                AutoAdvance = true,
                AutoAdvanceDelaySeconds = lookDialogueDisplayDurationSeconds
            };

            StoryDialogueRequested?.Invoke(request);
            AnyStoryDialogueRequested?.Invoke(this, request);
        }

        public bool SetOptionVisible(string optionId, bool value)
        {
            var option = FindOption(optionId);
            if (option == null)
            {
                return false;
            }

            option.visible = value;
            return true;
        }

        public bool SetOptionEnabled(string optionId, bool value)
        {
            var option = FindOption(optionId);
            if (option == null)
            {
                return false;
            }

            option.enabled = value;
            return true;
        }

        public InteractionOption FindOption(string optionId)
        {
            if (string.IsNullOrWhiteSpace(optionId))
            {
                return null;
            }

            for (int index = 0; index < options.Count; index++)
            {
                var option = options[index];
                if (option != null && string.Equals(option.id, optionId, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return null;
        }

        private void Reset()
        {
            promptAnchor = transform;
            inspectionSourceRoot = transform;
            outline = GetComponent<SelectableOutline>();
        }

        private void OnEnable()
        {
            if (!ActiveInteractablesInternal.Contains(this))
            {
                ActiveInteractablesInternal.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveInteractablesInternal.Remove(this);
            if (outline != null)
            {
                outline.SetVisible(false);
            }
        }

        private void OnDestroy()
        {
            ActiveInteractablesInternal.Remove(this);
        }
    }
}
