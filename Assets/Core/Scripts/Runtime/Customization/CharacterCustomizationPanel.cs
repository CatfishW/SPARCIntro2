using System;
using System.Collections.Generic;
using System.Reflection;
using Blocks.Gameplay.Core;
using ItemInteraction;
using Unity.Netcode;
using UnityEngine.Animations;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Customization
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CharacterCustomizationPanel : MonoBehaviour
    {
        private static readonly FieldInfo ParentUiField = typeof(UIDocument).GetField("m_ParentUI", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Font s_RuntimeUiFont;
        private static int s_OpenPanelCount;

        [Header("Catalog")]
        [SerializeField] private CharacterCustomizationCatalog catalogOverride;

        [Header("Preview")]
        [SerializeField, Min(256)] private int previewResolution = 1024;
        [SerializeField] private int previewLayer = 31;
        [SerializeField] private float previewFieldOfView = 24f;
        [SerializeField] private float previewSpinSpeed = 12f;
        [SerializeField] private Color previewClearColor = new Color(0.1f, 0.09f, 0.08f, 1f);

        [Header("Input")]
        [SerializeField] private bool lockPlayerMovementWhileOpen = true;

        [Header("UI")]
        [SerializeField] private int documentSortingOrder = 1200;
        [SerializeField] private PanelSettings panelSettingsOverride;

        private UIDocument m_UIDocument;
        private VisualElement m_Root;
        private VisualElement m_Overlay;
        private VisualElement m_Window;
        private VisualElement m_Header;
        private VisualElement m_HeaderTitleGroup;
        private VisualElement m_HeaderActions;
        private VisualElement m_Content;
        private VisualElement m_LeftPane;
        private VisualElement m_RightPane;
        private VisualElement m_PreviewFrame;
        private VisualElement m_PreviewHud;
        private VisualElement m_SelectionCard;
        private VisualElement m_ActionsRow;
        private ListView m_ListView;
        private TextField m_SearchField;
        private Label m_TitleLabel;
        private Label m_StatusLabel;
        private Label m_PreviewBadgeLabel;
        private Label m_SidebarTitleLabel;
        private Label m_SidebarSubtitleLabel;
        private Label m_SelectionLabel;
        private Label m_DetailLabel;
        private Label m_AnimationStatusLabel;
        private Label m_PresetCountLabel;
        private Label m_HelperLabel;
        private Image m_PreviewImage;
        private Button m_ApplyButton;
        private Button m_RandomizeButton;
        private Button m_CloseButton;

        private readonly List<CharacterCustomizationPreset> m_AllPresets = new List<CharacterCustomizationPreset>();
        private readonly List<CharacterCustomizationPreset> m_FilteredPresets = new List<CharacterCustomizationPreset>();
        private readonly List<int> m_ListSelectionBuffer = new List<int>(1);
        private readonly List<SuspendedDocumentState> m_SuspendedDocuments = new List<SuspendedDocumentState>(8);
        private readonly List<Canvas> m_SuspendedCanvases = new List<Canvas>(8);
        private readonly List<MonoBehaviour> m_SuspendedInteractionBehaviours = new List<MonoBehaviour>(8);

        private CharacterCustomizationCatalog m_ActiveCatalog;
        private CharacterCustomizationPreset m_SelectedPreset;
        private CharacterCustomizationPreset m_AppliedPreset;

        private RenderTexture m_PreviewTexture;
        private GameObject m_PreviewRigRoot;
        private Transform m_PreviewSpinRoot;
        private Transform m_PreviewModelRoot;
        private Animator m_PreviewAnimator;
        private Camera m_PreviewCamera;
        private Light m_PreviewLight;
        private GameObject m_PreviewModelInstance;
        private float m_PreviewSpinAngle;
        private PlayableGraph m_PreviewAnimationGraph;
        private AnimationMixerPlayable m_PreviewAnimationMixer;
        private AnimationClipPlayable m_PreviewPrimaryPlayable;
        private AnimationClipPlayable m_PreviewSecondaryPlayable;
        private readonly List<AnimationClip> m_PreviewCycleClips = new List<AnimationClip>();
        private readonly HashSet<AnimationClip> m_PreviewClipDeduplication = new HashSet<AnimationClip>();
        private int m_CurrentPreviewClipIndex = -1;
        private int m_NextPreviewClipIndex = -1;
        private float m_NextPreviewClipSwitchAt;
        private float m_PreviewBlendStartedAt;
        private bool m_PreviewBlendActive;
        private Vector3 m_PreviewLookLocalPoint = new Vector3(0f, 1.18f, 0f);
        private float m_PreviewCameraVerticalOffset = -0.12f;
        private float m_PreviewCameraDistance = 2.36f;
        private float m_PreviewZoomMultiplier = 1f;
        private float m_PreviewPitchDegrees;
        private int m_PreviewPointerId = -1;
        private bool m_IsPreviewDragging;
        private Vector2 m_LastPreviewPointerPosition;
        private float m_PreviewManualControlUntilUnscaledTime;

        private CorePlayerManager m_LocalPlayerManager;
        private CorePlayerState m_LocalPlayerState;
        private CoreAnimator m_LocalPlayerCoreAnimator;
        private Animator m_LocalPlayerAnimator;
        private InteractionDirector m_InteractionDirector;
        private bool m_IsBuilt;
        private bool m_IsOpen;
        private bool m_IsUpdatingSelection;
        private bool m_CursorWasVisible;
        private CursorLockMode m_PreviousCursorLockState;
        private bool m_LockedMovement;
        private bool m_LockedInteractions;
        private bool m_PreviewDirty = true;
        private float m_UiScale = 1f;
        private float m_FontScale = 1f;
        private string m_SearchText = string.Empty;
        private int m_LastUiPointerHandledFrame = -1;
        private EventSystem m_EventSystem;
        private InputSystemUIInputModule m_InputModule;
        private StandaloneInputModule m_StandaloneInputModule;
        private PanelSettings m_RuntimePanelSettings;
        private Coroutine m_CursorGuardRoutine;
        private float m_CurrentListItemHeight = -1f;
        private bool m_IsCompactLayout;
        private bool m_IsPortraitLayout;
        private bool m_IsDenseListLayout;
        private bool m_UseStackedLayout;
        private bool m_IsRegisteredAsOpenPanel;

        private const float PreviewAnimationBlendDurationSeconds = 0.3f;
        private const float PreviewAnimationMinimumShowcaseSeconds = 2.4f;
        private const float PreviewAnimationMaximumShowcaseSeconds = 4.1f;
        private const float UiReferenceHeight = 1080f;
        private const float UiMinScale = 1f;
        private const float UiMaxScale = 1.14f;
        private const float UiFontReferenceHeight = 980f;
        private const float UiFontMinScale = 1f;
        private const float UiFontMaxScale = 1.44f;
        private const float PreviewPitchMinDegrees = -22f;
        private const float PreviewPitchMaxDegrees = 28f;
        private const float PreviewZoomMin = 0.82f;
        private const float PreviewZoomMax = 1.38f;
        private const float PreviewRotateDragSpeed = 0.35f;
        private const float PreviewPitchDragSpeed = 0.16f;
        private const float PreviewZoomWheelSpeed = 0.04f;
        private const float PreviewManualControlHoldSeconds = 8f;
        private const float DefaultPreviewTextureAspect = 1.2f;

        public bool IsOpen => m_IsOpen;
        public static bool IsAnyOpen => s_OpenPanelCount > 0;

        private sealed class SuspendedDocumentState
        {
            public UIDocument Document;
            public DisplayStyle PreviousDisplay;
            public PickingMode PreviousPickingMode;
        }

        private float U(float value)
        {
            return Mathf.Round(value * m_UiScale * 10f) / 10f;
        }

        private float T(float value)
        {
            return Mathf.Round(value * m_FontScale * 10f) / 10f;
        }

        private void GetUiLayoutDimensions(out float width, out float height)
        {
            width = 0f;
            height = 0f;

            if (m_Overlay != null)
            {
                width = m_Overlay.resolvedStyle.width;
                height = m_Overlay.resolvedStyle.height;
            }

            if ((width < 1f || height < 1f) && m_Root != null)
            {
                width = m_Root.resolvedStyle.width;
                height = m_Root.resolvedStyle.height;
            }

            if (width < 1f || height < 1f)
            {
                width = Screen.width;
                height = Screen.height;
            }
        }

        private void RefreshUiScale()
        {
            GetUiLayoutDimensions(out var layoutWidth, out var layoutHeight);
            var height = Mathf.Max(720f, layoutHeight);
            var width = Mathf.Max(480f, layoutWidth);
            m_UiScale = Mathf.Clamp(height / UiReferenceHeight, UiMinScale, UiMaxScale);
            var fontDriver = Mathf.Lerp(Mathf.Min(width, height), height, 0.18f);
            if (height > width * 1.12f)
            {
                fontDriver *= 0.77f;
            }

            m_FontScale = Mathf.Clamp(fontDriver / UiFontReferenceHeight, UiFontMinScale, UiFontMaxScale);
        }

        private Scene GetContextScene()
        {
            return gameObject != null && gameObject.scene.IsValid()
                ? gameObject.scene
                : SceneManager.GetActiveScene();
        }

        private static bool IsInScene(GameObject target, Scene scene)
        {
            return target != null && scene.IsValid() && target.scene == scene;
        }

        private static Font GetRuntimeUiFont()
        {
            if (s_RuntimeUiFont != null)
            {
                return s_RuntimeUiFont;
            }

            s_RuntimeUiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (s_RuntimeUiFont == null)
            {
                s_RuntimeUiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return s_RuntimeUiFont;
        }

        private static void ApplyRuntimeFont(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            var runtimeFont = GetRuntimeUiFont();
            if (runtimeFont == null)
            {
                return;
            }

            element.style.unityFontDefinition = FontDefinition.FromFont(runtimeFont);
        }

        private static void ApplyRuntimeFontTree(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            ApplyRuntimeFont(root);
            foreach (var child in root.Children())
            {
                ApplyRuntimeFontTree(child);
            }
        }

        private static T FindSceneComponent<T>(Scene scene, bool includeInactive) where T : Component
        {
            var components = FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int index = 0; index < components.Length; index++)
            {
                var component = components[index];
                if (component == null || !IsInScene(component.gameObject, scene))
                {
                    continue;
                }

                return component;
            }

            return null;
        }

        private void Awake()
        {
            m_UIDocument = GetComponent<UIDocument>();
            EnsureStandaloneDocumentHost();
            EnsureBuilt();
        }

        private void OnEnable()
        {
            EnsureBuilt();
            SetVisible(false);
        }

        private void OnDisable()
        {
            Hide();
            ReleaseInteractionLock();
            TearDownPreview();
        }

        private void OnDestroy()
        {
            StopPreviewAnimationPlayback();

            if (m_RuntimePanelSettings != null)
            {
                DestroySmart(m_RuntimePanelSettings);
                m_RuntimePanelSettings = null;
            }
        }

        private void Update()
        {
            if (!m_IsOpen)
            {
                return;
            }

            if (IsCancelPressedThisFrame())
            {
                Hide();
                return;
            }

            if (IsSubmitPressedThisFrame())
            {
                ApplySelection();
                return;
            }

            HandleManualPointerFallback();

            if (m_PreviewSpinRoot != null && !m_IsPreviewDragging && Time.unscaledTime >= m_PreviewManualControlUntilUnscaledTime)
            {
                m_PreviewSpinAngle = Mathf.Repeat(m_PreviewSpinAngle + (previewSpinSpeed * Time.deltaTime), 360f);
                ApplyPreviewSpinRotation();
                if (Mathf.Abs(previewSpinSpeed) > 0.01f)
                {
                    m_PreviewDirty = true;
                }
            }

            UpdatePreviewAnimationCycle();

            if (m_PreviewDirty)
            {
                RequestPreviewRender();
            }
        }

        private void LateUpdate()
        {
            EnsureCursorVisibleWhileOpen();
        }

        public void Show()
        {
            bool wasOpen = m_IsOpen;

            EnsureBuilt();
            if (m_Root == null)
            {
                return;
            }

            if (!wasOpen)
            {
                AcquireInteractionLock();
            }

            RefreshCatalog();
            RefreshLocalPlayerReferences();
            RefreshSelectionFromPlayer();
            RefreshFilteredList();
            EnsureUiInputBridge();
            SetVisible(true);
            if (!wasOpen)
            {
                SuspendCompetingUi();
            }
            ApplyResponsiveLayout();
            SetPreviewCameraActive(true);
            FocusInitialElement();
            m_PreviewDirty = true;
            RefreshPreview();
            UpdateStatus();
            UpdateActionsState();

            if (wasOpen)
            {
                return;
            }

            m_PreviousCursorLockState = UnityEngine.Cursor.lockState;
            m_CursorWasVisible = UnityEngine.Cursor.visible;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            RegisterOpenPanel();
            StartCursorGuard();
            EnsureCursorVisibleWhileOpen();

            m_LockedMovement = false;
            if (lockPlayerMovementWhileOpen && m_LocalPlayerManager != null)
            {
                m_LocalPlayerManager.SetMovementInputEnabled(false);
                m_LockedMovement = true;
            }
        }

        public void Open()
        {
            Show();
        }

        public bool RebuildAndShow()
        {
            m_IsBuilt = false;
            m_Root = null;

            if (m_UIDocument == null)
            {
                m_UIDocument = GetComponent<UIDocument>();
            }

            if (m_UIDocument != null)
            {
                m_UIDocument.enabled = false;
                m_UIDocument.enabled = true;
            }

            EnsureBuilt();
            Show();
            return m_IsOpen;
        }

        public void Hide()
        {
            if (!m_IsOpen)
            {
                return;
            }

            if (m_LockedMovement && m_LocalPlayerManager != null)
            {
                m_LocalPlayerManager.SetMovementInputEnabled(true);
                m_LockedMovement = false;
            }

            ReleasePreviewPointerCapture();
            StopCursorGuard();
            UnregisterOpenPanel();

            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
            else
            {
                UnityEngine.Cursor.lockState = m_PreviousCursorLockState;
                UnityEngine.Cursor.visible = m_CursorWasVisible;
            }
            SetPreviewCameraActive(false);
            SetVisible(false);
            RestoreSuspendedUi();
            ReleaseInteractionLock();
            m_IsOpen = false;
        }

        public void Close()
        {
            Hide();
        }

        private void EnsureCursorVisibleWhileOpen()
        {
            if (!m_IsOpen)
            {
                return;
            }

            if (UnityEngine.Cursor.lockState != CursorLockMode.None)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
            }

            if (!UnityEngine.Cursor.visible)
            {
                UnityEngine.Cursor.visible = true;
            }
        }

        private void RegisterOpenPanel()
        {
            if (m_IsRegisteredAsOpenPanel)
            {
                return;
            }

            s_OpenPanelCount++;
            m_IsRegisteredAsOpenPanel = true;
        }

        private void UnregisterOpenPanel()
        {
            if (!m_IsRegisteredAsOpenPanel)
            {
                return;
            }

            s_OpenPanelCount = Mathf.Max(0, s_OpenPanelCount - 1);
            m_IsRegisteredAsOpenPanel = false;
        }

        private void StartCursorGuard()
        {
            StopCursorGuard();
            if (!isActiveAndEnabled)
            {
                return;
            }

            m_CursorGuardRoutine = StartCoroutine(CursorGuardRoutine());
        }

        private void StopCursorGuard()
        {
            if (m_CursorGuardRoutine == null)
            {
                return;
            }

            StopCoroutine(m_CursorGuardRoutine);
            m_CursorGuardRoutine = null;
        }

        private System.Collections.IEnumerator CursorGuardRoutine()
        {
            while (m_IsOpen)
            {
                yield return new WaitForEndOfFrame();
                EnsureCursorVisibleWhileOpen();
            }

            m_CursorGuardRoutine = null;
        }

        public void ApplySelection()
        {
            if (m_SelectedPreset == null)
            {
                return;
            }

            RefreshLocalPlayerReferences();

            var requestedThroughAddon = TryRequestPresetThroughAddon(m_SelectedPreset.id);
            if (!requestedThroughAddon)
            {
                if (m_LocalPlayerState != null)
                {
                    m_LocalPlayerState.SetCharacterPresetId(m_SelectedPreset.id);
                }
                else
                {
                    CharacterCustomizationStorage.SaveSelectedPresetId(m_SelectedPreset.id);
                }
            }
            else
            {
                CharacterCustomizationStorage.SaveSelectedPresetId(m_SelectedPreset.id);
            }

            var appliedLocally = TryApplyPresetLocally(m_SelectedPreset);
            if (!appliedLocally && !requestedThroughAddon)
            {
                UpdateStatus("Unable to apply preset: local player reference is missing.");
                return;
            }

            m_AppliedPreset = m_SelectedPreset;
            if (!appliedLocally && requestedThroughAddon)
            {
                UpdateStatus("Preset synced. It will appear as soon as the local player rig is available.");
            }
            else
            {
                UpdateStatus();
            }

            UpdateActionsState();
        }

        public void RandomizeSelection()
        {
            RefreshCatalog();
            if (m_AllPresets.Count == 0)
            {
                return;
            }

            var candidate = m_ActiveCatalog != null ? m_ActiveCatalog.GetRandomPreset() : null;
            if (candidate == null)
            {
                candidate = m_AllPresets[UnityEngine.Random.Range(0, m_AllPresets.Count)];
            }

            if (candidate != null)
            {
                SelectPreset(candidate, false);
            }
        }

        private void EnsureBuilt()
        {
            if (m_IsBuilt)
            {
                return;
            }

            if (m_UIDocument == null)
            {
                m_UIDocument = GetComponent<UIDocument>();
            }

            if (m_UIDocument == null)
            {
                Debug.LogError("[CharacterCustomizationPanel] UIDocument is required.", this);
                return;
            }

            EnsureStandaloneDocumentHost();
            EnsureDocumentCanRender();
            m_UIDocument.sortingOrder = documentSortingOrder;

            m_Root = m_UIDocument.rootVisualElement;
            if (m_Root == null)
            {
                // UI Toolkit can transiently report null root when panel settings are still being resolved.
                m_UIDocument.enabled = false;
                m_UIDocument.enabled = true;
                EnsureDocumentCanRender(forceRecreateRuntimeSettings: true);
                m_Root = m_UIDocument.rootVisualElement;
            }

            if (m_Root == null)
            {
                Debug.LogError("[CharacterCustomizationPanel] UIDocument rootVisualElement is missing.", this);
                return;
            }

            RefreshUiScale();

            m_Root.Clear();
            m_Root.style.flexGrow = 1f;
            m_Root.style.display = DisplayStyle.None;
            m_Root.style.position = Position.Absolute;
            m_Root.style.left = 0f;
            m_Root.style.right = 0f;
            m_Root.style.top = 0f;
            m_Root.style.bottom = 0f;
            m_Root.pickingMode = PickingMode.Ignore;
            m_UIDocument.sortingOrder = documentSortingOrder;

            m_Overlay = new VisualElement();
            m_Overlay.style.position = Position.Absolute;
            m_Overlay.style.left = 0f;
            m_Overlay.style.right = 0f;
            m_Overlay.style.top = 0f;
            m_Overlay.style.bottom = 0f;
            m_Overlay.style.flexDirection = FlexDirection.Column;
            m_Overlay.style.justifyContent = Justify.Center;
            m_Overlay.style.alignItems = Align.Center;
            m_Overlay.style.backgroundColor = new Color(0.03f, 0.02f, 0.015f, 0.8f);
            m_Overlay.style.paddingLeft = U(28f);
            m_Overlay.style.paddingRight = U(28f);
            m_Overlay.style.paddingTop = U(28f);
            m_Overlay.style.paddingBottom = U(28f);
            m_Overlay.pickingMode = PickingMode.Position;
            m_Overlay.focusable = true;
            m_Root.Add(m_Overlay);

            m_Window = new VisualElement();
            m_Window.style.width = new Length(94f, LengthUnit.Percent);
            m_Window.style.height = new Length(92f, LengthUnit.Percent);
            m_Window.style.minWidth = 1280f;
            m_Window.style.minHeight = 820f;
            m_Window.style.maxWidth = 3120f;
            m_Window.style.maxHeight = 1820f;
            m_Window.style.flexDirection = FlexDirection.Column;
            m_Window.style.backgroundColor = new Color(0.972f, 0.936f, 0.868f, 0.99f);
            m_Window.style.borderTopLeftRadius = U(22f);
            m_Window.style.borderTopRightRadius = U(22f);
            m_Window.style.borderBottomLeftRadius = U(22f);
            m_Window.style.borderBottomRightRadius = U(22f);
            m_Window.style.borderLeftWidth = U(4f);
            m_Window.style.borderRightWidth = U(4f);
            m_Window.style.borderTopWidth = U(4f);
            m_Window.style.borderBottomWidth = U(7f);
            m_Window.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_Window.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_Window.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_Window.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_Window.style.paddingLeft = U(30f);
            m_Window.style.paddingRight = U(30f);
            m_Window.style.paddingTop = U(26f);
            m_Window.style.paddingBottom = U(26f);
            m_Window.style.overflow = Overflow.Hidden;
            m_Window.pickingMode = PickingMode.Position;
            m_Window.focusable = true;
            m_Window.tabIndex = 0;
            m_Window.RegisterCallback<PointerDownEvent>(evt =>
            {
                m_Window.Focus();
            });
            m_Overlay.Add(m_Window);

            BuildHeader();
            BuildContent();
            ApplyRuntimeFontTree(m_Window);
            m_Overlay.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsiveLayout());
            m_Window.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsiveLayout());
            ApplyResponsiveLayout();

            try
            {
                BuildPreviewRig();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }

            m_IsBuilt = true;
        }

        private void EnsureDocumentCanRender(bool forceRecreateRuntimeSettings = false)
        {
            if (m_UIDocument == null)
            {
                return;
            }

            var contextScene = GetContextScene();

            DetachFromParentDocument();

            if (panelSettingsOverride != null)
            {
                m_UIDocument.panelSettings = panelSettingsOverride;
                return;
            }

            var referencePanelSettings = FindReferencePanelSettings(contextScene);
            if (referencePanelSettings != null)
            {
                m_UIDocument.panelSettings = referencePanelSettings;
                return;
            }

            if (m_RuntimePanelSettings != null && forceRecreateRuntimeSettings)
            {
                DestroySmart(m_RuntimePanelSettings);
                m_RuntimePanelSettings = null;
            }

            if (m_RuntimePanelSettings == null)
            {
                m_RuntimePanelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                m_RuntimePanelSettings.name = $"CharacterCustomizationRuntimePanelSettings_{contextScene.name}";
                m_RuntimePanelSettings.hideFlags = HideFlags.DontSave;
            }

            ConfigureFallbackPanelSettings(m_RuntimePanelSettings);
            m_UIDocument.panelSettings = m_RuntimePanelSettings;
        }

        private PanelSettings FindReferencePanelSettings(Scene contextScene)
        {
            if (m_UIDocument != null &&
                m_UIDocument.panelSettings != null &&
                m_UIDocument.panelSettings != m_RuntimePanelSettings &&
                m_UIDocument.panelSettings.themeStyleSheet != null)
            {
                return m_UIDocument.panelSettings;
            }

            PanelSettings fallback = null;
            var documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < documents.Length; index++)
            {
                var candidate = documents[index];
                if (candidate == null || candidate == m_UIDocument || candidate.panelSettings == null)
                {
                    continue;
                }

                var settings = candidate.panelSettings;
                if (settings == m_RuntimePanelSettings)
                {
                    continue;
                }

                if (settings.themeStyleSheet == null)
                {
                    fallback ??= settings;
                    continue;
                }

                if (contextScene.IsValid() && candidate.gameObject != null && candidate.gameObject.scene == contextScene)
                {
                    return settings;
                }

                fallback ??= settings;
            }

            if (fallback != null && fallback.themeStyleSheet != null)
            {
                return fallback;
            }

            var loadedSettings = Resources.FindObjectsOfTypeAll<PanelSettings>();
            for (var index = 0; index < loadedSettings.Length; index++)
            {
                var candidate = loadedSettings[index];
                if (candidate != null && candidate != m_RuntimePanelSettings && candidate.themeStyleSheet != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void ConfigureFallbackPanelSettings(PanelSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1200, 800);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0f;
            settings.scale = 1f;
            settings.referenceSpritePixelsPerUnit = 100f;
            settings.referenceDpi = 96f;
            settings.fallbackDpi = 96f;
            settings.clearColor = false;
            settings.colorClearValue = Color.clear;
            settings.clearDepthStencil = true;
            settings.targetTexture = null;
            settings.targetDisplay = 0;
            settings.sortingOrder = documentSortingOrder;

            var reference = FindReferencePanelSettings(GetContextScene());
            if (reference == null)
            {
                return;
            }

            settings.themeStyleSheet = reference.themeStyleSheet;
            settings.textSettings = reference.textSettings;
            settings.scaleMode = reference.scaleMode;
            settings.referenceResolution = reference.referenceResolution;
            settings.screenMatchMode = reference.screenMatchMode;
            settings.match = reference.match;
            settings.scale = reference.scale;
            settings.referenceSpritePixelsPerUnit = reference.referenceSpritePixelsPerUnit;
            settings.referenceDpi = reference.referenceDpi;
            settings.fallbackDpi = reference.fallbackDpi;
            settings.clearColor = reference.clearColor;
            settings.colorClearValue = reference.colorClearValue;
            settings.clearDepthStencil = reference.clearDepthStencil;
            settings.targetDisplay = reference.targetDisplay;
            settings.bindingLogLevel = reference.bindingLogLevel;
            settings.textureSlotCount = reference.textureSlotCount;
            settings.forceGammaRendering = reference.forceGammaRendering;
        }

        private void EnsureStandaloneDocumentHost()
        {
            if (transform == null)
            {
                return;
            }

            var parent = transform.parent;
            if (parent == null)
            {
                return;
            }

            var parentHasUiDocument =
                parent.GetComponent<UIDocument>() != null ||
                parent.GetComponentInParent<UIDocument>() != null;

            if (!parentHasUiDocument)
            {
                return;
            }

            // Keep the customization panel out of nested UIDocument hierarchies to avoid hidden/invalid roots.
            transform.SetParent(null, false);
            var contextScene = GetContextScene();
            if (contextScene.IsValid() && gameObject.scene != contextScene)
            {
                SceneManager.MoveGameObjectToScene(gameObject, contextScene);
            }
        }

        private void DetachFromParentDocument()
        {
            if (m_UIDocument == null || ParentUiField == null)
            {
                return;
            }

            if (ParentUiField.GetValue(m_UIDocument) != null)
            {
                ParentUiField.SetValue(m_UIDocument, null);
            }
        }

        private void BuildHeader()
        {
            m_Header = new VisualElement();
            m_Header.style.flexDirection = FlexDirection.Row;
            m_Header.style.alignItems = Align.FlexStart;
            m_Header.style.justifyContent = Justify.SpaceBetween;
            m_Header.style.paddingBottom = U(22f);
            m_Header.style.borderBottomWidth = U(4f);
            m_Header.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_Window.Add(m_Header);

            m_HeaderTitleGroup = new VisualElement();
            m_HeaderTitleGroup.style.flexDirection = FlexDirection.Column;
            m_HeaderTitleGroup.style.flexGrow = 1f;
            m_HeaderTitleGroup.style.marginRight = U(24f);
            m_Header.Add(m_HeaderTitleGroup);

            var badgeRow = new VisualElement();
            badgeRow.style.flexDirection = FlexDirection.Row;
            badgeRow.style.alignItems = Align.Center;
            badgeRow.style.marginBottom = U(10f);
            m_HeaderTitleGroup.Add(badgeRow);

            var wardrobeChip = new Label("WARDROBE");
            ApplyRuntimeFont(wardrobeChip);
            wardrobeChip.style.paddingLeft = U(18f);
            wardrobeChip.style.paddingRight = U(18f);
            wardrobeChip.style.height = U(40f);
            wardrobeChip.style.marginRight = U(12f);
            wardrobeChip.style.backgroundColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            wardrobeChip.style.color = new Color(0.98f, 0.95f, 0.88f, 1f);
            wardrobeChip.style.unityFontStyleAndWeight = FontStyle.Bold;
            wardrobeChip.style.fontSize = T(18f);
            wardrobeChip.style.unityTextAlign = TextAnchor.MiddleCenter;
            wardrobeChip.style.letterSpacing = 1.8f;
            wardrobeChip.style.borderTopLeftRadius = U(14f);
            wardrobeChip.style.borderTopRightRadius = U(14f);
            wardrobeChip.style.borderBottomLeftRadius = U(14f);
            wardrobeChip.style.borderBottomRightRadius = U(14f);
            badgeRow.Add(wardrobeChip);

            m_PresetCountLabel = new Label("00 LOOKS");
            ApplyRuntimeFont(m_PresetCountLabel);
            m_PresetCountLabel.style.paddingLeft = U(14f);
            m_PresetCountLabel.style.paddingRight = U(14f);
            m_PresetCountLabel.style.height = U(38f);
            m_PresetCountLabel.style.backgroundColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            m_PresetCountLabel.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_PresetCountLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_PresetCountLabel.style.fontSize = T(16f);
            m_PresetCountLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_PresetCountLabel.style.borderTopLeftRadius = U(12f);
            m_PresetCountLabel.style.borderTopRightRadius = U(12f);
            m_PresetCountLabel.style.borderBottomLeftRadius = U(12f);
            m_PresetCountLabel.style.borderBottomRightRadius = U(12f);
            badgeRow.Add(m_PresetCountLabel);

            m_TitleLabel = new Label("Change Character");
            ApplyRuntimeFont(m_TitleLabel);
            m_TitleLabel.style.fontSize = T(48f);
            m_TitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_TitleLabel.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_TitleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_TitleLabel.style.minHeight = U(60f);
            m_TitleLabel.style.marginBottom = U(2f);
            m_HeaderTitleGroup.Add(m_TitleLabel);

            m_StatusLabel = new Label("Preview a look, then apply it.");
            ApplyRuntimeFont(m_StatusLabel);
            m_StatusLabel.style.fontSize = T(22f);
            m_StatusLabel.style.color = new Color(0.16f, 0.14f, 0.1f, 0.92f);
            m_StatusLabel.style.whiteSpace = WhiteSpace.Normal;
            m_StatusLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            m_StatusLabel.style.minHeight = U(28f);
            m_HeaderTitleGroup.Add(m_StatusLabel);

            m_HeaderActions = new VisualElement();
            m_HeaderActions.style.flexDirection = FlexDirection.Column;
            m_HeaderActions.style.alignItems = Align.FlexEnd;
            m_HeaderActions.style.minWidth = U(160f);
            m_Header.Add(m_HeaderActions);

            m_HelperLabel = new Label("Drag to orbit");
            ApplyRuntimeFont(m_HelperLabel);
            m_HelperLabel.style.fontSize = T(19f);
            m_HelperLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_HelperLabel.style.color = new Color(0.24f, 0.22f, 0.17f, 0.9f);
            m_HelperLabel.style.marginBottom = U(12f);
            m_HeaderActions.Add(m_HelperLabel);

            m_CloseButton = CreateActionButton("Close", Hide, new Color(0.95f, 0.72f, 0.62f, 1f));
            m_CloseButton.style.minWidth = U(132f);
            m_HeaderActions.Add(m_CloseButton);
        }

        private void BuildContent()
        {
            m_Content = new VisualElement();
            m_Content.style.flexGrow = 1f;
            m_Content.style.flexShrink = 1f;
            m_Content.style.minHeight = 0f;
            m_Content.style.flexDirection = FlexDirection.Row;
            m_Content.style.alignItems = Align.Stretch;
            m_Content.style.marginTop = U(20f);
            m_Window.Add(m_Content);

            BuildDetailsPane();
            BuildSidebar();
        }

        private void BuildSidebar()
        {
            m_LeftPane = new VisualElement();
            m_LeftPane.style.width = U(420f);
            m_LeftPane.style.flexShrink = 0f;
            m_LeftPane.style.flexDirection = FlexDirection.Column;
            m_LeftPane.style.marginLeft = U(18f);
            m_LeftPane.style.paddingLeft = U(24f);
            m_LeftPane.style.paddingRight = U(24f);
            m_LeftPane.style.paddingTop = U(24f);
            m_LeftPane.style.paddingBottom = U(24f);
            m_LeftPane.style.backgroundColor = new Color(0.95f, 0.89f, 0.74f, 1f);
            m_LeftPane.style.borderLeftWidth = U(4f);
            m_LeftPane.style.borderRightWidth = U(4f);
            m_LeftPane.style.borderTopWidth = U(4f);
            m_LeftPane.style.borderBottomWidth = U(8f);
            m_LeftPane.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_LeftPane.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_LeftPane.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_LeftPane.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_LeftPane.style.borderTopLeftRadius = U(18f);
            m_LeftPane.style.borderTopRightRadius = U(18f);
            m_LeftPane.style.borderBottomLeftRadius = U(18f);
            m_LeftPane.style.borderBottomRightRadius = U(18f);
            m_Content.Add(m_LeftPane);

            m_SidebarTitleLabel = new Label("Preset Library");
            ApplyRuntimeFont(m_SidebarTitleLabel);
            m_SidebarTitleLabel.style.fontSize = T(30f);
            m_SidebarTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_SidebarTitleLabel.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SidebarTitleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_SidebarTitleLabel.style.marginBottom = U(6f);
            m_LeftPane.Add(m_SidebarTitleLabel);

            m_SidebarSubtitleLabel = new Label("Search and pick a look.");
            ApplyRuntimeFont(m_SidebarSubtitleLabel);
            m_SidebarSubtitleLabel.style.fontSize = T(19f);
            m_SidebarSubtitleLabel.style.color = new Color(0.2f, 0.17f, 0.12f, 0.92f);
            m_SidebarSubtitleLabel.style.whiteSpace = WhiteSpace.Normal;
            m_SidebarSubtitleLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            m_SidebarSubtitleLabel.style.marginBottom = U(16f);
            m_LeftPane.Add(m_SidebarSubtitleLabel);

            m_SearchField = new TextField();
            ApplyRuntimeFont(m_SearchField);
            m_SearchField.label = string.Empty;
            m_SearchField.value = string.Empty;
            m_SearchField.isDelayed = true;
            m_SearchField.style.height = U(56f);
            m_SearchField.style.marginBottom = U(14f);
            m_SearchField.style.borderTopLeftRadius = U(12f);
            m_SearchField.style.borderTopRightRadius = U(12f);
            m_SearchField.style.borderBottomLeftRadius = U(12f);
            m_SearchField.style.borderBottomRightRadius = U(12f);
            m_SearchField.style.backgroundColor = new Color(1f, 0.98f, 0.93f, 1f);
            m_SearchField.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SearchField.style.borderLeftWidth = U(3f);
            m_SearchField.style.borderRightWidth = U(3f);
            m_SearchField.style.borderTopWidth = U(3f);
            m_SearchField.style.borderBottomWidth = U(5f);
            m_SearchField.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SearchField.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SearchField.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SearchField.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SearchField.style.paddingLeft = U(12f);
            m_SearchField.style.paddingRight = U(12f);
            m_SearchField.tooltip = "Search presets by name or id";
            m_SearchField.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_SearchField.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_SearchField.style.fontSize = T(24f);
            m_SearchField.focusable = true;
            m_SearchField.RegisterValueChangedCallback(OnSearchChanged);
            m_LeftPane.Add(m_SearchField);

            var searchInput = m_SearchField.Q(TextField.textInputUssName);
            if (searchInput != null)
            {
                ApplyRuntimeFont(searchInput);
                searchInput.style.backgroundColor = Color.clear;
                searchInput.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
                searchInput.style.unityFontStyleAndWeight = FontStyle.Bold;
                searchInput.style.fontSize = T(21f);
            }

            m_ListView = new ListView
            {
                selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                fixedItemHeight = U(126f),
                showBoundCollectionSize = false,
                showFoldoutHeader = false,
                reorderable = false,
                makeItem = CreatePresetRow,
                bindItem = BindPresetRow
            };
            m_CurrentListItemHeight = m_ListView.fixedItemHeight;
            m_ListView.style.flexGrow = 1f;
            m_ListView.style.flexBasis = 0f;
            m_ListView.style.minHeight = 0f;
            m_ListView.style.backgroundColor = Color.clear;
            m_ListView.style.borderLeftWidth = 0f;
            m_ListView.style.borderRightWidth = 0f;
            m_ListView.style.borderTopWidth = 0f;
            m_ListView.style.borderBottomWidth = 0f;
            m_ListView.selectionChanged += OnSelectionChanged;
            ConfigureListViewScrollArea();
            m_LeftPane.Add(m_ListView);
        }

        private void BuildDetailsPane()
        {
            m_RightPane = new VisualElement();
            m_RightPane.style.flexGrow = 1f;
            m_RightPane.style.flexShrink = 1f;
            m_RightPane.style.flexDirection = FlexDirection.Column;
            m_RightPane.style.justifyContent = Justify.FlexStart;
            m_RightPane.style.minWidth = 0f;
            m_RightPane.style.minHeight = 0f;
            m_RightPane.style.marginRight = U(8f);
            m_Content.Add(m_RightPane);

            m_PreviewFrame = new VisualElement();
            m_PreviewFrame.style.flexGrow = 0f;
            m_PreviewFrame.style.flexBasis = StyleKeyword.Auto;
            m_PreviewFrame.style.height = U(460f);
            m_PreviewFrame.style.minHeight = U(360f);
            m_PreviewFrame.style.flexShrink = 1f;
            m_PreviewFrame.style.position = Position.Relative;
            m_PreviewFrame.style.overflow = Overflow.Hidden;
            m_PreviewFrame.style.backgroundColor = new Color(0.085f, 0.09f, 0.12f, 1f);
            m_PreviewFrame.style.borderTopLeftRadius = U(18f);
            m_PreviewFrame.style.borderTopRightRadius = U(18f);
            m_PreviewFrame.style.borderBottomLeftRadius = U(18f);
            m_PreviewFrame.style.borderBottomRightRadius = U(18f);
            m_PreviewFrame.style.paddingLeft = U(20f);
            m_PreviewFrame.style.paddingRight = U(20f);
            m_PreviewFrame.style.paddingTop = U(20f);
            m_PreviewFrame.style.paddingBottom = U(20f);
            m_PreviewFrame.style.borderLeftWidth = U(4f);
            m_PreviewFrame.style.borderRightWidth = U(4f);
            m_PreviewFrame.style.borderTopWidth = U(4f);
            m_PreviewFrame.style.borderBottomWidth = U(8f);
            m_PreviewFrame.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_PreviewFrame.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_PreviewFrame.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_PreviewFrame.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_RightPane.Add(m_PreviewFrame);

            m_PreviewHud = new VisualElement();
            m_PreviewHud.style.flexDirection = FlexDirection.Row;
            m_PreviewHud.style.justifyContent = Justify.SpaceBetween;
            m_PreviewHud.style.alignItems = Align.Center;
            m_PreviewHud.style.position = Position.Absolute;
            m_PreviewHud.style.left = U(16f);
            m_PreviewHud.style.right = U(16f);
            m_PreviewHud.style.top = U(16f);
            m_PreviewHud.style.marginBottom = 0f;
            m_PreviewHud.pickingMode = PickingMode.Ignore;
            m_PreviewFrame.Add(m_PreviewHud);

            m_PreviewBadgeLabel = new Label("PREVIEW");
            ApplyRuntimeFont(m_PreviewBadgeLabel);
            m_PreviewBadgeLabel.style.paddingLeft = U(14f);
            m_PreviewBadgeLabel.style.paddingRight = U(14f);
            m_PreviewBadgeLabel.style.height = U(34f);
            m_PreviewBadgeLabel.style.backgroundColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            m_PreviewBadgeLabel.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_PreviewBadgeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_PreviewBadgeLabel.style.fontSize = T(15f);
            m_PreviewBadgeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_PreviewBadgeLabel.style.borderTopLeftRadius = U(12f);
            m_PreviewBadgeLabel.style.borderTopRightRadius = U(12f);
            m_PreviewBadgeLabel.style.borderBottomLeftRadius = U(12f);
            m_PreviewBadgeLabel.style.borderBottomRightRadius = U(12f);
            m_PreviewHud.Add(m_PreviewBadgeLabel);

            m_AnimationStatusLabel = new Label("STANDBY");
            ApplyRuntimeFont(m_AnimationStatusLabel);
            m_AnimationStatusLabel.style.paddingLeft = U(14f);
            m_AnimationStatusLabel.style.paddingRight = U(14f);
            m_AnimationStatusLabel.style.height = U(34f);
            m_AnimationStatusLabel.style.backgroundColor = new Color(0.15f, 0.56f, 0.9f, 1f);
            m_AnimationStatusLabel.style.color = new Color(1f, 0.99f, 0.95f, 1f);
            m_AnimationStatusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_AnimationStatusLabel.style.fontSize = T(15f);
            m_AnimationStatusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_AnimationStatusLabel.style.borderTopLeftRadius = U(12f);
            m_AnimationStatusLabel.style.borderTopRightRadius = U(12f);
            m_AnimationStatusLabel.style.borderBottomLeftRadius = U(12f);
            m_AnimationStatusLabel.style.borderBottomRightRadius = U(12f);
            m_PreviewHud.Add(m_AnimationStatusLabel);

            m_PreviewImage = new Image();
            m_PreviewImage.scaleMode = ScaleMode.ScaleToFit;
            m_PreviewImage.style.flexGrow = 1f;
            m_PreviewImage.style.minHeight = U(300f);
            m_PreviewImage.style.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            m_PreviewImage.style.borderTopLeftRadius = U(16f);
            m_PreviewImage.style.borderTopRightRadius = U(16f);
            m_PreviewImage.style.borderBottomLeftRadius = U(16f);
            m_PreviewImage.style.borderBottomRightRadius = U(16f);
            m_PreviewImage.style.borderLeftWidth = U(3f);
            m_PreviewImage.style.borderRightWidth = U(3f);
            m_PreviewImage.style.borderTopWidth = U(3f);
            m_PreviewImage.style.borderBottomWidth = U(6f);
            m_PreviewImage.style.borderLeftColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            m_PreviewImage.style.borderRightColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            m_PreviewImage.style.borderTopColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            m_PreviewImage.style.borderBottomColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            m_PreviewImage.style.unityBackgroundImageTintColor = Color.white;
            m_PreviewImage.pickingMode = PickingMode.Position;
            m_PreviewImage.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                EnsurePreviewTexture();
                ApplyPreviewCameraView();
                if (m_IsOpen)
                {
                    RequestPreviewRender();
                }
            });
            m_PreviewImage.RegisterCallback<PointerDownEvent>(HandlePreviewPointerDown);
            m_PreviewImage.RegisterCallback<PointerMoveEvent>(HandlePreviewPointerMove);
            m_PreviewImage.RegisterCallback<PointerUpEvent>(HandlePreviewPointerUp);
            m_PreviewImage.RegisterCallback<PointerCaptureOutEvent>(HandlePreviewPointerCaptureOut);
            m_PreviewImage.RegisterCallback<WheelEvent>(HandlePreviewWheel);
            m_PreviewFrame.Add(m_PreviewImage);
            m_PreviewHud.BringToFront();

            m_SelectionCard = new VisualElement();
            m_SelectionCard.style.marginTop = U(20f);
            m_SelectionCard.style.flexShrink = 0f;
            m_SelectionCard.style.minHeight = U(156f);
            m_SelectionCard.style.paddingLeft = U(22f);
            m_SelectionCard.style.paddingRight = U(22f);
            m_SelectionCard.style.paddingTop = U(18f);
            m_SelectionCard.style.paddingBottom = U(18f);
            m_SelectionCard.style.backgroundColor = new Color(0.995f, 0.98f, 0.94f, 1f);
            m_SelectionCard.style.borderLeftWidth = U(4f);
            m_SelectionCard.style.borderRightWidth = U(4f);
            m_SelectionCard.style.borderTopWidth = U(4f);
            m_SelectionCard.style.borderBottomWidth = U(8f);
            m_SelectionCard.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SelectionCard.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SelectionCard.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SelectionCard.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SelectionCard.style.borderTopLeftRadius = U(18f);
            m_SelectionCard.style.borderTopRightRadius = U(18f);
            m_SelectionCard.style.borderBottomLeftRadius = U(18f);
            m_SelectionCard.style.borderBottomRightRadius = U(18f);
            m_RightPane.Add(m_SelectionCard);

            m_SelectionLabel = new Label("Pick a look");
            ApplyRuntimeFont(m_SelectionLabel);
            m_SelectionLabel.style.fontSize = T(34f);
            m_SelectionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_SelectionLabel.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SelectionLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_SelectionLabel.style.whiteSpace = WhiteSpace.NoWrap;
            m_SelectionLabel.style.overflow = Overflow.Hidden;
            m_SelectionLabel.style.marginBottom = U(8f);
            m_SelectionCard.Add(m_SelectionLabel);

            m_DetailLabel = new Label("Preview the selected look here.");
            ApplyRuntimeFont(m_DetailLabel);
            m_DetailLabel.style.whiteSpace = WhiteSpace.NoWrap;
            m_DetailLabel.style.fontSize = T(18f);
            m_DetailLabel.style.color = new Color(0.2f, 0.17f, 0.12f, 0.95f);
            m_DetailLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_DetailLabel.style.minHeight = 0f;
            m_DetailLabel.style.flexGrow = 0f;
            m_DetailLabel.style.flexShrink = 1f;
            m_DetailLabel.style.overflow = Overflow.Hidden;
            m_DetailLabel.style.marginBottom = U(10f);
            m_SelectionCard.Add(m_DetailLabel);

            m_ActionsRow = new VisualElement();
            m_ActionsRow.style.flexDirection = FlexDirection.Row;
            m_ActionsRow.style.flexShrink = 0f;
            m_ActionsRow.style.marginTop = U(4f);
            m_SelectionCard.Add(m_ActionsRow);

            m_RandomizeButton = CreateActionButton("Shuffle", RandomizeSelection, new Color(0.35f, 0.58f, 0.95f, 1f));
            m_RandomizeButton.style.flexGrow = 1f;
            m_RandomizeButton.style.marginRight = U(14f);
            m_ActionsRow.Add(m_RandomizeButton);

            m_ApplyButton = CreateActionButton("Apply", ApplySelection, new Color(0.9f, 0.68f, 0.26f, 1f));
            m_ApplyButton.style.flexGrow = 1f;
            m_ActionsRow.Add(m_ApplyButton);
        }

        private void ConfigureListViewScrollArea()
        {
            if (m_ListView == null)
            {
                return;
            }

            m_ListView.style.paddingBottom = U(6f);
            m_ListView.style.marginBottom = U(2f);

            var scrollView = m_ListView.Q<ScrollView>();
            if (scrollView == null)
            {
                return;
            }

            scrollView.mode = ScrollViewMode.Vertical;
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scrollView.style.flexGrow = 1f;
            scrollView.style.paddingBottom = U(8f);
            scrollView.style.paddingRight = U(4f);

            if (scrollView.contentContainer != null)
            {
                scrollView.contentContainer.style.paddingBottom = U(8f);
                scrollView.contentContainer.style.paddingRight = U(4f);
            }

            var verticalScroller = scrollView.verticalScroller;
            if (verticalScroller != null)
            {
                verticalScroller.style.width = U(m_IsCompactLayout ? 10f : 12f);
                verticalScroller.style.minWidth = U(m_IsCompactLayout ? 10f : 12f);
                verticalScroller.style.backgroundColor = new Color(0.91f, 0.85f, 0.69f, 0.9f);
                verticalScroller.style.borderTopLeftRadius = U(8f);
                verticalScroller.style.borderTopRightRadius = U(8f);
                verticalScroller.style.borderBottomLeftRadius = U(8f);
                verticalScroller.style.borderBottomRightRadius = U(8f);
                verticalScroller.lowButton.style.display = DisplayStyle.None;
                verticalScroller.highButton.style.display = DisplayStyle.None;
                verticalScroller.slider.style.flexGrow = 1f;
            }
        }

        private void BuildPreviewRig()
        {
            if (m_PreviewRigRoot != null)
            {
                return;
            }

            m_PreviewRigRoot = new GameObject("CharacterCustomizationPreviewRig");
            m_PreviewRigRoot.hideFlags = HideFlags.HideInHierarchy;
            m_PreviewRigRoot.transform.position = new Vector3(10000f, 10000f, 10000f);

            var previewSpinRootObject = new GameObject("PreviewSpinRoot");
            previewSpinRootObject.hideFlags = HideFlags.HideInHierarchy;
            m_PreviewSpinRoot = previewSpinRootObject.transform;
            m_PreviewSpinRoot.SetParent(m_PreviewRigRoot.transform, false);

            var previewModelRootObject = new GameObject("PreviewModelRoot");
            previewModelRootObject.hideFlags = HideFlags.HideInHierarchy;
            m_PreviewModelRoot = previewModelRootObject.transform;
            m_PreviewModelRoot.SetParent(m_PreviewSpinRoot, false);

            var cameraObject = new GameObject("PreviewCamera", typeof(Camera));
            cameraObject.hideFlags = HideFlags.HideInHierarchy;
            cameraObject.transform.SetParent(m_PreviewRigRoot.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.04f, -2.36f);
            cameraObject.transform.localRotation = Quaternion.identity;
            m_PreviewCamera = cameraObject.GetComponent<Camera>();
            m_PreviewCamera.enabled = false;
            m_PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            m_PreviewCamera.backgroundColor = previewClearColor;
            var effectivePreviewLayer = GetEffectivePreviewLayer();
            m_PreviewCamera.cullingMask = effectivePreviewLayer >= 0 ? 1 << effectivePreviewLayer : ~0;
            m_PreviewCamera.nearClipPlane = 0.01f;
            m_PreviewCamera.farClipPlane = 100f;
            m_PreviewCamera.fieldOfView = previewFieldOfView;
            m_PreviewCamera.allowMSAA = true;
            m_PreviewCamera.allowHDR = false;

            var lightObject = new GameObject("PreviewLight", typeof(Light));
            lightObject.hideFlags = HideFlags.HideInHierarchy;
            lightObject.transform.SetParent(m_PreviewRigRoot.transform, false);
            lightObject.transform.localPosition = new Vector3(1.6f, 2.2f, -1.8f);
            lightObject.transform.localRotation = Quaternion.Euler(38f, 214f, 0f);
            m_PreviewLight = lightObject.GetComponent<Light>();
            m_PreviewLight.type = LightType.Directional;
            m_PreviewLight.intensity = 1.7f;
            m_PreviewLight.color = new Color(1f, 0.98f, 0.94f, 1f);

            m_PreviewAnimator = m_PreviewModelRoot.gameObject.AddComponent<Animator>();
            m_PreviewAnimator.enabled = true;
            m_PreviewAnimator.applyRootMotion = false;
            m_PreviewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            m_PreviewAnimator.fireEvents = false;
            m_PreviewAnimator.logWarnings = false;

            EnsurePreviewTexture();
        }

        private void TearDownPreview()
        {
            StopPreviewAnimationPlayback();

            if (m_PreviewTexture != null)
            {
                m_PreviewTexture.Release();
                DestroySmart(m_PreviewTexture);
                m_PreviewTexture = null;
            }

            if (m_PreviewRigRoot != null)
            {
                DestroySmart(m_PreviewRigRoot);
                m_PreviewRigRoot = null;
                m_PreviewSpinRoot = null;
                m_PreviewModelRoot = null;
                m_PreviewAnimator = null;
                m_PreviewCamera = null;
                m_PreviewLight = null;
            }

            m_PreviewModelInstance = null;
            m_PreviewDirty = true;
            m_IsBuilt = false;
        }

        private void EnsurePreviewTexture()
        {
            int targetHeight = previewResolution;
            int targetWidth = Mathf.Max(previewResolution, Mathf.RoundToInt(targetHeight * GetPreviewTextureAspect()));
            if (m_PreviewTexture != null && m_PreviewTexture.width == targetWidth && m_PreviewTexture.height == targetHeight)
            {
                return;
            }

            if (m_PreviewTexture != null)
            {
                m_PreviewTexture.Release();
                DestroySmart(m_PreviewTexture);
            }

            m_PreviewTexture = new RenderTexture(targetWidth, targetHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "CharacterCustomizationPreview",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            m_PreviewTexture.Create();
            if (m_PreviewCamera != null)
            {
                m_PreviewCamera.targetTexture = m_PreviewTexture;
            }

            if (m_PreviewImage != null)
            {
                m_PreviewImage.image = m_PreviewTexture;
            }

            m_PreviewDirty = true;
        }

        private float GetPreviewTextureAspect()
        {
            if (m_PreviewImage == null)
            {
                return DefaultPreviewTextureAspect;
            }

            var width = m_PreviewImage.resolvedStyle.width;
            var height = m_PreviewImage.resolvedStyle.height;
            if (width < 1f || height < 1f)
            {
                return DefaultPreviewTextureAspect;
            }

            return Mathf.Clamp(width / height, 1.08f, 1.3f);
        }

        private void RefreshCatalog()
        {
            m_ActiveCatalog = catalogOverride != null ? catalogOverride : CharacterCustomizationCatalogRegistry.GetActiveCatalog();
            m_AllPresets.Clear();

            if (m_ActiveCatalog == null || m_ActiveCatalog.Presets == null)
            {
                UpdateStatus("No character catalog found.");
                return;
            }

            for (int index = 0; index < m_ActiveCatalog.Presets.Count; index++)
            {
                var preset = m_ActiveCatalog.Presets[index];
                if (preset != null && preset.characterPrefab != null && !string.IsNullOrWhiteSpace(preset.id))
                {
                    m_AllPresets.Add(preset);
                }
            }

            UpdatePresetCountLabel();
        }

        private void RefreshLocalPlayerReferences()
        {
            m_LocalPlayerManager = null;
            m_LocalPlayerState = null;
            m_LocalPlayerCoreAnimator = null;
            m_LocalPlayerAnimator = null;
            var contextScene = GetContextScene();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                var playerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (playerObject != null && IsInScene(playerObject.gameObject, contextScene))
                {
                    CacheLocalPlayerFromRoot(playerObject.gameObject);
                }
            }

            if (m_LocalPlayerState == null && m_LocalPlayerManager == null && m_LocalPlayerCoreAnimator == null)
            {
                TryResolveLikelyLocalPlayerFromScene(contextScene);
            }

            if (m_LocalPlayerState == null)
            {
                var playerStates = FindObjectsByType<CorePlayerState>(FindObjectsSortMode.None);
                CorePlayerState fallbackState = null;
                for (int index = 0; index < playerStates.Length; index++)
                {
                    var state = playerStates[index];
                    if (state == null || !IsInScene(state.gameObject, contextScene))
                    {
                        continue;
                    }

                    if (state.IsOwner)
                    {
                        fallbackState = state;
                        break;
                    }

                    if (fallbackState == null)
                    {
                        fallbackState = state;
                    }
                }

                if (fallbackState != null)
                {
                    CacheLocalPlayerFromRoot(fallbackState.gameObject);
                }
            }

            if (m_LocalPlayerState == null)
            {
                var addons = FindObjectsByType<CharacterCustomizationAddon>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                CharacterCustomizationAddon fallbackAddon = null;
                for (int index = 0; index < addons.Length; index++)
                {
                    var addon = addons[index];
                    if (addon == null || !IsInScene(addon.gameObject, contextScene))
                    {
                        continue;
                    }

                    var state = addon.GetComponent<CorePlayerState>();
                    if (state != null && state.IsOwner)
                    {
                        fallbackAddon = addon;
                        break;
                    }

                    if (fallbackAddon == null)
                    {
                        fallbackAddon = addon;
                    }
                }

                if (fallbackAddon != null)
                {
                    CacheLocalPlayerFromRoot(fallbackAddon.gameObject);
                }
            }

            if (m_LocalPlayerCoreAnimator == null)
            {
                m_LocalPlayerCoreAnimator = ResolveBestCoreAnimatorCandidate(contextScene);
            }

            if (m_LocalPlayerAnimator == null && m_LocalPlayerCoreAnimator != null)
            {
                m_LocalPlayerAnimator = m_LocalPlayerCoreAnimator.BoundAnimator != null
                    ? m_LocalPlayerCoreAnimator.BoundAnimator
                    : m_LocalPlayerCoreAnimator.GetComponent<Animator>();
            }

            if (m_PreviewAnimator != null && m_LocalPlayerAnimator != null)
            {
                m_PreviewAnimator.runtimeAnimatorController = m_LocalPlayerAnimator.runtimeAnimatorController;
            }
        }

        private bool TryResolveLikelyLocalPlayerFromScene(Scene contextScene)
        {
            GameObject taggedPlayer = null;
            try
            {
                taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            }
            catch (UnityException)
            {
                taggedPlayer = null;
            }

            if (IsInScene(taggedPlayer, contextScene))
            {
                CacheLocalPlayerFromRoot(taggedPlayer);
                if (m_LocalPlayerCoreAnimator != null)
                {
                    return true;
                }
            }

            var movements = FindObjectsByType<CoreMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            CoreMovement fallbackByName = null;
            CoreMovement fallbackByAvailability = null;

            for (int index = 0; index < movements.Length; index++)
            {
                var movement = movements[index];
                if (movement == null || !IsInScene(movement.gameObject, contextScene))
                {
                    continue;
                }

                var root = movement.gameObject;
                if (movement.IsOwner)
                {
                    CacheLocalPlayerFromRoot(root);
                    if (m_LocalPlayerCoreAnimator != null)
                    {
                        return true;
                    }
                }

                if (fallbackByName == null &&
                    root.name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    fallbackByName = movement;
                }

                if (fallbackByAvailability == null)
                {
                    fallbackByAvailability = movement;
                }
            }

            if (fallbackByName != null)
            {
                CacheLocalPlayerFromRoot(fallbackByName.gameObject);
            }
            else if (fallbackByAvailability != null)
            {
                CacheLocalPlayerFromRoot(fallbackByAvailability.gameObject);
            }

            return m_LocalPlayerCoreAnimator != null;
        }

        private void CacheLocalPlayerFromRoot(GameObject rootObject)
        {
            if (rootObject == null)
            {
                return;
            }

            if (m_LocalPlayerState == null)
            {
                m_LocalPlayerState = rootObject.GetComponent<CorePlayerState>() ??
                                     rootObject.GetComponentInParent<CorePlayerState>() ??
                                     rootObject.GetComponentInChildren<CorePlayerState>(true);
            }

            if (m_LocalPlayerManager == null)
            {
                m_LocalPlayerManager = rootObject.GetComponent<CorePlayerManager>() ??
                                       rootObject.GetComponentInParent<CorePlayerManager>() ??
                                       rootObject.GetComponentInChildren<CorePlayerManager>(true);
            }

            if (m_LocalPlayerCoreAnimator == null)
            {
                m_LocalPlayerCoreAnimator = rootObject.GetComponent<CoreAnimator>() ??
                                            rootObject.GetComponentInParent<CoreAnimator>() ??
                                            rootObject.GetComponentInChildren<CoreAnimator>(true);
            }

            if (m_LocalPlayerAnimator == null && m_LocalPlayerCoreAnimator != null)
            {
                m_LocalPlayerAnimator = m_LocalPlayerCoreAnimator.BoundAnimator;
            }

            if (m_LocalPlayerAnimator == null)
            {
                m_LocalPlayerAnimator = rootObject.GetComponentInChildren<Animator>(true);
            }
        }

        private void RefreshSelectionFromPlayer()
        {
            var currentPresetId = string.Empty;

            if (m_LocalPlayerState != null)
            {
                currentPresetId = m_LocalPlayerState.CharacterPresetId;
            }

            if (string.IsNullOrWhiteSpace(currentPresetId))
            {
                currentPresetId = CharacterCustomizationStorage.LoadSelectedPresetId();
            }

            if (string.IsNullOrWhiteSpace(currentPresetId) && m_ActiveCatalog != null)
            {
                var defaultPreset = m_ActiveCatalog.GetDefaultPreset();
                if (defaultPreset != null)
                {
                    currentPresetId = defaultPreset.id;
                }
            }

            if (!string.IsNullOrWhiteSpace(currentPresetId))
            {
                SelectPresetById(currentPresetId, true);
            }
            else if (m_AllPresets.Count > 0)
            {
                SelectPreset(m_AllPresets[0], true);
            }
        }

        private void RefreshFilteredList()
        {
            m_FilteredPresets.Clear();

            for (int index = 0; index < m_AllPresets.Count; index++)
            {
                var preset = m_AllPresets[index];
                if (preset == null)
                {
                    continue;
                }

                if (MatchesFilter(preset, m_SearchText))
                {
                    m_FilteredPresets.Add(preset);
                }
            }

            m_ListView.itemsSource = m_FilteredPresets;
            if (m_ListView.panel != null)
            {
                m_ListView.Rebuild();
            }
            UpdatePresetCountLabel();

            if (m_SelectedPreset != null)
            {
                var selectedIndex = m_FilteredPresets.IndexOf(m_SelectedPreset);
                if (selectedIndex >= 0)
                {
                    SetListSelectionWithoutNotify(selectedIndex);
                }
                else if (m_FilteredPresets.Count > 0)
                {
                    SelectPreset(m_FilteredPresets[0], true);
                }
                else
                {
                    m_SelectedPreset = null;
                    UpdatePreviewLabel();
                }
            }
        }

        private bool MatchesFilter(CharacterCustomizationPreset preset, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return true;
            }

            filter = filter.Trim();
            return preset.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   preset.id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (!string.IsNullOrWhiteSpace(preset.sourceAssetPath) && preset.sourceAssetPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void SelectPresetById(string presetId, bool suppressNotify)
        {
            for (int index = 0; index < m_AllPresets.Count; index++)
            {
                var preset = m_AllPresets[index];
                if (preset != null && string.Equals(preset.id, presetId, StringComparison.OrdinalIgnoreCase))
                {
                    SelectPreset(preset, suppressNotify);
                    return;
                }
            }
        }

        private void SelectPreset(CharacterCustomizationPreset preset, bool suppressNotify)
        {
            if (preset == null)
            {
                return;
            }

            m_SelectedPreset = preset;
            UpdatePreviewLabel();
            UpdateActionsState();

            if (!suppressNotify)
            {
                var filteredIndex = m_FilteredPresets.IndexOf(preset);
                if (filteredIndex >= 0)
                {
                    SetListSelectionWithoutNotify(filteredIndex);
                }
            }

            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (m_SelectedPreset == null || m_SelectedPreset.characterPrefab == null || m_PreviewAnimator == null)
            {
                ResetPreviewViewControls();
                m_DetailLabel.text = "Pick a look to preview.";
                if (m_AnimationStatusLabel != null)
                {
                    m_AnimationStatusLabel.text = "STANDBY";
                }
                StopPreviewAnimationPlayback();
                if (m_PreviewImage != null)
                {
                    m_PreviewImage.image = m_PreviewTexture;
                }
                return;
            }

            EnsurePreviewTexture();

            if (m_PreviewAnimator != null)
            {
                m_PreviewAnimator.runtimeAnimatorController = m_LocalPlayerAnimator != null
                    ? m_LocalPlayerAnimator.runtimeAnimatorController
                    : m_PreviewAnimator.runtimeAnimatorController;
            }

            if (m_PreviewSpinRoot != null)
            {
                ResetPreviewViewControls();
                ApplyPreviewSpinRotation();
            }

            var effectivePreviewLayer = GetEffectivePreviewLayer();
            if (!CharacterRigUtility.TryApplyPreset(
                    m_PreviewModelRoot,
                    m_PreviewAnimator,
                    m_SelectedPreset.characterPrefab,
                    out m_PreviewModelInstance,
                    effectivePreviewLayer,
                    true))
            {
                m_DetailLabel.text = "Preview unavailable.";
                return;
            }

            if (m_PreviewModelInstance != null && CharacterRigUtility.TryCalculateBounds(m_PreviewModelInstance, out var bounds))
            {
                var localCenter = m_PreviewSpinRoot != null
                    ? m_PreviewSpinRoot.InverseTransformPoint(bounds.center)
                    : bounds.center;
                var localMin = m_PreviewSpinRoot != null
                    ? m_PreviewSpinRoot.InverseTransformPoint(bounds.min)
                    : bounds.min;
                var localMax = m_PreviewSpinRoot != null
                    ? m_PreviewSpinRoot.InverseTransformPoint(bounds.max)
                    : bounds.max;
                var modelHeight = Mathf.Max(localMax.y - localMin.y, 0.9f);

                m_PreviewModelRoot.localPosition = new Vector3(
                    -localCenter.x,
                    -localMin.y + 0.02f,
                    -localCenter.z);

                var framingRadius = Mathf.Max(bounds.extents.x * 1.1f, modelHeight * 0.47f, 0.42f);
                var fieldOfViewRadians = previewFieldOfView * Mathf.Deg2Rad * 0.5f;
                var distance = framingRadius / Mathf.Tan(fieldOfViewRadians);
                m_PreviewCamera.fieldOfView = previewFieldOfView;
                var lookY = Mathf.Clamp(modelHeight * 0.52f, 0.82f, 1.24f);
                var cameraY = Mathf.Clamp(modelHeight * 0.58f, 0.9f, 1.36f);
                m_PreviewLookLocalPoint = new Vector3(0f, lookY, 0f);
                m_PreviewCameraVerticalOffset = cameraY - m_PreviewLookLocalPoint.y;
                m_PreviewCameraDistance = distance * 0.94f;
                ApplyPreviewCameraView();
            }

            RebuildPreviewAnimationPlaylist();

            m_PreviewCamera.targetTexture = m_PreviewTexture;
            m_PreviewImage.image = m_PreviewTexture;
            SetPreviewCameraActive(m_IsOpen);

            m_DetailLabel.text = BuildSelectionDetailText();
            if (m_IsOpen)
            {
                RequestPreviewRender();
            }
            else
            {
                m_PreviewDirty = true;
            }
        }

        private void UpdatePreviewLabel()
        {
            if (m_SelectedPreset == null)
            {
                m_SelectionLabel.text = "Pick a look";
                return;
            }

            m_SelectionLabel.text = m_SelectedPreset.DisplayName;
        }

        private void UpdateStatus(string overrideText = null)
        {
            if (!string.IsNullOrWhiteSpace(overrideText))
            {
                m_StatusLabel.text = overrideText;
                return;
            }

            if (m_SelectedPreset == null)
            {
                m_StatusLabel.text = "No look selected.";
                return;
            }

            if (m_AppliedPreset != null && string.Equals(m_AppliedPreset.id, m_SelectedPreset.id, StringComparison.OrdinalIgnoreCase))
            {
                m_StatusLabel.text = $"Applied: {m_SelectedPreset.DisplayName}";
            }
            else
            {
                m_StatusLabel.text = $"Preview: {m_SelectedPreset.DisplayName}";
            }
        }

        private void UpdateActionsState()
        {
            bool hasSelection = m_SelectedPreset != null;
            if (m_ApplyButton != null)
            {
                m_ApplyButton.SetEnabled(hasSelection);
            }

            if (m_RandomizeButton != null)
            {
                m_RandomizeButton.SetEnabled(m_AllPresets.Count > 0);
            }

            UpdateStatus();
            m_ListView?.RefreshItems();
        }

        private void UpdatePresetCountLabel()
        {
            if (m_PresetCountLabel == null)
            {
                return;
            }

            int visibleCount = m_FilteredPresets.Count > 0 || !string.IsNullOrWhiteSpace(m_SearchText)
                ? m_FilteredPresets.Count
                : m_AllPresets.Count;
            m_PresetCountLabel.text = $"{visibleCount:00} LOOKS";
        }

        private void RebuildPreviewAnimationPlaylist()
        {
            StopPreviewAnimationPlayback();

            if (m_PreviewAnimator == null)
            {
                return;
            }

            m_PreviewCycleClips.Clear();
            m_PreviewClipDeduplication.Clear();

            CollectPreviewAnimationClips(m_PreviewAnimator.runtimeAnimatorController);
            if (m_LocalPlayerAnimator != null && m_LocalPlayerAnimator.runtimeAnimatorController != m_PreviewAnimator.runtimeAnimatorController)
            {
                CollectPreviewAnimationClips(m_LocalPlayerAnimator.runtimeAnimatorController);
            }

            if (m_PreviewCycleClips.Count == 0)
            {
                if (m_AnimationStatusLabel != null)
                {
                    m_AnimationStatusLabel.text = "STATIC";
                }

                return;
            }

            EnsurePreviewAnimationGraph();
            PlayPreviewClipImmediate(0);
        }

        private void CollectPreviewAnimationClips(RuntimeAnimatorController controller)
        {
            if (controller == null)
            {
                return;
            }

            AddPreviewClipsByKeywords(controller, "idle", "stand");
            AddPreviewClipsByKeywords(controller, "walk", "run", "move");
            AddPreviewClipsByKeywords(controller, "emoji", "smile", "hi", "nice", "showmanship", "reaction", "dance");

            foreach (var clip in controller.animationClips)
            {
                TryRegisterPreviewClip(clip);
                if (m_PreviewCycleClips.Count >= 6)
                {
                    break;
                }
            }
        }

        private void AddPreviewClipsByKeywords(RuntimeAnimatorController controller, params string[] keywords)
        {
            if (controller == null || keywords == null || keywords.Length == 0)
            {
                return;
            }

            foreach (var clip in controller.animationClips)
            {
                if (clip == null)
                {
                    continue;
                }

                var clipName = clip.name ?? string.Empty;
                for (int index = 0; index < keywords.Length; index++)
                {
                    if (clipName.IndexOf(keywords[index], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        TryRegisterPreviewClip(clip);
                        break;
                    }
                }

                if (m_PreviewCycleClips.Count >= 6)
                {
                    return;
                }
            }
        }

        private void TryRegisterPreviewClip(AnimationClip clip)
        {
            if (clip == null || clip.empty || !m_PreviewClipDeduplication.Add(clip))
            {
                return;
            }

            m_PreviewCycleClips.Add(clip);
        }

        private void EnsurePreviewAnimationGraph()
        {
            if (m_PreviewAnimator == null || m_PreviewAnimationGraph.IsValid())
            {
                return;
            }

            m_PreviewAnimationGraph = PlayableGraph.Create("CharacterCustomizationPreviewAnimation");
            m_PreviewAnimationGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(m_PreviewAnimationGraph, "PreviewOutput", m_PreviewAnimator);
            m_PreviewAnimationMixer = AnimationMixerPlayable.Create(m_PreviewAnimationGraph, 2);
            output.SetSourcePlayable(m_PreviewAnimationMixer);
            m_PreviewAnimationMixer.SetInputWeight(0, 0f);
            m_PreviewAnimationMixer.SetInputWeight(1, 0f);
            m_PreviewAnimationGraph.Play();
        }

        private void PlayPreviewClipImmediate(int clipIndex)
        {
            if (!m_PreviewAnimationGraph.IsValid() ||
                clipIndex < 0 ||
                clipIndex >= m_PreviewCycleClips.Count)
            {
                return;
            }

            DisconnectPreviewPlayable(ref m_PreviewPrimaryPlayable, 0, true);
            DisconnectPreviewPlayable(ref m_PreviewSecondaryPlayable, 1, true);

            var clip = m_PreviewCycleClips[clipIndex];
            m_PreviewPrimaryPlayable = CreatePreviewClipPlayable(clip);
            m_PreviewAnimationGraph.Connect(m_PreviewPrimaryPlayable, 0, m_PreviewAnimationMixer, 0);
            m_PreviewAnimationMixer.SetInputWeight(0, 1f);
            m_PreviewAnimationMixer.SetInputWeight(1, 0f);
            m_CurrentPreviewClipIndex = clipIndex;
            m_NextPreviewClipIndex = -1;
            m_PreviewBlendActive = false;
            m_NextPreviewClipSwitchAt = Time.unscaledTime + GetPreviewShowcaseDuration(clip);
            UpdatePreviewAnimationLabel(clip);
            m_PreviewDirty = true;
        }

        private void UpdatePreviewAnimationCycle()
        {
            if (!m_IsOpen || !m_PreviewAnimationGraph.IsValid() || m_PreviewCycleClips.Count == 0)
            {
                return;
            }

            if (m_PreviewBlendActive)
            {
                var normalized = Mathf.Clamp01((Time.unscaledTime - m_PreviewBlendStartedAt) / PreviewAnimationBlendDurationSeconds);
                m_PreviewAnimationMixer.SetInputWeight(0, 1f - normalized);
                m_PreviewAnimationMixer.SetInputWeight(1, normalized);
                m_PreviewDirty = true;

                if (normalized >= 1f)
                {
                    FinishPreviewBlend();
                }

                return;
            }

            if (m_PreviewCycleClips.Count <= 1 || Time.unscaledTime < m_NextPreviewClipSwitchAt)
            {
                return;
            }

            int nextIndex = (m_CurrentPreviewClipIndex + 1) % m_PreviewCycleClips.Count;
            BeginPreviewBlend(nextIndex);
        }

        private void BeginPreviewBlend(int nextClipIndex)
        {
            if (!m_PreviewAnimationGraph.IsValid() ||
                nextClipIndex < 0 ||
                nextClipIndex >= m_PreviewCycleClips.Count ||
                nextClipIndex == m_CurrentPreviewClipIndex)
            {
                return;
            }

            DisconnectPreviewPlayable(ref m_PreviewSecondaryPlayable, 1, true);
            m_PreviewSecondaryPlayable = CreatePreviewClipPlayable(m_PreviewCycleClips[nextClipIndex]);
            m_PreviewAnimationGraph.Connect(m_PreviewSecondaryPlayable, 0, m_PreviewAnimationMixer, 1);
            m_PreviewAnimationMixer.SetInputWeight(0, 1f);
            m_PreviewAnimationMixer.SetInputWeight(1, 0f);
            m_PreviewBlendStartedAt = Time.unscaledTime;
            m_PreviewBlendActive = true;
            m_NextPreviewClipIndex = nextClipIndex;
            UpdatePreviewAnimationLabel(m_PreviewCycleClips[nextClipIndex]);
            m_PreviewDirty = true;
        }

        private void FinishPreviewBlend()
        {
            if (!m_PreviewAnimationGraph.IsValid() || m_NextPreviewClipIndex < 0 || !m_PreviewSecondaryPlayable.IsValid())
            {
                return;
            }

            DisconnectPreviewPlayable(ref m_PreviewPrimaryPlayable, 0, true);
            if (m_PreviewAnimationMixer.IsValid())
            {
                m_PreviewAnimationMixer.DisconnectInput(0);
                m_PreviewAnimationMixer.DisconnectInput(1);
                m_PreviewAnimationGraph.Connect(m_PreviewSecondaryPlayable, 0, m_PreviewAnimationMixer, 0);
                m_PreviewAnimationMixer.SetInputWeight(0, 1f);
                m_PreviewAnimationMixer.SetInputWeight(1, 0f);
            }

            m_PreviewPrimaryPlayable = m_PreviewSecondaryPlayable;
            m_PreviewSecondaryPlayable = default;
            m_CurrentPreviewClipIndex = m_NextPreviewClipIndex;
            m_NextPreviewClipIndex = -1;
            m_PreviewBlendActive = false;
            var currentClip = m_PreviewCycleClips[m_CurrentPreviewClipIndex];
            m_NextPreviewClipSwitchAt = Time.unscaledTime + GetPreviewShowcaseDuration(currentClip);
            UpdatePreviewAnimationLabel(currentClip);
            m_PreviewDirty = true;
        }

        private void StopPreviewAnimationPlayback()
        {
            if (m_PreviewAnimationGraph.IsValid())
            {
                DisconnectPreviewPlayable(ref m_PreviewPrimaryPlayable, 0, true);
                DisconnectPreviewPlayable(ref m_PreviewSecondaryPlayable, 1, true);
                m_PreviewAnimationGraph.Destroy();
            }

            m_PreviewAnimationMixer = default;
            m_PreviewAnimationGraph = default;
            m_CurrentPreviewClipIndex = -1;
            m_NextPreviewClipIndex = -1;
            m_PreviewBlendActive = false;
            m_PreviewBlendStartedAt = 0f;
            m_NextPreviewClipSwitchAt = 0f;
        }

        private AnimationClipPlayable CreatePreviewClipPlayable(AnimationClip clip)
        {
            var playable = AnimationClipPlayable.Create(m_PreviewAnimationGraph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(1f);
            return playable;
        }

        private void DisconnectPreviewPlayable(ref AnimationClipPlayable playable, int inputIndex, bool destroyPlayable)
        {
            if (m_PreviewAnimationMixer.IsValid())
            {
                m_PreviewAnimationMixer.DisconnectInput(inputIndex);
                m_PreviewAnimationMixer.SetInputWeight(inputIndex, 0f);
            }

            if (destroyPlayable && playable.IsValid())
            {
                playable.Destroy();
            }

            playable = default;
        }

        private float GetPreviewShowcaseDuration(AnimationClip clip)
        {
            float clipLength = clip != null ? clip.length : PreviewAnimationMinimumShowcaseSeconds;
            return Mathf.Clamp(clipLength * 0.9f, PreviewAnimationMinimumShowcaseSeconds, PreviewAnimationMaximumShowcaseSeconds);
        }

        private void UpdatePreviewAnimationLabel(AnimationClip clip)
        {
            if (m_AnimationStatusLabel == null)
            {
                return;
            }

            if (clip == null)
            {
                m_AnimationStatusLabel.text = "STATIC";
                return;
            }

            m_AnimationStatusLabel.text = FormatPreviewAnimationName(clip.name);
        }

        private static string FormatPreviewAnimationName(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return "MOTION";
            }

            string normalized = clipName.Replace("Anim@", string.Empty)
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Trim();

            string lower = normalized.ToLowerInvariant();
            if (lower.Contains("idle") || lower.Contains("stand"))
            {
                return "IDLE";
            }

            if (lower.Contains("walk"))
            {
                return "WALK";
            }

            if (lower.Contains("run"))
            {
                return "RUN";
            }

            if (lower.Contains("jump"))
            {
                return "JUMP";
            }

            if (lower.Contains("land"))
            {
                return "LAND";
            }

            if (lower.Contains("dance"))
            {
                return "DANCE";
            }

            if (lower.Contains("wave") || lower.Contains("hi"))
            {
                return "WAVE";
            }

            if (lower.Contains("smile") || lower.Contains("emoji") || lower.Contains("react"))
            {
                return "EMOTE";
            }

            var tokens = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var compact = new List<string>(2);
            for (int i = 0; i < tokens.Length && compact.Count < 2; i++)
            {
                string token = tokens[i];
                if (token.Length <= 1 || token.Equals("anim", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                compact.Add(token);
            }

            if (compact.Count == 0)
            {
                return "MOTION";
            }

            string label = string.Join(" ", compact).ToUpperInvariant();
            return label.Length <= 12 ? label : label.Substring(0, 12).TrimEnd();
        }

        private string BuildSelectionDetailText()
        {
            if (m_SelectedPreset == null)
            {
                return "Pick a look to preview.";
            }

            return $"ID {m_SelectedPreset.id}";
        }

        private static string GetCompactSourceLabel(CharacterCustomizationPreset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.sourceAssetPath))
            {
                return "Catalog preset";
            }

            var path = preset.sourceAssetPath;
            int slashIndex = Mathf.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            string fileName = slashIndex >= 0 && slashIndex + 1 < path.Length
                ? path.Substring(slashIndex + 1)
                : path;
            int dotIndex = fileName.LastIndexOf('.');
            return dotIndex > 0 ? fileName.Substring(0, dotIndex) : fileName;
        }

        private void SetVisible(bool visible)
        {
            if (m_Root == null)
            {
                return;
            }

            m_Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            m_Root.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
            if (m_UIDocument != null)
            {
                m_UIDocument.sortingOrder = documentSortingOrder;
            }
            m_IsOpen = visible;
        }

        private void SuspendCompetingUi()
        {
            RestoreSuspendedUi();

            var documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < documents.Length; index++)
            {
                var document = documents[index];
                if (document == null || document == m_UIDocument || !document.enabled || !document.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var root = document.rootVisualElement;
                if (root == null)
                {
                    continue;
                }

                m_SuspendedDocuments.Add(new SuspendedDocumentState
                {
                    Document = document,
                    PreviousDisplay = root.resolvedStyle.display,
                    PreviousPickingMode = root.pickingMode
                });

                root.style.display = DisplayStyle.None;
                root.pickingMode = PickingMode.Ignore;
            }

            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < canvases.Length; index++)
            {
                var canvas = canvases[index];
                if (canvas == null ||
                    !canvas.enabled ||
                    !canvas.gameObject.activeInHierarchy ||
                    (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.renderMode != RenderMode.ScreenSpaceCamera))
                {
                    continue;
                }

                m_SuspendedCanvases.Add(canvas);
                canvas.enabled = false;
            }

            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < behaviours.Length; index++)
            {
                var behaviour = behaviours[index];
                if (behaviour == null || !behaviour.enabled || behaviour == this)
                {
                    continue;
                }

                var typeName = behaviour.GetType().Name;
                if (!(behaviour is InteractionDirector) &&
                    !typeName.EndsWith("InteractionBridge", StringComparison.Ordinal))
                {
                    continue;
                }

                m_SuspendedInteractionBehaviours.Add(behaviour);
                behaviour.enabled = false;
            }
        }

        private void RestoreSuspendedUi()
        {
            for (int index = 0; index < m_SuspendedDocuments.Count; index++)
            {
                var state = m_SuspendedDocuments[index];
                var document = state != null ? state.Document : null;
                if (document != null)
                {
                    var root = document.rootVisualElement;
                    if (root != null)
                    {
                        root.style.display = state.PreviousDisplay;
                        root.pickingMode = state.PreviousPickingMode;
                    }
                }
            }

            m_SuspendedDocuments.Clear();

            for (int index = 0; index < m_SuspendedCanvases.Count; index++)
            {
                var canvas = m_SuspendedCanvases[index];
                if (canvas != null)
                {
                    canvas.enabled = true;
                }
            }

            m_SuspendedCanvases.Clear();

            for (int index = 0; index < m_SuspendedInteractionBehaviours.Count; index++)
            {
                var behaviour = m_SuspendedInteractionBehaviours[index];
                if (behaviour != null)
                {
                    behaviour.enabled = true;
                }
            }

            m_SuspendedInteractionBehaviours.Clear();
        }

        private void EnsureUiInputBridge()
        {
            m_EventSystem = EventSystem.current != null
                ? EventSystem.current
                : FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);

            if (m_EventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                m_EventSystem = eventSystemObject.GetComponent<EventSystem>();
            }

            if (m_EventSystem == null)
            {
                return;
            }

            m_InputModule = m_EventSystem.currentInputModule as InputSystemUIInputModule;
            if (m_InputModule == null)
            {
                m_InputModule = m_EventSystem.GetComponent<InputSystemUIInputModule>();
                if (m_InputModule == null)
                {
                    m_InputModule = m_EventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                }

                m_EventSystem.UpdateModules();
            }

            if (m_InputModule != null && m_InputModule.actionsAsset == null)
            {
                m_InputModule.AssignDefaultActions();
            }

            m_StandaloneInputModule = m_EventSystem.GetComponent<StandaloneInputModule>();
            if (m_StandaloneInputModule == null)
            {
                m_StandaloneInputModule = m_EventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }

            m_EventSystem.enabled = true;
            if (m_InputModule != null)
            {
                m_InputModule.enabled = true;
            }

            if (m_StandaloneInputModule != null)
            {
                m_StandaloneInputModule.enabled = false;
            }

            m_EventSystem.UpdateModules();
            if (m_EventSystem.currentInputModule == null && m_StandaloneInputModule != null)
            {
                if (m_InputModule != null)
                {
                    m_InputModule.enabled = false;
                }

                m_StandaloneInputModule.enabled = true;
                m_EventSystem.UpdateModules();
            }
        }

        private void AcquireInteractionLock()
        {
            if (m_LockedInteractions)
            {
                return;
            }

            var contextScene = GetContextScene();
            if (m_InteractionDirector != null && !IsInScene(m_InteractionDirector.gameObject, contextScene))
            {
                m_InteractionDirector = null;
            }

            m_InteractionDirector ??= FindSceneComponent<InteractionDirector>(contextScene, true);
            if (m_InteractionDirector == null)
            {
                return;
            }

            m_InteractionDirector.SetInteractionsLocked(true);
            m_InteractionDirector.ClearFocusImmediate();
            m_LockedInteractions = true;
        }

        private void ApplyResponsiveLayout()
        {
            if (m_Window == null || m_Content == null || m_LeftPane == null || m_RightPane == null)
            {
                return;
            }

            RefreshUiScale();

            var viewportWidth = m_Overlay != null ? m_Overlay.resolvedStyle.width : 0f;
            var viewportHeight = m_Overlay != null ? m_Overlay.resolvedStyle.height : 0f;
            if (viewportWidth < 1f || viewportHeight < 1f)
            {
                return;
            }

            var viewportAspect = viewportWidth / Mathf.Max(1f, viewportHeight);
            var portraitScreen = Screen.height > Screen.width * 1.03f;
            m_IsPortraitLayout = portraitScreen || viewportAspect < 0.96f || viewportWidth < 980f;
            m_UseStackedLayout = m_IsPortraitLayout || viewportWidth < 1120f || viewportAspect < 1.14f;
            m_IsCompactLayout = m_IsPortraitLayout || viewportWidth < 1500f || viewportAspect < 1.52f;
            m_IsDenseListLayout = m_UseStackedLayout || viewportWidth < 1640f || viewportAspect < 1.68f;

            m_Overlay.style.justifyContent = m_IsCompactLayout ? Justify.FlexStart : Justify.Center;
            m_Overlay.style.alignItems = Align.Center;
            m_Overlay.style.backgroundColor = m_IsPortraitLayout
                ? new Color(0.02f, 0.015f, 0.01f, 0.94f)
                : new Color(0.03f, 0.02f, 0.015f, 0.84f);
            m_Overlay.style.paddingTop = m_IsPortraitLayout ? U(8f) : m_IsCompactLayout ? U(14f) : U(28f);
            m_Overlay.style.paddingBottom = m_IsPortraitLayout ? U(8f) : m_IsCompactLayout ? U(14f) : U(28f);
            m_Overlay.style.paddingLeft = m_IsPortraitLayout ? U(8f) : m_IsCompactLayout ? U(16f) : U(28f);
            m_Overlay.style.paddingRight = m_IsPortraitLayout ? U(8f) : m_IsCompactLayout ? U(16f) : U(28f);

            m_Window.style.width = new Length(m_IsPortraitLayout ? 100f : m_IsCompactLayout ? 95f : 88f, LengthUnit.Percent);
            m_Window.style.height = new Length(m_IsPortraitLayout ? 100f : m_IsCompactLayout ? 94f : 88f, LengthUnit.Percent);
            m_Window.style.minWidth = m_IsCompactLayout ? 0f : 1180f;
            m_Window.style.minHeight = m_IsCompactLayout ? 0f : 760f;
            m_Window.style.maxWidth = m_IsPortraitLayout
                ? viewportWidth
                : Mathf.Min(viewportWidth - U(20f), 1920f);
            m_Window.style.maxHeight = m_IsPortraitLayout
                ? viewportHeight
                : Mathf.Min(viewportHeight - U(20f), 1180f);
            m_Window.style.paddingLeft = m_IsPortraitLayout ? U(12f) : m_IsCompactLayout ? U(16f) : U(28f);
            m_Window.style.paddingRight = m_IsPortraitLayout ? U(12f) : m_IsCompactLayout ? U(16f) : U(28f);
            m_Window.style.paddingTop = m_IsPortraitLayout ? U(12f) : m_IsCompactLayout ? U(12f) : U(24f);
            m_Window.style.paddingBottom = m_IsPortraitLayout ? U(12f) : m_IsCompactLayout ? U(12f) : U(24f);
            m_Window.style.borderTopLeftRadius = m_IsPortraitLayout ? U(14f) : U(22f);
            m_Window.style.borderTopRightRadius = m_IsPortraitLayout ? U(14f) : U(22f);
            m_Window.style.borderBottomLeftRadius = m_IsPortraitLayout ? U(14f) : U(22f);
            m_Window.style.borderBottomRightRadius = m_IsPortraitLayout ? U(14f) : U(22f);

            m_Header.style.flexDirection = m_UseStackedLayout ? FlexDirection.Column : FlexDirection.Row;
            m_Header.style.alignItems = m_UseStackedLayout ? Align.Stretch : Align.FlexStart;
            m_Header.style.paddingBottom = m_IsPortraitLayout ? U(12f) : m_IsCompactLayout ? U(8f) : U(22f);

            m_TitleLabel.style.fontSize = m_IsPortraitLayout ? T(22f) : m_IsCompactLayout ? T(24f) : T(44f);
            m_TitleLabel.style.minHeight = m_IsPortraitLayout ? U(30f) : m_IsCompactLayout ? U(36f) : U(68f);
            m_StatusLabel.style.fontSize = m_IsPortraitLayout ? T(12f) : m_IsCompactLayout ? T(14f) : T(19f);
            m_StatusLabel.style.minHeight = m_IsPortraitLayout ? U(22f) : m_IsCompactLayout ? U(18f) : U(42f);
            m_SelectionLabel.style.fontSize = m_IsPortraitLayout ? T(20f) : m_IsCompactLayout ? T(24f) : T(30f);
            m_DetailLabel.style.fontSize = m_IsPortraitLayout ? T(12f) : m_IsCompactLayout ? T(13f) : T(16f);
            m_DetailLabel.style.minHeight = m_IsPortraitLayout ? StyleKeyword.Auto : m_IsCompactLayout ? U(18f) : U(48f);
            m_CloseButton.style.minWidth = m_IsPortraitLayout ? U(90f) : m_IsCompactLayout ? U(104f) : U(156f);
            m_CloseButton.style.height = m_IsPortraitLayout ? U(40f) : m_IsCompactLayout ? U(44f) : U(64f);
            m_CloseButton.style.fontSize = m_IsPortraitLayout ? T(15f) : m_IsCompactLayout ? T(17f) : T(22f);

            if (m_HeaderTitleGroup != null)
            {
                m_HeaderTitleGroup.style.marginRight = m_UseStackedLayout ? 0f : U(24f);
            }

            if (m_HeaderActions != null)
            {
                m_HeaderActions.style.flexDirection = m_UseStackedLayout ? FlexDirection.Row : FlexDirection.Column;
                m_HeaderActions.style.alignItems = m_UseStackedLayout ? Align.FlexEnd : Align.FlexEnd;
                m_HeaderActions.style.justifyContent = m_UseStackedLayout ? Justify.FlexEnd : Justify.FlexStart;
                m_HeaderActions.style.minWidth = StyleKeyword.Auto;
                m_HeaderActions.style.marginTop = m_UseStackedLayout ? U(10f) : 0f;
            }

            if (m_HelperLabel != null)
            {
                m_HelperLabel.style.display = m_IsCompactLayout ? DisplayStyle.None : DisplayStyle.Flex;
                m_HelperLabel.style.marginBottom = m_IsCompactLayout ? 0f : U(12f);
                m_HelperLabel.style.marginRight = 0f;
                m_HelperLabel.style.flexGrow = 0f;
                m_HelperLabel.style.fontSize = m_IsCompactLayout ? T(14f) : T(16f);
            }

            m_Content.style.flexDirection = m_UseStackedLayout ? FlexDirection.Column : FlexDirection.Row;
            m_Content.style.alignItems = Align.Stretch;
            m_Content.style.flexShrink = 1f;
            m_Content.style.minHeight = 0f;
            m_Content.style.marginTop = m_IsPortraitLayout ? U(10f) : m_IsCompactLayout ? U(8f) : U(18f);

            m_RightPane.style.marginRight = 0f;
            m_RightPane.style.minHeight = 0f;
            m_RightPane.style.flexShrink = 1f;
            m_RightPane.style.flexGrow = m_IsPortraitLayout ? 0f : 1f;
            m_RightPane.style.flexBasis = 0f;

            float sidebarWidth = m_UseStackedLayout
                ? 0f
                : m_IsCompactLayout
                    ? Mathf.Clamp(viewportWidth * 0.24f, U(292f), U(380f))
                    : Mathf.Clamp(viewportWidth * 0.26f, U(320f), U(420f));
            m_LeftPane.style.width = m_UseStackedLayout
                ? new Length(100f, LengthUnit.Percent)
                : sidebarWidth;
            m_LeftPane.style.marginLeft = m_UseStackedLayout ? 0f : m_IsCompactLayout ? U(12f) : U(14f);
            m_LeftPane.style.marginTop = m_UseStackedLayout ? U(14f) : 0f;
            m_LeftPane.style.paddingLeft = m_IsPortraitLayout ? U(14f) : m_IsCompactLayout ? U(18f) : U(22f);
            m_LeftPane.style.paddingRight = m_IsPortraitLayout ? U(14f) : m_IsCompactLayout ? U(18f) : U(22f);
            m_LeftPane.style.paddingTop = m_IsPortraitLayout ? U(14f) : m_IsCompactLayout ? U(18f) : U(22f);
            m_LeftPane.style.paddingBottom = m_IsPortraitLayout ? U(14f) : m_IsCompactLayout ? U(18f) : U(22f);
            m_LeftPane.style.flexGrow = m_UseStackedLayout ? 1f : 0f;
            m_LeftPane.style.minHeight = m_UseStackedLayout
                ? Mathf.Clamp(viewportHeight * 0.36f, U(220f), U(420f))
                : 0f;
            m_LeftPane.style.height = StyleKeyword.Auto;

            float previewFramePaddingX = m_IsPortraitLayout ? U(14f) : m_IsCompactLayout ? U(16f) : U(20f);
            float previewFramePaddingY = m_IsPortraitLayout ? U(14f) : m_IsCompactLayout ? U(12f) : U(20f);
            float previewHudHeight = m_IsPortraitLayout ? U(28f) : m_IsCompactLayout ? U(32f) : U(34f);
            float previewHudInset = m_IsPortraitLayout ? U(10f) : m_IsCompactLayout ? U(12f) : U(16f);
            float previewImageMinHeight = m_IsPortraitLayout
                ? Mathf.Clamp(viewportHeight * 0.2f, U(150f), U(250f))
                : m_IsCompactLayout
                    ? Mathf.Clamp(viewportHeight * 0.25f, U(210f), U(280f))
                    : Mathf.Clamp(viewportHeight * 0.24f, U(230f), U(360f));
            float previewFrameHeight = m_IsPortraitLayout
                ? Mathf.Clamp(viewportHeight * 0.28f, U(200f), U(330f))
                : m_IsCompactLayout
                    ? Mathf.Clamp(viewportHeight * 0.38f, U(330f), U(460f))
                    : Mathf.Clamp(viewportHeight * 0.42f, U(420f), U(620f));
            previewFrameHeight = Mathf.Max(previewFrameHeight, previewFramePaddingY * 2f + previewImageMinHeight);
            m_PreviewFrame.style.flexGrow = m_UseStackedLayout ? 0f : 1f;
            m_PreviewFrame.style.height = previewFrameHeight;
            m_PreviewFrame.style.minHeight = previewFrameHeight;
            m_PreviewFrame.style.paddingLeft = previewFramePaddingX;
            m_PreviewFrame.style.paddingRight = previewFramePaddingX;
            m_PreviewFrame.style.paddingTop = previewFramePaddingY;
            m_PreviewFrame.style.paddingBottom = previewFramePaddingY;

            if (m_PreviewHud != null)
            {
                m_PreviewHud.style.left = previewHudInset;
                m_PreviewHud.style.right = previewHudInset;
                m_PreviewHud.style.top = previewHudInset;
                m_PreviewHud.style.minHeight = previewHudHeight;
                m_PreviewHud.style.marginBottom = 0f;
            }

            if (m_PreviewBadgeLabel != null)
            {
                m_PreviewBadgeLabel.style.height = m_IsPortraitLayout ? U(28f) : m_IsCompactLayout ? U(32f) : U(34f);
                m_PreviewBadgeLabel.style.fontSize = m_IsPortraitLayout ? T(12f) : m_IsCompactLayout ? T(13f) : T(15f);
            }

            if (m_AnimationStatusLabel != null)
            {
                m_AnimationStatusLabel.style.height = m_IsPortraitLayout ? U(28f) : m_IsCompactLayout ? U(32f) : U(34f);
                m_AnimationStatusLabel.style.fontSize = m_IsPortraitLayout ? T(12f) : m_IsCompactLayout ? T(13f) : T(15f);
            }

            if (m_PreviewImage != null)
            {
                m_PreviewImage.style.minHeight = previewImageMinHeight;
            }

            if (m_SelectionCard != null)
            {
                float selectionPaddingY = m_IsPortraitLayout ? U(10f) : m_IsCompactLayout ? U(10f) : U(12f);
                float selectionPaddingX = m_IsPortraitLayout ? U(14f) : m_IsCompactLayout ? U(14f) : U(20f);
                float selectionTitleHeight = m_IsPortraitLayout ? U(28f) : m_IsCompactLayout ? U(32f) : U(36f);
                float selectionDetailHeight = m_IsPortraitLayout ? U(16f) : m_IsCompactLayout ? U(16f) : U(18f);
                float selectionActionGap = m_IsPortraitLayout ? U(6f) : U(4f);
                float selectionButtonsHeight = m_IsPortraitLayout ? U(44f) : m_IsCompactLayout ? U(40f) : U(54f);
                float selectionCardHeight = selectionPaddingY * 2f + selectionTitleHeight + selectionDetailHeight + selectionActionGap + selectionButtonsHeight + U(4f);

                m_SelectionCard.style.marginTop = m_IsPortraitLayout ? U(8f) : m_IsCompactLayout ? U(8f) : U(16f);
                m_SelectionCard.style.paddingLeft = selectionPaddingX;
                m_SelectionCard.style.paddingRight = selectionPaddingX;
                m_SelectionCard.style.paddingTop = selectionPaddingY;
                m_SelectionCard.style.paddingBottom = selectionPaddingY;
                m_SelectionCard.style.minHeight = selectionCardHeight;
            }

            if (m_ActionsRow != null)
            {
                m_ActionsRow.style.flexDirection = m_IsPortraitLayout ? FlexDirection.Column : FlexDirection.Row;
                m_ActionsRow.style.marginTop = m_IsPortraitLayout ? U(6f) : U(4f);
            }

            if (m_DetailLabel != null)
            {
                m_DetailLabel.style.fontSize = m_IsPortraitLayout ? T(11f) : m_IsCompactLayout ? T(13f) : T(15f);
                m_DetailLabel.style.marginBottom = m_IsPortraitLayout ? U(4f) : U(4f);
            }

            if (m_RandomizeButton != null)
            {
                m_RandomizeButton.style.marginRight = m_IsPortraitLayout ? 0f : U(12f);
                m_RandomizeButton.style.marginBottom = m_IsPortraitLayout ? U(10f) : 0f;
                m_RandomizeButton.style.height = m_IsPortraitLayout ? U(44f) : m_IsCompactLayout ? U(40f) : U(54f);
                m_RandomizeButton.style.fontSize = m_IsPortraitLayout ? T(15f) : m_IsCompactLayout ? T(15f) : T(18f);
            }

            if (m_ApplyButton != null)
            {
                m_ApplyButton.style.height = m_IsPortraitLayout ? U(44f) : m_IsCompactLayout ? U(40f) : U(54f);
                m_ApplyButton.style.fontSize = m_IsPortraitLayout ? T(15f) : m_IsCompactLayout ? T(15f) : T(18f);
            }

            if (m_SidebarTitleLabel != null)
            {
                m_SidebarTitleLabel.style.fontSize = m_IsPortraitLayout ? T(20f) : m_IsCompactLayout ? T(24f) : T(28f);
            }

            if (m_SidebarSubtitleLabel != null)
            {
                m_SidebarSubtitleLabel.style.display = m_UseStackedLayout ? DisplayStyle.None : DisplayStyle.Flex;
                m_SidebarSubtitleLabel.style.fontSize = m_IsPortraitLayout ? T(12f) : m_IsCompactLayout ? T(14f) : T(15f);
                m_SidebarSubtitleLabel.style.marginBottom = m_UseStackedLayout ? U(8f) : U(14f);
            }

            if (m_SearchField != null)
            {
                m_SearchField.style.height = m_IsPortraitLayout ? U(42f) : m_IsCompactLayout ? U(46f) : U(52f);
                m_SearchField.style.fontSize = m_IsPortraitLayout ? T(15f) : m_IsCompactLayout ? T(16f) : T(18f);
                m_SearchField.style.marginBottom = m_IsPortraitLayout ? U(12f) : U(10f);
                var searchInput = m_SearchField.Q(TextField.textInputUssName);
                if (searchInput != null)
                {
                    searchInput.style.fontSize = m_IsPortraitLayout ? T(15f) : m_IsCompactLayout ? T(16f) : T(18f);
                }
            }

            if (m_SelectionLabel != null)
            {
                m_SelectionLabel.style.minHeight = m_IsPortraitLayout ? U(30f) : m_IsCompactLayout ? U(34f) : U(40f);
                m_SelectionLabel.style.marginBottom = m_IsPortraitLayout ? U(4f) : U(4f);
            }

            ConfigureListViewScrollArea();
            SetListItemHeight(m_IsPortraitLayout ? U(70f) : m_IsCompactLayout ? U(68f) : U(78f));
        }

        private void SetListItemHeight(float height)
        {
            if (m_ListView == null || height <= 0f)
            {
                return;
            }

            if (Mathf.Abs(m_CurrentListItemHeight - height) < 0.5f)
            {
                return;
            }

            m_CurrentListItemHeight = height;
            m_ListView.fixedItemHeight = height;
            if (m_ListView.panel != null)
            {
                m_ListView.Rebuild();
            }
        }

        private void ReleaseInteractionLock()
        {
            if (!m_LockedInteractions)
            {
                return;
            }

            var contextScene = GetContextScene();
            if (m_InteractionDirector != null && !IsInScene(m_InteractionDirector.gameObject, contextScene))
            {
                m_InteractionDirector = null;
            }

            m_InteractionDirector ??= FindSceneComponent<InteractionDirector>(contextScene, true);
            if (m_InteractionDirector != null)
            {
                m_InteractionDirector.SetInteractionsLocked(false);
            }

            m_LockedInteractions = false;
        }

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            var nextValue = evt != null ? evt.newValue : string.Empty;
            if (string.Equals(m_SearchText, nextValue, StringComparison.Ordinal))
            {
                return;
            }

            m_SearchText = nextValue;
            RefreshFilteredList();
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (m_IsUpdatingSelection)
            {
                return;
            }

            using var enumerator = selection != null ? selection.GetEnumerator() : null;
            if (enumerator == null || !enumerator.MoveNext())
            {
                return;
            }

            if (enumerator.Current is CharacterCustomizationPreset preset)
            {
                SelectPreset(preset, false);
            }
        }

        private void SetListSelectionWithoutNotify(int index)
        {
            if (m_ListView == null || index < 0)
            {
                return;
            }

            m_IsUpdatingSelection = true;
            m_ListSelectionBuffer.Clear();
            m_ListSelectionBuffer.Add(index);
            m_ListView.SetSelectionWithoutNotify(m_ListSelectionBuffer);
            m_IsUpdatingSelection = false;
        }

        private void RequestPreviewRender()
        {
            if (m_PreviewCamera == null || m_PreviewTexture == null)
            {
                return;
            }

            if (GraphicsSettings.currentRenderPipeline != null)
            {
                // In SRP pipelines Camera.Render() is not reliable here; keep camera enabled and let pipeline draw to target texture.
                SetPreviewCameraActive(true);
                m_PreviewDirty = false;
                return;
            }

            m_PreviewCamera.Render();
            m_PreviewDirty = false;
        }

        private void ResetPreviewViewControls()
        {
            m_PreviewSpinAngle = 0f;
            m_PreviewPitchDegrees = 0f;
            m_PreviewZoomMultiplier = 1f;
            m_PreviewPointerId = -1;
            m_IsPreviewDragging = false;
            m_PreviewManualControlUntilUnscaledTime = 0f;
        }

        private void MarkPreviewManualControlActive()
        {
            m_PreviewManualControlUntilUnscaledTime = Time.unscaledTime + PreviewManualControlHoldSeconds;
            m_PreviewDirty = true;
        }

        private void ApplyPreviewSpinRotation()
        {
            if (m_PreviewSpinRoot == null)
            {
                return;
            }

            m_PreviewSpinRoot.localRotation = Quaternion.Euler(0f, m_PreviewSpinAngle, 0f);
        }

        private void ApplyPreviewCameraView()
        {
            if (m_PreviewCamera == null || m_PreviewRigRoot == null)
            {
                return;
            }

            var focusLocalPoint = m_PreviewLookLocalPoint;
            var orbitOffset = Quaternion.Euler(m_PreviewPitchDegrees, 0f, 0f) *
                              new Vector3(0f, m_PreviewCameraVerticalOffset, -m_PreviewCameraDistance * m_PreviewZoomMultiplier);

            m_PreviewCamera.transform.localPosition = focusLocalPoint + orbitOffset;
            m_PreviewCamera.transform.localRotation = Quaternion.identity;
            m_PreviewCamera.transform.LookAt(m_PreviewRigRoot.transform.TransformPoint(focusLocalPoint), Vector3.up);
        }

        private void HandlePreviewPointerDown(PointerDownEvent evt)
        {
            if (!m_IsOpen || evt == null || evt.button != 0 || m_PreviewImage == null)
            {
                return;
            }

            MarkUiPointerHandledThisFrame();
            m_IsPreviewDragging = true;
            m_PreviewPointerId = evt.pointerId;
            m_LastPreviewPointerPosition = new Vector2(evt.position.x, evt.position.y);
            m_PreviewImage.CapturePointer(evt.pointerId);
            MarkPreviewManualControlActive();
            evt.StopPropagation();
        }

        private void HandlePreviewPointerMove(PointerMoveEvent evt)
        {
            if (!m_IsOpen || evt == null || !m_IsPreviewDragging || evt.pointerId != m_PreviewPointerId)
            {
                return;
            }

            var pointerPosition = new Vector2(evt.position.x, evt.position.y);
            var delta = pointerPosition - m_LastPreviewPointerPosition;
            m_LastPreviewPointerPosition = pointerPosition;
            if (delta.sqrMagnitude <= 0f)
            {
                return;
            }

            MarkUiPointerHandledThisFrame();
            m_PreviewSpinAngle = Mathf.Repeat(m_PreviewSpinAngle + (delta.x * PreviewRotateDragSpeed), 360f);
            m_PreviewPitchDegrees = Mathf.Clamp(
                m_PreviewPitchDegrees - (delta.y * PreviewPitchDragSpeed),
                PreviewPitchMinDegrees,
                PreviewPitchMaxDegrees);
            ApplyPreviewSpinRotation();
            ApplyPreviewCameraView();
            MarkPreviewManualControlActive();
            evt.StopPropagation();
        }

        private void HandlePreviewPointerUp(PointerUpEvent evt)
        {
            if (evt == null || evt.pointerId != m_PreviewPointerId)
            {
                return;
            }

            ReleasePreviewPointerCapture();
            evt.StopPropagation();
        }

        private void HandlePreviewPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt != null && evt.pointerId != m_PreviewPointerId)
            {
                return;
            }

            ReleasePreviewPointerCapture();
        }

        private void HandlePreviewWheel(WheelEvent evt)
        {
            if (!m_IsOpen || evt == null)
            {
                return;
            }

            MarkUiPointerHandledThisFrame();
            m_PreviewZoomMultiplier = Mathf.Clamp(
                m_PreviewZoomMultiplier + (evt.delta.y * PreviewZoomWheelSpeed * 0.01f),
                PreviewZoomMin,
                PreviewZoomMax);
            ApplyPreviewCameraView();
            MarkPreviewManualControlActive();
            evt.StopPropagation();
        }

        private void ReleasePreviewPointerCapture()
        {
            if (m_PreviewImage != null && m_PreviewPointerId >= 0 && m_PreviewImage.HasPointerCapture(m_PreviewPointerId))
            {
                m_PreviewImage.ReleasePointer(m_PreviewPointerId);
            }

            m_IsPreviewDragging = false;
            m_PreviewPointerId = -1;
        }

        private VisualElement CreatePresetRow()
        {
            var row = new VisualElement();
            ApplyRuntimeFont(row);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.height = new Length(100f, LengthUnit.Percent);
            row.style.flexGrow = 1f;
            row.style.paddingLeft = U(14f);
            row.style.paddingRight = U(14f);
            row.style.paddingTop = U(8f);
            row.style.paddingBottom = U(8f);
            row.style.marginBottom = 0f;
            row.style.borderTopLeftRadius = U(14f);
            row.style.borderTopRightRadius = U(14f);
            row.style.borderBottomLeftRadius = U(14f);
            row.style.borderBottomRightRadius = U(14f);
            row.style.backgroundColor = new Color(1f, 0.98f, 0.94f, 1f);
            row.style.borderLeftWidth = U(3f);
            row.style.borderRightWidth = U(3f);
            row.style.borderTopWidth = U(3f);
            row.style.borderBottomWidth = U(6f);
            row.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            row.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            row.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            row.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            row.pickingMode = PickingMode.Position;
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (row.userData is CharacterCustomizationPreset preset)
                {
                    MarkUiPointerHandledThisFrame();
                    SelectPreset(preset, false);
                    evt.StopPropagation();
                }
            });

            var indexBadge = new Label("01") { name = "preset-index" };
            ApplyRuntimeFont(indexBadge);
            indexBadge.style.minWidth = U(44f);
            indexBadge.style.height = U(40f);
            indexBadge.style.marginRight = U(12f);
            indexBadge.style.backgroundColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            indexBadge.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            indexBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            indexBadge.style.fontSize = T(15f);
            indexBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
            indexBadge.style.flexShrink = 0f;
            indexBadge.style.borderTopLeftRadius = U(10f);
            indexBadge.style.borderTopRightRadius = U(10f);
            indexBadge.style.borderBottomLeftRadius = U(10f);
            indexBadge.style.borderBottomRightRadius = U(10f);
            row.Add(indexBadge);

            var nameColumn = new VisualElement();
            nameColumn.style.flexGrow = 1f;
            nameColumn.style.flexDirection = FlexDirection.Column;
            nameColumn.style.justifyContent = Justify.Center;
            nameColumn.style.minWidth = 0f;
            nameColumn.style.marginRight = U(12f);
            row.Add(nameColumn);

            var nameLabel = new Label { name = "preset-name" };
            ApplyRuntimeFont(nameLabel);
            nameLabel.style.fontSize = T(18f);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.marginBottom = U(3f);
            nameColumn.Add(nameLabel);

            var idLabel = new Label { name = "preset-id" };
            ApplyRuntimeFont(idLabel);
            idLabel.style.fontSize = T(12f);
            idLabel.style.color = new Color(0.28f, 0.24f, 0.17f, 0.88f);
            idLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            idLabel.style.whiteSpace = WhiteSpace.NoWrap;
            idLabel.style.overflow = Overflow.Hidden;
            nameColumn.Add(idLabel);

            var badge = new Label("VIEW");
            ApplyRuntimeFont(badge);
            badge.name = "preset-badge";
            badge.style.fontSize = T(13f);
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.letterSpacing = 1.4f;
            badge.style.paddingLeft = U(10f);
            badge.style.paddingRight = U(10f);
            badge.style.paddingTop = U(6f);
            badge.style.paddingBottom = U(6f);
            badge.style.borderTopLeftRadius = U(10f);
            badge.style.borderTopRightRadius = U(10f);
            badge.style.borderBottomLeftRadius = U(10f);
            badge.style.borderBottomRightRadius = U(10f);
            badge.style.backgroundColor = new Color(0.2f, 0.17f, 0.12f, 1f);
            badge.style.color = new Color(1f, 0.98f, 0.92f, 1f);
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            badge.style.whiteSpace = WhiteSpace.NoWrap;
            badge.style.minWidth = U(68f);
            badge.style.flexShrink = 0f;
            badge.style.overflow = Overflow.Hidden;
            row.Add(badge);

            return row;
        }

        private void BindPresetRow(VisualElement element, int index)
        {
            if (index < 0 || index >= m_FilteredPresets.Count)
            {
                return;
            }

            var preset = m_FilteredPresets[index];
            if (preset == null)
            {
                return;
            }

            element.userData = preset;
            element.style.height = Mathf.Max(U(m_IsDenseListLayout ? 54f : 64f), m_CurrentListItemHeight);
            element.style.paddingLeft = m_IsPortraitLayout ? U(10f) : m_IsCompactLayout ? U(12f) : U(14f);
            element.style.paddingRight = m_IsPortraitLayout ? U(10f) : m_IsCompactLayout ? U(12f) : U(14f);
            element.style.paddingTop = m_IsDenseListLayout ? U(5f) : U(6f);
            element.style.paddingBottom = m_IsDenseListLayout ? U(5f) : U(6f);
            element.style.marginBottom = 0f;

            var selected = m_SelectedPreset != null && string.Equals(m_SelectedPreset.id, preset.id, StringComparison.OrdinalIgnoreCase);
            element.style.backgroundColor = selected
                ? new Color(0.99f, 0.9f, 0.46f, 1f)
                : new Color(1f, 0.98f, 0.94f, 1f);

            var nameLabel = element.Q<Label>("preset-name");
            if (nameLabel != null)
            {
                nameLabel.text = preset.DisplayName;
                nameLabel.style.fontSize = m_IsPortraitLayout ? T(14f) : m_IsDenseListLayout ? T(15f) : T(17f);
                nameLabel.style.marginBottom = m_IsDenseListLayout ? 0f : U(3f);
            }

            var idLabel = element.Q<Label>("preset-id");
            if (idLabel != null)
            {
                idLabel.text = $"ID {preset.id}";
                idLabel.style.display = m_IsDenseListLayout ? DisplayStyle.None : DisplayStyle.Flex;
                idLabel.style.fontSize = m_IsCompactLayout ? T(11f) : T(12f);
            }

            var indexBadge = element.Q<Label>("preset-index");
            if (indexBadge != null)
            {
                indexBadge.text = (index + 1).ToString("00");
                indexBadge.style.minWidth = m_IsPortraitLayout ? U(34f) : m_IsCompactLayout ? U(38f) : U(44f);
                indexBadge.style.height = m_IsPortraitLayout ? U(30f) : m_IsCompactLayout ? U(34f) : U(40f);
                indexBadge.style.marginRight = m_IsPortraitLayout ? U(10f) : U(12f);
                indexBadge.style.fontSize = m_IsPortraitLayout ? T(11f) : m_IsCompactLayout ? T(12f) : T(14f);
                indexBadge.style.backgroundColor = selected
                    ? new Color(0.15f, 0.56f, 0.9f, 1f)
                    : new Color(0.99f, 0.83f, 0.26f, 1f);
                indexBadge.style.color = selected
                    ? new Color(1f, 0.99f, 0.95f, 1f)
                    : new Color(0.08f, 0.07f, 0.05f, 1f);
            }

            var badge = element.Q<Label>("preset-badge");
            if (badge != null)
            {
                var applied = m_AppliedPreset != null && string.Equals(m_AppliedPreset.id, preset.id, StringComparison.OrdinalIgnoreCase);
                badge.text = applied
                    ? (m_IsDenseListLayout ? "SET" : "ACTIVE")
                    : selected
                        ? (m_IsDenseListLayout ? "TRY" : "PREVIEW")
                        : "VIEW";
                badge.style.fontSize = m_IsPortraitLayout ? T(10f) : m_IsCompactLayout ? T(11f) : T(13f);
                badge.style.minWidth = m_IsDenseListLayout ? U(44f) : m_IsCompactLayout ? U(74f) : U(84f);
                badge.style.paddingLeft = m_IsCompactLayout ? U(8f) : U(10f);
                badge.style.paddingRight = m_IsCompactLayout ? U(8f) : U(10f);
                badge.style.backgroundColor = selected
                    ? new Color(0.08f, 0.07f, 0.05f, 1f)
                    : new Color(1f, 0.98f, 0.92f, 1f);
                badge.style.color = selected
                    ? new Color(1f, 0.98f, 0.92f, 1f)
                    : new Color(0.08f, 0.07f, 0.05f, 1f);
            }
        }

        private Button CreateActionButton(string label, Action onClick, Color accentColor)
        {
            var button = new Button(() => onClick?.Invoke())
            {
                text = label
            };
            ApplyRuntimeFont(button);

            button.style.height = U(70f);
            button.style.minWidth = U(140f);
            button.style.borderTopLeftRadius = U(12f);
            button.style.borderTopRightRadius = U(12f);
            button.style.borderBottomLeftRadius = U(12f);
            button.style.borderBottomRightRadius = U(12f);
            button.style.borderLeftWidth = U(2f);
            button.style.borderRightWidth = U(2f);
            button.style.borderTopWidth = U(2f);
            button.style.borderBottomWidth = U(4f);
            button.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            button.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            button.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            button.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            button.style.backgroundColor = accentColor;
            button.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = T(24f);
            button.pickingMode = PickingMode.Position;
            button.focusable = true;
            button.tabIndex = 0;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.style.flexShrink = 0f;

            var hoverColor = Color.Lerp(accentColor, Color.white, 0.12f);
            var pressedColor = Color.Lerp(accentColor, Color.black, 0.18f);
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
                    button.style.backgroundColor = accentColor;
                }
            });
            button.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (button.enabledSelf)
                {
                    MarkUiPointerHandledThisFrame();
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

        private void HandleManualPointerFallback()
        {
            if (!IsLeftPointerPressedThisFrame() || m_Root == null || m_Root.panel == null)
            {
                return;
            }

            if (m_LastUiPointerHandledFrame == Time.frameCount)
            {
                return;
            }

            var panelPosition = RuntimePanelUtils.ScreenToPanel(m_Root.panel, GetPointerScreenPosition());

            if (m_CloseButton != null && m_CloseButton.worldBound.Contains(panelPosition))
            {
                Hide();
                return;
            }

            if (m_ApplyButton != null && m_ApplyButton.worldBound.Contains(panelPosition))
            {
                ApplySelection();
                return;
            }

            if (m_RandomizeButton != null && m_RandomizeButton.worldBound.Contains(panelPosition))
            {
                RandomizeSelection();
                return;
            }

            var picked = m_Root.panel.Pick(panelPosition);
            if (picked == null)
            {
                return;
            }

            for (var element = picked; element != null; element = element.parent)
            {
                if (element == m_CloseButton)
                {
                    Hide();
                    return;
                }

                if (element == m_ApplyButton)
                {
                    ApplySelection();
                    return;
                }

                if (element == m_RandomizeButton)
                {
                    RandomizeSelection();
                    return;
                }

                if (element.userData is CharacterCustomizationPreset preset)
                {
                    SelectPreset(preset, false);
                    return;
                }
            }
        }

        private void MarkUiPointerHandledThisFrame()
        {
            m_LastUiPointerHandledFrame = Time.frameCount;
        }

        private void SetPreviewCameraActive(bool active)
        {
            if (m_PreviewCamera == null)
            {
                return;
            }

            if (active)
            {
                EnsurePreviewTexture();
                m_PreviewCamera.targetTexture = m_PreviewTexture;
            }

            m_PreviewCamera.enabled = active;
        }

        private int GetEffectivePreviewLayer()
        {
            // Layer 31 is often excluded from custom URP renderer masks in this project setup.
            if (previewLayer < 0 || previewLayer > 30)
            {
                return -1;
            }

            return previewLayer;
        }

        private static bool IsLeftPointerPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                return true;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0))
            {
                return true;
            }
