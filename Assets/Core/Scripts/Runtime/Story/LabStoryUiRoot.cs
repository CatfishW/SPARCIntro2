using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class LabStoryUiRoot : MonoBehaviour
    {
        [SerializeField] private StoryFlowChannels channels;
        [SerializeField] private BedroomStorySubtitlePresenter subtitlePresenter;
        [SerializeField] private ClassroomStoryChoicePresenter choicePresenter;
        [SerializeField] private LabStoryPresentationState presentationState = new LabStoryPresentationState();

        private void Awake()
        {
            EnsurePresenters();
        }

        private void EnsurePresenters()
        {
            if (subtitlePresenter == null)
            {
                subtitlePresenter = CreateOverlayPresenter<BedroomStorySubtitlePresenter>("LabStorySubtitlePresenter");
            }

            if (choicePresenter == null)
            {
                choicePresenter = CreateOverlayPresenter<ClassroomStoryChoicePresenter>("LabStoryChoicePresenter");
            }
        }

        private void OnEnable()
        {
            EnsurePresenters();
            if (channels == null)
            {
                return;
            }

            choicePresenter.Configure(channels.ChoiceSelections);
            channels.DialogueRequests?.Register(HandleDialogue);
            channels.ChoiceRequests?.Register(HandleChoice);
            channels.GraphNotifications?.Register(HandleGraphNotification);
        }

        private void OnDisable()
        {
            if (channels == null)
            {
                return;
            }

            channels.DialogueRequests?.Unregister(HandleDialogue);
            channels.ChoiceRequests?.Unregister(HandleChoice);
            channels.GraphNotifications?.Unregister(HandleGraphNotification);
        }

        public void Configure(StoryFlowChannels storyChannels)
        {
            EnsurePresenters();
            if (channels == storyChannels)
            {
                return;
            }

            if (isActiveAndEnabled && channels != null)
            {
                channels.DialogueRequests?.Unregister(HandleDialogue);
                channels.ChoiceRequests?.Unregister(HandleChoice);
                channels.GraphNotifications?.Unregister(HandleGraphNotification);
            }

            channels = storyChannels;
            if (choicePresenter != null)
            {
                choicePresenter.Configure(channels != null ? channels.ChoiceSelections : null);
            }

            if (isActiveAndEnabled && channels != null)
            {
                channels.DialogueRequests?.Register(HandleDialogue);
                channels.ChoiceRequests?.Register(HandleChoice);
                channels.GraphNotifications?.Register(HandleGraphNotification);
            }
        }

        public void ClearPresentation()
        {
            EnsurePresenters();
            presentationState.Clear();
            subtitlePresenter?.HideImmediate();
            choicePresenter?.Clear();
        }

        private void HandleDialogue(StoryDialogueRequest request)
        {
            EnsurePresenters();
            presentationState.ShowDialogue(request);
            subtitlePresenter.Present(request);
            choicePresenter.Clear();
        }

        private void HandleChoice(StoryChoiceRequest request)
        {
            EnsurePresenters();
            presentationState.ShowChoice(request);
            subtitlePresenter.Clear();
            choicePresenter.Present(request);
        }

        private void HandleGraphNotification(StoryGraphNotification notification)
        {
            if (notification == null)
            {
                return;
            }

            if (notification.Kind == StoryGraphNotificationKind.Completed ||
                notification.Kind == StoryGraphNotificationKind.Failed ||
                notification.Kind == StoryGraphNotificationKind.Loaded)
            {
                ClearPresentation();
            }
        }

        private static T CreateOverlayPresenter<T>(string objectName) where T : Component
        {
            var existing = FindFirstObjectByType<T>();
            if (existing != null)
            {
                return existing;
            }

            var presenterObject = new GameObject(objectName, typeof(RectTransform));
            presenterObject.transform.SetParent(null, false);
            return presenterObject.AddComponent<T>();
        }
    }
}
