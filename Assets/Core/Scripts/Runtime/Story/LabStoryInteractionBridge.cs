using System;
using System.Collections;
using System.Collections.Generic;
using ItemInteraction;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using ModularStoryFlow.Runtime.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class LabStoryInteractionBridge : MonoBehaviour
    {
        private const string StoryMachineId = "LabStory.Progress";
        private const string InspectOptionId = "inspect";
        private const string LookOptionId = "look";
        private const string UseOptionId = "use";
        private const string EnterOptionId = "enter";
        private const string BodyStoryId = "lab.bodyModel";
        private const string DnaMachineStoryId = "lab.shrinkMachine";
        private const string RocketStoryId = "lab.rocket";
        private const string ShrinkMachinePromptAnchorName = "PromptAnchor_Runtime";
        private const string RocketPromptAnchorName = "RocketPromptAnchor_Runtime";
        private const string DefaultInnerSpeaker = "You";
        private const float DefaultObjectiveFocusDistance = 4f;
        private static readonly List<UIDocument> HudDocumentsBuffer = new List<UIDocument>(8);

        [SerializeField] private StoryFlowChannels channels;
        [SerializeField] private LabSceneContext sceneContext;
        [SerializeField] private InteractionDirector interactionDirector;
        [SerializeField] private StoryFlowPlayer storyFlowPlayer;
        [SerializeField] private StoryNpcStorySignalBridge npcSignalBridge;
        [SerializeField] private LabStorySceneTransition sceneTransition;
        [SerializeField] private string currentSessionId = string.Empty;
        [SerializeField] private bool bodyInspectionReady;
        [SerializeField] private bool puzzleReady;
        [SerializeField] private bool shrinkReady;
        [SerializeField] private bool rocketReady;
        [SerializeField] private bool cutsceneCommitted;
        [SerializeField] private bool capConversationCompleted;
        [SerializeField] private bool bodyInspectionCompleted;
        [SerializeField] private bool puzzleSolved;
        [SerializeField] private bool shrinkCompleted;
        [SerializeField] private string currentObjectiveText = string.Empty;
        [SerializeField, Min(0f)] private float objectiveOutlineDistancePadding = 0.75f;
        [SerializeField] private bool puzzleOpenRequestedByMachine;

        private Transform cachedPlayerTransform;
        private float nextPlayerResolveTime;
        private Coroutine openLightPuzzleRoutine;
        private Coroutine autoShrinkRoutine;

        public void Configure(StoryFlowChannels storyChannels, LabStorySceneTransition transition)
        {
            if (isActiveAndEnabled)
            {
                UnregisterStoryChannels();
                UnregisterSceneHooks();
            }

            channels = storyChannels;
            if (transition != null)
            {
                sceneTransition = transition;
            }

            ResolveSceneReferences();
            SyncSessionIdFromPlayer();
            ApplyScenePresentation();

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
            sceneContext?.CapConversationDirector?.SetSessionId(currentSessionId);
        }

        public void RefreshScenePresentation()
        {
            ResolveSceneReferences();
            SyncSessionIdFromPlayer();
            ApplyScenePresentation();
        }

        private void Awake()
        {
            ResolveSceneReferences();
            ApplyScenePresentation();
        }

        private void OnEnable()
        {
            ResolveSceneReferences();
            SyncSessionIdFromPlayer();
            RegisterGlobalDialogueRelay();
            RegisterStoryChannels();
            RegisterSceneHooks();
            ApplyScenePresentation();
        }

        private void OnDisable()
        {
            UnregisterSceneHooks();
            UnregisterStoryChannels();
            UnregisterGlobalDialogueRelay();
            if (openLightPuzzleRoutine != null)
            {
                StopCoroutine(openLightPuzzleRoutine);
                openLightPuzzleRoutine = null;
            }

            if (autoShrinkRoutine != null)
            {
                StopCoroutine(autoShrinkRoutine);
                autoShrinkRoutine = null;
            }
        }

        private void LateUpdate()
        {
            UpdateObjectiveVisuals();
            QueuePuzzleOpenIfNeeded();
        }

        private void RegisterStoryChannels()
        {
            if (channels == null)
            {
                return;
            }

            channels.StateChanged?.Register(HandleStateChanged);
            channels.GraphNotifications?.Register(HandleGraphNotification);
        }

        private void UnregisterStoryChannels()
        {
            if (channels == null)
            {
                return;
            }

            channels.StateChanged?.Unregister(HandleStateChanged);
            channels.GraphNotifications?.Unregister(HandleGraphNotification);
        }

        private void RegisterSceneHooks()
        {
            RegisterOption(sceneContext?.BodyInteractable, HandleBodyTriggered);
            RegisterOption(sceneContext?.DnaMachineInteractable, HandleDnaMachineTriggered);
            RegisterOption(sceneContext?.RocketInteractable, HandleRocketTriggered);

            if (sceneContext?.CapConversationDirector != null)
            {
                sceneContext.CapConversationDirector.ConversationCompleted -= HandleCapConversationCompleted;
                sceneContext.CapConversationDirector.ConversationCompleted += HandleCapConversationCompleted;
            }

            if (sceneContext?.BodyInspectionUi != null)
            {
                sceneContext.BodyInspectionUi.Completed -= HandleBodyInspectionCompleted;
                sceneContext.BodyInspectionUi.Completed += HandleBodyInspectionCompleted;
            }

            if (sceneContext?.LightPuzzleUi != null)
            {
                sceneContext.LightPuzzleUi.Solved -= HandlePuzzleSolved;
                sceneContext.LightPuzzleUi.Solved += HandlePuzzleSolved;
                sceneContext.LightPuzzleUi.AssistanceStateChanged -= HandlePuzzleAssistanceStateChanged;
                sceneContext.LightPuzzleUi.AssistanceStateChanged += HandlePuzzleAssistanceStateChanged;
            }

            if (sceneContext?.ShrinkSequenceController != null)
            {
                sceneContext.ShrinkSequenceController.Completed -= HandleShrinkCompleted;
                sceneContext.ShrinkSequenceController.Completed += HandleShrinkCompleted;
            }

            if (sceneContext?.FinalCutsceneController != null)
            {
                sceneContext.FinalCutsceneController.Completed -= HandleFinalCutsceneCompleted;
                sceneContext.FinalCutsceneController.Completed += HandleFinalCutsceneCompleted;
            }
        }

        private void UnregisterSceneHooks()
        {
            UnregisterOption(sceneContext?.BodyInteractable, HandleBodyTriggered);
            UnregisterOption(sceneContext?.DnaMachineInteractable, HandleDnaMachineTriggered);
            UnregisterOption(sceneContext?.RocketInteractable, HandleRocketTriggered);

            if (sceneContext?.CapConversationDirector != null)
            {
                sceneContext.CapConversationDirector.ConversationCompleted -= HandleCapConversationCompleted;
            }

            if (sceneContext?.BodyInspectionUi != null)
            {
                sceneContext.BodyInspectionUi.Completed -= HandleBodyInspectionCompleted;
            }

            if (sceneContext?.LightPuzzleUi != null)
            {
                sceneContext.LightPuzzleUi.Solved -= HandlePuzzleSolved;
                sceneContext.LightPuzzleUi.AssistanceStateChanged -= HandlePuzzleAssistanceStateChanged;
            }

            if (sceneContext?.ShrinkSequenceController != null)
            {
                sceneContext.ShrinkSequenceController.Completed -= HandleShrinkCompleted;
            }

            if (sceneContext?.FinalCutsceneController != null)
            {
                sceneContext.FinalCutsceneController.Completed -= HandleFinalCutsceneCompleted;
            }
        }

        private void ResolveSceneReferences()
        {
            var activeScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();

            if (!IsInScene(interactionDirector, activeScene))
            {
                interactionDirector = null;
            }

            if (!IsInScene(storyFlowPlayer, activeScene))
            {
                storyFlowPlayer = null;
            }

            if (!IsInScene(sceneTransition, activeScene))
            {
                sceneTransition = null;
            }

            if (!IsInScene(sceneContext, activeScene))
            {
                sceneContext = null;
            }

            interactionDirector = interactionDirector != null ? interactionDirector : FindSceneObject<InteractionDirector>(activeScene);
            storyFlowPlayer = storyFlowPlayer != null ? storyFlowPlayer : GetComponent<StoryFlowPlayer>();
            if (!IsInScene(storyFlowPlayer, activeScene))
            {
                storyFlowPlayer = FindSceneObject<StoryFlowPlayer>(activeScene);
            }

            sceneTransition = sceneTransition != null ? sceneTransition : GetComponent<LabStorySceneTransition>();
            if (!IsInScene(sceneTransition, activeScene))
            {
                sceneTransition = FindSceneObject<LabStorySceneTransition>(activeScene);
            }

            sceneContext = sceneContext != null
                ? sceneContext
                : GetComponent<LabSceneContext>() ?? FindSceneObject<LabSceneContext>(activeScene);
            sceneContext?.ResolveRuntimeReferences();

            npcSignalBridge = npcSignalBridge != null ? npcSignalBridge : GetComponent<StoryNpcStorySignalBridge>();
            if (npcSignalBridge == null)
            {
                npcSignalBridge = gameObject.AddComponent<StoryNpcStorySignalBridge>();
            }

            npcSignalBridge.Configure(channels, currentSessionId);
            sceneContext?.CapConversationDirector?.Configure(channels);
        }

        private static bool IsInScene(Component component, Scene scene)
        {
            if (component == null)
            {
                return false;
            }

            var componentScene = component.gameObject.scene;
            if (!scene.IsValid())
            {
                return componentScene.IsValid();
            }

            return componentScene.IsValid() && componentScene == scene;
        }

        private static T FindSceneObject<T>(Scene scene)
            where T : Component
        {
            T fallback = null;
            var candidates = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (candidate == null)
                {
                    continue;
                }

                fallback ??= candidate;
                if (!scene.IsValid() || candidate.gameObject.scene == scene)
                {
                    return candidate;
                }
            }

            return fallback;
        }

        private void ApplyScenePresentation()
        {
            var startupCooldownActive = sceneContext != null && sceneContext.StartupCooldownActive;
            if (interactionDirector != null && !startupCooldownActive)
            {
                interactionDirector.SetInteractionsLocked(false);
            }

            sceneContext?.CapNpcController?.ConfigureForLabMission();
            if (sceneContext?.CapNpc?.Interactable != null)
            {
                var capInteractable = sceneContext.CapNpc.Interactable;
                capInteractable.isInteractable = !startupCooldownActive;
                EnsurePromptAndOutline(capInteractable);
            }

            ConfigureBodyInteractable(sceneContext?.BodyInteractable);
            ConfigureDnaMachineInteractable(sceneContext?.DnaMachineInteractable);
            ConfigureRocketInteractable(sceneContext?.RocketInteractable);
            sceneContext?.DoorController?.SetUnlocked(capConversationCompleted && !startupCooldownActive);
            sceneContext?.CapConversationDirector?.SetMissionProgress(
                capConversationCompleted,
                bodyInspectionReady,
                bodyInspectionCompleted,
                puzzleReady,
                puzzleSolved,
                shrinkReady,
                shrinkCompleted,
                rocketReady);
            sceneContext?.CapNpcController?.SetFollowPlayer(bodyInspectionReady && !bodyInspectionCompleted);
            ForceHideVitalsBars();
            UpdateObjectivePanel();
            UpdateObjectiveVisuals();
        }

        private void ConfigureBodyInteractable(InteractableItem interactable)
        {
            if (interactable == null)
            {
                return;
            }

            interactable.displayName = "Body Model";
            interactable.storyId = "lab.bodyModel";
            interactable.lookDialogueSpeaker = DefaultInnerSpeaker;
            interactable.lookDialogueBody = bodyInspectionReady
                ? "CAP wants me to study the body model before we shrink."
                : "The body model shows where the tiny rocket mission will go.";
            interactable.lookDialogueDisplayDurationSeconds = 2.4f;
            interactable.isInteractable = sceneContext == null || !sceneContext.StartupCooldownActive;
            EnsurePromptAndOutline(interactable);

            interactable.options.Clear();
            interactable.options.Add(new InteractionOption
            {
                id = LookOptionId,
                label = "Look",
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
            interactable.options.Add(new InteractionOption
            {
                id = InspectOptionId,
                label = bodyInspectionReady ? "Inspect Body" : "Inspect Locked",
                slot = InteractionOptionSlot.Bottom,
                visible = true,
                enabled = !sceneContext.StartupCooldownActive && bodyInspectionReady && !bodyInspectionCompleted,
                opensInspection = false
            });
        }

        private void ConfigureDnaMachineInteractable(InteractableItem interactable)
        {
            if (interactable == null)
            {
                return;
            }

            var puzzleAvailable = puzzleReady && !puzzleSolved;
            var shrinkAvailable = shrinkReady && !shrinkCompleted;

            interactable.displayName = "Shrink Machine";
            interactable.storyId = "lab.shrinkMachine";
            interactable.lookDialogueSpeaker = DefaultInnerSpeaker;
            interactable.lookDialogueBody = shrinkReady
                ? "The shrink machine is powered and ready for us."
                : puzzleAvailable
                    ? "The machine needs its light path connected before it can shrink us."
                    : "This machine will help us shrink after we finish the prep work.";
            interactable.lookDialogueDisplayDurationSeconds = 2.4f;
            interactable.isInteractable = sceneContext == null || !sceneContext.StartupCooldownActive;
            EnsurePromptAndOutline(interactable);
            interactable.promptAnchor = EnsureFloatingPromptAnchor(interactable, ShrinkMachinePromptAnchorName, 0.26f, 1.36f);
            interactable.inspectionSourceRoot = interactable.transform;

            var label = shrinkAvailable
                ? "Shrink Us"
                : puzzleAvailable
                    ? "Route Light"
                    : "Stand By";
            var enabled = (sceneContext == null || !sceneContext.StartupCooldownActive) && (puzzleAvailable || shrinkAvailable);

            interactable.options.Clear();
            var useOption = new InteractionOption
            {
                id = UseOptionId,
                label = label,
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = enabled
            };
            useOption.onInvoked.AddListener(() => HandleDnaMachineDirectUse(interactable));
            interactable.options.Add(useOption);
            interactable.options.Add(new InteractionOption
            {
                id = InspectOptionId,
                label = "Inspect",
                slot = InteractionOptionSlot.Right,
                visible = true,
                enabled = true,
                opensInspection = true
            });
            interactable.options.Add(new InteractionOption
            {
                id = LookOptionId,
                label = "Look",
                slot = InteractionOptionSlot.Bottom,
                visible = true,
                enabled = true
            });
        }

        private void HandleDnaMachineDirectUse(InteractableItem interactable)
        {
            if (interactable == null)
            {
                return;
            }

            var option = interactable.FindOption(UseOptionId);
            if (option == null || !option.enabled)
            {
                return;
            }

            HandleDnaMachineTriggered(new InteractionInvocation(interactionDirector, interactable, option));
        }

        private void ConfigureRocketInteractable(InteractableItem interactable)
        {
            if (interactable == null)
            {
                return;
            }

            interactable.displayName = "Mini Rocket";
            interactable.storyId = "lab.rocket";
            interactable.lookDialogueSpeaker = DefaultInnerSpeaker;
            interactable.lookDialogueBody = rocketReady
                ? "The mini rocket is ready. CAP and I can launch now."
                : "The mini rocket stays locked until the lab prep is complete.";
            interactable.lookDialogueDisplayDurationSeconds = 2.4f;
            interactable.isInteractable = sceneContext == null || !sceneContext.StartupCooldownActive;
            EnsurePromptAndOutline(interactable);
            interactable.promptAnchor = EnsureFloatingPromptAnchor(interactable, RocketPromptAnchorName, 0.28f, 1.52f);

            interactable.options.Clear();
            interactable.options.Add(new InteractionOption
            {
                id = LookOptionId,
                label = "Look",
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
            interactable.options.Add(new InteractionOption
            {
                id = EnterOptionId,
                label = rocketReady ? "Enter Rocket" : "Locked",
                slot = InteractionOptionSlot.Bottom,
                visible = true,
                enabled = !sceneContext.StartupCooldownActive && rocketReady && !cutsceneCommitted
            });
        }

        private void UpdateObjectiveVisuals()
        {
            UpdateInteractableOutline(sceneContext?.CapNpc?.Interactable, !capConversationCompleted);
            UpdateInteractableOutline(sceneContext?.BodyInteractable, bodyInspectionReady && !bodyInspectionCompleted);
            UpdateInteractableOutline(sceneContext?.DnaMachineInteractable, (puzzleReady && !puzzleSolved) || (shrinkReady && !shrinkCompleted));
            UpdateInteractableOutline(sceneContext?.RocketInteractable, rocketReady && !cutsceneCommitted);
        }

        private void UpdateInteractableOutline(InteractableItem interactable, bool shouldShowObjective)
        {
            if (interactable == null || interactable.outline == null)
            {
                return;
            }

            var isFocused = interactionDirector != null && interactionDirector.CurrentFocus == interactable;
            var isNearbyObjective = shouldShowObjective && IsPlayerNearInteractable(interactable);
            interactable.outline.SetVisible(isNearbyObjective || isFocused);
        }

        private bool IsPlayerNearInteractable(InteractableItem interactable)
        {
            if (interactable == null)
            {
                return false;
            }

            var playerTransform = ResolvePlayerTransform();
            if (playerTransform == null)
            {
                return false;
            }

            var maxDistance = interactable.EffectiveMaxDistance(DefaultObjectiveFocusDistance) + objectiveOutlineDistancePadding;
            var distance = Vector3.Distance(playerTransform.position, interactable.GetPromptWorldPosition());
            return distance <= maxDistance;
        }

        private Transform ResolvePlayerTransform()
        {
            if (cachedPlayerTransform != null && cachedPlayerTransform.gameObject.activeInHierarchy)
            {
                return cachedPlayerTransform;
            }

            if (Time.time < nextPlayerResolveTime)
            {
                return cachedPlayerTransform;
            }

            nextPlayerResolveTime = Time.time + 0.5f;

            var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                cachedPlayerTransform = taggedPlayer.transform;
                return cachedPlayerTransform;
            }

            cachedPlayerTransform = null;
            return null;
        }

        private void HandleBodyTriggered(InteractionInvocation invocation)
        {
            sceneContext?.ResolveRuntimeReferences();
            if (!MatchesTarget(invocation, sceneContext?.BodyInteractable) ||
                !string.Equals(invocation.OptionId, InspectOptionId, StringComparison.OrdinalIgnoreCase) ||
                !bodyInspectionReady || bodyInspectionCompleted)
            {
                return;
            }

            sceneContext?.BodyInspectionUi?.Open();
        }

        private void HandleDnaMachineTriggered(InteractionInvocation invocation)
        {
            sceneContext?.ResolveRuntimeReferences();
            if (!MatchesTarget(invocation, sceneContext?.DnaMachineInteractable))
            {
                return;
            }

            var optionId = invocation.OptionId ?? string.Empty;
            if (string.Equals(optionId, LookOptionId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(optionId, InspectOptionId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (puzzleReady && !puzzleSolved)
            {
                puzzleOpenRequestedByMachine = true;
                var puzzleUi = sceneContext?.LightPuzzleUi;
                if (puzzleUi != null)
                {
                    interactionDirector?.SetInteractionsLocked(false);
                    puzzleUi.Open();
                    if (puzzleUi.IsOpen)
                    {
                        return;
                    }
                }

                QueueLightPuzzleOpen();
                return;
            }

            if (shrinkReady && !shrinkCompleted &&
                (string.IsNullOrWhiteSpace(optionId) || string.Equals(optionId, UseOptionId, StringComparison.OrdinalIgnoreCase)))
            {
                sceneContext?.ShrinkSequenceController?.PlaySequence();
            }
        }

        private void QueueLightPuzzleOpen()
        {
            if (openLightPuzzleRoutine != null)
            {
                return;
            }

            openLightPuzzleRoutine = StartCoroutine(OpenLightPuzzleNextFrame());
        }

        private IEnumerator OpenLightPuzzleNextFrame()
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                yield return null;

                sceneContext?.ResolveRuntimeReferences();
                if (puzzleSolved || shrinkReady)
                {
                    openLightPuzzleRoutine = null;
                    yield break;
                }

                var puzzleUi = sceneContext?.LightPuzzleUi;
                if (puzzleUi == null)
                {
                    continue;
                }

                if (interactionDirector != null)
                {
                    interactionDirector.SetInteractionsLocked(false);
                }

                puzzleUi.Open();
                if (puzzleUi.IsOpen)
                {
                    openLightPuzzleRoutine = null;
                    yield break;
                }
            }

            openLightPuzzleRoutine = null;
        }

        private void HandleRocketTriggered(InteractionInvocation invocation)
        {
            sceneContext?.ResolveRuntimeReferences();
            if (!MatchesTarget(invocation, sceneContext?.RocketInteractable) ||
                !string.Equals(invocation.OptionId, EnterOptionId, StringComparison.OrdinalIgnoreCase) ||
                !rocketReady || cutsceneCommitted)
            {
                return;
            }

            RaiseSignal(LabStorySignals.RocketEntered, sceneContext?.RocketInteractable?.name ?? "rocket-entered");
        }

        private void HandleCapConversationCompleted(string branchPortId)
        {
            capConversationCompleted = true;
            bodyInspectionReady = true;
            RaiseSignal(LabStorySignals.CapTalked, string.IsNullOrWhiteSpace(branchPortId) ? "cap-talked" : branchPortId);
            sceneContext?.DoorController?.SetUnlocked(true);
            ApplyScenePresentation();
        }

        private void HandleBodyInspectionCompleted()
        {
            if (bodyInspectionCompleted)
            {
                return;
            }

            SyncSessionIdFromPlayer();
            bodyInspectionCompleted = true;
            puzzleReady = true;
            puzzleOpenRequestedByMachine = false;
            RaiseSignal(LabStorySignals.BodyInspected, "body-inspection-complete");
            ApplyScenePresentation();
        }

        private void HandlePuzzleSolved()
        {
            if (puzzleSolved)
            {
                return;
            }

            puzzleSolved = true;
            shrinkReady = true;
            puzzleOpenRequestedByMachine = false;
            RaiseSignal(LabStorySignals.PuzzleSolved, "light-puzzle-solved");
            ApplyScenePresentation();
            if (autoShrinkRoutine != null)
            {
                StopCoroutine(autoShrinkRoutine);
            }

            autoShrinkRoutine = StartCoroutine(AutoPlayShrinkAfterPuzzleSolvedRoutine());
        }

        private void HandlePuzzleAssistanceStateChanged(int failedAttempts, bool assistUnlocked)
        {
            UpdateObjectivePanel();
        }

        private void HandleShrinkCompleted()
        {
            if (shrinkCompleted)
            {
                return;
            }

            shrinkCompleted = true;
            rocketReady = true;
            RaiseSignal(LabStorySignals.ShrinkConfirmed, "shrink-complete");
            ApplyScenePresentation();
        }

        private void HandleFinalCutsceneCompleted()
        {
            RaiseSignal(LabStorySignals.CutsceneCompleted, "cutscene-complete");
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
            else
            {
                SyncSessionIdFromPlayer();
            }

            npcSignalBridge?.SetSessionId(currentSessionId);
            sceneContext?.CapConversationDirector?.SetSessionId(currentSessionId);

            if (notification.Kind == StoryGraphNotificationKind.Started || notification.Kind == StoryGraphNotificationKind.Loaded)
            {
                if (openLightPuzzleRoutine != null)
                {
                    StopCoroutine(openLightPuzzleRoutine);
                    openLightPuzzleRoutine = null;
                }

                if (autoShrinkRoutine != null)
                {
                    StopCoroutine(autoShrinkRoutine);
                    autoShrinkRoutine = null;
                }

                bodyInspectionReady = false;
                puzzleReady = false;
                shrinkReady = false;
                rocketReady = false;
                cutsceneCommitted = false;
                capConversationCompleted = false;
                bodyInspectionCompleted = false;
                puzzleSolved = false;
                shrinkCompleted = false;
                puzzleOpenRequestedByMachine = false;
                sceneTransition?.ResetRequest();
                sceneContext?.LightPuzzleUi?.ResetPuzzleState();
                sceneContext?.BodyInspectionUi?.HideImmediate();
                sceneContext?.LightPuzzleUi?.HideImmediate();
                sceneContext?.FinalCutsceneController?.ResetSequence();
                sceneContext?.DoorController?.SetUnlocked(false);
                sceneContext?.CapNpcController?.ClearManualFollowOverride();
                sceneContext?.CapNpcController?.SetFollowPlayer(false);
                ApplyScenePresentation();
            }
        }

        private IEnumerator AutoPlayShrinkAfterPuzzleSolvedRoutine()
        {
            sceneContext?.ResolveRuntimeReferences();
            var puzzleUi = sceneContext?.LightPuzzleUi;
            if (puzzleUi != null && puzzleUi.IsOpen)
            {
                yield return new WaitForSecondsRealtime(0.28f);
                puzzleUi.Close();
                yield return new WaitForSecondsRealtime(0.12f);
                if (puzzleUi.IsOpen)
                {
                    puzzleUi.HideImmediate();
                }
            }

            var shrinkController = sceneContext?.ShrinkSequenceController;
            if (shrinkController != null && !shrinkCompleted && !shrinkController.IsPlaying)
            {
                shrinkController.PlaySequence();
            }

            autoShrinkRoutine = null;
        }

        private void HandleStateChanged(StoryStateChangedPayload payload)
        {
            if (payload == null || payload.MachineId != StoryMachineId)
            {
                return;
            }

            SyncSessionIdFromPlayer();

            bodyInspectionReady = IsAtOrBeyond(payload.NextStateId, "BodyInspectionReady", "BodyInspected", "PuzzleReady", "PuzzleSolved", "ShrinkReady", "Shrunk", "RocketReady", "CutsceneCommitted");
            puzzleReady = IsAtOrBeyond(payload.NextStateId, "PuzzleReady", "PuzzleSolved", "ShrinkReady", "Shrunk", "RocketReady", "CutsceneCommitted");
            shrinkReady = IsAtOrBeyond(payload.NextStateId, "ShrinkReady", "Shrunk", "RocketReady", "CutsceneCommitted");
            rocketReady = IsAtOrBeyond(payload.NextStateId, "RocketReady", "CutsceneCommitted");
            cutsceneCommitted = string.Equals(payload.NextStateId, "CutsceneCommitted", StringComparison.Ordinal);
            capConversationCompleted |= bodyInspectionReady || puzzleReady || shrinkReady || rocketReady;
            bodyInspectionCompleted |= puzzleReady || shrinkReady || rocketReady || cutsceneCommitted;
            puzzleSolved |= shrinkReady || rocketReady || cutsceneCommitted;
            shrinkCompleted |= rocketReady || cutsceneCommitted;

            ApplyScenePresentation();
            UpdateObjectiveVisuals();

            if (cutsceneCommitted)
            {
                sceneContext?.FinalCutsceneController?.PlayEndingSequence();
            }
        }

        private void QueuePuzzleOpenIfNeeded()
        {
            if (!puzzleOpenRequestedByMachine || !puzzleReady || puzzleSolved || shrinkReady || sceneContext == null || sceneContext.StartupCooldownActive)
            {
                return;
            }

            sceneContext.ResolveRuntimeReferences();
            var puzzleUi = sceneContext.LightPuzzleUi;
            if (puzzleUi == null)
            {
                return;
            }

            if (puzzleUi.IsOpen)
            {
                puzzleOpenRequestedByMachine = false;
                return;
            }

            if (interactionDirector != null && interactionDirector.IsInspectionOpen)
            {
                return;
            }

            if (openLightPuzzleRoutine == null)
            {
                QueueLightPuzzleOpen();
            }
        }

        private void UpdateObjectivePanel()
        {
            if (sceneContext?.ObjectivePanelUi == null)
            {
                return;
            }

            if (sceneContext.LightPuzzleUi != null && sceneContext.LightPuzzleUi.IsOpen)
            {
                sceneContext.ObjectivePanelUi.SetCompactMode(true);
            }
            else
            {
                sceneContext.ObjectivePanelUi.SetCompactMode(false);
            }

            var stage = LabObjectivePanelUi.ObjectiveStage.GreetCap;
            var title = "Mission Checklist";

            if (sceneContext.StartupCooldownActive)
            {
                stage = LabObjectivePanelUi.ObjectiveStage.EnteringLab;
                title = "Briefing";
                currentObjectiveText = "Get ready. CAP will brief you in a moment.";
            }
            else if (sceneContext.CapConversationDirector != null && sceneContext.CapConversationDirector.ConversationRunning)
            {
                stage = LabObjectivePanelUi.ObjectiveStage.GreetCap;
                title = "Briefing";
                currentObjectiveText = "Listen to CAP's briefing.";
            }
            else if (!capConversationCompleted)
            {
                stage = LabObjectivePanelUi.ObjectiveStage.GreetCap;
                title = "Mission";
                currentObjectiveText = "Talk to CAP.";
            }
            else if (bodyInspectionReady && !bodyInspectionCompleted)
            {
                stage = LabObjectivePanelUi.ObjectiveStage.InspectBody;
                title = "Mission";
                currentObjectiveText = "Inspect the body model on the lab table.";
            }
            else if (puzzleReady && !puzzleSolved)
            {
                stage = LabObjectivePanelUi.ObjectiveStage.RouteLight;
                title = "Mission";
                currentObjectiveText = "Route the light to power the shrink machine.";
            }
            else if (shrinkReady && !shrinkCompleted)
            {
                stage = LabObjectivePanelUi.ObjectiveStage.UseShrinkMachine;
                title = "Mission";
                currentObjectiveText = "Use the shrink machine.";
            }
            else if (rocketReady && !cutsceneCommitted)
            {
                stage = LabObjectivePanelUi.ObjectiveStage.EnterRocket;
                title = "Mission";
                currentObjectiveText = "Enter the mini rocket.";
            }
            else
            {
                stage = LabObjectivePanelUi.ObjectiveStage.MissionReady;
                title = "Mission";
                currentObjectiveText = "Follow CAP's lead.";
            }

            sceneContext.ObjectivePanelUi.ShowStage(stage, currentObjectiveText, title);
            sceneContext.ObjectivePanelUi.SetHint(BuildObjectiveHint(stage));
        }

        private string BuildObjectiveHint(LabObjectivePanelUi.ObjectiveStage stage)
        {
            if (stage != LabObjectivePanelUi.ObjectiveStage.RouteLight || sceneContext?.LightPuzzleUi == null || puzzleSolved)
            {
                return string.Empty;
            }

            if (!sceneContext.LightPuzzleUi.CanRequestCapSolve)
            {
                return string.Empty;
            }

            return "Hint: Stuck? Ask CAP in Free chat to solve the light puzzle for you.";
        }

        private void RaiseSignal(string signalId, string payload)
        {
            if (channels == null || string.IsNullOrWhiteSpace(currentSessionId) || string.IsNullOrWhiteSpace(signalId))
            {
                return;
            }

            channels.ExternalSignals?.Raise(new StoryExternalSignal
            {
                SessionId = currentSessionId,
                SignalId = signalId,
                Payload = payload
            });
        }

        private void SyncSessionIdFromPlayer()
        {
            if (storyFlowPlayer != null && !string.IsNullOrWhiteSpace(storyFlowPlayer.SessionId))
            {
                currentSessionId = storyFlowPlayer.SessionId;
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

        private static void EnsurePromptAndOutline(InteractableItem interactable)
        {
            if (interactable == null)
            {
                return;
            }

            interactable.promptAnchor = interactable.promptAnchor != null ? interactable.promptAnchor : interactable.transform;
            interactable.inspectionSourceRoot = interactable.inspectionSourceRoot != null ? interactable.inspectionSourceRoot : interactable.transform;
            interactable.outline = interactable.outline != null ? interactable.outline : interactable.GetComponent<SelectableOutline>() ?? interactable.gameObject.AddComponent<SelectableOutline>();
        }

        private static Transform EnsureFloatingPromptAnchor(InteractableItem interactable, string anchorName, float heightPadding, float minimumLocalY)
        {
            if (interactable == null)
            {
                return null;
            }

            var root = interactable.transform;
            Transform anchor = interactable.promptAnchor;
            if (anchor == null || anchor == root || anchor.parent != root)
            {
                anchor = root.Find(anchorName);
                if (anchor == null)
                {
                    var anchorObject = new GameObject(anchorName);
                    anchor = anchorObject.transform;
                    anchor.SetParent(root, false);
                }
            }

            if (TryGetCombinedRendererBounds(root, out var bounds))
            {
                var worldPosition = new Vector3(bounds.center.x, bounds.max.y + Mathf.Max(heightPadding, 0.08f), bounds.center.z);
                anchor.localPosition = root.InverseTransformPoint(worldPosition);
            }
            else
            {
                anchor.localPosition = new Vector3(0f, minimumLocalY, 0f);
            }

            if (anchor.localPosition.y < minimumLocalY)
            {
                anchor.localPosition = new Vector3(anchor.localPosition.x, minimumLocalY, anchor.localPosition.z);
            }

            anchor.localRotation = Quaternion.identity;
            anchor.localScale = Vector3.one;
            return anchor;
        }

        private static bool TryGetCombinedRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
            {
                return false;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (bounds.size == Vector3.zero)
                {
                    bounds = renderer.bounds;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds.size.sqrMagnitude > 0.0001f;
        }

        private void ForceHideVitalsBars()
        {
            var documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            HudDocumentsBuffer.Clear();
            for (var index = 0; index < documents.Length; index++)
            {
                var document = documents[index];
                if (document == null || document.rootVisualElement == null)
                {
                    continue;
                }

                HudDocumentsBuffer.Add(document);
            }

            for (var index = 0; index < HudDocumentsBuffer.Count; index++)
            {
                var root = HudDocumentsBuffer[index].rootVisualElement;
                HideBar(root, "player-health-bar");
                HideBar(root, "player-stamina-bar");
            }
        }

        private static void HideBar(VisualElement root, string name)
        {
            var element = root?.Q<VisualElement>(name);
            if (element != null)
            {
                element.style.display = DisplayStyle.None;
                element.style.visibility = Visibility.Hidden;
            }
        }

        private static bool MatchesTarget(InteractionInvocation invocation, InteractableItem target)
        {
            if (invocation?.Target == null)
            {
                return false;
            }

            if (target != null && invocation.Target == target)
            {
                return true;
            }

            var fallbackStoryId = target == null
                ? string.Empty
                : string.Equals(target.storyId, BodyStoryId, StringComparison.OrdinalIgnoreCase)
                    ? BodyStoryId
                    : string.Equals(target.storyId, DnaMachineStoryId, StringComparison.OrdinalIgnoreCase)
                        ? DnaMachineStoryId
                        : string.Equals(target.storyId, RocketStoryId, StringComparison.OrdinalIgnoreCase)
                            ? RocketStoryId
                            : target.storyId;

            if (!string.IsNullOrWhiteSpace(fallbackStoryId) &&
                string.Equals(invocation.Target.storyId, fallbackStoryId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (target == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(target.displayName) &&
                string.Equals(invocation.Target.displayName, target.displayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(invocation.Target.gameObject.name, target.gameObject.name, StringComparison.OrdinalIgnoreCase);
        }

        private static void RegisterOption(InteractableItem interactable, Action<InteractionInvocation> handler)
        {
            if (interactable == null)
            {
                return;
            }

            interactable.OptionTriggered -= handler;
            interactable.OptionTriggered += handler;
        }

        private static void UnregisterOption(InteractableItem interactable, Action<InteractionInvocation> handler)
        {
            if (interactable == null)
            {
                return;
            }

            interactable.OptionTriggered -= handler;
        }

        private static bool IsAtOrBeyond(string stateId, params string[] validStates)
        {
            if (string.IsNullOrWhiteSpace(stateId) || validStates == null)
            {
                return false;
            }

            for (var index = 0; index < validStates.Length; index++)
            {
                if (string.Equals(stateId, validStates[index], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
