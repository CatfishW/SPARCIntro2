using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Graph
{
    /// <summary>
    /// Describes a logical port on a story node.
    /// </summary>
    [Serializable]
    public sealed class StoryPortDefinition
    {
        [SerializeField] private string id = "port";
        [SerializeField] private string displayName = "Port";
        [SerializeField] private StoryPortDirection direction = StoryPortDirection.Input;
        [SerializeField] private StoryPortCapacity capacity = StoryPortCapacity.Single;

        public StoryPortDefinition(string id, string displayName, StoryPortDirection direction, StoryPortCapacity capacity)
        {
            this.id = id;
            this.displayName = displayName;
            this.direction = direction;
            this.capacity = capacity;
        }

        public string Id => id;
        public string DisplayName => displayName;
        public StoryPortDirection Direction => direction;
        public StoryPortCapacity Capacity => capacity;
    }

    /// <summary>
    /// Serialized connection between two logical node ports.
    /// </summary>
    [Serializable]
    public sealed class StoryConnection
    {
        [SerializeField] private string connectionId = string.Empty;
        [SerializeField] private string fromNodeId = string.Empty;
        [SerializeField] private string fromPortId = string.Empty;
        [SerializeField] private string toNodeId = string.Empty;
        [SerializeField] private string toPortId = StoryNodeAsset.DefaultInputPortId;

        public string ConnectionId
        {
            get => connectionId;
            set => connectionId = value;
        }

        public string FromNodeId
        {
            get => fromNodeId;
            set => fromNodeId = value;
        }

        public string FromPortId
        {
            get => fromPortId;
            set => fromPortId = value;
        }

        public string ToNodeId
        {
            get => toNodeId;
            set => toNodeId = value;
        }

        public string ToPortId
        {
            get => toPortId;
            set => toPortId = value;
        }

        public void EnsureStableId()
        {
            if (!string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }

            connectionId = StoryIds.NewId();
        }

    }

    public enum StoryPortDirection
    {
        Input = 0,
        Output = 1
    }

    public enum StoryPortCapacity
    {
        Single = 0,
        Multi = 1
    }

    /// <summary>
    /// Decorates node classes so the editor can discover them.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class StoryNodeAttribute : Attribute
    {
        public StoryNodeAttribute(string menuPath, string displayName = null)
        {
            MenuPath = menuPath;
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? menuPath.Substring(menuPath.LastIndexOf('/') + 1)
                : displayName;
        }

        public string MenuPath { get; }
        public string DisplayName { get; }
    }

    /// <summary>
    /// Small helper methods for generating persistent ids.
    /// </summary>
    public static class StoryIds
    {
        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static string Ensure(ref string value, string prefix = null)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            value = string.IsNullOrWhiteSpace(prefix)
                ? NewId()
                : $"{prefix}_{NewId()}";

            return value;
        }
    }
}
