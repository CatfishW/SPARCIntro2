using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ItemInteraction;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using ModularStoryFlow.Runtime.Player;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class LabCapConversationDirector : MonoBehaviour
    {
        [SerializeField] private StoryFlowChannels channels;
        [SerializeField] private StoryFlowPlayer storyFlowPlayer;
        [SerializeField] private StoryNpcAgent capNpc;
        [SerializeField] private LabCapNpcController capController;
        [SerializeField] private ClassroomPlayerControlLock controlLock;
        [SerializeField] private LabCameraFocusController cameraFocusController;
        [SerializeField] private LabSceneContext sceneContext;
        [SerializeField, Min(0.05f)] private float linePaddingSeconds = 0.18f;
        [SerializeField] private ClassroomLlmService llmService;
        [SerializeField] private ClassroomNpcFreeChatUi freeChatUi;
        [SerializeField] private ClassroomNpcActionExecutor npcActionExecutor;
        [SerializeField] private InteractionDirector interactionDirector;
        [SerializeField, TextArea(4, 12)] private string freeChatSystemPrompt =
            "You are CAP, a kind science guide helping a kid player inside a lab mission. " +
            "Use easy K-12 words, stay warm and encouraging, and keep replies to 1-2 short sentences. " +
            "Always output exactly this format:\n" +
            "SAY: <what CAP says>\n" +
            "ACTIONS: <comma-separated actions or none>\n" +
            "Allowed actions: open_door,dance,jump,surprised,follow_player,stop_following,none.";
        [SerializeField, Min(2f)] private float llmRequestTimeoutSeconds = 30f;
        [SerializeField, Min(8)] private int llmFreeChatMaxTokens = 132;

        private Coroutine activeConversationRoutine;
        private string currentSessionId = string.Empty;
        private string pendingChoiceRequestId = string.Empty;
        private bool pendingChoiceResolved;
        private string pendingChoicePortId = string.Empty;
        private string resolvedChoicePortId = string.Empty;
        private bool introBriefingCompleted;
        private bool capConversationCompleted;
        private bool bodyInspectionReady;
        private bool bodyInspectionCompleted;
        private bool puzzleReady;
        private bool puzzleSolved;
        private bool shrinkReady;
        private bool shrinkCompleted;
        private bool rocketReady;
        private readonly Queue<string> streamingDeltaQueue = new Queue<string>(64);
        private float lastDialogueHideReadyTime;

        public event Action<string> ConversationCompleted;

        public bool ConversationRunning => activeConversationRoutine != null;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            channels?.ChoiceSelections?.Register(HandleChoiceSelection);
            if (capNpc != null)
            {
                capNpc.InteractionTriggered -= HandleCapInteraction;
                capNpc.InteractionTriggered += HandleCapInteraction;
            }
        }

        private void OnDisable()
        {
            channels?.ChoiceSelections?.Unregister(HandleChoiceSelection);
            if (capNpc != null)
            {
                capNpc.InteractionTriggered -= HandleCapInteraction;
            }

            if (activeConversationRoutine != null)
            {
                StopCoroutine(activeConversationRoutine);
            }

            CleanupConversationState();
        }

        public void Configure(StoryFlowChannels storyChannels)
        {
            if (isActiveAndEnabled && channels != null)
            {
                channels.ChoiceSelections?.Unregister(HandleChoiceSelection);
            }

            channels = storyChannels;
            ResolveReferences();
            if (isActiveAndEnabled && channels != null)
            {
                channels.ChoiceSelections?.Register(HandleChoiceSelection);
            }

            RefreshCapInteractionSubscription();
        }

        public void SetSessionId(string sessionId)
        {
            currentSessionId = sessionId ?? string.Empty;
        }

        public void SetMissionProgress(
            bool hasTalkedToCap,
            bool canInspectBody,
            bool hasInspectedBody,
            bool canSolvePuzzle,
            bool hasSolvedPuzzle,
            bool canShrink,
            bool hasShrunk,
            bool canEnterRocket)
        {
            capConversationCompleted = hasTalkedToCap;
            bodyInspectionReady = canInspectBody;
            bodyInspectionCompleted = hasInspectedBody;
            puzzleReady = canSolvePuzzle;
            puzzleSolved = hasSolvedPuzzle;
            shrinkReady = canShrink;
            shrinkCompleted = hasShrunk;
            rocketReady = canEnterRocket;
        }

        public void StartConversation()
        {
            ResolveReferences();
            if (ConversationRunning || channels == null)
            {
                return;
            }

            activeConversationRoutine = StartCoroutine(RunConversationRoutine());
        }

        public void StartIntroBriefing()
        {
            ResolveReferences();
            if (ConversationRunning || channels == null || introBriefingCompleted)
            {
                return;
            }

            activeConversationRoutine = StartCoroutine(RunIntroBriefingRoutine());
        }

        private void HandleCapInteraction(StoryNpcInteractionPayload payload)
        {
            if (payload == null || payload.Agent != capNpc || !string.Equals(payload.OptionId, "talk", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            StartConversation();
        }

        private IEnumerator RunConversationRoutine()
        {
            string branch = null;
            try
            {
                SyncSessionId();
                controlLock?.Acquire(unlockCursor: false);
                interactionDirector?.SetInteractionsLocked(true);
                cameraFocusController?.SetConversationTarget(capNpc != null ? capNpc.transform : transform);
                cameraFocusController?.BeginConversation();
                capController?.SetConversationMode(true);

                if (!introBriefingCompleted)
                {
                    yield return SpeakRoutine("CAP", "Hi! I'm CAP. Today we get to take a super tiny science trip inside the body.", 2.8f);
                    yield return SpeakRoutine("CAP", "First, let's look at the body model. Then we can power the shrink machine and board the rocket.", 3f);
                }
                else
                {
                    yield return SpeakRoutine("CAP", BuildStageGreeting(), 2.1f);
                }

                yield return RequestChoiceRoutine(
                    BuildPrompt(),
                    BuildChoiceOptions());

                branch = string.IsNullOrWhiteSpace(resolvedChoicePortId) ? ResolveDefaultChoicePort() : resolvedChoicePortId;

                if (string.Equals(branch, "free_chat", StringComparison.Ordinal))
                {
                    yield return RunFreeChatRoutine();
                    branch = "free_chat";
                }
                else
                {
                    yield return RunScriptedBranchRoutine(branch);
                }

                capController?.PlayReaction();
                capConversationCompleted = true;
            }
            finally
            {
                CleanupConversationState();
            }

            ConversationCompleted?.Invoke(branch ?? "brave");
        }

        private IEnumerator RunIntroBriefingRoutine()
        {
            try
            {
                SyncSessionId();
                controlLock?.Acquire(unlockCursor: false);
                interactionDirector?.SetInteractionsLocked(true);
                cameraFocusController?.SetConversationTarget(capNpc != null ? capNpc.transform : transform);
                cameraFocusController?.BeginConversation();
                capController?.SetConversationMode(true);

                yield return SpeakRoutine("CAP", "Hi! I'm CAP. Welcome to the lab.", 2.2f);
                yield return SpeakRoutine("CAP", "When you're ready, talk to me and we will start our tiny body mission together.", 2.8f);

                introBriefingCompleted = true;
                RaiseExternalSignal(LabStorySignals.IntroBriefingCompleted, "intro-briefing-complete");
            }
            finally
            {
                CleanupConversationState();
            }
        }

        private void CleanupConversationState()
        {
            capController?.SetTalking(false);
            capController?.SetConversationMode(false);
            cameraFocusController?.ClearFocus();
            interactionDirector?.SetInteractionsLocked(false);
            controlLock?.Release();
            activeConversationRoutine = null;
            pendingChoiceRequestId = string.Empty;
            pendingChoicePortId = string.Empty;
            pendingChoiceResolved = false;
        }

        private IEnumerator RunScriptedBranchRoutine(string branch)
        {
            var normalized = string.IsNullOrWhiteSpace(branch) ? ResolveDefaultChoicePort() : branch;
            switch (normalized)
            {
                case "curious":
                    yield return SpeakRoutine("You", bodyInspectionCompleted ? "What happens after that?" : "How small will we be?", 1.8f);
                    yield return SpeakRoutine("CAP", bodyInspectionCompleted
                        ? "After the puzzle, we power the machine and shrink for the rocket ride."
                        : "Small enough for a mini rocket ride, but still safe for a smart explorer.", 2.7f);
                    break;

                case "careful":
                    yield return SpeakRoutine("You", puzzleReady && !puzzleSolved ? "What should we do carefully next?" : "Is it safe?", 1.7f);
                    yield return SpeakRoutine("CAP", puzzleReady && !puzzleSolved
                        ? "We bend the light one step at a time so the machine gets power safely."
                        : "Yes. We will go step by step, and I will guide you the whole time.", 2.8f);
                    break;

                case "status":
                    yield return SpeakRoutine("You", "What is our next step?", 1.6f);
                    yield return SpeakRoutine("CAP", BuildNextStepLine(), 2.8f);
                    break;

                default:
                    normalized = "brave";
                    yield return SpeakRoutine("You", rocketReady ? "Let's finish this mission!" : "Let's do it!", 1.5f);
                    yield return SpeakRoutine("CAP", rocketReady
                        ? "You are ready. The rocket is waiting for your launch choice."
                        : "That's the spirit! Brave explorers still make careful choices.", 2.4f);
                    break;
            }

            if (!rocketReady)
            {
                yield return SpeakRoutine("CAP", BuildNextStepLine(), 2.8f);
            }
        }

        private IEnumerator SpeakRoutine(string speaker, string body, float autoAdvanceDelaySeconds)
        {
            if (channels == null || string.IsNullOrWhiteSpace(body))
            {
                yield break;
            }

            SyncSessionId();
            var isCapSpeaker = string.Equals(speaker, "CAP", StringComparison.OrdinalIgnoreCase);
            capController?.SetTalking(isCapSpeaker);
            channels.DialogueRequests?.Raise(new StoryDialogueRequest
            {
                SessionId = currentSessionId,
                RequestId = Guid.NewGuid().ToString("N"),
                SpeakerId = speaker,
                SpeakerDisplayName = speaker,
                Body = body,
                AutoAdvance = true,
                AutoAdvanceDelaySeconds = autoAdvanceDelaySeconds
            });

            cameraFocusController?.FocusOnSpeaker(speaker);

            var holdTime = autoAdvanceDelaySeconds + linePaddingSeconds;
            lastDialogueHideReadyTime = Time.unscaledTime + holdTime;
            yield return new WaitForSecondsRealtime(holdTime);
            if (isCapSpeaker)
            {
                capController?.SetTalking(false);
            }
        }

        private IEnumerator RunFreeChatRoutine()
        {
            ResolveReferences();
            if (capNpc == null || freeChatUi == null || llmService == null)
            {
                yield return SpeakRoutine("CAP", "My free chat is not ready right now. Let's use the mission choices instead.", 2.6f);
                yield break;
            }

            var history = new List<LlmChatMessage>(12)
            {
                new LlmChatMessage("system", BuildNpcSystemPrompt())
            };

            var closeRequested = false;
            string pendingInput = null;
            void HandleSend(string text) => pendingInput = text;
            void HandleClosed() => closeRequested = true;

            freeChatUi.SendRequested += HandleSend;
            freeChatUi.Closed += HandleClosed;
            freeChatUi.Open(capNpc.NpcDisplayName);
            freeChatUi.SetInputVisible(false, immediate: true);
            yield return WaitForDialogueUiToVanish();
            freeChatUi.SetInputVisible(true, immediate: false);

            try
            {
                while (!closeRequested)
                {
                    if (!string.IsNullOrWhiteSpace(pendingInput))
                    {
                        var line = pendingInput.Trim();
                        pendingInput = null;
                        if (line.Equals("leave", StringComparison.OrdinalIgnoreCase) ||
                            line.Equals("bye", StringComparison.OrdinalIgnoreCase) ||
                            line.Equals("exit", StringComparison.OrdinalIgnoreCase))
                        {
                            closeRequested = true;
                            continue;
                        }

                        yield return RunFreeChatExchange(history, line);
                    }

                    yield return null;
                }
            }
            finally
            {
                freeChatUi.SendRequested -= HandleSend;
                freeChatUi.Closed -= HandleClosed;
                freeChatUi.HideImmediate();
            }
        }

        private IEnumerator RunFreeChatExchange(List<LlmChatMessage> history, string playerText)
        {
            if (history == null || string.IsNullOrWhiteSpace(playerText) || capNpc == null || freeChatUi == null || llmService == null)
            {
                yield break;
            }

            history.Add(new LlmChatMessage("user", playerText));
            freeChatUi.SetInputVisible(false, immediate: false);
            yield return SpeakRoutine("You", playerText, ResolveDialogueDurationSeconds(playerText, 1.2f, 2.2f));
            freeChatUi.SetBusy(true);

            lock (streamingDeltaQueue)
            {
                streamingDeltaQueue.Clear();
            }

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Mathf.Max(8f, llmRequestTimeoutSeconds)));
            Task<string> requestTask = null;
            string requestFailureMessage = null;
            try
            {
                requestTask = llmService.StreamChatAsync(
                    history,
                    delta =>
                    {
                        if (string.IsNullOrEmpty(delta))
                        {
                            return;
                        }

                        lock (streamingDeltaQueue)
                        {
                            streamingDeltaQueue.Enqueue(delta);
                        }
                    },
                    llmFreeChatMaxTokens,
                    0.62f,
                    cancellation.Token);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LabCapConversationDirector] Failed to issue LLM request: {ex.Message}", this);
                requestFailureMessage = "I could not reach my chat brain. Try again in a moment.";
            }

            if (!string.IsNullOrWhiteSpace(requestFailureMessage) || requestTask == null)
            {
                freeChatUi.SetBusy(false);
                if (!string.IsNullOrWhiteSpace(requestFailureMessage))
                {
                    yield return SpeakRoutine("CAP", requestFailureMessage, ResolveDialogueDurationSeconds(requestFailureMessage, 1.8f, 2.8f));
                }

                yield return WaitForDialogueUiToVanish();
                freeChatUi.SetInputVisible(true, immediate: false);

                yield break;
            }

            while (!requestTask.IsCompleted)
            {
                yield return null;
            }

            if (requestTask.IsFaulted)
            {
                freeChatUi.SetBusy(false);
                const string faultMessage = "Connection failed. Try a shorter question.";
                yield return SpeakRoutine("CAP", faultMessage, ResolveDialogueDurationSeconds(faultMessage, 1.8f, 2.8f));
                yield return WaitForDialogueUiToVanish();
                freeChatUi.SetInputVisible(true, immediate: false);
                yield break;
            }

            var raw = requestTask.Result ?? string.Empty;
            var structured = llmService.ParseStructuredNpcReply(raw);
            var normalizedActions = BuildReliableLabActions(playerText, structured.Actions);
            var cleanedSpeech = ClampSpeechLength(structured.Say, 180);

            freeChatUi.SetBusy(false);

            if (!string.IsNullOrWhiteSpace(cleanedSpeech))
            {
                history.Add(new LlmChatMessage("assistant", $"SAY: {cleanedSpeech}\nACTIONS: {string.Join(",", normalizedActions)}"));
                yield return SpeakRoutine("CAP", cleanedSpeech, ResolveDialogueDurationSeconds(cleanedSpeech, 1.8f, 3.2f));
            }

            if (normalizedActions.Count > 0)
            {
                yield return ExecuteLabActionsRoutine(normalizedActions);
            }

            yield return WaitForDialogueUiToVanish();
            freeChatUi.SetInputVisible(true, immediate: false);
        }

        private static float ResolveDialogueDurationSeconds(string text, float minSeconds, float maxSeconds)
        {
            var normalizedLength = string.IsNullOrWhiteSpace(text) ? 0 : text.Trim().Length;
            return Mathf.Clamp(normalizedLength * 0.032f, minSeconds, maxSeconds);
        }

        private IEnumerator WaitForDialogueUiToVanish()
        {
            var readyTime = Mathf.Max(lastDialogueHideReadyTime, Time.unscaledTime);
            while (Time.unscaledTime < readyTime)
            {
                yield return null;
            }
        }

        private string BuildNpcSystemPrompt()
        {
            var prompt = new StringBuilder(640);
            prompt.AppendLine(freeChatSystemPrompt);
            prompt.AppendLine("Current speaker: CAP (cap)");
            prompt.AppendLine("Scene context: sci-fi lab before a mini rocket mission into a human body.");
            prompt.Append("Current mission step: ").AppendLine(BuildMissionSnapshot());
            prompt.AppendLine("Stay kid-friendly, short, and encouraging.");
            prompt.AppendLine("If the player asks what to do next, answer with the current next mission step.");
            prompt.AppendLine("Never use scary medical language.");
            prompt.AppendLine("Use ACTIONS: open_door if the player asks CAP to help with the door.");
            prompt.AppendLine("Use ACTIONS: dance if the player asks CAP to dance.");
            prompt.AppendLine("Use ACTIONS: follow_player if the player asks CAP to follow.");
            prompt.AppendLine("Use ACTIONS: stop_following if the player asks CAP to stop following or wait.");
            return prompt.ToString();
        }

        private IEnumerator ExecuteLabActionsRoutine(IReadOnlyList<string> actions)
        {
            if (actions == null || actions.Count == 0)
            {
                yield break;
            }

            var genericActions = new List<string>(actions.Count);
            for (var index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                if (string.IsNullOrWhiteSpace(action))
                {
                    continue;
                }

                switch (action)
                {
                    case "open_door":
                        ResolveReferences();
                        sceneContext?.DoorController?.OpenForCapAssistance();
                        break;

                    case "dance":
                        capController?.PlayDance();
                        if (capController != null)
                        {
                            yield return new WaitForSeconds(capController.DanceDuration);
                        }
                        break;

                    case "follow_player":
                        capController?.SetManualFollowOverride(true);
                        break;

                    case "stop_following":
                        capController?.SetManualFollowOverride(false);
                        break;

                    default:
                        genericActions.Add(action);
                        break;
                }

                yield return null;
            }

            if (genericActions.Count > 0 && npcActionExecutor != null)
            {
                yield return npcActionExecutor.ExecuteActionsRoutine(capNpc, genericActions);
            }
        }

        private void RaiseExternalSignal(string signalId, string payload)
        {
            if (channels == null || string.IsNullOrWhiteSpace(signalId))
            {
                return;
            }

            SyncSessionId();
            channels.ExternalSignals?.Raise(new StoryExternalSignal
            {
                SessionId = currentSessionId,
                SignalId = signalId,
                Payload = payload ?? string.Empty
            });
        }

        private static List<string> BuildReliableLabActions(string playerText, IReadOnlyList<string> modelActions)
        {
            var normalized = NormalizeLabActions(modelActions);
            var inferred = InferActionsFromPlayerText(playerText);
            for (var index = 0; index < inferred.Count; index++)
            {
                var action = inferred[index];
                if (!normalized.Contains(action))
                {
                    normalized.Add(action);
                }
            }

            return normalized;
        }

        private static List<string> InferActionsFromPlayerText(string playerText)
        {
            var inferred = new List<string>(4);
            var text = (playerText ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text))
            {
                return inferred;
            }

            if (text.Contains("door") && (text.Contains("open") || text.Contains("unlock") || text.Contains("help")))
            {
                inferred.Add("open_door");
            }

            if (text.Contains("dance"))
            {
                inferred.Add("dance");
            }

            if (text.Contains("follow") || text.Contains("come with me"))
            {
                inferred.Add("follow_player");
            }

            if ((text.Contains("stop") && text.Contains("follow")) || text.Contains("wait here") || text.Contains("stay here"))
            {
                inferred.Add("stop_following");
            }

            return inferred;
        }

        private static List<string> NormalizeLabActions(IReadOnlyList<string> rawActions)
        {
            var normalized = new List<string>(4);
            if (rawActions == null)
            {
                return normalized;
            }

            for (var index = 0; index < rawActions.Count; index++)
            {
                var canonical = NormalizeLabAction(rawActions[index]);
                if (string.IsNullOrWhiteSpace(canonical) || normalized.Contains(canonical))
                {
                    continue;
                }

                normalized.Add(canonical);
            }

            return normalized;
        }

        private static string NormalizeLabAction(string rawAction)
        {
            var token = (rawAction ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(token) || token == "none")
            {
                return string.Empty;
            }

            return token switch
            {
                "open_door" or "unlock_door" or "open_the_door" or "door" or "help_with_door" => "open_door",
                "dance" or "do_a_dance" => "dance",
                "jump" => "jump",
                "surprised" or "surprise" => "surprised",
                "follow_player" or "follow_me" or "follow" or "come_with_me" or "follow_player_short" => "follow_player",
                "stop_following" or "stop_follow" or "stop" or "wait_here" or "stay_here" => "stop_following",
                _ => token
            };
        }

        private string BuildMissionSnapshot()
        {
            if (!capConversationCompleted)
            {
                return "meet CAP and hear the mission briefing";
            }

            if (bodyInspectionReady && !bodyInspectionCompleted)
            {
                return "inspect the body model together";
            }

            if (puzzleReady && !puzzleSolved)
            {
                return "route the light puzzle to power the shrink machine";
            }

            if (shrinkReady && !shrinkCompleted)
            {
                return "use the shrink machine";
            }

            if (rocketReady)
            {
                return "board the mini rocket";
            }

            return "stay ready for CAP's next instruction";
        }

        private string BuildStageGreeting()
        {
            if (rocketReady)
            {
                return "The rocket is ready. Want a quick plan, a free chat, or a final pep talk?";
            }

            if (shrinkReady && !shrinkCompleted)
            {
                return "The shrink machine is ready now. Want the plan again or do you have a question?";
            }

            if (puzzleReady && !puzzleSolved)
            {
                return "Nice job on the body check. Next we need to bend the light path and power the machine.";
            }

            if (bodyInspectionReady && !bodyInspectionCompleted)
            {
                return "Let us study the body model first. I can explain the plan or answer a quick question.";
            }

            return "I am here if you want the mission plan, a quick answer, or free chat.";
        }

        private string BuildPrompt()
        {
            return rocketReady
                ? "What do you want from CAP?"
                : "How do you want to talk with CAP?";
        }

        private IReadOnlyList<StoryChoiceOption> BuildChoiceOptions()
        {
            return new[]
            {
                new StoryChoiceOption { PortId = "status", Label = "What next?", IsAvailable = true },
                new StoryChoiceOption { PortId = "curious", Label = bodyInspectionCompleted ? "What happens after that?" : "How small will we be?", IsAvailable = true },
                new StoryChoiceOption { PortId = "careful", Label = puzzleReady && !puzzleSolved ? "How do we do the puzzle?" : "Is it safe?", IsAvailable = true },
                new StoryChoiceOption { PortId = "free_chat", Label = "Free chat", IsAvailable = llmService != null && freeChatUi != null },
                new StoryChoiceOption { PortId = "brave", Label = rocketReady ? "Let's launch!" : "Let's do it!", IsAvailable = true }
            };
        }

        private string ResolveDefaultChoicePort()
        {
            if (rocketReady)
            {
                return "brave";
            }

            return puzzleReady ? "status" : "brave";
        }

        private string BuildNextStepLine()
        {
            if (!capConversationCompleted)
            {
                return "Talk with me first, then I will guide the mission.";
            }

            if (bodyInspectionReady && !bodyInspectionCompleted)
            {
                return "Go inspect the body model on the lab table. I will stay close while you check it.";
            }

            if (puzzleReady && !puzzleSolved)
            {
                return "Head to the shrink machine and route the light so we can power it up.";
            }

            if (shrinkReady && !shrinkCompleted)
            {
                return "The machine is powered. Use it so we can shrink safely together.";
            }

            if (rocketReady)
            {
                return "The mini rocket is ready. When you choose to enter it, the final mission begins.";
            }

            return "Stay with me and we will keep the mission moving.";
        }

        private static string ClampSpeechLength(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            if (trimmed.Length <= maxLength)
            {
                return trimmed;
            }

            return trimmed.Substring(0, maxLength).TrimEnd() + "...";
        }

        private IEnumerator RequestChoiceRoutine(string prompt, IReadOnlyList<StoryChoiceOption> options)
        {
            SyncSessionId();
            pendingChoiceRequestId = Guid.NewGuid().ToString("N");
            pendingChoicePortId = string.Empty;
            pendingChoiceResolved = false;
            resolvedChoicePortId = string.Empty;

            var request = new StoryChoiceRequest
            {
                SessionId = currentSessionId,
                RequestId = pendingChoiceRequestId,
                Prompt = prompt,
                Options = new List<StoryChoiceOption>()
            };

            if (options != null)
            {
                for (var index = 0; index < options.Count; index++)
                {
                    var option = options[index];
                    if (option == null)
                    {
                        continue;
                    }

                    request.Options.Add(new StoryChoiceOption
                    {
                        PortId = option.PortId,
                        Label = option.Label,
                        IsAvailable = option.IsAvailable
                    });
                }
            }

            channels.ChoiceRequests?.Raise(request);
            while (!pendingChoiceResolved)
            {
                yield return null;
            }

            resolvedChoicePortId = pendingChoicePortId;
            pendingChoiceRequestId = string.Empty;
            pendingChoicePortId = string.Empty;
            pendingChoiceResolved = false;
        }

        private void HandleChoiceSelection(StoryChoiceSelection selection)
        {
            if (selection == null || string.IsNullOrWhiteSpace(pendingChoiceRequestId))
            {
                return;
            }

            if (!string.Equals(selection.RequestId, pendingChoiceRequestId, StringComparison.Ordinal) ||
                !string.Equals(selection.SessionId, currentSessionId, StringComparison.Ordinal))
            {
                return;
            }

            pendingChoicePortId = selection.PortId ?? string.Empty;
            pendingChoiceResolved = true;
        }

        private void ResolveReferences()
        {
            storyFlowPlayer = storyFlowPlayer != null ? storyFlowPlayer : FindFirstObjectByType<StoryFlowPlayer>(FindObjectsInactive.Include);
            capController = capController != null ? capController : FindFirstObjectByType<LabCapNpcController>(FindObjectsInactive.Include);
            controlLock = controlLock != null ? controlLock : FindFirstObjectByType<ClassroomPlayerControlLock>(FindObjectsInactive.Include);
            cameraFocusController = cameraFocusController != null ? cameraFocusController : FindFirstObjectByType<LabCameraFocusController>(FindObjectsInactive.Include);
            interactionDirector = interactionDirector != null ? interactionDirector : FindFirstObjectByType<InteractionDirector>(FindObjectsInactive.Include);
            sceneContext = sceneContext != null ? sceneContext : FindFirstObjectByType<LabSceneContext>(FindObjectsInactive.Include);
            llmService = llmService != null ? llmService : FindFirstObjectByType<ClassroomLlmService>(FindObjectsInactive.Include);
            if (llmService == null)
            {
                llmService = gameObject.GetComponent<ClassroomLlmService>();
                if (llmService == null)
                {
                    llmService = gameObject.AddComponent<ClassroomLlmService>();
                }
            }

            freeChatUi = freeChatUi != null ? freeChatUi : FindFirstObjectByType<ClassroomNpcFreeChatUi>(FindObjectsInactive.Include);
            if (freeChatUi == null)
            {
                freeChatUi = EnsureOverlayComponent<ClassroomNpcFreeChatUi>("ClassroomNpcFreeChatUiRoot");
            }
            npcActionExecutor = npcActionExecutor != null ? npcActionExecutor : FindFirstObjectByType<ClassroomNpcActionExecutor>(FindObjectsInactive.Include);
            if (capController != null)
            {
                capNpc = capNpc != null ? capNpc : capController.NpcAgent;
            }

            capNpc = capNpc != null ? capNpc : FindFirstObjectByType<StoryNpcAgent>(FindObjectsInactive.Include);
        }

        private void RefreshCapInteractionSubscription()
        {
            if (capNpc == null)
            {
                return;
            }

            capNpc.InteractionTriggered -= HandleCapInteraction;
            capNpc.InteractionTriggered += HandleCapInteraction;
        }

        private void SyncSessionId()
        {
            if (storyFlowPlayer != null && !string.IsNullOrWhiteSpace(storyFlowPlayer.SessionId))
            {
                currentSessionId = storyFlowPlayer.SessionId;
            }
        }

        private static T EnsureOverlayComponent<T>(string rootName)
            where T : MonoBehaviour
        {
            var root = GameObject.Find(rootName);
            if (root == null)
            {
                root = new GameObject(rootName);
            }

            var document = root.GetComponent<UnityEngine.UIElements.UIDocument>();
            if (document == null)
            {
                document = root.AddComponent<UnityEngine.UIElements.UIDocument>();
            }

            var component = root.GetComponent<T>();
            if (component == null)
            {
                component = root.AddComponent<T>();
            }

            return component;
        }
    }
}
