using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.Save;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Player
{
    [CreateAssetMenu(fileName = "StoryFlowProjectConfig", menuName = "Story Flow/Project/Project Config")]
    public sealed class StoryFlowProjectConfig : ScriptableObject
    {
        [SerializeField] private StoryGraphRegistry graphRegistry;
        [SerializeField] private StoryVariableCatalog variableCatalog;
        [SerializeField] private StoryStateMachineCatalog stateMachineCatalog;
        [SerializeField] private StoryTimelineCatalog timelineCatalog;
        [SerializeField] private StoryFlowChannels channels;
        [SerializeField] private StorySaveProviderAsset saveProvider;

        public StoryGraphRegistry GraphRegistry => graphRegistry;
        public StoryVariableCatalog VariableCatalog => variableCatalog;
        public StoryStateMachineCatalog StateMachineCatalog => stateMachineCatalog;
        public StoryTimelineCatalog TimelineCatalog => timelineCatalog;
        public StoryFlowChannels Channels => channels;
        public StorySaveProviderAsset SaveProvider => saveProvider;

        public void Configure(
            StoryGraphRegistry registry,
            StoryVariableCatalog variables,
            StoryStateMachineCatalog stateMachines,
            StoryTimelineCatalog timelines,
            StoryFlowChannels storyChannels,
            StorySaveProviderAsset provider)
        {
            graphRegistry = registry;
            variableCatalog = variables;
            stateMachineCatalog = stateMachines;
            timelineCatalog = timelines;
            channels = storyChannels;
            saveProvider = provider;
        }
    }
}
