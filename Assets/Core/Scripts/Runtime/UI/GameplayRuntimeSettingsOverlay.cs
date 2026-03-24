using Blocks.Gameplay.Core.Story;
using ItemInteraction;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Blocks.Gameplay.Core
{
    [DisallowMultipleComponent]
    public sealed class GameplayRuntimeSettingsOverlay : MonoBehaviour
    {
        private const string OverlayObjectName = "GameplayRuntimeSettingsOverlay";
        private const string PrefTouchEnabled = "intro2.settings.touch_controls";
        private const string PrefTouchPlatform = "intro2.settings.touch_platform";
        private const string PrefHighQuality = "intro2.settings.high_quality";

        private static GameplayRuntimeSettingsOverlay s_Instance;

        private enum NeoGlyphKind
        {
            None,
            Settings,
            Jump,
            Sprint,
            Action,
            Touch,
            Quality,
            Close
        }

        private Canvas m_Canvas;
        private RectTransform m_CanvasRect;
        private RectTransform m_TouchRoot;
        private RectTransform m_SettingsButtonRoot;
        private RectTransform m_SettingsPanel;
        private RectTransform m_MovePad;
        private RectTransform m_MoveHandle;
        private RectTransform m_LookZone;
        private Button m_SettingsButton;
        private Button m_InteractButton;
        private Button m_TouchToggleButton;
        private Button m_QualityButton;
        private Text m_TouchToggleLabel;
        private Text m_QualityLabel;

        private CoreInputHandler m_LocalInput;
        private CorePlayerManager m_LocalPlayer;
        private CoreCameraController m_LocalCamera;
        private InteractionDirector m_InteractionDirector;
        private ClassroomPlayerControlLock m_PlayerControlLock;
        private ClassroomStoryConversationPresentationController m_ClassroomConversationPresentation;
        private LabCameraFocusController m_LabConversationFocus;
        private ClassroomSceneIntroCutscene m_ClassroomIntroCutscene;
        private LabShrinkSequenceController m_LabShrinkSequence;
        private LabFinalCutsceneController m_LabFinalCutscene;

        private Vector2 m_MoveVector;
        private Vector2 m_PendingLookInput;
        private Vector2 m_LastLookPointerPosition;
        private int m_MovePointerId = -1;
        private int m_LookPointerId = -1;
        private float m_NextResolveTime;
        private bool m_SettingsOpen;
        private bool m_PrimaryReleaseQueued;
        private bool m_LockedMovementBySettings;
        private bool m_LockedInteractionsBySettings;
        private bool m_TouchControlsEnabled = true;
        private bool m_HighQualityMode;
        private bool m_LastTouchVisibility;
        private bool m_HadMoveInputLastFrame;
        private bool m_ObservedTouchInput;
        private float m_NextEventSystemNormalizeTime;
        private float m_NextPlatformProbeTime;
        private bool m_IsLikelyMobileWebGl;
        private bool m_LastSettingsButtonVisible = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            if (s_Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<GameplayRuntimeSettingsOverlay>(FindObjectsInactive.Include);
            if (existing != null)
            {
                s_Instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                return;
            }

            var overlayObject = new GameObject(OverlayObjectName);
            s_Instance = overlayObject.AddComponent<GameplayRuntimeSettingsOverlay>();
            DontDestroyOnLoad(overlayObject);
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeTouchControlsPreference();
            m_HighQualityMode = PlayerPrefs.GetInt(PrefHighQuality, 1) != 0;

            BuildUi();
            EnsureEventSystem();
            ApplyQualityMode(m_HighQualityMode, persist: false);
            RefreshSettingsLabels();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void Update()
        {
            ResolveRuntimeReferences();
            EnforceSettingsAvailability();
            UpdateSettingsButtonVisibility();
            HandleMenuToggleShortcut();
            RefreshPlatformDetection();
            ObserveTouchInput();
            TryNormalizeEventSystems();
            UpdateTouchControlsVisibility();
            PumpTouchInputs();

            if (m_PrimaryReleaseQueued && m_LocalInput != null)
            {
                m_PrimaryReleaseQueued = false;
                m_LocalInput.InjectPrimaryActionReleased();
            }
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            m_MovePointerId = -1;
            m_LookPointerId = -1;
            m_MoveVector = Vector2.zero;
            m_PendingLookInput = Vector2.zero;
            m_HadMoveInputLastFrame = false;
            m_NextResolveTime = 0f;
            ResolveRuntimeReferences(force: true);
            m_NextEventSystemNormalizeTime = Time.unscaledTime + 0.15f;
            m_NextPlatformProbeTime = 0f;
            RefreshPlatformDetection();

            if (m_SettingsOpen)
            {
                SetSettingsOpen(false);
            }
        }

        private void ResolveRuntimeReferences(bool force = false)
        {
            if (!force && Time.unscaledTime < m_NextResolveTime)
            {
                return;
            }

            m_NextResolveTime = Time.unscaledTime + 0.35f;
            var activeScene = SceneManager.GetActiveScene();

            if (m_LocalInput == null || !m_LocalInput.IsSpawned || !m_LocalInput.IsOwner || !IsInScene(m_LocalInput.gameObject, activeScene))
            {
                m_LocalInput = null;
                var inputs = FindObjectsByType<CoreInputHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (var index = 0; index < inputs.Length; index++)
                {
                    var input = inputs[index];
                    if (input != null && input.IsSpawned && input.IsOwner && IsInScene(input.gameObject, activeScene))
                    {
                        m_LocalInput = input;
                        break;
                    }
                }
            }

            if (m_LocalPlayer == null || !m_LocalPlayer.IsSpawned || !m_LocalPlayer.IsOwner || !IsInScene(m_LocalPlayer.gameObject, activeScene))
            {
                m_LocalPlayer = null;
                var players = FindObjectsByType<CorePlayerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (var index = 0; index < players.Length; index++)
                {
                    var player = players[index];
                    if (player != null && player.IsSpawned && player.IsOwner && IsInScene(player.gameObject, activeScene))
                    {
                        m_LocalPlayer = player;
                        break;
                    }
                }
            }

            if (m_LocalPlayer != null)
            {
                m_LocalCamera = m_LocalPlayer.CoreCamera;
            }
            else
            {
                m_LocalCamera = null;
            }

            if (m_InteractionDirector != null && !IsInScene(m_InteractionDirector.gameObject, activeScene))
            {
                m_InteractionDirector = null;
            }

            m_InteractionDirector ??= FindSceneObject<InteractionDirector>(activeScene, includeInactive: true);

            if (m_PlayerControlLock != null && !IsInScene(m_PlayerControlLock.gameObject, activeScene))
            {
                m_PlayerControlLock = null;
            }

            if (m_ClassroomConversationPresentation != null && !IsInScene(m_ClassroomConversationPresentation.gameObject, activeScene))
            {
                m_ClassroomConversationPresentation = null;
            }

            if (m_LabConversationFocus != null && !IsInScene(m_LabConversationFocus.gameObject, activeScene))
            {
                m_LabConversationFocus = null;
            }

            if (m_ClassroomIntroCutscene != null && !IsInScene(m_ClassroomIntroCutscene.gameObject, activeScene))
            {
                m_ClassroomIntroCutscene = null;
            }

            if (m_LabShrinkSequence != null && !IsInScene(m_LabShrinkSequence.gameObject, activeScene))
            {
                m_LabShrinkSequence = null;
            }

            if (m_LabFinalCutscene != null && !IsInScene(m_LabFinalCutscene.gameObject, activeScene))
            {
                m_LabFinalCutscene = null;
            }

            m_PlayerControlLock ??= FindSceneObject<ClassroomPlayerControlLock>(activeScene, includeInactive: true);
            m_ClassroomConversationPresentation ??= FindSceneObject<ClassroomStoryConversationPresentationController>(activeScene, includeInactive: true);
            m_LabConversationFocus ??= FindSceneObject<LabCameraFocusController>(activeScene, includeInactive: true);
            m_ClassroomIntroCutscene ??= FindSceneObject<ClassroomSceneIntroCutscene>(activeScene, includeInactive: true);
            m_LabShrinkSequence ??= FindSceneObject<LabShrinkSequenceController>(activeScene, includeInactive: true);
            m_LabFinalCutscene ??= FindSceneObject<LabFinalCutsceneController>(activeScene, includeInactive: true);
        }

        private void HandleMenuToggleShortcut()
        {
            bool tabPressed = false;
            bool escapePressed = false;

            if (Keyboard.current != null)
            {
                escapePressed |= Keyboard.current.escapeKey.wasPressedThisFrame;
                tabPressed |= Keyboard.current.tabKey.wasPressedThisFrame;
            }

            escapePressed |= Input.GetKeyDown(KeyCode.Escape);
            tabPressed |= Input.GetKeyDown(KeyCode.Tab);

            if (tabPressed)
            {
                ToggleSettingsPanel();
                return;
            }

            // Esc should only close an already open settings panel.
            if (escapePressed && m_SettingsOpen)
            {
                SetSettingsOpen(false);
            }
        }

        private void UpdateTouchControlsVisibility()
        {
            if (m_TouchRoot == null)
            {
                return;
            }

            bool shouldShow = ShouldShowTouchControls();
            if (m_LastTouchVisibility == shouldShow)
            {
                return;
            }

            m_LastTouchVisibility = shouldShow;
            m_TouchRoot.gameObject.SetActive(shouldShow);

            if (!shouldShow)
            {
                m_MoveVector = Vector2.zero;
                m_PendingLookInput = Vector2.zero;
                if (m_MoveHandle != null)
                {
                    m_MoveHandle.anchoredPosition = Vector2.zero;
                }
            }
        }

        private bool ShouldShowTouchControls()
        {
            if (!m_TouchControlsEnabled || m_SettingsOpen)
            {
                return false;
            }

            return IsTouchControlsRuntimeAllowed();
        }

        private void ObserveTouchInput()
        {
            if (m_ObservedTouchInput)
            {
                return;
            }

            if (Input.touchCount > 0)
            {
                m_ObservedTouchInput = true;
                return;
            }

#if ENABLE_INPUT_SYSTEM
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var touches = touchscreen.touches;
                for (var index = 0; index < touches.Count; index++)
                {
                    if (touches[index].press.isPressed)
                    {
                        m_ObservedTouchInput = true;
                        return;
                    }
                }
            }
#endif
        }

        private void InitializeTouchControlsPreference()
        {
            m_IsLikelyMobileWebGl = IsLikelyMobileWebGlPlatform();
            var platformSignature = BuildTouchPlatformSignature();
            bool detectedDefault = DetectDefaultTouchState();
            bool touchRuntimeAllowed = IsTouchControlsRuntimeAllowed();
            bool hasSavedPreference = PlayerPrefs.HasKey(PrefTouchEnabled);
            var savedPlatformSignature = PlayerPrefs.GetString(PrefTouchPlatform, string.Empty);

            if (!touchRuntimeAllowed)
            {
                m_TouchControlsEnabled = false;
                PlayerPrefs.SetInt(PrefTouchEnabled, 0);
                PlayerPrefs.SetString(PrefTouchPlatform, platformSignature);
                PlayerPrefs.Save();
                return;
            }

            if (!hasSavedPreference || !string.Equals(savedPlatformSignature, platformSignature, System.StringComparison.Ordinal))
            {
                m_TouchControlsEnabled = detectedDefault;
                PlayerPrefs.SetInt(PrefTouchEnabled, m_TouchControlsEnabled ? 1 : 0);
                PlayerPrefs.SetString(PrefTouchPlatform, platformSignature);
                PlayerPrefs.Save();
                return;
            }

            m_TouchControlsEnabled = PlayerPrefs.GetInt(PrefTouchEnabled, detectedDefault ? 1 : 0) != 0;
        }

        private void RefreshPlatformDetection()
        {
            if (Time.unscaledTime < m_NextPlatformProbeTime)
            {
                return;
            }

            m_NextPlatformProbeTime = Time.unscaledTime + 1.25f;
            bool previousValue = m_IsLikelyMobileWebGl;
            m_IsLikelyMobileWebGl = IsLikelyMobileWebGlPlatform();

            if (!IsTouchControlsRuntimeAllowed() && m_TouchControlsEnabled)
            {
                m_TouchControlsEnabled = false;
                PlayerPrefs.SetInt(PrefTouchEnabled, 0);
                PlayerPrefs.SetString(PrefTouchPlatform, BuildTouchPlatformSignature());
                PlayerPrefs.Save();
            }

            if (previousValue != m_IsLikelyMobileWebGl)
            {
                RefreshSettingsLabels();
                UpdateTouchControlsVisibility();
            }
        }

        private bool DetectDefaultTouchState()
        {
            return IsTouchControlsRuntimeAllowed();
        }

        private static bool IsNativeMobilePlatform()
        {
            if (Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld)
            {
                return true;
            }

            return Application.platform == RuntimePlatform.IPhonePlayer
                || Application.platform == RuntimePlatform.Android;
        }

        private bool IsLikelyMobileWebGlPlatform()
        {
            if (Application.platform != RuntimePlatform.WebGLPlayer)
            {
                return false;
            }

            if (IsNativeMobilePlatform())
            {
                return true;
            }

            if (!Input.touchSupported)
            {
                return false;
            }

            var shorterSide = Mathf.Min(Screen.width, Screen.height);
            if (shorterSide > 0 && shorterSide <= 1024)
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            bool hasTouchscreen = Touchscreen.current != null;
            bool hasMouse = Mouse.current != null;
            bool hasKeyboard = Keyboard.current != null;
            if (hasTouchscreen && !hasMouse && !hasKeyboard)
            {
                return true;
            }
#endif

            return false;
        }

        private string BuildTouchPlatformSignature()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                return m_IsLikelyMobileWebGl ? "WebGL-Mobile" : "WebGL-Desktop";
            }

            return $"{Application.platform}-{SystemInfo.deviceType}";
        }

        private bool IsTouchControlsRuntimeAllowed()
        {
            if (Application.isEditor)
            {
                return false;
            }

            if (IsNativeMobilePlatform())
            {
                return true;
            }

            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                return m_IsLikelyMobileWebGl;
            }

            return false;
        }

        private static bool HasTouchInputHardware()
        {
            if (Input.touchSupported || Input.touchCount > 0)
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            return Touchscreen.current != null;
#else
            return false;
#endif
        }

        private void TryNormalizeEventSystems()
        {
            if (Time.unscaledTime < m_NextEventSystemNormalizeTime)
            {
                return;
            }

            m_NextEventSystemNormalizeTime = Time.unscaledTime + 0.75f;
            NormalizeEventSystems();
        }

        private void PumpTouchInputs()
        {
            if (m_LocalInput == null || !m_LocalInput.IsOwner || !m_LocalInput.IsSpawned)
            {
                return;
            }

            if (m_LastTouchVisibility)
            {
                m_LocalInput.InjectMoveInput(m_MoveVector);
                m_HadMoveInputLastFrame = m_MoveVector.sqrMagnitude > 0.0001f;
            }
            else if (m_HadMoveInputLastFrame)
            {
                m_LocalInput.InjectMoveInput(Vector2.zero);
                m_HadMoveInputLastFrame = false;
            }

            if (m_PendingLookInput.sqrMagnitude > 0.0001f)
            {
                m_LocalInput.InjectLookInput(m_PendingLookInput);
                m_PendingLookInput = Vector2.zero;
            }
        }

        private void ToggleSettingsPanel()
        {
            if (!m_SettingsOpen && !CanOpenSettings())
            {
                return;
            }

            SetSettingsOpen(!m_SettingsOpen);
        }

        private void SetSettingsOpen(bool open)
        {
            if (m_SettingsOpen == open)
            {
                return;
            }

            m_SettingsOpen = open;
            if (m_SettingsPanel != null)
            {
                m_SettingsPanel.gameObject.SetActive(open);
            }

            if (open)
            {
                ResolveRuntimeReferences(force: true);

                if (m_LocalInput != null)
                {
                    m_LocalInput.InjectMenuPressed();
                }

                if (m_LocalPlayer != null)
                {
                    m_LocalPlayer.SetMovementInputEnabled(false);
                    m_LockedMovementBySettings = true;
                }

                if (m_InteractionDirector != null)
                {
                    m_InteractionDirector.SetInteractionsLocked(true);
                    m_LockedInteractionsBySettings = true;
                }

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                if (m_LockedMovementBySettings && m_LocalPlayer != null)
                {
                    m_LocalPlayer.SetMovementInputEnabled(true);
                }

                if (m_LockedInteractionsBySettings && m_InteractionDirector != null)
                {
                    m_InteractionDirector.SetInteractionsLocked(false);
                }

                m_LockedMovementBySettings = false;
                m_LockedInteractionsBySettings = false;

                if (!m_LastTouchVisibility)
                {
                    Cursor.visible = false;
                    Cursor.lockState = CursorLockMode.Locked;
                }
            }

            UpdateTouchControlsVisibility();
        }

        private void BuildUi()
        {
            m_Canvas = gameObject.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = 1600;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();
            m_CanvasRect = gameObject.GetComponent<RectTransform>();
            m_CanvasRect.anchorMin = Vector2.zero;
            m_CanvasRect.anchorMax = Vector2.one;
            m_CanvasRect.offsetMin = Vector2.zero;
            m_CanvasRect.offsetMax = Vector2.zero;

            BuildSettingsButton();
            BuildTouchControls();
            BuildSettingsPanel();
        }

        private void BuildSettingsButton()
        {
            var root = CreateRect("SettingsButton", transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            m_SettingsButtonRoot = root;
            root.sizeDelta = new Vector2(76f, 76f);
            root.anchoredPosition = new Vector2(-38f, -38f);

            var image = root.gameObject.AddComponent<Image>();
            image.color = new Color(0.99f, 0.88f, 0.3f, 1f);
            AddNeoBrutalistFrame(root, new Color(0.08f, 0.08f, 0.1f, 0.95f), 3f, new Vector2(4f, -4f), new Color(0.05f, 0.06f, 0.08f, 0.7f));

            m_SettingsButton = root.gameObject.AddComponent<Button>();
            var colors = m_SettingsButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.95f);
            colors.pressedColor = new Color(0.94f, 0.94f, 0.94f, 0.95f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.7f);
            m_SettingsButton.colors = colors;
            m_SettingsButton.onClick.AddListener(ToggleSettingsPanel);

            var iconRect = CreateRect("GearIcon", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            iconRect.sizeDelta = new Vector2(40f, 40f);
            iconRect.anchoredPosition = Vector2.zero;
            // Force procedural glyph on settings button to avoid blank SVG sprite imports.
            CreateNeoIconVisual(iconRect, NeoGlyphKind.Settings, null, new Color(0.08f, 0.09f, 0.12f, 1f));
        }

        private void BuildTouchControls()
        {
            m_TouchRoot = CreateRect("TouchControls", transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f));
            m_TouchRoot.offsetMin = Vector2.zero;
            m_TouchRoot.offsetMax = Vector2.zero;
            m_TouchRoot.gameObject.SetActive(false);

            BuildMovePad();
            BuildLookZone();
            BuildActionButtons();
        }

        private void BuildMovePad()
        {
            m_MovePad = CreateRect("MovePad", m_TouchRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
            m_MovePad.sizeDelta = new Vector2(240f, 240f);
            m_MovePad.anchoredPosition = new Vector2(170f, 170f);
            var bg = m_MovePad.gameObject.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.07f, 0.1f, 0.32f);
            bg.raycastTarget = true;

            m_MoveHandle = CreateRect("Handle", m_MovePad, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            m_MoveHandle.sizeDelta = new Vector2(88f, 88f);
            m_MoveHandle.anchoredPosition = Vector2.zero;
            var handleImage = m_MoveHandle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(0.99f, 0.88f, 0.3f, 0.92f);
            handleImage.raycastTarget = false;
            AddNeoBrutalistFrame(m_MoveHandle, new Color(0.06f, 0.08f, 0.12f, 1f), 2f, Vector2.zero, Color.clear);

            var dragSurface = m_MovePad.gameObject.AddComponent<DragSurface>();
            dragSurface.OnPointerDownEvent += HandleMovePadDown;
            dragSurface.OnDragEvent += HandleMovePadDrag;
            dragSurface.OnPointerUpEvent += HandleMovePadUp;
        }

        private void BuildLookZone()
        {
            m_LookZone = CreateRect("LookZone", m_TouchRoot, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
            m_LookZone.offsetMin = new Vector2(60f, 120f);
            m_LookZone.offsetMax = new Vector2(-16f, -120f);
            var lookBg = m_LookZone.gameObject.AddComponent<Image>();
            lookBg.color = new Color(0.02f, 0.03f, 0.04f, 0.01f);
            lookBg.raycastTarget = true;

            var dragSurface = m_LookZone.gameObject.AddComponent<DragSurface>();
            dragSurface.OnPointerDownEvent += HandleLookZoneDown;
            dragSurface.OnDragEvent += HandleLookZoneDrag;
            dragSurface.OnPointerUpEvent += HandleLookZoneUp;
        }

        private void BuildActionButtons()
        {
            m_InteractButton = CreateNeoButton("InteractButton", m_TouchRoot, "ACT", new Vector2(1f, 0f), new Vector2(-128f, 160f), new Vector2(136f, 68f), new Color(0.99f, 0.88f, 0.3f, 1f), NeoGlyphKind.Action, "Action");
            m_InteractButton.onClick.AddListener(HandleInteractPressed);

            var jumpButton = CreateNeoButton("JumpButton", m_TouchRoot, "JUMP", new Vector2(1f, 0f), new Vector2(-128f, 80f), new Vector2(136f, 62f), new Color(0.72f, 0.93f, 1f, 1f), NeoGlyphKind.Jump, "Jump");
            var jumpHold = jumpButton.gameObject.AddComponent<HoldSurface>();
            jumpHold.OnHeldStateChanged += HandleJumpHoldChanged;

            var sprintButton = CreateNeoButton("SprintButton", m_TouchRoot, "RUN", new Vector2(1f, 0f), new Vector2(-274f, 80f), new Vector2(136f, 62f), new Color(1f, 0.74f, 0.67f, 1f), NeoGlyphKind.Sprint, "Sprint");
            var sprintHold = sprintButton.gameObject.AddComponent<HoldSurface>();
            sprintHold.OnHeldStateChanged += HandleSprintHoldChanged;
        }

        private void BuildSettingsPanel()
        {
            m_SettingsPanel = CreateRect("SettingsPanel", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            m_SettingsPanel.sizeDelta = new Vector2(640f, 420f);
            m_SettingsPanel.anchoredPosition = Vector2.zero;
            var panelBg = m_SettingsPanel.gameObject.AddComponent<Image>();
            panelBg.color = new Color(0.96f, 0.93f, 0.82f, 0.98f);
            AddNeoBrutalistFrame(m_SettingsPanel, new Color(0.06f, 0.07f, 0.1f, 1f), 4f, new Vector2(10f, -10f), new Color(0.03f, 0.04f, 0.06f, 0.76f));

            var title = CreateText("Title", m_SettingsPanel, "SETTINGS", 48, FontStyle.Bold, TextAnchor.MiddleLeft);
            Stretch(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -84f), new Vector2(-110f, -24f));
            title.color = new Color(0.07f, 0.08f, 0.12f, 1f);

            var closeButton = CreateNeoButton("CloseButton", m_SettingsPanel, "CLOSE", new Vector2(1f, 1f), new Vector2(-88f, -44f), new Vector2(150f, 56f), new Color(0.78f, 0.81f, 0.89f, 1f), NeoGlyphKind.Close, "Close", 22);
            closeButton.onClick.AddListener(() => SetSettingsOpen(false));

            m_TouchToggleButton = CreateNeoButton("TouchToggle", m_SettingsPanel, string.Empty, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(560f, 72f), new Color(0.98f, 0.88f, 0.52f, 1f), NeoGlyphKind.Touch, "Touch", 24);
            m_TouchToggleButton.onClick.AddListener(ToggleTouchControls);
            m_TouchToggleLabel = m_TouchToggleButton.GetComponentInChildren<Text>(true);

            m_QualityButton = CreateNeoButton("QualityToggle", m_SettingsPanel, string.Empty, new Vector2(0.5f, 1f), new Vector2(0f, -248f), new Vector2(560f, 72f), new Color(0.77f, 0.92f, 1f, 1f), NeoGlyphKind.Quality, "Quality", 24);
            m_QualityButton.onClick.AddListener(ToggleQualityMode);
            m_QualityLabel = m_QualityButton.GetComponentInChildren<Text>(true);

            var hint = CreateText("Hint", m_SettingsPanel, "Press Tab to open/close. Press Esc to close.", 24, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 24f), new Vector2(-24f, 72f));
            hint.color = new Color(0.08f, 0.09f, 0.12f, 0.9f);

            m_SettingsPanel.gameObject.SetActive(false);
        }

        private void EnforceSettingsAvailability()
        {
            if (m_SettingsOpen && !CanOpenSettings())
            {
                SetSettingsOpen(false);
            }
        }

        private void UpdateSettingsButtonVisibility()
        {
            if (m_SettingsButtonRoot == null)
            {
                return;
            }

            bool shouldShow = !m_SettingsOpen && !IsSettingsButtonSuppressed();
            if (m_LastSettingsButtonVisible == shouldShow)
            {
                return;
            }

            m_LastSettingsButtonVisible = shouldShow;
            m_SettingsButtonRoot.gameObject.SetActive(shouldShow);
        }

        private bool CanOpenSettings()
        {
            return !IsSettingsButtonSuppressed();
        }

        private bool IsSettingsButtonSuppressed()
        {
            if (m_PlayerControlLock != null && m_PlayerControlLock.IsLocked)
            {
                return true;
            }

            if (m_ClassroomConversationPresentation != null && m_ClassroomConversationPresentation.IsConversationActive)
            {
                return true;
            }

            if (m_LabConversationFocus != null && m_LabConversationFocus.IsConversationActive)
            {
                return true;
            }

            if (m_ClassroomIntroCutscene != null && m_ClassroomIntroCutscene.IsPlaying)
            {
                return true;
            }

            if (m_LabShrinkSequence != null && m_LabShrinkSequence.IsPlaying)
            {
                return true;
            }

            if (m_LabFinalCutscene != null && m_LabFinalCutscene.IsPlaying)
            {
                return true;
            }

            var currentFocus = m_InteractionDirector != null ? m_InteractionDirector.CurrentFocus : null;
            if (currentFocus != null &&
                (currentFocus.GetComponent<StoryNpcAgent>() != null ||
                 currentFocus.GetComponentInParent<StoryNpcAgent>() != null))
            {
                return true;
            }

            return false;
        }

        private static bool IsInScene(GameObject target, Scene activeScene)
        {
            return target != null && activeScene.IsValid() && target.scene == activeScene;
        }

        private static T FindSceneObject<T>(Scene activeScene, bool includeInactive) where T : Component
        {
            if (!activeScene.IsValid())
            {
                return null;
            }

            var candidates = FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (candidate != null && IsInScene(candidate.gameObject, activeScene))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void HandleMovePadDown(PointerEventData evt)
        {
            if (!m_LastTouchVisibility || evt == null)
            {
                return;
            }

            m_MovePointerId = evt.pointerId;
            UpdateMoveVectorFromPointer(evt);
        }

        private void HandleMovePadDrag(PointerEventData evt)
        {
            if (!m_LastTouchVisibility || evt == null || evt.pointerId != m_MovePointerId)
            {
                return;
            }

            UpdateMoveVectorFromPointer(evt);
        }

        private void HandleMovePadUp(PointerEventData evt)
        {
            if (evt == null || evt.pointerId != m_MovePointerId)
            {
                return;
            }

            m_MovePointerId = -1;
            m_MoveVector = Vector2.zero;
            if (m_MoveHandle != null)
            {
                m_MoveHandle.anchoredPosition = Vector2.zero;
            }
        }

        private void UpdateMoveVectorFromPointer(PointerEventData evt)
        {
            if (m_MovePad == null || m_MoveHandle == null || evt == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_MovePad, evt.position, evt.pressEventCamera, out var local))
            {
                return;
            }

            float radius = Mathf.Min(m_MovePad.rect.width, m_MovePad.rect.height) * 0.38f;
            var clamped = Vector2.ClampMagnitude(local, radius);
            m_MoveVector = radius > 0.0001f ? clamped / radius : Vector2.zero;
            m_MoveHandle.anchoredPosition = clamped;
        }

        private void HandleLookZoneDown(PointerEventData evt)
        {
            if (!m_LastTouchVisibility || evt == null)
            {
                return;
            }

            m_LookPointerId = evt.pointerId;
            m_LastLookPointerPosition = evt.position;
        }

        private void HandleLookZoneDrag(PointerEventData evt)
        {
            if (!m_LastTouchVisibility || evt == null || evt.pointerId != m_LookPointerId)
            {
                return;
            }

            Vector2 delta = evt.position - m_LastLookPointerPosition;
            m_LastLookPointerPosition = evt.position;
            m_PendingLookInput += delta * 0.0245f;
        }

        private void HandleLookZoneUp(PointerEventData evt)
        {
            if (evt == null || evt.pointerId != m_LookPointerId)
            {
                return;
            }

            m_LookPointerId = -1;
        }

        private void HandleInteractPressed()
        {
            if (m_LocalInput == null || !m_LastTouchVisibility)
            {
                return;
            }

            m_LocalInput.InjectPrimaryActionPressed();
            m_PrimaryReleaseQueued = true;
        }

        private void HandleJumpHoldChanged(bool pressed)
        {
            if (m_LocalInput == null || !m_LastTouchVisibility)
            {
                return;
            }

            if (pressed)
            {
                m_LocalInput.InjectJumpPressed();
            }
            else
            {
                m_LocalInput.InjectJumpReleased();
            }
        }

        private void HandleSprintHoldChanged(bool pressed)
        {
            if (m_LocalInput == null || !m_LastTouchVisibility)
            {
                return;
            }

            m_LocalInput.InjectSprintState(pressed);
        }

        private void ToggleTouchControls()
        {
            if (!IsTouchControlsRuntimeAllowed())
            {
                m_TouchControlsEnabled = false;
                PlayerPrefs.SetInt(PrefTouchEnabled, 0);
                PlayerPrefs.SetString(PrefTouchPlatform, BuildTouchPlatformSignature());
                PlayerPrefs.Save();
                RefreshSettingsLabels();
                UpdateTouchControlsVisibility();
                return;
            }

            m_TouchControlsEnabled = !m_TouchControlsEnabled;
            PlayerPrefs.SetInt(PrefTouchEnabled, m_TouchControlsEnabled ? 1 : 0);
            PlayerPrefs.SetString(PrefTouchPlatform, BuildTouchPlatformSignature());
            PlayerPrefs.Save();
            RefreshSettingsLabels();
            UpdateTouchControlsVisibility();
        }

        private void ToggleQualityMode()
        {
            ApplyQualityMode(!m_HighQualityMode, persist: true);
            RefreshSettingsLabels();
        }

        private void ApplyQualityMode(bool highQuality, bool persist)
        {
            m_HighQualityMode = highQuality;

            if (highQuality)
            {
                Application.targetFrameRate = 60;
                QualitySettings.vSyncCount = 0;
                QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 26f);
                QualitySettings.lodBias = Mathf.Max(QualitySettings.lodBias, 1.2f);
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            }
            else
            {
                Application.targetFrameRate = 60;
                QualitySettings.vSyncCount = 0;
                QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance <= 0f ? 16f : QualitySettings.shadowDistance, 16f);
                QualitySettings.lodBias = Mathf.Min(QualitySettings.lodBias <= 0f ? 0.8f : QualitySettings.lodBias, 0.8f);
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            }

            if (!persist)
            {
                return;
            }

            PlayerPrefs.SetInt(PrefHighQuality, m_HighQualityMode ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void RefreshSettingsLabels()
        {
            if (m_TouchToggleLabel != null)
            {
                m_TouchToggleLabel.text = IsTouchControlsRuntimeAllowed()
                    ? (m_TouchControlsEnabled ? "TOUCH CONTROLS: ON" : "TOUCH CONTROLS: OFF")
                    : "TOUCH CONTROLS: MOBILE ONLY";
            }

            if (m_TouchToggleButton != null)
            {
                m_TouchToggleButton.interactable = IsTouchControlsRuntimeAllowed();
            }

            if (m_QualityLabel != null)
            {
                m_QualityLabel.text = m_HighQualityMode ? "QUALITY MODE: HIGH" : "QUALITY MODE: BALANCED";
            }
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            return rect;
        }

        private static Text CreateText(string name, Transform parent, string value, int fontSize, FontStyle fontStyle, TextAnchor alignment)
        {
            var rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var text = rect.gameObject.AddComponent<Text>();
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null)
            {
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateNeoButton(
            string name,
            Transform parent,
            string label,
            Vector2 anchor,
            Vector2 anchoredPos,
            Vector2 size,
            Color fill,
            NeoGlyphKind iconKind = NeoGlyphKind.None,
            string iconResourceName = null,
            int fontSize = 26)
        {
            var rect = CreateRect(name, parent, anchor, anchor, new Vector2(0.5f, 0.5f));
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = fill;

            AddNeoBrutalistFrame(rect, new Color(0.06f, 0.07f, 0.1f, 1f), 3f, new Vector2(3f, -3f), new Color(0.03f, 0.04f, 0.06f, 0.5f));

            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.96f);
            colors.pressedColor = new Color(0.93f, 0.93f, 0.93f, 0.95f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.7f);
            button.colors = colors;

            var textAlignment = iconKind == NeoGlyphKind.None ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            var text = CreateText("Label", rect, label, fontSize, FontStyle.Bold, textAlignment);
            if (iconKind == NeoGlyphKind.None)
            {
                Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
            else
            {
                var iconRect = CreateRect("Icon", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f));
                var iconSize = Mathf.Min(size.y - 18f, 34f);
                iconRect.sizeDelta = new Vector2(iconSize, iconSize);
                iconRect.anchoredPosition = new Vector2(24f + (iconSize * 0.5f), 0f);
                CreateNeoIconVisual(iconRect, iconKind, iconResourceName, new Color(0.08f, 0.09f, 0.12f, 1f));

                Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(iconSize + 34f, 0f), new Vector2(-10f, 0f));
            }

            text.color = new Color(0.08f, 0.09f, 0.12f, 1f);

            return button;
        }

        private static Graphic CreateNeoIconVisual(RectTransform parent, NeoGlyphKind kind, string resourceName, Color tint)
        {
            Graphic graphic;
            var sprite = string.IsNullOrWhiteSpace(resourceName) ? null : Resources.Load<Sprite>($"TouchIcons/{resourceName}");
            if (IsUsableTouchIconSprite(sprite))
            {
                var image = parent.gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.color = tint;
                graphic = image;

                // Keep a procedural fallback on top so icon still renders if SVG import ends up blank.
                var fallbackRect = CreateRect("GlyphFallback", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
                Stretch(fallbackRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var fallbackGlyph = fallbackRect.gameObject.AddComponent<NeoGlyphGraphic>();
                fallbackGlyph.Glyph = kind;
                fallbackGlyph.color = new Color(tint.r, tint.g, tint.b, 0.92f);
                fallbackGlyph.raycastTarget = false;
            }
            else
            {
                var glyph = parent.gameObject.AddComponent<NeoGlyphGraphic>();
                glyph.Glyph = kind;
                glyph.color = tint;
                graphic = glyph;
            }

            graphic.raycastTarget = false;
            return graphic;
        }

        private static bool IsUsableTouchIconSprite(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
            {
                return false;
            }

            return sprite.rect.width > 1f && sprite.rect.height > 1f;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void AddNeoBrutalistFrame(RectTransform target, Color borderColor, float borderSize, Vector2 shadowOffset, Color shadowColor)
        {
            if (target == null)
            {
                return;
            }

            if (borderSize > 0.01f)
            {
                var outline = target.gameObject.GetComponent<Outline>() ?? target.gameObject.AddComponent<Outline>();
                outline.effectColor = borderColor;
                outline.effectDistance = new Vector2(borderSize, -borderSize);
                outline.useGraphicAlpha = true;
            }

            if (shadowColor.a > 0.001f)
            {
                var shadow = target.gameObject.GetComponent<Shadow>() ?? target.gameObject.AddComponent<Shadow>();
                shadow.effectColor = shadowColor;
                shadow.effectDistance = shadowOffset;
                shadow.useGraphicAlpha = true;
            }
        }

        private static void EnsureEventSystem()
        {
            NormalizeEventSystems();
        }

        private static void NormalizeEventSystems()
        {
            var current = EventSystem.current;
            if (current != null)
            {
                current.gameObject.SetActive(true);
            }

            var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (systems == null || systems.Length == 0)
            {
                var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
                EnsureInputModule(eventSystemObject);
                return;
            }

            var activeScene = SceneManager.GetActiveScene();
            EventSystem preferred = null;

            for (var index = 0; index < systems.Length; index++)
            {
                var system = systems[index];
                if (system == null || !system.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (system.gameObject.scene == activeScene)
                {
                    preferred = system;
                    break;
                }
            }

            if (preferred == null)
            {
                preferred = systems[0];
            }

            for (var index = 0; index < systems.Length; index++)
            {
                var system = systems[index];
                if (system == null)
                {
                    continue;
                }

                bool shouldBeActive = system == preferred;
                if (system.gameObject.activeSelf != shouldBeActive)
                {
                    system.gameObject.SetActive(shouldBeActive);
                }

                if (shouldBeActive)
                {
                    EnsureInputModule(system.gameObject);
                }
            }
        }

        private static void EnsureInputModule(GameObject eventSystemObject)
        {
            if (eventSystemObject == null)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            var standalone = eventSystemObject.GetComponent<StandaloneInputModule>();
            if (standalone != null)
            {
                Destroy(standalone);
            }

            if (eventSystemObject.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
            {
                eventSystemObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
#else
            var inputSystemModule = eventSystemObject.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (inputSystemModule != null)
            {
                Destroy(inputSystemModule);
            }

            if (eventSystemObject.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystemObject.AddComponent<StandaloneInputModule>();
            }
#endif
        }

        private sealed class DragSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            public System.Action<PointerEventData> OnPointerDownEvent;
            public System.Action<PointerEventData> OnDragEvent;
            public System.Action<PointerEventData> OnPointerUpEvent;

            public void OnPointerDown(PointerEventData eventData) => OnPointerDownEvent?.Invoke(eventData);
            public void OnDrag(PointerEventData eventData) => OnDragEvent?.Invoke(eventData);
            public void OnPointerUp(PointerEventData eventData) => OnPointerUpEvent?.Invoke(eventData);
        }

        private sealed class HoldSurface : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
        {
            public System.Action<bool> OnHeldStateChanged;
            private bool m_IsHeld;

            public void OnPointerDown(PointerEventData eventData)
            {
                if (m_IsHeld)
                {
                    return;
                }

                m_IsHeld = true;
                OnHeldStateChanged?.Invoke(true);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                if (!m_IsHeld)
                {
                    return;
                }

                m_IsHeld = false;
                OnHeldStateChanged?.Invoke(false);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (!m_IsHeld)
                {
                    return;
                }

                m_IsHeld = false;
                OnHeldStateChanged?.Invoke(false);
            }
        }

        private sealed class NeoGlyphGraphic : MaskableGraphic
        {
            [SerializeField] private NeoGlyphKind glyph = NeoGlyphKind.Settings;
            [SerializeField, Range(6, 12)] private int teeth = 8;

            public NeoGlyphKind Glyph
            {
                get => glyph;
                set
                {
                    if (glyph == value)
                    {
                        return;
                    }

                    glyph = value;
                    SetVerticesDirty();
                }
            }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                Rect rect = GetPixelAdjustedRect();
                Vector2 center = rect.center;
                float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
                switch (glyph)
                {
                    case NeoGlyphKind.Settings:
                        DrawSettings(vh, center, radius);
                        break;
                    case NeoGlyphKind.Jump:
                        DrawJump(vh, center, radius);
                        break;
                    case NeoGlyphKind.Sprint:
                        DrawSprint(vh, center, radius);
                        break;
                    case NeoGlyphKind.Action:
                        DrawAction(vh, center, radius);
                        break;
                    case NeoGlyphKind.Touch:
                        DrawTouch(vh, center, radius);
                        break;
                    case NeoGlyphKind.Quality:
                        DrawQuality(vh, center, radius);
                        break;
                    case NeoGlyphKind.Close:
                        DrawClose(vh, center, radius);
                        break;
                }
            }

            private void DrawSettings(VertexHelper vh, Vector2 center, float radius)
            {
                float outer = radius * 0.72f;
                float inner = radius * 0.46f;
                float hole = radius * 0.2f;

                AddRing(vh, center, inner, outer, 36, color);
                AddRing(vh, center, 0f, hole, 28, color);

                int safeTeeth = Mathf.Max(6, teeth);
                for (int index = 0; index < safeTeeth; index++)
                {
                    float angle = (Mathf.PI * 2f * index) / safeTeeth;
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 tangent = new Vector2(-dir.y, dir.x);
                    float toothHalfWidth = radius * 0.09f;
                    float toothLength = radius * 0.2f;
                    Vector2 baseCenter = center + dir * outer;
                    Vector2 tipCenter = center + dir * (outer + toothLength);

                    AddQuad(vh,
                        baseCenter - tangent * toothHalfWidth,
                        baseCenter + tangent * toothHalfWidth,
                        tipCenter + tangent * toothHalfWidth,
                        tipCenter - tangent * toothHalfWidth,
                        color);
                }
            }

            private void DrawJump(VertexHelper vh, Vector2 center, float radius)
            {
                float thickness = radius * 0.18f;
                AddLine(vh, center + new Vector2(0f, radius * 0.34f), center + new Vector2(0f, -radius * 0.18f), thickness, color);
                AddTriangle(
                    vh,
                    center + new Vector2(0f, radius * 0.62f),
                    center + new Vector2(-radius * 0.33f, radius * 0.24f),
                    center + new Vector2(radius * 0.33f, radius * 0.24f),
                    color);
                AddLine(vh, center + new Vector2(-radius * 0.42f, -radius * 0.42f), center + new Vector2(radius * 0.42f, -radius * 0.42f), thickness * 0.72f, color);
            }

            private void DrawSprint(VertexHelper vh, Vector2 center, float radius)
            {
                float thickness = radius * 0.13f;
                DrawChevron(vh, center + new Vector2(-radius * 0.08f, 0f), radius * 0.46f, radius * 0.64f, thickness, color);
                DrawChevron(vh, center + new Vector2(radius * 0.22f, 0f), radius * 0.46f, radius * 0.64f, thickness, color);
                AddLine(vh, center + new Vector2(-radius * 0.74f, -radius * 0.18f), center + new Vector2(-radius * 0.34f, -radius * 0.18f), thickness * 0.85f, color);
                AddLine(vh, center + new Vector2(-radius * 0.7f, radius * 0.18f), center + new Vector2(-radius * 0.42f, radius * 0.18f), thickness * 0.85f, color);
            }

            private void DrawAction(VertexHelper vh, Vector2 center, float radius)
            {
                float thickness = radius * 0.12f;
                AddRing(vh, center, radius * 0.24f, radius * 0.38f, 32, color);
                AddLine(vh, center + new Vector2(-radius * 0.08f, radius * 0.72f), center + new Vector2(radius * 0.08f, radius * 0.3f), thickness, color);
                AddLine(vh, center + new Vector2(-radius * 0.72f, 0f), center + new Vector2(-radius * 0.3f, 0f), thickness, color);
                AddLine(vh, center + new Vector2(radius * 0.3f, 0f), center + new Vector2(radius * 0.72f, 0f), thickness, color);
                AddLine(vh, center + new Vector2(0f, -radius * 0.3f), center + new Vector2(0f, -radius * 0.72f), thickness, color);
            }

            private void DrawTouch(VertexHelper vh, Vector2 center, float radius)
            {
                AddRing(vh, center + new Vector2(radius * 0.22f, radius * 0.26f), radius * 0.16f, radius * 0.28f, 28, color);
                AddLine(vh, center + new Vector2(-radius * 0.04f, -radius * 0.5f), center + new Vector2(-radius * 0.04f, radius * 0.26f), radius * 0.2f, color);
                AddLine(vh, center + new Vector2(radius * 0.18f, -radius * 0.28f), center + new Vector2(radius * 0.18f, radius * 0.12f), radius * 0.16f, color);
                AddLine(vh, center + new Vector2(-radius * 0.04f, -radius * 0.52f), center + new Vector2(radius * 0.28f, -radius * 0.52f), radius * 0.18f, color);
                AddLine(vh, center + new Vector2(-radius * 0.22f, -radius * 0.16f), center + new Vector2(-radius * 0.04f, -radius * 0.34f), radius * 0.16f, color);
            }

            private void DrawQuality(VertexHelper vh, Vector2 center, float radius)
            {
                float thickness = radius * 0.12f;
                AddRing(vh, center, radius * 0.12f, radius * 0.24f, 24, color);
                AddLine(vh, center + new Vector2(0f, radius * 0.72f), center + new Vector2(0f, radius * 0.28f), thickness, color);
                AddLine(vh, center + new Vector2(0f, -radius * 0.72f), center + new Vector2(0f, -radius * 0.28f), thickness, color);
                AddLine(vh, center + new Vector2(-radius * 0.72f, 0f), center + new Vector2(-radius * 0.28f, 0f), thickness, color);
                AddLine(vh, center + new Vector2(radius * 0.28f, 0f), center + new Vector2(radius * 0.72f, 0f), thickness, color);
                AddLine(vh, center + new Vector2(-radius * 0.52f, -radius * 0.52f), center + new Vector2(-radius * 0.2f, -radius * 0.2f), thickness * 0.82f, color);
                AddLine(vh, center + new Vector2(radius * 0.2f, radius * 0.2f), center + new Vector2(radius * 0.52f, radius * 0.52f), thickness * 0.82f, color);
                AddLine(vh, center + new Vector2(-radius * 0.52f, radius * 0.52f), center + new Vector2(-radius * 0.2f, radius * 0.2f), thickness * 0.82f, color);
                AddLine(vh, center + new Vector2(radius * 0.2f, -radius * 0.2f), center + new Vector2(radius * 0.52f, -radius * 0.52f), thickness * 0.82f, color);
            }

            private void DrawClose(VertexHelper vh, Vector2 center, float radius)
            {
                float thickness = radius * 0.18f;
                AddLine(vh, center + new Vector2(-radius * 0.56f, -radius * 0.56f), center + new Vector2(radius * 0.56f, radius * 0.56f), thickness, color);
                AddLine(vh, center + new Vector2(-radius * 0.56f, radius * 0.56f), center + new Vector2(radius * 0.56f, -radius * 0.56f), thickness, color);
            }

            private static void DrawChevron(VertexHelper vh, Vector2 center, float width, float height, float thickness, Color tint)
            {
                var left = center + new Vector2(-width * 0.5f, -height * 0.5f);
                var mid = center + new Vector2(width * 0.1f, 0f);
                var top = center + new Vector2(-width * 0.5f, height * 0.5f);
                AddLine(vh, left, mid, thickness, tint);
                AddLine(vh, mid, top, thickness, tint);
            }

            private static void AddLine(VertexHelper vh, Vector2 start, Vector2 end, float thickness, Color tint)
            {
                var delta = end - start;
                if (delta.sqrMagnitude <= 0.0001f)
                {
                    return;
                }

                var tangent = new Vector2(-delta.y, delta.x).normalized * (thickness * 0.5f);
                AddQuad(vh, start - tangent, start + tangent, end + tangent, end - tangent, tint);
            }

            private static void AddRing(VertexHelper vh, Vector2 center, float innerRadius, float outerRadius, int segments, Color tint)
            {
                int safeSegments = Mathf.Max(3, segments);
                for (int index = 0; index < safeSegments; index++)
                {
                    float t0 = (Mathf.PI * 2f * index) / safeSegments;
                    float t1 = (Mathf.PI * 2f * (index + 1)) / safeSegments;
                    Vector2 inner0 = center + new Vector2(Mathf.Cos(t0), Mathf.Sin(t0)) * innerRadius;
                    Vector2 inner1 = center + new Vector2(Mathf.Cos(t1), Mathf.Sin(t1)) * innerRadius;
                    Vector2 outer0 = center + new Vector2(Mathf.Cos(t0), Mathf.Sin(t0)) * outerRadius;
                    Vector2 outer1 = center + new Vector2(Mathf.Cos(t1), Mathf.Sin(t1)) * outerRadius;

                    if (innerRadius <= 0.0001f)
                    {
                        AddTriangle(vh, center, outer0, outer1, tint);
                    }
                    else
                    {
                        AddQuad(vh, inner0, inner1, outer1, outer0, tint);
                    }
                }
            }

            private static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color tint)
            {
                int start = vh.currentVertCount;
                vh.AddVert(a, tint, Vector2.zero);
                vh.AddVert(b, tint, Vector2.zero);
                vh.AddVert(c, tint, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
            }

            private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
            {
                int start = vh.currentVertCount;
                vh.AddVert(a, tint, Vector2.zero);
                vh.AddVert(b, tint, Vector2.zero);
                vh.AddVert(c, tint, Vector2.zero);
                vh.AddVert(d, tint, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
