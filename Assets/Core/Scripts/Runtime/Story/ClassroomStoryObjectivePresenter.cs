using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public sealed class ClassroomStoryObjectivePresenter : MonoBehaviour
    {
        private const float FadeDurationSeconds = 0.22f;

        private CanvasGroup canvasGroup;
        private RectTransform panelRect;
        private Text titleText;
        private Text bodyText;
        private bool visible;

        private void Awake()
        {
            EnsureUi();
            HideImmediate();
        }

        private void Update()
        {
            if (canvasGroup == null)
            {
                return;
            }

            var targetAlpha = visible ? 1f : 0f;
            var delta = Mathf.Max(0.01f, Time.unscaledDeltaTime / FadeDurationSeconds);
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, delta);
        }

        public void SetObjective(string title, params string[] lines)
        {
            EnsureUi();

            var safeTitle = string.IsNullOrWhiteSpace(title) ? "Objective" : title.Trim();
            titleText.text = safeTitle;

            var builder = new StringBuilder(220);
            if (lines != null)
            {
                for (var index = 0; index < lines.Length; index++)
                {
                    var line = lines[index];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (builder.Length > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append("- ").Append(line.Trim());
                }
            }

            bodyText.text = builder.ToString();
            panelRect.gameObject.SetActive(true);
            visible = true;
        }

        public void Clear()
        {
            visible = false;
            if (titleText != null)
            {
                titleText.text = string.Empty;
            }

            if (bodyText != null)
            {
                bodyText.text = string.Empty;
            }
        }

        public void HideImmediate()
        {
            EnsureUi();
            visible = false;
            titleText.text = string.Empty;
            bodyText.text = string.Empty;
            canvasGroup.alpha = 0f;
            panelRect.gameObject.SetActive(false);
        }

        private void EnsureUi()
        {
            if (panelRect != null)
            {
                return;
            }

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 640;

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

            panelRect = BedroomStoryUiFactory.CreateUiObject("ObjectivePanel", transform).GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(44f, -40f);
            panelRect.sizeDelta = new Vector2(520f, 170f);

            var background = panelRect.gameObject.AddComponent<Image>();
            background.color = new Color(0.03f, 0.05f, 0.08f, 0.62f);
            background.raycastTarget = false;

            canvasGroup = panelRect.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            titleText = BedroomStoryUiFactory.CreateText("ObjectiveTitle", panelRect, 23, TextAnchor.UpperLeft);
            titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.pivot = new Vector2(0f, 1f);
            titleText.rectTransform.offsetMin = new Vector2(16f, -36f);
            titleText.rectTransform.offsetMax = new Vector2(-14f, -4f);
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(0.92f, 0.97f, 1f, 0.96f);
            titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleText.verticalOverflow = VerticalWrapMode.Truncate;

            bodyText = BedroomStoryUiFactory.CreateText("ObjectiveBody", panelRect, 20, TextAnchor.UpperLeft);
            bodyText.rectTransform.anchorMin = new Vector2(0f, 0f);
            bodyText.rectTransform.anchorMax = new Vector2(1f, 1f);
            bodyText.rectTransform.offsetMin = new Vector2(16f, 12f);
            bodyText.rectTransform.offsetMax = new Vector2(-14f, -42f);
            bodyText.color = new Color(0.95f, 0.97f, 1f, 0.95f);
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Truncate;
            bodyText.lineSpacing = 1.15f;
        }
    }
}
