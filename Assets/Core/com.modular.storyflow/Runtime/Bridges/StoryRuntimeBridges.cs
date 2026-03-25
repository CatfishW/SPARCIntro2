using ModularStoryFlow.Runtime.Events;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.Player;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;
using UnityEngine.UIElements;

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
        private readonly List<SuspendedDocumentState> suspendedDocuments = new List<SuspendedDocumentState>(8);
        private readonly List<Canvas> suspendedCanvases = new List<Canvas>(16);
        private bool uiSuspended;

        private sealed class SuspendedDocumentState
        {
            public UIDocument Document;
            public DisplayStyle PreviousDisplay;
            public PickingMode PreviousPickingMode;
        }

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
                director.played += HandleDirectorPlayed;
                director.stopped += HandleDirectorStopped;
            }

            projectConfig?.Channels?.TimelineRequests?.Register(HandleTimelineRequest);
        }

        private void OnDisable()
        {
            if (director != null)
            {
                director.played -= HandleDirectorPlayed;
                director.stopped -= HandleDirectorStopped;
            }

            RestoreSuspendedUi();
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
            RestoreSuspendedUi();
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

        private void HandleDirectorPlayed(PlayableDirector playedDirector)
        {
            SuspendCompetingUi();
        }

        private void SuspendCompetingUi()
        {
            if (uiSuspended)
            {
                return;
            }

            suspendedDocuments.Clear();
            suspendedCanvases.Clear();

            var timelineRoot = director != null ? director.transform : transform;

            var documents = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < documents.Length; index++)
            {
                var document = documents[index];
                if (document == null ||
                    !document.enabled ||
                    document == GetComponent<UIDocument>() ||
                    IsOwnedByTimeline(document.transform, timelineRoot))
                {
                    continue;
                }

                var root = document.rootVisualElement;
                if (root == null)
                {
                    continue;
                }

                suspendedDocuments.Add(new SuspendedDocumentState
                {
                    Document = document,
                    PreviousDisplay = root.resolvedStyle.display,
                    PreviousPickingMode = root.pickingMode
                });

                root.style.display = DisplayStyle.None;
                root.pickingMode = PickingMode.Ignore;
            }

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < canvases.Length; index++)
            {
                var canvas = canvases[index];
                if (canvas == null ||
                    !canvas.enabled ||
                    IsOwnedByTimeline(canvas.transform, timelineRoot))
                {
                    continue;
                }

                suspendedCanvases.Add(canvas);
                canvas.enabled = false;
            }

            uiSuspended = true;
        }

        private void RestoreSuspendedUi()
        {
            if (!uiSuspended)
            {
                return;
            }

            for (var index = 0; index < suspendedDocuments.Count; index++)
            {
                var state = suspendedDocuments[index];
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

            suspendedDocuments.Clear();

            for (var index = 0; index < suspendedCanvases.Count; index++)
            {
                var canvas = suspendedCanvases[index];
                if (canvas != null)
                {
                    canvas.enabled = true;
                }
            }

            suspendedCanvases.Clear();
            uiSuspended = false;
        }

        private static bool IsOwnedByTimeline(Transform candidate, Transform timelineRoot)
        {
            return candidate != null && timelineRoot != null &&
                   (candidate == timelineRoot || candidate.IsChildOf(timelineRoot));
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
