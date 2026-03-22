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
            "The mini rocket starts at the mouth, goes down the esophagus, passes the stomach, and aims for the small intestine where nutrient pickup is strongest.",
            "Breathing and swallowing share one hallway. During swallowing, the epiglottis helps cover the airway so the rocket goes to the esophagus, not the trachea.",
            "Inside the small intestine, villi and microvilli create lots of surface area. At tiny scale, this area feels like a landscape where absorption happens in front of you."
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
            ResolveRuntimeReferences();
            if (IsOpen)
            {
                return;
            }

            if (overlay == null)
            {
                return;
            }

            if (uiDocument != null)
            {
                EnsurePanelSettingsBound();
                uiDocument.sortingOrder = Mathf.Max(uiDocument.sortingOrder, 680);
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
            var wasOpen = IsOpen;
            EnsureBuilt();
            if (overlay != null)
            {
                overlay.style.display = DisplayStyle.None;
            }

            IsOpen = false;
            if (wasOpen)
            {
                controlLock?.Release();
            }
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

            if (uiDocument == null)
            {
                return;
            }

            EnsurePanelSettingsBound();
            uiDocument.sortingOrder = Mathf.Max(uiDocument.sortingOrder, 680);

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

            bookFrame.style.width = new Length(84f, LengthUnit.Percent);
            bookFrame.style.maxWidth = 1280f;
            bookFrame.style.height = new Length(74f, LengthUnit.Percent);
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

        private void ResolveRuntimeReferences()
        {
            if (controlLock != null)
            {
                return;
            }

            controlLock = GetComponentInParent<ClassroomPlayerControlLock>();
            if (controlLock == null)
            {
                controlLock = FindFirstObjectByType<ClassroomPlayerControlLock>();
            }
        }

        private void EnsurePanelSettingsBound()
        {
            if (uiDocument == null || uiDocument.panelSettings != null)
            {
                return;
            }

            if (panelSettings != null)
            {
                uiDocument.panelSettings = panelSettings;
                return;
            }

            var anyDocuments = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < anyDocuments.Length; index++)
            {
                var candidate = anyDocuments[index];
                if (candidate == null || candidate == uiDocument || candidate.panelSettings == null)
                {
                    continue;
                }

                uiDocument.panelSettings = candidate.panelSettings;
                return;
            }

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            uiDocument.panelSettings = panelSettings;
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

            var imageKey = currentPageIndex < pageImageResourceKeys.Length ? pageImageResourceKeys[currentPageIndex] : string.Empty;
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
            page.style.overflow = Overflow.Hidden;
            return page;
        }

        private static Label CreateTitleLabel(string name)
        {
            var label = new Label
            {
                name = name
            };

            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 20f;
            label.style.color = new Color(0.25f, 0.17f, 0.09f, 1f);
            label.style.marginBottom = 12f;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.height = 58f;
            label.style.flexShrink = 1f;
            label.style.overflow = Overflow.Hidden;
            return label;
        }

        private static Label CreateBodyLabel(string name)
        {
            var label = new Label
            {
                name = name
            };

            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 18f;
            label.style.color = new Color(0.24f, 0.18f, 0.12f, 1f);
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            label.style.overflow = Overflow.Hidden;
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
