using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class BedroomObjectivePanelUi : MonoBehaviour
    {
        private const string RuntimeUiRootName = "BedroomObjectivePanelUiRoot";

        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PanelSettings panelSettings;

        private GameObject runtimeUiRoot;
        private VisualElement overlay;
        private VisualElement card;
        private Label titleLabel;
        private Label progressLabel;
        private Label objectiveLabel;
        private Label detailLabel;
        private Label checkLabel;
        private bool built;
        private Coroutine ensureUiRetryCoroutine;
        private string cachedObjective = "Follow the objective.";
        private string cachedDetail = string.Empty;
        private int cachedCurrentStep = 1;
        private int cachedTotalSteps = 1;
        private bool cachedCompleted;
        private bool objectiveVisibleRequested;

        private void Awake()
        {
            EnsureUi();
            Hide();
        }

        public void ShowObjective(string objective, string detail, int currentStep, int totalSteps, bool completed)
        {
            cachedObjective = string.IsNullOrWhiteSpace(objective) ? "Follow the objective." : objective.Trim();
            cachedDetail = string.IsNullOrWhiteSpace(detail) ? string.Empty : detail.Trim();
            cachedCurrentStep = currentStep;
            cachedTotalSteps = totalSteps;
            cachedCompleted = completed;
            objectiveVisibleRequested = true;
            EnsureUi();
            ApplyRequestedState();
        }

        public void Hide()
        {
            objectiveVisibleRequested = false;
            EnsureUi();
            ApplyRequestedState();
        }

        private void EnsureUi()
        {
            if (built)
            {
                ApplyRequestedState();
                return;
            }

            uiDocument = ResolveUiDocument();
            if (uiDocument == null)
            {
                return;
            }

            if (!uiDocument.enabled)
            {
                uiDocument.enabled = true;
            }

            var root = uiDocument.rootVisualElement;
            if (root == null)
            {
                uiDocument.enabled = false;
                uiDocument.enabled = true;
                root = uiDocument.rootVisualElement;
            }

            if (root == null)
            {
                QueueEnsureUiRetry();
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

            overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 20f;
            overlay.style.top = 16f;
            overlay.style.width = 312f;
            overlay.style.maxWidth = 312f;
            overlay.style.display = DisplayStyle.None;
            overlay.style.opacity = 0f;
            overlay.pickingMode = PickingMode.Ignore;
            root.Add(overlay);

            var shadow = new VisualElement();
            shadow.style.position = Position.Absolute;
            shadow.style.left = 10f;
            shadow.style.top = 10f;
            shadow.style.right = -8f;
            shadow.style.bottom = -8f;
            shadow.style.backgroundColor = new Color(0.03f, 0.05f, 0.08f, 0.62f);
            shadow.style.borderTopLeftRadius = 16f;
            shadow.style.borderTopRightRadius = 16f;
            shadow.style.borderBottomLeftRadius = 16f;
            shadow.style.borderBottomRightRadius = 16f;
            overlay.Add(shadow);

            card = new VisualElement();
            card.style.position = Position.Relative;
            card.style.paddingLeft = 14f;
            card.style.paddingRight = 14f;
            card.style.paddingTop = 12f;
            card.style.paddingBottom = 12f;
            card.style.backgroundColor = new Color(0.98f, 0.94f, 0.58f, 0.96f);
            card.style.borderTopLeftRadius = 14f;
            card.style.borderTopRightRadius = 14f;
            card.style.borderBottomLeftRadius = 14f;
            card.style.borderBottomRightRadius = 14f;
            card.style.borderLeftWidth = 4f;
            card.style.borderRightWidth = 4f;
            card.style.borderTopWidth = 4f;
            card.style.borderBottomWidth = 4f;
            card.style.borderLeftColor = new Color(0.04f, 0.06f, 0.08f, 1f);
            card.style.borderRightColor = new Color(0.04f, 0.06f, 0.08f, 1f);
            card.style.borderTopColor = new Color(0.04f, 0.06f, 0.08f, 1f);
            card.style.borderBottomColor = new Color(0.04f, 0.06f, 0.08f, 1f);
            overlay.Add(card);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8f;
            card.Add(header);

            var titleBadge = new VisualElement();
            titleBadge.style.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            titleBadge.style.paddingLeft = 10f;
            titleBadge.style.paddingRight = 10f;
            titleBadge.style.paddingTop = 5f;
            titleBadge.style.paddingBottom = 5f;
            titleBadge.style.borderTopLeftRadius = 10f;
            titleBadge.style.borderTopRightRadius = 10f;
            titleBadge.style.borderBottomLeftRadius = 10f;
            titleBadge.style.borderBottomRightRadius = 10f;
            header.Add(titleBadge);

            titleLabel = new Label("MISSION");
            titleLabel.style.fontSize = 15f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(1f, 0.96f, 0.74f, 1f);
            titleBadge.Add(titleLabel);

            var progressBadge = new VisualElement();
            progressBadge.style.backgroundColor = new Color(0.98f, 0.98f, 0.98f, 1f);
            progressBadge.style.paddingLeft = 10f;
            progressBadge.style.paddingRight = 10f;
            progressBadge.style.paddingTop = 5f;
            progressBadge.style.paddingBottom = 5f;
            progressBadge.style.borderTopLeftRadius = 10f;
            progressBadge.style.borderTopRightRadius = 10f;
            progressBadge.style.borderBottomLeftRadius = 10f;
            progressBadge.style.borderBottomRightRadius = 10f;
            header.Add(progressBadge);

            progressLabel = new Label("0/2");
            progressLabel.style.fontSize = 14f;
            progressLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            progressLabel.style.color = new Color(0.05f, 0.08f, 0.11f, 1f);
            progressBadge.Add(progressLabel);

            var objectiveRow = new VisualElement();
            objectiveRow.style.flexDirection = FlexDirection.Row;
            objectiveRow.style.alignItems = Align.Center;
            objectiveRow.style.paddingLeft = 8f;
            objectiveRow.style.paddingRight = 8f;
            objectiveRow.style.paddingTop = 7f;
            objectiveRow.style.paddingBottom = 7f;
            objectiveRow.style.marginTop = 6f;
            objectiveRow.style.backgroundColor = new Color(1f, 0.98f, 0.85f, 1f);
            objectiveRow.style.borderTopLeftRadius = 12f;
            objectiveRow.style.borderTopRightRadius = 12f;
            objectiveRow.style.borderBottomLeftRadius = 12f;
            objectiveRow.style.borderBottomRightRadius = 12f;
            objectiveRow.style.borderLeftWidth = 3f;
            objectiveRow.style.borderRightWidth = 3f;
            objectiveRow.style.borderTopWidth = 3f;
            objectiveRow.style.borderBottomWidth = 3f;
            objectiveRow.style.borderLeftColor = new Color(0.08f, 0.1f, 0.12f, 1f);
            objectiveRow.style.borderRightColor = new Color(0.08f, 0.1f, 0.12f, 1f);
            objectiveRow.style.borderTopColor = new Color(0.08f, 0.1f, 0.12f, 1f);
            objectiveRow.style.borderBottomColor = new Color(0.08f, 0.1f, 0.12f, 1f);
            card.Add(objectiveRow);

            var checkBadge = new VisualElement();
            checkBadge.style.width = 18f;
            checkBadge.style.height = 18f;
            checkBadge.style.marginRight = 8f;
            checkBadge.style.backgroundColor = new Color(1f, 0.82f, 0.2f, 1f);
            checkBadge.style.borderTopLeftRadius = 9f;
            checkBadge.style.borderTopRightRadius = 9f;
            checkBadge.style.borderBottomLeftRadius = 9f;
            checkBadge.style.borderBottomRightRadius = 9f;
            checkBadge.style.borderLeftWidth = 2f;
            checkBadge.style.borderRightWidth = 2f;
            checkBadge.style.borderTopWidth = 2f;
            checkBadge.style.borderBottomWidth = 2f;
            checkBadge.style.borderLeftColor = new Color(0.08f, 0.1f, 0.12f, 1f);
            checkBadge.style.borderRightColor = new Color(0.08f, 0.1f, 0.12f, 1f);
            checkBadge.style.borderTopColor = new Color(0.08f, 0.1f, 0.12f, 1f);
            checkBadge.style.borderBottomColor = new Color(0.08f, 0.1f, 0.12f, 1f);
            objectiveRow.Add(checkBadge);

            checkLabel = new Label("✓");
            checkLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            checkLabel.style.fontSize = 10f;
            checkLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            checkLabel.style.color = new Color(0.08f, 0.12f, 0.12f, 0f);
            checkLabel.style.flexGrow = 1f;
            checkBadge.Add(checkLabel);

            objectiveLabel = new Label("Follow the objective.");
            objectiveLabel.style.flexGrow = 1f;
            objectiveLabel.style.whiteSpace = WhiteSpace.Normal;
            objectiveLabel.style.fontSize = 18f;
            objectiveLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            objectiveLabel.style.color = new Color(0.06f, 0.08f, 0.11f, 1f);
            objectiveRow.Add(objectiveLabel);

            detailLabel = new Label(string.Empty);
            detailLabel.style.marginTop = 8f;
            detailLabel.style.fontSize = 15f;
            detailLabel.style.whiteSpace = WhiteSpace.Normal;
            detailLabel.style.color = new Color(0.08f, 0.11f, 0.14f, 0.92f);
            card.Add(detailLabel);

            built = true;
            ensureUiRetryCoroutine = null;
            ApplyRequestedState();
        }

        private void ApplyRequestedState()
        {
            if (!built || overlay == null)
            {
                return;
            }

            if (!objectiveVisibleRequested)
            {
                overlay.style.opacity = 0f;
                overlay.style.display = DisplayStyle.None;
                return;
            }

            var safeTotal = Mathf.Max(1, cachedTotalSteps);
            var safeCurrent = Mathf.Clamp(cachedCurrentStep, 0, safeTotal);

            titleLabel.text = "MISSION";
            progressLabel.text = $"{safeCurrent}/{safeTotal}";
            objectiveLabel.text = cachedObjective;
            detailLabel.text = cachedDetail;
            checkLabel.style.color = cachedCompleted
                ? new Color(0.08f, 0.12f, 0.12f, 1f)
                : new Color(0.08f, 0.12f, 0.12f, 0f);
            card.style.backgroundColor = cachedCompleted
                ? new Color(0.77f, 0.93f, 0.78f, 0.96f)
                : new Color(0.98f, 0.94f, 0.58f, 0.96f);

            overlay.style.display = DisplayStyle.Flex;
            overlay.style.opacity = 1f;
        }

        private void QueueEnsureUiRetry()
        {
            if (ensureUiRetryCoroutine != null || !isActiveAndEnabled)
            {
                return;
            }

            ensureUiRetryCoroutine = StartCoroutine(EnsureUiReadyRoutine());
        }

        private IEnumerator EnsureUiReadyRoutine()
        {
            const int maxRetryFrames = 30;
            for (int frame = 0; frame < maxRetryFrames && !built; frame++)
            {
                yield return null;
                EnsureUi();
            }

            ensureUiRetryCoroutine = null;
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

            runtimeDocument.enabled = true;

            EnsurePanelSettingsBound(runtimeDocument);
            runtimeDocument.sortingOrder = Mathf.Max(runtimeDocument.sortingOrder, 660);
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
    }
}
