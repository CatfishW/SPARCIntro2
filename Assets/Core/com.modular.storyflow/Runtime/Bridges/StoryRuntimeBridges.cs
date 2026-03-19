using ModularStoryFlow.Runtime.Events;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.Player;
using UnityEngine;
using UnityEngine.Playables;

namespace ModularStoryFlow.Runtime.Bridges
{
    /// <summary>
    /// Listens for timeline requests and plays them through a PlayableDirector.
    /// </summary>
    [RequireComponent(typeof(PlayableDirector))]
    public sealed class StoryTimelineDirectorBridge : MonoBehaviour
    {
        [SerializeField] private StoryFlowProjectConfig projectConfig;
        [SerializeField] private StoryTimelineCatalog timelineCatalog;
        [SerializeField] private PlayableDirector director;
        [SerializeField] private string sessionFilter = string.Empty;

        private StoryTimelineRequest activeRequest;

        private StoryTimelineCatalog ActiveCatalog => timelineCatalog != null ? timelineCatalog : projectConfig != null ? projectConfig.TimelineCatalog : null;

        public void Configure(StoryFlowProjectConfig config, StoryTimelineCatalog catalog = null)
        {
            if (projectConfig == config && timelineCatalog == catalog)
            {
                return;
            }

            if (isActiveAndEnabled && projectConfig != null)
            {
                projectConfig.Channels?.TimelineRequests?.Unregister(HandleTimelineRequest);
            }

            projectConfig = config;
            timelineCatalog = catalog;

            if (isActiveAndEnabled && projectConfig != null)
            {
                projectConfig.Channels?.TimelineRequests?.Register(HandleTimelineRequest);
            }
        }

        private void Reset()
        {
            director = GetComponent<PlayableDirector>();
        }

        private void OnEnable()
        {
            if (director == null)
            {
                director = GetComponent<PlayableDirector>();
            }

            if (director != null)
            {
                director.stopped += HandleDirectorStopped;
            }

            projectConfig?.Channels?.TimelineRequests?.Register(HandleTimelineRequest);
        }

        private void OnDisable()
        {
            if (director != null)
            {
                director.stopped -= HandleDirectorStopped;
            }

            projectConfig?.Channels?.TimelineRequests?.Unregister(HandleTimelineRequest);
        }

        private void HandleTimelineRequest(StoryTimelineRequest request)
        {
            if (request == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(sessionFilter) && request.SessionId != sessionFilter)
            {
                return;
            }

            var playableAsset = ActiveCatalog != null ? ActiveCatalog.ResolvePlayableAsset(request.CueId) : null;
            if (playableAsset == null || director == null)
            {
                if (request.WaitForCompletion)
                {
                    projectConfig?.Channels?.TimelineResults?.Raise(new StoryTimelineResult
                    {
                        SessionId = request.SessionId,
                        RequestId = request.RequestId,
                        Completed = false,
                        Message = $"No playable asset was registered for cue '{request.CueId}'."
                    });
                }

                return;
            }

            activeRequest = request;
            director.playableAsset = playableAsset;
            director.Play();

            if (!request.WaitForCompletion)
            {
                activeRequest = null;
            }
        }

        private void HandleDirectorStopped(PlayableDirector stoppedDirector)
        {
            if (activeRequest == null)
            {
                return;
            }

            projectConfig?.Channels?.TimelineResults?.Raise(new StoryTimelineResult
            {
                SessionId = activeRequest.SessionId,
                RequestId = activeRequest.RequestId,
                Completed = true,
                Message = $"Playable '{activeRequest.CueDisplayName}' finished."
            });

            activeRequest = null;
        }
    }

    /// <summary>
    /// Convenience component for emitting external signals into the story system.
    /// </summary>
    public sealed class StoryExternalSignalEmitter : MonoBehaviour
    {
        [SerializeField] private StoryFlowProjectConfig projectConfig;
        [SerializeField] private StorySignalDefinition signal;
        [SerializeField] private string sessionId = string.Empty;
        [SerializeField] private string payload = string.Empty;

        public void Emit()
        {
            EmitWithPayload(payload);
        }

        public void EmitWithPayload(string customPayload)
        {
            if (signal == null)
            {
                return;
            }

            projectConfig?.Channels?.ExternalSignals?.Raise(new StoryExternalSignal
            {
                SessionId = sessionId,
                SignalId = signal.SignalId,
                Payload = customPayload
            });
        }
    }
}
