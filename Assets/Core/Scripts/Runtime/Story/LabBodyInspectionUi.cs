using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class LabBodyInspectionUi : MonoBehaviour
    {
        [SerializeField] private ClassroomPlayerControlLock controlLock;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PanelSettings panelSettings;
        [SerializeField] private bool closeOnEscape = true;
        [SerializeField] private string[] pageTitles =
        {
            "Mouth Entry",
            "Esophagus Route",
            "Stomach and Intestines"
        };
        [SerializeField, TextArea] private string[] pageBodies =
        {
            "The journey starts at the mouth. This is where food first enters the digestive system.",
            "After swallowing, food moves through the esophagus. Think of it like a careful travel tube.",
            "Next comes the stomach, then the intestines, where food is broken down and helpful nutrients are collected."
        };

        private VisualElement overlay;
        private VisualElement panel;
        private Label sectionLabel;
        private Label progressLabel;
        private Label titleLabel;
        private Label subtitleLabel;
        private Label bodyLabel;
        private Button previousButton;
        private Button nextButton;
        private Button closeButton;
        private int currentPageIndex;
        private bool built;
        private bool completedThisOpen;

        public event Action Completed;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            EnsureBuilt();
            HideImmediate();
        }

        private void Update()
        {
            if (IsOpen && closeOnEscape && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
        }

        public void Open()
        {
            EnsureBuilt();
            ResolveRuntimeReferences();
            if (overlay == null)
            {
                return;
            }

            currentPageIndex = 0;
            completedThisOpen = false;
            ApplyPage();
            overlay.style.display = DisplayStyle.Flex;
            IsOpen = true;
            controlLock?.Acquire(unlockCursor: true);
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            overlay.style.display = DisplayStyle.None;
            IsOpen = false;
            controlLock?.Release();
        }

        public void HideImmediate()
        {
            EnsureBuilt();
            if (overlay != null)
            {
                overlay.style.display = DisplayStyle.None;
            }

            IsOpen = false;
        }

        private void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            uiDocument = uiDocument != null ? uiDocument : GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
            }

            if (uiDocument.panelSettings == null)
            {
                if (panelSettings != null)
                {
                    uiDocument.panelSettings = panelSettings;
                }
                else
                {
                    var documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    for (var index = 0; index < documents.Length; index++)
                    {
                        var candidate = documents[index];
                        if (candidate == null || candidate == uiDocument || candidate.panelSettings == null)
                        {
                            continue;
                        }

                        uiDocument.panelSettings = candidate.panelSettings;
                        break;
                    }

                    if (uiDocument.panelSettings == null)
                    {
                        panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                        uiDocument.panelSettings = panelSettings;
                    }
                }
            }

            var root = uiDocument.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1f;
            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.right = 0f;
            root.style.top = 0f;
            root.style.bottom = 0f;

            overlay = new VisualElement { name = "lab-body-inspection-overlay" };
            overlay.style.flexGrow = 1f;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.paddingLeft = 28f;
            overlay.style.paddingRight = 28f;
            overlay.style.paddingTop = 28f;
            overlay.style.paddingBottom = 28f;
            overlay.style.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 0.74f);
            overlay.style.display = DisplayStyle.None;

            var shadow = new VisualElement { name = "lab-body-inspection-shadow" };
            shadow.style.position = Position.Absolute;
            shadow.style.width = new Length(54f, LengthUnit.Percent);
            shadow.style.maxWidth = 930f;
            shadow.style.minWidth = 620f;
            shadow.style.height = 420f;
            shadow.style.translate = new Translate(10f, 10f, 0f);
            shadow.style.backgroundColor = new Color(0.04f, 0.06f, 0.09f, 0.48f);
            shadow.style.borderTopLeftRadius = 28f;
            shadow.style.borderTopRightRadius = 28f;
            shadow.style.borderBottomLeftRadius = 28f;
            shadow.style.borderBottomRightRadius = 28f;
            overlay.Add(shadow);

            panel = new VisualElement { name = "lab-body-inspection-panel" };
            panel.style.width = new Length(52f, LengthUnit.Percent);
            panel.style.maxWidth = 900f;
            panel.style.minWidth = 600f;
            panel.style.paddingLeft = 18f;
            panel.style.paddingRight = 18f;
            panel.style.paddingTop = 18f;
            panel.style.paddingBottom = 18f;
            panel.style.backgroundColor = new Color(0.97f, 0.94f, 0.89f, 0.98f);
            panel.style.borderTopLeftRadius = 26f;
            panel.style.borderTopRightRadius = 26f;
            panel.style.borderBottomLeftRadius = 26f;
            panel.style.borderBottomRightRadius = 26f;
            panel.style.borderLeftWidth = 4f;
            panel.style.borderRightWidth = 4f;
            panel.style.borderTopWidth = 4f;
            panel.style.borderBottomWidth = 4f;
            panel.style.borderLeftColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            panel.style.borderRightColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            panel.style.borderTopColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            panel.style.borderBottomColor = new Color(0.08f, 0.09f, 0.12f, 1f);

            var topAccent = new VisualElement { name = "lab-body-inspection-top-accent" };
            topAccent.style.height = 8f;
            topAccent.style.marginLeft = -18f;
            topAccent.style.marginRight = -18f;
            topAccent.style.marginTop = -18f;
            topAccent.style.marginBottom = 14f;
            topAccent.style.backgroundColor = new Color(0.17f, 0.66f, 1f, 1f);
            topAccent.style.borderTopLeftRadius = 22f;
            topAccent.style.borderTopRightRadius = 22f;
            panel.Add(topAccent);

            var headerRow = new VisualElement { name = "lab-body-inspection-header" };
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 12f;
            panel.Add(headerRow);

            var sectionPlate = new VisualElement { name = "lab-body-inspection-section-plate" };
            sectionPlate.style.backgroundColor = new Color(1f, 0.84f, 0.16f, 1f);
            sectionPlate.style.paddingLeft = 14f;
            sectionPlate.style.paddingRight = 14f;
            sectionPlate.style.paddingTop = 8f;
            sectionPlate.style.paddingBottom = 8f;
            sectionPlate.style.borderTopLeftRadius = 14f;
            sectionPlate.style.borderTopRightRadius = 14f;
            sectionPlate.style.borderBottomLeftRadius = 14f;
            sectionPlate.style.borderBottomRightRadius = 14f;
            headerRow.Add(sectionPlate);

            sectionLabel = new Label("BODY MAP");
            sectionLabel.style.fontSize = 18f;
            sectionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            sectionLabel.style.color = new Color(0.08f, 0.08f, 0.08f, 1f);
            sectionPlate.Add(sectionLabel);

            var progressPlate = new VisualElement { name = "lab-body-inspection-progress-plate" };
            progressPlate.style.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            progressPlate.style.paddingLeft = 14f;
            progressPlate.style.paddingRight = 14f;
            progressPlate.style.paddingTop = 8f;
            progressPlate.style.paddingBottom = 8f;
            progressPlate.style.borderTopLeftRadius = 14f;
            progressPlate.style.borderTopRightRadius = 14f;
            progressPlate.style.borderBottomLeftRadius = 14f;
            progressPlate.style.borderBottomRightRadius = 14f;
            headerRow.Add(progressPlate);

            progressLabel = new Label("1 / 3");
            progressLabel.style.fontSize = 17f;
            progressLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            progressLabel.style.color = Color.white;
            progressPlate.Add(progressLabel);

            titleLabel = new Label();
            titleLabel.style.fontSize = 34f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.08f, 0.09f, 0.12f, 1f);
            titleLabel.style.marginBottom = 6f;

            subtitleLabel = new Label("Trace the route step by step so CAP can guide the tiny mission.");
            subtitleLabel.style.fontSize = 18f;
            subtitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            subtitleLabel.style.color = new Color(0.25f, 0.31f, 0.36f, 1f);
            subtitleLabel.style.whiteSpace = WhiteSpace.Normal;
            subtitleLabel.style.marginBottom = 14f;

            var bodyCard = new VisualElement { name = "lab-body-inspection-body-card" };
            bodyCard.style.paddingLeft = 18f;
            bodyCard.style.paddingRight = 18f;
            bodyCard.style.paddingTop = 16f;
            bodyCard.style.paddingBottom = 16f;
            bodyCard.style.backgroundColor = new Color(1f, 0.97f, 0.88f, 0.96f);
            bodyCard.style.borderTopLeftRadius = 18f;
            bodyCard.style.borderTopRightRadius = 18f;
            bodyCard.style.borderBottomLeftRadius = 18f;
            bodyCard.style.borderBottomRightRadius = 18f;
            bodyCard.style.borderLeftWidth = 3f;
            bodyCard.style.borderRightWidth = 3f;
            bodyCard.style.borderTopWidth = 3f;
            bodyCard.style.borderBottomWidth = 3f;
            bodyCard.style.borderLeftColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            bodyCard.style.borderRightColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            bodyCard.style.borderTopColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            bodyCard.style.borderBottomColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            bodyCard.style.marginBottom = 18f;

            bodyLabel = new Label();
            bodyLabel.style.whiteSpace = WhiteSpace.Normal;
            bodyLabel.style.fontSize = 24f;
            bodyLabel.style.color = new Color(0.08f, 0.09f, 0.12f, 1f);
            bodyCard.Add(bodyLabel);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.SpaceBetween;
            footer.style.alignItems = Align.Center;

            previousButton = CreateButton("Back", ShowPreviousPage);
            nextButton = CreateButton("Next", ShowNextPage);
            closeButton = CreateButton("Close", Close);

            footer.Add(previousButton);
            footer.Add(closeButton);
            footer.Add(nextButton);

            panel.Add(titleLabel);
            panel.Add(subtitleLabel);
            panel.Add(bodyCard);
            panel.Add(footer);
            overlay.Add(panel);
            root.Add(overlay);
            built = true;
        }

        private void ResolveRuntimeReferences()
        {
            controlLock = controlLock != null ? controlLock : GetComponentInParent<ClassroomPlayerControlLock>();
            if (controlLock == null)
            {
                controlLock = FindFirstObjectByType<ClassroomPlayerControlLock>(FindObjectsInactive.Include);
            }
        }

        private void ShowPreviousPage()
        {
            currentPageIndex = Mathf.Max(0, currentPageIndex - 1);
            ApplyPage();
        }

        private void ShowNextPage()
        {
            if (currentPageIndex >= Mathf.Min(pageTitles.Length, pageBodies.Length) - 1)
            {
                completedThisOpen = true;
                Close();
                Completed?.Invoke();
                return;
            }

            currentPageIndex++;
            ApplyPage();
        }

        private void ApplyPage()
        {
            var maxPage = Mathf.Min(pageTitles.Length, pageBodies.Length) - 1;
            currentPageIndex = Mathf.Clamp(currentPageIndex, 0, Mathf.Max(0, maxPage));
            titleLabel.text = pageTitles[currentPageIndex];
            bodyLabel.text = pageBodies[currentPageIndex];
            progressLabel.text = $"{currentPageIndex + 1} / {maxPage + 1}";
            previousButton.SetEnabled(currentPageIndex > 0);
            nextButton.text = currentPageIndex >= maxPage ? "Finish Scan" : "Next Step";
        }

        private static Button CreateButton(string text, Action onClicked)
        {
            var button = new Button(onClicked) { text = text };
            button.style.height = 48f;
            button.style.minWidth = 146f;
            button.style.paddingLeft = 16f;
            button.style.paddingRight = 16f;
            button.style.backgroundColor = new Color(0.39f, 0.68f, 1f, 1f);
            button.style.color = new Color(0.08f, 0.09f, 0.12f, 1f);
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = 18f;
            button.style.borderLeftWidth = 3f;
            button.style.borderRightWidth = 3f;
            button.style.borderTopWidth = 3f;
            button.style.borderBottomWidth = 3f;
            button.style.borderLeftColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            button.style.borderRightColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            button.style.borderTopColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            button.style.borderBottomColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            button.style.borderTopLeftRadius = 14f;
            button.style.borderTopRightRadius = 14f;
            button.style.borderBottomLeftRadius = 14f;
            button.style.borderBottomRightRadius = 14f;
            return button;
        }
    }
}
