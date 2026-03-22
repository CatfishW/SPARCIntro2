using System;
using System.Collections;
using ItemInteraction;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class LabLightPuzzleUi : MonoBehaviour
    {
        private const int GridSize = 4;

        [SerializeField] private ClassroomPlayerControlLock controlLock;
        [SerializeField] private InteractionDirector interactionDirector;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PanelSettings panelSettings;
        [SerializeField, Min(0.1f)] private float openRetrySeconds = 2f;
        [SerializeField, Min(0.05f)] private float solvedFlashSeconds = 0.32f;
        [SerializeField, Min(0.05f)] private float pulseSpeed = 5.2f;

        private VisualElement overlay;
        private VisualElement panel;
        private VisualElement missionBackdrop;
        private VisualElement panelGlow;
        private Label titleLabel;
        private Label subtitleLabel;
        private Label hintLabel;
        private Label statusLabel;
        private Button resetButton;
        private Button closeButton;
        private Button[,] cellButtons;
        private VisualElement[] legendSwatches;
        private Label[] legendLabels;
        private int[] currentPaths;
        private int activeColorIndex = -1;
        private bool isDraggingPath;
        private bool built;
        private bool solvedRaised;
        private Coroutine deferredOpenRoutine;
        private Coroutine solvedFlashRoutine;
        private LabObjectivePanelUi objectivePanelUi;

        public event Action Solved;

        public bool IsOpen { get; private set; }
        public bool IsSolved { get; private set; }

        private enum CellType
        {
            Empty,
            Source,
            Target,
            Blocked
        }

        private struct CellDefinition
        {
            public CellType Type;
            public int ColorIndex;

            public CellDefinition(CellType type, int colorIndex)
            {
                Type = type;
                ColorIndex = colorIndex;
            }
        }

        private static readonly CellDefinition[] BoardTemplate =
        {
            new(CellType.Source, 0), new(CellType.Empty, -1), new(CellType.Empty, -1), new(CellType.Source, 1),
            new(CellType.Empty, -1), new(CellType.Empty, -1), new(CellType.Empty, -1), new(CellType.Empty, -1),
            new(CellType.Empty, -1), new(CellType.Empty, -1), new(CellType.Empty, -1), new(CellType.Empty, -1),
            new(CellType.Target, 0), new(CellType.Empty, -1), new(CellType.Target, 1), new(CellType.Empty, -1)
        };

        private static readonly Color[] FlowColors =
        {
            new(0.22f, 0.86f, 1f, 1f),
            new(1f, 0.83f, 0.22f, 1f)
        };

        private static readonly Color[] FlowDimColors =
        {
            new(0.16f, 0.32f, 0.42f, 1f),
            new(0.42f, 0.34f, 0.16f, 1f)
        };

        private static readonly string[] FlowNames =
        {
            "Blue reactor flow",
            "Amber machine flow"
        };

        private static readonly Vector2Int[] SourceCells =
        {
            new(0, 0),
            new(3, 0)
        };

        private static readonly Vector2Int[] TargetCells =
        {
            new(0, 3),
            new(2, 3)
        };

        private void Awake()
        {
            EnsureBuilt();
            HideImmediate();
        }

        public void Open()
        {
            ResolveRuntimeReferences();
            if (TryOpenNow())
            {
                return;
            }

            if (deferredOpenRoutine != null)
            {
                return;
            }

            deferredOpenRoutine = StartCoroutine(OpenWhenReadyRoutine());
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            if (deferredOpenRoutine != null)
            {
                StopCoroutine(deferredOpenRoutine);
                deferredOpenRoutine = null;
            }

            if (overlay != null)
            {
                overlay.style.display = DisplayStyle.None;
            }

            if (solvedFlashRoutine != null)
            {
                StopCoroutine(solvedFlashRoutine);
                solvedFlashRoutine = null;
            }

            IsOpen = false;
            objectivePanelUi?.SetCompactMode(false);
            interactionDirector?.SetInteractionsLocked(false);
            controlLock?.Release();
        }

        public void HideImmediate()
        {
            EnsureBuilt();
            if (deferredOpenRoutine != null)
            {
                StopCoroutine(deferredOpenRoutine);
                deferredOpenRoutine = null;
            }

            if (IsOpen)
            {
                objectivePanelUi?.SetCompactMode(false);
                interactionDirector?.SetInteractionsLocked(false);
                controlLock?.Release();
            }

            if (overlay != null)
            {
                overlay.style.display = DisplayStyle.None;
            }

            if (solvedFlashRoutine != null)
            {
                StopCoroutine(solvedFlashRoutine);
                solvedFlashRoutine = null;
            }

            IsOpen = false;
        }

        public void ResetPuzzleState()
        {
            currentPaths = new int[BoardTemplate.Length];
            for (var index = 0; index < currentPaths.Length; index++)
            {
                currentPaths[index] = BoardTemplate[index].Type == CellType.Source || BoardTemplate[index].Type == CellType.Target
                    ? BoardTemplate[index].ColorIndex
                    : -1;
            }

            activeColorIndex = -1;
            isDraggingPath = false;
            IsSolved = false;
            solvedRaised = false;
            if (solvedFlashRoutine != null)
            {
                StopCoroutine(solvedFlashRoutine);
                solvedFlashRoutine = null;
            }
            UpdateBoard();
        }

        private bool TryOpenNow()
        {
            EnsureBuilt();
            if (!built || overlay == null || uiDocument == null)
            {
                return false;
            }

            var root = uiDocument.rootVisualElement;
            if (root == null)
            {
                return false;
            }

            root.style.display = DisplayStyle.Flex;
            root.style.visibility = Visibility.Visible;

            if (overlay.parent != root)
            {
                root.Clear();
                root.Add(overlay);
            }

            ResetPuzzleState();
            if (!uiDocument.gameObject.activeSelf)
            {
                uiDocument.gameObject.SetActive(true);
            }

            uiDocument.enabled = true;
            uiDocument.sortingOrder = Mathf.Max(uiDocument.sortingOrder, 710);
            overlay.style.display = DisplayStyle.Flex;
            overlay.style.visibility = Visibility.Visible;
            IsOpen = true;
            objectivePanelUi?.SetCompactMode(true);
            interactionDirector?.SetInteractionsLocked(true);
            controlLock?.Acquire(unlockCursor: true);
            return true;
        }

        private IEnumerator OpenWhenReadyRoutine()
        {
            var timeoutAt = Time.unscaledTime + Mathf.Max(0.1f, openRetrySeconds);
            while (Time.unscaledTime < timeoutAt)
            {
                if (TryOpenNow())
                {
                    deferredOpenRoutine = null;
                    yield break;
                }

                yield return null;
            }

            deferredOpenRoutine = null;
        }

        private void EnsureBuilt()
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

            if (!uiDocument.gameObject.activeSelf)
            {
                uiDocument.gameObject.SetActive(true);
            }

            uiDocument.enabled = true;
            uiDocument.sortingOrder = Mathf.Max(uiDocument.sortingOrder, 710);

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

            overlay = new VisualElement { name = "LabPuzzleRoot" };
            overlay.style.flexGrow = 1f;
            overlay.style.flexDirection = FlexDirection.Column;
            overlay.style.alignItems = Align.FlexEnd;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.paddingRight = 28f;
            overlay.style.paddingLeft = 12f;
            overlay.style.paddingTop = 12f;
            overlay.style.paddingBottom = 12f;
            overlay.style.backgroundColor = new Color(0.01f, 0.04f, 0.08f, 0.52f);
            overlay.style.display = DisplayStyle.None;
            overlay.pickingMode = PickingMode.Position;

            missionBackdrop = new VisualElement { name = "lab-puzzle-hud-dim" };
            missionBackdrop.style.position = Position.Absolute;
            missionBackdrop.style.left = 18f;
            missionBackdrop.style.top = 14f;
            missionBackdrop.style.width = 242f;
            missionBackdrop.style.height = 108f;
            missionBackdrop.style.backgroundColor = new Color(0.03f, 0.07f, 0.12f, 0.2f);
            missionBackdrop.style.borderTopLeftRadius = 16f;
            missionBackdrop.style.borderTopRightRadius = 16f;
            missionBackdrop.style.borderBottomLeftRadius = 16f;
            missionBackdrop.style.borderBottomRightRadius = 16f;
            overlay.Add(missionBackdrop);

            panel = new VisualElement { name = "lab-flow-panel" };
            panel.style.width = new Length(62f, LengthUnit.Percent);
            panel.style.maxWidth = 980f;
            panel.style.minWidth = 740f;
            panel.style.paddingLeft = 22f;
            panel.style.paddingRight = 22f;
            panel.style.paddingTop = 18f;
            panel.style.paddingBottom = 14f;
            panel.style.backgroundColor = new Color(0.04f, 0.08f, 0.13f, 0.9f);
            panel.style.borderTopLeftRadius = 22f;
            panel.style.borderTopRightRadius = 22f;
            panel.style.borderBottomLeftRadius = 22f;
            panel.style.borderBottomRightRadius = 22f;
            panel.style.borderLeftWidth = 3f;
            panel.style.borderRightWidth = 3f;
            panel.style.borderTopWidth = 3f;
            panel.style.borderBottomWidth = 3f;
            panel.style.borderLeftColor = new Color(0.91f, 0.93f, 0.98f, 1f);
            panel.style.borderRightColor = new Color(0.91f, 0.93f, 0.98f, 1f);
            panel.style.borderTopColor = new Color(0.91f, 0.93f, 0.98f, 1f);
            panel.style.borderBottomColor = new Color(0.91f, 0.93f, 0.98f, 1f);
            overlay.Add(panel);

            panelGlow = new VisualElement { name = "lab-flow-panel-glow" };
            panelGlow.style.position = Position.Absolute;
            panelGlow.style.left = 12f;
            panelGlow.style.right = 12f;
            panelGlow.style.top = 12f;
            panelGlow.style.bottom = 12f;
            panelGlow.style.backgroundColor = new Color(0.2f, 0.8f, 1f, 0.06f);
            panelGlow.style.borderTopLeftRadius = 16f;
            panelGlow.style.borderTopRightRadius = 16f;
            panelGlow.style.borderBottomLeftRadius = 16f;
            panelGlow.style.borderBottomRightRadius = 16f;
            panel.Add(panelGlow);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.FlexStart;
            panel.Add(header);

            var headingGroup = new VisualElement();
            headingGroup.style.flexGrow = 1f;
            header.Add(headingGroup);

            titleLabel = new Label("Direct the flow") { name = "lab-flow-title" };
            titleLabel.style.fontSize = 32f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = Color.white;
            titleLabel.style.marginBottom = 2f;
            headingGroup.Add(titleLabel);

            subtitleLabel = new Label("Tap a source, then tap touching cells until the matching receiver lights up.");
            subtitleLabel.style.marginTop = 6f;
            subtitleLabel.style.fontSize = 15f;
            subtitleLabel.style.color = new Color(0.79f, 0.88f, 0.96f, 0.96f);
            subtitleLabel.style.whiteSpace = WhiteSpace.Normal;
            subtitleLabel.style.maxWidth = 520f;
            headingGroup.Add(subtitleLabel);

            statusLabel = new Label("Start with a source node, then trace a path across the board.");
            statusLabel.style.minWidth = 240f;
            statusLabel.style.marginLeft = 18f;
            statusLabel.style.paddingLeft = 14f;
            statusLabel.style.paddingRight = 14f;
            statusLabel.style.paddingTop = 10f;
            statusLabel.style.paddingBottom = 10f;
            statusLabel.style.backgroundColor = new Color(0.17f, 0.24f, 0.37f, 1f);
            statusLabel.style.borderTopLeftRadius = 14f;
            statusLabel.style.borderTopRightRadius = 14f;
            statusLabel.style.borderBottomLeftRadius = 14f;
            statusLabel.style.borderBottomRightRadius = 14f;
            statusLabel.style.borderLeftWidth = 3f;
            statusLabel.style.borderRightWidth = 3f;
            statusLabel.style.borderTopWidth = 3f;
            statusLabel.style.borderBottomWidth = 3f;
            statusLabel.style.borderLeftColor = new Color(1f, 1f, 1f, 1f);
            statusLabel.style.borderRightColor = new Color(1f, 1f, 1f, 1f);
            statusLabel.style.borderTopColor = new Color(1f, 1f, 1f, 1f);
            statusLabel.style.borderBottomColor = new Color(1f, 1f, 1f, 1f);
            statusLabel.style.color = new Color(0.9f, 0.96f, 1f, 1f);
            statusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            header.Add(statusLabel);

            var contentRow = new VisualElement();
            contentRow.style.flexDirection = FlexDirection.Row;
            contentRow.style.marginTop = 16f;
            contentRow.style.justifyContent = Justify.SpaceBetween;
            panel.Add(contentRow);

            var boardShell = new VisualElement();
            boardShell.style.flexGrow = 1f;
            boardShell.style.marginRight = 16f;
            boardShell.style.paddingLeft = 12f;
            boardShell.style.paddingRight = 12f;
            boardShell.style.paddingTop = 12f;
            boardShell.style.paddingBottom = 12f;
            boardShell.style.backgroundColor = new Color(0.08f, 0.13f, 0.2f, 1f);
            boardShell.style.borderTopLeftRadius = 18f;
            boardShell.style.borderTopRightRadius = 18f;
            boardShell.style.borderBottomLeftRadius = 18f;
            boardShell.style.borderBottomRightRadius = 18f;
            boardShell.style.borderLeftWidth = 3f;
            boardShell.style.borderRightWidth = 3f;
            boardShell.style.borderTopWidth = 3f;
            boardShell.style.borderBottomWidth = 3f;
            boardShell.style.borderLeftColor = new Color(1f, 1f, 1f, 1f);
            boardShell.style.borderRightColor = new Color(1f, 1f, 1f, 1f);
            boardShell.style.borderTopColor = new Color(1f, 1f, 1f, 1f);
            boardShell.style.borderBottomColor = new Color(1f, 1f, 1f, 1f);
            contentRow.Add(boardShell);

            var boardGrid = new VisualElement { name = "lab-flow-grid" };
            boardGrid.style.flexDirection = FlexDirection.Column;
            boardGrid.style.alignItems = Align.Center;
            boardShell.Add(boardGrid);

            cellButtons = new Button[GridSize, GridSize];
            for (var y = 0; y < GridSize; y++)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.Center;
                row.style.marginBottom = y == GridSize - 1 ? 0f : 10f;
                boardGrid.Add(row);

                for (var x = 0; x < GridSize; x++)
                {
                    var localX = x;
                    var localY = y;
                    var cell = new Button(() => HandleCellPressed(localX, localY));
                    cell.style.width = 96f;
                    cell.style.height = 96f;
                    cell.style.marginRight = x == GridSize - 1 ? 0f : 8f;
                    cell.style.borderTopLeftRadius = 18f;
                    cell.style.borderTopRightRadius = 18f;
                    cell.style.borderBottomLeftRadius = 18f;
                    cell.style.borderBottomRightRadius = 18f;
                    cell.style.borderLeftWidth = 4f;
                    cell.style.borderRightWidth = 4f;
                    cell.style.borderTopWidth = 4f;
                    cell.style.borderBottomWidth = 4f;
                    cell.style.unityFontStyleAndWeight = FontStyle.Bold;
                    cell.style.fontSize = 13f;
                    cell.style.whiteSpace = WhiteSpace.Normal;
                    cell.style.unityTextAlign = TextAnchor.MiddleCenter;
                    cell.style.unityTextOutlineWidth = 0f;
                    cellButtons[x, y] = cell;
                    row.Add(cell);
                }
            }

            var sidePanel = new VisualElement();
            sidePanel.style.width = 236f;
            sidePanel.style.flexShrink = 0f;
            contentRow.Add(sidePanel);

            var legendCard = new VisualElement();
            legendCard.style.paddingLeft = 16f;
            legendCard.style.paddingRight = 16f;
            legendCard.style.paddingTop = 16f;
            legendCard.style.paddingBottom = 16f;
            legendCard.style.backgroundColor = new Color(0.14f, 0.2f, 0.31f, 1f);
            legendCard.style.borderTopLeftRadius = 16f;
            legendCard.style.borderTopRightRadius = 16f;
            legendCard.style.borderBottomLeftRadius = 16f;
            legendCard.style.borderBottomRightRadius = 16f;
            legendCard.style.borderLeftWidth = 4f;
            legendCard.style.borderRightWidth = 4f;
            legendCard.style.borderTopWidth = 4f;
            legendCard.style.borderBottomWidth = 4f;
            legendCard.style.borderLeftColor = new Color(1f, 1f, 1f, 1f);
            legendCard.style.borderRightColor = new Color(1f, 1f, 1f, 1f);
            legendCard.style.borderTopColor = new Color(1f, 1f, 1f, 1f);
            legendCard.style.borderBottomColor = new Color(1f, 1f, 1f, 1f);
            sidePanel.Add(legendCard);

            var legendTitle = new Label("Flow channels");
            legendTitle.style.fontSize = 18f;
            legendTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            legendTitle.style.color = Color.white;
            legendCard.Add(legendTitle);

            legendSwatches = new VisualElement[FlowColors.Length];
            legendLabels = new Label[FlowColors.Length];
            for (var index = 0; index < FlowColors.Length; index++)
            {
                var legendRow = new VisualElement();
                legendRow.style.flexDirection = FlexDirection.Row;
                legendRow.style.alignItems = Align.Center;
                legendRow.style.marginTop = 14f;
                legendCard.Add(legendRow);

                var swatch = new VisualElement();
                swatch.style.width = 18f;
                swatch.style.height = 18f;
                swatch.style.borderTopLeftRadius = 9f;
                swatch.style.borderTopRightRadius = 9f;
                swatch.style.borderBottomLeftRadius = 9f;
                swatch.style.borderBottomRightRadius = 9f;
                legendSwatches[index] = swatch;
                legendRow.Add(swatch);

                var legendText = new Label();
                legendText.style.marginLeft = 10f;
                legendText.style.fontSize = 15f;
                legendText.style.color = new Color(0.87f, 0.93f, 0.98f, 1f);
                legendText.style.whiteSpace = WhiteSpace.Normal;
                legendLabels[index] = legendText;
                legendRow.Add(legendText);
            }

            var tipsCard = new VisualElement();
            tipsCard.style.marginTop = 16f;
            tipsCard.style.paddingLeft = 16f;
            tipsCard.style.paddingRight = 16f;
            tipsCard.style.paddingTop = 16f;
            tipsCard.style.paddingBottom = 16f;
            tipsCard.style.backgroundColor = new Color(0.17f, 0.14f, 0.27f, 1f);
            tipsCard.style.borderTopLeftRadius = 16f;
            tipsCard.style.borderTopRightRadius = 16f;
            tipsCard.style.borderBottomLeftRadius = 16f;
            tipsCard.style.borderBottomRightRadius = 16f;
            tipsCard.style.borderLeftWidth = 4f;
            tipsCard.style.borderRightWidth = 4f;
            tipsCard.style.borderTopWidth = 4f;
            tipsCard.style.borderBottomWidth = 4f;
            tipsCard.style.borderLeftColor = new Color(1f, 1f, 1f, 1f);
            tipsCard.style.borderRightColor = new Color(1f, 1f, 1f, 1f);
            tipsCard.style.borderTopColor = new Color(1f, 1f, 1f, 1f);
            tipsCard.style.borderBottomColor = new Color(1f, 1f, 1f, 1f);
            sidePanel.Add(tipsCard);

            hintLabel = new Label();
            hintLabel.style.fontSize = 18f;
            hintLabel.style.color = new Color(1f, 0.91f, 0.58f, 1f);
            hintLabel.style.whiteSpace = WhiteSpace.Normal;
            tipsCard.Add(hintLabel);

            var controlsRow = new VisualElement();
            controlsRow.style.marginTop = 16f;
            controlsRow.style.flexDirection = FlexDirection.Row;
            controlsRow.style.justifyContent = Justify.SpaceBetween;
            controlsRow.style.alignItems = Align.Center;
            controlsRow.style.flexWrap = Wrap.Wrap;
            panel.Add(controlsRow);

            resetButton = new Button(ResetPuzzleClicked) { text = "Reset board" };
            ApplyControlButtonStyle(resetButton, new Color(0.25f, 0.16f, 0.18f, 1f));
            controlsRow.Add(resetButton);

            var instructions = new Label("Select a glowing source, then tap touching cells until its matching receiver lights up.");
            instructions.style.flexGrow = 1f;
            instructions.style.marginLeft = 18f;
            instructions.style.marginRight = 18f;
            instructions.style.fontSize = 14f;
            instructions.style.color = new Color(0.79f, 0.88f, 0.96f, 0.92f);
            instructions.style.whiteSpace = WhiteSpace.Normal;
            instructions.style.maxWidth = 440f;
            controlsRow.Add(instructions);

            closeButton = new Button(CloseAfterSuccess) { text = "Continue" };
            ApplyControlButtonStyle(closeButton, new Color(0.12f, 0.28f, 0.22f, 1f));
            controlsRow.Add(closeButton);

            root.Add(overlay);
            built = true;
        }

        private static void ApplyControlButtonStyle(Button button, Color backgroundColor)
        {
            if (button == null)
            {
                return;
            }

            button.style.height = 50f;
            button.style.minWidth = 148f;
            button.style.paddingLeft = 18f;
            button.style.paddingRight = 18f;
            button.style.backgroundColor = backgroundColor;
            button.style.color = Color.white;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = 16f;
            button.style.borderTopLeftRadius = 14f;
            button.style.borderTopRightRadius = 14f;
            button.style.borderBottomLeftRadius = 14f;
            button.style.borderBottomRightRadius = 14f;
            button.style.borderLeftWidth = 3f;
            button.style.borderRightWidth = 3f;
            button.style.borderTopWidth = 3f;
            button.style.borderBottomWidth = 3f;
            button.style.borderLeftColor = new Color(1f, 1f, 1f, 1f);
            button.style.borderRightColor = new Color(1f, 1f, 1f, 1f);
            button.style.borderTopColor = new Color(1f, 1f, 1f, 1f);
            button.style.borderBottomColor = new Color(1f, 1f, 1f, 1f);
        }

        private UIDocument ResolveUiDocument()
        {
            var localDocument = uiDocument != null ? uiDocument : GetComponent<UIDocument>();
            if (localDocument == null)
            {
                localDocument = gameObject.AddComponent<UIDocument>();
            }

            EnsurePanelSettingsBound(localDocument);
            if (localDocument.panelSettings != null)
            {
                localDocument.sortingOrder = Mathf.Max(localDocument.sortingOrder, 710);
                uiDocument = localDocument;
                return localDocument;
            }

            return null;
        }

        private void EnsurePanelSettingsBound(UIDocument document)
        {
            if (document == null || document.panelSettings != null)
            {
                return;
            }

            if (panelSettings != null)
            {
                document.panelSettings = panelSettings;
                return;
            }

            if (uiDocument != null && uiDocument.panelSettings != null)
            {
                document.panelSettings = uiDocument.panelSettings;
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

        private void ResolveRuntimeReferences()
        {
            controlLock = controlLock != null ? controlLock : GetComponentInParent<ClassroomPlayerControlLock>();
            if (controlLock == null)
            {
                controlLock = FindFirstObjectByType<ClassroomPlayerControlLock>(FindObjectsInactive.Include);
            }

            interactionDirector = interactionDirector != null ? interactionDirector : FindFirstObjectByType<InteractionDirector>(FindObjectsInactive.Include);
            objectivePanelUi = objectivePanelUi != null ? objectivePanelUi : FindFirstObjectByType<LabObjectivePanelUi>(FindObjectsInactive.Include);
        }

        private void HandleCellPressed(int x, int y)
        {
            if (IsSolved)
            {
                return;
            }

            var index = ToIndex(x, y);
            var definition = BoardTemplate[index];
            if (definition.Type == CellType.Blocked)
            {
                statusLabel.text = "That tile is locked. Pick another route.";
                return;
            }

            if (definition.Type == CellType.Source)
            {
                BeginPath(definition.ColorIndex);
                statusLabel.text = $"Routing {FlowNames[definition.ColorIndex].ToLowerInvariant()}...";
                UpdateBoard();
                return;
            }

            if (!isDraggingPath || activeColorIndex < 0)
            {
                statusLabel.text = "Pick a glowing source node first.";
                return;
            }

            if (!IsNeighborToCurrentPath(x, y))
            {
                statusLabel.text = "Choose a touching tile next.";
                return;
            }

            if (definition.Type == CellType.Target)
            {
                if (definition.ColorIndex != activeColorIndex)
                {
                    statusLabel.text = "That receiver belongs to another color stream.";
                    return;
                }

                currentPaths[index] = activeColorIndex;
                isDraggingPath = false;
                activeColorIndex = -1;
                statusLabel.text = "Good. That receiver is online.";
                UpdateBoard();
                return;
            }

            if (currentPaths[index] >= 0 && currentPaths[index] != activeColorIndex)
            {
                statusLabel.text = "That path belongs to the other stream.";
                return;
            }

            currentPaths[index] = activeColorIndex;
            UpdateBoard();
        }

        private void BeginPath(int colorIndex)
        {
            activeColorIndex = colorIndex;
            isDraggingPath = true;
            ClearColorPath(colorIndex);
            var source = SourceCells[colorIndex];
            currentPaths[ToIndex(source.x, source.y)] = colorIndex;
            var target = TargetCells[colorIndex];
            currentPaths[ToIndex(target.x, target.y)] = colorIndex;
        }

        private void ClearColorPath(int colorIndex)
        {
            for (var index = 0; index < currentPaths.Length; index++)
            {
                if (BoardTemplate[index].Type == CellType.Empty && currentPaths[index] == colorIndex)
                {
                    currentPaths[index] = -1;
                }
            }
        }

        private bool IsNeighborToCurrentPath(int x, int y)
        {
            for (var index = 0; index < currentPaths.Length; index++)
            {
                if (currentPaths[index] != activeColorIndex)
                {
                    continue;
                }

                var otherX = index % GridSize;
                var otherY = index / GridSize;
                var distance = Mathf.Abs(otherX - x) + Mathf.Abs(otherY - y);
                if (distance == 1)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResetPuzzleClicked()
        {
            ResetPuzzleState();
            statusLabel.text = "Board cleared. Connect the blue and amber receivers.";
        }

        private void CloseAfterSuccess()
        {
            if (!IsSolved)
            {
                return;
            }

            Close();
        }

        private void UpdateBoard()
        {
            if (cellButtons == null || currentPaths == null)
            {
                return;
            }

            var connectedCount = 0;
            for (var y = 0; y < GridSize; y++)
            {
                for (var x = 0; x < GridSize; x++)
                {
                    var index = ToIndex(x, y);
                    var definition = BoardTemplate[index];
                    var button = cellButtons[x, y];
                    var colorIndex = currentPaths[index];
                    var hasFlow = colorIndex >= 0;

                    button.style.borderLeftColor = new Color(1f, 1f, 1f, 0.35f);
                    button.style.borderRightColor = new Color(1f, 1f, 1f, 0.35f);
                    button.style.borderTopColor = new Color(1f, 1f, 1f, 0.35f);
                    button.style.borderBottomColor = new Color(1f, 1f, 1f, 0.35f);

                    switch (definition.Type)
                    {
                        case CellType.Source:
                            button.text = definition.ColorIndex == 0 ? "BLUE\nSTART" : "AMBER\nSTART";
                            button.style.backgroundColor = FlowColors[definition.ColorIndex];
                            button.style.color = new Color(0.03f, 0.06f, 0.09f, 1f);
                            break;
                        case CellType.Target:
                            button.text = definition.ColorIndex == 0 ? "BLUE\nGOAL" : "AMBER\nGOAL";
                            button.style.backgroundColor = colorIndex == definition.ColorIndex && HasConnectedTarget(definition.ColorIndex)
                                ? FlowColors[definition.ColorIndex]
                                : FlowDimColors[definition.ColorIndex];
                            button.style.color = colorIndex == definition.ColorIndex && HasConnectedTarget(definition.ColorIndex)
                                ? new Color(0.03f, 0.06f, 0.09f, 1f)
                                : Color.white;
                            if (HasConnectedTarget(definition.ColorIndex))
                            {
                                connectedCount++;
                            }
                            break;
                        case CellType.Blocked:
                            button.text = "WALL";
                            button.style.backgroundColor = new Color(0.19f, 0.22f, 0.28f, 1f);
                            button.style.color = new Color(0.88f, 0.92f, 0.98f, 1f);
                            break;
                        default:
                            button.text = hasFlow ? "PATH" : string.Empty;
                            button.style.backgroundColor = hasFlow ? FlowColors[colorIndex] : new Color(0.11f, 0.15f, 0.22f, 1f);
                            button.style.color = new Color(0.03f, 0.06f, 0.09f, 1f);
                            break;
                    }
                }
            }

            IsSolved = connectedCount == FlowColors.Length;
            closeButton?.SetEnabled(IsSolved);

            for (var index = 0; index < FlowColors.Length; index++)
            {
                var connected = HasConnectedTarget(index);
                legendSwatches[index].style.backgroundColor = connected ? FlowColors[index] : FlowDimColors[index];
                legendLabels[index].text = connected
                    ? $"{FlowNames[index]} connected"
                    : $"{FlowNames[index]} waiting";
            }

            if (IsSolved)
            {
                titleLabel.text = "Flow stabilized";
                hintLabel.text = "Perfect. Both receivers are online and the shrink machine is ready.";
                statusLabel.text = "Shrink machine power restored.";
                if (!solvedRaised)
                {
                    solvedRaised = true;
                    solvedFlashRoutine = StartCoroutine(PlaySolvedFlashRoutine());
                    Solved?.Invoke();
                }
            }
            else
            {
                titleLabel.text = "Direct the flow";
                hintLabel.text = "Connect the blue start to the blue goal, then connect the amber start to the amber goal.";
            }
        }

        private bool HasConnectedTarget(int colorIndex)
        {
            var target = TargetCells[colorIndex];
            return currentPaths[ToIndex(target.x, target.y)] == colorIndex && HasAdjacentPath(target.x, target.y, colorIndex);
        }

        private bool HasAdjacentPath(int x, int y, int colorIndex)
        {
            for (var offsetIndex = 0; offsetIndex < 4; offsetIndex++)
            {
                var offset = offsetIndex switch
                {
                    0 => new Vector2Int(1, 0),
                    1 => new Vector2Int(-1, 0),
                    2 => new Vector2Int(0, 1),
                    _ => new Vector2Int(0, -1)
                };

                var nextX = x + offset.x;
                var nextY = y + offset.y;
                if (nextX < 0 || nextX >= GridSize || nextY < 0 || nextY >= GridSize)
                {
                    continue;
                }

                if (currentPaths[ToIndex(nextX, nextY)] == colorIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private static int ToIndex(int x, int y)
        {
            return (y * GridSize) + x;
        }

        private IEnumerator PlaySolvedFlashRoutine()
        {
            var elapsed = 0f;
            while (elapsed < solvedFlashSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / solvedFlashSeconds);
                var flash = Mathf.Sin(t * Mathf.PI);
                if (panelGlow != null)
                {
                    panelGlow.style.backgroundColor = new Color(0.32f, 0.9f, 1f, 0.08f + (flash * 0.2f));
                }

                for (var y = 0; y < GridSize; y++)
                {
                    for (var x = 0; x < GridSize; x++)
                    {
                        var button = cellButtons[x, y];
                        if (button == null)
                        {
                            continue;
                        }

                        button.style.scale = new Scale(Vector2.one * (1f + (flash * 0.03f)));
                    }
                }

                yield return null;
            }

            if (panelGlow != null)
            {
                panelGlow.style.backgroundColor = new Color(0.2f, 0.8f, 1f, 0.06f);
            }

            for (var y = 0; y < GridSize; y++)
            {
                for (var x = 0; x < GridSize; x++)
                {
                    var button = cellButtons[x, y];
                    if (button != null)
                    {
                        button.style.scale = new Scale(Vector2.one);
                    }
                }
            }

            solvedFlashRoutine = null;
        }
    }
}
