using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ClassroomBodyKnowledgeQuizUi : MonoBehaviour
    {
        [Serializable]
        private sealed class QuizQuestion
        {
            public string prompt;
            public string[] options;
            public int answerIndex;
            public string explanation;
        }

        [SerializeField] private ClassroomPlayerControlLock controlLock;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PanelSettings panelSettings;
        [SerializeField] private bool closeOnEscape = true;
        [SerializeField, Min(1)] private int rounds = 3;
        [SerializeField, Min(5f)] private float answerTimeoutSeconds = 18f;
        [SerializeField] private List<QuizQuestion> questionBank = new List<QuizQuestion>
        {
            new QuizQuestion
            {
                prompt = "Which route keeps the mini rocket out of the airway?",
                options = new[] { "Trachea", "Esophagus", "Nasal cavity", "Bronchi" },
                answerIndex = 1,
                explanation = "The mission route is mouth -> esophagus -> stomach."
            },
            new QuizQuestion
            {
                prompt = "What structure helps redirect swallowed material away from the airway?",
                options = new[] { "Epiglottis", "Uvula", "Diaphragm", "Larynx cartilage" },
                answerIndex = 0,
                explanation = "The epiglottis helps protect the airway during swallowing."
            },
            new QuizQuestion
            {
                prompt = "Where is nutrient absorption strongest in this mission briefing?",
                options = new[] { "Stomach", "Large intestine", "Small intestine", "Esophagus" },
                answerIndex = 2,
                explanation = "Small intestine villi and microvilli provide high surface area."
            },
            new QuizQuestion
            {
                prompt = "Why is stomach timing critical for the mini rocket?",
                options = new[] { "No oxygen", "High acidity", "No movement", "No mucus" },
                answerIndex = 1,
                explanation = "Acidic stomach conditions are survivable only with quick transit."
            },
            new QuizQuestion
            {
                prompt = "The classroom board marks the mission's final learning zone as:",
                options = new[] { "Mouth cavity", "Pyloric sphincter", "Small intestine", "Trachea" },
                answerIndex = 2,
                explanation = "The target zone is the small intestine absorption region."
            }
        };

        private VisualElement overlay;
        private Label titleLabel;
        private Label subtitleLabel;
        private Label roundLabel;
        private Label timerLabel;
        private VisualElement timerFill;
        private Label questionLabel;
        private readonly List<Button> optionButtons = new List<Button>(4);
        private Label feedbackLabel;
        private Label scoreLabel;
        private Button closeButton;

        private bool built;
        private bool closingRequested;
        private int selectedAnswerIndex;
        private bool answered;
        private Coroutine activeRoutine;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            EnsureBuilt();
            HideImmediate();
        }

        private void Update()
        {
            if (!IsOpen || !closeOnEscape)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                RequestClose();
            }
        }

        public IEnumerator OpenQuizAndWait(string hostDisplayName)
        {
            Open(hostDisplayName);
            while (IsOpen)
            {
                yield return null;
            }
        }

        public void Open(string hostDisplayName)
        {
            EnsureBuilt();
            ResolveRuntimeReferences();
            if (overlay == null)
            {
                return;
            }

            if (uiDocument != null)
            {
                uiDocument.sortingOrder = Mathf.Max(uiDocument.sortingOrder, 700);
            }

            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }

            controlLock?.Acquire(unlockCursor: true);
            overlay.style.display = DisplayStyle.Flex;
            IsOpen = true;
            closingRequested = false;
            activeRoutine = StartCoroutine(RunQuizRoutine(hostDisplayName));
        }

        public void RequestClose()
        {
            closingRequested = true;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }

            overlay.style.display = DisplayStyle.None;
            IsOpen = false;
            controlLock?.Release();
        }

        private void HideImmediate()
        {
            EnsureBuilt();
            if (overlay != null)
            {
                overlay.style.display = DisplayStyle.None;
            }

            IsOpen = false;
        }

        private IEnumerator RunQuizRoutine(string hostDisplayName)
        {
            var speaker = string.IsNullOrWhiteSpace(hostDisplayName) ? "Classroom Challenge" : hostDisplayName;
            titleLabel.text = "Bio Knowledge Sprint";
            subtitleLabel.text = $"{speaker} triggered a hidden challenge";
            feedbackLabel.text = "Countdown started...";
            scoreLabel.text = "Score: 0";
            closeButton.style.display = DisplayStyle.None;
            closeButton.SetEnabled(false);

            for (var count = 3; count >= 1; count--)
            {
                if (closingRequested)
                {
                    Close();
                    yield break;
                }

                roundLabel.text = $"Starting in {count}";
                timerLabel.text = string.Empty;
                timerFill.style.width = new Length(100f, LengthUnit.Percent);
                questionLabel.text = "Get ready. Keep responses quick and precise.";
                SetOptionsVisible(false);
                yield return new WaitForSecondsRealtime(0.9f);
            }

            var score = 0;
            var asked = 0;
            var shuffled = BuildShuffledQuestions();
            var totalRounds = Mathf.Clamp(rounds, 1, shuffled.Count);

            for (var roundIndex = 0; roundIndex < totalRounds; roundIndex++)
            {
                if (closingRequested)
                {
                    Close();
                    yield break;
                }

                var question = shuffled[roundIndex];
                if (question == null || question.options == null || question.options.Length == 0)
                {
                    continue;
                }

                asked++;
                roundLabel.text = $"Round {roundIndex + 1}/{totalRounds}";
                questionLabel.text = question.prompt ?? string.Empty;
                feedbackLabel.text = "Choose the best answer.";
                RenderOptions(question.options);
                SetOptionsInteractable(true);

                selectedAnswerIndex = -1;
                answered = false;
                var remaining = Mathf.Max(5f, answerTimeoutSeconds);
                while (!answered && remaining > 0f && !closingRequested)
                {
                    remaining -= Time.unscaledDeltaTime;
                    var normalized = Mathf.Clamp01(remaining / Mathf.Max(5f, answerTimeoutSeconds));
                    timerFill.style.width = new Length(normalized * 100f, LengthUnit.Percent);
                    timerLabel.text = $"{Mathf.CeilToInt(Mathf.Max(0f, remaining))}s";
                    yield return null;
                }

                if (closingRequested)
                {
                    Close();
                    yield break;
                }

                SetOptionsInteractable(false);
                var correct = answered && selectedAnswerIndex == question.answerIndex;
                if (correct)
                {
                    score++;
                }

                scoreLabel.text = $"Score: {score}";
                feedbackLabel.text = correct
                    ? $"Correct. {question.explanation}"
                    : $"Answer: {SafeOption(question.options, question.answerIndex)}. {question.explanation}";
                yield return new WaitForSecondsRealtime(1.25f);
            }

            var grade = asked > 0 ? Mathf.RoundToInt((score / (float)asked) * 100f) : 0;
            roundLabel.text = "Quiz Complete";
            timerLabel.text = string.Empty;
            timerFill.style.width = new Length(100f, LengthUnit.Percent);
            questionLabel.text = grade >= 80
                ? "Excellent. You are ready for the lab handoff."
                : "Review the classroom clues and try again.";
            feedbackLabel.text = $"Final score: {score}/{Mathf.Max(1, asked)} ({grade}%)";
            SetOptionsVisible(false);
            closeButton.style.display = DisplayStyle.Flex;
            closeButton.SetEnabled(true);

            while (!closingRequested)
            {
                yield return null;
            }

            Close();
        }

        private List<QuizQuestion> BuildShuffledQuestions()
        {
            var valid = new List<QuizQuestion>();
            for (var index = 0; index < questionBank.Count; index++)
            {
                var question = questionBank[index];
                if (question == null || string.IsNullOrWhiteSpace(question.prompt))
                {
                    continue;
                }

                if (question.options == null || question.options.Length == 0)
                {
                    continue;
                }

                valid.Add(question);
            }

            for (var index = valid.Count - 1; index > 0; index--)
            {
                var swapIndex = UnityEngine.Random.Range(0, index + 1);
                (valid[index], valid[swapIndex]) = (valid[swapIndex], valid[index]);
            }

            return valid;
        }

        private void RenderOptions(string[] options)
        {
            var count = Mathf.Min(optionButtons.Count, options.Length);
            for (var index = 0; index < optionButtons.Count; index++)
            {
                var button = optionButtons[index];
                if (button == null)
                {
                    continue;
                }

                var visible = index < count;
                button.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                {
                    button.text = $"{index + 1}. {options[index]}";
                }
            }
        }

        private void SetOptionsVisible(bool visible)
        {
            for (var index = 0; index < optionButtons.Count; index++)
            {
                if (optionButtons[index] != null)
                {
                    optionButtons[index].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
        }

        private void SetOptionsInteractable(bool value)
        {
            for (var index = 0; index < optionButtons.Count; index++)
            {
                optionButtons[index]?.SetEnabled(value);
            }
        }

        private string SafeOption(string[] options, int index)
        {
            if (options == null || index < 0 || index >= options.Length)
            {
                return "Unknown";
            }

            return options[index];
        }

        private void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            uiDocument = uiDocument != null ? uiDocument : GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
            }

            if (uiDocument == null)
            {
                return;
            }

            if (uiDocument.panelSettings == null)
            {
                if (panelSettings != null)
                {
                    uiDocument.panelSettings = panelSettings;
                }
                else
                {
                    var docs = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    for (var index = 0; index < docs.Length; index++)
                    {
                        var candidate = docs[index];
                        if (candidate == null || candidate == uiDocument || candidate.panelSettings == null)
                        {
                            continue;
                        }

                        uiDocument.panelSettings = candidate.panelSettings;
                        break;
                    }

                    if (uiDocument.panelSettings == null)
                    {
                        panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                        uiDocument.panelSettings = panelSettings;
                    }
                }
            }

            uiDocument.sortingOrder = Mathf.Max(uiDocument.sortingOrder, 700);

            var root = uiDocument.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1f;
            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.right = 0f;
            root.style.top = 0f;
            root.style.bottom = 0f;

            overlay = new VisualElement();
            overlay.style.flexGrow = 1f;
            overlay.style.display = DisplayStyle.None;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.backgroundColor = new Color(0.02f, 0.04f, 0.08f, 0.78f);

            var panel = new VisualElement();
            panel.style.width = new Length(52f, LengthUnit.Percent);
            panel.style.maxWidth = 860f;
            panel.style.minWidth = 620f;
            panel.style.paddingLeft = 18f;
            panel.style.paddingRight = 18f;
            panel.style.paddingTop = 16f;
            panel.style.paddingBottom = 16f;
            panel.style.backgroundColor = new Color(0.06f, 0.11f, 0.18f, 0.97f);
            panel.style.borderTopLeftRadius = 14f;
            panel.style.borderTopRightRadius = 14f;
            panel.style.borderBottomLeftRadius = 14f;
            panel.style.borderBottomRightRadius = 14f;
            panel.style.flexDirection = FlexDirection.Column;

            titleLabel = CreateLabel(27, FontStyle.Bold, new Color(0.93f, 0.97f, 1f, 1f));
            subtitleLabel = CreateLabel(14, FontStyle.Italic, new Color(0.74f, 0.85f, 0.97f, 0.95f));
            roundLabel = CreateLabel(18, FontStyle.Bold, new Color(0.99f, 0.92f, 0.72f, 1f));
            questionLabel = CreateLabel(22, FontStyle.Bold, Color.white);
            questionLabel.style.whiteSpace = WhiteSpace.Normal;
            questionLabel.style.marginTop = 8f;
            questionLabel.style.marginBottom = 10f;

            var timerRow = new VisualElement();
            timerRow.style.flexDirection = FlexDirection.Row;
            timerRow.style.alignItems = Align.Center;
            timerRow.style.marginTop = 8f;
            timerRow.style.marginBottom = 10f;

            var timerTrack = new VisualElement();
            timerTrack.style.flexGrow = 1f;
            timerTrack.style.height = 10f;
            timerTrack.style.marginRight = 10f;
            timerTrack.style.backgroundColor = new Color(0.13f, 0.19f, 0.27f, 1f);
            timerTrack.style.borderTopLeftRadius = 6f;
            timerTrack.style.borderTopRightRadius = 6f;
            timerTrack.style.borderBottomLeftRadius = 6f;
            timerTrack.style.borderBottomRightRadius = 6f;

            timerFill = new VisualElement();
            timerFill.style.height = 10f;
            timerFill.style.backgroundColor = new Color(0.2f, 0.68f, 0.95f, 1f);
            timerFill.style.borderTopLeftRadius = 6f;
            timerFill.style.borderTopRightRadius = 6f;
            timerFill.style.borderBottomLeftRadius = 6f;
            timerFill.style.borderBottomRightRadius = 6f;
            timerTrack.Add(timerFill);

            timerLabel = CreateLabel(16, FontStyle.Bold, new Color(0.89f, 0.94f, 1f, 1f));
            timerLabel.style.width = 58f;
            timerLabel.style.unityTextAlign = TextAnchor.MiddleRight;

            timerRow.Add(timerTrack);
            timerRow.Add(timerLabel);

            var optionContainer = new VisualElement();
            optionContainer.style.flexDirection = FlexDirection.Column;
            optionContainer.style.marginTop = 4f;
            optionContainer.style.marginBottom = 10f;

            optionButtons.Clear();
            for (var index = 0; index < 4; index++)
            {
                var choiceIndex = index;
                var button = new Button(() =>
                {
                    selectedAnswerIndex = choiceIndex;
                    answered = true;
                });

                button.style.height = 38f;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.paddingLeft = 12f;
                button.style.backgroundColor = new Color(0.12f, 0.22f, 0.34f, 0.98f);
                button.style.color = Color.white;
                button.style.marginBottom = 8f;
                optionButtons.Add(button);
                optionContainer.Add(button);
            }

            feedbackLabel = CreateLabel(16, FontStyle.Italic, new Color(0.84f, 0.95f, 1f, 0.98f));
            feedbackLabel.style.whiteSpace = WhiteSpace.Normal;
            feedbackLabel.style.marginTop = 4f;

            scoreLabel = CreateLabel(18, FontStyle.Bold, new Color(0.98f, 0.88f, 0.6f, 1f));
            scoreLabel.style.marginTop = 8f;

            closeButton = new Button(RequestClose) { text = "Close Quiz" };
            closeButton.style.marginTop = 14f;
            closeButton.style.height = 36f;
            closeButton.style.width = 180f;
            closeButton.style.alignSelf = Align.FlexEnd;
            closeButton.style.backgroundColor = new Color(0.24f, 0.16f, 0.16f, 1f);
            closeButton.style.color = Color.white;

            panel.Add(titleLabel);
            panel.Add(subtitleLabel);
            panel.Add(roundLabel);
            panel.Add(timerRow);
            panel.Add(questionLabel);
            panel.Add(optionContainer);
            panel.Add(feedbackLabel);
            panel.Add(scoreLabel);
            panel.Add(closeButton);

            overlay.Add(panel);
            root.Add(overlay);
            built = true;
        }

        private void ResolveRuntimeReferences()
        {
            if (controlLock == null)
            {
                controlLock = GetComponentInParent<ClassroomPlayerControlLock>();
            }

            if (controlLock == null)
            {
                controlLock = FindFirstObjectByType<ClassroomPlayerControlLock>();
            }
        }

        private static Label CreateLabel(int fontSize, FontStyle fontStyle, Color color)
        {
            var label = new Label();
            label.style.fontSize = fontSize;
            label.style.unityFontStyleAndWeight = fontStyle;
            label.style.color = color;
            return label;
        }
    }
}
