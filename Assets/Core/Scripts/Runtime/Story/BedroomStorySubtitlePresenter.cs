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
    public sealed class BedroomStorySubtitlePresenter : MonoBehaviour
    {
        private const float FadeDurationSeconds = 0.16f;
        private const float TypewriterCharactersPerSecond = 54f;

        private CanvasGroup canvasGroup;
        private RectTransform bandRect;
        private Text speakerText;
        private Text bodyText;
        private Image divider;
        private Image background;
        private bool visible;
        private float autoCloseRemainingSeconds = -1f;
        private readonly BedroomStorySubtitleTypewriter typewriter = new BedroomStorySubtitleTypewriter();

        public bool IsVisible => visible;

        private void Awake()
        {
            EnsureUi();
            HideImmediate();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Tick(float deltaTime)
        {
            if (canvasGroup == null)
            {
                return;
            }

            var targetAlpha = visible ? 1f : 0f;
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, deltaTime / FadeDurationSeconds);

            if (visible && autoCloseRemainingSeconds >= 0f)
            {
                autoCloseRemainingSeconds -= deltaTime;
                if (autoCloseRemainingSeconds <= 0f)
                {
                    Clear();
                    return;
                }
            }

            if (!visible || bodyText == null || typewriter.IsComplete)
            {
                return;
            }

            typewriter.Advance(deltaTime, TypewriterCharactersPerSecond);
            bodyText.text = typewriter.VisibleText;
        }

        public void Present(StoryDialogueRequest request)
        {
            EnsureUi();
            speakerText.text = string.IsNullOrWhiteSpace(request?.SpeakerDisplayName) ? request?.SpeakerId ?? string.Empty : request.SpeakerDisplayName;
            typewriter.Begin(request?.Body ?? string.Empty);
            bodyText.text = typewriter.VisibleText;
            visible = !string.IsNullOrWhiteSpace(typewriter.FullText);
            bandRect.gameObject.SetActive(visible);
            autoCloseRemainingSeconds = visible && request != null && request.AutoAdvance
                ? Mathf.Max(0f, request.AutoAdvanceDelaySeconds)
                : -1f;
        }

        public void Clear()
        {
            visible = false;
            autoCloseRemainingSeconds = -1f;
            typewriter.Clear();
            if (bodyText != null)
            {
                bodyText.text = string.Empty;
            }
        }

        public void HideImmediate()
        {
            EnsureUi();
            visible = false;
            autoCloseRemainingSeconds = -1f;
            canvasGroup.alpha = 0f;
            bandRect.gameObject.SetActive(false);
            typewriter.Clear();
            speakerText.text = string.Empty;
            bodyText.text = string.Empty;
        }

        private void EnsureUi()
        {
            if (canvasGroup != null)
            {
                return;
            }

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600;

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

            bandRect = BedroomStoryUiFactory.CreateUiObject("SubtitleBand", transform).GetComponent<RectTransform>();
            bandRect.anchorMin = new Vector2(0.5f, 0f);
            bandRect.anchorMax = new Vector2(0.5f, 0f);
            bandRect.pivot = new Vector2(0.5f, 0f);
            bandRect.sizeDelta = new Vector2(980f, 156f);
            bandRect.anchoredPosition = new Vector2(0f, 86f);

            background = bandRect.gameObject.AddComponent<Image>();
            background.color = new Color(0.01f, 0.02f, 0.03f, 0.34f);
            background.raycastTarget = false;

            canvasGroup = bandRect.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            speakerText = BedroomStoryUiFactory.CreateText("Speaker", bandRect, 33, TextAnchor.MiddleCenter);
            speakerText.fontStyle = FontStyle.Bold;
            speakerText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            speakerText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            speakerText.rectTransform.pivot = new Vector2(0.5f, 1f);
            speakerText.rectTransform.sizeDelta = new Vector2(840f, 38f);
            speakerText.rectTransform.anchoredPosition = new Vector2(0f, -12f);

            divider = BedroomStoryUiFactory.CreateImage("Divider", bandRect, new Color(1f, 1f, 1f, 0.92f));
            divider.sprite = BedroomStoryUiFactory.HorizontalDividerSprite;
            divider.type = Image.Type.Sliced;
            divider.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            divider.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            divider.rectTransform.pivot = new Vector2(0.5f, 1f);
            divider.rectTransform.sizeDelta = new Vector2(860f, 3f);
            divider.rectTransform.anchoredPosition = new Vector2(0f, -50f);

            bodyText = BedroomStoryUiFactory.CreateText("Body", bandRect, 30, TextAnchor.UpperCenter);
            bodyText.font = BedroomStoryUiFactory.DefaultSerifFont;
            bodyText.fontStyle = FontStyle.Italic;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            bodyText.lineSpacing = 1.25f;
            bodyText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            bodyText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            bodyText.rectTransform.pivot = new Vector2(0.5f, 1f);
            bodyText.rectTransform.sizeDelta = new Vector2(860f, 88f);
            bodyText.rectTransform.anchoredPosition = new Vector2(0f, -68f);
        }
    }
}
