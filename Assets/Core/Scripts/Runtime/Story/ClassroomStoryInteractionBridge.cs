using System;
using ItemInteraction;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using UnityEngine;
using UnityEngine.Serialization;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomStoryInteractionBridge : MonoBehaviour
    {
        private const string StoryMachineId = "ClassroomStory.Progress";
        private const string DoorOptionId = "leave";
        private const string DoorFallbackName = "EntranceDoor";
        private const string DoorLockedLabel = "Lab Door Locked";
        private const string DoorReadyLabel = "Enter Lab";

        [SerializeField] private StoryFlowChannels channels;
        [SerializeField, FormerlySerializedAs("enteranceDoorInteractable")] private InteractableItem entranceDoorInteractable;
        [SerializeField] private InteractionDirector interactionDirector;
        [SerializeField] private StoryNpcStorySignalBridge npcSignalBridge;
        [SerializeField] private ClassroomStorySceneTransition sceneTransition;
        [SerializeField] private bool doorReady;
        [SerializeField] private bool transitionCommitted;
        [SerializeField] private string currentSessionId = string.Empty;

        public string CurrentSessionId => currentSessionId;

        private void Awake()
        {
            ResolveSceneReferences();
            ApplyDoorState();
        }

        private void OnEnable()
        {
            ResolveSceneReferences();
            RegisterGlobalDialogueRelay();
            RegisterStoryChannels();
            RegisterSceneHooks();
            ApplyDoorState();
        }

        private void OnDisable()
        {
            UnregisterSceneHooks();
            UnregisterStoryChannels();
            UnregisterGlobalDialogueRelay();
        }

        private void LateUpdate()
        {
            ResolveSceneReferences();
            UpdateDoorOutline();
        }

        public void Configure(
            StoryFlowChannels storyChannels,
            InteractableItem laptop,
            InteractableItem entranceDoor,
            ClassroomStorySceneTransition transition)
        {
            if (isActiveAndEnabled)
            {
                UnregisterStoryChannels();
                UnregisterSceneHooks();
            }

            channels = storyChannels;
            if (entranceDoor != null)
            {
                entranceDoorInteractable = entranceDoor;
            }

            if (transition != null)
            {
                sceneTransition = transition;
            }

            ResolveSceneReferences();
            npcSignalBridge?.Configure(channels, currentSessionId);
            ApplyDoorState();

            if (isActiveAndEnabled)
            {
                RegisterStoryChannels();
                RegisterSceneHooks();
            }
        }

        public void SetSessionId(string sessionId)
        {
            currentSessionId = sessionId ?? string.Empty;
            npcSignalBridge?.SetSessionId(currentSessionId);
        }

        public void SetDoorReady(bool value)
        {
            doorReady = value;
            ApplyDoorState();
        }

        public void ConfirmDoorInteraction()
        {
            if (!doorReady || channels == null)
            {
                return;
            }

            channels.ExternalSignals?.Raise(new StoryExternalSignal
            {
                SessionId = currentSessionId,
                SignalId = ClassroomStorySignals.DoorConfirmed,
                Payload = entranceDoorInteractable != null ? entranceDoorInteractable.name : DoorFallbackName
            });
        }

        public void CommitTransition()
        {
            if (transitionCommitted)
            {
                return;
            }

            transitionCommitted = true;
            sceneTransition?.RequestLoad();
        }

        public void ResetProgression()
        {
            doorReady = false;
            transitionCommitted = false;
            currentSessionId = string.Empty;
            sceneTransition?.ResetRequest();
            ApplyDoorState();
        }

        private void RegisterStoryChannels()
        {
            if (channels == null)
            {
                return;
            }

            channels.StateChanged?.Register(HandleStateChanged);
            channels.GraphNotifications?.Register(HandleGraphNotification);
            channels.ExternalSignals?.Register(HandleExternalSignal);
        }

        private void UnregisterStoryChannels()
        {
            if (channels == null)
            {
                return;
            }

            channels.StateChanged?.Unregister(HandleStateChanged);
            channels.GraphNotifications?.Unregister(HandleGraphNotification);
            channels.ExternalSignals?.Unregister(HandleExternalSignal);
        }

        private void RegisterSceneHooks()
        {
            if (entranceDoorInteractable != null)
            {
                entranceDoorInteractable.OptionTriggered -= HandleDoorOptionTriggered;
                entranceDoorInteractable.OptionTriggered += HandleDoorOptionTriggered;
            }
        }

        private void UnregisterSceneHooks()
        {
            if (entranceDoorInteractable != null)
            {
                entranceDoorInteractable.OptionTriggered -= HandleDoorOptionTriggered;
            }
        }

        private void ResolveSceneReferences()
        {
            interactionDirector = interactionDirector != null ? interactionDirector : FindFirstObjectByType<InteractionDirector>();
            npcSignalBridge = npcSignalBridge != null ? npcSignalBridge : FindFirstObjectByType<StoryNpcStorySignalBridge>();
            sceneTransition = sceneTransition != null ? sceneTransition : GetComponent<ClassroomStorySceneTransition>();

            entranceDoorInteractable = EnsureInteractable(
                entranceDoorInteractable,
                "Classroom/EntranceDoor",
                "EntranceDoor",
                "Lab Door");

            if (entranceDoorInteractable == null)
            {
                entranceDoorInteractable = EnsureInteractable(
                    null,
                    "Classroom/EnteranceDoor",
                    "EnteranceDoor",
                    "Lab Door");
            }
        }

        private void ApplyDoorState()
        {
            if (entranceDoorInteractable == null)
            {
                return;
            }

            entranceDoorInteractable.displayName = "Lab Door";
            entranceDoorInteractable.storyId = "classroom.labDoor";
            entranceDoorInteractable.lookDialogueSpeaker = "You";
            entranceDoorInteractable.lookDialogueBody = doorReady
                ? "Lab access granted. The miniaturization chamber is waiting."
                : "The lab door is sealed until Dr. Mira clears your briefing.";
            entranceDoorInteractable.lookDialogueDisplayDurationSeconds = 2.7f;
            entranceDoorInteractable.isInteractable = true;

            if (entranceDoorInteractable.options == null)
            {
                entranceDoorInteractable.options = new System.Collections.Generic.List<InteractionOption>();
            }

            entranceDoorInteractable.options.Clear();
            entranceDoorInteractable.options.Add(new InteractionOption
            {
                id = DoorOptionId,
                label = doorReady ? DoorReadyLabel : DoorLockedLabel,
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = doorReady
            });

            UpdateDoorOutline();
        }

        private void UpdateDoorOutline()
        {
            if (entranceDoorInteractable == null || entranceDoorInteractable.outline == null)
            {
                return;
            }

            var isFocused = interactionDirector != null && interactionDirector.CurrentFocus == entranceDoorInteractable;
            entranceDoorInteractable.outline.SetVisible(doorReady && isFocused);
        }

        private void HandleDoorOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != entranceDoorInteractable)
            {
                return;
            }

            if (!string.Equals(invocation.OptionId, DoorOptionId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ConfirmDoorInteraction();
        }

        private void HandleGraphNotification(StoryGraphNotification notification)
        {
            if (notification == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(notification.SessionId))
            {
                currentSessionId = notification.SessionId;
            }

            if (notification.Kind == StoryGraphNotificationKind.Started || notification.Kind == StoryGraphNotificationKind.Loaded)
            {
                transitionCommitted = false;
                doorReady = false;
                ApplyDoorState();
            }
        }

        private void HandleStateChanged(StoryStateChangedPayload payload)
        {
            if (payload == null || payload.MachineId != StoryMachineId)
            {
                return;
            }

            doorReady = string.Equals(payload.NextStateId, "DoorReady", StringComparison.Ordinal) ||
                        string.Equals(payload.NextStateId, "TransitionCommitted", StringComparison.Ordinal);

            if (string.Equals(payload.NextStateId, "FreshSpawn", StringComparison.Ordinal))
            {
                transitionCommitted = false;
            }

            ApplyDoorState();

            if (string.Equals(payload.NextStateId, "TransitionCommitted", StringComparison.Ordinal))
            {
                CommitTransition();
            }
        }

        private void HandleExternalSignal(StoryExternalSignal signal)
        {
            if (signal == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(signal.SessionId))
            {
                currentSessionId = signal.SessionId;
            }

            if (string.Equals(signal.SignalId, ClassroomStorySignals.LabClearanceEarned, StringComparison.Ordinal))
            {
                doorReady = true;
                ApplyDoorState();
            }
        }

        private void HandleAnyItemDialogueRequested(InteractableItem item, StoryDialogueRequest request)
        {
            if (request == null || channels == null)
            {
                return;
            }

            request.SessionId = string.IsNullOrWhiteSpace(request.SessionId) ? currentSessionId : request.SessionId;
            if (string.IsNullOrWhiteSpace(request.SpeakerDisplayName) && item != null)
            {
                request.SpeakerDisplayName = string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName;
            }

            if (string.IsNullOrWhiteSpace(request.SpeakerId) && !string.IsNullOrWhiteSpace(request.SpeakerDisplayName))
            {
                request.SpeakerId = request.SpeakerDisplayName;
            }

            channels.DialogueRequests?.Raise(request);
        }

        private void RegisterGlobalDialogueRelay()
        {
            InteractableItem.AnyStoryDialogueRequested -= HandleAnyItemDialogueRequested;
            InteractableItem.AnyStoryDialogueRequested += HandleAnyItemDialogueRequested;
        }

        private void UnregisterGlobalDialogueRelay()
        {
            InteractableItem.AnyStoryDialogueRequested -= HandleAnyItemDialogueRequested;
        }

        private static InteractableItem EnsureInteractable(
            InteractableItem existing,
            string hierarchyPath,
            string fallbackName,
            string displayName)
        {
            if (existing != null)
            {
                return PrepareInteractable(existing, displayName);
            }

            var gameObject = GameObject.Find(hierarchyPath) ?? GameObject.Find(fallbackName);
            if (gameObject == null)
            {
                return null;
            }

            var interactable = gameObject.GetComponent<InteractableItem>();
            if (interactable == null)
            {
                interactable = gameObject.AddComponent<InteractableItem>();
            }

            return PrepareInteractable(interactable, displayName);
        }

        private static InteractableItem PrepareInteractable(InteractableItem interactable, string displayName)
        {
            if (interactable == null)
            {
                return null;
            }

            interactable.displayName = displayName;
            interactable.promptAnchor = EnsurePromptAnchor(interactable);
            interactable.inspectionSourceRoot = interactable.inspectionSourceRoot != null
                ? interactable.inspectionSourceRoot
                : interactable.transform;
            interactable.outline = interactable.outline != null
                ? interactable.outline
                : interactable.GetComponent<SelectableOutline>() ?? interactable.gameObject.AddComponent<SelectableOutline>();
            interactable.isInteractable = true;
            return interactable;
        }

        private static Transform EnsurePromptAnchor(InteractableItem interactable)
        {
            if (interactable == null || interactable.transform == null)
            {
                return null;
            }

            var root = interactable.transform;
            var anchor = root.Find("PromptAnchor");
            if (anchor == null)
            {
                var anchorObject = new GameObject("PromptAnchor");
                anchorObject.hideFlags = HideFlags.HideInHierarchy;
                anchor = anchorObject.transform;
                anchor.SetParent(root, false);
            }

            if (TryGetCombinedBounds(interactable.gameObject, out var bounds))
            {
                var objectHeight = Mathf.Max(bounds.size.y, 0.01f);
                var normalizedHeight = objectHeight >= 1.8f
                    ? 0.14f
                    : objectHeight >= 0.8f
                        ? 0.32f
                        : 0.62f;
                var anchorY = Mathf.Lerp(bounds.min.y, bounds.max.y, normalizedHeight);
                anchor.position = new Vector3(bounds.center.x, anchorY, bounds.center.z);
            }
            else
            {
                anchor.localPosition = new Vector3(0f, 0.2f, 0f);
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
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (hasBounds)
            {
                return true;
            }

            var colliders = target.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (collider == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }
    }
}
