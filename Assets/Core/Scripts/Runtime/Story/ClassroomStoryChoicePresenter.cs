using System;
using System.Collections.Generic;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public sealed class ClassroomStoryChoicePresenter : MonoBehaviour
    {
        private sealed class ChoiceView
        {
            public StoryChoiceOption Option;
            public RectTransform Rect;
            public Button Button;
            public Image Background;
            public Text Label;
            public Text KeyRing;
            public Text KeyNumber;
            public Image Accent;
            public CanvasGroup CanvasGroup;
        }

        private readonly List<ChoiceView> optionViews = new List<ChoiceView>(8);

        private RectTransform panelRect;
        private CanvasGroup canvasGroup;
        private Text promptText;

        private StoryChoiceRequest activeChoice;
        private StoryChoiceSelectionChannel selectionChannel;
        private int selectedOptionIndex = -1;
        private float pulseTimer;
        private bool visible;

        public bool IsVisible => visible || (canvasGroup != null && canvasGroup.alpha > 0.001f);

        public void Configure(StoryChoiceSelectionChannel channel)
        {
            selectionChannel = channel;
        }

        private void Awake()
        {
            EnsureUi();
            HideImmediate();
        }

        private void Update()
        {
            TickFade(Time.unscaledDeltaTime);

            if (!visible || activeChoice == null)
            {
                return;
            }

            HandleKeyboardInput();
            TickSelectionPulse(Time.unscaledDeltaTime);
        }

        public void Present(StoryChoiceRequest request)
        {
            EnsureUi();
            ClearOptionViews();

            activeChoice = request;
            selectedOptionIndex = -1;
            pulseTimer = 0f;
            visible = true;
            panelRect.gameObject.SetActive(true);

            promptText.text = request?.Prompt ?? string.Empty;
            promptText.gameObject.SetActive(!string.IsNullOrWhiteSpace(promptText.text));

            if (request == null || request.Options == null || request.Options.Count == 0)
            {
                return;
            }

            for (var index = 0; index < request.Options.Count; index++)
            {
                var option = request.Options[index];
                var view = CreateOptionView(index, option);
                optionViews.Add(view);
            }

            LayoutOptionViews();
            SetSelectedIndex(FindFirstAvailableOptionIndex());
            RefreshOptionVisuals();
        }

        public void Clear()
        {
            activeChoice = null;
            visible = false;
            selectedOptionIndex = -1;
            ClearOptionViews();

            if (promptText != null)
            {
                promptText.text = string.Empty;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            if (panelRect != null)
            {
                panelRect.gameObject.SetActive(false);
            }
        }

        public void HideImmediate()
        {
            EnsureUi();
            activeChoice = null;
            visible = false;
            selectedOptionIndex = -1;
            ClearOptionViews();

            if (promptText != null)
            {
                promptText.text = string.Empty;
                promptText.gameObject.SetActive(false);
            }

            canvasGroup.alpha = 0f;
            panelRect.gameObject.SetActive(false);
        }

        private void TickFade(float deltaTime)
        {
            if (canvasGroup == null)
            {
                return;
            }

            var target = visible ? 1f : 0f;
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, target, deltaTime / 0.14f);
        }

        private void TickSelectionPulse(float deltaTime)
        {
            if (optionViews.Count == 0)
            {
                return;
            }

            pulseTimer += deltaTime * 4.2f;
            RefreshOptionVisuals();
        }

        private void HandleKeyboardInput()
        {
            if (optionViews.Count == 0)
            {
                return;
            }

            if (TryHandleNumberInput())
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            {
                MoveSelection(-1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            {
                MoveSelection(1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                SubmitSelection(selectedOptionIndex);
            }
        }

        private bool TryHandleNumberInput()
        {
            for (var index = 0; index < optionViews.Count && index < 9; index++)
            {
                var key = KeyCode.Alpha1 + index;
                var keypad = KeyCode.Keypad1 + index;
                if (!Input.GetKeyDown(key) && !Input.GetKeyDown(keypad))
                {
                    continue;
                }

                if (!optionViews[index].Option.IsAvailable)
                {
                    return true;
                }

                SetSelectedIndex(index);
                SubmitSelection(index);
                return true;
            }

            return false;
        }

        private void MoveSelection(int direction)
        {
            if (optionViews.Count == 0)
            {
                return;
            }

            var start = selectedOptionIndex;
            if (start < 0 || start >= optionViews.Count)
            {
                start = direction >= 0 ? -1 : 0;
            }

            var probe = start;
            for (var step = 0; step < optionViews.Count; step++)
            {
                probe += direction;
                if (probe < 0)
                {
                    probe = optionViews.Count - 1;
                }
                else if (probe >= optionViews.Count)
                {
                    probe = 0;
                }

                if (!optionViews[probe].Option.IsAvailable)
                {
                    continue;
                }

                SetSelectedIndex(probe);
                return;
            }
        }

        private int FindFirstAvailableOptionIndex()
        {
            for (var index = 0; index < optionViews.Count; index++)
            {
                if (optionViews[index].Option.IsAvailable)
                {
                    return index;
                }
            }

            return optionViews.Count > 0 ? 0 : -1;
        }

        private void SetSelectedIndex(int index)
        {
            var clamped = index;
            if (clamped >= optionViews.Count)
            {
                clamped = optionViews.Count - 1;
            }

            selectedOptionIndex = Mathf.Max(-1, clamped);
            RefreshOptionVisuals();
        }

        private void SubmitSelection(int index)
        {
            if (activeChoice == null || index < 0 || index >= optionViews.Count)
            {
                return;
            }

            var view = optionViews[index];
            if (!view.Option.IsAvailable)
            {
                return;
            }

            selectionChannel?.Raise(new StoryChoiceSelection
            {
                SessionId = activeChoice.SessionId,
                RequestId = activeChoice.RequestId,
                PortId = view.Option.PortId,
                OptionIndex = index
            });

            Clear();
        }

        private void RefreshOptionVisuals()
        {
            if (optionViews.Count == 0)
            {
                return;
            }

            var pulse = 0.72f + (Mathf.Sin(pulseTimer) * 0.28f);
            for (var index = 0; index < optionViews.Count; index++)
            {
                var view = optionViews[index];
                var isSelected = index == selectedOptionIndex;
                var isAvailable = view.Option.IsAvailable;

                if (!isAvailable)
                {
                    view.CanvasGroup.alpha = 0.34f;
                    view.Label.color = new Color(1f, 1f, 1f, 0.45f);
                    view.KeyRing.color = new Color(1f, 1f, 1f, 0.3f);
                    view.KeyNumber.color = new Color(1f, 1f, 1f, 0.35f);
                    view.Background.color = new Color(0.03f, 0.04f, 0.05f, 0.08f);
                    view.Accent.color = new Color(0.8f, 0.8f, 0.8f, 0f);
                    view.Rect.localScale = Vector3.one;
                    continue;
                }

                view.CanvasGroup.alpha = 1f;
                view.Background.color = isSelected
                    ? new Color(0.08f, 0.14f, 0.2f, 0.22f)
                    : new Color(0.01f, 0.02f, 0.04f, 0.06f);
                view.Label.color = isSelected
                    ? new Color(1f, 1f, 1f, 1f)
                    : new Color(1f, 1f, 1f, 0.74f);
                view.KeyRing.color = isSelected
                    ? new Color(1f, 1f, 1f, 0.95f)
                    : new Color(1f, 1f, 1f, 0.68f);
                view.KeyNumber.color = isSelected
                    ? new Color(0.93f, 0.96f, 1f, 1f)
                    : new Color(1f, 1f, 1f, 0.86f);
                view.Accent.color = isSelected
                    ? new Color(0.84f, 0.92f, 1f, pulse)
                    : new Color(0.84f, 0.92f, 1f, 0.1f);
                view.Rect.localScale = isSelected ? new Vector3(1.02f, 1.02f, 1f) : Vector3.one;
            }
        }

        private ChoiceView CreateOptionView(int index, StoryChoiceOption option)
        {
            var buttonObject = BedroomStoryUiFactory.CreateUiObject($"Choice_{index}", panelRect);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(420f, 78f);
            rect.anchoredPosition = new Vector2(0f, 12f);

            var background = buttonObject.AddComponent<Image>();
            background.color = new Color(0.02f, 0.03f, 0.05f, 0.14f);
            background.raycastTarget = true;

            var canvas = buttonObject.AddComponent<CanvasGroup>();

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = BuildButtonColors();
            button.interactable = option.IsAvailable;

            var keyRoot = BedroomStoryUiFactory.CreateUiObject("KeyHint", rect).GetComponent<RectTransform>();
            keyRoot.anchorMin = new Vector2(0f, 0.5f);
            keyRoot.anchorMax = new Vector2(0f, 0.5f);
            keyRoot.pivot = new Vector2(0.5f, 0.5f);
            keyRoot.sizeDelta = new Vector2(40f, 40f);
            keyRoot.anchoredPosition = new Vector2(34f, 0f);

            var keyRing = BedroomStoryUiFactory.CreateText("KeyRing", keyRoot, 34, TextAnchor.MiddleCenter);
            keyRing.rectTransform.anchorMin = Vector2.zero;
            keyRing.rectTransform.anchorMax = Vector2.one;
            keyRing.rectTransform.offsetMin = Vector2.zero;
            keyRing.rectTransform.offsetMax = Vector2.zero;
            keyRing.text = "○";
            keyRing.color = new Color(1f, 1f, 1f, 0.72f);
            keyRing.raycastTarget = false;

            var keyNumber = BedroomStoryUiFactory.CreateText("KeyNumber", keyRoot, 16, TextAnchor.MiddleCenter);
            keyNumber.rectTransform.anchorMin = Vector2.zero;
            keyNumber.rectTransform.anchorMax = Vector2.one;
            keyNumber.rectTransform.offsetMin = new Vector2(0f, -1f);
            keyNumber.rectTransform.offsetMax = Vector2.zero;
            keyNumber.text = (index + 1).ToString();
            keyNumber.color = new Color(0.94f, 0.95f, 1f, 0.96f);
            keyNumber.fontStyle = FontStyle.Bold;
            keyNumber.raycastTarget = false;

            var label = BedroomStoryUiFactory.CreateText("Label", rect, 34, TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(64f, 0f);
            label.rectTransform.offsetMax = new Vector2(-14f, 0f);
            label.text = option.Label;
            label.font = BedroomStoryUiFactory.DefaultSerifFont;
            label.fontStyle = FontStyle.Italic;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.lineSpacing = 1.05f;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 18;
            label.resizeTextMaxSize = 32;
            label.raycastTarget = false;
            ApplyTextShadow(label, 0.92f, new Vector2(1.4f, -1.2f));

            var accent = BedroomStoryUiFactory.CreateImage("Accent", rect, new Color(0.84f, 0.92f, 1f, 0.1f));
            accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            accent.rectTransform.anchorMax = new Vector2(1f, 0f);
            accent.rectTransform.pivot = new Vector2(0.5f, 0f);
            accent.rectTransform.sizeDelta = new Vector2(0f, 2f);
            accent.rectTransform.anchoredPosition = Vector2.zero;
            accent.raycastTarget = false;

            var capturedIndex = index;
            button.onClick.AddListener(() =>
            {
                SetSelectedIndex(capturedIndex);
                SubmitSelection(capturedIndex);
            });

            var hoverRelay = buttonObject.AddComponent<ChoiceHoverRelay>();
            hoverRelay.Initialize(() => SetSelectedIndex(capturedIndex));

            return new ChoiceView
            {
                Option = option,
                Rect = rect,
                Button = button,
                Background = background,
                Label = label,
                KeyRing = keyRing,
                KeyNumber = keyNumber,
                Accent = accent,
                CanvasGroup = canvas
            };
        }

        private static ColorBlock BuildButtonColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.98f);
            colors.pressedColor = new Color(0.88f, 0.93f, 1f, 0.92f);
            colors.selectedColor = new Color(1f, 1f, 1f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.42f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            return colors;
        }

        private void LayoutOptionViews()
        {
            if (optionViews.Count == 0)
            {
                return;
            }

            var optionCount = optionViews.Count;
            var spacing = optionCount <= 2 ? 64f : 44f;
            var availableWidth = 1460f;
            var optionWidth = Mathf.Clamp((availableWidth - (spacing * (optionCount - 1))) / optionCount, 278f, 620f);
            var totalWidth = (optionWidth * optionCount) + (spacing * (optionCount - 1));
            var xStart = -totalWidth * 0.5f + optionWidth * 0.5f;

            for (var index = 0; index < optionViews.Count; index++)
            {
                var rect = optionViews[index].Rect;
                rect.sizeDelta = new Vector2(optionWidth, 78f);
                rect.anchoredPosition = new Vector2(xStart + (index * (optionWidth + spacing)), 8f);
            }
        }

        private void ClearOptionViews()
        {
            for (var index = 0; index < optionViews.Count; index++)
            {
                var view = optionViews[index];
                if (view?.Rect != null)
                {
                    Destroy(view.Rect.gameObject);
                }
            }

            optionViews.Clear();
        }

        private static void ApplyTextShadow(Text text, float alpha, Vector2 offset)
        {
            if (text == null)
            {
                return;
            }

            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, alpha);
            shadow.effectDistance = offset;
            shadow.useGraphicAlpha = true;
        }

        private void EnsureUi()
        {
            if (panelRect != null)
            {
                return;
            }

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 705;

            var rootRect = GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = Vector2.zero;
            rootRect.localScale = Vector3.one;

            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            panelRect = BedroomStoryUiFactory.CreateUiObject("ChoicePanel", transform).GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.sizeDelta = new Vector2(1560f, 224f);
            panelRect.anchoredPosition = new Vector2(0f, 62f);

            canvasGroup = panelRect.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;

            promptText = BedroomStoryUiFactory.CreateText("Prompt", panelRect, 24, TextAnchor.MiddleCenter);
            promptText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            promptText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            promptText.rectTransform.pivot = new Vector2(0.5f, 1f);
            promptText.rectTransform.sizeDelta = new Vector2(1400f, 48f);
            promptText.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            promptText.color = new Color(1f, 1f, 1f, 0.86f);
            promptText.font = BedroomStoryUiFactory.DefaultSerifFont;
            promptText.fontStyle = FontStyle.Italic;
            promptText.horizontalOverflow = HorizontalWrapMode.Wrap;
            promptText.verticalOverflow = VerticalWrapMode.Truncate;
            promptText.resizeTextForBestFit = true;
            promptText.resizeTextMinSize = 16;
            promptText.resizeTextMaxSize = 24;
            ApplyTextShadow(promptText, 0.9f, new Vector2(1.6f, -1.4f));
        }

        private sealed class ChoiceHoverRelay : MonoBehaviour, IPointerEnterHandler
        {
            private Action onHover;

            public void Initialize(Action callback)
            {
                onHover = callback;
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                onHover?.Invoke();
            }
        }
    }
}
