using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ItemInteraction;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomStoryConversationDirector : MonoBehaviour
    {
        private const string StoryNamespace = "classroom";

        private const string TeacherDisplayName = "Dr. Mira Sato";
        private const string FriendDisplayName = "Nia Park";
        private const string SkepticDisplayName = "Theo Mercer";

        private const string BoardDisplayName = "Route Board";
        private const string DeskDisplayName = "Teacher Desk";
        private const string ShelfDisplayName = "Reference Shelf";
        private const string ClockDisplayName = "Wall Clock";

        private const string TeacherTalkOptionId = "talk";
        private const string TeacherSafetyOptionId = "ask_safety";
        private const string TeacherVolunteerOptionId = "volunteer";

        private const string FriendTalkOptionId = "talk";
        private const string FriendReassureOptionId = "reassure";
        private const string FriendJokeOptionId = "joke";

        private const string SkepticTalkOptionId = "talk";
        private const string SkepticChallengeOptionId = "challenge";
        private const string SkepticAirwayOptionId = "ask_airway";

        private const string BoardReadOptionId = "read";
        private const string DeskReviewOptionId = "review_notes";
        private const string ShelfBookOptionId = "take_book";
        private const string ClockCheckOptionId = "check_time";

        [SerializeField] private StoryFlowChannels channels;
        [SerializeField] private StoryNpcRegistry npcRegistry;
        [SerializeField] private StoryNpcAgent teacherNpc;
        [SerializeField] private StoryNpcAgent friendNpc;
        [SerializeField] private StoryNpcAgent skepticNpc;
        [SerializeField] private ClassroomStoryConversationPresentationController presentationController;
        [SerializeField] private ClassroomStoryObjectivePresenter objectivePresenter;
        [SerializeField] private ClassroomBodyKnowledgeBookUi knowledgeBookUi;
        [SerializeField] private ClassroomBodyKnowledgeQuizUi knowledgeQuizUi;
        [SerializeField] private ClassroomLlmService llmService;
        [SerializeField] private ClassroomNpcFreeChatUi freeChatUi;
        [SerializeField] private ClassroomNpcActionExecutor npcActionExecutor;
        [SerializeField] private ClassroomNpcAmbientChatterLoop ambientChatterLoop;
        [SerializeField, TextArea(4, 12)] private string freeChatSystemPrompt =
            "You are roleplaying one friendly NPC in a K-12 biology classroom preparing for a miniaturized mission. " +
            "Use simple, clear words. Reply in 1-2 short sentences. " +
            "Always output exactly this format:\n" +
            "SAY: <what the NPC says>\n" +
            "ACTIONS: <comma-separated actions or none>\n" +
            "Allowed actions: dance,jump,surprised,lie_down,follow_player_short,start_quiz,go_talk_nia,go_talk_theo,go_talk_mira.";
        [SerializeField, Min(2f)] private float llmRequestTimeoutSeconds = 30f;
        [SerializeField, Min(8)] private int llmFreeChatMaxTokens = 136;

        [SerializeField] private InteractableItem boardInteractable;
        [SerializeField] private InteractableItem deskInteractable;
        [SerializeField] private InteractableItem shelfInteractable;
        [SerializeField] private InteractableItem clockInteractable;

        [SerializeField] private string boardHierarchyPath = "Classroom/Collection/Collection|board_L_board.blend|Dupli|";
        [SerializeField] private string deskHierarchyPath = "Classroom/teacherDesk.001/teacherDesk.001|teacherDesk_L_teacherDesk.blend|Dupli|";
        [SerializeField] private string shelfHierarchyPath = "Classroom/etagere.001/etagere.001|etagere.001_L_etagere.blend|Dupli|1";
        [SerializeField] private string clockHierarchyPath = "Classroom/clock.001/clock.001|clock_L_clock.blend|Dupli|";

        [SerializeField] private string currentSessionId = string.Empty;
        [SerializeField, Min(0.05f)] private float linePaddingSeconds = 0.18f;

        [Header("Progress Flags")]
        [SerializeField] private bool teacherTalked;
        [SerializeField] private bool teacherSafetyExplained;
        [SerializeField] private bool friendTalked;
        [SerializeField] private bool skepticTalked;
        [SerializeField] private bool boardExamined;
        [SerializeField] private bool deskExamined;
        [SerializeField] private bool shelfBookRead;
        [SerializeField] private bool clockChecked;
        [SerializeField] private bool volunteerConfirmed;
        [SerializeField] private bool labClearanceEarned;
        [SerializeField] private ClassroomPlayerAttitude playerAttitude;
        [SerializeField] private ClassroomRelationshipMoment relationshipMoment;

        private Coroutine activeConversationRoutine;
        private string pendingChoiceRequestId = string.Empty;
        private string pendingChoicePortId = string.Empty;
        private bool pendingChoiceResolved;
        private readonly Queue<string> streamingDeltaQueue = new Queue<string>(64);

        private enum ClassroomPlayerAttitude
        {
            None = 0,
            Curious = 1,
            Brave = 2,
            Nervous = 3
        }

        private enum ClassroomRelationshipMoment
        {
            None = 0,
            ComfortedNia = 1,
            DebatedTheo = 2,
            PressedMira = 3
        }

        [Serializable]
        private readonly struct DialogueBeat
        {
            public DialogueBeat(string speaker, string body, float durationSeconds = 2.8f)
            {
                Speaker = speaker;
                Body = body;
                DurationSeconds = durationSeconds;
            }

            public string Speaker { get; }
            public string Body { get; }
            public float DurationSeconds { get; }
        }

        private readonly struct ChoiceOptionConfig
        {
            public ChoiceOptionConfig(string portId, string label)
            {
                PortId = portId;
                Label = label;
            }

            public string PortId { get; }
            public string Label { get; }
        }

        private bool ConversationBusy => activeConversationRoutine != null;

        private void Awake()
        {
            ResolveSceneReferences();
            ConfigureSceneActors();
        }

        private void OnEnable()
        {
            ResolveSceneReferences();
            RegisterStoryChannels();
            RegisterSceneHooks();
            ConfigureSceneActors();
            RefreshInteractionAvailability();
        }

        private void OnDisable()
        {
            if (activeConversationRoutine != null)
            {
                StopCoroutine(activeConversationRoutine);
                activeConversationRoutine = null;
            }

            pendingChoiceRequestId = string.Empty;
            pendingChoicePortId = string.Empty;
            pendingChoiceResolved = false;

            presentationController?.EndConversation();
            freeChatUi?.HideImmediate();
            objectivePresenter?.Clear();
            UnregisterSceneHooks();
            UnregisterStoryChannels();
        }

        public void Configure(StoryFlowChannels storyChannels)
        {
            if (channels == storyChannels)
            {
                return;
            }

            if (isActiveAndEnabled)
            {
                UnregisterStoryChannels();
            }

            channels = storyChannels;

            if (isActiveAndEnabled)
            {
                RegisterStoryChannels();
            }
        }

        public void SetSessionId(string sessionId)
        {
            currentSessionId = sessionId ?? string.Empty;
        }

        private void ResolveSceneReferences()
        {
            npcRegistry = npcRegistry != null ? npcRegistry : FindFirstObjectByType<StoryNpcRegistry>();
            teacherNpc = teacherNpc != null ? teacherNpc : ResolveNpc(ClassroomStoryNpcIds.Teacher);
            friendNpc = friendNpc != null ? friendNpc : ResolveNpc(ClassroomStoryNpcIds.Friend);
            skepticNpc = skepticNpc != null ? skepticNpc : ResolveNpc(ClassroomStoryNpcIds.Skeptic);

            presentationController = presentationController != null
                ? presentationController
                : FindFirstObjectByType<ClassroomStoryConversationPresentationController>();

            objectivePresenter = objectivePresenter != null
                ? objectivePresenter
                : FindFirstObjectByType<ClassroomStoryObjectivePresenter>(FindObjectsInactive.Include);

            knowledgeBookUi = knowledgeBookUi != null
                ? knowledgeBookUi
                : FindFirstObjectByType<ClassroomBodyKnowledgeBookUi>(FindObjectsInactive.Include);

            knowledgeQuizUi = knowledgeQuizUi != null
                ? knowledgeQuizUi
                : FindFirstObjectByType<ClassroomBodyKnowledgeQuizUi>(FindObjectsInactive.Include);

            llmService = llmService != null
                ? llmService
                : FindFirstObjectByType<ClassroomLlmService>(FindObjectsInactive.Include);

            if (llmService == null)
            {
                llmService = gameObject.GetComponent<ClassroomLlmService>();
                if (llmService == null)
                {
                    llmService = gameObject.AddComponent<ClassroomLlmService>();
                }
            }

            npcActionExecutor = npcActionExecutor != null
                ? npcActionExecutor
                : FindFirstObjectByType<ClassroomNpcActionExecutor>(FindObjectsInactive.Include);
            if (npcActionExecutor == null)
            {
                npcActionExecutor = gameObject.GetComponent<ClassroomNpcActionExecutor>();
                if (npcActionExecutor == null)
                {
                    npcActionExecutor = gameObject.AddComponent<ClassroomNpcActionExecutor>();
                }
            }

            freeChatUi = freeChatUi != null
                ? freeChatUi
                : FindFirstObjectByType<ClassroomNpcFreeChatUi>(FindObjectsInactive.Include);
            if (freeChatUi == null)
            {
                freeChatUi = EnsureOverlayComponent<ClassroomNpcFreeChatUi>("ClassroomNpcFreeChatUiRoot");
            }

            if (knowledgeBookUi == null)
            {
                knowledgeBookUi = EnsureOverlayComponent<ClassroomBodyKnowledgeBookUi>("ClassroomBookUiRoot");
            }

            if (knowledgeQuizUi == null)
            {
                knowledgeQuizUi = EnsureOverlayComponent<ClassroomBodyKnowledgeQuizUi>("ClassroomQuizUiRoot");
            }

            if (ambientChatterLoop == null)
            {
                ambientChatterLoop = FindFirstObjectByType<ClassroomNpcAmbientChatterLoop>(FindObjectsInactive.Include);
            }

            boardInteractable = EnsureInteractable(
                boardInteractable,
                boardHierarchyPath,
                "Collection|board_L_board.blend|Dupli|",
                BoardDisplayName);

            deskInteractable = EnsureInteractable(
                deskInteractable,
                deskHierarchyPath,
                "teacherDesk.001|teacherDesk_L_teacherDesk.blend|Dupli|",
                DeskDisplayName);

            shelfInteractable = EnsureInteractable(
                shelfInteractable,
                shelfHierarchyPath,
                "etagere.001|etagere.001_L_etagere.blend|Dupli|1",
                ShelfDisplayName);

            clockInteractable = EnsureInteractable(
                clockInteractable,
                clockHierarchyPath,
                "clock.001|clock_L_clock.blend|Dupli|",
                ClockDisplayName);
        }

        private void ConfigureSceneActors()
        {
            teacherNpc?.ConfigureNpc(
                ClassroomStoryNpcIds.Teacher,
                TeacherDisplayName,
                CreateTeacherOptions(),
                "You",
                "Dr. Mira is reading the room like she already knows who will volunteer and who will avoid eye contact.",
                2.8f,
                StoryNamespace);

            friendNpc?.ConfigureNpc(
                ClassroomStoryNpcIds.Friend,
                FriendDisplayName,
                CreateFriendOptions(),
                "You",
                "Nia keeps glancing from the board to the lab door, rehearsing worst-case scenarios before they happen.",
                2.8f,
                StoryNamespace);

            skepticNpc?.ConfigureNpc(
                ClassroomStoryNpcIds.Skeptic,
                SkepticDisplayName,
                CreateSkepticOptions(),
                "You",
                "Theo has the posture of someone pretending this is a joke so he does not have to admit he is nervous.",
                2.7f,
                StoryNamespace);

            ConfigureBoardInteractable();
            ConfigureDeskInteractable();
            ConfigureShelfInteractable();
            ConfigureClockInteractable();
            RefreshInteractionAvailability();
            ambientChatterLoop?.SetEnabled(true);
        }

        private void ConfigureBoardInteractable()
        {
            if (boardInteractable == null)
            {
                return;
            }

            boardInteractable.displayName = BoardDisplayName;
            boardInteractable.storyId = "classroom.route.board";
            boardInteractable.lookDialogueSpeaker = "You";
            boardInteractable.lookDialogueBody = "Mouth. Esophagus. Stomach. Small intestine. The route is simple on paper and brutal in motion.";
            boardInteractable.lookDialogueDisplayDurationSeconds = 3f;
            boardInteractable.isInteractable = true;
            EnsureOptionsList(boardInteractable);
            boardInteractable.options.Clear();
            boardInteractable.options.Add(new InteractionOption
            {
                id = BoardReadOptionId,
                label = "Read",
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
            boardInteractable.options.Add(new InteractionOption
            {
                id = "look",
                label = "Observe",
                slot = InteractionOptionSlot.Bottom,
                visible = true,
                enabled = true
            });
        }

        private void ConfigureDeskInteractable()
        {
            if (deskInteractable == null)
            {
                return;
            }

            deskInteractable.displayName = DeskDisplayName;
            deskInteractable.storyId = "classroom.teacher.desk";
            deskInteractable.lookDialogueSpeaker = "You";
            deskInteractable.lookDialogueBody = "A clipboard, launch checklist, and handwritten timing notes. Nothing here looks improvised.";
            deskInteractable.lookDialogueDisplayDurationSeconds = 2.9f;
            deskInteractable.isInteractable = true;
            EnsureOptionsList(deskInteractable);
            deskInteractable.options.Clear();
            deskInteractable.options.Add(new InteractionOption
            {
                id = DeskReviewOptionId,
                label = "Review Notes",
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
            deskInteractable.options.Add(new InteractionOption
            {
                id = "look",
                label = "Observe",
                slot = InteractionOptionSlot.Bottom,
                visible = true,
                enabled = true
            });
        }

        private void ConfigureShelfInteractable()
        {
            if (shelfInteractable == null)
            {
                return;
            }

            shelfInteractable.displayName = ShelfDisplayName;
            shelfInteractable.storyId = "classroom.reference.shelf";
            shelfInteractable.lookDialogueSpeaker = "You";
            shelfInteractable.lookDialogueBody = "A worn anatomy atlas is wedged between old chemistry binders.";
            shelfInteractable.lookDialogueDisplayDurationSeconds = 2.7f;
            shelfInteractable.isInteractable = true;
            EnsureOptionsList(shelfInteractable);
            shelfInteractable.options.Clear();
            shelfInteractable.options.Add(new InteractionOption
            {
                id = ShelfBookOptionId,
                label = "Take Book",
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
            shelfInteractable.options.Add(new InteractionOption
            {
                id = "look",
                label = "Observe",
                slot = InteractionOptionSlot.Bottom,
                visible = true,
                enabled = true
            });
        }

        private void ConfigureClockInteractable()
        {
            if (clockInteractable == null)
            {
                return;
            }

            clockInteractable.displayName = ClockDisplayName;
            clockInteractable.storyId = "classroom.wall.clock";
            clockInteractable.lookDialogueSpeaker = "You";
            clockInteractable.lookDialogueBody = "Second hand steady. No drama, just timing.";
            clockInteractable.lookDialogueDisplayDurationSeconds = 2.5f;
            clockInteractable.isInteractable = true;
            EnsureOptionsList(clockInteractable);
            clockInteractable.options.Clear();
            clockInteractable.options.Add(new InteractionOption
            {
                id = ClockCheckOptionId,
                label = "Check Time",
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
            clockInteractable.options.Add(new InteractionOption
            {
                id = "look",
                label = "Observe",
                slot = InteractionOptionSlot.Bottom,
                visible = true,
                enabled = true
            });
        }

        private void RegisterStoryChannels()
        {
            if (channels == null)
            {
                return;
            }

            channels.ChoiceSelections?.Register(HandleChoiceSelection);
            channels.ExternalSignals?.Register(HandleExternalSignal);
            channels.GraphNotifications?.Register(HandleGraphNotification);
        }

        private void UnregisterStoryChannels()
        {
            if (channels == null)
            {
                return;
            }

            channels.ChoiceSelections?.Unregister(HandleChoiceSelection);
            channels.ExternalSignals?.Unregister(HandleExternalSignal);
            channels.GraphNotifications?.Unregister(HandleGraphNotification);
        }

        private void RegisterSceneHooks()
        {
            StoryNpcAgent.AnyInteractionTriggered -= HandleNpcInteractionTriggered;
            StoryNpcAgent.AnyInteractionTriggered += HandleNpcInteractionTriggered;

            if (boardInteractable != null)
            {
                boardInteractable.OptionTriggered -= HandleBoardOptionTriggered;
                boardInteractable.OptionTriggered += HandleBoardOptionTriggered;
            }

            if (deskInteractable != null)
            {
                deskInteractable.OptionTriggered -= HandleDeskOptionTriggered;
                deskInteractable.OptionTriggered += HandleDeskOptionTriggered;
            }

            if (shelfInteractable != null)
            {
                shelfInteractable.OptionTriggered -= HandleShelfOptionTriggered;
                shelfInteractable.OptionTriggered += HandleShelfOptionTriggered;
            }

            if (clockInteractable != null)
            {
                clockInteractable.OptionTriggered -= HandleClockOptionTriggered;
                clockInteractable.OptionTriggered += HandleClockOptionTriggered;
            }
        }

        private void UnregisterSceneHooks()
        {
            StoryNpcAgent.AnyInteractionTriggered -= HandleNpcInteractionTriggered;

            if (boardInteractable != null)
            {
                boardInteractable.OptionTriggered -= HandleBoardOptionTriggered;
            }

            if (deskInteractable != null)
            {
                deskInteractable.OptionTriggered -= HandleDeskOptionTriggered;
            }

            if (shelfInteractable != null)
            {
                shelfInteractable.OptionTriggered -= HandleShelfOptionTriggered;
            }

            if (clockInteractable != null)
            {
                clockInteractable.OptionTriggered -= HandleClockOptionTriggered;
            }
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

            if (notification.Kind == StoryGraphNotificationKind.Started ||
                notification.Kind == StoryGraphNotificationKind.Loaded)
            {
                ResetConversationState();
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

            switch (signal.SignalId)
            {
                case ClassroomStorySignals.VolunteerConfirmed:
                    volunteerConfirmed = true;
                    RefreshInteractionAvailability();
                    break;

                case ClassroomStorySignals.LabClearanceEarned:
                    labClearanceEarned = true;
                    RefreshInteractionAvailability();
                    break;
            }
        }

        private void HandleChoiceSelection(StoryChoiceSelection selection)
        {
            if (selection == null || string.IsNullOrWhiteSpace(pendingChoiceRequestId))
            {
                return;
            }

            if (!string.Equals(selection.RequestId, pendingChoiceRequestId, StringComparison.Ordinal))
            {
                return;
            }

            pendingChoicePortId = selection.PortId ?? string.Empty;
            pendingChoiceResolved = true;
        }

        private void HandleNpcInteractionTriggered(StoryNpcInteractionPayload payload)
        {
            if (payload == null || payload.Agent == null || ConversationBusy)
            {
                return;
            }

            if (!ReferencesRegisteredNpc(payload.Agent))
            {
                return;
            }

            presentationController?.SetConversationTarget(payload.Agent.transform);

            switch (payload.NpcId)
            {
                case ClassroomStoryNpcIds.Teacher:
                    switch (payload.OptionId)
                    {
                        case TeacherTalkOptionId:
                            BeginNpcConversation(TalkEntryRoutine(teacherNpc, TeacherTalkRoutine));
                            break;

                        case TeacherSafetyOptionId:
                            BeginNpcConversation(TeacherSafetyRoutine());
                            break;

                        case TeacherVolunteerOptionId:
                            BeginNpcConversation(TeacherVolunteerRoutine());
                            break;
                    }

                    break;

                case ClassroomStoryNpcIds.Friend:
                    switch (payload.OptionId)
                    {
                        case FriendTalkOptionId:
                            BeginNpcConversation(TalkEntryRoutine(friendNpc, FriendTalkRoutine));
                            break;

                        case FriendReassureOptionId:
                            BeginNpcConversation(FriendReassureRoutine());
                            break;

                        case FriendJokeOptionId:
                            BeginNpcConversation(FriendJokeRoutine());
                            break;
                    }

                    break;

                case ClassroomStoryNpcIds.Skeptic:
                    switch (payload.OptionId)
                    {
                        case SkepticTalkOptionId:
                            BeginNpcConversation(TalkEntryRoutine(skepticNpc, SkepticTalkRoutine));
                            break;

                        case SkepticChallengeOptionId:
                            BeginNpcConversation(SkepticChallengeRoutine());
                            break;

                        case SkepticAirwayOptionId:
                            BeginNpcConversation(SkepticAirwayRoutine());
                            break;
                    }

                    break;
            }
        }

        private void HandleBoardOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != boardInteractable || ConversationBusy)
            {
                return;
            }

            if (string.Equals(invocation.OptionId, BoardReadOptionId, StringComparison.OrdinalIgnoreCase))
            {
                BeginItemConversation(BoardReadRoutine());
            }
        }

        private void HandleDeskOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != deskInteractable || ConversationBusy)
            {
                return;
            }

            if (string.Equals(invocation.OptionId, DeskReviewOptionId, StringComparison.OrdinalIgnoreCase))
            {
                BeginItemConversation(DeskNotesRoutine());
            }
        }

        private void HandleShelfOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != shelfInteractable || ConversationBusy)
            {
                return;
            }

            if (string.Equals(invocation.OptionId, ShelfBookOptionId, StringComparison.OrdinalIgnoreCase))
            {
                BeginItemConversation(ShelfBookRoutine());
            }
        }

        private void HandleClockOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != clockInteractable || ConversationBusy)
            {
                return;
            }

            if (string.Equals(invocation.OptionId, ClockCheckOptionId, StringComparison.OrdinalIgnoreCase))
            {
                BeginItemConversation(ClockRoutine());
            }
        }

        private void BeginNpcConversation(IEnumerator routine)
        {
            BeginConversation(routine, enablePresentation: true);
        }

        private void BeginItemConversation(IEnumerator routine)
        {
            presentationController?.SetConversationTarget(null);
            BeginConversation(routine, enablePresentation: false);
        }

        private void BeginConversation(IEnumerator routine, bool enablePresentation)
        {
            if (routine == null || ConversationBusy)
            {
                return;
            }

            activeConversationRoutine = StartCoroutine(RunConversationRoutine(routine, enablePresentation));
        }

        private IEnumerator RunConversationRoutine(IEnumerator routine, bool enablePresentation)
        {
            if (enablePresentation)
            {
                presentationController?.BeginConversation();
            }

            try
            {
                yield return routine;
            }
            finally
            {
                pendingChoiceRequestId = string.Empty;
                pendingChoicePortId = string.Empty;
                pendingChoiceResolved = false;
                activeConversationRoutine = null;
                if (enablePresentation)
                {
                    presentationController?.EndConversation();
                }

                RefreshInteractionAvailability();
            }
        }

        private IEnumerator TalkEntryRoutine(StoryNpcAgent npc, Func<IEnumerator> scriptedRoutineFactory)
        {
            if (npc == null)
            {
                yield break;
            }

            var option = string.Empty;
            yield return PresentChoice(
                $"How do you want to approach {npc.NpcDisplayName}?",
                choice => option = choice,
                new ChoiceOptionConfig("scripted", "Scripted Talk"),
                new ChoiceOptionConfig("free_chat", "Free Chat"),
                new ChoiceOptionConfig("leave", "Leave"));

            if (string.Equals(option, "scripted", StringComparison.Ordinal))
            {
                if (scriptedRoutineFactory != null)
                {
                    yield return scriptedRoutineFactory();
                }

                yield break;
            }

            if (string.Equals(option, "free_chat", StringComparison.Ordinal))
            {
                yield return RunFreeChatRoutine(npc);
                yield break;
            }
        }

        private IEnumerator RunFreeChatRoutine(StoryNpcAgent npc)
        {
            ResolveSceneReferences();
            if (npc == null || freeChatUi == null || llmService == null)
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat("System", "Free chat is unavailable right now. Use scripted talk.", 2.3f));
                yield break;
            }

            var history = new List<LlmChatMessage>(12)
            {
                new LlmChatMessage("system", BuildNpcSystemPrompt(npc))
            };

            var closeRequested = false;
            string pendingInput = null;
            void HandleSend(string text)
            {
                pendingInput = text;
            }

            void HandleClosed()
            {
                closeRequested = true;
            }

            freeChatUi.SendRequested += HandleSend;
            freeChatUi.Closed += HandleClosed;
            freeChatUi.Open(npc.NpcDisplayName);
            freeChatUi.AppendSystem("Free chat active. Keep your prompts short. Type 'leave' to exit.");

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

                        yield return RunFreeChatExchange(npc, history, line);
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

        private IEnumerator RunFreeChatExchange(StoryNpcAgent npc, List<LlmChatMessage> history, string playerText)
        {
            if (npc == null || history == null || string.IsNullOrWhiteSpace(playerText))
            {
                yield break;
            }

            history.Add(new LlmChatMessage("user", playerText));
            freeChatUi.AppendPlayer(playerText);
            freeChatUi.SetBusy(true);
            freeChatUi.BeginAssistantStreaming(npc.NpcDisplayName);
            presentationController?.FocusOnSpeaker("You");

            lock (streamingDeltaQueue)
            {
                streamingDeltaQueue.Clear();
            }

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Mathf.Max(8f, llmRequestTimeoutSeconds)));
            Task<string> requestTask;
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
                Debug.LogWarning($"[ClassroomStoryConversationDirector] Failed to issue LLM request: {ex.Message}", this);
                freeChatUi.ReplaceActiveAssistantText("I could not reach the classroom model.");
                freeChatUi.FinalizeAssistantStreaming();
                freeChatUi.SetBusy(false);
                yield break;
            }

            while (!requestTask.IsCompleted)
            {
                FlushStreamingDeltasToUi();
                yield return null;
            }

            FlushStreamingDeltasToUi();
            if (requestTask.IsFaulted)
            {
                freeChatUi.ReplaceActiveAssistantText("Connection failed. Try a shorter line.");
                freeChatUi.FinalizeAssistantStreaming();
                freeChatUi.SetBusy(false);
                yield break;
            }

            var raw = requestTask.Result ?? string.Empty;
            var structured = llmService.ParseStructuredNpcReply(raw);
            var cleanedSpeech = ClampSpeechLength(structured.Say, 190);

            freeChatUi.ReplaceActiveAssistantText(cleanedSpeech);
            freeChatUi.FinalizeAssistantStreaming();
            freeChatUi.SetBusy(false);

            if (!string.IsNullOrWhiteSpace(cleanedSpeech))
            {
                history.Add(new LlmChatMessage(
                    "assistant",
                    $"SAY: {cleanedSpeech}\nACTIONS: {string.Join(",", structured.Actions)}"));
                presentationController?.FocusOnSpeaker(npc.NpcDisplayName);
                yield return PresentDialogue(npc.NpcDisplayName, cleanedSpeech, Mathf.Clamp(cleanedSpeech.Length * 0.06f, 1.8f, 4.8f));
            }

            if (structured.Actions != null && structured.Actions.Count > 0 && npcActionExecutor != null)
            {
                yield return npcActionExecutor.ExecuteActionsRoutine(npc, structured.Actions);
            }
        }

        private void FlushStreamingDeltasToUi()
        {
            if (freeChatUi == null)
            {
                return;
            }

            lock (streamingDeltaQueue)
            {
                while (streamingDeltaQueue.Count > 0)
                {
                    var delta = streamingDeltaQueue.Dequeue();
                    freeChatUi.AppendAssistantDelta(delta);
                }
            }
        }

        private string BuildNpcSystemPrompt(StoryNpcAgent npc)
        {
            var display = npc != null ? npc.NpcDisplayName : "NPC";
            var npcId = npc != null ? npc.NpcId : string.Empty;
            var prompt = new StringBuilder(640);
            prompt.AppendLine(freeChatSystemPrompt);
            prompt.Append("Current speaker: ").Append(display).Append(" (").Append(npcId).AppendLine(")");
            prompt.AppendLine("Scene context: classroom before lab transition; mission is mini rocket entry through mouth.");
            prompt.AppendLine("If user asks for action, include action tokens in ACTIONS line.");
            prompt.AppendLine("Never include more than 2 action tokens in one response.");
            prompt.AppendLine("Avoid long explanations. Keep teaching points compact and concrete.");
            return prompt.ToString();
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

        private IEnumerator TeacherTalkRoutine()
        {
            if (!teacherTalked)
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat(TeacherDisplayName, "Alright team, big science day.", 2.3f),
                    new DialogueBeat(TeacherDisplayName, "In the lab next door, one of you will ride a tiny rocket through the digestive system.", 3.9f));

                var firstResponse = string.Empty;
                yield return PresentChoice(
                    "How do you answer Dr. Mira?",
                    choice => firstResponse = choice,
                    new ChoiceOptionConfig("curious", "Can you explain the route?"),
                    new ChoiceOptionConfig("brave", "I can do it."),
                    new ChoiceOptionConfig("nervous", "This sounds kind of scary."));

                switch (firstResponse)
                {
                    case "curious":
                        playerAttitude = ClassroomPlayerAttitude.Curious;
                        yield return PresentDialogueSequence(
                            new DialogueBeat("You", "Can you explain the route?", 2.1f),
                            new DialogueBeat(TeacherDisplayName, "Great question. We enter through the mouth, move down the food tube, pass the stomach, and reach the small intestine.", 4f),
                            new DialogueBeat(TeacherDisplayName, "What matters is knowing where it gets risky, and what to do next.", 3.3f));
                        break;

                    case "brave":
                        playerAttitude = ClassroomPlayerAttitude.Brave;
                        yield return PresentDialogueSequence(
                            new DialogueBeat("You", "I can do it.", 1.8f),
                            new DialogueBeat(TeacherDisplayName, "Bravery helps, but following safety rules matters more.", 2.7f),
                            new DialogueBeat(TeacherDisplayName, "Talk to classmates and check room clues, then volunteer with real facts.", 3.6f));
                        break;

                    default:
                        playerAttitude = ClassroomPlayerAttitude.Nervous;
                        yield return PresentDialogueSequence(
                            new DialogueBeat("You", "This sounds kind of scary.", 2.2f),
                            new DialogueBeat(TeacherDisplayName, "It is unusual, but we control every step.", 2.4f),
                            new DialogueBeat(TeacherDisplayName, "Being nervous is okay. Learn the facts first.", 3.2f));
                        break;
                }

                teacherTalked = true;
                RaiseSignal(ClassroomStorySignals.TeacherTalked, ClassroomStoryNpcIds.Teacher);
                RefreshInteractionAvailability();
                yield break;
            }

            if (!HasEnoughScienceEvidence())
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat(TeacherDisplayName, "You need a little more evidence before I open the lab door.", 2.8f),
                    new DialogueBeat(TeacherDisplayName, "Talk with Nia or Theo, read the board, check my desk notes, open the shelf atlas, and check the wall clock.", 4.0f));
                yield break;
            }

            if (!(volunteerConfirmed || labClearanceEarned))
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat(TeacherDisplayName, "Nice work. If you are ready, choose Volunteer.", 3.1f));
                yield break;
            }

            yield return PresentDialogueSequence(
                new DialogueBeat(TeacherDisplayName, "Lab door is ready. Let's go.", 2.4f));
        }

        private IEnumerator TeacherSafetyRoutine()
        {
            if (!teacherTalked)
            {
                yield return TeacherTalkRoutine();
                yield break;
            }

            teacherSafetyExplained = true;
            relationshipMoment = ClassroomRelationshipMoment.PressedMira;
            RaiseSignal(ClassroomStorySignals.TeacherSafetyExplained, ClassroomStoryNpcIds.Teacher);

            yield return PresentDialogueSequence(
                new DialogueBeat("You", "What part of the risk are you still worried about?", 2.2f),
                new DialogueBeat(TeacherDisplayName, "Mouth entry is the easy part. The throat handoff is the risky part.", 3.8f),
                new DialogueBeat(TeacherDisplayName, "The rocket must go into the food tube. If it goes into the airway, mission over.", 4.0f),
                new DialogueBeat(TeacherDisplayName, "Then speed matters. We can handle stomach acid only if we keep moving.", 3.7f));
        }

        private IEnumerator TeacherVolunteerRoutine()
        {
            if (!teacherTalked)
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat(TeacherDisplayName, "No blind volunteering. Talk to me first.", 2.4f));
                yield break;
            }

            if (!HasEnoughScienceEvidence())
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat(TeacherDisplayName, "Not yet. Bring me more evidence from the room and classmates.", 3.0f));
                yield break;
            }

            if (volunteerConfirmed || labClearanceEarned)
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat(TeacherDisplayName, "You already committed. Use the door.", 2.2f));
                yield break;
            }

            var decision = string.Empty;
            yield return PresentChoice(
                "Dr. Mira waits for your answer.",
                choice => decision = choice,
                new ChoiceOptionConfig("go", "I'm going."),
                new ChoiceOptionConfig("summary", "Give me the quick mission summary."),
                new ChoiceOptionConfig("why", "Why me?"));

            if (string.Equals(decision, "summary", StringComparison.Ordinal))
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat(TeacherDisplayName, "In the lab, you shrink, board the mini rocket, and enter through the mouth.", 3.0f),
                    new DialogueBeat(TeacherDisplayName, "Stay out of the airway, cross the stomach quickly, then reach the small intestine to study absorption.", 4.2f),
                    new DialogueBeat(TeacherDisplayName, "That's the core lesson: be precise, not flashy.", 2.8f));

                yield return PresentChoice(
                    "After the recap?",
                    choice => decision = choice,
                    new ChoiceOptionConfig("go", "I'm in."),
                    new ChoiceOptionConfig("hold", "I need one minute."));
            }
            else if (string.Equals(decision, "why", StringComparison.Ordinal))
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat("You", "Why me?", 1.7f),
                    new DialogueBeat(TeacherDisplayName, ResolveWhyMeLine(), 3.1f),
                    new DialogueBeat(TeacherDisplayName, "That answer matters only if you still choose this clearly.", 2.8f));

                yield return PresentChoice(
                    "Are you ready to commit?",
                    choice => decision = choice,
                    new ChoiceOptionConfig("go", "I commit."),
                    new ChoiceOptionConfig("hold", "Not yet."));
            }

            if (!string.Equals(decision, "go", StringComparison.Ordinal))
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat(TeacherDisplayName, "Take a breath and choose when you are ready.", 2.1f));
                yield break;
            }

            volunteerConfirmed = true;
            labClearanceEarned = true;
            RaiseSignal(ClassroomStorySignals.VolunteerConfirmed, ClassroomStoryNpcIds.Teacher);
            RaiseSignal(ClassroomStorySignals.LabClearanceEarned, ClassroomStoryNpcIds.Teacher);

            yield return PresentDialogueSequence(
                new DialogueBeat("You", "I'm going.", 1.6f),
                new DialogueBeat(TeacherDisplayName, ResolveVolunteerApprovalLine(), 3.1f),
                new DialogueBeat(TeacherDisplayName, "Lab door. Final briefing in one minute, then launch.", 3.2f));
        }

        private IEnumerator FriendTalkRoutine()
        {
            if (!friendTalked)
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat(FriendDisplayName, "I know the diagrams, but it feels weird to map a real body.", 3.4f));

                var choice = string.Empty;
                yield return PresentChoice(
                    "How do you answer Nia?",
                    selected => choice = selected,
                    new ChoiceOptionConfig("matter", "That is exactly why this matters."),
                    new ChoiceOptionConfig("easy", "You do not need to act like this is easy."),
                    new ChoiceOptionConfig("panic", "If I panic, I blame you for jinxing me."));

                switch (choice)
                {
                    case "matter":
                        yield return PresentDialogueSequence(
                            new DialogueBeat("You", "That is exactly why this matters.", 2.0f),
                            new DialogueBeat(FriendDisplayName, "Exactly. It's a real person, not just a school diagram.", 2.7f));
                        break;

                    case "easy":
                        relationshipMoment = ClassroomRelationshipMoment.ComfortedNia;
                        yield return PresentDialogueSequence(
                            new DialogueBeat("You", "You do not need to act like this is easy.", 2.0f),
                            new DialogueBeat(FriendDisplayName, "Thanks. People forget this feels scary, not just like homework.", 3.0f));
                        break;

                    default:
                        yield return PresentDialogueSequence(
                            new DialogueBeat("You", "If I panic, I blame you for jinxing me.", 2.2f),
                            new DialogueBeat(FriendDisplayName, "That is not how science works... I think.", 2.2f));
                        break;
                }

                yield return PresentDialogueSequence(
                    new DialogueBeat(FriendDisplayName, "Stomach acid is still the part that scares me.", 2.2f),
                    new DialogueBeat(FriendDisplayName, "The stomach has a mucus shield. We stay safe by moving fast.", 4.4f));

                friendTalked = true;
                RaiseSignal(ClassroomStorySignals.FriendTalked, ClassroomStoryNpcIds.Friend);
                RefreshInteractionAvailability();
                yield break;
            }

            yield return PresentDialogueSequence(
                new DialogueBeat(FriendDisplayName, "Team reminder: respect acid, respect timing.", 2.3f));
        }

        private IEnumerator FriendReassureRoutine()
        {
            if (!friendTalked)
            {
                yield return FriendTalkRoutine();
                yield break;
            }

            relationshipMoment = ClassroomRelationshipMoment.ComfortedNia;
            yield return PresentDialogueSequence(
                new DialogueBeat("You", "We understand the chemistry. That makes it risky, but not random.", 2.9f),
                new DialogueBeat(FriendDisplayName, "That actually helps.", 1.9f),
                new DialogueBeat(FriendDisplayName, "Once you reach the small intestine, the real science starts.", 3.5f));
        }

        private IEnumerator FriendJokeRoutine()
        {
            if (!friendTalked)
            {
                friendTalked = true;
                RaiseSignal(ClassroomStorySignals.FriendTalked, ClassroomStoryNpcIds.Friend);
            }

            yield return PresentDialogueSequence(
                new DialogueBeat("You", "If I come back smelling like acid, I am filing an academic complaint.", 2.3f),
                new DialogueBeat(FriendDisplayName, "Please do not make that real.", 2.2f),
                new DialogueBeat(FriendDisplayName, "Still, good joke. Thanks for the laugh.", 2.7f));
        }

        private IEnumerator SkepticTalkRoutine()
        {
            if (!skepticTalked)
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat(SkepticDisplayName, "So the plan is to get swallowed on purpose for class?", 3.1f));

                var choice = string.Empty;
                yield return PresentChoice(
                    "How do you answer Theo?",
                    selected => choice = selected,
                    new ChoiceOptionConfig("irresponsible", "That is an irresponsible summary."),
                    new ChoiceOptionConfig("scores", "Still better than your test scores."),
                    new ChoiceOptionConfig("airway", "What if the rocket hits the airway?"));

                switch (choice)
                {
                    case "irresponsible":
                        yield return PresentDialogueSequence(
                            new DialogueBeat("You", "That is an irresponsible summary.", 2.0f),
                            new DialogueBeat(SkepticDisplayName, "Irresponsible and memorable. My specialty.", 2.3f));
                        break;

                    case "scores":
                        relationshipMoment = ClassroomRelationshipMoment.DebatedTheo;
                        yield return PresentDialogueSequence(
                            new DialogueBeat("You", "Still better than your test scores.", 1.9f),
                            new DialogueBeat(SkepticDisplayName, "Ouch. Fair point.", 2.0f));
                        break;

                    default:
                        yield return PresentDialogueSequence(
                            new DialogueBeat("You", "What if the rocket hits the airway?", 2.1f),
                            new DialogueBeat(SkepticDisplayName, "Good question. That is the right worry.", 1.8f));
                        break;
                }

                yield return PresentDialogueSequence(
                    new DialogueBeat(SkepticDisplayName, "That is why the epiglottis matters.", 2.0f),
                    new DialogueBeat(SkepticDisplayName, "When you swallow, the epiglottis helps block the airway and sends us to the food tube. Miss that, mission over.", 4.0f));

                skepticTalked = true;
                RaiseSignal(ClassroomStorySignals.SkepticTalked, ClassroomStoryNpcIds.Skeptic);
                RefreshInteractionAvailability();
                yield break;
            }

            yield return PresentDialogueSequence(
                new DialogueBeat(SkepticDisplayName, "I still think 'learning by swallowing' is a terrible slogan.", 2.5f));
        }

        private IEnumerator SkepticChallengeRoutine()
        {
            if (!skepticTalked)
            {
                yield return SkepticTalkRoutine();
                yield break;
            }

            relationshipMoment = ClassroomRelationshipMoment.DebatedTheo;
            yield return PresentDialogueSequence(
                new DialogueBeat("You", "You joke because this is too real.", 2.2f),
                new DialogueBeat(SkepticDisplayName, "True. Jokes are how I calm down.", 2.5f),
                new DialogueBeat(SkepticDisplayName, "But the throat rule is serious. Wrong tube means mission over.", 3.4f));
        }

        private IEnumerator SkepticAirwayRoutine()
        {
            if (!skepticTalked)
            {
                skepticTalked = true;
                RaiseSignal(ClassroomStorySignals.SkepticTalked, ClassroomStoryNpcIds.Skeptic);
            }

            yield return PresentDialogueSequence(
                new DialogueBeat("You", "Walk me through airway risk again.", 2.0f),
                new DialogueBeat(SkepticDisplayName, "Breathing and swallowing use the same space, which is bad design.", 2.6f),
                new DialogueBeat(SkepticDisplayName, "Epiglottis guards the airway. The food tube is our only lane.", 3.2f));
        }

        private IEnumerator BoardReadRoutine()
        {
            if (boardExamined)
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat("You", "The board still points to the same goal: small intestine, absorption zone.", 3.0f));
                yield break;
            }

            yield return PresentDialogueSequence(
                new DialogueBeat("You", "Mouth. Esophagus. Stomach. Pylorus. Small intestine.", 2.8f),
                new DialogueBeat("You", "The underlined note says: primary site of nutrient absorption.", 2.7f),
                new DialogueBeat("You", "At micro scale, villi turn tissue into topography.", 2.6f));

            boardExamined = true;
            RaiseSignal(ClassroomStorySignals.BoardExamined, "route.board");
            RefreshInteractionAvailability();
        }

        private IEnumerator DeskNotesRoutine()
        {
            if (deskExamined)
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat("You", "Same desk notes: launch window, stomach transit cap, intestine checkpoint.", 3.0f));
                yield break;
            }

            yield return PresentDialogueSequence(
                new DialogueBeat("You", "Mira's checklist is strict: throat alignment, stomach crossing under forty seconds, intestine telemetry online.", 4.0f),
                new DialogueBeat("You", "Every line is practical. No room for risky improvising.", 2.9f));

            deskExamined = true;
            RaiseSignal(ClassroomStorySignals.DeskExamined, "teacher.desk");
            RefreshInteractionAvailability();
        }

        private IEnumerator ShelfBookRoutine()
        {
            if (knowledgeBookUi != null)
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat("You", "I pull out the anatomy atlas and open the digestive section.", 2.7f));
                yield return knowledgeBookUi.OpenAndWait();
            }
            else
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat("You", "I pull out an anatomy atlas and skim digestive diagrams.", 2.7f));
            }

            if (shelfBookRead)
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat("You", "The atlas says the same thing: route discipline beats confidence.", 3.0f));
                yield break;
            }

            yield return PresentDialogueSequence(
                new DialogueBeat("You", "Cross-sections make the route easier to picture and navigate.", 2.8f),
                new DialogueBeat("You", "Mouth entry is setup. Reaching the intestine is the real goal.", 2.8f));

            shelfBookRead = true;
            RaiseSignal(ClassroomStorySignals.ShelfBookRead, "shelf.atlas");
            RefreshInteractionAvailability();
        }

        private IEnumerator ClockRoutine()
        {
            if (clockChecked)
            {
                yield return PresentDialogueSequence(
                    new DialogueBeat("You", "The clock keeps reminding me this mission is measured in seconds.", 3.0f));
                yield break;
            }

            yield return PresentDialogueSequence(
                new DialogueBeat("You", "The second hand makes it real: every segment has a time limit.", 3.2f),
                new DialogueBeat("You", "Throat alignment, stomach crossing, intestine handoff. No mistakes.", 2.8f));

            clockChecked = true;
            RaiseSignal(ClassroomStorySignals.ClockChecked, "wall.clock");
            RefreshInteractionAvailability();
        }

        private IEnumerator PresentDialogueSequence(params DialogueBeat[] beats)
        {
            if (beats == null)
            {
                yield break;
            }

            for (var index = 0; index < beats.Length; index++)
            {
                var beat = beats[index];
                if (string.IsNullOrWhiteSpace(beat.Body))
                {
                    continue;
                }

                yield return PresentDialogue(beat.Speaker, beat.Body, beat.DurationSeconds);
            }
        }

        private IEnumerator PresentDialogue(string speaker, string body, float durationSeconds)
        {
            if (channels == null)
            {
                yield break;
            }

            var speakerDisplay = string.IsNullOrWhiteSpace(speaker) ? "Narrator" : speaker;
            presentationController?.FocusOnSpeaker(speakerDisplay);

            var clipKey = IsNpcSpeaker(speakerDisplay)
                ? ClassroomStoryVoiceLibrary.BuildClipKey(speakerDisplay, body)
                : string.Empty;
            var moodTag = ClassroomStoryVoiceLibrary.ResolveMoodTag(speakerDisplay, body);
            var autoDelay = Mathf.Max(0.25f, durationSeconds);
            if (TryResolveVoiceDuration(clipKey, out var clipDuration) && clipDuration > 0f)
            {
                autoDelay = Mathf.Max(autoDelay, clipDuration + 0.05f);
            }

            channels.DialogueRequests?.Raise(new StoryDialogueRequest
            {
                SessionId = currentSessionId,
                RequestId = Guid.NewGuid().ToString("N"),
                SpeakerDisplayName = speakerDisplay,
                Body = body ?? string.Empty,
                SpeakerId = speakerDisplay,
                NodeId = "conversation",
                PortraitKey = clipKey,
                MoodTag = moodTag,
                AutoAdvance = true,
                AutoAdvanceDelaySeconds = autoDelay
            });

            yield return new WaitForSecondsRealtime(autoDelay + linePaddingSeconds);
        }

        private static bool TryResolveVoiceDuration(string clipKey, out float clipDuration)
        {
            clipDuration = 0f;
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                return false;
            }

            return ClassroomStoryVoiceLibrary.TryGetClipDuration(clipKey, out clipDuration);
        }

        private static bool IsNpcSpeaker(string speakerDisplay)
        {
            if (string.IsNullOrWhiteSpace(speakerDisplay))
            {
                return false;
            }

            return speakerDisplay.Equals(TeacherDisplayName, StringComparison.OrdinalIgnoreCase) ||
                   speakerDisplay.Equals(FriendDisplayName, StringComparison.OrdinalIgnoreCase) ||
                   speakerDisplay.Equals(SkepticDisplayName, StringComparison.OrdinalIgnoreCase) ||
                   speakerDisplay.Equals(ClassroomStoryNpcIds.Teacher, StringComparison.OrdinalIgnoreCase) ||
                   speakerDisplay.Equals(ClassroomStoryNpcIds.Friend, StringComparison.OrdinalIgnoreCase) ||
                   speakerDisplay.Equals(ClassroomStoryNpcIds.Skeptic, StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerator PresentChoice(
            string prompt,
            Action<string> onResolved,
            params ChoiceOptionConfig[] options)
        {
            if (channels == null || options == null || options.Length == 0)
            {
                onResolved?.Invoke(string.Empty);
                yield break;
            }

            pendingChoiceRequestId = Guid.NewGuid().ToString("N");
            pendingChoicePortId = string.Empty;
            pendingChoiceResolved = false;

            var request = new StoryChoiceRequest
            {
                SessionId = currentSessionId,
                RequestId = pendingChoiceRequestId,
                Prompt = prompt ?? string.Empty,
                NodeId = "conversation.choice",
                GraphId = "ClassroomConversation"
            };

            for (var index = 0; index < options.Length; index++)
            {
                request.Options.Add(new StoryChoiceOption
                {
                    PortId = options[index].PortId,
                    Label = options[index].Label,
                    IsAvailable = true
                });
            }

            channels.ChoiceRequests?.Raise(request);

            while (!pendingChoiceResolved)
            {
                yield return null;
            }

            var resolvedPortId = pendingChoicePortId;
            pendingChoiceRequestId = string.Empty;
            pendingChoicePortId = string.Empty;
            pendingChoiceResolved = false;
            onResolved?.Invoke(resolvedPortId);
        }

        private void ResetConversationState()
        {
            teacherTalked = false;
            teacherSafetyExplained = false;
            friendTalked = false;
            skepticTalked = false;
            boardExamined = false;
            deskExamined = false;
            shelfBookRead = false;
            clockChecked = false;
            volunteerConfirmed = false;
            labClearanceEarned = false;
            playerAttitude = ClassroomPlayerAttitude.None;
            relationshipMoment = ClassroomRelationshipMoment.None;
            RefreshInteractionAvailability();
        }

        private void RefreshInteractionAvailability()
        {
            teacherNpc?.SetOptionVisible(TeacherSafetyOptionId, teacherTalked);
            teacherNpc?.SetOptionEnabled(TeacherSafetyOptionId, teacherTalked);
            teacherNpc?.SetOptionVisible(TeacherVolunteerOptionId, teacherTalked);
            teacherNpc?.SetOptionEnabled(TeacherVolunteerOptionId, teacherTalked && HasEnoughScienceEvidence());

            if (teacherNpc != null)
            {
                var clearedForLab = volunteerConfirmed || labClearanceEarned;
                teacherNpc.SetOptionLabel(
                    TeacherVolunteerOptionId,
                    clearedForLab ? "Proceed to Lab" : "Volunteer");
            }

            friendNpc?.SetOptionEnabled(FriendReassureOptionId, true);
            friendNpc?.SetOptionEnabled(FriendJokeOptionId, true);
            skepticNpc?.SetOptionEnabled(SkepticChallengeOptionId, true);
            skepticNpc?.SetOptionEnabled(SkepticAirwayOptionId, true);

            boardInteractable?.SetOptionVisible(BoardReadOptionId, true);
            boardInteractable?.SetOptionEnabled(BoardReadOptionId, true);
            deskInteractable?.SetOptionVisible(DeskReviewOptionId, true);
            deskInteractable?.SetOptionEnabled(DeskReviewOptionId, true);
            shelfInteractable?.SetOptionVisible(ShelfBookOptionId, true);
            shelfInteractable?.SetOptionEnabled(ShelfBookOptionId, true);
            clockInteractable?.SetOptionVisible(ClockCheckOptionId, true);
            clockInteractable?.SetOptionEnabled(ClockCheckOptionId, true);
            RefreshObjectiveHint();
        }

        private void RefreshObjectiveHint()
        {
            if (objectivePresenter == null)
            {
                return;
            }

            if (labClearanceEarned || volunteerConfirmed)
            {
                objectivePresenter.SetObjective(
                    "Next: Go To The Lab",
                    "Return to Dr. Mira and choose Proceed to Lab if needed.",
                    "Use the classroom door to transition to LabMiniEntryScene.");
                return;
            }

            if (!teacherTalked)
            {
                objectivePresenter.SetObjective(
                    "Next: Start Briefing",
                    "Talk to Dr. Mira Sato at the teacher desk.",
                    "Choose Talk to unlock mission context.");
                return;
            }

            if (!HasEnoughScienceEvidence())
            {
                var lines = new List<string>(4);
                if (!teacherSafetyExplained)
                {
                    lines.Add("Ask Dr. Mira about safety and the throat handoff.");
                }

                if (!friendTalked)
                {
                    lines.Add("Talk to Nia Park about stomach and intestine risks.");
                }

                if (!skepticTalked)
                {
                    lines.Add("Talk to Theo Mercer about airway mistakes.");
                }

                if (!boardExamined || !deskExamined || !shelfBookRead || !clockChecked)
                {
                    lines.Add("Inspect classroom props for route evidence (board, desk, shelf, clock).");
                }

                if (lines.Count == 0)
                {
                    lines.Add("Collect at least two science clues before volunteering.");
                }

                objectivePresenter.SetObjective("Next: Gather Evidence", lines.ToArray());
                return;
            }

            objectivePresenter.SetObjective(
                "Next: Volunteer",
                "Talk to Dr. Mira again.",
                "Choose Volunteer to unlock lab clearance.");
        }

        private bool HasEnoughScienceEvidence()
        {
            if (EvidenceCount() < 2)
            {
                return false;
            }

            return teacherSafetyExplained || friendTalked || skepticTalked;
        }

        private int EvidenceCount()
        {
            var count = 0;
            if (teacherSafetyExplained)
            {
                count++;
            }

            if (friendTalked)
            {
                count++;
            }

            if (skepticTalked)
            {
                count++;
            }

            if (boardExamined)
            {
                count++;
            }

            if (deskExamined)
            {
                count++;
            }

            if (shelfBookRead)
            {
                count++;
            }

            if (clockChecked)
            {
                count++;
            }

            return count;
        }

        private string ResolveWhyMeLine()
        {
            return playerAttitude switch
            {
                ClassroomPlayerAttitude.Curious => "Because you keep asking smart questions.",
                ClassroomPlayerAttitude.Brave => "Because you stepped up and stayed to learn the risks.",
                ClassroomPlayerAttitude.Nervous => "Because you are scared but still thinking clearly.",
                _ => "Because you asked the right question at the right time."
            };
        }

        private string ResolveVolunteerApprovalLine()
        {
            if (relationshipMoment == ClassroomRelationshipMoment.ComfortedNia)
            {
                return "Good. Keep that empathy. It makes science safer and kinder.";
            }

            if (relationshipMoment == ClassroomRelationshipMoment.DebatedTheo)
            {
                return "Good. Keep the jokes, but keep airway facts even closer.";
            }

            if (relationshipMoment == ClassroomRelationshipMoment.PressedMira)
            {
                return "Good. Questions help when you also follow the rules.";
            }

            return "Good. Bring facts, not guesses.";
        }

        private void RaiseSignal(string signalId, string payload = null)
        {
            if (channels == null || string.IsNullOrWhiteSpace(signalId))
            {
                return;
            }

            channels.ExternalSignals?.Raise(new StoryExternalSignal
            {
                SessionId = currentSessionId,
                SignalId = signalId,
                Payload = payload ?? string.Empty
            });
        }

        private StoryNpcAgent ResolveNpc(string npcId)
        {
            if (npcRegistry != null)
            {
                var fromRegistry = npcRegistry.GetNpc(npcId);
                if (fromRegistry != null)
                {
                    return fromRegistry;
                }
            }

            var npcs = FindObjectsByType<StoryNpcAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < npcs.Length; index++)
            {
                if (npcs[index] != null && string.Equals(npcs[index].NpcId, npcId, StringComparison.Ordinal))
                {
                    return npcs[index];
                }
            }

            var fallbackName = ResolveFallbackNpcName(npcId);
            if (!string.IsNullOrWhiteSpace(fallbackName))
            {
                for (var index = 0; index < npcs.Length; index++)
                {
                    if (npcs[index] != null &&
                        string.Equals(npcs[index].gameObject.name, fallbackName, StringComparison.Ordinal))
                    {
                        return npcs[index];
                    }
                }
            }

            return null;
        }

        private static string ResolveFallbackNpcName(string npcId)
        {
            if (string.Equals(npcId, ClassroomStoryNpcIds.Teacher, StringComparison.Ordinal))
            {
                return "DrMira";
            }

            if (string.Equals(npcId, ClassroomStoryNpcIds.Friend, StringComparison.Ordinal))
            {
                return "NiaPark";
            }

            if (string.Equals(npcId, ClassroomStoryNpcIds.Skeptic, StringComparison.Ordinal))
            {
                return "TheoMercer";
            }

            return string.Empty;
        }

        private bool ReferencesRegisteredNpc(StoryNpcAgent agent)
        {
            return agent == teacherNpc || agent == friendNpc || agent == skepticNpc;
        }

        private static List<StoryNpcOptionDefinition> CreateTeacherOptions()
        {
            return new List<StoryNpcOptionDefinition>
            {
                CreateOption(TeacherTalkOptionId, "Talk", InteractionOptionSlot.Top),
                CreateOption(TeacherSafetyOptionId, "Ask Safety", InteractionOptionSlot.Left, false),
                CreateOption(TeacherVolunteerOptionId, "Volunteer", InteractionOptionSlot.Right, false)
            };
        }

        private static List<StoryNpcOptionDefinition> CreateFriendOptions()
        {
            return new List<StoryNpcOptionDefinition>
            {
                CreateOption(FriendTalkOptionId, "Talk", InteractionOptionSlot.Top),
                CreateOption(FriendReassureOptionId, "Reassure", InteractionOptionSlot.Left),
                CreateOption(FriendJokeOptionId, "Joke", InteractionOptionSlot.Right)
            };
        }

        private static List<StoryNpcOptionDefinition> CreateSkepticOptions()
        {
            return new List<StoryNpcOptionDefinition>
            {
                CreateOption(SkepticTalkOptionId, "Talk", InteractionOptionSlot.Top),
                CreateOption(SkepticChallengeOptionId, "Challenge", InteractionOptionSlot.Left),
                CreateOption(SkepticAirwayOptionId, "Ask Airway", InteractionOptionSlot.Right)
            };
        }

        private static StoryNpcOptionDefinition CreateOption(
            string id,
            string label,
            InteractionOptionSlot slot,
            bool visible = true)
        {
            return new StoryNpcOptionDefinition
            {
                id = id,
                label = label,
                slot = slot,
                interactionId = string.Concat(StoryNamespace, ".", id),
                visible = visible,
                enabled = visible
            };
        }

        private static void EnsureOptionsList(InteractableItem interactable)
        {
            if (interactable.options == null)
            {
                interactable.options = new List<InteractionOption>();
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
    }
}
