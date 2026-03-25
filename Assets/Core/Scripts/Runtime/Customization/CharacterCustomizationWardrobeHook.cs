using System;
using ItemInteraction;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blocks.Gameplay.Core.Customization
{
    [DefaultExecutionOrder(120)]
    [DisallowMultipleComponent]
    public sealed class CharacterCustomizationWardrobeHook : MonoBehaviour
    {
        private const string RuntimePanelObjectName = "CharacterCustomizationPanelRuntime";

        [SerializeField] private InteractableItem interactableItem;
        [SerializeField] private CharacterCustomizationPanel customizationPanel;
        [SerializeField] private string optionId = "change_character";
        [SerializeField] private string optionLabel = "Change Character";
        [SerializeField] private InteractionOptionSlot slot = InteractionOptionSlot.Top;
        [SerializeField] private bool visible = true;
        [FormerlySerializedAs("enabled")]
        [SerializeField] private bool optionEnabled = true;
        [SerializeField] private bool installOnAwake = true;
        [SerializeField] private float deferredInstallDelaySeconds = 0.45f;
        [SerializeField] private InteractionDirector interactionDirector;

        private bool m_DeferredInstallPending;
        private float m_DeferredInstallAt;
        private int m_LastHandleInvokedFrame = -1;

        private void Reset()
        {
            interactableItem = GetComponent<InteractableItem>();
        }

        private void Awake()
        {
            if (installOnAwake)
            {
                InstallOption();
            }
        }

        private void Start()
        {
            if (!installOnAwake || !Application.isPlaying)
            {
                return;
            }

            // Some scene bootstrap scripts rewrite interaction options in Start; re-install after them.
            m_DeferredInstallPending = true;
            m_DeferredInstallAt = Time.unscaledTime + Mathf.Max(0.05f, deferredInstallDelaySeconds);
        }

        private void OnEnable()
        {
            if (installOnAwake)
            {
                InstallOption();

                if (Application.isPlaying)
                {
                    m_DeferredInstallPending = true;
                    m_DeferredInstallAt = Time.unscaledTime + Mathf.Max(0.05f, deferredInstallDelaySeconds);
                }
            }

            ResolveDependencies();
            BindDirectorInvocation();
        }

        private void OnDisable()
        {
            if (interactableItem == null)
            {
                interactableItem = GetComponent<InteractableItem>();
            }

            if (interactableItem != null)
            {
                interactableItem.OptionTriggered -= HandleOptionTriggered;
            }

            var option = interactableItem != null ? interactableItem.FindOption(optionId) : null;
            if (option != null)
            {
                option.onInvoked.RemoveListener(HandleInvoked);
            }

            UnbindDirectorInvocation();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }
#endif

            if (!Application.isPlaying)
            {
                InstallOption();
            }
        }

        private void Update()
        {
            if (!m_DeferredInstallPending || !Application.isPlaying)
            {
                return;
            }

            if (Time.unscaledTime < m_DeferredInstallAt)
            {
                return;
            }

            m_DeferredInstallPending = false;
            InstallOption();
        }

        public void InstallOption()
        {
            ResolveDependencies();

            if (interactableItem == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(optionId))
            {
                optionId = "change_character";
            }

            if (string.IsNullOrWhiteSpace(optionLabel))
            {
                optionLabel = "Change Character";
            }

            if (interactableItem.options == null)
            {
                interactableItem.options = new System.Collections.Generic.List<InteractionOption>();
            }

            var option = interactableItem.FindOption(optionId);
            if (option == null)
            {
                option = new InteractionOption
                {
                    id = optionId,
                    label = optionLabel,
                    slot = slot,
                    visible = visible,
                    enabled = optionEnabled
                };
                interactableItem.options.Add(option);
            }
            else
            {
                option.label = optionLabel;
                option.slot = slot;
                option.visible = visible;
                option.enabled = optionEnabled;
            }

            NormalizeLookOptionSlot(option);
            EnsurePrimaryOptionFirst(option);
            option.onInvoked.RemoveListener(HandleInvoked);
            option.onInvoked.AddListener(HandleInvoked);
            interactableItem.OptionTriggered -= HandleOptionTriggered;
            interactableItem.OptionTriggered += HandleOptionTriggered;
            BindDirectorInvocation();
        }

        private void HandleInvoked()
        {
            ResolveDependencies();

            if (customizationPanel == null)
            {
                Debug.LogWarning("[CharacterCustomizationWardrobeHook] Change Character was triggered but no panel could be resolved or created.", this);
                return;
            }

            if (Time.frameCount == m_LastHandleInvokedFrame)
            {
                return;
            }

            m_LastHandleInvokedFrame = Time.frameCount;

            if (!customizationPanel.gameObject.activeSelf)
            {
                customizationPanel.gameObject.SetActive(true);
            }

            if (TryShowPanel(customizationPanel))
            {
                return;
            }

            var scene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
            if (!TryRecoverAndShowPanel(scene))
            {
                Debug.LogWarning("[CharacterCustomizationWardrobeHook] Change Character option was invoked, but the customization panel did not open.", this);
            }
        }

        private void HandleOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != interactableItem)
            {
                return;
            }

            if (!string.Equals(invocation.OptionId, optionId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            HandleInvoked();
        }

        private void ResolveDependencies()
        {
            if (interactableItem == null)
            {
                interactableItem = GetComponent<InteractableItem>();
            }

            var scene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
            if (interactionDirector == null || interactionDirector.gameObject.scene != scene)
            {
                interactionDirector = FindSceneObject<InteractionDirector>(scene, includeInactive: true);
            }

            if (!IsUsablePanel(customizationPanel, scene))
            {
                customizationPanel = null;
            }

            if (customizationPanel == null)
            {
                customizationPanel = FindBestPanel(scene);
                if (customizationPanel == null && IsSceneReadyForPanelMutation(scene))
                {
                    customizationPanel = CreateRuntimePanel(scene);
                }
            }
        }

        private static bool IsUsablePanel(CharacterCustomizationPanel panel, Scene scene)
        {
            if (panel == null || !scene.IsValid() || !scene.isLoaded || panel.gameObject.scene != scene)
            {
                return false;
            }

            var current = panel.transform.parent;
            while (current != null)
            {
                if (current.GetComponent<UIDocument>() != null)
                {
                    return false;
                }

                current = current.parent;
            }

            return true;
        }

        private static CharacterCustomizationPanel FindBestPanel(Scene scene)
        {
            if (!IsSceneReadyForPanelMutation(scene))
            {
                return null;
            }

            CharacterCustomizationPanel standaloneFallback = null;
            var panels = FindObjectsByType<CharacterCustomizationPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < panels.Length; index++)
            {
                var panel = panels[index];
                if (!IsUsablePanel(panel, scene))
                {
                    continue;
                }

                if (string.Equals(panel.gameObject.name, RuntimePanelObjectName, StringComparison.Ordinal))
                {
                    return panel;
                }

                standaloneFallback ??= panel;
            }

            return standaloneFallback;
        }

        private CharacterCustomizationPanel CreateRuntimePanel(Scene scene)
        {
            if (!IsSceneReadyForPanelMutation(scene))
            {
                return null;
            }

            var panelObject = FindSceneObjectByName(scene, RuntimePanelObjectName);
            if (panelObject == null)
            {
                panelObject = new GameObject(RuntimePanelObjectName);
            }

            if (scene.IsValid() && panelObject.scene != scene)
            {
                SceneManager.MoveGameObjectToScene(panelObject, scene);
            }

            if (panelObject.transform.parent != null)
            {
                panelObject.transform.SetParent(null, false);
            }

            if (panelObject.GetComponent<UIDocument>() == null)
            {
                panelObject.AddComponent<UIDocument>();
            }

            var panel = panelObject.GetComponent<CharacterCustomizationPanel>();
            if (panel == null)
            {
                panel = panelObject.AddComponent<CharacterCustomizationPanel>();
            }

            if (!panelObject.activeSelf)
            {
                panelObject.SetActive(true);
            }

            return panel;
        }

        private bool TryShowPanel(CharacterCustomizationPanel panel)
        {
            if (panel == null)
            {
                return false;
            }

            try
            {
                panel.Show();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }

            if (panel.IsOpen)
            {
                return true;
            }

            return panel.RebuildAndShow();
        }

        private bool TryRecoverAndShowPanel(Scene scene)
        {
            if (!IsSceneReadyForPanelMutation(scene))
            {
                return false;
            }

            var scenePanel = FindBestPanel(scene);
            if (scenePanel != null)
            {
                customizationPanel = scenePanel;
                if (!customizationPanel.gameObject.activeSelf)
                {
                    customizationPanel.gameObject.SetActive(true);
                }

                if (TryShowPanel(customizationPanel))
                {
                    return true;
                }
            }

            var runtimePanelObject = FindSceneObjectByName(scene, RuntimePanelObjectName);
            if (runtimePanelObject != null)
            {
                Destroy(runtimePanelObject);
            }

            customizationPanel = CreateRuntimePanel(scene);
            if (customizationPanel == null)
            {
                return false;
            }

            if (!customizationPanel.gameObject.activeSelf)
            {
                customizationPanel.gameObject.SetActive(true);
            }

            return TryShowPanel(customizationPanel);
        }

        private static T FindSceneObject<T>(Scene scene, bool includeInactive)
            where T : Component
        {
            if (!IsSceneReadyForPanelMutation(scene))
            {
                return null;
            }

            var candidates = FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (candidate != null && candidate.gameObject.scene == scene)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static GameObject FindSceneObjectByName(Scene scene, string objectName)
        {
            if (!IsSceneReadyForPanelMutation(scene) || string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var root = roots[rootIndex];
                if (root == null)
                {
                    continue;
                }

                if (string.Equals(root.name, objectName, StringComparison.Ordinal))
                {
                    return root;
                }

                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (var index = 0; index < transforms.Length; index++)
                {
                    var candidate = transforms[index];
                    if (candidate != null && string.Equals(candidate.name, objectName, StringComparison.Ordinal))
                    {
                        return candidate.gameObject;
                    }
                }
            }

            return null;
        }

        private static bool IsSceneReadyForPanelMutation(Scene scene)
        {
            return scene.IsValid() && scene.isLoaded;
        }

        private void NormalizeLookOptionSlot(InteractionOption primaryOption)
        {
            if (interactableItem == null || interactableItem.options == null || primaryOption == null)
            {
                return;
            }

            var lookOption = interactableItem.FindOption("look");
            if (lookOption == null || lookOption == primaryOption || lookOption.slot != primaryOption.slot)
            {
                return;
            }

            lookOption.slot = primaryOption.slot == InteractionOptionSlot.Top
                ? InteractionOptionSlot.Left
                : InteractionOptionSlot.Top;
        }

        private void EnsurePrimaryOptionFirst(InteractionOption primaryOption)
        {
            if (interactableItem == null || interactableItem.options == null || primaryOption == null)
            {
                return;
            }

            var currentIndex = interactableItem.options.IndexOf(primaryOption);
            if (currentIndex <= 0)
            {
                return;
            }

            interactableItem.options.RemoveAt(currentIndex);
            interactableItem.options.Insert(0, primaryOption);
        }

        private void BindDirectorInvocation()
        {
            if (interactionDirector == null)
            {
                return;
            }

            interactionDirector.OptionInvoked -= HandleDirectorOptionInvoked;
            interactionDirector.OptionInvoked += HandleDirectorOptionInvoked;
        }

        private void UnbindDirectorInvocation()
        {
            if (interactionDirector == null)
            {
                return;
            }

            interactionDirector.OptionInvoked -= HandleDirectorOptionInvoked;
        }

        private void HandleDirectorOptionInvoked(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != interactableItem)
            {
                return;
            }

            if (!string.Equals(invocation.OptionId, optionId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            HandleInvoked();
        }
    }
}
