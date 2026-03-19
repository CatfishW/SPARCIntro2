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
        private const float TitleHeight = 22f;
        private const float TitleUnderlineWidth = 108f;
        private const float TitleUnderlineHeight = 2f;
        private const float TitleUnderlineGap = 7f;
        private const float TitleToRowsGap = 12f;
        private const float RowHeight = 30f;
        private const float RowSpacing = 8f;

        private static readonly Color TitleColor = new Color(0.97f, 0.98f, 1f, 0.98f);
        private static readonly Color TitleUnderlineColor = new Color(0.21f, 0.69f, 1f, 0.95f);
        private static readonly Color ConnectorColor = new Color(1f, 1f, 1f, 0.96f);
        private static readonly Color HintPlateColor = new Color(0.05f, 0.13f, 0.22f, 0.92f);
        private static readonly Color HintPlateHighlightColor = new Color(0.17f, 0.54f, 0.90f, 0.95f);
        private static readonly Color HintTextColor = new Color(0.95f, 0.98f, 1f, 0.96f);
        private static readonly Color HintTextHighlightColor = new Color(0.03f, 0.07f, 0.12f, 1f);
        private static readonly Color ActionFillColor = new Color(0.04f, 0.18f, 0.31f, 0.88f);
        private static readonly Color ActionFillHighlightColor = new Color(0.08f, 0.32f, 0.57f, 0.96f);
        private static readonly Color ActionFillDisabledColor = new Color(0.04f, 0.10f, 0.16f, 0.52f);
        private static readonly Color ActionAccentColor = new Color(0.25f, 0.77f, 1f, 1f);
        private static readonly Color ActionLabelColor = new Color(0.94f, 0.97f, 1f, 0.98f);
        private static readonly Color DisabledLabelColor = new Color(0.73f, 0.80f, 0.88f, 0.56f);

        [Header("Layout")]
        [SerializeField, Min(180f)] private float promptWidth = 224f;
        [SerializeField, Min(48f)] private float connectorHorizontalOffset = 110f;
        [SerializeField] private float promptVerticalOffset = -12f;
        [SerializeField, Min(0f)] private float screenMargin = 48f;

        [Header("Animation")]
        [SerializeField, Min(0.01f)] private float panelFadeDuration = 0.12f;
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

            var promptHeight = LayoutPrompt(visibleCount, itemCanvasPoint);
            var promptPosition = ResolvePromptPosition(itemCanvasPoint, promptHeight);

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
            UpdateConnector(itemCanvasPoint, connectorPoint, eased);

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
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

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
            promptRoot.anchorMin = new Vector2(0.5f, 0.5f);
            promptRoot.anchorMax = new Vector2(0.5f, 0.5f);
            promptRoot.pivot = new Vector2(0f, 0.5f);
            promptRoot.sizeDelta = new Vector2(promptWidth, 84f);

            promptGroup = promptObject.AddComponent<CanvasGroup>();
            promptGroup.alpha = 0f;

            titleText = RuntimeUiFactory.CreateText("Title", promptRoot, 18, TextAnchor.MiddleLeft);
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
            var promptHeight =
                TitleHeight +
                TitleUnderlineGap +
                TitleUnderlineHeight +
                TitleToRowsGap +
                (visibleCount * RowHeight) +
                (Mathf.Max(visibleCount - 1, 0) * RowSpacing);

            promptRoot.sizeDelta = new Vector2(promptWidth, promptHeight);

            currentPromptOnRight = ResolvePromptSide(itemCanvasPoint);
            promptRoot.pivot = new Vector2(currentPromptOnRight ? 0f : 1f, 0.5f);

            titleText.alignment = currentPromptOnRight ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;

            titleUnderline.rectTransform.anchorMin = new Vector2(currentPromptOnRight ? 0f : 1f, 1f);
            titleUnderline.rectTransform.anchorMax = titleUnderline.rectTransform.anchorMin;
            titleUnderline.rectTransform.pivot = new Vector2(currentPromptOnRight ? 0f : 1f, 1f);
            titleUnderline.rectTransform.sizeDelta = new Vector2(TitleUnderlineWidth, TitleUnderlineHeight);
            titleUnderline.rectTransform.anchoredPosition = new Vector2(0f, -(TitleHeight + TitleUnderlineGap));

            var cursor = TitleHeight + TitleUnderlineGap + TitleUnderlineHeight + TitleToRowsGap;
            foreach (var row in rows)
            {
                if (!row.IsActive)
                {
                    continue;
                }

                row.ConfigureLayout(promptWidth, cursor, !currentPromptOnRight);
                cursor += RowHeight + RowSpacing;
            }

            return promptHeight;
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

            ApplyConnectorSegment(connectorSegmentA.rectTransform, startPoint, bend, alpha);
            ApplyConnectorSegment(connectorSegmentB.rectTransform, bend, endPoint, alpha);

            connectorDot.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            connectorDot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            connectorDot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            connectorDot.rectTransform.anchoredPosition = startPoint;

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

            var showPrompt = panelT > 0.001f;
            promptRoot.gameObject.SetActive(showPrompt);
            promptGroup.alpha = panelT;
            promptRoot.localScale = Vector3.one * Mathf.Lerp(1f - panelSpawnScaleOffset, 1f, panelT);
        }

        private bool HasValidRuntimeState()
        {
            if (canvas == null || canvasRect == null || connectorRoot == null || connectorDot == null || connectorSegmentA == null || connectorSegmentB == null || promptRoot == null || promptGroup == null || titleText == null || titleUnderline == null)
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
                hintRoot.sizeDelta = new Vector2(28f, 28f);

                hintPlate = hintRoot.gameObject.AddComponent<Image>();
                hintPlate.color = HintPlateColor;
                hintPlate.raycastTarget = false;

                hintText = RuntimeUiFactory.CreateText("HintText", hintRoot, 13, TextAnchor.MiddleCenter);
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

                actionAccent = RuntimeUiFactory.CreateImage("Accent", actionRoot, ActionAccentColor);
                actionAccent.raycastTarget = false;

                labelText = RuntimeUiFactory.CreateText("Label", actionRoot, 17, TextAnchor.MiddleLeft);
                labelText.rectTransform.anchorMin = Vector2.zero;
                labelText.rectTransform.anchorMax = Vector2.one;
                labelText.color = ActionLabelColor;
                labelText.fontStyle = FontStyle.Bold;
                labelText.raycastTarget = false;

                root.gameObject.SetActive(false);
            }

            public void ConfigureLayout(float width, float cursor, bool mirror)
            {
                root.anchoredPosition = new Vector2(0f, -cursor);
                root.sizeDelta = new Vector2(0f, RowHeight);

                hintRoot.anchorMin = new Vector2(mirror ? 1f : 0f, 0.5f);
                hintRoot.anchorMax = hintRoot.anchorMin;
                hintRoot.pivot = new Vector2(mirror ? 1f : 0f, 0.5f);
                hintRoot.anchoredPosition = Vector2.zero;

                actionRoot.anchorMin = new Vector2(0f, 0.5f);
                actionRoot.anchorMax = new Vector2(1f, 0.5f);
                actionRoot.pivot = new Vector2(0.5f, 0.5f);
                actionRoot.offsetMin = new Vector2(mirror ? 0f : 38f, -14f);
                actionRoot.offsetMax = new Vector2(mirror ? -38f : 0f, 14f);

                actionAccent.rectTransform.anchorMin = new Vector2(mirror ? 1f : 0f, 0f);
                actionAccent.rectTransform.anchorMax = new Vector2(mirror ? 1f : 0f, 1f);
                actionAccent.rectTransform.pivot = new Vector2(mirror ? 1f : 0f, 0.5f);
                actionAccent.rectTransform.sizeDelta = new Vector2(5f, 0f);
                actionAccent.rectTransform.anchoredPosition = Vector2.zero;

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
                    hintPlate.color = HintPlateColor * new Color(1f, 1f, 1f, 0.65f);
                    hintText.color = DisabledLabelColor;
                    actionFill.color = ActionFillDisabledColor;
                    labelText.color = DisabledLabelColor;
                    return;
                }

                hintPlate.color = value ? HintPlateHighlightColor : HintPlateColor;
                hintText.color = value ? HintTextHighlightColor : HintTextColor;
                actionFill.color = value ? ActionFillHighlightColor : ActionFillColor;
                labelText.color = ActionLabelColor;
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
