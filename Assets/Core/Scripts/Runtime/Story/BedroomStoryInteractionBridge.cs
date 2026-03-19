using System;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Blocks.Gameplay.Core.UI.LaptopOS;
using ItemInteraction;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using UnityEngine;
using UnityEngine.Serialization;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class BedroomStoryInteractionBridge : MonoBehaviour
    {
        private const string DefaultDoorLabel = "Leave";
        private const string ReadyDoorLabel = "Leave (Go to Class)";
        private const string LaptopPromptLabel = "Check Laptop";
        private const string LaptopPromptOptionId = "open";
        private const string DefaultInnerSpeaker = "You";
        private const string LaptopFallbackName = "MacBook";
        private const string DoorFallbackName = "EnteranceDoor";
        private const string CoffeeMakerFallbackName = "Coffee+Maker";
        private const string OvenFallbackName = "Oven.001";
        private const string WaitSignalNodeTypeName = "WaitSignalNodeAsset";

        [SerializeField] private StoryFlowChannels channels;
        [SerializeField, FormerlySerializedAs("tabletInteractable")] private InteractableItem laptopInteractable;
        [SerializeField] private InteractableItem enteranceDoorInteractable;
        [SerializeField] private InteractableItem bedInteractable;
        [SerializeField] private InteractableItem deskInteractable;
        [SerializeField] private InteractableItem mirrorInteractable;
        [SerializeField] private InteractableItem wardrobeInteractable;
        [SerializeField] private InteractableItem coffeeMakerInteractable;
        [SerializeField] private InteractableItem ovenInteractable;
        [SerializeField] private InteractionDirector interactionDirector;
        [SerializeField] private CorePlayerManager localPlayerManager;
        [SerializeField] private LaptopDesktopSystem laptopDesktopSystem;
        [SerializeField] private BedroomStorySceneTransition sceneTransition;
        [SerializeField] private bool laptopResolved;
        [SerializeField] private bool laptopObjectiveActive;
        [SerializeField] private bool laptopSignalSent;
        [SerializeField, Min(0.05f)] private float laptopSignalRetryIntervalSeconds = 0.35f;
        [SerializeField] private float nextLaptopSignalTime;
        [SerializeField] private bool laptopWaitSignalNodeEntered;
        [SerializeField] private bool doorReady;
        [SerializeField] private bool transitionCommitted;
        [SerializeField] private string currentSessionId = string.Empty;

        private CursorLockMode previousCursorLockState;
        private bool previousCursorVisible;
        private bool cachedCursorState;
        private bool lockedMovementForLaptop;
        private bool laptopSessionOpen;

        public string CurrentSessionId => currentSessionId;

        private void Awake()
        {
            ResolveSceneReferences();
            ApplyScenePresentation();
        }

        private void OnEnable()
        {
            ResolveSceneReferences();
            RegisterGlobalDialogueRelay();
            RegisterStoryChannels();
            RegisterSceneHooks();
            ApplyScenePresentation();
            SyncLaptopResolutionFromUi();
            ApplyLaptopControlState();
            TryRaisePendingLaptopSignal();
        }

        private void OnDisable()
        {
            UnregisterSceneHooks();
            UnregisterStoryChannels();
            UnregisterGlobalDialogueRelay();
            ReleaseLaptopControl();
        }

        private void LateUpdate()
        {
            ResolveRuntimeSceneReferences();
            ApplyLaptopObjectiveVisuals();
            TryRaisePendingLaptopSignal();
        }

        public void Configure(
            StoryFlowChannels storyChannels,
            InteractableItem laptop,
            InteractableItem enteranceDoor,
            BedroomStorySceneTransition transition)
        {
            if (isActiveAndEnabled)
            {
                UnregisterStoryChannels();
                UnregisterSceneHooks();
            }

            channels = storyChannels;

            if (laptop != null)
            {
                laptopInteractable = laptop;
            }

            if (enteranceDoor != null)
            {
                enteranceDoorInteractable = enteranceDoor;
            }

            if (transition != null)
            {
                sceneTransition = transition;
            }

            ResolveSceneReferences();
            ApplyScenePresentation();
            SyncLaptopResolutionFromUi();

            if (isActiveAndEnabled)
            {
                RegisterStoryChannels();
                RegisterSceneHooks();
                ApplyLaptopControlState();
                TryRaisePendingLaptopSignal();
            }
        }

        public void SetSessionId(string sessionId)
        {
            currentSessionId = sessionId ?? string.Empty;
            TryRaisePendingLaptopSignal();
        }

        public void MarkLaptopResolved()
        {
            laptopResolved = true;
            ApplyLaptopObjectiveVisuals();
            TryRaisePendingLaptopSignal();
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
                SignalId = BedroomStorySignals.DoorConfirmed,
                Payload = enteranceDoorInteractable != null ? enteranceDoorInteractable.name : DoorFallbackName
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
            laptopResolved = false;
            laptopObjectiveActive = false;
            laptopSignalSent = false;
            nextLaptopSignalTime = 0f;
            laptopWaitSignalNodeEntered = false;
            doorReady = false;
            transitionCommitted = false;
            currentSessionId = string.Empty;
            sceneTransition?.ResetRequest();
            ApplyScenePresentation();
        }

        private void RegisterStoryChannels()
        {
            if (channels == null)
            {
                return;
            }

            channels.StateChanged?.Register(HandleStateChanged);
            channels.GraphNotifications?.Register(HandleGraphNotification);
            channels.NodeNotifications?.Register(HandleNodeNotification);
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
            channels.NodeNotifications?.Unregister(HandleNodeNotification);
            channels.ExternalSignals?.Unregister(HandleExternalSignal);
        }

        private void RegisterSceneHooks()
        {
            if (enteranceDoorInteractable != null)
            {
                enteranceDoorInteractable.OptionTriggered -= HandleDoorOptionTriggered;
                enteranceDoorInteractable.OptionTriggered += HandleDoorOptionTriggered;
            }

            if (laptopDesktopSystem != null)
            {
                laptopDesktopSystem.Opened -= HandleLaptopOpened;
                laptopDesktopSystem.Closed -= HandleLaptopClosed;
                laptopDesktopSystem.ReminderViewed -= HandleLaptopReminderViewed;

                laptopDesktopSystem.Opened += HandleLaptopOpened;
                laptopDesktopSystem.Closed += HandleLaptopClosed;
                laptopDesktopSystem.ReminderViewed += HandleLaptopReminderViewed;
            }
        }

        private void UnregisterSceneHooks()
        {
            if (enteranceDoorInteractable != null)
            {
                enteranceDoorInteractable.OptionTriggered -= HandleDoorOptionTriggered;
            }

            if (laptopDesktopSystem != null)
            {
                laptopDesktopSystem.Opened -= HandleLaptopOpened;
                laptopDesktopSystem.Closed -= HandleLaptopClosed;
                laptopDesktopSystem.ReminderViewed -= HandleLaptopReminderViewed;
            }
        }

        private void ResolveSceneReferences()
        {
            interactionDirector = interactionDirector != null ? interactionDirector : FindFirstObjectByType<InteractionDirector>();
            laptopDesktopSystem = laptopDesktopSystem != null ? laptopDesktopSystem : FindFirstObjectByType<LaptopDesktopSystem>();
            sceneTransition = sceneTransition != null ? sceneTransition : GetComponent<BedroomStorySceneTransition>();
            ResolveRuntimeSceneReferences();

            laptopInteractable = EnsureInteractable(
                laptopInteractable,
                "_Environment/Room/MacBook",
                "MacBook",
                "Laptop");
            enteranceDoorInteractable = EnsureInteractable(
                enteranceDoorInteractable,
                "_Environment/Room/EnteranceDoor",
                "EnteranceDoor",
                "Entrance Door");
            bedInteractable = EnsureInteractable(
                bedInteractable,
                "_Environment/Room/Bed",
                "Bed",
                "Bed");
            deskInteractable = EnsureInteractable(
                deskInteractable,
                "_Environment/Room/Office+Desk",
                "Office+Desk",
                "Desk");
            mirrorInteractable = EnsureInteractable(
                mirrorInteractable,
                "_Environment/Room/BathroomWashing.001",
                "BathroomWashing.001",
                "Mirror");

            wardrobeInteractable = EnsureInteractable(
                wardrobeInteractable,
                "_Environment/Room/BedroomWardrobes",
                "BedroomWardrobes",
                "Wardrobe");
            coffeeMakerInteractable = EnsureInteractable(
                coffeeMakerInteractable,
                "_Environment/Room/Coffee+Maker",
                CoffeeMakerFallbackName,
                "Coffee Maker");
            ovenInteractable = EnsureInteractable(
                ovenInteractable,
                "_Environment/Room/Oven.001",
                OvenFallbackName,
                "Oven");

            if (mirrorInteractable == null)
            {
                mirrorInteractable = EnsureInteractable(
                    null,
                    "_Environment/Room/BathroomWashing",
                    "BathroomWashing",
                    "Mirror");
            }

            DisableDistractorInteractable("_Environment/Room/iMac", "iMac");
            DisableDistractorInteractable("_Environment/Room/Tablet", "Tablet");
        }

        private void ResolveRuntimeSceneReferences()
        {
            interactionDirector = interactionDirector != null ? interactionDirector : FindFirstObjectByType<InteractionDirector>();
            laptopDesktopSystem = laptopDesktopSystem != null ? laptopDesktopSystem : FindFirstObjectByType<LaptopDesktopSystem>();
            sceneTransition = sceneTransition != null ? sceneTransition : GetComponent<BedroomStorySceneTransition>();

            if (localPlayerManager != null)
            {
                return;
            }

            var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                localPlayerManager = taggedPlayer.GetComponent<CorePlayerManager>();
            }

            if (localPlayerManager != null)
            {
                return;
            }

            var players = FindObjectsByType<CorePlayerManager>(FindObjectsSortMode.None);
            for (var index = 0; index < players.Length; index++)
            {
                if (players[index] == null)
                {
                    continue;
                }

                if (players[index].IsOwner)
                {
                    localPlayerManager = players[index];
                    return;
                }

                if (localPlayerManager == null)
                {
                    localPlayerManager = players[index];
                }
            }
        }

        private void ApplyScenePresentation()
        {
            ConfigureLaptopInteractable();
            ConfigureLookDialogueInteractable(mirrorInteractable, "Mirror", "I look half asleep.");
            ConfigureLookDialogueInteractable(deskInteractable, "Desk", "I should probably be more prepared for class.");
            ConfigureLookDialogueInteractable(bedInteractable, "Bed", "Tempting... but no.");
            ConfigureLookInspectDialogueInteractable(
                coffeeMakerInteractable,
                "Coffee Maker",
                "Cold coffee clings to the plate. I meant to clean it before class.");
            ConfigureLookInspectDialogueInteractable(
                ovenInteractable,
                "Oven",
                "It should probably stay off unless I actually want to bake something.");
            ConfigureWardrobeDecorations();
            DisableNonStoryInteractables();
            ApplyDoorState();
            ApplyLaptopObjectiveVisuals();
        }

        private void SyncLaptopResolutionFromUi()
        {
            if (laptopDesktopSystem != null && laptopDesktopSystem.HasViewedClassReminder)
            {
                laptopResolved = true;
            }
        }

        private void ConfigureLaptopInteractable()
        {
            if (laptopInteractable == null)
            {
                return;
            }

            laptopInteractable.displayName = "Laptop";
            laptopInteractable.storyId = "room.laptop";
            laptopInteractable.lookDialogueSpeaker = DefaultInnerSpeaker;
            laptopInteractable.lookDialogueBody = string.Empty;
            laptopInteractable.isInteractable = true;

            laptopInteractable.options.Clear();
            laptopInteractable.options.Add(new InteractionOption
            {
                id = LaptopPromptOptionId,
                label = LaptopPromptLabel,
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
        }

        private static void ConfigureLookDialogueInteractable(InteractableItem interactable, string displayName, string line)
        {
            if (interactable == null)
            {
                return;
            }

            interactable.displayName = displayName;
            interactable.storyId = $"room.{ToStoryIdSegment(displayName)}";
            interactable.lookDialogueSpeaker = DefaultInnerSpeaker;
            interactable.lookDialogueBody = line;
            interactable.lookDialogueDisplayDurationSeconds = 2.25f;
            interactable.isInteractable = true;

            interactable.options.Clear();
            interactable.options.Add(new InteractionOption
            {
                id = "look",
                label = "Look",
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
        }

        private void ConfigureWardrobeDecorations()
        {
            if (wardrobeInteractable == null || wardrobeInteractable.transform == null)
            {
                return;
            }

            var wardrobeRoot = wardrobeInteractable.transform;
            var transforms = wardrobeRoot.GetComponentsInChildren<Transform>(true);
            var decorationIndex = 0;

            for (var index = 0; index < transforms.Length; index++)
            {
                var candidate = transforms[index];
                if (candidate == null || candidate == wardrobeRoot || !IsWardrobeDecorationName(candidate.name))
                {
                    continue;
                }

                var displayName = GetWardrobeDecorationDisplayName(candidate.name);
                var interactable = EnsureInteractable(candidate.gameObject, displayName);
                ConfigureLookDialogueInteractable(
                    interactable,
                    displayName,
                    GetWardrobeDecorationLine(candidate.name, decorationIndex));
                decorationIndex++;
            }
        }

        private static void ConfigureLookInspectDialogueInteractable(InteractableItem interactable, string displayName, string line)
        {
            if (interactable == null)
            {
                return;
            }

            interactable.displayName = displayName;
            interactable.storyId = $"room.{ToStoryIdSegment(displayName)}";
            interactable.lookDialogueSpeaker = DefaultInnerSpeaker;
            interactable.lookDialogueBody = line;
            interactable.lookDialogueDisplayDurationSeconds = 2.25f;
            interactable.isInteractable = true;

            interactable.options.Clear();
            interactable.options.Add(new InteractionOption
            {
                id = "look",
                label = "Look",
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
            interactable.options.Add(new InteractionOption
            {
                id = "inspect",
                label = "Inspect",
                slot = InteractionOptionSlot.Bottom,
                visible = true,
                enabled = true,
                opensInspection = true
            });
        }

        private void ApplyDoorState()
        {
            if (enteranceDoorInteractable == null)
            {
                return;
            }

            enteranceDoorInteractable.displayName = "Entrance Door";
            enteranceDoorInteractable.storyId = "room.entranceDoor";
            enteranceDoorInteractable.lookDialogueBody = string.Empty;
            enteranceDoorInteractable.isInteractable = true;

            enteranceDoorInteractable.options.Clear();
            enteranceDoorInteractable.options.Add(new InteractionOption
            {
                id = "leave",
                label = doorReady ? ReadyDoorLabel : DefaultDoorLabel,
                slot = InteractionOptionSlot.Top,
                visible = doorReady,
                enabled = doorReady
            });

            var outline = enteranceDoorInteractable.outline;
            if (outline != null)
            {
                var isFocused = interactionDirector != null && interactionDirector.CurrentFocus == enteranceDoorInteractable;
                outline.SetVisible(doorReady && isFocused);
            }
        }

        private void ApplyLaptopObjectiveVisuals()
        {
            if (laptopInteractable == null || laptopInteractable.outline == null)
            {
                return;
            }

            var isFocused = interactionDirector != null && interactionDirector.CurrentFocus == laptopInteractable;
            var shouldShowObjectiveHint = laptopObjectiveActive && !laptopResolved && !laptopSessionOpen;
            laptopInteractable.outline.SetVisible(shouldShowObjectiveHint || isFocused);
        }

        private void ApplyLaptopControlState()
        {
            if (laptopDesktopSystem != null && laptopDesktopSystem.IsOpen)
            {
                HandleLaptopOpened();
                return;
            }

            ReleaseLaptopControl();
        }

        private void HandleLaptopOpened()
        {
            laptopSessionOpen = true;
            CacheCursorState();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (interactionDirector != null)
            {
                interactionDirector.SetInteractionsLocked(true);
            }

            if (localPlayerManager != null && !lockedMovementForLaptop)
            {
                localPlayerManager.SetMovementInputEnabled(false);
                lockedMovementForLaptop = true;
            }

            ApplyLaptopObjectiveVisuals();
        }

        private void HandleLaptopClosed()
        {
            ReleaseLaptopControl();
            ApplyLaptopObjectiveVisuals();
        }

        private void ReleaseLaptopControl()
        {
            laptopSessionOpen = false;

            if (interactionDirector != null)
            {
                interactionDirector.SetInteractionsLocked(false);
            }

            if (lockedMovementForLaptop && localPlayerManager != null)
            {
                localPlayerManager.SetMovementInputEnabled(true);
                lockedMovementForLaptop = false;
            }

            if (cachedCursorState)
            {
                Cursor.lockState = previousCursorLockState;
                Cursor.visible = previousCursorVisible;
                cachedCursorState = false;
            }
        }

        private void CacheCursorState()
        {
            if (cachedCursorState)
            {
                return;
            }

            previousCursorLockState = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            cachedCursorState = true;
        }

        private void HandleDoorOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != enteranceDoorInteractable)
            {
                return;
            }

            if (!string.Equals(invocation.OptionId, "leave", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ConfirmDoorInteraction();
        }

        private void HandleLaptopReminderViewed()
        {
            MarkLaptopResolved();
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
                laptopObjectiveActive = false;
                laptopSignalSent = false;
                nextLaptopSignalTime = 0f;
                laptopWaitSignalNodeEntered = false;
                ApplyDoorState();
                ApplyLaptopObjectiveVisuals();
                TryRaisePendingLaptopSignal();
            }
        }

        private void HandleNodeNotification(StoryNodeNotification notification)
        {
            if (notification == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(notification.SessionId))
            {
                currentSessionId = notification.SessionId;
            }

            if (string.Equals(notification.NodeType, WaitSignalNodeTypeName, StringComparison.Ordinal))
            {
                if (notification.Kind == StoryNodeNotificationKind.Entered && !doorReady)
                {
                    laptopWaitSignalNodeEntered = true;
                }
            }

            TryRaisePendingLaptopSignal();
        }

        private void HandleStateChanged(StoryStateChangedPayload payload)
        {
            if (payload == null || payload.MachineId != "BedroomStory.Progress")
            {
                return;
            }

            laptopObjectiveActive = string.Equals(payload.NextStateId, "LaptopObjectiveActive", StringComparison.Ordinal);
            doorReady = string.Equals(payload.NextStateId, "DoorReady", StringComparison.Ordinal) ||
                        string.Equals(payload.NextStateId, "TransitionCommitted", StringComparison.Ordinal);

            if (string.Equals(payload.NextStateId, "FreshSpawn", StringComparison.Ordinal))
            {
                transitionCommitted = false;
            }

            if (!laptopObjectiveActive)
            {
                laptopWaitSignalNodeEntered = false;
                nextLaptopSignalTime = 0f;
            }

            ApplyDoorState();
            ApplyLaptopObjectiveVisuals();
            TryRaisePendingLaptopSignal();

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
        }

        private void TryRaisePendingLaptopSignal()
        {
            if (!laptopResolved || channels == null)
            {
                return;
            }

            if (!laptopObjectiveActive && !laptopWaitSignalNodeEntered)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(currentSessionId))
            {
                return;
            }

            if (Time.unscaledTime < nextLaptopSignalTime)
            {
                return;
            }

            channels.ExternalSignals?.Raise(new StoryExternalSignal
            {
                SessionId = currentSessionId,
                SignalId = BedroomStorySignals.LaptopChecked,
                Payload = laptopInteractable != null ? laptopInteractable.name : LaptopFallbackName
            });

            laptopSignalSent = true;
            nextLaptopSignalTime = Time.unscaledTime + Mathf.Max(0.05f, laptopSignalRetryIntervalSeconds);
        }

        private void HandleAnyItemDialogueRequested(InteractableItem item, StoryDialogueRequest request)
        {
            if (request == null || channels == null)
            {
                return;
            }

            request.SessionId = string.IsNullOrWhiteSpace(request.SessionId) ? currentSessionId : request.SessionId;
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
            interactable.inspectionSourceRoot = interactable.inspectionSourceRoot != null ? interactable.inspectionSourceRoot : interactable.transform;
            interactable.outline = interactable.outline != null ? interactable.outline : interactable.GetComponent<SelectableOutline>() ?? interactable.gameObject.AddComponent<SelectableOutline>();
            interactable.isInteractable = true;
            return interactable;
        }

        private static void DisableDistractorInteractable(string hierarchyPath, string fallbackName)
        {
            var target = GameObject.Find(hierarchyPath) ?? GameObject.Find(fallbackName);
            if (target == null)
            {
                return;
            }

            var interactable = target.GetComponent<InteractableItem>();
            if (interactable == null)
            {
                return;
            }

            interactable.isInteractable = false;
            interactable.options.Clear();
            if (interactable.outline != null)
            {
                interactable.outline.SetVisible(false);
            }
        }

        private void DisableNonStoryInteractables()
        {
            var storyInteractables = new HashSet<InteractableItem>();
            AddIfNotNull(storyInteractables, laptopInteractable);
            AddIfNotNull(storyInteractables, enteranceDoorInteractable);
            AddIfNotNull(storyInteractables, bedInteractable);
            AddIfNotNull(storyInteractables, deskInteractable);
            AddIfNotNull(storyInteractables, mirrorInteractable);
            AddIfNotNull(storyInteractables, wardrobeInteractable);
            AddIfNotNull(storyInteractables, coffeeMakerInteractable);
            AddIfNotNull(storyInteractables, ovenInteractable);

            var interactables = FindObjectsByType<InteractableItem>(FindObjectsSortMode.None);
            for (var index = 0; index < interactables.Length; index++)
            {
                var interactable = interactables[index];
                if (interactable == null || storyInteractables.Contains(interactable))
                {
                    continue;
                }

                if (IsUnderWardrobe(interactable.transform))
                {
                    continue;
                }

                interactable.isInteractable = false;
                interactable.options.Clear();
                if (interactable.outline != null)
                {
                    interactable.outline.SetVisible(false);
                }
            }
        }

        private static void AddIfNotNull(HashSet<InteractableItem> targets, InteractableItem interactable)
        {
            if (interactable != null)
            {
                targets.Add(interactable);
            }
        }

        private static InteractableItem EnsureInteractable(GameObject target, string displayName)
        {
            if (target == null)
            {
                return null;
            }

            var interactable = target.GetComponent<InteractableItem>();
            if (interactable == null)
            {
                interactable = target.AddComponent<InteractableItem>();
            }

            return PrepareInteractable(interactable, displayName);
        }

        private bool IsUnderWardrobe(Transform candidate)
        {
            if (candidate == null || wardrobeInteractable == null || wardrobeInteractable.transform == null)
            {
                return false;
            }

            return candidate != wardrobeInteractable.transform && candidate.IsChildOf(wardrobeInteractable.transform);
        }

        private static bool IsWardrobeDecorationName(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                   && (name.IndexOf("shoe", StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("rack", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetWardrobeDecorationDisplayName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "Shoes";
            }

            if (rawName.IndexOf("rack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Shoe Rack";
            }

            return "Shoes";
        }

        private static string GetWardrobeDecorationLine(string rawName, int decorationIndex)
        {
            if (!string.IsNullOrWhiteSpace(rawName) && rawName.IndexOf("rack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "The rack is packed with shoes I probably should have sorted months ago.";
            }

            return decorationIndex == 0
                ? "A neat pair of shoes. Small details like this make the room feel lived in."
                : "Another pair of shoes, waiting for a day that feels a little more put together.";
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
                var anchorPosition = GetPromptAnchorPosition(bounds);
                anchor.position = anchorPosition;
            }
            else
            {
                anchor.localPosition = new Vector3(0f, 0.2f, 0f);
            }

            return anchor;
        }

        private static Vector3 GetPromptAnchorPosition(Bounds bounds)
        {
            // Use a lower anchor for tall meshes (wardrobes, beds) so prompts don't float near ceilings.
            var objectHeight = Mathf.Max(bounds.size.y, 0.01f);
            var normalizedHeight = objectHeight >= 1.8f
                ? 0.14f
                : objectHeight >= 0.8f
                    ? 0.32f
                    : 0.62f;
            var anchorY = Mathf.Lerp(bounds.min.y, bounds.max.y, normalizedHeight);
            return new Vector3(bounds.center.x, anchorY, bounds.center.z);
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

        private static string ToStoryIdSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "item";
            }

            var chars = new char[value.Length];
            var count = 0;
            for (var index = 0; index < value.Length; index++)
            {
                var c = value[index];
                if (!char.IsLetterOrDigit(c))
                {
                    continue;
                }

                chars[count++] = char.ToLowerInvariant(c);
            }

            return count > 0 ? new string(chars, 0, count) : "item";
        }
    }
}
