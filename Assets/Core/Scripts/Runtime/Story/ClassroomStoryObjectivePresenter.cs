using System;
using System.Collections.Generic;
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
        private const int MaxChecklistRows = 4;

        private sealed class ChecklistRowView
        {
            public RectTransform Root;
            public Image Badge;
            public Text BadgeGlyph;
            public Text Label;
        }

        private sealed class ChecklistItem
        {
            public string Text;
            public bool Completed;
        }

        private CanvasGroup canvasGroup;
        private RectTransform panelRect;
        private Text badgeText;
        private Text contextText;
        private Text titleText;
        private readonly List<ChecklistRowView> checklistRows = new List<ChecklistRowView>(MaxChecklistRows);
        private readonly List<ChecklistItem> pendingItems = new List<ChecklistItem>(MaxChecklistRows);
        private string pendingTitle = string.Empty;
        private bool visible;
        private bool suppressed;

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
            pendingTitle = string.IsNullOrWhiteSpace(title) ? "Next Objective" : title.Trim();
            BuildPendingChecklist(lines);
            if (suppressed)
            {
                return;
            }

            ApplyPendingObjective();
        }

        public void SetSuppressed(bool value)
        {
            if (suppressed == value)
            {
                return;
            }

            suppressed = value;
            if (suppressed)
            {
                visible = false;
                if (panelRect != null)
                {
                    panelRect.gameObject.SetActive(false);
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(pendingTitle) || pendingItems.Count > 0)
            {
                ApplyPendingObjective();
            }
        }

        public void Clear()
        {
            pendingTitle = string.Empty;
            pendingItems.Clear();
            visible = false;
            if (panelRect != null)
            {
                panelRect.gameObject.SetActive(false);
            }
        }

        public void HideImmediate()
        {
            EnsureUi();
            visible = false;
            if (titleText != null)
            {
                titleText.text = string.Empty;
            }

            for (var index = 0; index < checklistRows.Count; index++)
            {
                if (checklistRows[index]?.Root != null)
                {
                    checklistRows[index].Root.gameObject.SetActive(false);
                }
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

        private void ApplyPendingObjective()
        {
            EnsureUi();
            if (panelRect == null)
            {
                return;
            }

            badgeText.text = "MISSION";
            contextText.text = "CHECKLIST";
            titleText.text = string.IsNullOrWhiteSpace(pendingTitle) ? "Next Objective" : pendingTitle;

            var count = Mathf.Min(MaxChecklistRows, pendingItems.Count);
            if (count <= 0)
            {
                pendingItems.Add(new ChecklistItem
                {
                    Text = "Continue the classroom briefing.",
                    Completed = false
                });
                count = 1;
            }

            for (var index = 0; index < checklistRows.Count; index++)
            {
                var row = checklistRows[index];
                if (row == null || row.Root == null)
                {
                    continue;
                }

                if (index >= count)
                {
                    row.Root.gameObject.SetActive(false);
                    continue;
                }

                var item = pendingItems[index];
                row.Root.gameObject.SetActive(true);
                row.Label.text = item.Text;
                row.BadgeGlyph.text = item.Completed ? "✓" : string.Empty;
                row.Badge.color = item.Completed
                    ? new Color(0.2f, 0.66f, 0.29f, 1f)
                    : new Color(1f, 0.99f, 0.92f, 1f);
                row.BadgeGlyph.color = item.Completed
                    ? new Color(1f, 1f, 1f, 1f)
                    : new Color(0.04f, 0.06f, 0.08f, 0f);
            }

            panelRect.gameObject.SetActive(true);
            visible = true;
        }

        private void BuildPendingChecklist(string[] lines)
        {
            pendingItems.Clear();
            if (lines == null)
            {
                return;
            }

            for (var index = 0; index < lines.Length && pendingItems.Count < MaxChecklistRows; index++)
            {
                if (!TryParseChecklistLine(lines[index], out var text, out var completed))
                {
                    continue;
                }

                pendingItems.Add(new ChecklistItem
                {
                    Text = text,
                    Completed = completed
                });
            }
        }

        private static bool TryParseChecklistLine(string raw, out string text, out bool completed)
        {
            text = string.Empty;
            completed = false;

            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var working = raw.Trim();
            if (working.StartsWith("- ", StringComparison.Ordinal))
            {
                working = working.Substring(2).TrimStart();
            }

            if (working.StartsWith("[x]", StringComparison.OrdinalIgnoreCase))
            {
                completed = true;
                working = working.Substring(3).TrimStart();
            }
            else if (working.StartsWith("✓", StringComparison.Ordinal))
            {
                completed = true;
                working = working.Substring(1).TrimStart();
            }
            else if (working.StartsWith("done:", StringComparison.OrdinalIgnoreCase))
            {
                completed = true;
                working = working.Substring(5).TrimStart();
            }

            text = working.Trim();
            return !string.IsNullOrWhiteSpace(text);
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

            var shadowRect = BedroomStoryUiFactory.CreateUiObject("ObjectivePanelShadow", transform).GetComponent<RectTransform>();
            shadowRect.anchorMin = new Vector2(0f, 1f);
            shadowRect.anchorMax = new Vector2(0f, 1f);
            shadowRect.pivot = new Vector2(0f, 1f);
            shadowRect.anchoredPosition = new Vector2(38f, -28f);
            shadowRect.sizeDelta = new Vector2(430f, 232f);
            var shadowImage = shadowRect.gameObject.AddComponent<Image>();
            shadowImage.color = new Color(0.03f, 0.05f, 0.08f, 0.42f);
            shadowImage.raycastTarget = false;

            panelRect = BedroomStoryUiFactory.CreateUiObject("ObjectivePanel", transform).GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(28f, -18f);
            panelRect.sizeDelta = new Vector2(430f, 232f);

            var background = panelRect.gameObject.AddComponent<Image>();
            background.color = new Color(0.98f, 0.94f, 0.58f, 0.98f);
            background.raycastTarget = false;

            var border = panelRect.gameObject.AddComponent<Outline>();
            border.effectColor = new Color(0.04f, 0.06f, 0.08f, 1f);
            border.effectDistance = new Vector2(3f, -3f);
            border.useGraphicAlpha = false;

            canvasGroup = panelRect.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            var badgeRect = BedroomStoryUiFactory.CreateUiObject("ObjectiveBadge", panelRect).GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.anchoredPosition = new Vector2(14f, -12f);
            badgeRect.sizeDelta = new Vector2(126f, 38f);
            var badgeImage = badgeRect.gameObject.AddComponent<Image>();
            badgeImage.color = new Color(0.04f, 0.05f, 0.08f, 1f);
            badgeImage.raycastTarget = false;

            badgeText = BedroomStoryUiFactory.CreateText("ObjectiveBadgeText", badgeRect, 16, TextAnchor.MiddleCenter);
            badgeText.rectTransform.anchorMin = Vector2.zero;
            badgeText.rectTransform.anchorMax = Vector2.one;
            badgeText.rectTransform.offsetMin = Vector2.zero;
            badgeText.rectTransform.offsetMax = Vector2.zero;
            badgeText.fontStyle = FontStyle.Bold;
            badgeText.color = new Color(1f, 0.96f, 0.74f, 1f);
            badgeText.text = "MISSION";

            var contextRect = BedroomStoryUiFactory.CreateUiObject("ObjectiveContextBadge", panelRect).GetComponent<RectTransform>();
            contextRect.anchorMin = new Vector2(1f, 1f);
            contextRect.anchorMax = new Vector2(1f, 1f);
            contextRect.pivot = new Vector2(1f, 1f);
            contextRect.anchoredPosition = new Vector2(-14f, -12f);
            contextRect.sizeDelta = new Vector2(118f, 38f);
            var contextImage = contextRect.gameObject.AddComponent<Image>();
            contextImage.color = new Color(0.99f, 0.99f, 0.99f, 0.98f);
            contextImage.raycastTarget = false;

            contextText = BedroomStoryUiFactory.CreateText("ObjectiveContextText", contextRect, 13, TextAnchor.MiddleCenter);
            contextText.rectTransform.anchorMin = Vector2.zero;
            contextText.rectTransform.anchorMax = Vector2.one;
            contextText.rectTransform.offsetMin = Vector2.zero;
            contextText.rectTransform.offsetMax = Vector2.zero;
            contextText.fontStyle = FontStyle.Bold;
            contextText.color = new Color(0.05f, 0.08f, 0.11f, 1f);
            contextText.text = "CHECKLIST";

            titleText = BedroomStoryUiFactory.CreateText("ObjectiveTitle", panelRect, 34, TextAnchor.UpperLeft);
            titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.pivot = new Vector2(0f, 1f);
            titleText.rectTransform.offsetMin = new Vector2(16f, -82f);
            titleText.rectTransform.offsetMax = new Vector2(-16f, -40f);
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(0.05f, 0.08f, 0.11f, 1f);
            titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleText.verticalOverflow = VerticalWrapMode.Truncate;

            var checklistRoot = BedroomStoryUiFactory.CreateUiObject("ChecklistRoot", panelRect).GetComponent<RectTransform>();
            checklistRoot.anchorMin = new Vector2(0f, 0f);
            checklistRoot.anchorMax = new Vector2(1f, 1f);
            checklistRoot.offsetMin = new Vector2(16f, 14f);
            checklistRoot.offsetMax = new Vector2(-16f, -88f);

            var checklistLayout = checklistRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            checklistLayout.childControlWidth = true;
            checklistLayout.childControlHeight = false;
            checklistLayout.childForceExpandWidth = true;
            checklistLayout.childForceExpandHeight = false;
            checklistLayout.spacing = 8f;
            checklistLayout.padding = new RectOffset(0, 0, 0, 0);

            for (var index = 0; index < MaxChecklistRows; index++)
            {
                checklistRows.Add(CreateChecklistRow(checklistRoot, index));
            }
        }

        private static ChecklistRowView CreateChecklistRow(Transform parent, int index)
        {
            var rowRect = BedroomStoryUiFactory.CreateUiObject($"ChecklistRow_{index}", parent).GetComponent<RectTransform>();
            var rowImage = rowRect.gameObject.AddComponent<Image>();
            rowImage.color = new Color(1f, 0.98f, 0.85f, 1f);
            rowImage.raycastTarget = false;

            var rowBorder = rowRect.gameObject.AddComponent<Outline>();
            rowBorder.effectColor = new Color(0.07f, 0.09f, 0.12f, 1f);
            rowBorder.effectDistance = new Vector2(2f, -2f);
            rowBorder.useGraphicAlpha = false;

            var rowLayout = rowRect.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = 10f;
            rowLayout.padding = new RectOffset(10, 10, 7, 7);

            var rowElement = rowRect.gameObject.AddComponent<LayoutElement>();
            rowElement.minHeight = 38f;
            rowElement.preferredHeight = 38f;
            rowElement.flexibleWidth = 1f;

            var badgeRect = BedroomStoryUiFactory.CreateUiObject("ChecklistBadge", rowRect).GetComponent<RectTransform>();
            badgeRect.sizeDelta = new Vector2(22f, 22f);
            var badgeImage = badgeRect.gameObject.AddComponent<Image>();
            badgeImage.color = new Color(1f, 0.99f, 0.92f, 1f);
            badgeImage.raycastTarget = false;
            var badgeBorder = badgeRect.gameObject.AddComponent<Outline>();
            badgeBorder.effectColor = new Color(0.07f, 0.09f, 0.12f, 1f);
            badgeBorder.effectDistance = new Vector2(2f, -2f);
            badgeBorder.useGraphicAlpha = false;

            var badgeGlyph = BedroomStoryUiFactory.CreateText("ChecklistGlyph", badgeRect, 16, TextAnchor.MiddleCenter);
            badgeGlyph.rectTransform.anchorMin = Vector2.zero;
            badgeGlyph.rectTransform.anchorMax = Vector2.one;
            badgeGlyph.rectTransform.offsetMin = Vector2.zero;
            badgeGlyph.rectTransform.offsetMax = Vector2.zero;
            badgeGlyph.fontStyle = FontStyle.Bold;
            badgeGlyph.color = new Color(0.04f, 0.06f, 0.08f, 0f);
            badgeGlyph.text = string.Empty;

            var label = BedroomStoryUiFactory.CreateText("ChecklistLabel", rowRect, 16, TextAnchor.MiddleLeft);
            label.color = new Color(0.09f, 0.12f, 0.15f, 1f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            var labelElement = label.gameObject.AddComponent<LayoutElement>();
            labelElement.flexibleWidth = 1f;

            return new ChecklistRowView
            {
                Root = rowRect,
                Badge = badgeImage,
                BadgeGlyph = badgeGlyph,
                Label = label
            };
        }
    }
}
