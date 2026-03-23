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
        private const float MainFadeDuration = 0.22f;
        private const float SlideDuration = 0.4f;
        private const float RowStaggerDelay = 0.15f;
        private const float RowFadeDuration = 0.3f;
        private const float CheckBounceDuration = 0.4f;
        private const float StrikeDuration = 0.35f;
        private const int MaxChecklistRows = 4;
        private const float PanelVisibleX = 28f;
        private const float PanelHiddenX = -500f;
        private const float ShadowVisibleX = 38f;
        private const float ShadowHiddenX = -490f;
        private const float BasePanelWidth = 438f;
        private const float BasePanelHeight = 252f;
        private const float MaxPanelHeight = 404f;
        private const float ChecklistTopInset = 126f;
        private const float ChecklistBottomInset = 14f;
        private const float ChecklistRowSpacing = 7f;
        private const float MinRowHeight = 56f;
        private const float MaxRowHeight = 98f;
        private const float ApproxRowCharsPerLine = 43f;
        private const float ApproxRowLineHeight = 17f;
        private const float RowVerticalPadding = 20f;

        private sealed class ChecklistRowView
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Background;
            public Image Badge;
            public RectTransform BadgeRect;
            public Text BadgeGlyph;
            public Text Label;
            public RectTransform StrikethroughRect;
            public Image StrikethroughImage;
            public LayoutElement Layout;
        }

        private sealed class RowAnimState
        {
            public bool IsActive;
            public bool WasCompleted;
            public float DelayTimer;
            public float FadeAlpha;
            public float BounceTime;
            public float StrikeFill;
            public float BgDim;

            public void Reset(bool startCompleted, float delay)
            {
                IsActive = true;
                DelayTimer = delay;
                FadeAlpha = 0f;
                WasCompleted = startCompleted;
                BounceTime = startCompleted ? CheckBounceDuration : 0f;
                StrikeFill = startCompleted ? 1f : 0f;
                BgDim = startCompleted ? 1f : 0f;
            }
        }

        private sealed class ChecklistItem
        {
            public string Text;
            public bool Completed;
        }

        private CanvasGroup canvasGroup;
        private RectTransform panelRect;
        private RectTransform shadowRect;
        private RectTransform checklistRootRect;
        private Text badgeText;
        private Text contextText;
        private Text titleText;

        private readonly List<ChecklistRowView> checklistRows = new List<ChecklistRowView>(MaxChecklistRows);
        private readonly List<RowAnimState> rowStates = new List<RowAnimState>(MaxChecklistRows);
        private readonly List<ChecklistItem> pendingItems = new List<ChecklistItem>(MaxChecklistRows);
        
        private string pendingTitle = string.Empty;
        private bool visible;
        private bool suppressed;
        
        private float panelSlideTime;
        private float panelAlpha;

        private void Awake()
        {
            EnsureUi();
            for (int i = 0; i < MaxChecklistRows; i++)
            {
                rowStates.Add(new RowAnimState());
            }
            HideImmediate();
        }

        private void Update()
        {
            if (canvasGroup == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;

            // Animate Panel Alpha
            float targetAlpha = visible ? 1f : 0f;
            panelAlpha = Mathf.MoveTowards(panelAlpha, targetAlpha, dt / MainFadeDuration);
            canvasGroup.alpha = panelAlpha;

            // Animate Panel Slide (with cubic ease-out)
            float targetSlide = visible ? 1f : 0f;
            panelSlideTime = Mathf.MoveTowards(panelSlideTime, targetSlide, dt / SlideDuration);
            float slideEased = 1f - Mathf.Pow(1f - panelSlideTime, 3f);
            
            if (panelRect != null && shadowRect != null)
            {
                var pPos = panelRect.anchoredPosition;
                pPos.x = Mathf.Lerp(PanelHiddenX, PanelVisibleX, slideEased);
                panelRect.anchoredPosition = pPos;

                var sPos = shadowRect.anchoredPosition;
                sPos.x = Mathf.Lerp(ShadowHiddenX, ShadowVisibleX, slideEased);
                shadowRect.anchoredPosition = sPos;
            }

            // Animate Checklist Rows
            if (visible)
            {
                for (int i = 0; i < checklistRows.Count; i++)
                {
                    var state = rowStates[i];
                    if (!state.IsActive) continue;

                    if (state.DelayTimer > 0f)
                    {
                        state.DelayTimer -= dt;
                        continue;
                    }

                    // Perform Row Fade-In
                    if (state.FadeAlpha < 1f)
                    {
                        state.FadeAlpha = Mathf.MoveTowards(state.FadeAlpha, 1f, dt / RowFadeDuration);
                    }

                    // Progress Completed Trigger States
                    if (state.WasCompleted)
                    {
                        state.BounceTime += dt;
                        state.StrikeFill = Mathf.MoveTowards(state.StrikeFill, 1f, dt / StrikeDuration);
                        state.BgDim = Mathf.MoveTowards(state.BgDim, 1f, dt / StrikeDuration);
                    }

                    ApplyRowAnimState(checklistRows[i], state);
                }
            }
        }

        private void ApplyRowAnimState(ChecklistRowView row, RowAnimState state)
        {
            if (row.Group != null)
            {
                row.Group.alpha = state.FadeAlpha;
            }

            // Strikethrough Scale Animation X
            if (row.StrikethroughRect != null)
            {
                var scl = row.StrikethroughRect.localScale;
                scl.x = state.StrikeFill;
                row.StrikethroughRect.localScale = scl;
            }

            // Checkmark Bounce Animation applied to Scale
            if (state.BounceTime > 0f && state.BounceTime <= CheckBounceDuration)
            {
                float t = state.BounceTime / CheckBounceDuration;
                float scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.4f; // pop out to 1.4 then 1
                row.BadgeRect.localScale = new Vector3(scale, scale, 1f);
            }
            else if (row.BadgeRect != null)
            {
                row.BadgeRect.localScale = Vector3.one;
            }

            // Background subtle fade on complete
            if (row.Background != null)
            {
                var targetColor = Color.Lerp(
                    new Color(1f, 0.98f, 0.85f, 1f),
                    new Color(0.92f, 0.90f, 0.82f, 1f),
                    state.BgDim
                );
                row.Background.color = targetColor;
            }
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
        }

        public void HideImmediate()
        {
            EnsureUi();
            visible = false;
            panelAlpha = 0f;
            panelSlideTime = 0f;

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
                rowStates[index].IsActive = false;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            if (panelRect != null)
            {
                var pPos = panelRect.anchoredPosition;
                pPos.x = -500f;
                panelRect.anchoredPosition = pPos;
                panelRect.gameObject.SetActive(false);
            }

            if (shadowRect != null)
            {
                var sPos = shadowRect.anchoredPosition;
                sPos.x = -490f;
                shadowRect.anchoredPosition = sPos;
                shadowRect.gameObject.SetActive(false);
            }
        }

        private void ApplyPendingObjective()
        {
            EnsureUi();
            if (panelRect == null || shadowRect == null)
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

            bool wasAlreadyVisible = visible;

            for (var index = 0; index < checklistRows.Count; index++)
            {
                var row = checklistRows[index];
                var state = rowStates[index];

                if (row == null || row.Root == null)
                {
                    continue;
                }

                if (index >= count)
                {
                    row.Root.gameObject.SetActive(false);
                    state.IsActive = false;
                    continue;
                }

                var item = pendingItems[index];
                bool justCompleted = !state.WasCompleted && item.Completed;

                // Configure staggering resets or update existing completion flow
                if (!wasAlreadyVisible || !row.Root.gameObject.activeSelf)
                {
                    state.Reset(item.Completed, index * RowStaggerDelay);
                }
                else
                {
                    state.IsActive = true;
                    if (justCompleted)
                    {
                        state.WasCompleted = true;
                        state.BounceTime = 0f; // Initiate strike/bounce timeline
                    }
                }

                row.Root.gameObject.SetActive(true);
                row.Label.text = item.Text;
                var rowHeight = EstimateRowHeight(item.Text);
                if (row.Layout != null)
                {
                    row.Layout.minHeight = rowHeight;
                    row.Layout.preferredHeight = rowHeight;
                }
                
                if (item.Completed)
                {
                    row.BadgeGlyph.text = "✓";
                    row.Badge.color = new Color(0.2f, 0.66f, 0.29f, 1f);
                    row.BadgeGlyph.color = new Color(1f, 1f, 1f, 1f);
                }
                else
                {
                    row.BadgeGlyph.text = string.Empty;
                    row.Badge.color = new Color(1f, 0.99f, 0.92f, 1f);
                    row.BadgeGlyph.color = new Color(0.04f, 0.06f, 0.08f, 0f);
                }

                ApplyRowAnimState(row, state);
            }

            ApplyPanelSizingForRows(count);
            panelRect.gameObject.SetActive(true);
            shadowRect.gameObject.SetActive(true);
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

            shadowRect = BedroomStoryUiFactory.CreateUiObject("ObjectivePanelShadow", transform).GetComponent<RectTransform>();
            shadowRect.anchorMin = new Vector2(0f, 1f);
            shadowRect.anchorMax = new Vector2(0f, 1f);
            shadowRect.pivot = new Vector2(0f, 1f);
            shadowRect.anchoredPosition = new Vector2(ShadowHiddenX, -28f);
            shadowRect.sizeDelta = new Vector2(BasePanelWidth, BasePanelHeight);
            var shadowImage = shadowRect.gameObject.AddComponent<Image>();
            shadowImage.color = new Color(0.03f, 0.05f, 0.08f, 0.42f);
            shadowImage.raycastTarget = false;

            panelRect = BedroomStoryUiFactory.CreateUiObject("ObjectivePanel", transform).GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(PanelHiddenX, -18f);
            panelRect.sizeDelta = new Vector2(BasePanelWidth, BasePanelHeight);

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

            titleText = BedroomStoryUiFactory.CreateText("ObjectiveTitle", panelRect, 24, TextAnchor.UpperLeft);
            titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.pivot = new Vector2(0f, 1f);
            titleText.rectTransform.offsetMin = new Vector2(18f, -112f);
            titleText.rectTransform.offsetMax = new Vector2(-18f, -72f);
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(0.05f, 0.08f, 0.11f, 1f);
            titleText.resizeTextForBestFit = true;
            titleText.resizeTextMinSize = 16;
            titleText.resizeTextMaxSize = 22;
            titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleText.verticalOverflow = VerticalWrapMode.Truncate;

            checklistRootRect = BedroomStoryUiFactory.CreateUiObject("ChecklistRoot", panelRect).GetComponent<RectTransform>();
            checklistRootRect.anchorMin = new Vector2(0f, 0f);
            checklistRootRect.anchorMax = new Vector2(1f, 1f);
            checklistRootRect.offsetMin = new Vector2(16f, ChecklistBottomInset);
            checklistRootRect.offsetMax = new Vector2(-16f, -ChecklistTopInset);

            var checklistLayout = checklistRootRect.gameObject.AddComponent<VerticalLayoutGroup>();
            checklistLayout.childControlWidth = true;
            checklistLayout.childControlHeight = true;
            checklistLayout.childForceExpandWidth = true;
            checklistLayout.childForceExpandHeight = false;
            checklistLayout.spacing = ChecklistRowSpacing;
            checklistLayout.padding = new RectOffset(0, 0, 0, 0);

            for (var index = 0; index < MaxChecklistRows; index++)
            {
                checklistRows.Add(CreateChecklistRow(checklistRootRect, index));
            }
        }

        private static ChecklistRowView CreateChecklistRow(Transform parent, int index)
        {
            var rowRect = BedroomStoryUiFactory.CreateUiObject($"ChecklistRow_{index}", parent).GetComponent<RectTransform>();
            var group = rowRect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            var rowImage = rowRect.gameObject.AddComponent<Image>();
            rowImage.color = new Color(1f, 0.98f, 0.85f, 1f);
            rowImage.raycastTarget = false;

            var rowBorder = rowRect.gameObject.AddComponent<Outline>();
            rowBorder.effectColor = new Color(0.07f, 0.09f, 0.12f, 1f);
            rowBorder.effectDistance = new Vector2(2f, -2f);
            rowBorder.useGraphicAlpha = false;

            var rowElement = rowRect.gameObject.AddComponent<LayoutElement>();
            rowElement.minHeight = MinRowHeight;
            rowElement.preferredHeight = MinRowHeight;
            rowElement.flexibleWidth = 1f;

            var badgeRect = BedroomStoryUiFactory.CreateUiObject("ChecklistBadge", rowRect).GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0f, 0.5f);
            badgeRect.anchorMax = new Vector2(0f, 0.5f);
            badgeRect.pivot = new Vector2(0f, 0.5f);
            badgeRect.anchoredPosition = new Vector2(12f, 0f);
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

            var label = BedroomStoryUiFactory.CreateText("ChecklistLabel", rowRect, 14, TextAnchor.UpperLeft);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(42f, 7f);
            label.rectTransform.offsetMax = new Vector2(-12f, -7f);
            label.color = new Color(0.09f, 0.12f, 0.15f, 1f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = 14;

            var stRect = BedroomStoryUiFactory.CreateUiObject("Strikethrough", rowRect).GetComponent<RectTransform>();
            stRect.anchorMin = new Vector2(0f, 0.5f);
            stRect.anchorMax = new Vector2(1f, 0.5f); 
            stRect.pivot = new Vector2(0f, 0.5f);
            stRect.anchoredPosition = new Vector2(42f, 0f); 
            stRect.offsetMin = new Vector2(42f, -1f);
            stRect.offsetMax = new Vector2(-12f, 1f); 
            stRect.localScale = new Vector3(0f, 1f, 1f); 
            
            var stImage = stRect.gameObject.AddComponent<Image>();
            stImage.color = new Color(0.09f, 0.12f, 0.15f, 1f);
            stImage.raycastTarget = false;

            return new ChecklistRowView
            {
                Root = rowRect,
                Group = group,
                Background = rowImage,
                Badge = badgeImage,
                BadgeRect = badgeRect,
                BadgeGlyph = badgeGlyph,
                Label = label,
                StrikethroughRect = stRect,
                StrikethroughImage = stImage,
                Layout = rowElement
            };
        }

        private void ApplyPanelSizingForRows(int activeRowCount)
        {
            if (panelRect == null || shadowRect == null || checklistRootRect == null)
            {
                return;
            }

            activeRowCount = Mathf.Clamp(activeRowCount, 0, MaxChecklistRows);
            float requiredRowsHeight = 0f;
            for (var index = 0; index < activeRowCount; index++)
            {
                var row = checklistRows[index];
                if (row?.Layout == null)
                {
                    requiredRowsHeight += MinRowHeight;
                }
                else
                {
                    requiredRowsHeight += Mathf.Max(MinRowHeight, row.Layout.preferredHeight);
                }
            }

            if (activeRowCount > 1)
            {
                requiredRowsHeight += (activeRowCount - 1) * ChecklistRowSpacing;
            }

            float requiredPanelHeight = ChecklistTopInset + ChecklistBottomInset + requiredRowsHeight + 8f;
            var clampedHeight = Mathf.Clamp(requiredPanelHeight, BasePanelHeight, MaxPanelHeight);

            var panelSize = panelRect.sizeDelta;
            panelSize.x = BasePanelWidth;
            panelSize.y = clampedHeight;
            panelRect.sizeDelta = panelSize;

            var shadowSize = shadowRect.sizeDelta;
            shadowSize.x = BasePanelWidth;
            shadowSize.y = clampedHeight;
            shadowRect.sizeDelta = shadowSize;

            checklistRootRect.offsetMin = new Vector2(16f, ChecklistBottomInset);
            checklistRootRect.offsetMax = new Vector2(-16f, -ChecklistTopInset);
        }

        private static float EstimateRowHeight(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return MinRowHeight;
            }

            var trimmed = text.Trim();
            var estimatedLines = Mathf.CeilToInt(trimmed.Length / ApproxRowCharsPerLine);
            estimatedLines = Mathf.Clamp(estimatedLines, 1, 4);
            var estimatedHeight = (estimatedLines * ApproxRowLineHeight) + RowVerticalPadding;
            return Mathf.Clamp(estimatedHeight, MinRowHeight, MaxRowHeight);
        }
    }
}
