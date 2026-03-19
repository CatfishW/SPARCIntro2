using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using ModularStoryFlow.Runtime.Graph;

namespace ModularStoryFlow.Runtime.Integration
{
    [CreateAssetMenu(fileName = "SignalDefinition", menuName = "Story Flow/Integration/Signal Definition")]
    public sealed partial class StorySignalDefinition : ScriptableObject
    {
        [SerializeField] private string signalId = string.Empty;
        [SerializeField, TextArea] private string description = string.Empty;

        public string SignalId => string.IsNullOrWhiteSpace(signalId) ? name : signalId;
        public string Description => description;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(signalId))
            {
                signalId = name;
            }
        }
#endif
    }

    [CreateAssetMenu(fileName = "TimelineCue", menuName = "Story Flow/Integration/Timeline Cue")]
    public sealed partial class StoryTimelineCue : ScriptableObject
    {
        [SerializeField] private string cueId = string.Empty;
        [SerializeField, TextArea] private string description = string.Empty;

        public string CueId => string.IsNullOrWhiteSpace(cueId) ? name : cueId;
        public string Description => description;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(cueId))
            {
                cueId = name;
            }
        }
#endif
    }

    [System.Serializable]
    public sealed class StoryTimelineBinding
    {
        public string CueId = string.Empty;
        public string CueDisplayName = string.Empty;
        public PlayableAsset PlayableAsset;
    }

    [CreateAssetMenu(fileName = "StoryTimelineCatalog", menuName = "Story Flow/Catalogs/Timeline Catalog")]
    public sealed partial class StoryTimelineCatalog : ScriptableObject
    {
        [SerializeField] private List<StoryTimelineBinding> bindings = new List<StoryTimelineBinding>();

        public IReadOnlyList<StoryTimelineBinding> Bindings => bindings;

        public PlayableAsset ResolvePlayableAsset(string cueId)
        {
            return bindings.FirstOrDefault(binding => binding != null && binding.CueId == cueId)?.PlayableAsset;
        }

        public void SetBindings(IEnumerable<StoryTimelineBinding> entries)
        {
            bindings = entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.CueId))
                .GroupBy(entry => entry.CueId)
                .Select(group => group.First())
                .OrderBy(entry => entry.CueDisplayName)
                .ToList();
        }

        public void AddOrReplaceBinding(string cueId, string displayName, PlayableAsset playableAsset)
        {
            var existing = bindings.FirstOrDefault(binding => binding != null && binding.CueId == cueId);
            if (existing == null)
            {
                bindings.Add(new StoryTimelineBinding
                {
                    CueId = cueId,
                    CueDisplayName = displayName,
                    PlayableAsset = playableAsset
                });
                return;
            }

            existing.CueDisplayName = displayName;
            existing.PlayableAsset = playableAsset;
        }
    }

    [CreateAssetMenu(fileName = "StoryGraphRegistry", menuName = "Story Flow/Catalogs/Graph Registry")]
    public sealed partial class StoryGraphRegistry : ScriptableObject
    {
        [SerializeField] private List<StoryGraphAsset> graphs = new List<StoryGraphAsset>();

        public IReadOnlyList<StoryGraphAsset> Graphs => graphs;

        public StoryGraphAsset Resolve(string graphId)
        {
            return graphs.FirstOrDefault(graph => graph != null && graph.GraphId == graphId);
        }

        public void SetGraphs(IEnumerable<StoryGraphAsset> graphAssets)
        {
            graphs = graphAssets
                .Where(graph => graph != null)
                .Distinct()
                .OrderBy(graph => graph.name)
                .ToList();
        }
    }
}
