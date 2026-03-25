using System;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Blocks.Gameplay.Core.UI.LaptopOS;
using ItemInteraction;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
        [SerializeField] private BedroomObjectivePanelUi objectivePanel;
        [SerializeField] private bool laptopResolved;
        [SerializeField] private bool laptopObjectiveActive;
        [SerializeField] private bool laptopSignalSent;
        [SerializeField, Min(0.05f)] private float laptopSignalRetryIntervalSeconds = 0.35f;
        [SerializeField] private float nextLaptopSignalTime;
        [SerializeField] private bool laptopWaitSignalNodeEntered;
        [SerializeField] private bool doorReady;
        [SerializeField] private bool transitionCommitted;
        [SerializeField] private string currentSessionId = string.Empty;
        [SerializeField, Min(0.1f)] private float webGlDoorReadyFallbackDelaySeconds = 1.1f;
        [SerializeField, Min(0.1f)] private float webGlTransitionFallbackDelaySeconds = 0.9f;

        private CursorLockMode previousCursorLockState;
        private bool previousCursorVisible;
        private bool cachedCursorState;
        private bool lockedMovementForLaptop;
        private bool laptopSessionOpen;
        private bool cachedCoreInputEnabled;
        private bool cachedCoreCameraEnabled;
        private bool cachedCoreCameraLookInputEnabled;
        private bool cachedLaptopControlState;
        private Coroutine gameplayCursorRecoveryRoutine;
        private Coroutine gameplayStateRecoveryRoutine;
        private Coroutine doorReadyFallbackRoutine;
        private Coroutine transitionFallbackRoutine;
        private bool pendingGameplayCursorLock;

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
            CancelGameplayCursorRecovery();
            CancelGameplayStateRecovery();
            CancelDoorReadyFallback();
            CancelTransitionFallback();
            ReleaseLaptopControl();
        }

        private void LateUpdate()
        {
            ResolveRuntimeSceneReferences();
            ApplyLaptopObjectiveVisuals();
            ClearObsoleteLaptopFocus();
            TryRaisePendingLaptopSignal();
            TryReacquireGameplayCursorLock();
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
            ConfigureLaptopInteractable();
            ApplyLaptopObjectiveVisuals();
            UpdateObjectivePanel();
            TryRaisePendingLaptopSignal();
            QueueDoorReadyFallback();
        }

        public void SetDoorReady(bool value)
        {
            doorReady = value;
            ConfigureLaptopInteractable();
            ApplyDoorState();
            UpdateObjectivePanel();
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
            UpdateObjectivePanel();
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
            CancelDoorReadyFallback();
            CancelTransitionFallback();
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

            if (laptopInteractable != null)
            {
                laptopInteractable.OptionTriggered -= HandleLaptopOptionTriggered;
                laptopInteractable.OptionTriggered += HandleLaptopOptionTriggered;
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

            if (laptopInteractable != null)
            {
                laptopInteractable.OptionTriggered -= HandleLaptopOptionTriggered;
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
            objectivePanel = objectivePanel != null
                ? objectivePanel
                : GetComponent<BedroomObjectivePanelUi>() ?? FindFirstObjectByType<BedroomObjectivePanelUi>(FindObjectsInactive.Include);
            if (objectivePanel == null)
            {
                objectivePanel = gameObject.AddComponent<BedroomObjectivePanelUi>();
            }

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
            DisableLegacyLaptopRelay();
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
            UpdateObjectivePanel();
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

            var allowLaptopInteraction = !doorReady && !transitionCommitted;
            laptopInteractable.displayName = "Laptop";
            laptopInteractable.storyId = "room.laptop";
            laptopInteractable.lookDialogueSpeaker = DefaultInnerSpeaker;
            laptopInteractable.lookDialogueBody = string.Empty;
            laptopInteractable.isInteractable = allowLaptopInteraction;

            laptopInteractable.options.Clear();
            laptopInteractable.options.Add(new InteractionOption
            {
                id = LaptopPromptOptionId,
                label = laptopResolved ? "Review Reminder" : LaptopPromptLabel,
                slot = InteractionOptionSlot.Top,
                visible = allowLaptopInteraction,
                enabled = allowLaptopInteraction
            });

            if (!allowLaptopInteraction && interactionDirector != null && interactionDirector.CurrentFocus == laptopInteractable)
            {
                interactionDirector.ClearFocusImmediate();
                interactionDirector.BlockPromptResume(0.55f);
            }
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

        private void UpdateObjectivePanel()
        {
            if (objectivePanel == null)
            {
                return;
            }

            if (transitionCommitted)
            {
                objectivePanel.ShowObjective(
                    "Mission complete",
                    "Heading to class...",
                    2,
                    2,
                    true);
                return;
            }

            if (doorReady)
            {
                objectivePanel.ShowObjective(
                    "Leave apartment",
                    "Use the entrance door to go to class.",
                    2,
                    2,
                    false);
                return;
            }

            if (laptopResolved)
            {
                objectivePanel.ShowObjective(
                    "Get to the door",
                    "Walk to the entrance door when you're ready.",
                    2,
                    2,
                    false);
                return;
            }

            if (laptopObjectiveActive)
            {
                objectivePanel.ShowObjective(
                    "Check laptop reminder",
                    laptopSessionOpen
                        ? "Review the reminder, then close the laptop."
                        : "Open the laptop and read your class reminder.",
                    1,
                    2,
                    false);
                return;
            }

            objectivePanel.ShowObjective(
                "Morning routine",
                "Check the laptop before leaving the bedroom.",
                1,
                2,
                false);
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
            CancelGameplayCursorRecovery();
            CancelGameplayStateRecovery();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (interactionDirector != null)
            {
                interactionDirector.SetInteractionsLocked(true);
            }

            if (localPlayerManager != null && !lockedMovementForLaptop)
            {
                cachedCoreInputEnabled = localPlayerManager.CoreInput != null && localPlayerManager.CoreInput.enabled;
                cachedCoreCameraEnabled = localPlayerManager.CoreCamera != null && localPlayerManager.CoreCamera.enabled;
                cachedCoreCameraLookInputEnabled = localPlayerManager.CoreCamera != null && localPlayerManager.CoreCamera.IsLookInputEnabled;
                cachedLaptopControlState = true;

                localPlayerManager.SetMovementInputEnabled(false);
                lockedMovementForLaptop = true;

                if (localPlayerManager.CoreInput != null)
                {
                    localPlayerManager.CoreInput.enabled = false;
                }

                if (localPlayerManager.CoreCamera != null && localPlayerManager.CoreCamera.enabled)
                {
                    localPlayerManager.CoreCamera.SetLookInputEnabled(false);
                }
            }

            ApplyLaptopObjectiveVisuals();
        }

        private void HandleLaptopClosed()
        {
            ReleaseLaptopControl();
            RestoreGameplayStateImmediate();
            QueueGameplayStateRecovery();
            ApplyLaptopObjectiveVisuals();
        }

        private void ReleaseLaptopControl()
        {
            laptopSessionOpen = false;
            var releasedLaptopGameplayLock = lockedMovementForLaptop || cachedLaptopControlState;

            if (interactionDirector != null)
            {
                interactionDirector.SetInteractionsLocked(false);
            }

            if (lockedMovementForLaptop && localPlayerManager != null)
            {
                localPlayerManager.SetMovementInputEnabled(true);
                lockedMovementForLaptop = false;
            }

            if (localPlayerManager != null && cachedLaptopControlState)
            {
                if (localPlayerManager.CoreInput != null)
                {
                    localPlayerManager.CoreInput.enabled = cachedCoreInputEnabled;
                }

                if (localPlayerManager.CoreCamera != null)
                {
                    localPlayerManager.CoreCamera.enabled = cachedCoreCameraEnabled;
                    if (cachedCoreCameraEnabled)
                    {
                        localPlayerManager.CoreCamera.SetLookInputEnabled(cachedCoreCameraLookInputEnabled);
                    }
                }

                cachedLaptopControlState = false;
            }

            var shouldForceGameplayLock = ShouldForceGameplayCursorLock();
            if (IsWebGlLikeRuntime())
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                pendingGameplayCursorLock = false;
                cachedCursorState = false;
            }
            else if (shouldForceGameplayLock)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
                pendingGameplayCursorLock = Cursor.lockState != CursorLockMode.Locked;

                if (pendingGameplayCursorLock && ShouldDeferGameplayCursorLockToUserGesture())
                {
                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;
                }

                cachedCursorState = false;
            }
            else if (cachedCursorState)
            {
                Cursor.lockState = previousCursorLockState;
                Cursor.visible = previousCursorVisible;
                cachedCursorState = false;
            }

            if (shouldForceGameplayLock)
            {
                QueueGameplayCursorRecovery();
            }

            if (releasedLaptopGameplayLock)
            {
                QueueGameplayStateRecovery();
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
            QueueTransitionFallback();
        }

        private void HandleLaptopReminderViewed()
        {
            MarkLaptopResolved();
            laptopWaitSignalNodeEntered = true;
            nextLaptopSignalTime = 0f;
            TryRaisePendingLaptopSignal();
            ForceExitLaptopSession();
            RestoreGameplayStateImmediate();
            QueueGameplayStateRecovery();
            QueueDoorReadyFallback();
        }

        private void HandleLaptopOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != laptopInteractable)
            {
                return;
            }

            if (!string.Equals(invocation.OptionId, LaptopPromptOptionId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (laptopDesktopSystem == null)
            {
                laptopDesktopSystem = FindFirstObjectByType<LaptopDesktopSystem>(FindObjectsInactive.Include);
            }

            CacheCursorState();
            laptopDesktopSystem?.Open();
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
                CancelDoorReadyFallback();
                CancelTransitionFallback();
                ApplyDoorState();
                ApplyLaptopObjectiveVisuals();
                UpdateObjectivePanel();
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

            if (doorReady)
            {
                CancelDoorReadyFallback();
            }

            if (transitionCommitted)
            {
                CancelTransitionFallback();
            }

            if (!laptopObjectiveActive)
            {
                laptopWaitSignalNodeEntered = false;
                nextLaptopSignalTime = 0f;
                ForceExitLaptopSession();
                RestoreGameplayStateImmediate();
            }

            ApplyDoorState();
            ConfigureLaptopInteractable();
            ApplyLaptopObjectiveVisuals();
            UpdateObjectivePanel();
            TryRaisePendingLaptopSignal();

            if (string.Equals(payload.NextStateId, "TransitionCommitted", StringComparison.Ordinal))
            {
                CommitTransition();
            }
        }

        private void ForceExitLaptopSession()
        {
            if (laptopDesktopSystem != null && laptopDesktopSystem.IsOpen)
            {
                laptopDesktopSystem.Close();
            }
            else
            {
                ReleaseLaptopControl();
            }

            if (interactionDirector != null)
            {
                interactionDirector.SetInteractionsLocked(false);
                interactionDirector.ClearFocusImmediate();
                interactionDirector.BlockPromptResume(0.55f);
            }

            RestoreGameplayStateImmediate();
        }

        private void ClearObsoleteLaptopFocus()
        {
            if (interactionDirector == null || laptopInteractable == null)
            {
                return;
            }

            var allowLaptopInteraction = !laptopResolved && !doorReady && !transitionCommitted;
            if (allowLaptopInteraction || interactionDirector.CurrentFocus != laptopInteractable)
            {
                return;
            }

            interactionDirector.ClearFocusImmediate();
            interactionDirector.BlockPromptResume(0.7f);
        }

        private void QueueGameplayCursorRecovery()
        {
            CancelGameplayCursorRecovery();
            if (!ShouldForceGameplayCursorLock())
            {
                return;
            }

            if (!CanRunDeferredRecovery())
            {
                CompleteGameplayCursorRecoveryImmediate();
                return;
            }

            gameplayCursorRecoveryRoutine = StartCoroutine(RestoreGameplayCursorRoutine());
        }

        private void CancelGameplayCursorRecovery()
        {
            if (gameplayCursorRecoveryRoutine == null)
            {
                return;
            }

            StopCoroutine(gameplayCursorRecoveryRoutine);
            gameplayCursorRecoveryRoutine = null;
        }

        private void QueueGameplayStateRecovery()
        {
            CancelGameplayStateRecovery();
            if (!CanRunDeferredRecovery())
            {
                CompleteGameplayStateRecoveryImmediate();
                return;
            }

            gameplayStateRecoveryRoutine = StartCoroutine(RestoreGameplayStateRoutine());
        }

        private void CancelGameplayStateRecovery()
        {
            if (gameplayStateRecoveryRoutine == null)
            {
                return;
            }

            StopCoroutine(gameplayStateRecoveryRoutine);
            gameplayStateRecoveryRoutine = null;
        }

        private System.Collections.IEnumerator RestoreGameplayCursorRoutine()
        {
            yield return null;
            yield return new WaitForEndOfFrame();

            CompleteGameplayCursorRecoveryImmediate();
        }

        private System.Collections.IEnumerator RestoreGameplayStateRoutine()
        {
            yield return null;
            yield return new WaitForEndOfFrame();

            CompleteGameplayStateRecoveryImmediate();
        }

        private bool CanRunDeferredRecovery()
        {
            return isActiveAndEnabled && gameObject.activeInHierarchy;
        }

        private void CompleteGameplayCursorRecoveryImmediate()
        {
            if (laptopSessionOpen || ShouldDeferGameplayCursorLockToUserGesture())
            {
                gameplayCursorRecoveryRoutine = null;
                return;
            }

            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            pendingGameplayCursorLock = Cursor.lockState != CursorLockMode.Locked;
            gameplayCursorRecoveryRoutine = null;
        }

        private void CompleteGameplayStateRecoveryImmediate()
        {
            if (laptopSessionOpen)
            {
                gameplayStateRecoveryRoutine = null;
                return;
            }

            RestoreGameplayStateImmediate();
            gameplayStateRecoveryRoutine = null;
        }

        private void RestoreGameplayStateImmediate()
        {
            ResolveRuntimeSceneReferences();
            if (laptopSessionOpen)
            {
                return;
            }

            var restoredAnyPlayer = false;
            var players = FindObjectsByType<CorePlayerManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var index = 0; index < players.Length; index++)
            {
                var player = players[index];
                if (player == null || !player.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!ShouldRestorePlayerGameplayState(player, players.Length))
                {
                    continue;
                }

                RestorePlayerGameplayState(player);
                restoredAnyPlayer = true;
            }

            if (!restoredAnyPlayer && localPlayerManager != null)
            {
                RestorePlayerGameplayState(localPlayerManager);
            }

            if (interactionDirector != null)
            {
                interactionDirector.SetInteractionsLocked(false);
                interactionDirector.ClearFocusImmediate();
                interactionDirector.BlockPromptResume(0.7f);
            }

            ConfigureLaptopInteractable();
            ApplyLaptopObjectiveVisuals();
        }

        private bool ShouldRestorePlayerGameplayState(CorePlayerManager player, int totalPlayers)
        {
            if (player == null)
            {
                return false;
            }

            if (player == localPlayerManager)
            {
                return true;
            }

            if (player.IsOwner)
            {
                return true;
            }

            return totalPlayers <= 1;
        }

        private static void RestorePlayerGameplayState(CorePlayerManager player)
        {
            if (player == null)
            {
                return;
            }

            player.SetMovementInputEnabled(true);

            if (player.CoreInput != null)
            {
                player.CoreInput.enabled = true;
            }

            if (player.CoreCamera != null)
            {
                if (!player.CoreCamera.enabled)
                {
                    player.CoreCamera.enabled = true;
                }

                player.CoreCamera.SetLookInputEnabled(true);
            }
        }

        private static bool ShouldForceGameplayCursorLock()
        {
            if (Application.isMobilePlatform || IsWebGlLikeRuntime())
            {
                return false;
            }

            return Mouse.current != null;
        }

        private static bool ShouldDeferGameplayCursorLockToUserGesture()
        {
            return IsWebGlLikeRuntime();
        }

        private void TryReacquireGameplayCursorLock()
        {
            if (!pendingGameplayCursorLock || laptopSessionOpen)
            {
                return;
            }

            if (IsWebGlLikeRuntime())
            {
                pendingGameplayCursorLock = false;
                return;
            }

            if (Cursor.lockState == CursorLockMode.Locked && !Cursor.visible)
            {
                pendingGameplayCursorLock = false;
                return;
            }

            if (!IsWebGlLikeRuntime() && !ShouldForceGameplayCursorLock())
            {
                return;
            }

            var mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            if (!mousePressed)
            {
                return;
            }

            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            pendingGameplayCursorLock = Cursor.lockState != CursorLockMode.Locked;
        }

        private void QueueDoorReadyFallback()
        {
            CancelDoorReadyFallback();
            if (!IsWebGlLikeRuntime() || !laptopResolved || doorReady || transitionCommitted)
            {
                return;
            }

            doorReadyFallbackRoutine = StartCoroutine(DoorReadyFallbackRoutine());
        }

        private void CancelDoorReadyFallback()
        {
            if (doorReadyFallbackRoutine == null)
            {
                return;
            }

            StopCoroutine(doorReadyFallbackRoutine);
            doorReadyFallbackRoutine = null;
        }

        private System.Collections.IEnumerator DoorReadyFallbackRoutine()
        {
            var remaining = Mathf.Max(0.1f, webGlDoorReadyFallbackDelaySeconds);
            while (remaining > 0f)
            {
                if (!laptopResolved || doorReady || transitionCommitted)
                {
                    doorReadyFallbackRoutine = null;
                    yield break;
                }

                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (!doorReady && laptopResolved && !transitionCommitted)
            {
                Debug.LogWarning("[BedroomStoryInteractionBridge] WebGL fallback unlocked the bedroom door because story progression did not advance after the laptop reminder.");
                SetDoorReady(true);
                interactionDirector?.ClearFocusImmediate();
                interactionDirector?.BlockPromptResume(0.35f);
            }

            doorReadyFallbackRoutine = null;
        }

        private void QueueTransitionFallback()
        {
            CancelTransitionFallback();
            if (!IsWebGlLikeRuntime() || !doorReady || transitionCommitted)
            {
                return;
            }

            transitionFallbackRoutine = StartCoroutine(TransitionFallbackRoutine());
        }

        private void CancelTransitionFallback()
        {
            if (transitionFallbackRoutine == null)
            {
                return;
            }

            StopCoroutine(transitionFallbackRoutine);
            transitionFallbackRoutine = null;
        }

        private System.Collections.IEnumerator TransitionFallbackRoutine()
        {
            var remaining = Mathf.Max(0.1f, webGlTransitionFallbackDelaySeconds);
            while (remaining > 0f)
            {
                if (!doorReady || transitionCommitted)
                {
                    transitionFallbackRoutine = null;
                    yield break;
                }

                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (doorReady && !transitionCommitted)
            {
                Debug.LogWarning("[BedroomStoryInteractionBridge] WebGL fallback committed the bedroom transition because the door confirmation signal did not advance story state in time.");
                CommitTransition();
            }

            transitionFallbackRoutine = null;
        }

        private static bool IsWebGlLikeRuntime()
        {
#if UNITY_EDITOR
            return EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL;
#else
            return Application.platform == RuntimePlatform.WebGLPlayer;
#endif
        }

        private void DisableLegacyLaptopRelay()
        {
            if (laptopInteractable == null)
            {
                return;
            }

            var relay = laptopInteractable.GetComponent<LaptopDesktopOpenRelay>();
            if (relay != null && relay.enabled)
            {
                relay.enabled = false;
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

            var gameObject = FindSceneGameObject(hierarchyPath, fallbackName);
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

        // Unity asserts if GameObject.Find(string) runs while the target object is inactive during teardown.
        private static GameObject FindSceneGameObject(string hierarchyPath, string fallbackName)
        {
            var hierarchySegments = string.IsNullOrWhiteSpace(hierarchyPath)
                ? Array.Empty<string>()
                : hierarchyPath.Split('/');
            GameObject activeFallback = null;
            GameObject anyFallback = null;
            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < transforms.Length; index++)
            {
                var transform = transforms[index];
                if (transform == null)
                {
                    continue;
                }

                var candidate = transform.gameObject;
                if (candidate == null || !candidate.scene.IsValid())
                {
                    continue;
                }

                if (MatchesHierarchyPath(transform, hierarchySegments))
                {
                    return candidate;
                }

                if (string.IsNullOrWhiteSpace(fallbackName) ||
                    !string.Equals(candidate.name, fallbackName, StringComparison.Ordinal))
                {
                    continue;
                }

                anyFallback ??= candidate;
                if (activeFallback == null && candidate.activeInHierarchy)
                {
                    activeFallback = candidate;
                }
            }

            return activeFallback != null ? activeFallback : anyFallback;
        }

        private static bool MatchesHierarchyPath(Transform transform, IReadOnlyList<string> hierarchySegments)
        {
            if (transform == null || hierarchySegments == null || hierarchySegments.Count == 0)
            {
                return false;
            }

            var current = transform;
            for (var index = hierarchySegments.Count - 1; index >= 0; index--)
            {
                if (current == null || !string.Equals(current.name, hierarchySegments[index], StringComparison.Ordinal))
                {
                    return false;
                }

                current = current.parent;
            }

            return current == null;
        }

        private static void DisableDistractorInteractable(string hierarchyPath, string fallbackName)
        {
            var target = FindSceneGameObject(hierarchyPath, fallbackName);
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
