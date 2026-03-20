using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ClassroomBodyKnowledgeBookUi : MonoBehaviour
    {
        [SerializeField] private ClassroomPlayerControlLock controlLock;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PanelSettings panelSettings;
        [SerializeField] private bool closeOnEscape = true;
        [SerializeField] private string[] pageTitles =
        {
            "Digestive Route Primer",
            "Airway and Swallowing Safety",
            "Absorption Terrain"
        };

        [SerializeField, TextArea] private string[] pageBodies =
        {
            "The mini rocket enters through the mouth, is routed into the esophagus, survives the stomach chamber, and targets the first region of the small intestine where absorption is strongest.",
            "Swallowing and breathing share a corridor. The epiglottis helps close the airway route during swallowing so cargo is redirected to the esophagus instead of the trachea.",
            "In the small intestine, villi and microvilli massively increase surface area. At miniature scale, this region becomes a navigable biome where nutrient absorption is visible in motion."
        };

        [SerializeField] private string[] pageImageResourceKeys =
        {
            "Art/Story/ClassroomBook/digestive_route",
            "Art/Story/ClassroomBook/epiglottis_route",
            "Art/Story/ClassroomBook/villi_surface"
        };

        private VisualElement overlay;
        private VisualElement bookFrame;
        private Label leftTitle;
        private Label leftBody;
        private Label rightTitle;
        private Label rightBody;
        private VisualElement rightImage;
        private Label imageFallback;
        private Button previousButton;
        private Button nextButton;
        private Button closeButton;
        private int currentPageIndex;
        private bool built;

        public event Action Closed;
        public bool IsOpen { get; private set; }

        private void Awake()
        {
            EnsureBuilt();
            HideImmediate();
        }

        private void Update()
        {
            if (!IsOpen || !closeOnEscape)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
        }

        public void Open()
        {
            EnsureBuilt();
            if (IsOpen)
            {
                return;
            }

            currentPageIndex = Mathf.Clamp(currentPageIndex, 0, Mathf.Max(0, pageTitles.Length - 1));
            ApplyPage();
            overlay.style.display = DisplayStyle.Flex;
            IsOpen = true;
            controlLock?.Acquire(unlockCursor: true);
        }

        public IEnumerator OpenAndWait()
        {
            Open();
            while (IsOpen)
            {
                yield return null;
            }
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
            Closed?.Invoke();
        }

        public void HideImmediate()
        {
            EnsureBuilt();
            overlay.style.display = DisplayStyle.None;
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
                return;
            }

            if (uiDocument.panelSettings == null)
            {
                if (panelSettings != null)
                {
                    uiDocument.panelSettings = panelSettings;
                }
                else
                {
                    var anyDocument = FindFirstObjectByType<UIDocument>(FindObjectsInactive.Include);
                    if (anyDocument != null && anyDocument != uiDocument && anyDocument.panelSettings != null)
                    {
                        uiDocument.panelSettings = anyDocument.panelSettings;
                    }
                    else
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

            overlay = new VisualElement
            {
                name = "classroom-book-overlay"
            };

            overlay.style.flexGrow = 1f;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.75f);
            overlay.style.display = DisplayStyle.None;

            bookFrame = new VisualElement
            {
                name = "classroom-book-frame"
            };

            bookFrame.style.width = new Length(78f, LengthUnit.Percent);
            bookFrame.style.maxWidth = 1280f;
            bookFrame.style.height = new Length(70f, LengthUnit.Percent);
            bookFrame.style.maxHeight = 760f;
            bookFrame.style.flexDirection = FlexDirection.Row;
            bookFrame.style.paddingLeft = 24f;
            bookFrame.style.paddingRight = 24f;
            bookFrame.style.paddingTop = 18f;
            bookFrame.style.paddingBottom = 18f;
            bookFrame.style.backgroundColor = new Color(0.93f, 0.86f, 0.72f, 0.98f);
            bookFrame.style.borderTopLeftRadius = 12f;
            bookFrame.style.borderTopRightRadius = 12f;
            bookFrame.style.borderBottomLeftRadius = 12f;
            bookFrame.style.borderBottomRightRadius = 12f;

            var leftPage = CreatePage("left-page");
            var rightPage = CreatePage("right-page");

            leftTitle = CreateTitleLabel("left-title");
            leftBody = CreateBodyLabel("left-body");
            rightTitle = CreateTitleLabel("right-title");
            rightBody = CreateBodyLabel("right-body");

            rightImage = new VisualElement { name = "right-image" };
            rightImage.style.height = 210f;
            rightImage.style.marginTop = 12f;
            rightImage.style.marginBottom = 6f;
            rightImage.style.borderTopLeftRadius = 8f;
            rightImage.style.borderTopRightRadius = 8f;
            rightImage.style.borderBottomLeftRadius = 8f;
            rightImage.style.borderBottomRightRadius = 8f;
            rightImage.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            rightImage.style.backgroundColor = new Color(0.79f, 0.72f, 0.58f, 1f);

            imageFallback = new Label("Image unavailable")
            {
                name = "image-fallback"
            };
            imageFallback.style.unityTextAlign = TextAnchor.MiddleCenter;
            imageFallback.style.color = new Color(0.35f, 0.25f, 0.16f, 0.9f);
            imageFallback.style.fontSize = 18f;
            imageFallback.style.paddingTop = 90f;
            rightImage.Add(imageFallback);

            leftPage.Add(leftTitle);
            leftPage.Add(leftBody);
            rightPage.Add(rightTitle);
            rightPage.Add(rightImage);
            rightPage.Add(rightBody);

            bookFrame.Add(leftPage);
            bookFrame.Add(CreateSpineDivider());
            bookFrame.Add(rightPage);

            var footer = new VisualElement
            {
                name = "book-footer"
            };

            footer.style.position = Position.Absolute;
            footer.style.left = 0f;
            footer.style.right = 0f;
            footer.style.bottom = 22f;
            footer.style.height = 38f;
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.SpaceBetween;
            footer.style.alignItems = Align.Center;
            footer.style.paddingLeft = 18f;
            footer.style.paddingRight = 18f;

            previousButton = CreateButton("<<", ShowPreviousPage);
            nextButton = CreateButton(">>", ShowNextPage);
            closeButton = CreateButton("Close", Close);

            footer.Add(previousButton);
            footer.Add(closeButton);
            footer.Add(nextButton);

            overlay.Add(bookFrame);
            overlay.Add(footer);
            root.Add(overlay);

            controlLock = controlLock != null ? controlLock : GetComponentInParent<ClassroomPlayerControlLock>();
            built = true;
        }

        private void ApplyPage()
        {
            var maxPage = Mathf.Min(pageTitles.Length, pageBodies.Length) - 1;
            if (maxPage < 0)
            {
                return;
            }

            currentPageIndex = Mathf.Clamp(currentPageIndex, 0, maxPage);
            leftTitle.text = pageTitles[currentPageIndex];
            leftBody.text = pageBodies[currentPageIndex];

            var secondaryIndex = Mathf.Min(currentPageIndex + 1, maxPage);
            rightTitle.text = pageTitles[secondaryIndex];
            rightBody.text = pageBodies[secondaryIndex];

            var imageKey = secondaryIndex < pageImageResourceKeys.Length ? pageImageResourceKeys[secondaryIndex] : string.Empty;
            if (!string.IsNullOrWhiteSpace(imageKey))
            {
                var texture = Resources.Load<Texture2D>(imageKey);
                if (texture != null)
                {
                    rightImage.style.backgroundImage = new StyleBackground(texture);
                    imageFallback.style.display = DisplayStyle.None;
                }
                else
                {
                    rightImage.style.backgroundImage = StyleKeyword.None;
                    imageFallback.style.display = DisplayStyle.Flex;
                }
            }
            else
            {
                rightImage.style.backgroundImage = StyleKeyword.None;
                imageFallback.style.display = DisplayStyle.Flex;
            }

            previousButton.SetEnabled(currentPageIndex > 0);
            nextButton.SetEnabled(currentPageIndex < maxPage);
        }

        private void ShowPreviousPage()
        {
            currentPageIndex = Mathf.Max(0, currentPageIndex - 1);
            ApplyPage();
        }

        private void ShowNextPage()
        {
            currentPageIndex = Mathf.Min(pageTitles.Length - 1, currentPageIndex + 1);
            ApplyPage();
        }

        private static VisualElement CreatePage(string name)
        {
            var page = new VisualElement { name = name };
            page.style.flexGrow = 1f;
            page.style.paddingLeft = 18f;
            page.style.paddingRight = 18f;
            page.style.paddingTop = 14f;
            page.style.paddingBottom = 14f;
            page.style.backgroundColor = new Color(0.96f, 0.9f, 0.78f, 1f);
            page.style.borderTopLeftRadius = 8f;
            page.style.borderTopRightRadius = 8f;
            page.style.borderBottomLeftRadius = 8f;
            page.style.borderBottomRightRadius = 8f;
            return page;
        }

        private static Label CreateTitleLabel(string name)
        {
            var label = new Label
            {
                name = name
            };

            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 30f;
            label.style.color = new Color(0.25f, 0.17f, 0.09f, 1f);
            label.style.marginBottom = 12f;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            return label;
        }

        private static Label CreateBodyLabel(string name)
        {
            var label = new Label
            {
                name = name
            };

            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 22f;
            label.style.color = new Color(0.24f, 0.18f, 0.12f, 1f);
            label.style.flexGrow = 1f;
            return label;
        }

        private static VisualElement CreateSpineDivider()
        {
            var divider = new VisualElement { name = "book-spine" };
            divider.style.width = 12f;
            divider.style.marginLeft = 12f;
            divider.style.marginRight = 12f;
            divider.style.backgroundColor = new Color(0.74f, 0.64f, 0.5f, 1f);
            divider.style.borderTopLeftRadius = 4f;
            divider.style.borderTopRightRadius = 4f;
            divider.style.borderBottomLeftRadius = 4f;
            divider.style.borderBottomRightRadius = 4f;
            return divider;
        }

        private static Button CreateButton(string label, Action onClicked)
        {
            var button = new Button(() => onClicked?.Invoke())
            {
                text = label
            };

            button.style.width = 138f;
            button.style.height = 34f;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.fontSize = 18f;
            button.style.backgroundColor = new Color(0.18f, 0.27f, 0.39f, 0.93f);
            button.style.color = Color.white;
            return button;
        }
    }
}
