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
        private Label levelLabel;
        private Label hintLabel;
        private Label statusLabel;
        private Label attemptsLabel;
        private Button resetButton;
        private Button closeButton;
        private Button[,] cellButtons;
        private VisualElement[] legendSwatches;
        private Label[] legendLabels;
        private VisualElement boardEnergySweep;
        private VisualElement boardEnergySpark;
        private int[] currentPaths;
        private int activeColorIndex = -1;
        private int currentLevelIndex;
        private int movesThisLevel;
        private int failedAttempts;
        private bool isDraggingPath;
        private bool built;
        private bool solvedRaised;
        private bool capAssistUnlocked;
        private bool levelTransitionActive;
        private bool capAutoSolveActive;
        private Coroutine deferredOpenRoutine;
        private Coroutine solvedFlashRoutine;
        private Coroutine levelAdvanceRoutine;
        private Coroutine capAutoSolveRoutine;
        private LabObjectivePanelUi objectivePanelUi;

        public event Action Solved;
        public event Action<int, bool> AssistanceStateChanged;

        public bool IsOpen { get; private set; }
        public bool IsSolved { get; private set; }
        public int FailedAttempts => failedAttempts;
        public bool CanRequestCapSolve => capAssistUnlocked && !IsSolved;

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

        private sealed class PuzzleLevelDefinition
        {
            public readonly string Name;
            public readonly string Subtitle;
            public readonly int MoveBudget;
            public readonly CellDefinition[] Template;
            public readonly Vector2Int[] Sources;
            public readonly Vector2Int[] Targets;
            public readonly Vector2Int[][] CapSolutionPaths;

            public PuzzleLevelDefinition(
                string name,
                string subtitle,
                int moveBudget,
                Vector2Int[] sources,
                Vector2Int[] targets,
                Vector2Int[] blocked,
                Vector2Int[][] capSolutionPaths)
            {
                Name = name;
                Subtitle = subtitle;
                MoveBudget = Mathf.Max(4, moveBudget);
                Sources = ClonePoints(sources);
                Targets = ClonePoints(targets);
                CapSolutionPaths = ClonePaths(capSolutionPaths);
                Template = BuildTemplate(Sources, Targets, blocked);
            }

            private static Vector2Int[] ClonePoints(Vector2Int[] points)
            {
                if (points == null)
                {
                    return new Vector2Int[0];
                }

                var clone = new Vector2Int[points.Length];
                Array.Copy(points, clone, points.Length);
                return clone;
            }

            private static Vector2Int[][] ClonePaths(Vector2Int[][] paths)
            {
                if (paths == null)
                {
                    return new Vector2Int[0][];
                }

                var clone = new Vector2Int[paths.Length][];
                for (var index = 0; index < paths.Length; index++)
                {
                    clone[index] = ClonePoints(paths[index]);
                }

                return clone;
            }
        }

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

        private static readonly PuzzleLevelDefinition[] Levels =
        {
            new PuzzleLevelDefinition(
                "Easy",
                "Warm-up: power both channels.",
                moveBudget: 14,
                sources: new[] { new Vector2Int(0, 0), new Vector2Int(3, 0) },
                targets: new[] { new Vector2Int(0, 3), new Vector2Int(2, 3) },
                blocked: Array.Empty<Vector2Int>(),
                capSolutionPaths: new[]
                {
                    new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(0, 3) },
                    new[] { new Vector2Int(3, 0), new Vector2Int(3, 1), new Vector2Int(3, 2), new Vector2Int(3, 3), new Vector2Int(2, 3) }
                }),
            new PuzzleLevelDefinition(
                "Medium",
                "Route around blocked conduits.",
                moveBudget: 11,
                sources: new[] { new Vector2Int(0, 0), new Vector2Int(0, 3) },
                targets: new[] { new Vector2Int(3, 0), new Vector2Int(3, 3) },
                blocked: new[] { new Vector2Int(1, 0), new Vector2Int(2, 3) },
                capSolutionPaths: new[]
                {
                    new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(3, 1), new Vector2Int(3, 0) },
                    new[] { new Vector2Int(0, 3), new Vector2Int(0, 2), new Vector2Int(1, 2), new Vector2Int(2, 2), new Vector2Int(3, 2), new Vector2Int(3, 3) }
                }),
            new PuzzleLevelDefinition(
                "Hard",
                "Tight lanes. Plan each move.",
                moveBudget: 9,
                sources: new[] { new Vector2Int(0, 0), new Vector2Int(3, 0) },
                targets: new[] { new Vector2Int(1, 3), new Vector2Int(2, 3) },
                blocked: new[] { new Vector2Int(1, 1), new Vector2Int(2, 1) },
                capSolutionPaths: new[]
                {
                    new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(1, 2), new Vector2Int(1, 3) },
                    new[] { new Vector2Int(3, 0), new Vector2Int(3, 1), new Vector2Int(3, 2), new Vector2Int(2, 2), new Vector2Int(2, 3) }
                })
        };

        private void Awake()
        {
            StartNewPuzzleRun(resetFailures: true);
            EnsureBuilt();
            HideImmediate();
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            AnimatePuzzleVfx(Time.unscaledTime);
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
            StartNewPuzzleRun(resetFailures: true);
            UpdateBoard();
        }

        public bool TrySolveByCap()
        {
            if (IsSolved)
            {
                return true;
            }

            if (!CanRequestCapSolve)
            {
                if (statusLabel != null)
                {
                    statusLabel.text = "CAP unlocks auto-routing after two failed attempts.";
                }

                if (hintLabel != null)
                {
                    hintLabel.text = "Try again twice, then ask CAP in free chat to solve the flow.";
                }

                UpdateAttemptLabel();
                return false;
            }

            if (capAutoSolveRoutine != null)
            {
                StopCoroutine(capAutoSolveRoutine);
            }

            capAutoSolveRoutine = StartCoroutine(PlayCapAutoSolveRoutine());
            return true;
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

            EnsureLevelStatePrepared();
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
            UpdateBoard();
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

            levelLabel = new Label("LEVEL 1 · EASY");
            levelLabel.style.fontSize = 13f;
            levelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            levelLabel.style.color = new Color(0.73f, 0.9f, 1f, 1f);
            levelLabel.style.marginBottom = 4f;
            headingGroup.Add(levelLabel);

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
            boardShell.style.overflow = Overflow.Hidden;
            contentRow.Add(boardShell);

            boardEnergySweep = new VisualElement { name = "lab-flow-board-sweep" };
            boardEnergySweep.style.position = Position.Absolute;
            boardEnergySweep.style.width = 340f;
            boardEnergySweep.style.height = 640f;
            boardEnergySweep.style.left = -360f;
            boardEnergySweep.style.top = -140f;
            boardEnergySweep.style.backgroundColor = new Color(0.35f, 0.84f, 1f, 0.08f);
            boardEnergySweep.style.rotate = new Rotate(-18f);
            boardEnergySweep.style.borderTopLeftRadius = 100f;
            boardEnergySweep.style.borderTopRightRadius = 100f;
            boardEnergySweep.style.borderBottomLeftRadius = 100f;
            boardEnergySweep.style.borderBottomRightRadius = 100f;
            boardEnergySweep.pickingMode = PickingMode.Ignore;
            boardShell.Add(boardEnergySweep);

            boardEnergySpark = new VisualElement { name = "lab-flow-board-spark" };
            boardEnergySpark.style.position = Position.Absolute;
            boardEnergySpark.style.width = 88f;
            boardEnergySpark.style.height = 88f;
            boardEnergySpark.style.left = -20f;
            boardEnergySpark.style.top = 280f;
            boardEnergySpark.style.backgroundColor = new Color(1f, 0.91f, 0.34f, 0.12f);
            boardEnergySpark.style.borderTopLeftRadius = 44f;
            boardEnergySpark.style.borderTopRightRadius = 44f;
            boardEnergySpark.style.borderBottomLeftRadius = 44f;
            boardEnergySpark.style.borderBottomRightRadius = 44f;
            boardEnergySpark.pickingMode = PickingMode.Ignore;
            boardShell.Add(boardEnergySpark);

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

            attemptsLabel = new Label();
            attemptsLabel.style.marginTop = 12f;
            attemptsLabel.style.fontSize = 13f;
            attemptsLabel.style.color = new Color(0.8f, 0.91f, 1f, 0.95f);
            attemptsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            attemptsLabel.style.whiteSpace = WhiteSpace.Normal;
            tipsCard.Add(attemptsLabel);

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
            if (IsSolved || levelTransitionActive || capAutoSolveActive)
            {
                return;
            }

            EnsureLevelStatePrepared();
            var index = ToIndex(x, y);
            var definition = CurrentLevel.Template[index];
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

                if (currentPaths[index] != activeColorIndex)
                {
                    currentPaths[index] = activeColorIndex;
                    if (RegisterMoveAndCheckFailure())
                    {
                        return;
                    }
                }

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

            if (currentPaths[index] != activeColorIndex)
            {
                currentPaths[index] = activeColorIndex;
                if (RegisterMoveAndCheckFailure())
                {
                    return;
                }
            }

            UpdateBoard();
        }

        private void BeginPath(int colorIndex)
        {
            activeColorIndex = colorIndex;
            isDraggingPath = true;
            ClearColorPath(colorIndex);
            var source = CurrentLevel.Sources[colorIndex];
            currentPaths[ToIndex(source.x, source.y)] = colorIndex;
            var target = CurrentLevel.Targets[colorIndex];
            currentPaths[ToIndex(target.x, target.y)] = colorIndex;
        }

        private void ClearColorPath(int colorIndex)
        {
            for (var index = 0; index < currentPaths.Length; index++)
            {
                if (CurrentLevel.Template[index].Type == CellType.Empty && currentPaths[index] == colorIndex)
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
            if (!IsSolved && movesThisLevel > 0 && !levelTransitionActive)
            {
                RegisterFailedAttempt("Attempt reset before completion.");
            }

            ResetCurrentLevelState();
            statusLabel.text = $"Board reset. {CurrentLevel.Name} routing started.";
            UpdateBoard();
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

            var flowPulse = 0.75f + (Mathf.Sin(Time.unscaledTime * pulseSpeed) * 0.25f);
            var connectedCount = 0;
            for (var y = 0; y < GridSize; y++)
            {
                for (var x = 0; x < GridSize; x++)
                {
                    var index = ToIndex(x, y);
                    var definition = CurrentLevel.Template[index];
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
                            button.style.scale = new Scale(Vector2.one * (1f + (flowPulse * 0.015f)));
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

                            button.style.scale = new Scale(Vector2.one * (HasConnectedTarget(definition.ColorIndex) ? 1.06f : 1f));
                            break;
                        case CellType.Blocked:
                            button.text = "WALL";
                            button.style.backgroundColor = new Color(0.19f, 0.22f, 0.28f, 1f);
                            button.style.color = new Color(0.88f, 0.92f, 0.98f, 1f);
                            button.style.scale = new Scale(Vector2.one);
                            break;
                        default:
                            button.text = hasFlow ? "PATH" : string.Empty;
                            button.style.backgroundColor = hasFlow ? FlowColors[colorIndex] : new Color(0.11f, 0.15f, 0.22f, 1f);
                            button.style.color = new Color(0.03f, 0.06f, 0.09f, 1f);
                            button.style.scale = new Scale(Vector2.one * (hasFlow ? 1f + (flowPulse * 0.012f) : 1f));
                            break;
                    }
                }
            }

            var currentLevelSolved = connectedCount == FlowColors.Length;
            closeButton?.SetEnabled(IsSolved);

            for (var index = 0; index < FlowColors.Length; index++)
            {
                var connected = HasConnectedTarget(index);
                legendSwatches[index].style.backgroundColor = connected ? FlowColors[index] : FlowDimColors[index];
                legendLabels[index].text = connected
                    ? $"{FlowNames[index]} connected"
                    : $"{FlowNames[index]} waiting";
            }

            levelLabel.text = $"LEVEL {Mathf.Clamp(currentLevelIndex + 1, 1, Levels.Length)} · {CurrentLevel.Name.ToUpperInvariant()}";

            if (currentLevelSolved && currentLevelIndex < Levels.Length - 1)
            {
                titleLabel.text = $"{CurrentLevel.Name} cleared";
                subtitleLabel.text = "Channel lock confirmed. Preparing the next puzzle stage...";
                hintLabel.text = "Great routing. CAP is loading the next difficulty.";
                statusLabel.text = $"Move budget used: {movesThisLevel}/{CurrentLevel.MoveBudget}";
                if (levelAdvanceRoutine == null)
                {
                    levelAdvanceRoutine = StartCoroutine(AdvanceToNextLevelRoutine());
                }

                UpdateAttemptLabel();
                return;
            }

            IsSolved = currentLevelSolved && currentLevelIndex >= Levels.Length - 1;
            closeButton?.SetEnabled(IsSolved);

            if (IsSolved)
            {
                titleLabel.text = "Flow stabilized";
                hintLabel.text = "Perfect. Both receivers are online and the shrink machine is ready.";
                statusLabel.text = "Shrink machine power restored.";
                subtitleLabel.text = "All puzzle stages complete.";
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
                subtitleLabel.text = CurrentLevel.Subtitle;
                hintLabel.text = capAssistUnlocked
                    ? "Stuck? Ask CAP in free chat: \"CAP, solve the puzzle for me.\""
                    : "Connect both channels. Use Reset if you need a clean route.";
                statusLabel.text = $"Moves: {movesThisLevel}/{CurrentLevel.MoveBudget}";
            }

            UpdateAttemptLabel();
        }

        private bool HasConnectedTarget(int colorIndex)
        {
            var target = CurrentLevel.Targets[colorIndex];
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

        private PuzzleLevelDefinition CurrentLevel => Levels[Mathf.Clamp(currentLevelIndex, 0, Levels.Length - 1)];

        private static CellDefinition[] BuildTemplate(Vector2Int[] sources, Vector2Int[] targets, Vector2Int[] blocked)
        {
            var template = new CellDefinition[GridSize * GridSize];
            for (var index = 0; index < template.Length; index++)
            {
                template[index] = new CellDefinition(CellType.Empty, -1);
            }

            if (blocked != null)
            {
                for (var index = 0; index < blocked.Length; index++)
                {
                    if (!IsInBounds(blocked[index]))
                    {
                        continue;
                    }

                    template[ToIndex(blocked[index].x, blocked[index].y)] = new CellDefinition(CellType.Blocked, -1);
                }
            }

            for (var colorIndex = 0; colorIndex < FlowColors.Length; colorIndex++)
            {
                if (sources == null || colorIndex >= sources.Length || !IsInBounds(sources[colorIndex]))
                {
                    continue;
                }

                template[ToIndex(sources[colorIndex].x, sources[colorIndex].y)] = new CellDefinition(CellType.Source, colorIndex);
            }

            for (var colorIndex = 0; colorIndex < FlowColors.Length; colorIndex++)
            {
                if (targets == null || colorIndex >= targets.Length || !IsInBounds(targets[colorIndex]))
                {
                    continue;
                }

                template[ToIndex(targets[colorIndex].x, targets[colorIndex].y)] = new CellDefinition(CellType.Target, colorIndex);
            }

            return template;
        }

        private static bool IsInBounds(Vector2Int point)
        {
            return point.x >= 0 && point.x < GridSize && point.y >= 0 && point.y < GridSize;
        }

        private void EnsureLevelStatePrepared()
        {
            if (Levels.Length == 0)
            {
                return;
            }

            if (currentPaths == null || currentPaths.Length != GridSize * GridSize)
            {
                ResetCurrentLevelState();
            }
        }

        private void StartNewPuzzleRun(bool resetFailures)
        {
            currentLevelIndex = 0;
            IsSolved = false;
            solvedRaised = false;
            activeColorIndex = -1;
            isDraggingPath = false;
            levelTransitionActive = false;
            capAutoSolveActive = false;

            if (levelAdvanceRoutine != null)
            {
                StopCoroutine(levelAdvanceRoutine);
                levelAdvanceRoutine = null;
            }

            if (capAutoSolveRoutine != null)
            {
                StopCoroutine(capAutoSolveRoutine);
                capAutoSolveRoutine = null;
            }

            if (resetFailures)
            {
                failedAttempts = 0;
                SetCapAssistUnlocked(false);
            }

            ResetCurrentLevelState();
        }

        private void ResetCurrentLevelState()
        {
            var level = CurrentLevel;
            currentPaths = new int[level.Template.Length];
            for (var index = 0; index < currentPaths.Length; index++)
            {
                currentPaths[index] = level.Template[index].Type == CellType.Source || level.Template[index].Type == CellType.Target
                    ? level.Template[index].ColorIndex
                    : -1;
            }

            activeColorIndex = -1;
            isDraggingPath = false;
            movesThisLevel = 0;
        }

        private bool RegisterMoveAndCheckFailure()
        {
            movesThisLevel++;
            if (movesThisLevel <= CurrentLevel.MoveBudget)
            {
                return false;
            }

            RegisterFailedAttempt("Move budget exceeded.");
            statusLabel.text = $"Routing overload. {CurrentLevel.Name} restarted.";
            ResetCurrentLevelState();
            UpdateBoard();
            return true;
        }

        private void RegisterFailedAttempt(string reason)
        {
            failedAttempts = Mathf.Max(0, failedAttempts + 1);
            if (!capAssistUnlocked && failedAttempts >= 2)
            {
                SetCapAssistUnlocked(true);
            }

            if (statusLabel != null && !string.IsNullOrWhiteSpace(reason))
            {
                statusLabel.text = reason;
            }

            UpdateAttemptLabel();
        }

        private void SetCapAssistUnlocked(bool value)
        {
            if (capAssistUnlocked == value)
            {
                return;
            }

            capAssistUnlocked = value;
            AssistanceStateChanged?.Invoke(failedAttempts, capAssistUnlocked);
        }

        private void UpdateAttemptLabel()
        {
            if (attemptsLabel == null)
            {
                return;
            }

            if (capAssistUnlocked)
            {
                attemptsLabel.text = $"Attempts: {failedAttempts}\nCAP assist unlocked via LLM chat.";
                attemptsLabel.style.color = new Color(0.67f, 0.97f, 0.74f, 1f);
            }
            else
            {
                var needed = Mathf.Max(0, 2 - failedAttempts);
                attemptsLabel.text = $"Attempts: {failedAttempts}\nCAP assist unlocks in {needed} more failed {(needed == 1 ? "try" : "tries")}.";
                attemptsLabel.style.color = new Color(0.8f, 0.91f, 1f, 0.95f);
            }
        }

        private IEnumerator AdvanceToNextLevelRoutine()
        {
            levelTransitionActive = true;
            var pulseTime = 0f;
            while (pulseTime < 0.42f)
            {
                pulseTime += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(pulseTime / 0.42f);
                var flash = Mathf.Sin(t * Mathf.PI);
                if (panelGlow != null)
                {
                    panelGlow.style.backgroundColor = new Color(0.44f, 0.92f, 1f, 0.08f + (flash * 0.3f));
                }

                yield return null;
            }

            currentLevelIndex = Mathf.Clamp(currentLevelIndex + 1, 0, Levels.Length - 1);
            ResetCurrentLevelState();
            levelTransitionActive = false;
            levelAdvanceRoutine = null;
            UpdateBoard();
        }

        private IEnumerator PlayCapAutoSolveRoutine()
        {
            capAutoSolveActive = true;
            if (statusLabel != null)
            {
                statusLabel.text = "CAP is routing all flow channels...";
            }

            while (!IsSolved)
            {
                var level = CurrentLevel;
                for (var colorIndex = 0; colorIndex < level.CapSolutionPaths.Length; colorIndex++)
                {
                    var path = level.CapSolutionPaths[colorIndex];
                    if (path == null)
                    {
                        continue;
                    }

                    for (var pointIndex = 0; pointIndex < path.Length; pointIndex++)
                    {
                        var point = path[pointIndex];
                        if (!IsInBounds(point))
                        {
                            continue;
                        }

                        currentPaths[ToIndex(point.x, point.y)] = colorIndex;
                    }
                }

                movesThisLevel = Mathf.Min(level.MoveBudget, movesThisLevel + 1);

                if (currentLevelIndex < Levels.Length - 1)
                {
                    currentLevelIndex++;
                    ResetCurrentLevelState();
                    UpdateBoard();
                    yield return new WaitForSecondsRealtime(0.12f);
                }
                else
                {
                    UpdateBoard();
                    break;
                }
            }

            capAutoSolveActive = false;
            capAutoSolveRoutine = null;
        }

        private void AnimatePuzzleVfx(float clock)
        {
            if (panelGlow != null && !IsSolved)
            {
                var glowPulse = 0.04f + (Mathf.Sin(clock * (pulseSpeed * 0.72f)) * 0.03f);
                panelGlow.style.backgroundColor = new Color(0.2f, 0.8f, 1f, Mathf.Clamp(glowPulse, 0.02f, 0.12f));
            }

            if (boardEnergySweep != null)
            {
                var x = Mathf.Lerp(-360f, 520f, Mathf.PingPong(clock * 120f, 1f));
                boardEnergySweep.style.left = x;
            }

            if (boardEnergySpark != null)
            {
                var orbit = Mathf.PingPong(clock * 0.78f, 1f);
                boardEnergySpark.style.left = Mathf.Lerp(-18f, 320f, orbit);
                boardEnergySpark.style.top = Mathf.Lerp(286f, -26f, orbit);
                var alpha = 0.08f + Mathf.PingPong(clock * 0.64f, 0.12f);
                boardEnergySpark.style.backgroundColor = new Color(1f, 0.91f, 0.34f, alpha);
            }
        }
    }
}
