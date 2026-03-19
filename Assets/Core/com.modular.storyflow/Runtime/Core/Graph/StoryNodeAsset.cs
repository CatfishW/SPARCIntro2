using System.Collections.Generic;
using ModularStoryFlow.Runtime.Events;
using ModularStoryFlow.Runtime.Execution;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Graph
{
    /// <summary>
    /// Base class for all node assets used by a story graph.
    /// </summary>
    public abstract class StoryNodeAsset : ScriptableObject
    {
        public const string DefaultInputPortId = "in";
        public const string DefaultOutputPortId = "next";

        [SerializeField] private string nodeId = string.Empty;
        [SerializeField] private Rect editorPosition = new Rect(100f, 100f, 300f, 180f);
        [SerializeField, TextArea] private string notes = string.Empty;

        public string NodeId => nodeId;
        public Rect EditorPosition
        {
            get => editorPosition;
            set => editorPosition = value;
        }

        public string Notes
        {
            get => notes;
            set => notes = value;
        }

        public virtual string DisplayTitle
        {
            get
            {
                var attribute = GetType().GetCustomAttributes(typeof(StoryNodeAttribute), false);
                if (attribute.Length > 0 && attribute[0] is StoryNodeAttribute storyNodeAttribute)
                {
                    return storyNodeAttribute.DisplayName;
                }

                return GetType().Name.Replace("NodeAsset", string.Empty);
            }
        }

        public abstract IEnumerable<StoryPortDefinition> GetPorts();

        public abstract StoryExecutionResult Execute(IStoryExecutionContext context);

        public virtual void EnsureStableIds()
        {
            StoryIds.Ensure(ref nodeId, "node");
        }

#if UNITY_EDITOR
        public void Editor_SetNodeId(string value)
        {
            nodeId = value;
        }
#endif

        protected static StoryPortDefinition Input(string id = DefaultInputPortId, string name = "In", StoryPortCapacity capacity = StoryPortCapacity.Single)
        {
            return new StoryPortDefinition(id, name, StoryPortDirection.Input, capacity);
        }

        protected static StoryPortDefinition Output(string id, string name, StoryPortCapacity capacity = StoryPortCapacity.Single)
        {
            return new StoryPortDefinition(id, name, StoryPortDirection.Output, capacity);
        }
    }
}
