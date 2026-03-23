using System;
using System.Collections.Generic;
using ItemInteraction;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractableItem))]
    public sealed class StoryNpcAgent : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string npcId = "npc.unknown";
        [SerializeField] private string npcDisplayName = "NPC";
        [SerializeField] private string interactionNamespace = "npc";

        [Header("Interaction")]
        [SerializeField] private InteractableItem interactable;
        [SerializeField] private SelectableOutline outline;
        [SerializeField] private Transform promptAnchor;
        [SerializeField] private Transform inspectionSourceRoot;
        [SerializeField] private bool addLookOption = true;
        [SerializeField] private bool addInspectOption;
        [SerializeField] private bool isInteractable = true;
        [SerializeField] private List<StoryNpcOptionDefinition> storyOptions = new List<StoryNpcOptionDefinition>();

        [Header("Look Dialogue")]
        [SerializeField] private string lookDialogueSpeaker = string.Empty;
        [SerializeField, TextArea] private string lookDialogueBody = string.Empty;
        [SerializeField, Min(0.1f)] private float lookDialogueDisplayDurationSeconds = 2.5f;

        [Header("Presentation")]
        [SerializeField] private bool faceCameraWhenFocused = true;
        [SerializeField] private Transform visualRoot;
        [SerializeField, Min(0f)] private float turnSpeedDegreesPerSecond = 360f;

        private bool isFocused;

        public static event Action<StoryNpcInteractionPayload> AnyInteractionTriggered;

        public event Action<StoryNpcInteractionPayload> InteractionTriggered;

        public string NpcId => npcId;
        public string NpcDisplayName => npcDisplayName;
        public InteractableItem Interactable => interactable;

        private void Reset()
        {
            CacheReferences();
            EnsureDefaultOptions();
            ApplyNpcPresentation();
        }

        private void OnValidate()
        {
            CacheReferences();
            EnsureDefaultOptions();
            ApplyNpcPresentation();
        }

        private void Awake()
        {
            CacheReferences();
            EnsureDefaultOptions();
            ApplyNpcPresentation();
        }

        private void OnEnable()
        {
            CacheReferences();
            if (interactable == null)
            {
                return;
            }

            interactable.OptionTriggered -= HandleOptionTriggered;
            interactable.OptionTriggered += HandleOptionTriggered;
            interactable.FocusChanged -= HandleFocusChanged;
            interactable.FocusChanged += HandleFocusChanged;
        }

        private void OnDisable()
        {
            if (interactable == null)
            {
                return;
            }

            interactable.OptionTriggered -= HandleOptionTriggered;
            interactable.FocusChanged -= HandleFocusChanged;
            isFocused = false;
        }

        private void Update()
        {
            if (!faceCameraWhenFocused || !isFocused)
            {
                return;
            }

            var target = ResolveFacingTarget();
            if (target == null)
            {
                return;
            }

            var root = visualRoot != null ? visualRoot : transform;
            var toTarget = target.position - root.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            root.rotation = Quaternion.RotateTowards(
                root.rotation,
                targetRotation,
                turnSpeedDegreesPerSecond * Time.deltaTime);
        }

        public void ApplyNpcPresentation()
        {
            CacheReferences();
            if (interactable == null)
            {
                return;
            }

            interactable.displayName = string.IsNullOrWhiteSpace(npcDisplayName) ? gameObject.name : npcDisplayName;
            interactable.storyId = string.IsNullOrWhiteSpace(npcId) ? gameObject.name : npcId;
            interactable.promptAnchor = EnsurePromptAnchor(interactable);
            promptAnchor = interactable.promptAnchor;
            interactable.inspectionSourceRoot = inspectionSourceRoot != null ? inspectionSourceRoot : interactable.inspectionSourceRoot != null ? interactable.inspectionSourceRoot : transform;
            interactable.outline = outline != null ? outline : interactable.outline;
            interactable.lookDialogueSpeaker = string.IsNullOrWhiteSpace(lookDialogueSpeaker) ? interactable.displayName : lookDialogueSpeaker;
            interactable.lookDialogueBody = lookDialogueBody ?? string.Empty;
            interactable.lookDialogueDisplayDurationSeconds = Mathf.Max(0.1f, lookDialogueDisplayDurationSeconds);
            interactable.isInteractable = isInteractable;
            RebuildInteractionOptions();
        }

        public void SetInteractable(bool value)
        {
            isInteractable = value;
            if (interactable != null)
            {
                interactable.isInteractable = value;
            }
        }

        public bool SetOptionVisible(string optionId, bool value)
        {
            return interactable != null && interactable.SetOptionVisible(optionId, value);
        }

        public bool SetOptionEnabled(string optionId, bool value)
        {
            return interactable != null && interactable.SetOptionEnabled(optionId, value);
        }

        public bool SetOptionLabel(string optionId, string label)
        {
            if (interactable == null)
            {
                return false;
            }

            var option = interactable.FindOption(optionId);
            if (option == null)
            {
                return false;
            }

            option.label = label ?? string.Empty;
            return true;
        }

        public void SetLookDialogue(string speaker, string body, float displayDurationSeconds = 2.5f)
        {
            lookDialogueSpeaker = speaker ?? string.Empty;
            lookDialogueBody = body ?? string.Empty;
            lookDialogueDisplayDurationSeconds = Mathf.Max(0.1f, displayDurationSeconds);
            ApplyNpcPresentation();
        }

        public void ConfigureNpc(
            string id,
            string displayName,
            IEnumerable<StoryNpcOptionDefinition> options,
            string speaker,
            string body,
            float displayDurationSeconds = 2.5f,
            string optionNamespace = null,
            bool includeLookOption = true,
            bool includeInspectOption = false)
        {
            npcId = string.IsNullOrWhiteSpace(id) ? npcId : id;
            npcDisplayName = string.IsNullOrWhiteSpace(displayName) ? npcDisplayName : displayName;

            if (!string.IsNullOrWhiteSpace(optionNamespace))
            {
                interactionNamespace = optionNamespace;
            }

            addLookOption = includeLookOption;
            addInspectOption = includeInspectOption;

            storyOptions.Clear();
            if (options != null)
            {
                foreach (var option in options)
                {
                    if (option == null || string.IsNullOrWhiteSpace(option.id))
                    {
                        continue;
                    }

                    storyOptions.Add(new StoryNpcOptionDefinition
                    {
                        id = option.id,
                        label = option.label,
                        slot = option.slot,
                        hintOverride = option.hintOverride,
                        visible = option.visible,
                        enabled = option.enabled,
                        interactionId = option.interactionId,
                        opensInspection = option.opensInspection,
                        useInspectionOverride = option.useInspectionOverride,
                        inspectionOverride = option.inspectionOverride
                    });
                }
            }

            lookDialogueSpeaker = speaker ?? string.Empty;
            lookDialogueBody = body ?? string.Empty;
            lookDialogueDisplayDurationSeconds = Mathf.Max(0.1f, displayDurationSeconds);

            EnsureDefaultOptions();
            ApplyNpcPresentation();
        }

        private void CacheReferences()
        {
            interactable = interactable != null ? interactable : GetComponent<InteractableItem>();
            outline = outline != null ? outline : GetComponent<SelectableOutline>();
            promptAnchor = promptAnchor != null ? promptAnchor : transform.Find("PromptAnchor");
            inspectionSourceRoot = inspectionSourceRoot != null ? inspectionSourceRoot : transform;
            visualRoot = visualRoot != null ? visualRoot : transform;
        }

        private void EnsureDefaultOptions()
        {
            if (storyOptions.Count > 0)
            {
                return;
            }

            storyOptions.Add(new StoryNpcOptionDefinition
            {
                id = "talk",
                label = "Talk",
                slot = InteractionOptionSlot.Top,
                interactionId = string.Empty,
                visible = true,
                enabled = true
            });
        }

        private void RebuildInteractionOptions()
        {
            if (interactable == null)
            {
                return;
            }

            if (interactable.options == null)
            {
                interactable.options = new List<InteractionOption>();
            }

            interactable.options.Clear();

            if (addLookOption)
            {
                interactable.options.Add(new InteractionOption
                {
                    id = "look",
                    label = "Observe",
                    slot = InteractionOptionSlot.Bottom,
                    visible = !string.IsNullOrWhiteSpace(lookDialogueBody),
                    enabled = !string.IsNullOrWhiteSpace(lookDialogueBody)
                });
            }

            if (addInspectOption)
            {
                interactable.options.Add(new InteractionOption
                {
                    id = "inspect",
                    label = "Inspect",
                    slot = InteractionOptionSlot.Right,
                    visible = true,
                    enabled = true,
                    opensInspection = true
                });
            }

            for (var index = 0; index < storyOptions.Count; index++)
            {
                var definition = storyOptions[index];
                if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                {
                    continue;
                }

                interactable.options.Add(new InteractionOption
                {
                    id = definition.id,
                    label = string.IsNullOrWhiteSpace(definition.label) ? definition.id : definition.label,
                    slot = definition.slot,
                    hintOverride = definition.hintOverride,
                    visible = definition.visible,
                    enabled = definition.enabled,
                    opensInspection = definition.opensInspection,
                    useInspectionOverride = definition.useInspectionOverride,
                    inspectionOverride = definition.inspectionOverride
                });
            }
        }

        private void HandleFocusChanged(InteractableItem _, bool focused)
        {
            isFocused = focused;
        }

        private void HandleOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != interactable)
            {
                return;
            }

            var payload = new StoryNpcInteractionPayload(this, invocation, BuildInteractionId(invocation.OptionId));
            InteractionTriggered?.Invoke(payload);
            AnyInteractionTriggered?.Invoke(payload);
        }

        private string BuildInteractionId(string optionId)
        {
            if (string.IsNullOrWhiteSpace(optionId))
            {
                return string.Empty;
            }

            for (var index = 0; index < storyOptions.Count; index++)
            {
                var definition = storyOptions[index];
                if (definition == null || !string.Equals(definition.id, optionId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(definition.interactionId))
                {
                    return definition.interactionId;
                }

                break;
            }

            if (string.IsNullOrWhiteSpace(interactionNamespace))
            {
                return string.Concat(npcId, ".", optionId);
            }

            return string.Concat(interactionNamespace, ".", npcId, ".", optionId);
        }

        private static Transform EnsurePromptAnchor(InteractableItem target)
        {
            if (target == null || target.transform == null)
            {
                return null;
            }

            var root = target.transform;
            var anchor = root.Find("PromptAnchor");
            if (anchor == null)
            {
                var anchorObject = new GameObject("PromptAnchor");
                anchorObject.hideFlags = HideFlags.HideInHierarchy;
                anchor = anchorObject.transform;
                anchor.SetParent(root, false);
            }

            if (TryGetCombinedBounds(target.gameObject, out var bounds))
            {
                var anchorY = Mathf.Lerp(bounds.min.y, bounds.max.y, 0.58f);
                anchor.position = new Vector3(bounds.center.x, anchorY, bounds.center.z);
            }
            else
            {
                anchor.localPosition = new Vector3(0f, 1.1f, 0f);
            }

            return anchor;
        }

        private static bool TryGetCombinedBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
            {
                return false;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            for (var index = 0; index < renderers.Length; index++)
            {
                var current = renderers[index];
                if (current == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = current.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(current.bounds);
                }
            }

            return hasBounds;
        }

        private static Transform ResolveFacingTarget()
        {
            var mainCamera = Camera.main;
            return mainCamera != null ? mainCamera.transform : null;
        }
    }
}
