using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ClassroomNpcFreeChatUi : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PanelSettings panelSettings;
        [SerializeField] private bool closeOnEscape = true;
        [SerializeField, Min(0.05f)] private float panelTransitionSeconds = 0.18f;
        [SerializeField] private float hiddenOffsetY = 84f;

        private VisualElement overlay;
        private VisualElement window;
        private TextField inputField;
        private VisualElement inputTextInput;
        private Button sendButton;
        private Button closeButton;
        private bool built;
        private bool isBusy;
        private Coroutine panelTransitionRoutine;

        public bool IsOpen { get; private set; }

        public event Action<string> SendRequested;
        public event Action Closed;

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

            if (!isBusy && inputField != null && Input.GetKeyDown(KeyCode.Return))
            {
                SubmitCurrentInput();
            }
        }

        public void Open(string npcDisplayName)
        {
            EnsureBuilt();
            if (overlay == null)
            {
                return;
            }

            if (uiDocument != null)
            {
                uiDocument.sortingOrder = Mathf.Max(uiDocument.sortingOrder, 690);
            }

            overlay.style.display = DisplayStyle.Flex;
            IsOpen = true;
            SetInputVisible(false, immediate: true);
            inputField.Focus();
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            HideImmediate();
            Closed?.Invoke();
        }

        public void HideImmediate()
        {
            EnsureBuilt();
            if (overlay == null)
            {
                return;
            }

            overlay.style.display = DisplayStyle.None;
            IsOpen = false;
            isBusy = false;

            if (inputField != null)
            {
                inputField.value = string.Empty;
            }

            ShowInputPanelImmediate();

            if (sendButton != null)
            {
                sendButton.SetEnabled(true);
            }

            if (inputField != null)
            {
                inputField.SetEnabled(true);
            }
        }

        public void SetBusy(bool busy)
        {
            isBusy = busy;
            if (sendButton != null)
            {
                sendButton.SetEnabled(!busy);
                sendButton.text = busy ? "..." : "Send";
            }

            if (closeButton != null)
            {
                closeButton.SetEnabled(!busy);
            }

            if (inputField != null)
            {
                inputField.SetEnabled(!busy);
            }

            if (window != null)
            {
                window.pickingMode = busy ? PickingMode.Ignore : PickingMode.Position;
            }

        }

        public void SetInputVisible(bool visible, bool immediate)
        {
            if (window == null)
            {
                return;
            }

            if (immediate)
            {
                if (panelTransitionRoutine != null)
                {
                    StopCoroutine(panelTransitionRoutine);
                    panelTransitionRoutine = null;
                }

                window.style.translate = new Translate(0f, visible ? 0f : hiddenOffsetY, 0f);
                window.style.opacity = visible ? 1f : 0f;
                return;
            }

            AnimateInputPanel(hidden: !visible);
        }

        public void AppendSystem(string text)
        {
        }

        public void AppendPlayer(string text)
        {
        }

        public void BeginAssistantStreaming(string speaker)
        {
        }

        public void AppendAssistantDelta(string delta)
        {
        }

        public void FinalizeAssistantStreaming()
        {
        }

        public void ReplaceActiveAssistantText(string text)
        {
        }

        private void SubmitCurrentInput()
        {
            if (isBusy || inputField == null)
            {
                return;
            }

            var text = (inputField.value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            inputField.value = string.Empty;
            AnimateInputPanel(hidden: true);
            SendRequested?.Invoke(text);
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

            EnsurePanelSettings();
            uiDocument.sortingOrder = Mathf.Max(uiDocument.sortingOrder, 690);

            var root = uiDocument.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1f;
            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.right = 0f;
            root.style.top = 0f;
            root.style.bottom = 0f;

            overlay = new VisualElement();
            overlay.style.flexGrow = 1f;
            overlay.style.alignItems = Align.FlexStart;
            overlay.style.justifyContent = Justify.FlexEnd;
            overlay.style.paddingLeft = 22f;
            overlay.style.paddingRight = 22f;
            overlay.style.paddingBottom = 22f;
            overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            overlay.style.display = DisplayStyle.None;

            window = new VisualElement();
            window.style.width = new Length(620f, LengthUnit.Pixel);
            window.style.maxWidth = 620f;
            window.style.minWidth = 480f;
            window.style.flexDirection = FlexDirection.Row;
            window.style.alignItems = Align.Center;
            window.style.paddingLeft = 10f;
            window.style.paddingRight = 10f;
            window.style.paddingTop = 10f;
            window.style.paddingBottom = 10f;
            window.style.backgroundColor = new Color(0.97f, 0.94f, 0.89f, 0.82f);
            window.style.borderTopLeftRadius = 16f;
            window.style.borderTopRightRadius = 16f;
            window.style.borderBottomLeftRadius = 16f;
            window.style.borderBottomRightRadius = 16f;
            window.style.borderLeftWidth = 4f;
            window.style.borderRightWidth = 4f;
            window.style.borderTopWidth = 4f;
            window.style.borderBottomWidth = 4f;
            window.style.borderLeftColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            window.style.borderRightColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            window.style.borderTopColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            window.style.borderBottomColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            window.style.translate = new Translate(0f, 0f, 0f);
            window.style.opacity = 1f;

            inputField = new TextField();
            inputField.style.flexGrow = 1f;
            inputField.style.height = 48f;
            inputField.style.minHeight = 48f;
            inputField.style.marginRight = 10f;
            inputField.style.paddingLeft = 0f;
            inputField.style.paddingRight = 0f;
            inputField.style.paddingTop = 0f;
            inputField.style.paddingBottom = 0f;
            inputField.style.backgroundColor = new Color(1f, 1f, 1f, 1f);
            inputField.style.color = new Color(0.08f, 0.09f, 0.12f, 1f);
            inputField.style.fontSize = 18f;
            inputField.style.borderLeftWidth = 3f;
            inputField.style.borderRightWidth = 3f;
            inputField.style.borderTopWidth = 3f;
            inputField.style.borderBottomWidth = 3f;
            inputField.style.borderLeftColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            inputField.style.borderRightColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            inputField.style.borderTopColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            inputField.style.borderBottomColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            inputField.style.unityTextAlign = TextAnchor.MiddleLeft;
            inputField.label = string.Empty;
            inputField.multiline = false;
            inputTextInput = inputField.Q(TextField.textInputUssName);
            if (inputTextInput != null)
            {
                inputTextInput.style.minHeight = 42f;
                inputTextInput.style.flexGrow = 1f;
                inputTextInput.style.flexShrink = 1f;
                inputTextInput.style.marginTop = 0f;
                inputTextInput.style.marginBottom = 0f;
                inputTextInput.style.paddingLeft = 12f;
                inputTextInput.style.paddingRight = 12f;
                inputTextInput.style.paddingTop = 2f;
                inputTextInput.style.paddingBottom = 2f;
                inputTextInput.style.fontSize = 18f;
                inputTextInput.style.unityTextAlign = TextAnchor.MiddleLeft;
            }
            inputField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return && !isBusy)
                {
                    evt.StopPropagation();
                    SubmitCurrentInput();
                }
            });
            window.Add(inputField);

            sendButton = new Button(SubmitCurrentInput) { text = "Send" };
            sendButton.style.width = 96f;
            sendButton.style.height = 40f;
            sendButton.style.marginRight = 6f;
            sendButton.style.backgroundColor = new Color(0.39f, 0.68f, 1f, 1f);
            sendButton.style.color = new Color(0.08f, 0.09f, 0.12f, 1f);
            sendButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            sendButton.style.borderLeftWidth = 3f;
            sendButton.style.borderRightWidth = 3f;
            sendButton.style.borderTopWidth = 3f;
            sendButton.style.borderBottomWidth = 3f;
            sendButton.style.borderLeftColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            sendButton.style.borderRightColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            sendButton.style.borderTopColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            sendButton.style.borderBottomColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            window.Add(sendButton);

            closeButton = new Button(Close) { text = "Close" };
            closeButton.style.width = 96f;
            closeButton.style.height = 40f;
            closeButton.style.backgroundColor = new Color(1f, 0.7f, 0.64f, 1f);
            closeButton.style.color = new Color(0.08f, 0.09f, 0.12f, 1f);
            closeButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            closeButton.style.borderLeftWidth = 3f;
            closeButton.style.borderRightWidth = 3f;
            closeButton.style.borderTopWidth = 3f;
            closeButton.style.borderBottomWidth = 3f;
            closeButton.style.borderLeftColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            closeButton.style.borderRightColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            closeButton.style.borderTopColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            closeButton.style.borderBottomColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            window.Add(closeButton);
            overlay.Add(window);
            root.Add(overlay);

            built = true;
        }

        private void EnsurePanelSettings()
        {
            if (uiDocument.panelSettings != null)
            {
                return;
            }

            if (panelSettings != null)
            {
                uiDocument.panelSettings = panelSettings;
                return;
            }

            var documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < documents.Length; index++)
            {
                var candidate = documents[index];
                if (candidate != null && candidate != uiDocument && candidate.panelSettings != null)
                {
                    uiDocument.panelSettings = candidate.panelSettings;
                    return;
                }
            }

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            uiDocument.panelSettings = panelSettings;
            uiDocument.sortingOrder = Mathf.Max(uiDocument.sortingOrder, 690);
        }

        private void ShowInputPanelImmediate()
        {
            if (window == null)
            {
                return;
            }

            if (panelTransitionRoutine != null)
            {
                StopCoroutine(panelTransitionRoutine);
                panelTransitionRoutine = null;
            }

            window.style.translate = new Translate(0f, 0f, 0f);
            window.style.opacity = 1f;
        }

        private void AnimateInputPanel(bool hidden)
        {
            if (window == null || !gameObject.activeInHierarchy)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                if (panelTransitionRoutine != null)
                {
                    StopCoroutine(panelTransitionRoutine);
                    panelTransitionRoutine = null;
                }

                window.style.translate = new Translate(0f, hidden ? hiddenOffsetY : 0f, 0f);
                window.style.opacity = hidden ? 0f : 1f;
                return;
            }

            if (panelTransitionRoutine != null)
            {
                StopCoroutine(panelTransitionRoutine);
            }

            panelTransitionRoutine = StartCoroutine(AnimateInputPanelRoutine(hidden));
        }

        private IEnumerator AnimateInputPanelRoutine(bool hidden)
        {
            var duration = Mathf.Max(0.05f, panelTransitionSeconds);
            var elapsed = 0f;
            var fromOffset = window.resolvedStyle.translate.y;
            var toOffset = hidden ? hiddenOffsetY : 0f;
            var fromOpacity = window.resolvedStyle.opacity;
            var toOpacity = hidden ? 0f : 1f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                var offset = Mathf.Lerp(fromOffset, toOffset, eased);
                var opacity = Mathf.Lerp(fromOpacity, toOpacity, eased);
                window.style.translate = new Translate(0f, offset, 0f);
                window.style.opacity = opacity;
                yield return null;
            }

            window.style.translate = new Translate(0f, toOffset, 0f);
            window.style.opacity = toOpacity;
            panelTransitionRoutine = null;
        }
    }
}
