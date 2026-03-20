using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomStoryChoicePresenter : MonoBehaviour
    {
        [SerializeField] private BedroomStoryChoicePresenter implementation;

        private void Awake()
        {
            EnsureImplementation();
        }

        public void Configure(StoryChoiceSelectionChannel channel)
        {
            EnsureImplementation();
            implementation?.Configure(channel);
        }

        public void Present(StoryChoiceRequest request)
        {
            EnsureImplementation();
            implementation?.Present(request);
        }

        public void Clear()
        {
            EnsureImplementation();
            implementation?.Clear();
        }

        private void EnsureImplementation()
        {
            if (implementation != null)
            {
                return;
            }

            implementation = GetComponent<BedroomStoryChoicePresenter>();
            if (implementation == null)
            {
                implementation = gameObject.AddComponent<BedroomStoryChoicePresenter>();
            }
        }
    }
}
