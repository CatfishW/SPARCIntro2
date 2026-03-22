using System.Collections.Generic;
using ModularStoryFlow.Runtime.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public sealed class BedroomStoryChoicePresenter : MonoBehaviour
    {
        private readonly List<Button> optionButtons = new List<Button>();

        private RectTransform panelRect;
        private CanvasGroup canvasGroup;
        private Text promptText;
        private StoryChoiceRequest activeChoice;
        private ModularStoryFlow.Runtime.Channels.StoryChoiceSelectionChannel selectionChannel;

        public bool IsVisible => panelRect != null && panelRect.gameObject.activeSelf && canvasGroup != null && canvasGroup.alpha > 0.001f;

        public void Configure(ModularStoryFlow.Runtime.Channels.StoryChoiceSelectionChannel channel)
        {
            selectionChannel = channel;
        }

        private void Awake()
        {
            EnsureUi();
            Clear();
        }

        public void Present(StoryChoiceRequest request)
        {
            EnsureUi();
            activeChoice = request;
            panelRect.gameObject.SetActive(true);
            canvasGroup.alpha = 1f;
            promptText.text = request?.Prompt ?? string.Empty;

            for (var index = 0; index < optionButtons.Count; index++)
            {
                Destroy(optionButtons[index].gameObject);
            }

            optionButtons.Clear();

            if (request == null)
            {
                return;
            }

            for (var index = 0; index < request.Options.Count; index++)
            {
                var option = request.Options[index];
                var button = CreateOptionButton(index, option);
                optionButtons.Add(button);
            }
        }

        public void Clear()
        {
            activeChoice = null;
            if (panelRect != null)
            {
                panelRect.gameObject.SetActive(false);
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }
        }

        private Button CreateOptionButton(int index, StoryChoiceOption option)
        {
            var buttonObject = BedroomStoryUiFactory.CreateUiObject($"Choice_{index}", panelRect);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(540f, 44f);
            rect.anchoredPosition = new Vector2(0f, -58f - (index * 52f));

            var image = buttonObject.AddComponent<Image>();
            image.color = option.IsAvailable ? new Color(0.08f, 0.11f, 0.16f, 0.88f) : new Color(0.08f, 0.08f, 0.08f, 0.54f);

            var button = buttonObject.AddComponent<Button>();
            button.interactable = option.IsAvailable;

            var label = BedroomStoryUiFactory.CreateText("Label", rect, 22, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.text = option.Label;
            label.color = option.IsAvailable ? Color.white : new Color(1f, 1f, 1f, 0.35f);

            var capturedIndex = index;
            var capturedOption = option;
            button.onClick.AddListener(() =>
            {
                if (!capturedOption.IsAvailable || activeChoice == null)
                {
                    return;
                }

                selectionChannel?.Raise(new StoryChoiceSelection
                {
                    SessionId = activeChoice.SessionId,
                    RequestId = activeChoice.RequestId,
                    PortId = capturedOption.PortId,
                    OptionIndex = capturedIndex
                });
                Clear();
            });

            return button;
        }

        private void EnsureUi()
        {
            if (panelRect != null)
            {
                return;
            }

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 610;

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
            panelRect.sizeDelta = new Vector2(620f, 280f);
            panelRect.anchoredPosition = new Vector2(0f, 260f);

            var background = panelRect.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.55f);
            background.raycastTarget = false;

            canvasGroup = panelRect.gameObject.AddComponent<CanvasGroup>();
            promptText = BedroomStoryUiFactory.CreateText("Prompt", panelRect, 24, TextAnchor.MiddleCenter);
            promptText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            promptText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            promptText.rectTransform.pivot = new Vector2(0.5f, 1f);
            promptText.rectTransform.sizeDelta = new Vector2(560f, 44f);
            promptText.rectTransform.anchoredPosition = new Vector2(0f, -10f);
        }
    }
}
