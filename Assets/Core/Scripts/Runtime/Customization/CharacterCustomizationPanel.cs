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
        private VisualElement m_Content;
        private VisualElement m_LeftPane;
        private VisualElement m_RightPane;
        private VisualElement m_PreviewFrame;
        private VisualElement m_PreviewHud;
        private ListView m_ListView;
        private TextField m_SearchField;
        private Label m_TitleLabel;
        private Label m_StatusLabel;
        private Label m_SelectionLabel;
        private Label m_DetailLabel;
        private Label m_AnimationStatusLabel;
        private Label m_PresetCountLabel;
        private Image m_PreviewImage;
        private Button m_ApplyButton;
        private Button m_RandomizeButton;
        private Button m_CloseButton;

        private readonly List<CharacterCustomizationPreset> m_AllPresets = new List<CharacterCustomizationPreset>();
        private readonly List<CharacterCustomizationPreset> m_FilteredPresets = new List<CharacterCustomizationPreset>();
        private readonly List<int> m_ListSelectionBuffer = new List<int>(1);

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
        private string m_SearchText = string.Empty;
        private int m_LastUiPointerHandledFrame = -1;
        private EventSystem m_EventSystem;
        private InputSystemUIInputModule m_InputModule;
        private StandaloneInputModule m_StandaloneInputModule;
        private PanelSettings m_RuntimePanelSettings;

        private const float PreviewAnimationBlendDurationSeconds = 0.3f;
        private const float PreviewAnimationMinimumShowcaseSeconds = 2.4f;
        private const float PreviewAnimationMaximumShowcaseSeconds = 4.1f;

        public bool IsOpen => m_IsOpen;

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

            if (m_PreviewSpinRoot != null)
            {
                m_PreviewSpinAngle = Mathf.Repeat(m_PreviewSpinAngle + (previewSpinSpeed * Time.deltaTime), 360f);
                m_PreviewSpinRoot.localRotation = Quaternion.Euler(0f, m_PreviewSpinAngle, 0f);
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

        public void Show()
        {
            bool wasOpen = m_IsOpen;

            EnsureBuilt();
            if (m_Root == null)
            {
                return;
            }

            RefreshCatalog();
            RefreshLocalPlayerReferences();
            RefreshSelectionFromPlayer();
            RefreshFilteredList();
            EnsureUiInputBridge();
            SetVisible(true);
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

            AcquireInteractionLock();
            m_PreviousCursorLockState = UnityEngine.Cursor.lockState;
            m_CursorWasVisible = UnityEngine.Cursor.visible;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

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

            UnityEngine.Cursor.lockState = m_PreviousCursorLockState;
            UnityEngine.Cursor.visible = m_CursorWasVisible;
            SetPreviewCameraActive(false);
            SetVisible(false);
            ReleaseInteractionLock();
            m_IsOpen = false;
        }

        public void Close()
        {
            Hide();
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
            m_Overlay.style.paddingLeft = 26f;
            m_Overlay.style.paddingRight = 26f;
            m_Overlay.style.paddingTop = 26f;
            m_Overlay.style.paddingBottom = 26f;
            m_Overlay.pickingMode = PickingMode.Position;
            m_Overlay.focusable = true;
            m_Root.Add(m_Overlay);

            m_Window = new VisualElement();
            m_Window.style.width = new Length(97f, LengthUnit.Percent);
            m_Window.style.height = new Length(95f, LengthUnit.Percent);
            m_Window.style.minWidth = 1380f;
            m_Window.style.minHeight = 860f;
            m_Window.style.maxWidth = 3360f;
            m_Window.style.maxHeight = 1920f;
            m_Window.style.flexDirection = FlexDirection.Column;
            m_Window.style.backgroundColor = new Color(0.965f, 0.928f, 0.855f, 0.985f);
            m_Window.style.borderTopLeftRadius = 22f;
            m_Window.style.borderTopRightRadius = 22f;
            m_Window.style.borderBottomLeftRadius = 22f;
            m_Window.style.borderBottomRightRadius = 22f;
            m_Window.style.borderLeftWidth = 5f;
            m_Window.style.borderRightWidth = 5f;
            m_Window.style.borderTopWidth = 5f;
            m_Window.style.borderBottomWidth = 9f;
            m_Window.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_Window.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_Window.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_Window.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_Window.style.paddingLeft = 28f;
            m_Window.style.paddingRight = 28f;
            m_Window.style.paddingTop = 24f;
            m_Window.style.paddingBottom = 24f;
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

            if (!forceRecreateRuntimeSettings && m_UIDocument.panelSettings != null && m_UIDocument.panelSettings == m_RuntimePanelSettings)
            {
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

            m_UIDocument.panelSettings = m_RuntimePanelSettings;
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
            m_Header.style.paddingBottom = 18f;
            m_Header.style.borderBottomWidth = 4f;
            m_Header.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_Window.Add(m_Header);

            var titleGroup = new VisualElement();
            titleGroup.style.flexDirection = FlexDirection.Column;
            titleGroup.style.flexGrow = 1f;
            titleGroup.style.marginRight = 18f;
            m_Header.Add(titleGroup);

            var badgeRow = new VisualElement();
            badgeRow.style.flexDirection = FlexDirection.Row;
            badgeRow.style.alignItems = Align.Center;
            badgeRow.style.marginBottom = 12f;
            titleGroup.Add(badgeRow);

            var wardrobeChip = new Label("WARDROBE");
            ApplyRuntimeFont(wardrobeChip);
            wardrobeChip.style.paddingLeft = 18f;
            wardrobeChip.style.paddingRight = 18f;
            wardrobeChip.style.paddingTop = 10f;
            wardrobeChip.style.paddingBottom = 10f;
            wardrobeChip.style.marginRight = 12f;
            wardrobeChip.style.backgroundColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            wardrobeChip.style.color = new Color(0.98f, 0.95f, 0.88f, 1f);
            wardrobeChip.style.unityFontStyleAndWeight = FontStyle.Bold;
            wardrobeChip.style.fontSize = 15f;
            wardrobeChip.style.letterSpacing = 1.8f;
            wardrobeChip.style.borderTopLeftRadius = 14f;
            wardrobeChip.style.borderTopRightRadius = 14f;
            wardrobeChip.style.borderBottomLeftRadius = 14f;
            wardrobeChip.style.borderBottomRightRadius = 14f;
            badgeRow.Add(wardrobeChip);

            m_PresetCountLabel = new Label("00 LOOKS");
            ApplyRuntimeFont(m_PresetCountLabel);
            m_PresetCountLabel.style.paddingLeft = 14f;
            m_PresetCountLabel.style.paddingRight = 14f;
            m_PresetCountLabel.style.paddingTop = 9f;
            m_PresetCountLabel.style.paddingBottom = 9f;
            m_PresetCountLabel.style.backgroundColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            m_PresetCountLabel.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_PresetCountLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_PresetCountLabel.style.fontSize = 13f;
            m_PresetCountLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_PresetCountLabel.style.borderTopLeftRadius = 12f;
            m_PresetCountLabel.style.borderTopRightRadius = 12f;
            m_PresetCountLabel.style.borderBottomLeftRadius = 12f;
            m_PresetCountLabel.style.borderBottomRightRadius = 12f;
            badgeRow.Add(m_PresetCountLabel);

            m_TitleLabel = new Label("Change Character");
            ApplyRuntimeFont(m_TitleLabel);
            m_TitleLabel.style.fontSize = 58f;
            m_TitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_TitleLabel.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_TitleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_TitleLabel.style.minHeight = 68f;
            m_TitleLabel.style.marginBottom = 6f;
            titleGroup.Add(m_TitleLabel);

            m_StatusLabel = new Label("Preview a look, then apply it once. Your choice persists across the connected story scenes.");
            ApplyRuntimeFont(m_StatusLabel);
            m_StatusLabel.style.fontSize = 18f;
            m_StatusLabel.style.color = new Color(0.16f, 0.14f, 0.1f, 0.92f);
            m_StatusLabel.style.whiteSpace = WhiteSpace.Normal;
            m_StatusLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            m_StatusLabel.style.minHeight = 50f;
            titleGroup.Add(m_StatusLabel);

            var rightControls = new VisualElement();
            rightControls.style.flexDirection = FlexDirection.Column;
            rightControls.style.alignItems = Align.FlexEnd;
            rightControls.style.minWidth = 220f;
            m_Header.Add(rightControls);

            var helperLabel = new Label("Preview cycles through motion");
            ApplyRuntimeFont(helperLabel);
            helperLabel.style.fontSize = 14f;
            helperLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            helperLabel.style.color = new Color(0.24f, 0.22f, 0.17f, 0.9f);
            helperLabel.style.marginBottom = 10f;
            rightControls.Add(helperLabel);

            m_CloseButton = CreateActionButton("Close", Hide, new Color(0.95f, 0.72f, 0.62f, 1f));
            m_CloseButton.style.minWidth = 144f;
            rightControls.Add(m_CloseButton);
        }

        private void BuildContent()
        {
            m_Content = new VisualElement();
            m_Content.style.flexGrow = 1f;
            m_Content.style.flexDirection = FlexDirection.Row;
            m_Content.style.marginTop = 18f;
            m_Window.Add(m_Content);

            BuildDetailsPane();
            BuildSidebar();
        }

        private void BuildSidebar()
        {
            m_LeftPane = new VisualElement();
            m_LeftPane.style.width = 580f;
            m_LeftPane.style.flexShrink = 0f;
            m_LeftPane.style.flexDirection = FlexDirection.Column;
            m_LeftPane.style.marginLeft = 24f;
            m_LeftPane.style.paddingLeft = 22f;
            m_LeftPane.style.paddingRight = 22f;
            m_LeftPane.style.paddingTop = 22f;
            m_LeftPane.style.paddingBottom = 22f;
            m_LeftPane.style.backgroundColor = new Color(0.94f, 0.88f, 0.73f, 1f);
            m_LeftPane.style.borderLeftWidth = 4f;
            m_LeftPane.style.borderRightWidth = 4f;
            m_LeftPane.style.borderTopWidth = 4f;
            m_LeftPane.style.borderBottomWidth = 8f;
            m_LeftPane.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_LeftPane.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_LeftPane.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_LeftPane.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_LeftPane.style.borderTopLeftRadius = 18f;
            m_LeftPane.style.borderTopRightRadius = 18f;
            m_LeftPane.style.borderBottomLeftRadius = 18f;
            m_LeftPane.style.borderBottomRightRadius = 18f;
            m_Content.Add(m_LeftPane);

            var sidebarTitle = new Label("Preset Library");
            ApplyRuntimeFont(sidebarTitle);
            sidebarTitle.style.fontSize = 28f;
            sidebarTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            sidebarTitle.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            sidebarTitle.style.unityTextAlign = TextAnchor.MiddleLeft;
            sidebarTitle.style.marginBottom = 4f;
            m_LeftPane.Add(sidebarTitle);

            var sidebarSubtitle = new Label("Search, preview, then lock the look you want to carry into later scenes.");
            ApplyRuntimeFont(sidebarSubtitle);
            sidebarSubtitle.style.fontSize = 16f;
            sidebarSubtitle.style.color = new Color(0.2f, 0.17f, 0.12f, 0.92f);
            sidebarSubtitle.style.whiteSpace = WhiteSpace.Normal;
            sidebarSubtitle.style.unityTextAlign = TextAnchor.UpperLeft;
            sidebarSubtitle.style.marginBottom = 14f;
            m_LeftPane.Add(sidebarSubtitle);

            m_SearchField = new TextField();
            ApplyRuntimeFont(m_SearchField);
            m_SearchField.label = string.Empty;
            m_SearchField.value = string.Empty;
            m_SearchField.isDelayed = true;
            m_SearchField.style.height = 56f;
            m_SearchField.style.marginBottom = 16f;
            m_SearchField.style.borderTopLeftRadius = 12f;
            m_SearchField.style.borderTopRightRadius = 12f;
            m_SearchField.style.borderBottomLeftRadius = 12f;
            m_SearchField.style.borderBottomRightRadius = 12f;
            m_SearchField.style.backgroundColor = new Color(1f, 0.98f, 0.93f, 1f);
            m_SearchField.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SearchField.style.borderLeftWidth = 3f;
            m_SearchField.style.borderRightWidth = 3f;
            m_SearchField.style.borderTopWidth = 3f;
            m_SearchField.style.borderBottomWidth = 5f;
            m_SearchField.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SearchField.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SearchField.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SearchField.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SearchField.style.paddingLeft = 10f;
            m_SearchField.style.paddingRight = 10f;
            m_SearchField.tooltip = "Search presets by name or id";
            m_SearchField.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_SearchField.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_SearchField.style.fontSize = 18f;
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
                searchInput.style.fontSize = 18f;
            }

            m_ListView = new ListView
            {
                selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                fixedItemHeight = 116f,
                showBoundCollectionSize = false,
                showFoldoutHeader = false,
                reorderable = false,
                makeItem = CreatePresetRow,
                bindItem = BindPresetRow
            };
            m_ListView.style.flexGrow = 1f;
            m_ListView.style.backgroundColor = Color.clear;
            m_ListView.style.borderLeftWidth = 0f;
            m_ListView.style.borderRightWidth = 0f;
            m_ListView.style.borderTopWidth = 0f;
            m_ListView.style.borderBottomWidth = 0f;
            m_ListView.selectionChanged += OnSelectionChanged;
            m_LeftPane.Add(m_ListView);
        }

        private void BuildDetailsPane()
        {
            m_RightPane = new VisualElement();
            m_RightPane.style.flexGrow = 1f;
            m_RightPane.style.flexDirection = FlexDirection.Column;
            m_RightPane.style.justifyContent = Justify.FlexStart;
            m_RightPane.style.minWidth = 0f;
            m_RightPane.style.minHeight = 0f;
            m_RightPane.style.marginRight = 6f;
            m_Content.Add(m_RightPane);

            m_PreviewFrame = new VisualElement();
            m_PreviewFrame.style.flexGrow = 1f;
            m_PreviewFrame.style.minHeight = 720f;
            m_PreviewFrame.style.flexShrink = 1f;
            m_PreviewFrame.style.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            m_PreviewFrame.style.borderTopLeftRadius = 18f;
            m_PreviewFrame.style.borderTopRightRadius = 18f;
            m_PreviewFrame.style.borderBottomLeftRadius = 18f;
            m_PreviewFrame.style.borderBottomRightRadius = 18f;
            m_PreviewFrame.style.paddingLeft = 18f;
            m_PreviewFrame.style.paddingRight = 18f;
            m_PreviewFrame.style.paddingTop = 18f;
            m_PreviewFrame.style.paddingBottom = 18f;
            m_PreviewFrame.style.borderLeftWidth = 4f;
            m_PreviewFrame.style.borderRightWidth = 4f;
            m_PreviewFrame.style.borderTopWidth = 4f;
            m_PreviewFrame.style.borderBottomWidth = 8f;
            m_PreviewFrame.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_PreviewFrame.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_PreviewFrame.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_PreviewFrame.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_RightPane.Add(m_PreviewFrame);

            m_PreviewHud = new VisualElement();
            m_PreviewHud.style.flexDirection = FlexDirection.Row;
            m_PreviewHud.style.justifyContent = Justify.SpaceBetween;
            m_PreviewHud.style.alignItems = Align.Center;
            m_PreviewHud.style.marginBottom = 12f;
            m_PreviewFrame.Add(m_PreviewHud);

            var previewChip = new Label("LIVE PREVIEW");
            ApplyRuntimeFont(previewChip);
            previewChip.style.paddingLeft = 16f;
            previewChip.style.paddingRight = 16f;
            previewChip.style.paddingTop = 9f;
            previewChip.style.paddingBottom = 9f;
            previewChip.style.backgroundColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            previewChip.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            previewChip.style.unityFontStyleAndWeight = FontStyle.Bold;
            previewChip.style.fontSize = 14f;
            previewChip.style.borderTopLeftRadius = 12f;
            previewChip.style.borderTopRightRadius = 12f;
            previewChip.style.borderBottomLeftRadius = 12f;
            previewChip.style.borderBottomRightRadius = 12f;
            m_PreviewHud.Add(previewChip);

            m_AnimationStatusLabel = new Label("ANIMATION • STANDBY");
            ApplyRuntimeFont(m_AnimationStatusLabel);
            m_AnimationStatusLabel.style.paddingLeft = 14f;
            m_AnimationStatusLabel.style.paddingRight = 14f;
            m_AnimationStatusLabel.style.paddingTop = 8f;
            m_AnimationStatusLabel.style.paddingBottom = 8f;
            m_AnimationStatusLabel.style.backgroundColor = new Color(0.15f, 0.56f, 0.9f, 1f);
            m_AnimationStatusLabel.style.color = new Color(1f, 0.99f, 0.95f, 1f);
            m_AnimationStatusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_AnimationStatusLabel.style.fontSize = 13f;
            m_AnimationStatusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_AnimationStatusLabel.style.borderTopLeftRadius = 12f;
            m_AnimationStatusLabel.style.borderTopRightRadius = 12f;
            m_AnimationStatusLabel.style.borderBottomLeftRadius = 12f;
            m_AnimationStatusLabel.style.borderBottomRightRadius = 12f;
            m_PreviewHud.Add(m_AnimationStatusLabel);

            m_PreviewImage = new Image();
            m_PreviewImage.scaleMode = ScaleMode.ScaleToFit;
            m_PreviewImage.style.flexGrow = 1f;
            m_PreviewImage.style.minHeight = 640f;
            m_PreviewImage.style.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            m_PreviewImage.style.borderTopLeftRadius = 16f;
            m_PreviewImage.style.borderTopRightRadius = 16f;
            m_PreviewImage.style.borderBottomLeftRadius = 16f;
            m_PreviewImage.style.borderBottomRightRadius = 16f;
            m_PreviewImage.style.borderLeftWidth = 3f;
            m_PreviewImage.style.borderRightWidth = 3f;
            m_PreviewImage.style.borderTopWidth = 3f;
            m_PreviewImage.style.borderBottomWidth = 6f;
            m_PreviewImage.style.borderLeftColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            m_PreviewImage.style.borderRightColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            m_PreviewImage.style.borderTopColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            m_PreviewImage.style.borderBottomColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            m_PreviewImage.style.unityBackgroundImageTintColor = Color.white;
            m_PreviewImage.pickingMode = PickingMode.Ignore;
            m_PreviewFrame.Add(m_PreviewImage);

            var selectionCard = new VisualElement();
            selectionCard.style.marginTop = 18f;
            selectionCard.style.paddingLeft = 18f;
            selectionCard.style.paddingRight = 18f;
            selectionCard.style.paddingTop = 16f;
            selectionCard.style.paddingBottom = 16f;
            selectionCard.style.backgroundColor = new Color(1f, 0.98f, 0.93f, 1f);
            selectionCard.style.borderLeftWidth = 4f;
            selectionCard.style.borderRightWidth = 4f;
            selectionCard.style.borderTopWidth = 4f;
            selectionCard.style.borderBottomWidth = 8f;
            selectionCard.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            selectionCard.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            selectionCard.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            selectionCard.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            selectionCard.style.borderTopLeftRadius = 18f;
            selectionCard.style.borderTopRightRadius = 18f;
            selectionCard.style.borderBottomLeftRadius = 18f;
            selectionCard.style.borderBottomRightRadius = 18f;
            m_RightPane.Add(selectionCard);

            m_SelectionLabel = new Label("Select a character.");
            ApplyRuntimeFont(m_SelectionLabel);
            m_SelectionLabel.style.fontSize = 42f;
            m_SelectionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_SelectionLabel.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            m_SelectionLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_SelectionLabel.style.marginBottom = 6f;
            selectionCard.Add(m_SelectionLabel);

            m_DetailLabel = new Label("Browse the available character models and apply the one you want.");
            ApplyRuntimeFont(m_DetailLabel);
            m_DetailLabel.style.whiteSpace = WhiteSpace.Normal;
            m_DetailLabel.style.fontSize = 19f;
            m_DetailLabel.style.color = new Color(0.2f, 0.17f, 0.12f, 0.95f);
            m_DetailLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            m_DetailLabel.style.minHeight = 96f;
            m_DetailLabel.style.marginBottom = 16f;
            selectionCard.Add(m_DetailLabel);

            var actionsRow = new VisualElement();
            actionsRow.style.flexDirection = FlexDirection.Row;
            actionsRow.style.flexShrink = 0f;
            selectionCard.Add(actionsRow);

            m_RandomizeButton = CreateActionButton("Randomize", RandomizeSelection, new Color(0.35f, 0.58f, 0.95f, 1f));
            m_RandomizeButton.style.flexGrow = 1f;
            m_RandomizeButton.style.marginRight = 12f;
            actionsRow.Add(m_RandomizeButton);

            m_ApplyButton = CreateActionButton("Apply", ApplySelection, new Color(0.9f, 0.68f, 0.26f, 1f));
            m_ApplyButton.style.flexGrow = 1f;
            actionsRow.Add(m_ApplyButton);
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
            if (m_PreviewTexture != null && m_PreviewTexture.width == previewResolution && m_PreviewTexture.height == previewResolution)
            {
                return;
            }

            if (m_PreviewTexture != null)
            {
                m_PreviewTexture.Release();
                DestroySmart(m_PreviewTexture);
            }

            m_PreviewTexture = new RenderTexture(previewResolution, previewResolution, 16, RenderTextureFormat.ARGB32)
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
            m_ListView.Rebuild();
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
                m_DetailLabel.text = "Select a character preset to preview it here.";
                if (m_AnimationStatusLabel != null)
                {
                    m_AnimationStatusLabel.text = "ANIMATION • STANDBY";
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

            var effectivePreviewLayer = GetEffectivePreviewLayer();
            if (!CharacterRigUtility.TryApplyPreset(
                    m_PreviewModelRoot,
                    m_PreviewAnimator,
                    m_SelectedPreset.characterPrefab,
                    out m_PreviewModelInstance,
                    effectivePreviewLayer,
                    true))
            {
                m_DetailLabel.text = "Unable to build preview for this preset.";
                return;
            }

            if (m_PreviewModelInstance != null && CharacterRigUtility.TryCalculateBounds(m_PreviewModelInstance, out var bounds))
            {
                var localCenter = m_PreviewSpinRoot != null
                    ? m_PreviewSpinRoot.InverseTransformPoint(bounds.center)
                    : bounds.center;
                m_PreviewModelRoot.localPosition = -localCenter + new Vector3(0f, -0.04f, 0f);

                var radius = Mathf.Max(Mathf.Max(bounds.extents.x, bounds.extents.y), 0.45f);
                var fieldOfViewRadians = previewFieldOfView * Mathf.Deg2Rad * 0.5f;
                var distance = radius / Mathf.Tan(fieldOfViewRadians);
                m_PreviewCamera.fieldOfView = previewFieldOfView;
                m_PreviewCamera.transform.localPosition = new Vector3(0f, Mathf.Clamp(bounds.extents.y * 0.12f, 0.82f, 1.02f), -distance * 0.88f);
                m_PreviewCamera.transform.localRotation = Quaternion.identity;
                if (m_PreviewRigRoot != null)
                {
                    var lookTargetWorld = m_PreviewRigRoot.transform.TransformPoint(new Vector3(0f, Mathf.Clamp(bounds.extents.y * 0.6f, 0.92f, 1.35f), 0f));
                    m_PreviewCamera.transform.LookAt(lookTargetWorld, Vector3.up);
                }
            }

            RebuildPreviewAnimationPlaylist();

            m_PreviewCamera.targetTexture = m_PreviewTexture;
            m_PreviewImage.image = m_PreviewTexture;
            SetPreviewCameraActive(m_IsOpen);

            m_DetailLabel.text = $"Preset ID: {m_SelectedPreset.id}\nSource: {GetCompactSourceLabel(m_SelectedPreset)}\nApplies to your local player and carries forward when later scenes spawn your rig.";
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
                m_SelectionLabel.text = "Select a character.";
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
                m_StatusLabel.text = "No preset selected.";
                return;
            }

            if (m_AppliedPreset != null && string.Equals(m_AppliedPreset.id, m_SelectedPreset.id, StringComparison.OrdinalIgnoreCase))
            {
                m_StatusLabel.text = $"Applied: {m_SelectedPreset.DisplayName}";
            }
            else
            {
                m_StatusLabel.text = $"Previewing: {m_SelectedPreset.DisplayName}";
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
                    m_AnimationStatusLabel.text = "ANIMATION • STATIC";
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
                m_AnimationStatusLabel.text = "ANIMATION • STATIC";
                return;
            }

            var cleanName = clip.name.Replace("Anim@", string.Empty).Replace('_', ' ').Trim();
            m_AnimationStatusLabel.text = $"ANIMATION • {cleanName.ToUpperInvariant()}";
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
            m_LockedInteractions = true;
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

        private VisualElement CreatePresetRow()
        {
            var row = new VisualElement();
            ApplyRuntimeFont(row);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.height = 108f;
            row.style.paddingLeft = 14f;
            row.style.paddingRight = 14f;
            row.style.paddingTop = 10f;
            row.style.paddingBottom = 10f;
            row.style.marginBottom = 10f;
            row.style.borderTopLeftRadius = 14f;
            row.style.borderTopRightRadius = 14f;
            row.style.borderBottomLeftRadius = 14f;
            row.style.borderBottomRightRadius = 14f;
            row.style.backgroundColor = new Color(1f, 0.98f, 0.94f, 1f);
            row.style.borderLeftWidth = 3f;
            row.style.borderRightWidth = 3f;
            row.style.borderTopWidth = 3f;
            row.style.borderBottomWidth = 6f;
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
            indexBadge.style.minWidth = 50f;
            indexBadge.style.height = 50f;
            indexBadge.style.marginRight = 12f;
            indexBadge.style.backgroundColor = new Color(0.99f, 0.83f, 0.26f, 1f);
            indexBadge.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            indexBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            indexBadge.style.fontSize = 16f;
            indexBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
            indexBadge.style.flexShrink = 0f;
            indexBadge.style.borderTopLeftRadius = 10f;
            indexBadge.style.borderTopRightRadius = 10f;
            indexBadge.style.borderBottomLeftRadius = 10f;
            indexBadge.style.borderBottomRightRadius = 10f;
            row.Add(indexBadge);

            var nameColumn = new VisualElement();
            nameColumn.style.flexGrow = 1f;
            nameColumn.style.flexDirection = FlexDirection.Column;
            row.Add(nameColumn);

            var nameLabel = new Label { name = "preset-name" };
            ApplyRuntimeFont(nameLabel);
            nameLabel.style.fontSize = 22f;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.whiteSpace = WhiteSpace.Normal;
            nameLabel.style.marginBottom = 2f;
            nameColumn.Add(nameLabel);

            var idLabel = new Label { name = "preset-id" };
            ApplyRuntimeFont(idLabel);
            idLabel.style.fontSize = 14f;
            idLabel.style.color = new Color(0.28f, 0.24f, 0.17f, 0.88f);
            idLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            idLabel.style.whiteSpace = WhiteSpace.Normal;
            nameColumn.Add(idLabel);

            var badge = new Label("PREVIEW");
            ApplyRuntimeFont(badge);
            badge.name = "preset-badge";
            badge.style.fontSize = 13f;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.letterSpacing = 1.4f;
            badge.style.paddingLeft = 10f;
            badge.style.paddingRight = 10f;
            badge.style.paddingTop = 5f;
            badge.style.paddingBottom = 5f;
            badge.style.borderTopLeftRadius = 10f;
            badge.style.borderTopRightRadius = 10f;
            badge.style.borderBottomLeftRadius = 10f;
            badge.style.borderBottomRightRadius = 10f;
            badge.style.backgroundColor = new Color(0.2f, 0.17f, 0.12f, 1f);
            badge.style.color = new Color(1f, 0.98f, 0.92f, 1f);
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
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

            var selected = m_SelectedPreset != null && string.Equals(m_SelectedPreset.id, preset.id, StringComparison.OrdinalIgnoreCase);
            element.style.backgroundColor = selected
                ? new Color(0.99f, 0.9f, 0.46f, 1f)
                : new Color(1f, 0.98f, 0.94f, 1f);

            var nameLabel = element.Q<Label>("preset-name");
            if (nameLabel != null)
            {
                nameLabel.text = preset.DisplayName;
            }

            var idLabel = element.Q<Label>("preset-id");
            if (idLabel != null)
            {
                idLabel.text = $"ID {preset.id}";
            }

            var indexBadge = element.Q<Label>("preset-index");
            if (indexBadge != null)
            {
                indexBadge.text = (index + 1).ToString("00");
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
                badge.text = selected ? "SELECTED" : "PREVIEW";
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

            button.style.height = 60f;
            button.style.minWidth = 120f;
            button.style.borderTopLeftRadius = 12f;
            button.style.borderTopRightRadius = 12f;
            button.style.borderBottomLeftRadius = 12f;
            button.style.borderBottomRightRadius = 12f;
            button.style.borderLeftWidth = 3f;
            button.style.borderRightWidth = 3f;
            button.style.borderTopWidth = 3f;
            button.style.borderBottomWidth = 6f;
            button.style.borderLeftColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            button.style.borderRightColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            button.style.borderTopColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            button.style.borderBottomColor = new Color(0.08f, 0.07f, 0.05f, 1f);
            button.style.backgroundColor = accentColor;
            button.style.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = 18f;
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
