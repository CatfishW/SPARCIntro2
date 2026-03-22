using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ItemInteraction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public class InteractionPromptPresenter : MonoBehaviour
    {
        private const int ButtonSlotCount = 4;
        private const float TitleHeight = 24f;
        private const float TitleUnderlineWidth = 144f;
        private const float TitleUnderlineHeight = 4f;
        private const float TitleUnderlineGap = 6f;
        private const float TitleToRowsGap = 12f;
        private const float RowHeight = 28f;
        private const float RowSpacing = 8f;
        private const float CardPadding = 16f;

        private static readonly Color TitleColor = new Color(0.08f, 0.09f, 0.12f, 1f);
        private static readonly Color TitleUnderlineColor = new Color(0.08f, 0.09f, 0.12f, 1f);
        private static readonly Color ConnectorColor = new Color(1f, 1f, 1f, 0.98f);
        private static readonly Color PromptBorderColor = new Color(0.08f, 0.09f, 0.12f, 1f);
        private static readonly Color PromptShadowColor = new Color(0.05f, 0.08f, 0.11f, 0.42f);
        private static readonly Color PromptBackdropColor = new Color(0.97f, 0.94f, 0.89f, 0.65f); // Increased transparency
        private static readonly Color HintPlateColor = new Color(1f, 0.84f, 0.16f, 1f);
        private static readonly Color HintPlateHighlightColor = new Color(1f, 0.9f, 0.35f, 1f);
        private static readonly Color HintTextColor = new Color(0.08f, 0.09f, 0.12f, 1f);
        private static readonly Color HintTextHighlightColor = new Color(0.08f, 0.09f, 0.12f, 1f);
        private static readonly Color ActionFillColor = new Color(0.98f, 0.95f, 0.9f, 0.92f);
        private static readonly Color ActionFillHighlightColor = new Color(0.91f, 0.97f, 1f, 0.98f);
        private static readonly Color ActionFillDisabledColor = new Color(0.87f, 0.86f, 0.84f, 0.75f);
        private static readonly Color ActionAccentColor = new Color(0.08f, 0.09f, 0.12f, 1f);
        private static readonly Color ActionLabelColor = new Color(0.08f, 0.09f, 0.12f, 1f);
        private static readonly Color DisabledLabelColor = new Color(0.32f, 0.32f, 0.34f, 0.55f);

        [Header("Layout")]
        [SerializeField, Min(180f)] private float promptWidth = 244f;
        [SerializeField, Min(48f)] private float connectorHorizontalOffset = 110f;
        [SerializeField] private float promptVerticalOffset = -8f;
        [SerializeField] private float visualAnchorVerticalOffset = -132f;
        [SerializeField] private float capPromptExtraVerticalOffset = -20f;
        [SerializeField, Min(0f)] private float screenMargin = 48f;

        [Header("Animation")]
        [SerializeField, Min(0.01f)] private float panelFadeDuration = 0.18f;
        [SerializeField, Min(0.01f)] private float positionSmoothing = 20f;
        [SerializeField, Range(0f, 0.08f)] private float panelSpawnScaleOffset = 0.028f;

        private Canvas canvas;
        private RectTransform canvasRect;
        private RectTransform connectorRoot;
        private Image connectorDot;
        private Image connectorSegmentA;
        private Image connectorSegmentB;
        private RectTransform promptRoot;
        private CanvasGroup promptGroup;
        private Image promptShadow;
        private Image promptBorder;
        private Image promptBackdrop;
        private Text titleText;
        private Image titleUnderline;
        private PromptRow[] rows;
        private InteractableItem currentItem;
        private bool initialized;
        private bool currentPromptOnRight;
        private float revealTimer;
        private Vector2 smoothedPromptPosition;
        private bool hasSmoothedPromptPosition;

        public void Render(
            InteractableItem item,
            List<InteractionOption> options,
            Camera worldCamera,
            InteractionInputSource inputSource)
        {
            EnsureInitialized();

            if (!HasValidRuntimeState())
            {
                Hide();
                return;
            }

            if (item == null || worldCamera == null || options == null || options.Count == 0)
            {
                Hide();
                return;
            }

            var promptWorldPosition = item.GetPromptWorldPosition();
            var viewportPoint = worldCamera.WorldToViewportPoint(promptWorldPosition);
            if (viewportPoint.z <= 0f)
            {
                Hide();
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    RectTransformUtility.WorldToScreenPoint(worldCamera, promptWorldPosition),
                    null,
                    out var itemCanvasPoint))
            {
                Hide();
                return;
            }

            if (currentItem != item)
            {
                currentItem = item;
                revealTimer = 0f;
                hasSmoothedPromptPosition = false;
            }

            titleText.text = string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName;

            foreach (var row in rows)
            {
                row.SetActive(false);
            }

            var visibleCount = 0;
            for (var index = 0; index < options.Count; index++)
            {
                var option = options[index];
                if (option == null || !option.visible)
                {
                    continue;
                }

                var slotIndex = (int)option.slot;
                if (slotIndex < 0 || slotIndex >= rows.Length)
                {
                    continue;
                }

                var row = rows[slotIndex];
                if (row == null)
                {
                    continue;
                }

                var hint = !string.IsNullOrWhiteSpace(option.hintOverride)
                    ? option.hintOverride
                    : (inputSource != null ? inputSource.GetSlotHint(option.slot) : string.Empty);

                row.SetData(hint, option.label, option.enabled, false);
                row.SetActive(true);
                visibleCount++;
            }

            if (visibleCount == 0)
            {
                Hide();
                return;
            }

            var visualAnchorPoint = itemCanvasPoint + new Vector2(0f, visualAnchorVerticalOffset + ResolvePerItemVerticalOffset(item));
            visualAnchorPoint.y = Mathf.Max(visualAnchorPoint.y, canvasRect.rect.yMin + screenMargin + 24f);

            var promptHeight = LayoutPrompt(visibleCount, visualAnchorPoint);
            var promptPosition = ResolvePromptPosition(visualAnchorPoint, promptHeight);

            if (!hasSmoothedPromptPosition)
            {
                smoothedPromptPosition = promptPosition;
                hasSmoothedPromptPosition = true;
            }
            else
            {
                var smoothing = 1f - Mathf.Exp(-positionSmoothing * Time.deltaTime);
                smoothedPromptPosition = Vector2.Lerp(smoothedPromptPosition, promptPosition, smoothing);
            }

            promptRoot.anchoredPosition = smoothedPromptPosition;

            revealTimer = Mathf.Min(revealTimer + Time.deltaTime, panelFadeDuration);
            var panelT = panelFadeDuration <= Mathf.Epsilon
                ? 1f
                : Mathf.Clamp01(revealTimer / panelFadeDuration);
            var eased = 1f - Mathf.Pow(1f - panelT, 3f);

            var connectorPoint = GetConnectorPoint();
            UpdateConnector(visualAnchorPoint, connectorPoint, eased);

            UpdatePromptVisibility(eased);
        }

        public void Hide()
        {
            if (!initialized)
            {
                return;
            }

            currentItem = null;
            revealTimer = 0f;
            hasSmoothedPromptPosition = false;

            if (promptRoot != null)
            {
                promptRoot.gameObject.SetActive(false);
                promptRoot.localScale = Vector3.one * (1f - panelSpawnScaleOffset);
            }

            if (promptGroup != null)
            {
                promptGroup.alpha = 0f;
            }

            SetConnectorActive(false);
        }

        public void SetHighlightedSlot(InteractionOptionSlot slot)
        {
            if (!initialized || rows == null)
            {
                return;
            }

            for (var index = 0; index < rows.Length; index++)
            {
                rows[index]?.SetHighlighted(index == (int)slot);
            }
        }

        public InteractionOption GetOptionAtScreenPosition(Vector2 screenPos, List<InteractionOption> visibleOptions)
        {
            if (!initialized)
            {
                EnsureInitialized();
            }

            if (canvas == null || visibleOptions == null || rows == null)
            {
                return null;
            }

            foreach (var option in visibleOptions)
            {
                if (option == null)
                {
                    continue;
                }

                var slotIndex = (int)option.slot;
                if (slotIndex < 0 || slotIndex >= rows.Length)
                {
                    continue;
                }

                var row = rows[slotIndex];
                if (row != null && row.IsPointInside(screenPos, canvas))
                {
                    return option;
                }
            }

            return null;
        }

        private void EnsureInitialized()
        {
            if (initialized && HasValidRuntimeState())
            {
                return;
            }

            ResetRuntimeState();

            canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 670;

            if (!TryGetComponent<CanvasScaler>(out var scaler))
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (!GetComponent<GraphicRaycaster>())
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            canvasRect = (RectTransform)transform;
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            var canvasGraphic = canvas.GetComponent<Graphic>();
            if (canvasGraphic != null)
            {
                canvasGraphic.raycastTarget = false;
                canvasGraphic.color = new Color(0f, 0f, 0f, 0f);
            }

            BuildPrompt();

            initialized = true;
            Hide();
        }

        private void BuildPrompt()
        {
            var connectorObject = RuntimeUiFactory.CreateUiObject("PromptConnector", transform);
            connectorRoot = connectorObject.GetComponent<RectTransform>();
            connectorRoot.anchorMin = Vector2.zero;
            connectorRoot.anchorMax = Vector2.one;
            connectorRoot.offsetMin = Vector2.zero;
            connectorRoot.offsetMax = Vector2.zero;

            connectorSegmentA = RuntimeUiFactory.CreateImage("ConnectorSegmentA", connectorRoot, ConnectorColor);
            connectorSegmentA.raycastTarget = false;

            connectorSegmentB = RuntimeUiFactory.CreateImage("ConnectorSegmentB", connectorRoot, ConnectorColor);
            connectorSegmentB.raycastTarget = false;

            connectorDot = RuntimeUiFactory.CreateImage("ConnectorDot", connectorRoot, ConnectorColor);
            connectorDot.raycastTarget = false;
            connectorDot.rectTransform.sizeDelta = Vector2.one * 10f;

            var promptObject = RuntimeUiFactory.CreateUiObject("PromptRoot", transform);
            promptRoot = promptObject.GetComponent<RectTransform>();
            promptRoot.SetAsLastSibling();
            promptRoot.anchorMin = new Vector2(0.5f, 0.5f);
            promptRoot.anchorMax = new Vector2(0.5f, 0.5f);
            promptRoot.pivot = new Vector2(0f, 0.5f);
            promptRoot.sizeDelta = new Vector2(promptWidth, 84f);

            promptGroup = promptObject.AddComponent<CanvasGroup>();
            promptGroup.alpha = 0f;

            promptShadow = RuntimeUiFactory.CreateImage("PromptShadow", promptRoot, PromptShadowColor);
            promptShadow.raycastTarget = false;

            promptBorder = RuntimeUiFactory.CreateImage("PromptBorder", promptRoot, PromptBorderColor);
            promptBorder.raycastTarget = false;

            promptBackdrop = RuntimeUiFactory.CreateImage("PromptBackdrop", promptRoot, PromptBackdropColor);
            promptBackdrop.raycastTarget = false;

            titleText = RuntimeUiFactory.CreateText("Title", promptRoot, 20, TextAnchor.MiddleLeft);
            titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            titleText.rectTransform.sizeDelta = new Vector2(0f, TitleHeight);
            titleText.rectTransform.anchoredPosition = Vector2.zero;
            titleText.color = TitleColor;
            titleText.fontStyle = FontStyle.Bold;
            titleText.raycastTarget = false;

            titleUnderline = RuntimeUiFactory.CreateImage("TitleUnderline", promptRoot, TitleUnderlineColor);
            titleUnderline.raycastTarget = false;

            rows = new PromptRow[ButtonSlotCount];
            for (var index = 0; index < rows.Length; index++)
            {
                rows[index] = new PromptRow(promptRoot);
            }
        }

        private float LayoutPrompt(int visibleCount, Vector2 itemCanvasPoint)
        {
            var topOffset = 6f;
            var promptHeight =
                CardPadding +
                topOffset +
                TitleHeight +
                TitleUnderlineGap +
                TitleUnderlineHeight +
                TitleToRowsGap +
                (visibleCount * RowHeight) +
                (Mathf.Max(visibleCount - 1, 0) * RowSpacing) +
                CardPadding;

            promptRoot.sizeDelta = new Vector2(promptWidth, promptHeight);

            currentPromptOnRight = ResolvePromptSide(itemCanvasPoint);
            promptRoot.pivot = new Vector2(currentPromptOnRight ? 0f : 1f, 0.5f);

            LayoutCard(promptHeight);

            titleText.alignment = currentPromptOnRight ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            titleText.rectTransform.sizeDelta = new Vector2(-CardPadding * 2f, TitleHeight);
            titleText.rectTransform.anchoredPosition = new Vector2(0f, -(CardPadding + 6f));

            titleUnderline.rectTransform.anchorMin = new Vector2(currentPromptOnRight ? 0f : 1f, 1f);
            titleUnderline.rectTransform.anchorMax = titleUnderline.rectTransform.anchorMin;
            titleUnderline.rectTransform.pivot = new Vector2(currentPromptOnRight ? 0f : 1f, 1f);
            titleUnderline.rectTransform.sizeDelta = new Vector2(TitleUnderlineWidth, TitleUnderlineHeight);
            titleUnderline.rectTransform.anchoredPosition = new Vector2(currentPromptOnRight ? CardPadding : -CardPadding, -(CardPadding + TitleHeight + TitleUnderlineGap + 6f));

            var cursor = CardPadding + TitleHeight + TitleUnderlineGap + TitleUnderlineHeight + TitleToRowsGap + 6f;
            foreach (var row in rows)
            {
                if (!row.IsActive)
                {
                    continue;
                }

                row.ConfigureLayout(promptWidth - (CardPadding * 2f), CardPadding, cursor, !currentPromptOnRight);
                cursor += RowHeight + RowSpacing;
            }

            return promptHeight;
        }

        private void LayoutCard(float promptHeight)
        {
            if (promptShadow != null)
            {
                var shadowRect = promptShadow.rectTransform;
                shadowRect.anchorMin = Vector2.zero;
                shadowRect.anchorMax = Vector2.one;
                shadowRect.offsetMin = new Vector2(8f, -8f);
                shadowRect.offsetMax = new Vector2(8f, -8f);
            }

            if (promptBorder != null)
            {
                var borderRect = promptBorder.rectTransform;
                borderRect.anchorMin = Vector2.zero;
                borderRect.anchorMax = Vector2.one;
                borderRect.offsetMin = Vector2.zero;
                borderRect.offsetMax = Vector2.zero;
            }

            if (promptBackdrop != null)
            {
                var backdropRect = promptBackdrop.rectTransform;
                backdropRect.anchorMin = Vector2.zero;
                backdropRect.anchorMax = Vector2.one;
                backdropRect.offsetMin = new Vector2(3f, 3f);
                backdropRect.offsetMax = new Vector2(-3f, -3f);
                promptBackdrop.type = Image.Type.Sliced;
                promptBackdrop.color = PromptBackdropColor;
            }

        }

        private Vector2 ResolvePromptPosition(Vector2 itemCanvasPoint, float promptHeight)
        {
            var rect = canvasRect.rect;
            var preferredX = itemCanvasPoint.x + (currentPromptOnRight ? connectorHorizontalOffset : -connectorHorizontalOffset);
            var preferredY = itemCanvasPoint.y + promptVerticalOffset;

            var minX = rect.xMin + screenMargin + (currentPromptOnRight ? 0f : promptWidth);
            var maxX = rect.xMax - screenMargin - (currentPromptOnRight ? promptWidth : 0f);
            var minY = rect.yMin + screenMargin + (promptHeight * 0.5f);
            var maxY = rect.yMax - screenMargin - (promptHeight * 0.5f);

            return new Vector2(
                Mathf.Clamp(preferredX, minX, maxX),
                Mathf.Clamp(preferredY, minY, maxY));
        }

        private bool ResolvePromptSide(Vector2 itemCanvasPoint)
        {
            var rect = canvasRect.rect;
            var roomOnRight = itemCanvasPoint.x + connectorHorizontalOffset + promptWidth <= rect.xMax - screenMargin;
            var roomOnLeft = itemCanvasPoint.x - connectorHorizontalOffset - promptWidth >= rect.xMin + screenMargin;

            if (roomOnRight && !roomOnLeft)
            {
                return true;
            }

            if (roomOnLeft && !roomOnRight)
            {
                return false;
            }

            return itemCanvasPoint.x <= 0f;
        }

        private float ResolvePerItemVerticalOffset(InteractableItem item)
        {
            if (item == null)
            {
                return 0f;
            }

            var displayName = item.displayName ?? string.Empty;
            var storyId = item.storyId ?? string.Empty;
            if (displayName.IndexOf("CAP", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                storyId.IndexOf("cap", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return capPromptExtraVerticalOffset;
            }

            return 0f;
        }

        private Vector2 GetConnectorPoint()
        {
            PromptRow primaryRow = null;
            for (var index = 0; index < rows.Length; index++)
            {
                if (rows[index] != null && rows[index].IsActive)
                {
                    primaryRow = rows[index];
                    break;
                }
            }

            if (primaryRow == null)
            {
                return promptRoot.anchoredPosition;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    RectTransformUtility.WorldToScreenPoint(null, primaryRow.GetHintAnchorWorldPosition()),
                    null,
                    out var connectorPoint))
            {
                Vector3 localPoint = canvasRect.InverseTransformPoint(primaryRow.GetHintAnchorWorldPosition());
                return new Vector2(localPoint.x, localPoint.y);
            }

            return connectorPoint;
        }

        private void UpdateConnector(Vector2 startPoint, Vector2 endPoint, float alpha)
        {
            if (connectorRoot == null || connectorDot == null || connectorSegmentA == null || connectorSegmentB == null)
            {
                return;
            }

            var delta = endPoint - startPoint;
            if (delta.sqrMagnitude <= 1f)
            {
                SetConnectorActive(false);
                return;
            }

            SetConnectorActive(alpha > 0.001f);

            var direction = currentPromptOnRight ? 1f : -1f;
            var bend = startPoint + new Vector2(direction * Mathf.Max(26f, Mathf.Abs(delta.x) * 0.45f), delta.y * 0.24f);

            var grownBend = Vector2.Lerp(startPoint, bend, alpha);
            var grownEnd = alpha < 0.55f
                ? grownBend
                : Vector2.Lerp(grownBend, endPoint, (alpha - 0.55f) / 0.45f);

            ApplyConnectorSegment(connectorSegmentA.rectTransform, startPoint, grownBend, alpha);
            ApplyConnectorSegment(connectorSegmentB.rectTransform, grownBend, grownEnd, alpha);

            connectorDot.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            connectorDot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            connectorDot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            connectorDot.rectTransform.anchoredPosition = startPoint;
            connectorDot.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(8f, 12f, alpha);

            var tint = ConnectorColor;
            tint.a *= alpha;
            connectorDot.color = tint;
        }

        private static void ApplyConnectorSegment(RectTransform rect, Vector2 from, Vector2 to, float alpha)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length <= 0.01f)
            {
                rect.gameObject.SetActive(false);
                return;
            }

            rect.gameObject.SetActive(alpha > 0.001f);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(length, 3.5f);
            rect.anchoredPosition = from;
            rect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

            var image = rect.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            var tint = ConnectorColor;
            tint.a *= alpha;
            image.color = tint;
        }

        private void SetConnectorActive(bool active)
        {
            if (connectorRoot != null)
            {
                connectorRoot.gameObject.SetActive(active);
            }
        }

        private void UpdatePromptVisibility(float panelT)
        {
            if (promptRoot == null || promptGroup == null)
            {
                return;
            }

            var promptAlpha = Mathf.Clamp01((panelT - 0.58f) / 0.42f);
            var showPrompt = promptAlpha > 0.001f;
            promptRoot.gameObject.SetActive(showPrompt);
            promptGroup.alpha = promptAlpha;
            promptRoot.localScale = Vector3.one * Mathf.Lerp(1f - panelSpawnScaleOffset, 1f, promptAlpha);
        }

        private bool HasValidRuntimeState()
        {
            if (canvas == null || canvasRect == null || connectorRoot == null || connectorDot == null || connectorSegmentA == null || connectorSegmentB == null || promptRoot == null || promptGroup == null || promptShadow == null || promptBackdrop == null || titleText == null || titleUnderline == null)
            {
                return false;
            }

            if (rows == null || rows.Length != ButtonSlotCount)
            {
                return false;
            }

            for (var index = 0; index < rows.Length; index++)
            {
                if (rows[index] == null)
                {
                    return false;
                }
            }

            return true;
        }

        private void ResetRuntimeState()
        {
            initialized = false;
            currentItem = null;
            revealTimer = 0f;
            hasSmoothedPromptPosition = false;

            DestroyChildIfExists("PromptConnector");
            DestroyChildIfExists("PromptRoot");

            canvas = null;
            canvasRect = null;
            connectorRoot = null;
            connectorDot = null;
            connectorSegmentA = null;
            connectorSegmentB = null;
            promptRoot = null;
            promptGroup = null;
            promptShadow = null;
            promptBackdrop = null;
            titleText = null;
            titleUnderline = null;
            rows = null;
        }

        private void DestroyChildIfExists(string childName)
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }

        private sealed class PromptRow
        {
            private readonly RectTransform root;
            private readonly RectTransform hintRoot;
            private readonly Image hintPlate;
            private readonly Text hintText;
            private readonly RectTransform actionRoot;
            private readonly Image actionFill;
            private readonly Image actionAccent;
            private readonly Text labelText;
            private bool isEnabled;

            public bool IsActive => root.gameObject.activeSelf;

            public PromptRow(RectTransform parent)
            {
                root = RuntimeUiFactory.CreateUiObject("PromptRow", parent).GetComponent<RectTransform>();
                root.anchorMin = new Vector2(0f, 1f);
                root.anchorMax = new Vector2(1f, 1f);
                root.pivot = new Vector2(0.5f, 1f);
                root.sizeDelta = new Vector2(0f, RowHeight);

                hintRoot = RuntimeUiFactory.CreateUiObject("HintRoot", root).GetComponent<RectTransform>();
                hintRoot.sizeDelta = new Vector2(32f, 32f);

                hintPlate = hintRoot.gameObject.AddComponent<Image>();
                hintPlate.color = HintPlateColor;
                hintPlate.raycastTarget = false;
                hintPlate.type = Image.Type.Sliced;

                hintText = RuntimeUiFactory.CreateText("HintText", hintRoot, 14, TextAnchor.MiddleCenter);
                hintText.rectTransform.anchorMin = Vector2.zero;
                hintText.rectTransform.anchorMax = Vector2.one;
                hintText.rectTransform.offsetMin = Vector2.zero;
                hintText.rectTransform.offsetMax = Vector2.zero;
                hintText.color = HintTextColor;
                hintText.fontStyle = FontStyle.Bold;
                hintText.raycastTarget = false;

                actionRoot = RuntimeUiFactory.CreateUiObject("ActionRoot", root).GetComponent<RectTransform>();

                actionFill = actionRoot.gameObject.AddComponent<Image>();
                actionFill.color = ActionFillColor;
                actionFill.raycastTarget = false;
                actionFill.type = Image.Type.Sliced;

                actionAccent = RuntimeUiFactory.CreateImage("Accent", actionRoot, ActionAccentColor);
                actionAccent.raycastTarget = false;
                actionAccent.type = Image.Type.Sliced;

                labelText = RuntimeUiFactory.CreateText("Label", actionRoot, 19, TextAnchor.MiddleLeft);
                labelText.rectTransform.anchorMin = Vector2.zero;
                labelText.rectTransform.anchorMax = Vector2.one;
                labelText.color = ActionLabelColor;
                labelText.fontStyle = FontStyle.Bold;
                labelText.raycastTarget = false;

                root.gameObject.SetActive(false);
            }

            public void ConfigureLayout(float availableWidth, float leftInset, float cursor, bool mirror)
            {
                // Set anchoredPosition.x = 0 so that sizeDelta uniformly pads the right AND left edges inside the parent, keeping the element centered correctly within the layout bounds!
                root.anchoredPosition = new Vector2(0f, -cursor);
                root.sizeDelta = new Vector2(-leftInset * 2f, RowHeight);

                hintRoot.anchorMin = new Vector2(mirror ? 1f : 0f, 0.5f);
                hintRoot.anchorMax = hintRoot.anchorMin;
                hintRoot.pivot = new Vector2(mirror ? 1f : 0f, 0.5f);
                hintRoot.anchoredPosition = Vector2.zero;

                actionRoot.anchorMin = new Vector2(0f, 0.5f);
                actionRoot.anchorMax = new Vector2(1f, 0.5f);
                actionRoot.pivot = new Vector2(0.5f, 0.5f);
                actionRoot.offsetMin = new Vector2(mirror ? 0f : 40f, -RowHeight / 2f);
                actionRoot.offsetMax = new Vector2(mirror ? -40f : 0f, RowHeight / 2f);

                actionAccent.rectTransform.anchorMin = new Vector2(mirror ? 1f : 0f, 0f);
                actionAccent.rectTransform.anchorMax = new Vector2(mirror ? 1f : 0f, 1f);
                actionAccent.rectTransform.pivot = new Vector2(mirror ? 1f : 0f, 0.5f);
                actionAccent.rectTransform.sizeDelta = Vector2.zero;
                actionAccent.rectTransform.anchoredPosition = Vector2.zero;
                actionAccent.rectTransform.offsetMin = mirror ? new Vector2(-4f, 0f) : new Vector2(0f, 0f);
                actionAccent.rectTransform.offsetMax = mirror ? new Vector2(0f, 0f) : new Vector2(4f, 0f);

                labelText.alignment = mirror ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                labelText.rectTransform.offsetMin = new Vector2(14f, 0f);
                labelText.rectTransform.offsetMax = new Vector2(-14f, 0f);
            }

            public void SetData(string hint, string label, bool enabled, bool highlighted)
            {
                isEnabled = enabled;
                hintText.text = string.IsNullOrWhiteSpace(hint) ? "?" : hint;
                labelText.text = string.IsNullOrWhiteSpace(label) ? "Interact" : label;
                SetHighlighted(highlighted);
            }

            public void SetActive(bool value)
            {
                root.gameObject.SetActive(value);
            }

            public void SetHighlighted(bool value)
            {
                if (!isEnabled)
                {
                    hintPlate.color = HintPlateColor;
                    hintText.color = DisabledLabelColor;
                    actionFill.color = ActionFillDisabledColor;
                    actionAccent.color = DisabledLabelColor;
                    labelText.color = DisabledLabelColor;
                    return;
                }

                hintPlate.color = value ? HintPlateHighlightColor : HintPlateColor;
                hintText.color = value ? HintTextHighlightColor : HintTextColor;
                actionFill.color = value ? ActionFillHighlightColor : ActionFillColor;
                actionAccent.color = ActionAccentColor;
                labelText.color = value ? HintTextHighlightColor : ActionLabelColor;
            }

            public Vector3 GetHintAnchorWorldPosition()
            {
                return hintRoot.position;
            }

            public bool IsPointInside(Vector2 screenPos, Canvas canvas)
            {
                var eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;

                return RectTransformUtility.RectangleContainsScreenPoint(root, screenPos, eventCamera);
            }
        }
    }
}
