using System;
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

        private VisualElement overlay;
        private Label headerLabel;
        private Label statusLabel;
        private ScrollView chatScroll;
        private TextField inputField;
        private Button sendButton;
        private Button closeButton;
        private Label activeAssistantText;
        private bool built;
        private bool isBusy;

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

            headerLabel.text = string.IsNullOrWhiteSpace(npcDisplayName)
                ? "Free NPC Chat"
                : $"Free Chat · {npcDisplayName}";
            statusLabel.text = "Type your line and press Send.";
            overlay.style.display = DisplayStyle.Flex;
            IsOpen = true;
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
            activeAssistantText = null;
            if (chatScroll?.contentContainer != null)
            {
                chatScroll.contentContainer.Clear();
            }

            if (inputField != null)
            {
                inputField.value = string.Empty;
            }

            if (sendButton != null)
            {
                sendButton.SetEnabled(true);
            }

            if (inputField != null)
            {
                inputField.SetEnabled(true);
            }

            if (statusLabel != null)
            {
                statusLabel.text = "Type your line and press Send.";
            }
        }

        public void SetBusy(bool busy)
        {
            isBusy = busy;
            if (sendButton != null)
            {
                sendButton.SetEnabled(!busy);
            }

            if (inputField != null)
            {
                inputField.SetEnabled(!busy);
            }

            statusLabel.text = busy
                ? "NPC is responding..."
                : "Type your line and press Send.";
        }

        public void AppendSystem(string text)
        {
            AppendMessage("System", text, new Color(0.87f, 0.91f, 0.97f, 0.45f));
        }

        public void AppendPlayer(string text)
        {
            AppendMessage("You", text, new Color(0.12f, 0.21f, 0.31f, 0.95f));
        }

        public void BeginAssistantStreaming(string speaker)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginTop = 8f;
            row.style.marginBottom = 8f;
            row.style.paddingLeft = 10f;
            row.style.paddingRight = 10f;
            row.style.paddingTop = 8f;
            row.style.paddingBottom = 8f;
            row.style.borderTopLeftRadius = 10f;
            row.style.borderTopRightRadius = 10f;
            row.style.borderBottomLeftRadius = 10f;
            row.style.borderBottomRightRadius = 10f;
            row.style.backgroundColor = new Color(0.18f, 0.28f, 0.17f, 0.95f);

            var speakerLabel = new Label(string.IsNullOrWhiteSpace(speaker) ? "NPC" : speaker);
            speakerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            speakerLabel.style.fontSize = 13f;
            speakerLabel.style.color = new Color(0.93f, 0.97f, 0.88f, 1f);
            row.Add(speakerLabel);

            activeAssistantText = new Label(string.Empty);
            activeAssistantText.style.whiteSpace = WhiteSpace.Normal;
            activeAssistantText.style.fontSize = 16f;
            activeAssistantText.style.color = Color.white;
            activeAssistantText.style.marginTop = 3f;
            row.Add(activeAssistantText);

            chatScroll.contentContainer.Add(row);
            ScrollToBottom();
        }

        public void AppendAssistantDelta(string delta)
        {
            if (activeAssistantText == null || string.IsNullOrEmpty(delta))
            {
                return;
            }

            activeAssistantText.text += delta;
            ScrollToBottom();
        }

        public void FinalizeAssistantStreaming()
        {
            activeAssistantText = null;
            ScrollToBottom();
        }

        public void ReplaceActiveAssistantText(string text)
        {
            if (activeAssistantText == null)
            {
                return;
            }

            activeAssistantText.text = text ?? string.Empty;
            ScrollToBottom();
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
            SendRequested?.Invoke(text);
        }

        private void AppendMessage(string speaker, string text, Color background)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginTop = 8f;
            row.style.marginBottom = 8f;
            row.style.paddingLeft = 10f;
            row.style.paddingRight = 10f;
            row.style.paddingTop = 8f;
            row.style.paddingBottom = 8f;
            row.style.borderTopLeftRadius = 10f;
            row.style.borderTopRightRadius = 10f;
            row.style.borderBottomLeftRadius = 10f;
            row.style.borderBottomRightRadius = 10f;
            row.style.backgroundColor = background;

            var speakerLabel = new Label(string.IsNullOrWhiteSpace(speaker) ? "Narrator" : speaker);
            speakerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            speakerLabel.style.fontSize = 13f;
            speakerLabel.style.color = new Color(0.86f, 0.93f, 1f, 0.95f);
            row.Add(speakerLabel);

            var body = new Label(text);
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.fontSize = 16f;
            body.style.color = Color.white;
            body.style.marginTop = 3f;
            row.Add(body);

            chatScroll.contentContainer.Add(row);
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            if (chatScroll == null)
            {
                return;
            }

            chatScroll.schedule.Execute(() =>
            {
                chatScroll.scrollOffset = new Vector2(0f, Mathf.Infinity);
            }).ExecuteLater(16);
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
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.74f);
            overlay.style.display = DisplayStyle.None;

            var window = new VisualElement();
            window.style.width = new Length(62f, LengthUnit.Percent);
            window.style.maxWidth = 1020f;
            window.style.height = new Length(68f, LengthUnit.Percent);
            window.style.maxHeight = 790f;
            window.style.flexDirection = FlexDirection.Column;
            window.style.paddingLeft = 16f;
            window.style.paddingRight = 16f;
            window.style.paddingTop = 14f;
            window.style.paddingBottom = 14f;
            window.style.backgroundColor = new Color(0.06f, 0.09f, 0.13f, 0.97f);
            window.style.borderTopLeftRadius = 14f;
            window.style.borderTopRightRadius = 14f;
            window.style.borderBottomLeftRadius = 14f;
            window.style.borderBottomRightRadius = 14f;

            headerLabel = new Label("Free NPC Chat");
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLabel.style.fontSize = 24f;
            headerLabel.style.color = new Color(0.92f, 0.96f, 1f, 1f);
            window.Add(headerLabel);

            statusLabel = new Label("Type your line and press Send.");
            statusLabel.style.fontSize = 14f;
            statusLabel.style.color = new Color(0.66f, 0.76f, 0.88f, 0.95f);
            statusLabel.style.marginTop = 4f;
            statusLabel.style.marginBottom = 8f;
            window.Add(statusLabel);

            chatScroll = new ScrollView(ScrollViewMode.Vertical);
            chatScroll.style.flexGrow = 1f;
            chatScroll.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.84f);
            chatScroll.style.borderTopLeftRadius = 10f;
            chatScroll.style.borderTopRightRadius = 10f;
            chatScroll.style.borderBottomLeftRadius = 10f;
            chatScroll.style.borderBottomRightRadius = 10f;
            chatScroll.style.paddingLeft = 8f;
            chatScroll.style.paddingRight = 8f;
            chatScroll.style.paddingTop = 8f;
            chatScroll.style.paddingBottom = 8f;
            window.Add(chatScroll);

            var inputRow = new VisualElement();
            inputRow.style.flexDirection = FlexDirection.Row;
            inputRow.style.marginTop = 12f;
            inputRow.style.alignItems = Align.Center;

            inputField = new TextField();
            inputField.style.flexGrow = 1f;
            inputField.style.height = 36f;
            inputField.style.marginRight = 8f;
            inputField.style.backgroundColor = new Color(0.11f, 0.14f, 0.2f, 0.95f);
            inputField.style.color = Color.white;
            inputField.label = string.Empty;
            inputField.multiline = false;
            inputField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return && !isBusy)
                {
                    evt.StopPropagation();
                    SubmitCurrentInput();
                }
            });
            inputRow.Add(inputField);

            sendButton = new Button(SubmitCurrentInput) { text = "Send" };
            sendButton.style.width = 120f;
            sendButton.style.height = 36f;
            sendButton.style.marginRight = 6f;
            sendButton.style.backgroundColor = new Color(0.19f, 0.37f, 0.63f, 1f);
            sendButton.style.color = Color.white;
            inputRow.Add(sendButton);

            closeButton = new Button(Close) { text = "Close" };
            closeButton.style.width = 120f;
            closeButton.style.height = 36f;
            closeButton.style.backgroundColor = new Color(0.34f, 0.2f, 0.2f, 1f);
            closeButton.style.color = Color.white;
            inputRow.Add(closeButton);

            window.Add(inputRow);
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
    }
}
