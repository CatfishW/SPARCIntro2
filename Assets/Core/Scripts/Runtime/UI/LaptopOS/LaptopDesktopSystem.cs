using System;
using System.Collections.Generic;
using System.Linq;
using ItemInteraction;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.UI.LaptopOS
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class LaptopDesktopSystem : MonoBehaviour
    {
        private const string DefaultReminderTitle = "School Reminder";
        private const string DefaultReminderBody = "Class starts soon. Don't be late.";
        private const string DefaultDesktopLabel = "Desktop";
        private const string MessagesWindowId = "messages";
        private const string CalendarWindowId = "calendar";
        private const string FinderWindowId = "finder";
        private const string NotesWindowId = "notes";
        private const string MusicWindowId = "music";
        private const string PhotosWindowId = "photos";
        private const string SettingsWindowId = "settings";
        private const string ArcadeWindowId = "arcade";
        private const string LaunchpadWindowId = "launchpad";

        [Header("UI")]
        [SerializeField] private PanelSettings panelSettings;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private int documentSortingOrder = 1000;

        [Header("Story")]
        [SerializeField] private bool showReminderOnOpen = true;
        [SerializeField] private string reminderTitle = DefaultReminderTitle;
        [SerializeField, TextArea] private string reminderBody = DefaultReminderBody;
        [SerializeField] private string reminderPrimaryAppId = CalendarWindowId;

        [Header("Windowing")]
        [SerializeField] private float menuBarHeight = 28f;
        [SerializeField] private bool closeOnEscape = true;

        public event Action ReminderViewed;
        public event Action Opened;
        public event Action Closed;

        public bool IsOpen => isOpen;
        public bool HasViewedClassReminder => hasViewedClassReminder;
        public string ActiveAppId => activeAppId;

        private UIDocument uiDocument;
        private VisualElement root;
        private VisualElement shell;
        private VisualElement wallpaper;
        private VisualElement desktopStage;
        private VisualElement desktopShortcutContainer;
        private VisualElement menuBar;
        private VisualElement windowStage;
        private VisualElement dockTrack;
        private VisualElement launchpadOverlay;
        private VisualElement launchpadPanel;
        private VisualElement launchpadGrid;
        private VisualElement reminderCard;
        private VisualElement notificationStack;
        private TextField launchpadSearchField;
        private Label menuTitleLabel;
        private Button menuClockButton;
        private Label reminderBodyLabel;
        private Button reminderOpenButton;
        private readonly Dictionary<string, LaptopAppManifest> manifests = new Dictionary<string, LaptopAppManifest>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LaptopWindow> openWindows = new Dictionary<string, LaptopWindow>(StringComparer.OrdinalIgnoreCase);
        private readonly List<LaptopToast> toasts = new List<LaptopToast>();
        private readonly List<DesktopShortcut> desktopShortcuts = new List<DesktopShortcut>();
        private readonly List<DockButton> dockButtons = new List<DockButton>();
        private readonly List<LaunchpadTile> launchpadTiles = new List<LaunchpadTile>();
        private readonly List<string> launchpadFilterMatches = new List<string>();
        private readonly Dictionary<string, VectorImage> iconCache = new Dictionary<string, VectorImage>(StringComparer.OrdinalIgnoreCase);
        private bool built;
        private bool isOpen;
        private bool hasViewedClassReminder;
        private bool reminderViewedRaised;
        private bool launchpadVisible;
        private bool cachedCursorState;
        private CursorLockMode previousCursorLockState;
        private bool previousCursorVisible;
        private string activeAppId = DefaultDesktopLabel;
        private float clockAccumulator;
        private float dockBadgePulse;
        private SnakeGameController snakeGameController;
        private MemoryMatchGameController memoryMatchGameController;
        private readonly List<VisualElement> liveDynamicElements = new List<VisualElement>();

        private void Awake()
        {
            uiDocument = GetComponent<UIDocument>();
            EnsureBuilt();
            HideShellImmediate();
        }

        private void OnEnable()
        {
            EnsureBuilt();

            if (panelSettings != null)
            {
                uiDocument.panelSettings = panelSettings;
            }

            uiDocument.sortingOrder = documentSortingOrder;

            if (isOpen)
            {
                ShowShellImmediate();
            }
            else
            {
                HideShellImmediate();
            }
        }

        private void Update()
        {
            if (!built)
            {
                return;
            }

            clockAccumulator += Time.deltaTime;
            dockBadgePulse += Time.deltaTime;

            if (clockAccumulator >= 1f)
            {
                clockAccumulator = 0f;
                UpdateClockLabel();
            }

            UpdateToasts(Time.deltaTime);
            snakeGameController?.Tick(Time.deltaTime);
            memoryMatchGameController?.Tick(Time.deltaTime);
            UpdateReminderCard();

            if (closeOnEscape && isOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
        }

        public void Open()
        {
            EnsureBuilt();

            if (isOpen)
            {
                FocusDesktop();
                return;
            }

            EnsureCursorForLaptop();
            isOpen = true;
            ShowShellImmediate();
            FocusDesktop();

            if (showReminderOnOpen)
            {
                ShowNotification(reminderTitle, reminderBody, 4f);
            }

            Opened?.Invoke();
        }

        public void Close()
        {
            if (!isOpen)
            {
                return;
            }

            isOpen = false;
            launchpadVisible = false;
            SetDisplay(launchpadOverlay, DisplayStyle.None);
            HideShellImmediate();
            RestoreCursorState();
            Closed?.Invoke();
        }

        public void OpenApp(string appId)
        {
            EnsureBuilt();

            if (string.IsNullOrWhiteSpace(appId))
            {
                appId = FinderWindowId;
            }

            if (string.Equals(appId, LaunchpadWindowId, StringComparison.OrdinalIgnoreCase))
            {
                ToggleLaunchpad();
                return;
            }

            if (!isOpen)
            {
                Open();
            }

            launchpadVisible = false;
            SetDisplay(launchpadOverlay, DisplayStyle.None);

            if (!openWindows.TryGetValue(appId, out var window))
            {
                window = CreateWindow(appId);
                openWindows[appId] = window;
            }

            ShowWindow(window);
            activeAppId = appId;
            UpdateMenuTitle();

            if (IsReminderApp(appId))
            {
                MarkClassReminderViewed();
            }

            if (appId == ArcadeWindowId)
            {
                snakeGameController?.OnActivated();
                memoryMatchGameController?.OnActivated();
            }
        }

        public void ToggleLaunchpad()
        {
            EnsureBuilt();

            if (!isOpen)
            {
                Open();
            }

            launchpadVisible = !launchpadVisible;
            SetDisplay(launchpadOverlay, launchpadVisible ? DisplayStyle.Flex : DisplayStyle.None);
            if (launchpadVisible)
            {
                activeAppId = LaunchpadWindowId;
                UpdateMenuTitle();
                RefreshLaunchpadFilter();
            }

            RefreshDesktopLayering();
        }

        public void MarkClassReminderViewed()
        {
            if (hasViewedClassReminder)
            {
                return;
            }

            hasViewedClassReminder = true;
            if (!reminderViewedRaised)
            {
                reminderViewedRaised = true;
                ReminderViewed?.Invoke();
            }

            UpdateReminderCard();
            RefreshDockBadges();
            ShowNotification("Reminder updated", "The class reminder is now marked as seen.", 2.6f);
        }

        public bool IsAppOpen(string appId)
        {
            return openWindows.TryGetValue(appId, out var window) && window.Root.style.display == DisplayStyle.Flex;
        }

        private void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            if (uiDocument == null)
            {
                return;
            }

            if (panelSettings != null)
            {
                uiDocument.panelSettings = panelSettings;
            }

            uiDocument.sortingOrder = documentSortingOrder;

            root = uiDocument.rootVisualElement;
            if (root == null)
            {
                return;
            }

            root.Clear();
            root.style.flexGrow = 1f;
            root.style.position = Position.Relative;
            root.style.display = DisplayStyle.None;
            root.pickingMode = PickingMode.Ignore;
            root.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsiveLayout());

            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            BuildManifests();
            BuildShell();
            BuildDesktopShortcuts();
            BuildDock();
            BuildLaunchpad();
            BuildReminderCard();
            BuildNotificationStack();
            UpdateWindowStageVisibility();
            RefreshDesktopLayering();
            UpdateClockLabel();
            RefreshDockBadges();
            UpdateReminderCard();
            ApplyResponsiveLayout();

            built = true;
        }

        private void BuildManifests()
        {
            manifests.Clear();
            iconCache.Clear();

            RegisterManifest(new LaptopAppManifest(FinderWindowId, "Finder", "F", new Color(0.37f, 0.72f, 0.98f, 1f), "Files and folders", "Finder"));
            RegisterManifest(new LaptopAppManifest(MessagesWindowId, "Messages", "M", new Color(0.22f, 0.65f, 0.98f, 1f), "Inbox and class reminder", "Messages", true));
            RegisterManifest(new LaptopAppManifest(CalendarWindowId, "Calendar", "C", new Color(0.95f, 0.36f, 0.32f, 1f), "Timetable and upcoming class", "Calendar", true));
            RegisterManifest(new LaptopAppManifest(NotesWindowId, "Notes", "N", new Color(0.98f, 0.82f, 0.26f, 1f), "Quick notes and tasks", "Notes"));
            RegisterManifest(new LaptopAppManifest(MusicWindowId, "Music", "Mu", new Color(0.82f, 0.38f, 0.97f, 1f), "Playlist and visualizer", "Music"));
            RegisterManifest(new LaptopAppManifest(PhotosWindowId, "Photos", "Ph", new Color(0.30f, 0.83f, 0.65f, 1f), "Gallery and memories", "Photos"));
            RegisterManifest(new LaptopAppManifest(SettingsWindowId, "Settings", "S", new Color(0.55f, 0.61f, 0.68f, 1f), "System controls", "Settings"));
            RegisterManifest(new LaptopAppManifest(ArcadeWindowId, "Arcade", "G", new Color(0.98f, 0.58f, 0.18f, 1f), "Mini games and distractions", "Arcade"));
            RegisterManifest(new LaptopAppManifest(LaunchpadWindowId, "Launchpad", "+", new Color(0.52f, 0.62f, 0.98f, 1f), "All apps", "Launchpad"));
        }

        private void RegisterManifest(LaptopAppManifest manifest)
        {
            manifests[manifest.Id] = manifest;
        }

        private void BuildShell()
        {
            shell = new VisualElement { name = "LaptopShell" };
            shell.AddToClassList("laptop-shell");
            shell.style.flexGrow = 1f;
            shell.style.position = Position.Relative;
            root.Add(shell);

            wallpaper = new VisualElement { name = "Wallpaper" };
            wallpaper.AddToClassList("laptop-wallpaper");
            wallpaper.pickingMode = PickingMode.Ignore;
            shell.Add(wallpaper);

            shell.Add(CreateOrb("orb-top-left", "wallpaper-orb wallpaper-orb--top-left"));
            shell.Add(CreateOrb("orb-top-right", "wallpaper-orb wallpaper-orb--top-right"));
            shell.Add(CreateOrb("orb-bottom", "wallpaper-orb wallpaper-orb--bottom"));
            shell.Add(CreateOrb("orb-center", "wallpaper-orb wallpaper-orb--center"));

            menuBar = new VisualElement { name = "MenuBar" };
            menuBar.AddToClassList("laptop-menu-bar");
            shell.Add(menuBar);

            var menuLeft = new VisualElement { name = "MenuLeft" };
            menuLeft.AddToClassList("laptop-menu-bar__section");
            menuLeft.AddToClassList("laptop-menu-bar__left");
            menuBar.Add(menuLeft);

            var menuCenter = new VisualElement { name = "MenuCenter" };
            menuCenter.AddToClassList("laptop-menu-bar__section");
            menuCenter.AddToClassList("laptop-menu-bar__center");
            menuBar.Add(menuCenter);

            var menuRight = new VisualElement { name = "MenuRight" };
            menuRight.AddToClassList("laptop-menu-bar__section");
            menuRight.AddToClassList("laptop-menu-bar__right");
            menuBar.Add(menuRight);

            menuLeft.Add(CreateMenuChip("Finder", () => OpenApp(FinderWindowId)));
            menuLeft.Add(CreateMenuChip("File", () => OpenApp(NotesWindowId)));
            menuLeft.Add(CreateMenuChip("Edit", () => OpenApp(SettingsWindowId)));
            menuLeft.Add(CreateMenuChip("View", ToggleLaunchpad));
            menuLeft.Add(CreateMenuChip("Go", () => OpenApp(CalendarWindowId)));
            menuLeft.Add(CreateMenuChip("Window", () => OpenApp(FinderWindowId)));
            menuLeft.Add(CreateMenuChip("Help", () => ShowNotification("Help", "This is a mock macOS desktop for the school morning scene.", 3f)));

            menuTitleLabel = new Label(DefaultDesktopLabel);
            menuTitleLabel.AddToClassList("laptop-menu-title");
            menuCenter.Add(menuTitleLabel);

            var wifiButton = CreateMenuChip("Wi-Fi", () => ShowNotification("Wi-Fi", "Connected to the apartment network.", 2f));
            wifiButton.AddToClassList("laptop-menu-status");
            menuRight.Add(wifiButton);

            var batteryButton = CreateMenuChip("100%", () => OpenApp(SettingsWindowId));
            batteryButton.AddToClassList("laptop-menu-status");
            menuRight.Add(batteryButton);

            var clockButton = CreateMenuChip(string.Empty, () => OpenApp(CalendarWindowId));
            clockButton.AddToClassList("laptop-menu-status");
            clockButton.name = "MenuClockButton";
            menuRight.Add(clockButton);
            menuClockButton = clockButton;
            UpdateClockLabel();

            desktopStage = new VisualElement { name = "DesktopStage" };
            desktopStage.AddToClassList("laptop-desktop-stage");
            shell.Add(desktopStage);

            notificationStack = new VisualElement { name = "NotificationStack" };
            notificationStack.AddToClassList("laptop-notification-stack");
            notificationStack.pickingMode = PickingMode.Ignore;
            desktopStage.Add(notificationStack);

            windowStage = new VisualElement { name = "WindowStage" };
            windowStage.AddToClassList("laptop-window-stage");
            windowStage.style.display = DisplayStyle.None;
            desktopStage.Add(windowStage);

            dockTrack = new VisualElement { name = "DockTrack" };
            dockTrack.AddToClassList("laptop-dock");
            shell.Add(dockTrack);

            launchpadOverlay = new VisualElement { name = "LaunchpadOverlay" };
            launchpadOverlay.AddToClassList("laptop-launchpad-overlay");
            launchpadOverlay.style.display = DisplayStyle.None;
            desktopStage.Add(launchpadOverlay);
            launchpadOverlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == launchpadOverlay)
                {
                    launchpadVisible = false;
                    SetDisplay(launchpadOverlay, DisplayStyle.None);
                    FocusDesktop();
                    evt.StopPropagation();
                }
            });

            launchpadPanel = new VisualElement { name = "LaunchpadPanel" };
            launchpadPanel.AddToClassList("laptop-launchpad-panel");
            launchpadOverlay.Add(launchpadPanel);
            launchpadPanel.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());

            var launchpadHeader = new VisualElement { name = "LaunchpadHeader" };
            launchpadHeader.AddToClassList("laptop-launchpad-header");
            launchpadPanel.Add(launchpadHeader);

            var launchpadTitle = new Label("Launchpad");
            launchpadTitle.AddToClassList("laptop-launchpad-title");
            launchpadHeader.Add(launchpadTitle);

            launchpadSearchField = new TextField();
            launchpadSearchField.name = "LaunchpadSearch";
            launchpadSearchField.label = string.Empty;
            launchpadSearchField.value = string.Empty;
            launchpadSearchField.AddToClassList("laptop-launchpad-search");
            launchpadSearchField.RegisterValueChangedCallback(_ => RefreshLaunchpadFilter());
            launchpadHeader.Add(launchpadSearchField);

            var launchpadScroll = new ScrollView();
            launchpadScroll.AddToClassList("laptop-launchpad-scroll");
            launchpadPanel.Add(launchpadScroll);

            launchpadGrid = new VisualElement { name = "LaunchpadGrid" };
            launchpadGrid.AddToClassList("laptop-launchpad-grid");
            launchpadScroll.Add(launchpadGrid);
        }

        private void BuildDesktopShortcuts()
        {
            var shortcuts = new[]
            {
                FinderWindowId,
                MessagesWindowId,
                CalendarWindowId,
                NotesWindowId,
                ArcadeWindowId
            };

            desktopShortcutContainer = new VisualElement { name = "DesktopShortcuts" };
            desktopShortcutContainer.AddToClassList("laptop-desktop-shortcuts");
            desktopStage.Add(desktopShortcutContainer);

            foreach (var appId in shortcuts)
            {
                if (!manifests.TryGetValue(appId, out var manifest))
                {
                    continue;
                }

                var shortcut = CreateShortcut(manifest, appId);
                desktopShortcutContainer.Add(shortcut.Root);
                desktopShortcuts.Add(shortcut);
            }
        }

        private DesktopShortcut CreateShortcut(LaptopAppManifest manifest, string appId)
        {
            var rootElement = new Button(() => OpenApp(appId));
            rootElement.text = string.Empty;
            rootElement.AddToClassList("laptop-shortcut");
            WireHoverState(rootElement);

            var icon = CreateAppIcon(manifest, "laptop-shortcut__icon");
            rootElement.Add(icon);

            var title = new Label(manifest.Title);
            title.AddToClassList("laptop-shortcut__title");
            IgnorePicking(title);
            rootElement.Add(title);

            var subtitle = new Label(manifest.Description);
            subtitle.AddToClassList("laptop-shortcut__subtitle");
            IgnorePicking(subtitle);
            rootElement.Add(subtitle);

            return new DesktopShortcut(appId, rootElement, icon, title, subtitle);
        }

        private void BuildDock()
        {
            dockTrack.Clear();
            dockButtons.Clear();

            var dockOrder = new[]
            {
                FinderWindowId,
                MessagesWindowId,
                CalendarWindowId,
                NotesWindowId,
                MusicWindowId,
                PhotosWindowId,
                SettingsWindowId,
                ArcadeWindowId,
                LaunchpadWindowId
            };

            foreach (var appId in dockOrder)
            {
                if (!manifests.TryGetValue(appId, out var manifest))
                {
                    continue;
                }

                var dockButton = CreateDockButton(manifest, appId);
                dockTrack.Add(dockButton.Root);
                dockButtons.Add(dockButton);
            }
        }

        private DockButton CreateDockButton(LaptopAppManifest manifest, string appId)
        {
            var button = new Button(() => OpenApp(appId));
            button.AddToClassList("laptop-dock-button");
            WireHoverState(button);

            var icon = CreateAppIcon(manifest, "laptop-dock-button__icon");
            button.Add(icon);

            var badge = new Label("1");
            badge.AddToClassList("laptop-dock-button__badge");
            IgnorePicking(badge);
            button.Add(badge);

            return new DockButton(appId, button, icon, badge);
        }

        private void BuildLaunchpad()
        {
            launchpadGrid.Clear();
            launchpadTiles.Clear();

            foreach (var manifest in manifests.Values.Where(manifest => manifest.ShowInLaunchpad && !string.Equals(manifest.Id, LaunchpadWindowId, StringComparison.OrdinalIgnoreCase)))
            {
                var tile = CreateLaunchpadTile(manifest);
                launchpadGrid.Add(tile.Root);
                launchpadTiles.Add(tile);
            }

            RefreshLaunchpadFilter();
        }

        private LaunchpadTile CreateLaunchpadTile(LaptopAppManifest manifest)
        {
            var tileButton = new Button(() =>
            {
                launchpadVisible = false;
                SetDisplay(launchpadOverlay, DisplayStyle.None);
                OpenApp(manifest.Id);
            });
            tileButton.AddToClassList("laptop-launchpad-tile");
            WireHoverState(tileButton);

            var icon = CreateAppIcon(manifest, "laptop-launchpad-tile__icon");
            tileButton.Add(icon);

            var title = new Label(manifest.Title);
            title.AddToClassList("laptop-launchpad-tile__title");
            IgnorePicking(title);
            tileButton.Add(title);

            var description = new Label(manifest.Description);
            description.AddToClassList("laptop-launchpad-tile__description");
            IgnorePicking(description);
            tileButton.Add(description);

            return new LaunchpadTile(manifest.Id, tileButton, icon, title, description);
        }

        private void BuildReminderCard()
        {
            reminderCard = new VisualElement { name = "ReminderCard" };
            reminderCard.AddToClassList("laptop-reminder-card");
            desktopStage.Add(reminderCard);

            var header = new Label(reminderTitle);
            header.AddToClassList("laptop-reminder-card__title");
            IgnorePicking(header);
            reminderCard.Add(header);

            reminderBodyLabel = new Label(reminderBody);
            reminderBodyLabel.AddToClassList("laptop-reminder-card__body");
            IgnorePicking(reminderBodyLabel);
            reminderCard.Add(reminderBodyLabel);

            reminderOpenButton = new Button(() => OpenApp(reminderPrimaryAppId));
            reminderOpenButton.text = "Open Calendar";
            reminderOpenButton.AddToClassList("laptop-reminder-card__button");
            WireHoverState(reminderOpenButton);
            reminderCard.Add(reminderOpenButton);
            reminderCard.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == reminderOpenButton)
                {
                    return;
                }

                if (!hasViewedClassReminder)
                {
                    OpenApp(reminderPrimaryAppId);
                }
            });

            reminderCard.RegisterCallback<PointerEnterEvent>(_ => reminderCard.AddToClassList("is-hovered"));
            reminderCard.RegisterCallback<PointerLeaveEvent>(_ => reminderCard.RemoveFromClassList("is-hovered"));
        }

        private void BuildNotificationStack()
        {
            notificationStack.Clear();
        }

        private Image CreateAppIcon(LaptopAppManifest manifest, string className)
        {
            var icon = new Image();
            icon.AddToClassList(className);
            IgnorePicking(icon);
            icon.scaleMode = ScaleMode.ScaleToFit;
            icon.tintColor = Color.white;

            var vectorImage = LoadAppIcon(manifest.IconResourceName);
            if (vectorImage != null)
            {
                icon.vectorImage = vectorImage;
                icon.EnableInClassList("has-svg", true);
            }
            else
            {
                icon.style.backgroundColor = manifest.Color;
            }

            return icon;
        }

        private VectorImage LoadAppIcon(string iconResourceName)
        {
            if (string.IsNullOrWhiteSpace(iconResourceName))
            {
                return null;
            }

            if (iconCache.TryGetValue(iconResourceName, out var cachedIcon))
            {
                return cachedIcon;
            }

            var loadedIcon = Resources.Load<VectorImage>($"LaptopOS/Icons/{iconResourceName}");
            iconCache[iconResourceName] = loadedIcon;
            return loadedIcon;
        }

        private LaptopWindow CreateWindow(string appId)
        {
            var manifest = manifests.ContainsKey(appId) ? manifests[appId] : manifests[FinderWindowId];
            var window = new LaptopWindow(appId, manifest.Title);
            var defaultRect = GetDefaultWindowRect();
            window.Root = new VisualElement();
            window.Root.name = $"{manifest.Id}-window";
            window.Root.AddToClassList("laptop-window");
            window.Root.style.position = Position.Absolute;
            window.Root.style.display = DisplayStyle.None;
            window.Root.style.left = defaultRect.x;
            window.Root.style.top = defaultRect.y;
            window.Root.style.width = defaultRect.width;
            window.Root.style.height = defaultRect.height;
            window.Root.BringToFront();

            var header = new VisualElement();
            header.AddToClassList("laptop-window__header");
            window.Root.Add(header);
            window.Header = header;

            var controls = new VisualElement();
            controls.AddToClassList("laptop-window__controls");
            header.Add(controls);
            window.Controls = controls;

            var closeButton = CreateWindowControl("close");
            closeButton.clicked += () => CloseWindow(window);
            controls.Add(closeButton);

            var minimizeButton = CreateWindowControl("minimize");
            minimizeButton.clicked += () => MinimizeWindow(window);
            controls.Add(minimizeButton);

            var zoomButton = CreateWindowControl("zoom");
            zoomButton.clicked += () => ToggleWindowMaximize(window);
            controls.Add(zoomButton);

            var title = new Label(manifest.Title);
            title.AddToClassList("laptop-window__title");
            IgnorePicking(title);
            header.Add(title);
            window.TitleLabel = title;

            var content = new VisualElement();
            content.AddToClassList("laptop-window__content");
            content.style.flexGrow = 1f;
            window.Root.Add(content);
            window.Content = content;

            var resizeHandle = new VisualElement();
            resizeHandle.AddToClassList("laptop-window__resize-handle");
            window.Root.Add(resizeHandle);
            window.ResizeHandle = resizeHandle;

            BuildWindowContent(window, appId);
            RegisterWindowInteractions(window);

            windowStage.Add(window.Root);
            return window;
        }

        private Button CreateWindowControl(string kind)
        {
            var button = new Button();
            button.AddToClassList("laptop-window__control");
            button.AddToClassList($"laptop-window__control--{kind}");
            button.text = kind == "close" ? "x" : kind == "minimize" ? "-" : "o";
            WireHoverState(button);
            return button;
        }

        private void BuildWindowContent(LaptopWindow window, string appId)
        {
            window.Content.Clear();

            switch (appId)
            {
                case FinderWindowId:
                    window.Content.Add(BuildFinderContent());
                    break;
                case MessagesWindowId:
                    window.Content.Add(BuildMessagesContent());
                    break;
                case CalendarWindowId:
                    window.Content.Add(BuildCalendarContent());
                    break;
                case NotesWindowId:
                    window.Content.Add(BuildNotesContent());
                    break;
                case MusicWindowId:
                    window.Content.Add(BuildMusicContent());
                    break;
                case PhotosWindowId:
                    window.Content.Add(BuildPhotosContent());
                    break;
                case SettingsWindowId:
                    window.Content.Add(BuildSettingsContent());
                    break;
                case ArcadeWindowId:
                    window.Content.Add(BuildArcadeContent());
                    break;
                default:
                    window.Content.Add(BuildFinderContent());
                    break;
            }
        }

        private VisualElement BuildFinderContent()
        {
            var root = CreatePanelRoot();
            root.Add(CreateSectionHeader("Finder", "A tidy place for your school files and distraction stash."));

            var browser = new VisualElement();
            browser.AddToClassList("laptop-split-view");
            root.Add(browser);

            var sidebar = CreateSidebar("Locations");
            browser.Add(sidebar);

            var main = CreateScrollPane("laptop-main-scroll", "laptop-main-view");
            browser.Add(main);

            var detail = CreateFixedDetailPane(browser);
            var detailCard = CreatePreviewCard(detail, "Finder Selection", "Choose a folder or card to preview it here.", "Preview");
            var detailTitle = detailCard.Title;
            var detailBody = detailCard.Body;
            var detailFooter = detailCard.Footer;

            var finderButtons = new List<Button>();

            void SelectFinder(Button button, string title, string body, string footer)
            {
                UpdateSelection(finderButtons, button);
                detailTitle.text = title;
                detailBody.text = body;
                detailFooter.text = footer;
            }

            Button AddSidebarEntry(string title, string body, string footer)
            {
                Button button = null;
                button = CreateSidebarItem(title, () => SelectFinder(button, title, body, footer));
                finderButtons.Add(button);
                sidebar.Add(button);
                return button;
            }

            Button AddFinderCard(string title, string body, string footer)
            {
                Button button = null;
                button = CreateGridCard(title, body, () => SelectFinder(button, title, body, footer));
                finderButtons.Add(button);
                main.Add(button);
                return button;
            }

            var defaultSelection = AddSidebarEntry("School", "Timetable, homework, and classroom docs.", "Calendar and coursework");
            AddSidebarEntry("Downloads", "A class PDF, a campus map, and too many screenshots.", "Recent files");
            AddSidebarEntry("Notes", "Loose thoughts, class reminders, and a checklist for leaving.", "Quick notes");
            AddSidebarEntry("Photos", "Wallpaper tests, hallway shots, and a sunrise photo.", "Photo library");
            AddSidebarEntry("Music", "Late-night study mix and a softer morning playlist.", "Audio library");

            AddFinderCard("School", "Timetable, homework, and classroom docs.", "Pinned folder");
            AddFinderCard("Downloads", "Random files and a few too many screenshots.", "Recently changed");
            AddFinderCard("Notes", "Loose thoughts and class reminders.", "Shortcut folder");
            AddFinderCard("Photos", "Old shots, wallpapers, and memory lane.", "Shortcut folder");
            AddFinderCard("Music", "Night playlist and a lo-fi study mix.", "Shortcut folder");
            AddFinderCard("Archive", "A drawer of forgotten folders.", "Older files");

            SelectFinder(defaultSelection, "School", "Timetable, homework, and classroom docs.", "Calendar and coursework");

            return root;
        }

        private VisualElement BuildMessagesContent()
        {
            var root = CreatePanelRoot();
            root.Add(CreateSectionHeader("Messages", "Inbox, class thread, and the reminder you needed to see."));

            var split = new VisualElement();
            split.AddToClassList("laptop-split-view");
            root.Add(split);

            var sidebar = CreateSidebar("Conversations");
            split.Add(sidebar);

            var conversation = CreateScrollPane("laptop-conversation");
            split.Add(conversation);

            var threadInspector = CreatePreviewCard(conversation, "Thread", "Select a conversation or message.", "Messages");
            var threadButtons = new List<Button>();
            var bubbleButtons = new List<Button>();

            void SelectThread(Button button, string title, string body, string footer)
            {
                UpdateSelection(threadButtons, button);
                threadInspector.Title.text = title;
                threadInspector.Body.text = body;
                threadInspector.Footer.text = footer;
            }

            void SelectBubble(Button button, string title, string body, string footer)
            {
                UpdateSelection(bubbleButtons, button);
                threadInspector.Title.text = title;
                threadInspector.Body.text = body;
                threadInspector.Footer.text = footer;
            }

            Button schoolThread = null;
            schoolThread = CreateSidebarItem("School Office", () =>
            {
                MarkClassReminderViewed();
                SelectThread(schoolThread, "School Office", "Class starts soon. Don't be late.", "Administrative reminder");
            });
            threadButtons.Add(schoolThread);
            sidebar.Add(schoolThread);

            Button mayaThread = null;
            mayaThread = CreateSidebarItem("Maya", () => SelectThread(mayaThread, "Maya", "You still coming to class?", "Waiting by the classroom"));
            threadButtons.Add(mayaThread);
            sidebar.Add(mayaThread);

            Button momThread = null;
            momThread = CreateSidebarItem("Mom", () => SelectThread(momThread, "Mom", "Grab lunch on the way back.", "Family"));
            threadButtons.Add(momThread);
            sidebar.Add(momThread);

            Button groupThread = null;
            groupThread = CreateSidebarItem("Group Chat", () => SelectThread(groupThread, "Group Chat", "New meme. Zero academic value.", "Muted later"));
            threadButtons.Add(groupThread);
            sidebar.Add(groupThread);

            Button reminderBubble = null;
            reminderBubble = CreateMessageBubble("School Office", "Class starts soon. Don't be late.", true, false, () =>
            {
                MarkClassReminderViewed();
                SelectThread(schoolThread, "School Office", "Class starts soon. Don't be late.", "Administrative reminder");
                SelectBubble(reminderBubble, "School Office", "Class starts soon. Don't be late.", "Opens Calendar if needed");
            });
            bubbleButtons.Add(reminderBubble);
            conversation.Add(reminderBubble);

            Button mayaBubble = null;
            mayaBubble = CreateMessageBubble("Maya", "You still coming to class?", false, false, () =>
            {
                SelectThread(mayaThread, "Maya", "You still coming to class?", "Waiting by the classroom");
                SelectBubble(mayaBubble, "Maya", "You still coming to class?", "Reply pending");
            });
            bubbleButtons.Add(mayaBubble);
            conversation.Add(mayaBubble);

            Button selfBubble = null;
            selfBubble = CreateMessageBubble("You", "Yeah, on my way.", false, true, () =>
            {
                SelectBubble(selfBubble, "You", "Yeah, on my way.", "Reply sent");
            });
            bubbleButtons.Add(selfBubble);
            conversation.Add(selfBubble);

            var actionBar = new VisualElement();
            actionBar.AddToClassList("laptop-message-actions");
            conversation.Add(actionBar);

            var openCalendar = new Button(() =>
            {
                MarkClassReminderViewed();
                OpenApp(CalendarWindowId);
            });
            openCalendar.text = "Open Calendar";
            openCalendar.AddToClassList("laptop-primary-button");
            WireHoverState(openCalendar);
            actionBar.Add(openCalendar);

            var markSeen = new Button(() => MarkClassReminderViewed());
            markSeen.text = "Mark Reminder Seen";
            markSeen.AddToClassList("laptop-secondary-button");
            WireHoverState(markSeen);
            actionBar.Add(markSeen);

            SelectThread(schoolThread, "School Office", "Class starts soon. Don't be late.", "Administrative reminder");
            SelectBubble(reminderBubble, "School Office", "Class starts soon. Don't be late.", "Opens Calendar if needed");

            return root;
        }

        private VisualElement BuildCalendarContent()
        {
            var root = CreatePanelRoot();
            root.Add(CreateSectionHeader("Calendar", "Today is packed. The first class is the one that matters."));

            var split = new VisualElement();
            split.AddToClassList("laptop-split-view");
            root.Add(split);

            var timeline = CreateScrollPane("laptop-timeline");
            split.Add(timeline);

            var detail = new VisualElement();
            detail.AddToClassList("laptop-detail-pane");
            split.Add(detail);

            var selectedCard = CreatePreviewCard(detail, "08:40 First class", "This is the class you cannot miss.", "Today");
            var selectedTimelineButtons = new List<Button>();

            void SelectTimeline(Button button, string time, string title, string body, string footer)
            {
                UpdateSelection(selectedTimelineButtons, button);
                selectedCard.Title.text = $"{time} {title}";
                selectedCard.Body.text = body;
                selectedCard.Footer.text = footer;
            }

            Button wakeButton = null;
            wakeButton = CreateTimelineItem("08:10", "Wake up", "Stretch, breathe, stop pretending to sleep.", () => SelectTimeline(wakeButton, "08:10", "Wake up", "Stretch, breathe, stop pretending to sleep.", "Start of day"));
            selectedTimelineButtons.Add(wakeButton);
            timeline.Add(wakeButton);

            Button laptopButton = null;
            laptopButton = CreateTimelineItem("08:25", "Grab laptop", "Check the reminder before heading out.", () => SelectTimeline(laptopButton, "08:25", "Grab laptop", "Check the reminder before heading out.", "Prep"));
            selectedTimelineButtons.Add(laptopButton);
            timeline.Add(laptopButton);

            Button classButton = null;
            classButton = CreateTimelineItem("08:40", "First class", "Don't be late.", () =>
            {
                MarkClassReminderViewed();
                SelectTimeline(classButton, "08:40", "First class", "Don't be late.", "Highest priority");
            });
            selectedTimelineButtons.Add(classButton);
            timeline.Add(classButton);

            Button breakButton = null;
            breakButton = CreateTimelineItem("10:00", "Break", "Coffee if you survive the first block.", () => SelectTimeline(breakButton, "10:00", "Break", "Coffee if you survive the first block.", "Recovery"));
            selectedTimelineButtons.Add(breakButton);
            timeline.Add(breakButton);

            detail.Add(CreateReminderSummaryCard(() =>
            {
                MarkClassReminderViewed();
                OpenApp(MessagesWindowId);
            }));

            var openMessages = new Button(() => OpenApp(MessagesWindowId));
            openMessages.text = "Open Messages";
            openMessages.AddToClassList("laptop-primary-button");
            WireHoverState(openMessages);
            detail.Add(openMessages);

            SelectTimeline(classButton, "08:40", "First class", "Don't be late.", "Highest priority");

            return root;
        }

        private VisualElement BuildNotesContent()
        {
            var root = CreatePanelRoot();
            root.Add(CreateSectionHeader("Notes", "A few things worth remembering."));

            var split = new VisualElement();
            split.AddToClassList("laptop-split-view");
            root.Add(split);

            var notesGrid = CreateScrollPane("laptop-card-grid-scroll", "laptop-card-grid");
            split.Add(notesGrid);

            var detail = CreateFixedDetailPane(split);
            var noteDetail = CreatePreviewCard(detail, "Morning", "Shower. Laptop. Leave early. Not a complicated plan.", "Selected note");
            var noteButtons = new List<Button>();

            void SelectNote(Button button, string title, string body)
            {
                UpdateSelection(noteButtons, button);
                noteDetail.Title.text = title;
                noteDetail.Body.text = body;
                noteDetail.Footer.text = "Selected note";
            }

            Button morningNote = null;
            morningNote = CreateNoteCard("Morning", "Shower. Laptop. Leave early. Not a complicated plan.", () => SelectNote(morningNote, "Morning", "Shower. Laptop. Leave early. Not a complicated plan."));
            noteButtons.Add(morningNote);
            notesGrid.Add(morningNote);

            Button checklistNote = null;
            checklistNote = CreateNoteCard("Class Checklist", "Charge laptop\nNotebook\nPen\nWater bottle", () => SelectNote(checklistNote, "Class Checklist", "Charge laptop\nNotebook\nPen\nWater bottle"));
            noteButtons.Add(checklistNote);
            notesGrid.Add(checklistNote);

            Button ideaNote = null;
            ideaNote = CreateNoteCard("Idea", "Make the room feel like someone actually lives here.", () => SelectNote(ideaNote, "Idea", "Make the room feel like someone actually lives here."));
            noteButtons.Add(ideaNote);
            notesGrid.Add(ideaNote);

            Button arcadeNote = null;
            arcadeNote = CreateNoteCard("Distraction", "Arcade app should probably stay hidden until later.", () => SelectNote(arcadeNote, "Distraction", "Arcade app should probably stay hidden until later."));
            noteButtons.Add(arcadeNote);
            notesGrid.Add(arcadeNote);

            SelectNote(morningNote, "Morning", "Shower. Laptop. Leave early. Not a complicated plan.");

            return root;
        }

        private VisualElement BuildMusicContent()
        {
            var root = CreatePanelRoot();
            root.Add(CreateSectionHeader("Music", "Low-key tracks for thinking, moving, or ignoring the clock."));

            var bodyScroll = CreateScrollPane("laptop-static-scroll", "laptop-static-scroll__content");
            root.Add(bodyScroll);

            var player = new VisualElement();
            player.AddToClassList("laptop-music-player");
            bodyScroll.Add(player);

            var album = new Button(() => ShowNotification("Album Art", "Late Study cover opened in Quick Look.", 2.2f));
            album.text = string.Empty;
            album.AddToClassList("laptop-album-art");
            WireHoverState(album);
            player.Add(album);

            var albumTitle = new Label("Late Study");
            albumTitle.AddToClassList("laptop-album-title");
            IgnorePicking(albumTitle);
            player.Add(albumTitle);

            var albumSubtitle = new Label("Lo-fi / ambient / morning drift");
            albumSubtitle.AddToClassList("laptop-album-subtitle");
            IgnorePicking(albumSubtitle);
            player.Add(albumSubtitle);

            var controls = new VisualElement();
            controls.AddToClassList("laptop-music-controls");
            player.Add(controls);

            var playerDetail = CreatePreviewCard(player, "Late Study", "Lo-fi / ambient / morning drift", "Selected track detail");

            var playButton = new Button(() => ShowNotification("Now Playing", "A mellow loop is running in the background.", 3f));
            playButton.text = "Play";
            playButton.AddToClassList("laptop-primary-button");
            WireHoverState(playButton);
            playButton.clicked += () =>
            {
                playerDetail.Title.text = "Now Playing";
                playerDetail.Body.text = "Late Study is running in the background.";
                playerDetail.Footer.text = "Playback active";
            };
            controls.Add(playButton);

            var musicButtons = new List<Button>();

            var albumButton = album;
            musicButtons.Add(albumButton);
            albumButton.clicked += () =>
            {
                UpdateSelection(musicButtons, albumButton);
                playerDetail.Title.text = "Late Study";
                playerDetail.Body.text = "Album art preview open. Lo-fi / ambient / morning drift.";
                playerDetail.Footer.text = "Album selected";
            };

            Button tempoButton = null;
            tempoButton = CreateMiniStat("Tempo", "72 BPM", () =>
            {
                UpdateSelection(musicButtons, tempoButton);
                playerDetail.Title.text = "Tempo";
                playerDetail.Body.text = "72 BPM. Slow enough to ignore the clock.";
                playerDetail.Footer.text = "Track stat";
            });
            musicButtons.Add(tempoButton);
            controls.Add(tempoButton);

            Button moodButton = null;
            moodButton = CreateMiniStat("Mood", "Calm", () =>
            {
                UpdateSelection(musicButtons, moodButton);
                playerDetail.Title.text = "Mood";
                playerDetail.Body.text = "Calm, but not calm enough to skip class.";
                playerDetail.Footer.text = "Track stat";
            });
            musicButtons.Add(moodButton);
            controls.Add(moodButton);

            Button volumeButton = null;
            volumeButton = CreateMiniStat("Volume", "Low", () =>
            {
                UpdateSelection(musicButtons, volumeButton);
                playerDetail.Title.text = "Volume";
                playerDetail.Body.text = "Low volume. Neighbors remain unharmed.";
                playerDetail.Footer.text = "Track stat";
            });
            musicButtons.Add(volumeButton);
            controls.Add(volumeButton);

            var visualizer = new VisualElement();
            visualizer.AddToClassList("laptop-visualizer");
            player.Add(visualizer);

            for (var index = 0; index < 12; index++)
            {
                var bar = new VisualElement();
                bar.AddToClassList("laptop-visualizer__bar");
                bar.style.height = 16f + (index % 4) * 12f;
                bar.style.backgroundColor = Color.Lerp(new Color(0.32f, 0.71f, 0.99f, 1f), new Color(0.88f, 0.42f, 0.98f, 1f), index / 11f);
                IgnorePicking(bar);
                visualizer.Add(bar);
            }

            UpdateSelection(musicButtons, albumButton);

            return root;
        }

        private VisualElement BuildPhotosContent()
        {
            var root = CreatePanelRoot();
            root.Add(CreateSectionHeader("Photos", "Nothing dramatic. Just enough to remember the day."));

            var split = new VisualElement();
            split.AddToClassList("laptop-split-view");
            root.Add(split);

            var gallery = CreateScrollPane("laptop-photo-grid-scroll", "laptop-photo-grid");
            split.Add(gallery);

            var detail = CreateFixedDetailPane(split);
            var photoDetail = CreatePreviewCard(detail, "Sunrise", "Warm light hitting the apartment walls before class.", "Selected photo");
            var photoButtons = new List<Button>();

            void SelectPhoto(Button button, string title, string body)
            {
                UpdateSelection(photoButtons, button);
                photoDetail.Title.text = title;
                photoDetail.Body.text = body;
                photoDetail.Footer.text = "Selected photo";
            }

            Button sunrisePhoto = null;
            sunrisePhoto = CreatePhotoTile("Sunrise", new Color(0.95f, 0.59f, 0.31f, 1f), () => SelectPhoto(sunrisePhoto, "Sunrise", "Warm light hitting the apartment walls before class."));
            photoButtons.Add(sunrisePhoto);
            gallery.Add(sunrisePhoto);

            Button deskPhoto = null;
            deskPhoto = CreatePhotoTile("Desk", new Color(0.34f, 0.71f, 0.99f, 1f), () => SelectPhoto(deskPhoto, "Desk", "Messy enough to feel real, clean enough to function."));
            photoButtons.Add(deskPhoto);
            gallery.Add(deskPhoto);

            Button streetPhoto = null;
            streetPhoto = CreatePhotoTile("Street", new Color(0.26f, 0.82f, 0.67f, 1f), () => SelectPhoto(streetPhoto, "Street", "Morning traffic already moving outside."));
            photoButtons.Add(streetPhoto);
            gallery.Add(streetPhoto);

            Button windowPhoto = null;
            windowPhoto = CreatePhotoTile("Window", new Color(0.91f, 0.46f, 0.78f, 1f), () => SelectPhoto(windowPhoto, "Window", "Soft light and a little too much reflection."));
            photoButtons.Add(windowPhoto);
            gallery.Add(windowPhoto);

            Button hallPhoto = null;
            hallPhoto = CreatePhotoTile("Hall", new Color(0.85f, 0.80f, 0.42f, 1f), () => SelectPhoto(hallPhoto, "Hall", "The exit route to class, captured at the right angle."));
            photoButtons.Add(hallPhoto);
            gallery.Add(hallPhoto);

            Button posterPhoto = null;
            posterPhoto = CreatePhotoTile("Poster", new Color(0.56f, 0.53f, 0.98f, 1f), () => SelectPhoto(posterPhoto, "Poster", "A wall detail that makes the room feel inhabited."));
            photoButtons.Add(posterPhoto);
            gallery.Add(posterPhoto);

            SelectPhoto(sunrisePhoto, "Sunrise", "Warm light hitting the apartment walls before class.");

            return root;
        }

        private VisualElement BuildSettingsContent()
        {
            var root = CreatePanelRoot();
            root.Add(CreateSectionHeader("Settings", "Small adjustments. Nothing that ruins the vibe."));

            var bodyScroll = CreateScrollPane("laptop-static-scroll", "laptop-static-scroll__content");
            root.Add(bodyScroll);

            var settingsGrid = new VisualElement();
            settingsGrid.AddToClassList("laptop-settings-grid");
            bodyScroll.Add(settingsGrid);

            settingsGrid.Add(CreateSettingsSection("Focus Mode", "Reduce noise and hide extra badges.", new[]
            {
                CreateToggleSetting("Quiet notifications", true),
                CreateToggleSetting("Soft wallpaper", true),
                CreateToggleSetting("Dock magnification", true)
            }));

            settingsGrid.Add(CreateSettingsSection("Display", "Tweak the notebook look without breaking it.", new[]
            {
                CreateSliderSetting("Brightness", 0.68f),
                CreateSliderSetting("Accent", 0.72f),
                CreateSliderSetting("Wallpaper tone", 0.54f)
            }));

            settingsGrid.Add(CreateSettingsSection("System", "Useful buttons that feel believable.", new[]
            {
                CreateActionSetting("Clear Notifications", () => ClearNotifications()),
                CreateActionSetting("Show Launchpad", () => ToggleLaunchpad()),
                CreateActionSetting("Open Calendar", () => OpenApp(CalendarWindowId))
            }));

            return root;
        }

        private VisualElement BuildArcadeContent()
        {
            var root = CreatePanelRoot();
            root.Add(CreateSectionHeader("Arcade", "A couple of tiny distractions hidden inside the laptop."));

            var bodyScroll = CreateScrollPane("laptop-static-scroll", "laptop-static-scroll__content");
            root.Add(bodyScroll);

            var tabs = new VisualElement();
            tabs.AddToClassList("laptop-tab-strip");
            bodyScroll.Add(tabs);

            var gameHost = new VisualElement();
            gameHost.AddToClassList("laptop-arcade-host");
            bodyScroll.Add(gameHost);

            Button snakeTab = null;
            Button memoryTab = null;

            snakeTab = new Button(() =>
            {
                ActivateArcadeTab(ArcadeTab.Snake, gameHost, snakeTab, memoryTab);
            });
            snakeTab.text = "Snake";
            snakeTab.AddToClassList("laptop-tab-button");
            WireHoverState(snakeTab);
            tabs.Add(snakeTab);

            memoryTab = new Button(() =>
            {
                ActivateArcadeTab(ArcadeTab.Memory, gameHost, snakeTab, memoryTab);
            });
            memoryTab.text = "Memory Match";
            memoryTab.AddToClassList("laptop-tab-button");
            WireHoverState(memoryTab);
            tabs.Add(memoryTab);

            ActivateArcadeTab(ArcadeTab.Snake, gameHost, snakeTab, memoryTab);
            return root;
        }

        private void ActivateArcadeTab(ArcadeTab tab, VisualElement host, Button snakeTab, Button memoryTab)
        {
            host.Clear();

            if (snakeTab != null)
            {
                snakeTab.EnableInClassList("is-active", tab == ArcadeTab.Snake);
            }

            if (memoryTab != null)
            {
                memoryTab.EnableInClassList("is-active", tab == ArcadeTab.Memory);
            }

            switch (tab)
            {
                case ArcadeTab.Snake:
                    snakeGameController = BuildSnakeGame(host);
                    break;
                case ArcadeTab.Memory:
                    memoryMatchGameController = BuildMemoryGame(host);
                    break;
            }
        }

        private SnakeGameController BuildSnakeGame(VisualElement host)
        {
            var container = new VisualElement();
            container.AddToClassList("laptop-game-panel");
            host.Add(container);

            var header = new VisualElement();
            header.AddToClassList("laptop-game-header");
            container.Add(header);
            header.Add(CreateStyledLabel("Snake", "laptop-game-header__title"));
            header.Add(CreateStyledLabel("Use arrow keys or the on-screen controls.", "laptop-game-header__subtitle"));

            var scoreLabel = new Label("Score: 0");
            scoreLabel.AddToClassList("laptop-game-score");
            container.Add(scoreLabel);

            var grid = new VisualElement();
            grid.AddToClassList("laptop-snake-grid");
            container.Add(grid);

            var cells = new List<VisualElement>();
            for (var row = 0; row < 12; row++)
            {
                for (var column = 0; column < 12; column++)
                {
                    var cell = new VisualElement();
                    cell.AddToClassList("laptop-snake-cell");
                    grid.Add(cell);
                    cells.Add(cell);
                }
            }

            var controls = new VisualElement();
            controls.AddToClassList("laptop-snake-controls");
            container.Add(controls);

            var controller = new SnakeGameController(this, container, cells, scoreLabel);
            AddSnakeControls(controls, controller);
            controller.Render();
            return controller;
        }

        private void AddSnakeControls(VisualElement controls, SnakeGameController controller)
        {
            controls.Add(CreateSnakeButton("Up", () => controller.RequestDirection(SnakeDirection.Up)));
            controls.Add(CreateSnakeButton("Left", () => controller.RequestDirection(SnakeDirection.Left)));
            controls.Add(CreateSnakeButton("Down", () => controller.RequestDirection(SnakeDirection.Down)));
            controls.Add(CreateSnakeButton("Right", () => controller.RequestDirection(SnakeDirection.Right)));
            controls.Add(CreateSnakeButton("Restart", controller.Restart));
        }

        private Button CreateSnakeButton(string label, Action onClick)
        {
            var button = new Button(() => onClick?.Invoke());
            button.text = label;
            button.AddToClassList("laptop-snake-button");
            WireHoverState(button);
            return button;
        }

        private MemoryMatchGameController BuildMemoryGame(VisualElement host)
        {
            var container = new VisualElement();
            container.AddToClassList("laptop-game-panel");
            host.Add(container);

            var header = new VisualElement();
            header.AddToClassList("laptop-game-header");
            container.Add(header);
            header.Add(CreateStyledLabel("Memory Match", "laptop-game-header__title"));
            header.Add(CreateStyledLabel("Match the cards before the school bell does.", "laptop-game-header__subtitle"));

            var scoreLabel = new Label("Matches: 0");
            scoreLabel.AddToClassList("laptop-game-score");
            container.Add(scoreLabel);

            var grid = new VisualElement();
            grid.AddToClassList("laptop-memory-grid");
            container.Add(grid);

            var controller = new MemoryMatchGameController(this, container, grid, scoreLabel);
            controller.Render();
            return controller;
        }

        private VisualElement CreatePanelRoot()
        {
            var panel = new VisualElement();
            panel.AddToClassList("laptop-panel-root");
            panel.style.flexGrow = 1f;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.paddingLeft = 24f;
            panel.style.paddingRight = 24f;
            panel.style.paddingTop = 18f;
            panel.style.paddingBottom = 22f;
            return panel;
        }

        private ScrollView CreateScrollPane(string rootClassName, string contentClassName = null)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("laptop-scroll-pane");
            if (!string.IsNullOrWhiteSpace(rootClassName))
            {
                scroll.AddToClassList(rootClassName);
            }

            if (!string.IsNullOrWhiteSpace(contentClassName))
            {
                scroll.contentContainer.AddToClassList(contentClassName);
            }

            scroll.style.flexGrow = 1f;
            scroll.style.flexShrink = 1f;
            return scroll;
        }

        private VisualElement CreateSectionHeader(string title, string subtitle)
        {
            var header = new VisualElement();
            header.AddToClassList("laptop-section-header");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("laptop-section-header__title");
            IgnorePicking(titleLabel);
            header.Add(titleLabel);

            var subtitleLabel = new Label(subtitle);
            subtitleLabel.AddToClassList("laptop-section-header__subtitle");
            IgnorePicking(subtitleLabel);
            header.Add(subtitleLabel);

            return header;
        }

        private VisualElement CreateSidebar(string title)
        {
            var sidebar = new VisualElement();
            sidebar.AddToClassList("laptop-sidebar");

            var header = new Label(title);
            header.AddToClassList("laptop-sidebar__title");
            IgnorePicking(header);
            sidebar.Add(header);

            return sidebar;
        }

        private Button CreateSidebarItem(string title, Action onClick)
        {
            var chip = new Button(() => onClick?.Invoke());
            chip.text = title;
            chip.AddToClassList("laptop-sidebar__item");
            WireHoverState(chip);
            return chip;
        }

        private Button CreateGridCard(string title, string body, Action onClick)
        {
            var card = new Button(() => onClick?.Invoke());
            card.text = string.Empty;
            card.AddToClassList("laptop-grid-card");
            WireHoverState(card);

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("laptop-grid-card__title");
            IgnorePicking(titleLabel);
            card.Add(titleLabel);

            var bodyLabel = new Label(body);
            bodyLabel.AddToClassList("laptop-grid-card__body");
            IgnorePicking(bodyLabel);
            card.Add(bodyLabel);

            return card;
        }

        private Button CreateMessageBubble(string speaker, string text, bool incoming, bool highlighted, Action onClick)
        {
            var bubble = new Button(() => onClick?.Invoke());
            bubble.text = string.Empty;
            bubble.AddToClassList("laptop-message-bubble");
            bubble.EnableInClassList("is-incoming", incoming);
            bubble.EnableInClassList("is-highlighted", highlighted);
            WireHoverState(bubble);

            var speakerLabel = new Label(speaker);
            speakerLabel.AddToClassList("laptop-message-bubble__speaker");
            IgnorePicking(speakerLabel);
            bubble.Add(speakerLabel);

            var bodyLabel = new Label(text);
            bodyLabel.AddToClassList("laptop-message-bubble__body");
            IgnorePicking(bodyLabel);
            bubble.Add(bodyLabel);
            return bubble;
        }

        private Button CreateTimelineItem(string time, string title, string body, Action onClick)
        {
            var item = new Button(() => onClick?.Invoke());
            item.text = string.Empty;
            item.AddToClassList("laptop-timeline-item");
            WireHoverState(item);

            var timeLabel = new Label(time);
            timeLabel.AddToClassList("laptop-timeline-item__time");
            IgnorePicking(timeLabel);
            item.Add(timeLabel);

            var content = new VisualElement();
            content.AddToClassList("laptop-timeline-item__content");
            IgnorePicking(content);
            item.Add(content);

            content.Add(CreateStyledLabel(title, "laptop-timeline-item__title"));
            content.Add(CreateStyledLabel(body, "laptop-timeline-item__body"));

            return item;
        }

        private Button CreateReminderSummaryCard(Action onClick)
        {
            var card = new Button(() => onClick?.Invoke());
            card.text = string.Empty;
            card.AddToClassList("laptop-summary-card");
            WireHoverState(card);

            card.Add(CreateStyledLabel(reminderTitle, "laptop-summary-card__title"));
            card.Add(CreateStyledLabel(reminderBody, "laptop-summary-card__body"));
            card.Add(CreateStyledLabel("First class is waiting.", "laptop-summary-card__footer"));

            return card;
        }

        private Button CreateNoteCard(string title, string body, Action onClick)
        {
            var card = new Button(() => onClick?.Invoke());
            card.text = string.Empty;
            card.AddToClassList("laptop-note-card");
            WireHoverState(card);
            card.Add(CreateStyledLabel(title, "laptop-note-card__title"));
            card.Add(CreateStyledLabel(body, "laptop-note-card__body"));
            return card;
        }

        private Button CreatePhotoTile(string title, Color accent, Action onClick)
        {
            var tile = new Button(() => onClick?.Invoke());
            tile.text = string.Empty;
            tile.AddToClassList("laptop-photo-tile");
            WireHoverState(tile);

            var preview = new VisualElement();
            preview.AddToClassList("laptop-photo-tile__preview");
            preview.style.backgroundColor = accent;
            IgnorePicking(preview);
            tile.Add(preview);

            tile.Add(CreateStyledLabel(title, "laptop-photo-tile__title"));
            return tile;
        }

        private Button CreateMiniStat(string title, string value, Action onClick)
        {
            var stat = new Button(() => onClick?.Invoke());
            stat.text = string.Empty;
            stat.AddToClassList("laptop-mini-stat");
            WireHoverState(stat);
            stat.Add(CreateStyledLabel(title, "laptop-mini-stat__title"));
            stat.Add(CreateStyledLabel(value, "laptop-mini-stat__value"));
            return stat;
        }

        private VisualElement CreateFixedDetailPane(VisualElement parent)
        {
            var detail = new VisualElement();
            detail.AddToClassList("laptop-detail-pane");
            detail.style.width = 244f;
            detail.style.flexGrow = 0f;
            parent.Add(detail);
            return detail;
        }

        private PreviewCardRefs CreatePreviewCard(VisualElement parent, string title, string body, string footer)
        {
            var card = new VisualElement();
            card.AddToClassList("laptop-summary-card");
            card.AddToClassList("laptop-preview-card");
            IgnorePicking(card);
            parent.Add(card);

            var titleLabel = CreateStyledLabel(title, "laptop-summary-card__title");
            var bodyLabel = CreateStyledLabel(body, "laptop-summary-card__body");
            var footerLabel = CreateStyledLabel(footer, "laptop-summary-card__footer");

            card.Add(titleLabel);
            card.Add(bodyLabel);
            card.Add(footerLabel);
            return new PreviewCardRefs(card, titleLabel, bodyLabel, footerLabel);
        }

        private VisualElement CreateSettingsSection(string title, string body, IEnumerable<VisualElement> controls)
        {
            var section = new VisualElement();
            section.AddToClassList("laptop-settings-section");
            section.Add(CreateStyledLabel(title, "laptop-settings-section__title"));
            section.Add(CreateStyledLabel(body, "laptop-settings-section__body"));

            var controlStack = new VisualElement();
            controlStack.AddToClassList("laptop-settings-section__controls");
            foreach (var control in controls)
            {
                controlStack.Add(control);
            }

            section.Add(controlStack);
            return section;
        }

        private VisualElement CreateToggleSetting(string title, bool value)
        {
            var row = new VisualElement();
            row.AddToClassList("laptop-setting-row");

            var toggle = new Toggle(title);
            toggle.value = value;
            toggle.AddToClassList("laptop-setting-toggle");
            row.Add(toggle);
            return row;
        }

        private VisualElement CreateSliderSetting(string title, float value)
        {
            var row = new VisualElement();
            row.AddToClassList("laptop-setting-row");

            var label = new Label(title);
            label.AddToClassList("laptop-setting-row__label");
            row.Add(label);

            var slider = new Slider(0f, 1f);
            slider.value = value;
            slider.AddToClassList("laptop-setting-slider");
            row.Add(slider);

            return row;
        }

        private VisualElement CreateActionSetting(string title, Action onClick)
        {
            var button = new Button(() => onClick?.Invoke());
            button.text = title;
            button.AddToClassList("laptop-setting-action");
            WireHoverState(button);
            return button;
        }

        private void ToggleWindowMaximize(LaptopWindow window)
        {
            if (window == null)
            {
                return;
            }

            window.IsMaximized = !window.IsMaximized;
            if (window.IsMaximized)
            {
                window.PreviousRect = CaptureWindowRect(window.Root);
                if (!TryGetDesktopViewport(out var viewportWidth, out var viewportHeight))
                {
                    viewportWidth = Mathf.Max(980f, Screen.width * 0.72f);
                    viewportHeight = Mathf.Max(620f, Screen.height * 0.64f);
                }

                window.Root.style.left = 18f;
                window.Root.style.top = 18f;
                window.Root.style.width = Mathf.Max(480f, viewportWidth - 36f);
                window.Root.style.height = Mathf.Max(360f, viewportHeight - 36f);
            }
            else
            {
                ApplyWindowRect(window.Root, window.PreviousRect);
            }

            BringWindowToFront(window);
        }

        private void MinimizeWindow(LaptopWindow window)
        {
            if (window == null)
            {
                return;
            }

            window.Root.style.display = DisplayStyle.None;
            if (string.Equals(activeAppId, window.AppId, StringComparison.OrdinalIgnoreCase))
            {
                activeAppId = DefaultDesktopLabel;
                UpdateMenuTitle();
            }

            UpdateWindowStageVisibility();
            RefreshDesktopLayering();
            UpdateReminderCard();
        }

        private void CloseWindow(LaptopWindow window)
        {
            if (window == null)
            {
                return;
            }

            window.Root.style.display = DisplayStyle.None;
            if (string.Equals(activeAppId, window.AppId, StringComparison.OrdinalIgnoreCase))
            {
                activeAppId = DefaultDesktopLabel;
                UpdateMenuTitle();
            }

            UpdateWindowStageVisibility();
            RefreshDesktopLayering();
            UpdateReminderCard();
        }

        private void ShowWindow(LaptopWindow window)
        {
            if (window == null)
            {
                return;
            }

            if (window.Root.style.display == DisplayStyle.None)
            {
                window.Root.style.display = DisplayStyle.Flex;
            }

            ClampWindowToViewport(window);
            UpdateWindowStageVisibility();
            RefreshDesktopLayering();
            UpdateReminderCard();
            BringWindowToFront(window);
        }

        private void BringWindowToFront(LaptopWindow window)
        {
            if (window == null || window.Root == null)
            {
                return;
            }

            window.Root.BringToFront();
            window.Root.BringToFront();
        }

        private void RegisterWindowInteractions(LaptopWindow window)
        {
            if (window == null || window.Root == null)
            {
                return;
            }

            Vector2 dragOffset = Vector2.zero;
            Vector2 resizeStart = Vector2.zero;
            Rect initialRect = default;
            bool dragging = false;
            bool resizing = false;

            window.Root.RegisterCallback<PointerDownEvent>(evt =>
            {
                dragging = false;
                resizing = false;
                BringWindowToFront(window);
            });

            window.Header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                if (evt.target is VisualElement target && IsDescendantOf(window.Controls, target))
                {
                    return;
                }

                dragging = true;
                resizing = false;
                var pointerPosition = new Vector2(evt.position.x, evt.position.y);
                dragOffset = pointerPosition - new Vector2(GetLeft(window.Root), GetTop(window.Root));
                window.Header.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            window.Header.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging || !window.Header.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                var nextPosition = new Vector2(evt.position.x, evt.position.y) - dragOffset;
                window.Root.style.left = nextPosition.x;
                window.Root.style.top = nextPosition.y;
            });

            window.Header.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (dragging && window.Header.HasPointerCapture(evt.pointerId))
                {
                    window.Header.ReleasePointer(evt.pointerId);
                }

                dragging = false;
            });

            window.Header.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);

            window.ResizeHandle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                resizing = true;
                dragging = false;
                resizeStart = evt.position;
                initialRect = CaptureWindowRect(window.Root);
                window.ResizeHandle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            window.ResizeHandle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing || !window.ResizeHandle.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                var delta = new Vector2(evt.position.x, evt.position.y) - resizeStart;
                window.Root.style.width = Mathf.Max(460f, initialRect.width + delta.x);
                window.Root.style.height = Mathf.Max(360f, initialRect.height + delta.y);
            });

            window.ResizeHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (resizing && window.ResizeHandle.HasPointerCapture(evt.pointerId))
                {
                    window.ResizeHandle.ReleasePointer(evt.pointerId);
                }

                resizing = false;
            });

            window.ResizeHandle.RegisterCallback<PointerCaptureOutEvent>(_ => resizing = false);
        }

        private static float GetLeft(VisualElement element)
        {
            return element.resolvedStyle.left;
        }

        private static float GetTop(VisualElement element)
        {
            return element.resolvedStyle.top;
        }

        private static Rect CaptureWindowRect(VisualElement element)
        {
            return new Rect(element.resolvedStyle.left, element.resolvedStyle.top, element.resolvedStyle.width, element.resolvedStyle.height);
        }

        private static void ApplyWindowRect(VisualElement element, Rect rect)
        {
            element.style.left = rect.x;
            element.style.top = rect.y;
            element.style.width = rect.width;
            element.style.height = rect.height;
        }

        private void ShowShellImmediate()
        {
            if (root != null)
            {
                root.style.display = DisplayStyle.Flex;
                root.pickingMode = PickingMode.Position;
            }

            if (shell != null)
            {
                shell.style.display = DisplayStyle.Flex;
            }
            UpdateMenuTitle();
            UpdateReminderCard();
            RefreshDockBadges();
        }

        private void HideShellImmediate()
        {
            if (shell != null)
            {
                shell.style.display = DisplayStyle.None;
            }

            if (root != null)
            {
                root.style.display = DisplayStyle.None;
                root.pickingMode = PickingMode.Ignore;
            }
        }

        private void FocusDesktop()
        {
            activeAppId = DefaultDesktopLabel;
            UpdateMenuTitle();
            SetDisplay(launchpadOverlay, DisplayStyle.None);
            launchpadVisible = false;
            RefreshDesktopLayering();
        }

        private void UpdateMenuTitle()
        {
            if (menuTitleLabel == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(activeAppId) || string.Equals(activeAppId, DefaultDesktopLabel, StringComparison.OrdinalIgnoreCase))
            {
                menuTitleLabel.text = DefaultDesktopLabel;
                return;
            }

            if (manifests.TryGetValue(activeAppId, out var manifest))
            {
                menuTitleLabel.text = manifest.Title;
                return;
            }

            menuTitleLabel.text = DefaultDesktopLabel;
        }

        private void UpdateClockLabel()
        {
            if (menuClockButton == null)
            {
                return;
            }

            menuClockButton.text = DateTime.Now.ToString("h:mm tt");
        }

        private void UpdateReminderCard()
        {
            if (reminderCard == null)
            {
                return;
            }

            var hasVisibleWindow = openWindows.Values.Any(window => window?.Root != null && window.Root.style.display != DisplayStyle.None);
            reminderCard.style.display = (!hasViewedClassReminder && !hasVisibleWindow) ? DisplayStyle.Flex : DisplayStyle.None;
            reminderOpenButton?.SetEnabled(!hasViewedClassReminder);
            RefreshDockBadges();
        }

        private void RefreshDockBadges()
        {
            for (var index = 0; index < dockButtons.Count; index++)
            {
                var dockButton = dockButtons[index];
                if (dockButton == null)
                {
                    continue;
                }

                var showBadge = !hasViewedClassReminder &&
                                (string.Equals(dockButton.AppId, MessagesWindowId, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(dockButton.AppId, CalendarWindowId, StringComparison.OrdinalIgnoreCase));
                dockButton.Badge.style.display = showBadge ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void RefreshLaunchpadFilter()
        {
            var filter = launchpadSearchField != null ? launchpadSearchField.value.Trim() : string.Empty;
            launchpadFilterMatches.Clear();

            foreach (var tile in launchpadTiles)
            {
                if (tile == null)
                {
                    continue;
                }

                var manifest = manifests[tile.AppId];
                var match = string.IsNullOrWhiteSpace(filter) ||
                            manifest.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            manifest.Description.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                tile.Root.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
                if (match)
                {
                    launchpadFilterMatches.Add(tile.AppId);
                }
            }
        }

        private void ApplyResponsiveLayout()
        {
            if (root == null)
            {
                return;
            }

            var rootWidth = root.resolvedStyle.width;
            var rootHeight = root.resolvedStyle.height;
            if (rootWidth <= 0f || rootHeight <= 0f)
            {
                return;
            }

            var compact = rootWidth < 1360f || rootHeight < 820f;

            if (desktopShortcutContainer != null)
            {
                desktopShortcutContainer.style.left = compact ? 16f : 24f;
                desktopShortcutContainer.style.top = compact ? 18f : 24f;
                desktopShortcutContainer.style.width = compact ? 120f : 136f;
            }

            if (reminderCard != null)
            {
                reminderCard.style.top = compact ? 56f : 66f;
                reminderCard.style.right = compact ? 16f : 22f;
                reminderCard.style.width = Mathf.Clamp(rootWidth * (compact ? 0.24f : 0.22f), 236f, 320f);
            }

            if (dockTrack != null)
            {
                dockTrack.style.left = compact ? 22f : 44f;
                dockTrack.style.right = compact ? 22f : 44f;
                dockTrack.style.bottom = compact ? 8f : 12f;
            }

            if (launchpadPanel != null)
            {
                launchpadPanel.style.width = Mathf.Clamp(rootWidth - (compact ? 56f : 96f), 560f, 920f);
                launchpadPanel.style.maxHeight = Mathf.Clamp(rootHeight - (compact ? 112f : 156f), 440f, 760f);
            }

            if (launchpadSearchField != null)
            {
                launchpadSearchField.style.width = Mathf.Clamp(rootWidth * 0.20f, 208f, 296f);
            }

            foreach (var window in openWindows.Values)
            {
                ClampWindowToViewport(window);
            }
        }

        private Rect GetDefaultWindowRect()
        {
            if (!TryGetDesktopViewport(out var viewportWidth, out var viewportHeight))
            {
                return new Rect(120f, 80f, 760f, 520f);
            }

            var width = Mathf.Clamp(viewportWidth * 0.74f, 720f, 1180f);
            var height = Mathf.Clamp(viewportHeight * 0.84f, 520f, 860f);
            width = Mathf.Min(width, viewportWidth - 24f);
            height = Mathf.Min(height, viewportHeight - 24f);

            var left = Mathf.Max(14f, (viewportWidth - width) * 0.5f);
            var top = Mathf.Max(12f, Mathf.Min(44f, (viewportHeight - height) * 0.1f));
            return new Rect(left, top, width, height);
        }

        private void ClampWindowToViewport(LaptopWindow window)
        {
            if (window?.Root == null || !TryGetDesktopViewport(out var viewportWidth, out var viewportHeight))
            {
                return;
            }

            var minWidth = Mathf.Min(560f, Mathf.Max(340f, viewportWidth - 28f));
            var minHeight = Mathf.Min(440f, Mathf.Max(280f, viewportHeight - 28f));
            var maxWidth = Mathf.Max(minWidth, viewportWidth - 20f);
            var maxHeight = Mathf.Max(minHeight, viewportHeight - 20f);

            var width = Mathf.Clamp(window.Root.resolvedStyle.width, minWidth, maxWidth);
            var height = Mathf.Clamp(window.Root.resolvedStyle.height, minHeight, maxHeight);
            var left = Mathf.Clamp(GetLeft(window.Root), 14f, Mathf.Max(14f, viewportWidth - width - 14f));
            var top = Mathf.Clamp(GetTop(window.Root), 14f, Mathf.Max(14f, viewportHeight - height - 14f));

            window.Root.style.width = width;
            window.Root.style.height = height;
            window.Root.style.left = left;
            window.Root.style.top = top;
        }

        private bool TryGetDesktopViewport(out float width, out float height)
        {
            width = 0f;
            height = 0f;

            if (desktopStage == null)
            {
                return false;
            }

            width = desktopStage.resolvedStyle.width;
            height = desktopStage.resolvedStyle.height;
            return width > 0f && height > 0f;
        }

        private void UpdateToasts(float deltaTime)
        {
            if (toasts.Count == 0)
            {
                return;
            }

            for (var index = toasts.Count - 1; index >= 0; index--)
            {
                var toast = toasts[index];
                toast.Remaining -= deltaTime;
                toast.Root.style.opacity = Mathf.Clamp01(toast.Remaining / 0.35f);

                if (toast.Remaining <= 0f)
                {
                    toast.Root.RemoveFromHierarchy();
                    toasts.RemoveAt(index);
                }
            }
        }

        private void ShowNotification(string title, string body, float durationSeconds)
        {
            if (notificationStack == null)
            {
                return;
            }

            var toast = new VisualElement();
            toast.AddToClassList("laptop-toast");

            var toastTitle = new Label(title);
            toastTitle.AddToClassList("laptop-toast__title");
            toast.Add(toastTitle);

            var toastBody = new Label(body);
            toastBody.AddToClassList("laptop-toast__body");
            toast.Add(toastBody);

            notificationStack.Add(toast);
            toasts.Add(new LaptopToast(toast, Math.Max(0.75f, durationSeconds)));
        }

        private void ClearNotifications()
        {
            for (var index = 0; index < toasts.Count; index++)
            {
                toasts[index].Root.RemoveFromHierarchy();
            }

            toasts.Clear();
        }

        private void UpdateWindowStageVisibility()
        {
            if (windowStage == null)
            {
                return;
            }

            var hasVisibleWindow = openWindows.Values.Any(window => window?.Root != null && window.Root.style.display != DisplayStyle.None);
            windowStage.style.display = hasVisibleWindow ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshDesktopLayering()
        {
            desktopShortcutContainer?.SendToBack();
            reminderCard?.BringToFront();
            windowStage?.BringToFront();
            notificationStack?.BringToFront();
            launchpadOverlay?.BringToFront();
        }

        private void SetDisplay(VisualElement element, DisplayStyle displayStyle)
        {
            if (element != null)
            {
                element.style.display = displayStyle;
            }
        }

        private bool IsReminderApp(string appId)
        {
            return string.Equals(appId, MessagesWindowId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(appId, CalendarWindowId, StringComparison.OrdinalIgnoreCase);
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!isOpen || !built)
            {
                return;
            }
        }

        private void BuildOrReplaceWindowContent(string appId)
        {
            if (openWindows.TryGetValue(appId, out var window))
            {
                BuildWindowContent(window, appId);
            }
        }

        private void HandleVisibilityChanged(bool visible)
        {
            if (!visible)
            {
                return;
            }

            UpdateClockLabel();
            UpdateReminderCard();
            RefreshDockBadges();
        }

        private void OnValidate()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            if (panelSettings != null && uiDocument != null)
            {
                uiDocument.panelSettings = panelSettings;
            }
        }

        private void OnDisable()
        {
            RestoreCursorState();
            HideShellImmediate();
        }

        private void EnsureCursorForLaptop()
        {
            if (cachedCursorState)
            {
                return;
            }

            previousCursorLockState = UnityEngine.Cursor.lockState;
            previousCursorVisible = UnityEngine.Cursor.visible;
            cachedCursorState = true;

            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        private void RestoreCursorState()
        {
            if (!cachedCursorState)
            {
                return;
            }

            UnityEngine.Cursor.lockState = previousCursorLockState;
            UnityEngine.Cursor.visible = previousCursorVisible;
            cachedCursorState = false;
        }

        private static VisualElement CreateOrb(string name, string className)
        {
            var orb = new VisualElement { name = name };
            orb.AddToClassList(className);
            orb.pickingMode = PickingMode.Ignore;
            return orb;
        }

        private static Button CreateMenuChip(string text, Action onClick)
        {
            var button = new Button(() => onClick?.Invoke());
            button.text = text;
            button.AddToClassList("laptop-menu-chip");
            WireHoverState(button);
            return button;
        }

        private static Label CreateStyledLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            IgnorePicking(label);
            return label;
        }

        private static void SetText(VisualElement element, string value)
        {
            if (element is Label label)
            {
                label.text = value;
            }
        }

        private static void UpdateSelection(IEnumerable<Button> buttons, Button selected)
        {
            if (buttons == null)
            {
                return;
            }

            foreach (var button in buttons)
            {
                if (button == null)
                {
                    continue;
                }

                button.EnableInClassList("is-selected", button == selected);
                button.RemoveFromClassList("is-pressed");
            }
        }

        private static bool IsDescendantOf(VisualElement ancestor, VisualElement element)
        {
            while (element != null)
            {
                if (element == ancestor)
                {
                    return true;
                }

                element = element.parent;
            }

            return false;
        }

        private static void WireHoverState(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.RegisterCallback<PointerEnterEvent>(_ => button.AddToClassList("is-hovered"));
            button.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                button.RemoveFromClassList("is-hovered");
                button.RemoveFromClassList("is-pressed");
            });
            button.RegisterCallback<PointerDownEvent>(_ => button.AddToClassList("is-pressed"));
            button.RegisterCallback<PointerUpEvent>(_ => button.RemoveFromClassList("is-pressed"));
        }

        private static void IgnorePicking(VisualElement element)
        {
            if (element != null)
            {
                element.pickingMode = PickingMode.Ignore;
            }
        }

        private sealed class LaptopAppManifest
        {
            public LaptopAppManifest(string id, string title, string shortLabel, Color color, string description, string iconResourceName, bool showInLaunchpad = true)
            {
                Id = id;
                Title = title;
                ShortLabel = shortLabel;
                Color = color;
                Description = description;
                IconResourceName = iconResourceName;
                ShowInLaunchpad = showInLaunchpad;
            }

            public string Id { get; }
            public string Title { get; }
            public string ShortLabel { get; }
            public Color Color { get; }
            public string Description { get; }
            public string IconResourceName { get; }
            public bool ShowInLaunchpad { get; }
        }

        private sealed class LaptopWindow
        {
            public LaptopWindow(string appId, string title)
            {
                AppId = appId;
                Title = title;
            }

            public string AppId { get; }
            public string Title { get; }
            public VisualElement Root { get; set; }
            public VisualElement Header { get; set; }
            public VisualElement Controls { get; set; }
            public VisualElement Content { get; set; }
            public Label TitleLabel { get; set; }
            public VisualElement ResizeHandle { get; set; }
            public bool IsMaximized { get; set; }
            public Rect PreviousRect { get; set; }
        }

        private sealed class DesktopShortcut
        {
            public DesktopShortcut(string appId, VisualElement root, Image icon, Label title, Label subtitle)
            {
                AppId = appId;
                Root = root;
                Icon = icon;
                Title = title;
                Subtitle = subtitle;
            }

            public string AppId { get; }
            public VisualElement Root { get; }
            public Image Icon { get; }
            public Label Title { get; }
            public Label Subtitle { get; }
        }

        private sealed class PreviewCardRefs
        {
            public PreviewCardRefs(VisualElement root, Label title, Label body, Label footer)
            {
                Root = root;
                Title = title;
                Body = body;
                Footer = footer;
            }

            public VisualElement Root { get; }
            public Label Title { get; }
            public Label Body { get; }
            public Label Footer { get; }
        }

        private sealed class DockButton
        {
            public DockButton(string appId, Button root, Image icon, Label badge)
            {
                AppId = appId;
                Root = root;
                Icon = icon;
                Badge = badge;
            }

            public string AppId { get; }
            public Button Root { get; }
            public Image Icon { get; }
            public Label Badge { get; }
        }

        private sealed class LaunchpadTile
        {
            public LaunchpadTile(string appId, Button root, Image icon, Label title, Label description)
            {
                AppId = appId;
                Root = root;
                Icon = icon;
                Title = title;
                Description = description;
            }

            public string AppId { get; }
            public Button Root { get; }
            public Image Icon { get; }
            public Label Title { get; }
            public Label Description { get; }
        }

        private sealed class LaptopToast
        {
            public LaptopToast(VisualElement root, float remaining)
            {
                Root = root;
                Remaining = remaining;
            }

            public VisualElement Root { get; }
            public float Remaining { get; set; }
        }

        private enum ArcadeTab
        {
            Snake,
            Memory
        }

        private enum SnakeDirection
        {
            Up,
            Down,
            Left,
            Right
        }

        private sealed class SnakeGameController
        {
            private const int GridSize = 12;
            private const float MoveInterval = 0.26f;
            private readonly LaptopDesktopSystem owner;
            private readonly List<VisualElement> cells;
            private readonly Label scoreLabel;
            private readonly List<Vector2Int> snake = new List<Vector2Int>();
            private readonly System.Random random = new System.Random();
            private Vector2Int food;
            private SnakeDirection currentDirection = SnakeDirection.Right;
            private SnakeDirection requestedDirection = SnakeDirection.Right;
            private float moveAccumulator;
            private int score;
            private bool gameOver;
            private bool active = true;

            public SnakeGameController(LaptopDesktopSystem owner, VisualElement host, List<VisualElement> cells, Label scoreLabel)
            {
                this.owner = owner;
                this.cells = cells;
                this.scoreLabel = scoreLabel;
                Restart();
            }

            public void OnActivated()
            {
                active = true;
            }

            public void Restart()
            {
                snake.Clear();
                snake.Add(new Vector2Int(5, 6));
                snake.Add(new Vector2Int(4, 6));
                snake.Add(new Vector2Int(3, 6));
                currentDirection = SnakeDirection.Right;
                requestedDirection = SnakeDirection.Right;
                score = 0;
                gameOver = false;
                active = true;
                SpawnFood();
                Render();
            }

            public void RequestDirection(SnakeDirection direction)
            {
                if (IsOpposite(currentDirection, direction))
                {
                    return;
                }

                requestedDirection = direction;
            }

            public void Tick(float deltaTime)
            {
                if (!active || gameOver)
                {
                    return;
                }

                moveAccumulator += deltaTime;
                if (moveAccumulator < MoveInterval)
                {
                    return;
                }

                moveAccumulator = 0f;
                Step();
            }

            public void Render()
            {
                for (var index = 0; index < cells.Count; index++)
                {
                    var cell = cells[index];
                    var position = new Vector2Int(index % GridSize, index / GridSize);
                    cell.RemoveFromClassList("is-snake");
                    cell.RemoveFromClassList("is-food");
                    cell.RemoveFromClassList("is-head");

                    if (snake.Contains(position))
                    {
                        cell.AddToClassList("is-snake");
                    }

                    if (snake.Count > 0 && snake[0] == position)
                    {
                        cell.AddToClassList("is-head");
                    }

                    if (food == position)
                    {
                        cell.AddToClassList("is-food");
                    }
                }

                scoreLabel.text = gameOver ? $"Game Over - Score: {score}" : $"Score: {score}";
            }

            private void Step()
            {
                currentDirection = requestedDirection;
                var nextHead = snake[0];
                switch (currentDirection)
                {
                    case SnakeDirection.Up:
                        nextHead.y -= 1;
                        break;
                    case SnakeDirection.Down:
                        nextHead.y += 1;
                        break;
                    case SnakeDirection.Left:
                        nextHead.x -= 1;
                        break;
                    case SnakeDirection.Right:
                        nextHead.x += 1;
                        break;
                }

                if (nextHead.x < 0 || nextHead.y < 0 || nextHead.x >= GridSize || nextHead.y >= GridSize || snake.Contains(nextHead))
                {
                    gameOver = true;
                    owner.ShowNotification("Snake", "You ran into the edge of the notebook.", 3f);
                    Render();
                    return;
                }

                snake.Insert(0, nextHead);
                if (nextHead == food)
                {
                    score++;
                    SpawnFood();
                }
                else
                {
                    snake.RemoveAt(snake.Count - 1);
                }

                Render();
            }

            private void SpawnFood()
            {
                Vector2Int next;
                do
                {
                    next = new Vector2Int(random.Next(0, GridSize), random.Next(0, GridSize));
                }
                while (snake.Contains(next));

                food = next;
            }

            private static bool IsOpposite(SnakeDirection a, SnakeDirection b)
            {
                return (a == SnakeDirection.Up && b == SnakeDirection.Down) ||
                       (a == SnakeDirection.Down && b == SnakeDirection.Up) ||
                       (a == SnakeDirection.Left && b == SnakeDirection.Right) ||
                       (a == SnakeDirection.Right && b == SnakeDirection.Left);
            }
        }

        private sealed class MemoryMatchGameController
        {
            private readonly LaptopDesktopSystem owner;
            private readonly VisualElement host;
            private readonly VisualElement grid;
            private readonly Label scoreLabel;
            private readonly System.Random random = new System.Random();
            private readonly List<MemoryCard> cards = new List<MemoryCard>();
            private readonly List<MemoryCardData> deck = new List<MemoryCardData>();
            private MemoryCard firstSelection;
            private MemoryCard secondSelection;
            private float mismatchTimer = -1f;
            private int matches;

            public MemoryMatchGameController(LaptopDesktopSystem owner, VisualElement host, VisualElement grid, Label scoreLabel)
            {
                this.owner = owner;
                this.host = host;
                this.grid = grid;
                this.scoreLabel = scoreLabel;
                BuildDeck();
            }

            public void OnActivated()
            {
            }

            public void Render()
            {
                grid.Clear();
                cards.Clear();
                BuildDeck();

                for (var index = 0; index < deck.Count; index++)
                {
                    var data = deck[index];
                    var card = CreateCard(index, data);
                    cards.Add(card);
                    grid.Add(card.Root);
                }

                RefreshScore();
            }

            public void Tick(float deltaTime)
            {
                if (mismatchTimer < 0f)
                {
                    return;
                }

                mismatchTimer -= deltaTime;
                if (mismatchTimer > 0f)
                {
                    return;
                }

                mismatchTimer = -1f;
                HideSelection(firstSelection);
                HideSelection(secondSelection);
                firstSelection = null;
                secondSelection = null;
            }

            private void BuildDeck()
            {
                deck.Clear();
                var pairs = new[] { "A", "B", "C", "D", "E", "F", "G", "H" };
                for (var index = 0; index < pairs.Length; index++)
                {
                    deck.Add(new MemoryCardData(pairs[index], index));
                    deck.Add(new MemoryCardData(pairs[index], index));
                }

                for (var index = deck.Count - 1; index > 0; index--)
                {
                    var swapIndex = random.Next(index + 1);
                    (deck[index], deck[swapIndex]) = (deck[swapIndex], deck[index]);
                }
            }

            private MemoryCard CreateCard(int index, MemoryCardData data)
            {
                var button = new Button(() => HandleCardClicked(index));
                button.AddToClassList("laptop-memory-card");
                WireHoverState(button);

                var front = new Label(data.Symbol);
                front.AddToClassList("laptop-memory-card__front");
                IgnorePicking(front);
                button.Add(front);

                var back = new Label("?");
                back.AddToClassList("laptop-memory-card__back");
                IgnorePicking(back);
                button.Add(back);

                return new MemoryCard(index, data, button, front, back);
            }

            private void HandleCardClicked(int index)
            {
                if (mismatchTimer >= 0f)
                {
                    return;
                }

                var card = cards[index];
                if (card.Matched || card.Revealed)
                {
                    return;
                }

                card.Revealed = true;
                ApplyReveal(card, true);

                if (firstSelection == null)
                {
                    firstSelection = card;
                    return;
                }

                secondSelection = card;
                if (firstSelection.Data.Symbol == secondSelection.Data.Symbol)
                {
                    firstSelection.Matched = true;
                    secondSelection.Matched = true;
                    matches++;
                    firstSelection = null;
                    secondSelection = null;
                    RefreshScore();
                    owner.ShowNotification("Memory Match", $"Matched pair {matches} of 8.", 2.2f);
                    if (matches >= 8)
                    {
                        owner.ShowNotification("Memory Match", "All pairs found.", 3f);
                    }
                }
                else
                {
                    mismatchTimer = 0.65f;
                }
            }

            private void ApplyReveal(MemoryCard card, bool revealed)
            {
                card.Root.EnableInClassList("is-revealed", revealed);
                card.BackLabel.style.display = revealed ? DisplayStyle.None : DisplayStyle.Flex;
                card.FrontLabel.style.display = revealed ? DisplayStyle.Flex : DisplayStyle.None;
            }

            private void HideSelection(MemoryCard card)
            {
                if (card == null || card.Matched)
                {
                    return;
                }

                card.Revealed = false;
                ApplyReveal(card, false);
            }

            private void RefreshScore()
            {
                scoreLabel.text = $"Matches: {matches}/8";
            }

            private sealed class MemoryCardData
            {
                public MemoryCardData(string symbol, int pairIndex)
                {
                    Symbol = symbol;
                    PairIndex = pairIndex;
                }

                public string Symbol { get; }
                public int PairIndex { get; }
            }

            private sealed class MemoryCard
            {
                public MemoryCard(int index, MemoryCardData data, Button root, Label frontLabel, Label backLabel)
                {
                    Index = index;
                    Data = data;
                    Root = root;
                    FrontLabel = frontLabel;
                    BackLabel = backLabel;
                }

                public int Index { get; }
                public MemoryCardData Data { get; }
                public Button Root { get; }
                public Label FrontLabel { get; }
                public Label BackLabel { get; }
                public bool Revealed { get; set; }
                public bool Matched { get; set; }
            }
        }
    }
}
