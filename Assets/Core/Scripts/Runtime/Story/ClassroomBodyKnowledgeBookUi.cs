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
        [SerializeField] private ClassroomStoryObjectivePresenter objectivePresenter;
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
            SetObjectiveSuppressed(true);
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
            SetObjectiveSuppressed(false);
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

            SetObjectiveSuppressed(false);
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

            bookFrame.style.width = new Length(86f, LengthUnit.Percent);
            bookFrame.style.maxWidth = 1340f;
            bookFrame.style.height = new Length(76f, LengthUnit.Percent);
            bookFrame.style.maxHeight = 780f;
            bookFrame.style.flexDirection = FlexDirection.Row;
            bookFrame.style.paddingLeft = 18f;
            bookFrame.style.paddingRight = 18f;
            bookFrame.style.paddingTop = 16f;
            bookFrame.style.paddingBottom = 16f;
            bookFrame.style.backgroundColor = new Color(0.95f, 0.9f, 0.8f, 0.99f);
            bookFrame.style.borderLeftWidth = 4f;
            bookFrame.style.borderRightWidth = 4f;
            bookFrame.style.borderTopWidth = 4f;
            bookFrame.style.borderBottomWidth = 4f;
            bookFrame.style.borderLeftColor = new Color(0.05f, 0.05f, 0.05f, 1f);
            bookFrame.style.borderRightColor = new Color(0.05f, 0.05f, 0.05f, 1f);
            bookFrame.style.borderTopColor = new Color(0.05f, 0.05f, 0.05f, 1f);
            bookFrame.style.borderBottomColor = new Color(0.05f, 0.05f, 0.05f, 1f);
            bookFrame.style.borderTopLeftRadius = 2f;
            bookFrame.style.borderTopRightRadius = 2f;
            bookFrame.style.borderBottomLeftRadius = 2f;
            bookFrame.style.borderBottomRightRadius = 2f;

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
            rightImage.style.borderTopLeftRadius = 2f;
            rightImage.style.borderTopRightRadius = 2f;
            rightImage.style.borderBottomLeftRadius = 2f;
            rightImage.style.borderBottomRightRadius = 2f;
            rightImage.style.backgroundColor = new Color(0.87f, 0.79f, 0.64f, 1f);
            rightImage.style.borderLeftWidth = 3f;
            rightImage.style.borderRightWidth = 3f;
            rightImage.style.borderTopWidth = 3f;
            rightImage.style.borderBottomWidth = 3f;
            rightImage.style.borderLeftColor = new Color(0.05f, 0.05f, 0.05f, 1f);
            rightImage.style.borderRightColor = new Color(0.05f, 0.05f, 0.05f, 1f);
            rightImage.style.borderTopColor = new Color(0.05f, 0.05f, 0.05f, 1f);
            rightImage.style.borderBottomColor = new Color(0.05f, 0.05f, 0.05f, 1f);

            imageFallback = new Label("Image unavailable")
            {
                name = "image-fallback"
            };
            imageFallback.style.unityTextAlign = TextAnchor.MiddleCenter;
            imageFallback.style.color = new Color(0.15f, 0.11f, 0.07f, 0.9f);
            imageFallback.style.fontSize = 17f;
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
            footer.style.bottom = 16f;
            footer.style.height = 48f;
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.SpaceBetween;
            footer.style.alignItems = Align.Center;
            footer.style.paddingLeft = 12f;
            footer.style.paddingRight = 12f;

            previousButton = CreateButton("< PREV", ShowPreviousPage, new Color(0.49f, 0.7f, 0.96f, 1f), new Color(0.06f, 0.07f, 0.08f, 1f));
            nextButton = CreateButton("NEXT >", ShowNextPage, new Color(0.53f, 0.88f, 0.56f, 1f), new Color(0.06f, 0.07f, 0.08f, 1f));
            closeButton = CreateButton("CLOSE", Close, new Color(1f, 0.79f, 0.3f, 1f), new Color(0.06f, 0.07f, 0.08f, 1f));

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

            objectivePresenter = objectivePresenter != null
                ? objectivePresenter
                : FindFirstObjectByType<ClassroomStoryObjectivePresenter>(FindObjectsInactive.Include);
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
            page.style.paddingLeft = 16f;
            page.style.paddingRight = 16f;
            page.style.paddingTop = 14f;
            page.style.paddingBottom = 14f;
            page.style.backgroundColor = new Color(0.98f, 0.95f, 0.88f, 1f);
            page.style.borderTopLeftRadius = 1f;
            page.style.borderTopRightRadius = 1f;
            page.style.borderBottomLeftRadius = 1f;
            page.style.borderBottomRightRadius = 1f;
            page.style.borderLeftWidth = 3f;
            page.style.borderRightWidth = 3f;
            page.style.borderTopWidth = 3f;
            page.style.borderBottomWidth = 3f;
            page.style.borderLeftColor = new Color(0.07f, 0.07f, 0.07f, 1f);
            page.style.borderRightColor = new Color(0.07f, 0.07f, 0.07f, 1f);
            page.style.borderTopColor = new Color(0.07f, 0.07f, 0.07f, 1f);
            page.style.borderBottomColor = new Color(0.07f, 0.07f, 0.07f, 1f);
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
            label.style.fontSize = 22f;
            label.style.color = new Color(0.11f, 0.08f, 0.05f, 1f);
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
            label.style.fontSize = 17f;
            label.style.color = new Color(0.15f, 0.11f, 0.07f, 1f);
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            label.style.overflow = Overflow.Hidden;
            return label;
        }

        private static VisualElement CreateSpineDivider()
        {
            var divider = new VisualElement { name = "book-spine" };
            divider.style.width = 10f;
            divider.style.marginLeft = 10f;
            divider.style.marginRight = 10f;
            divider.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
            return divider;
        }

        private static Button CreateButton(string label, Action onClicked, Color fillColor, Color textColor)
        {
            var button = new Button(() => onClicked?.Invoke())
            {
                text = label
            };

            button.style.width = 172f;
            button.style.height = 44f;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.fontSize = 20f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.letterSpacing = 1.2f;
            button.style.backgroundColor = fillColor;
            button.style.color = textColor;
            button.style.borderLeftWidth = 3f;
            button.style.borderRightWidth = 3f;
            button.style.borderTopWidth = 3f;
            button.style.borderBottomWidth = 3f;
            button.style.borderLeftColor = new Color(0.06f, 0.07f, 0.08f, 1f);
            button.style.borderRightColor = new Color(0.06f, 0.07f, 0.08f, 1f);
            button.style.borderTopColor = new Color(0.06f, 0.07f, 0.08f, 1f);
            button.style.borderBottomColor = new Color(0.06f, 0.07f, 0.08f, 1f);
            button.style.borderTopLeftRadius = 0f;
            button.style.borderTopRightRadius = 0f;
            button.style.borderBottomLeftRadius = 0f;
            button.style.borderBottomRightRadius = 0f;

            var hoverColor = Color.Lerp(fillColor, Color.white, 0.1f);
            var pressedColor = Color.Lerp(fillColor, Color.black, 0.16f);
            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (button.enabledSelf)
                {
                    button.style.backgroundColor = hoverColor;
                }
            });
            button.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (button.enabledSelf)
                {
                    button.style.backgroundColor = fillColor;
                }
            });
            button.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (button.enabledSelf)
                {
                    button.style.backgroundColor = pressedColor;
                }
            });
            button.RegisterCallback<PointerUpEvent>(_ =>
            {
                if (button.enabledSelf)
                {
                    button.style.backgroundColor = hoverColor;
                }
            });
            return button;
        }

        private void SetObjectiveSuppressed(bool suppressed)
        {
            objectivePresenter = objectivePresenter != null
                ? objectivePresenter
                : FindFirstObjectByType<ClassroomStoryObjectivePresenter>(FindObjectsInactive.Include);
            objectivePresenter?.SetSuppressed(suppressed);
        }
    }
}
