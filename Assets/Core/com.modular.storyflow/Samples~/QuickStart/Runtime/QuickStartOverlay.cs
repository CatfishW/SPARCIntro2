using ModularStoryFlow.Runtime.Events;
using ModularStoryFlow.Runtime.Player;
using UnityEngine;

namespace ModularStoryFlow.Samples.QuickStart
{
    /// <summary>
    /// Minimal overlay used only by the sample to demonstrate channel-driven presentation.
    /// </summary>
    public sealed class QuickStartOverlay : MonoBehaviour
    {
        [SerializeField] private StoryFlowProjectConfig projectConfig;

        private StoryDialogueRequest activeDialogue;
        private StoryChoiceRequest activeChoice;
        private string lastStatus = "Waiting for story events...";
        private string lastSignal = string.Empty;
        private string lastStateChange = string.Empty;

        public StoryFlowProjectConfig ProjectConfig
        {
            get => projectConfig;
            set => projectConfig = value;
        }

        private void OnEnable()
        {
            Register();
        }

        private void OnDisable()
        {
            Unregister();
        }

        private void Register()
        {
            var channels = projectConfig != null ? projectConfig.Channels : null;
            if (channels == null)
            {
                return;
            }

            channels.DialogueRequests?.Register(HandleDialogue);
            channels.ChoiceRequests?.Register(HandleChoice);
            channels.GraphNotifications?.Register(HandleGraphNotification);
            channels.RaisedSignals?.Register(HandleSignal);
            channels.StateChanged?.Register(HandleStateChanged);
        }

        private void Unregister()
        {
            var channels = projectConfig != null ? projectConfig.Channels : null;
            if (channels == null)
            {
                return;
            }

            channels.DialogueRequests?.Unregister(HandleDialogue);
            channels.ChoiceRequests?.Unregister(HandleChoice);
            channels.GraphNotifications?.Unregister(HandleGraphNotification);
            channels.RaisedSignals?.Unregister(HandleSignal);
            channels.StateChanged?.Unregister(HandleStateChanged);
        }

        private void HandleDialogue(StoryDialogueRequest request)
        {
            activeDialogue = request;
            activeChoice = null;
            lastStatus = $"Dialogue @ {request.NodeId}";
        }

        private void HandleChoice(StoryChoiceRequest request)
        {
            activeChoice = request;
            activeDialogue = null;
            lastStatus = $"Choice @ {request.NodeId}";
        }

        private void HandleGraphNotification(StoryGraphNotification notification)
        {
            if (notification == null)
            {
                return;
            }

            lastStatus = $"{notification.Kind}: {(string.IsNullOrWhiteSpace(notification.Message) ? notification.GraphName : notification.Message)}";

            if (notification.Kind == StoryGraphNotificationKind.Completed ||
                notification.Kind == StoryGraphNotificationKind.Failed)
            {
                activeDialogue = null;
                activeChoice = null;
            }
        }

        private void HandleSignal(StorySignalPayload payload)
        {
            if (payload == null)
            {
                return;
            }

            lastSignal = string.IsNullOrWhiteSpace(payload.Payload)
                ? payload.SignalDisplayName
                : $"{payload.SignalDisplayName}: {payload.Payload}";
        }

        private void HandleStateChanged(StoryStateChangedPayload payload)
        {
            if (payload == null)
            {
                return;
            }

            lastStateChange = $"{payload.MachineId}: {payload.PreviousStateId} -> {payload.NextStateId}";
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 460f, 340f), GUI.skin.window);
            GUILayout.Label("Story Flow Quick Start", GUI.skin.label);
            GUILayout.Space(6f);

            if (activeDialogue != null)
            {
                GUILayout.Label(string.IsNullOrWhiteSpace(activeDialogue.SpeakerDisplayName)
                    ? activeDialogue.SpeakerId
                    : activeDialogue.SpeakerDisplayName, GUI.skin.box);
                GUILayout.Label(activeDialogue.Body, GUI.skin.textArea, GUILayout.MinHeight(90f));

                if (activeDialogue.AutoAdvance)
                {
                    GUILayout.Label("Auto-advancing…");
                }
                else if (GUILayout.Button("Continue"))
                {
                    projectConfig?.Channels?.AdvanceCommands?.Raise(new StoryAdvanceCommand
                    {
                        SessionId = activeDialogue.SessionId,
                        RequestId = activeDialogue.RequestId
                    });
                }
            }
            else if (activeChoice != null)
            {
                GUILayout.Label(activeChoice.Prompt, GUI.skin.box, GUILayout.MinHeight(36f));
                GUILayout.Space(4f);

                for (var i = 0; i < activeChoice.Options.Count; i++)
                {
                    var option = activeChoice.Options[i];
                    var previousEnabled = GUI.enabled;
                    GUI.enabled = option.IsAvailable;

                    if (GUILayout.Button(option.Label, GUILayout.Height(28f)))
                    {
                        projectConfig?.Channels?.ChoiceSelections?.Raise(new StoryChoiceSelection
                        {
                            SessionId = activeChoice.SessionId,
                            RequestId = activeChoice.RequestId,
                            PortId = option.PortId,
                            OptionIndex = i
                        });

                        activeChoice = null;
                    }

                    GUI.enabled = previousEnabled;
                }
            }
            else
            {
                GUILayout.Label("No active dialogue or choice request.");
            }

            GUILayout.Space(12f);
            GUILayout.Label($"Status: {lastStatus}");

            if (!string.IsNullOrWhiteSpace(lastSignal))
            {
                GUILayout.Label($"Signal: {lastSignal}");
            }

            if (!string.IsNullOrWhiteSpace(lastStateChange))
            {
                GUILayout.Label($"State: {lastStateChange}");
            }

            GUILayout.EndArea();
        }
    }
}
