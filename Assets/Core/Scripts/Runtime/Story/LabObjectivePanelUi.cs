using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class LabObjectivePanelUi : MonoBehaviour
    {
        private const string RuntimeUiRootName = "LabObjectivePanelUiRoot";
        private const string MissionTitle = "Mission";

        public enum ObjectiveStage
        {
            EnteringLab,
            GreetCap,
            InspectBody,
            RouteLight,
            UseShrinkMachine,
            EnterRocket,
            MissionReady
        }

        private static readonly string[] ObjectiveLabels =
        {
            "Talk to CAP",
            "Inspect body model",
            "Route the light",
            "Use shrink machine",
            "Enter rocket"
        };

        [SerializeField] private string defaultTitle = "Mission";
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PanelSettings panelSettings;

        private GameObject runtimeUiRoot;
        private VisualElement overlay;
        private VisualElement card;
        private VisualElement titlePlate;
        private VisualElement progressPlate;
        private VisualElement checkBadge;
        private Label titleLabel;
        private Label progressLabel;
        private Label bodyLabel;
        private Label stepLabel;
        private Label checkLabel;
        private VisualElement hintCard;
        private Label hintLabel;
        private Coroutine transitionRoutine;
        private ObjectiveStage? lastStage;
        private bool compactMode;
        private bool built;
        private string lastTitle = string.Empty;
        private string lastBody = string.Empty;
        private string currentHint = string.Empty;

        private void Awake()
        {
            defaultTitle = NormalizeDefaultTitle(defaultTitle);
            EnsureUi();
            Hide();
        }

        public void Show(string body, string title = null)
        {
            ShowStage(ObjectiveStage.EnteringLab, body, title);
        }

        public void ShowStage(ObjectiveStage stage, string body, string title = null)
        {
            EnsureUi();
            if (!built || overlay == null)
            {
                return;
            }

            overlay.style.display = DisplayStyle.Flex;
            overlay.style.opacity = 1f;

            var resolvedTitle = string.IsNullOrWhiteSpace(title) ? defaultTitle.ToUpperInvariant() : title.ToUpperInvariant();
            var resolvedBody = string.IsNullOrWhiteSpace(body) ? "Follow CAP's lead." : body;

            if (!lastStage.HasValue)
            {
                ApplyState(stage, resolvedTitle, resolvedBody);
                lastStage = stage;
                lastTitle = resolvedTitle;
                lastBody = resolvedBody;
                return;
            }

            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
            }

            if (lastStage.Value == stage)
            {
                lastTitle = resolvedTitle;
                lastBody = resolvedBody;
                ApplyState(stage, resolvedTitle, resolvedBody);
                return;
            }

            transitionRoutine = StartCoroutine(AnimateStageTransition(lastStage.Value, stage, resolvedTitle, resolvedBody));
            lastStage = stage;
            lastTitle = resolvedTitle;
            lastBody = resolvedBody;
        }

        public void Hide()
        {
            EnsureUi();
            if (!built || overlay == null)
            {
                return;
            }

            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }

            overlay.style.opacity = 0f;
            overlay.style.display = DisplayStyle.None;
            lastStage = null;
            lastTitle = string.Empty;
            lastBody = string.Empty;
        }

        public void SetCompactMode(bool value)
        {
            EnsureUi();
            if (!built)
            {
                compactMode = value;
                return;
            }

            compactMode = value;
            ApplyCompactVisualState();
        }

        public void SetHint(string hint)
        {
            EnsureUi();
            currentHint = string.IsNullOrWhiteSpace(hint) ? string.Empty : hint.Trim();
            ApplyHintVisualState();
        }

        private void EnsureUi()
        {
            if (built)
            {
                return;
            }

            uiDocument = ResolveUiDocument();
            if (uiDocument == null)
            {
                return;
            }

            var root = uiDocument.rootVisualElement;
            if (root == null)
            {
                return;
            }

            root.Clear();
            root.style.flexGrow = 1f;
            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.right = 0f;
            root.style.top = 0f;
            root.style.bottom = 0f;
            root.pickingMode = PickingMode.Ignore;

            overlay = new VisualElement { name = "lab-objective-overlay" };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 20f;
            overlay.style.top = 16f;
            overlay.style.width = 262f;
            overlay.style.maxWidth = 262f;
            overlay.style.opacity = 0f;
            overlay.style.display = DisplayStyle.None;
            overlay.pickingMode = PickingMode.Ignore;

            var shadow = new VisualElement { name = "lab-objective-shadow" };
            shadow.style.position = Position.Absolute;
            shadow.style.left = 10f;
            shadow.style.top = 10f;
            shadow.style.right = -8f;
            shadow.style.bottom = -8f;
            shadow.style.backgroundColor = new Color(0.01f, 0.02f, 0.04f, 0.72f);
            shadow.style.borderTopLeftRadius = 18f;
            shadow.style.borderTopRightRadius = 18f;
            shadow.style.borderBottomLeftRadius = 18f;
            shadow.style.borderBottomRightRadius = 18f;
            overlay.Add(shadow);

            card = new VisualElement { name = "lab-objective-card" };
            card.style.position = Position.Relative;
            card.style.paddingLeft = 10f;
            card.style.paddingRight = 10f;
            card.style.paddingTop = 10f;
            card.style.paddingBottom = 9f;
            card.style.backgroundColor = new Color(0.99f, 0.92f, 0.49f, 0.96f);
            card.style.borderTopLeftRadius = 14f;
            card.style.borderTopRightRadius = 14f;
            card.style.borderBottomLeftRadius = 14f;
            card.style.borderBottomRightRadius = 14f;
            card.style.borderLeftWidth = 4f;
            card.style.borderRightWidth = 4f;
            card.style.borderTopWidth = 4f;
            card.style.borderBottomWidth = 4f;
            card.style.borderLeftColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            card.style.borderRightColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            card.style.borderTopColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            card.style.borderBottomColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            overlay.Add(card);

            var topAccent = new VisualElement { name = "lab-objective-top-accent" };
            topAccent.style.position = Position.Absolute;
            topAccent.style.left = 0f;
            topAccent.style.right = 0f;
            topAccent.style.top = 0f;
            topAccent.style.height = 8f;
            topAccent.style.backgroundColor = new Color(0.05f, 0.09f, 0.15f, 1f);
            topAccent.style.borderTopLeftRadius = 10f;
            topAccent.style.borderTopRightRadius = 10f;
            card.Add(topAccent);

            var headerRow = new VisualElement { name = "lab-objective-header" };
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.alignItems = Align.FlexStart;
            headerRow.style.marginTop = 0f;
            card.Add(headerRow);

            titlePlate = new VisualElement { name = "lab-objective-title-plate" };
            titlePlate.style.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            titlePlate.style.paddingLeft = 7f;
            titlePlate.style.paddingRight = 7f;
            titlePlate.style.paddingTop = 3f;
            titlePlate.style.paddingBottom = 3f;
            titlePlate.style.borderTopLeftRadius = 8f;
            titlePlate.style.borderTopRightRadius = 8f;
            titlePlate.style.borderBottomLeftRadius = 8f;
            titlePlate.style.borderBottomRightRadius = 8f;
            titlePlate.style.minWidth = 68f;
            titlePlate.style.marginRight = 6f;
            headerRow.Add(titlePlate);

            titleLabel = new Label();
            titleLabel.style.fontSize = 11f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(1f, 0.94f, 0.62f, 1f);
            titlePlate.Add(titleLabel);

            progressPlate = new VisualElement { name = "lab-objective-progress-plate" };
            progressPlate.style.backgroundColor = new Color(1f, 1f, 1f, 1f);
            progressPlate.style.paddingLeft = 7f;
            progressPlate.style.paddingRight = 7f;
            progressPlate.style.paddingTop = 3f;
            progressPlate.style.paddingBottom = 3f;
            progressPlate.style.borderTopLeftRadius = 8f;
            progressPlate.style.borderTopRightRadius = 8f;
            progressPlate.style.borderBottomLeftRadius = 8f;
            progressPlate.style.borderBottomRightRadius = 8f;
            headerRow.Add(progressPlate);

            progressLabel = new Label();
            progressLabel.style.fontSize = 9f;
            progressLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            progressLabel.style.color = new Color(0.04f, 0.05f, 0.08f, 1f);
            progressPlate.Add(progressLabel);

            bodyLabel = new Label();
            bodyLabel.style.marginTop = 8f;
            bodyLabel.style.color = new Color(0.04f, 0.05f, 0.08f, 1f);
            bodyLabel.style.fontSize = 12f;
            bodyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            bodyLabel.style.whiteSpace = WhiteSpace.Normal;
            bodyLabel.style.maxWidth = 204f;
            card.Add(bodyLabel);

            var stepCard = new VisualElement { name = "lab-objective-step-card" };
            stepCard.style.flexDirection = FlexDirection.Row;
            stepCard.style.alignItems = Align.Center;
            stepCard.style.marginTop = 8f;
            stepCard.style.paddingLeft = 7f;
            stepCard.style.paddingRight = 7f;
            stepCard.style.paddingTop = 5f;
            stepCard.style.paddingBottom = 5f;
            stepCard.style.backgroundColor = new Color(1f, 0.98f, 0.84f, 1f);
            stepCard.style.borderTopLeftRadius = 12f;
            stepCard.style.borderTopRightRadius = 12f;
            stepCard.style.borderBottomLeftRadius = 12f;
            stepCard.style.borderBottomRightRadius = 12f;
            stepCard.style.borderLeftWidth = 3f;
            stepCard.style.borderRightWidth = 3f;
            stepCard.style.borderTopWidth = 3f;
            stepCard.style.borderBottomWidth = 3f;
            stepCard.style.borderLeftColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            stepCard.style.borderRightColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            stepCard.style.borderTopColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            stepCard.style.borderBottomColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            card.Add(stepCard);

            checkBadge = new VisualElement { name = "lab-objective-check-badge" };
            checkBadge.style.width = 16f;
            checkBadge.style.height = 16f;
            checkBadge.style.marginRight = 6f;
            checkBadge.style.backgroundColor = new Color(1f, 0.84f, 0.16f, 1f);
            checkBadge.style.borderTopLeftRadius = 8f;
            checkBadge.style.borderTopRightRadius = 8f;
            checkBadge.style.borderBottomLeftRadius = 8f;
            checkBadge.style.borderBottomRightRadius = 8f;
            checkBadge.style.borderLeftWidth = 2f;
            checkBadge.style.borderRightWidth = 2f;
            checkBadge.style.borderTopWidth = 2f;
            checkBadge.style.borderBottomWidth = 2f;
            checkBadge.style.borderLeftColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            checkBadge.style.borderRightColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            checkBadge.style.borderTopColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            checkBadge.style.borderBottomColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            stepCard.Add(checkBadge);

            checkLabel = new Label();
            checkLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            checkLabel.style.fontSize = 10f;
            checkLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            checkLabel.style.color = new Color(0.08f, 0.09f, 0.12f, 0f);
            checkLabel.style.flexGrow = 1f;
            checkBadge.Add(checkLabel);

            stepLabel = new Label();
            stepLabel.style.flexGrow = 1f;
            stepLabel.style.fontSize = 11f;
            stepLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            stepLabel.style.color = new Color(0.04f, 0.05f, 0.08f, 1f);
            stepLabel.style.whiteSpace = WhiteSpace.Normal;
            stepCard.Add(stepLabel);

            hintCard = new VisualElement { name = "lab-objective-hint-card" };
            hintCard.style.marginTop = 7f;
            hintCard.style.paddingLeft = 7f;
            hintCard.style.paddingRight = 7f;
            hintCard.style.paddingTop = 4f;
            hintCard.style.paddingBottom = 4f;
            hintCard.style.backgroundColor = new Color(0.16f, 0.25f, 0.35f, 0.94f);
            hintCard.style.borderTopLeftRadius = 9f;
            hintCard.style.borderTopRightRadius = 9f;
            hintCard.style.borderBottomLeftRadius = 9f;
            hintCard.style.borderBottomRightRadius = 9f;
            hintCard.style.borderLeftWidth = 2f;
            hintCard.style.borderRightWidth = 2f;
            hintCard.style.borderTopWidth = 2f;
            hintCard.style.borderBottomWidth = 2f;
            hintCard.style.borderLeftColor = new Color(0.03f, 0.05f, 0.08f, 1f);
            hintCard.style.borderRightColor = new Color(0.03f, 0.05f, 0.08f, 1f);
            hintCard.style.borderTopColor = new Color(0.03f, 0.05f, 0.08f, 1f);
            hintCard.style.borderBottomColor = new Color(0.03f, 0.05f, 0.08f, 1f);
            hintCard.style.display = DisplayStyle.None;
            card.Add(hintCard);

            hintLabel = new Label();
            hintLabel.style.color = new Color(0.87f, 0.97f, 1f, 1f);
            hintLabel.style.fontSize = 10f;
            hintLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            hintLabel.style.whiteSpace = WhiteSpace.Normal;
            hintLabel.style.maxWidth = 214f;
            hintCard.Add(hintLabel);

            root.Add(overlay);
            ApplyCompactVisualState();
            ApplyHintVisualState();
            built = true;
        }

        private void ApplyCompactVisualState()
        {
            if (!built || overlay == null || card == null || bodyLabel == null || stepLabel == null || titlePlate == null || progressPlate == null)
            {
                return;
            }

            overlay.style.left = compactMode ? 16f : 20f;
            overlay.style.top = compactMode ? 12f : 16f;
            overlay.style.width = compactMode ? 212f : 262f;
            overlay.style.maxWidth = compactMode ? 212f : 262f;

            card.style.paddingLeft = compactMode ? 8f : 9f;
            card.style.paddingRight = compactMode ? 8f : 9f;
            card.style.paddingTop = compactMode ? 8f : 9f;
            card.style.paddingBottom = compactMode ? 7f : 8f;
            card.style.backgroundColor = compactMode
                ? new Color(0.99f, 0.92f, 0.49f, 0.78f)
                : new Color(0.99f, 0.92f, 0.49f, 0.96f);
            card.style.borderLeftWidth = compactMode ? 3f : 4f;
            card.style.borderRightWidth = compactMode ? 3f : 4f;
            card.style.borderTopWidth = compactMode ? 3f : 4f;
            card.style.borderBottomWidth = compactMode ? 3f : 4f;
            card.style.borderLeftColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            card.style.borderRightColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            card.style.borderTopColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            card.style.borderBottomColor = new Color(0.04f, 0.05f, 0.08f, 1f);

            titlePlate.style.backgroundColor = new Color(0.04f, 0.05f, 0.08f, compactMode ? 0.92f : 1f);
            progressPlate.style.backgroundColor = new Color(1f, 1f, 1f, compactMode ? 0.88f : 1f);
            bodyLabel.style.fontSize = compactMode ? 11f : 12f;
            bodyLabel.style.maxWidth = compactMode ? 178f : 214f;
            bodyLabel.style.color = new Color(0.04f, 0.05f, 0.08f, 1f);
            stepLabel.style.fontSize = compactMode ? 10f : 11f;
            stepLabel.style.color = new Color(0.04f, 0.05f, 0.08f, 1f);

            if (hintCard != null)
            {
                hintCard.style.marginTop = compactMode ? 5f : 7f;
                hintCard.style.paddingLeft = compactMode ? 6f : 7f;
                hintCard.style.paddingRight = compactMode ? 6f : 7f;
                hintCard.style.paddingTop = compactMode ? 3f : 4f;
                hintCard.style.paddingBottom = compactMode ? 3f : 4f;
            }

            if (hintLabel != null)
            {
                hintLabel.style.fontSize = compactMode ? 9f : 10f;
                hintLabel.style.maxWidth = compactMode ? 178f : 214f;
            }

            ApplyHintVisualState();
        }

        private UIDocument ResolveUiDocument()
        {
            runtimeUiRoot = runtimeUiRoot != null ? runtimeUiRoot : GameObject.Find(RuntimeUiRootName);
            if (runtimeUiRoot == null)
            {
                runtimeUiRoot = new GameObject(RuntimeUiRootName);
            }

            if (runtimeUiRoot.transform.parent != null)
            {
                runtimeUiRoot.transform.SetParent(null, false);
            }

            var runtimeDocument = runtimeUiRoot.GetComponent<UIDocument>();
            if (runtimeDocument == null)
            {
                runtimeDocument = runtimeUiRoot.AddComponent<UIDocument>();
            }

            EnsurePanelSettingsBound(runtimeDocument);
            runtimeDocument.sortingOrder = Mathf.Max(runtimeDocument.sortingOrder, 650);
            uiDocument = runtimeDocument;
            return uiDocument;
        }

        private void EnsurePanelSettingsBound(UIDocument document)
        {
            if (document == null)
            {
                return;
            }

            if (document.panelSettings != null)
            {
                return;
            }

            if (panelSettings != null)
            {
                document.panelSettings = panelSettings;
                return;
            }

            var documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < documents.Length; index++)
            {
                var candidate = documents[index];
                if (candidate == null || candidate == document || candidate.panelSettings == null)
                {
                    continue;
                }

                document.panelSettings = candidate.panelSettings;
                return;
            }

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = panelSettings;
        }

        private IEnumerator AnimateStageTransition(ObjectiveStage previousStage, ObjectiveStage nextStage, string title, string body)
        {
            var previousIndex = ResolveCurrentIndex(previousStage);
            var nextIndex = ResolveCurrentIndex(nextStage);

            if (nextIndex > previousIndex && previousStage != ObjectiveStage.EnteringLab)
            {
                const float completeDuration = 0.2f;
                var elapsed = 0f;
                while (elapsed < completeDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(elapsed / completeDuration);
                    var pulse = Mathf.Sin(t * Mathf.PI);
                    checkBadge.style.backgroundColor = Color.Lerp(new Color(0.94f, 0.92f, 0.85f, 1f), new Color(0.28f, 0.8f, 0.46f, 1f), t);
                    checkLabel.text = "✓";
                    checkLabel.style.color = new Color(0.08f, 0.09f, 0.12f, t);
                    card.style.scale = new Scale(new Vector2(1f + (pulse * 0.02f), 1f + (pulse * 0.02f)));
                    yield return null;
                }
            }

            const float fadeOutDuration = 0.14f;
            var fadeOutElapsed = 0f;
            while (fadeOutElapsed < fadeOutDuration)
            {
                fadeOutElapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(fadeOutElapsed / fadeOutDuration);
                card.style.opacity = 1f - t;
                card.style.translate = new Translate(-18f * t, 0f, 0f);
                yield return null;
            }

            ApplyState(nextStage, title, body);
            card.style.opacity = 0f;
            card.style.translate = new Translate(18f, 0f, 0f);

            const float fadeInDuration = 0.18f;
            var fadeInElapsed = 0f;
            while (fadeInElapsed < fadeInDuration)
            {
                fadeInElapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(fadeInElapsed / fadeInDuration);
                card.style.opacity = t;
                card.style.translate = new Translate(18f * (1f - t), 0f, 0f);
                yield return null;
            }

            card.style.opacity = 1f;
            card.style.translate = new Translate(0f, 0f, 0f);
            card.style.scale = new Scale(Vector2.one);
            transitionRoutine = null;
        }

        private void ApplyState(ObjectiveStage stage, string title, string body)
        {
            titleLabel.text = title;
            bodyLabel.text = body;

            if (stage == ObjectiveStage.MissionReady)
            {
                progressLabel.text = "DONE";
                stepLabel.text = "Mission complete";
                checkBadge.style.backgroundColor = new Color(0.28f, 0.8f, 0.46f, 1f);
                checkLabel.text = "✓";
                checkLabel.style.color = new Color(0.08f, 0.09f, 0.12f, 1f);
                card.style.backgroundColor = compactMode ? new Color(0.14f, 0.28f, 0.19f, 0.72f) : new Color(0.92f, 0.96f, 0.9f, 0.985f);
                ApplyHintVisualState();
                return;
            }

            var index = ResolveCurrentIndex(stage);
            progressLabel.text = $"{Mathf.Clamp(index + 1, 1, ObjectiveLabels.Length)}/{ObjectiveLabels.Length}";
            stepLabel.text = ObjectiveLabels[Mathf.Clamp(index, 0, ObjectiveLabels.Length - 1)];
            checkBadge.style.backgroundColor = new Color(0.94f, 0.92f, 0.85f, 1f);
            checkLabel.text = string.Empty;
            checkLabel.style.color = new Color(0.08f, 0.09f, 0.12f, 0f);
            card.style.backgroundColor = compactMode ? new Color(0.08f, 0.1f, 0.14f, 0.58f) : new Color(0.97f, 0.94f, 0.89f, 0.985f);
            card.style.opacity = 1f;
            card.style.translate = new Translate(0f, 0f, 0f);
            card.style.scale = new Scale(Vector2.one);
            ApplyCompactVisualState();
            ApplyHintVisualState();
        }

        private void ApplyHintVisualState()
        {
            if (hintCard == null || hintLabel == null)
            {
                return;
            }

            var hasHint = !string.IsNullOrWhiteSpace(currentHint);
            hintCard.style.display = hasHint ? DisplayStyle.Flex : DisplayStyle.None;
            hintLabel.text = hasHint ? currentHint : string.Empty;
        }

        private static int ResolveCurrentIndex(ObjectiveStage stage)
        {
            switch (stage)
            {
                case ObjectiveStage.EnteringLab:
                case ObjectiveStage.GreetCap:
                    return 0;
                case ObjectiveStage.InspectBody:
                    return 1;
                case ObjectiveStage.RouteLight:
                    return 2;
                case ObjectiveStage.UseShrinkMachine:
                    return 3;
                case ObjectiveStage.EnterRocket:
                    return 4;
                default:
                    return 4;
            }
        }

        private static string NormalizeDefaultTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return MissionTitle;
            }

            var trimmed = title.Trim();
            return trimmed.Equals("Objective", System.StringComparison.OrdinalIgnoreCase)
                ? MissionTitle
                : trimmed;
        }
    }
}