#endif
            return false;
        }

        private static bool IsCancelPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                return true;
            }
#endif
            return false;
        }

        private static bool IsSubmitPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null &&
                (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                return true;
            }
#endif
            return false;
        }

        private static Vector2 GetPointerScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }

            if (Touchscreen.current != null)
            {
                return Touchscreen.current.primaryTouch.position.ReadValue();
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector2.zero;
#endif
        }

        private void FocusInitialElement()
        {
            if (m_SearchField != null)
            {
                m_SearchField.Focus();
                return;
            }

            if (m_ApplyButton != null)
            {
                m_ApplyButton.Focus();
            }
        }

        private static void DestroySmart(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private bool TryRequestPresetThroughAddon(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                return false;
            }

            var addon = ResolvePreferredAddon();
            return addon != null && addon.RequestPreset(presetId);
        }

        private CharacterCustomizationAddon ResolvePreferredAddon()
        {
            var contextScene = GetContextScene();

            if (m_LocalPlayerState != null)
            {
                var localStateAddon = m_LocalPlayerState.GetComponent<CharacterCustomizationAddon>();
                if (localStateAddon != null)
                {
                    return localStateAddon;
                }
            }

            if (m_LocalPlayerManager != null)
            {
                var localManagerAddon = m_LocalPlayerManager.GetComponent<CharacterCustomizationAddon>();
                if (localManagerAddon != null)
                {
                    return localManagerAddon;
                }
            }

            if (m_LocalPlayerCoreAnimator != null)
            {
                var localAnimatorAddon = m_LocalPlayerCoreAnimator.GetComponentInParent<CharacterCustomizationAddon>();
                if (localAnimatorAddon != null)
                {
                    return localAnimatorAddon;
                }
            }

            var addons = FindObjectsByType<CharacterCustomizationAddon>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            CharacterCustomizationAddon fallbackAddon = null;
            CharacterCustomizationAddon namedFallbackAddon = null;
            for (int index = 0; index < addons.Length; index++)
            {
                var candidate = addons[index];
                if (candidate == null || !IsInScene(candidate.gameObject, contextScene))
                {
                    continue;
                }

                var state = candidate.GetComponent<CorePlayerState>();
                if (state != null && state.IsOwner)
                {
                    return candidate;
                }

                if (candidate.IsOwner)
                {
                    return candidate;
                }

                if (namedFallbackAddon == null &&
                    candidate.gameObject.name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    namedFallbackAddon = candidate;
                }

                if (fallbackAddon == null)
                {
                    fallbackAddon = candidate;
                }
            }

            return namedFallbackAddon != null ? namedFallbackAddon : fallbackAddon;
        }

        private bool TryApplyPresetLocally(CharacterCustomizationPreset preset)
        {
            if (preset == null || preset.characterPrefab == null)
            {
                return false;
            }

            if (m_LocalPlayerCoreAnimator == null)
            {
                if (m_LocalPlayerState != null)
                {
                    m_LocalPlayerCoreAnimator = m_LocalPlayerState.GetComponentInChildren<CoreAnimator>(true);
                }
                else if (m_LocalPlayerManager != null)
                {
                    m_LocalPlayerCoreAnimator = m_LocalPlayerManager.GetComponentInChildren<CoreAnimator>(true);
                }
            }

            if (m_LocalPlayerCoreAnimator == null)
            {
                m_LocalPlayerCoreAnimator = ResolveBestCoreAnimatorCandidate(GetContextScene());
            }

            if (m_LocalPlayerCoreAnimator == null)
            {
                return false;
            }

            var applied = CharacterCustomizationModelSwapper.TryApplyPreset(m_LocalPlayerCoreAnimator, preset);
            if (applied)
            {
                m_LocalPlayerAnimator = m_LocalPlayerCoreAnimator.BoundAnimator != null
                    ? m_LocalPlayerCoreAnimator.BoundAnimator
                    : m_LocalPlayerCoreAnimator.GetComponent<Animator>();
            }

            return applied;
        }

        private CoreAnimator ResolveBestCoreAnimatorCandidate(Scene contextScene)
        {
            var animators = FindObjectsByType<CoreAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (animators == null || animators.Length == 0)
            {
                return null;
            }

            CoreAnimator movementCandidate = null;
            CoreAnimator namedCandidate = null;
            CoreAnimator cameraProximityCandidate = null;
            CoreAnimator fallback = null;
            var mainCamera = Camera.main;
            var bestDistance = float.MaxValue;

            for (int index = 0; index < animators.Length; index++)
            {
                var candidate = animators[index];
                if (candidate == null || !IsInScene(candidate.gameObject, contextScene))
                {
                    continue;
                }

                if (m_PreviewRigRoot != null && candidate.transform.IsChildOf(m_PreviewRigRoot.transform))
                {
                    continue;
                }

                var state = candidate.GetComponentInParent<CorePlayerState>();
                if (state != null && state.IsOwner)
                {
                    return candidate;
                }

                var movement = candidate.GetComponentInParent<CoreMovement>();
                if (movement != null)
                {
                    if (movement.IsOwner)
                    {
                        return candidate;
                    }

                    if (movementCandidate == null)
                    {
                        movementCandidate = candidate;
                    }
                }

                var candidateName = candidate.name;
                var rootName = candidate.transform.root != null ? candidate.transform.root.name : string.Empty;
                if (namedCandidate == null &&
                    (candidateName.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     rootName.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    namedCandidate = candidate;
                }

                if (mainCamera != null)
                {
                    var distanceSq = (candidate.transform.position - mainCamera.transform.position).sqrMagnitude;
                    if (distanceSq < bestDistance)
                    {
                        bestDistance = distanceSq;
                        cameraProximityCandidate = candidate;
                    }
                }

                if (fallback == null)
                {
                    fallback = candidate;
                }
            }

            return movementCandidate ?? namedCandidate ?? cameraProximityCandidate ?? fallback;
        }
    }
}
