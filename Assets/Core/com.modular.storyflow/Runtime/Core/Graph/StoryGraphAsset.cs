using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Graph
{
    /// <summary>
    /// Serialized story graph asset made of node sub-assets and id-based connections.
    /// </summary>
    [CreateAssetMenu(fileName = "StoryGraph", menuName = "Story Flow/Story Graph")]
    public sealed class StoryGraphAsset : ScriptableObject
    {
        [SerializeField] private string graphId = string.Empty;
        [SerializeField] private string entryNodeId = string.Empty;
        [SerializeField] private List<StoryNodeAsset> nodes = new List<StoryNodeAsset>();
        [SerializeField] private List<StoryConnection> connections = new List<StoryConnection>();

        private readonly Dictionary<string, StoryNodeAsset> nodeLookup = new Dictionary<string, StoryNodeAsset>();
        private readonly Dictionary<string, List<StoryConnection>> outputLookup = new Dictionary<string, List<StoryConnection>>();
        private bool cacheBuilt;

        public string GraphId
        {
            get
            {
                StoryIds.Ensure(ref graphId, "graph");
                return graphId;
            }
        }

        public string EntryNodeId
        {
            get => entryNodeId;
            set => entryNodeId = value;
        }

        public IReadOnlyList<StoryNodeAsset> Nodes => nodes;
        public IReadOnlyList<StoryConnection> Connections => connections;

        public StoryNodeAsset GetEntryNode()
        {
            EnsureCache();
            if (!string.IsNullOrWhiteSpace(entryNodeId) && nodeLookup.TryGetValue(entryNodeId, out var node))
            {
                return node;
            }

            return nodes.OfType<StartNodeAsset>().FirstOrDefault() ?? nodes.FirstOrDefault();
        }

        public StoryNodeAsset GetNode(string nodeId)
        {
            EnsureCache();
            nodeLookup.TryGetValue(nodeId, out var node);
            return node;
        }

        public bool TryResolveNextNodeId(string fromNodeId, string fromPortId, out string toNodeId)
        {
            EnsureCache();
            toNodeId = null;
            var key = MakeOutputKey(fromNodeId, fromPortId);
            if (!outputLookup.TryGetValue(key, out var connected))
            {
                return false;
            }

            var first = connected.FirstOrDefault();
            if (first == null || string.IsNullOrWhiteSpace(first.ToNodeId))
            {
                return false;
            }

            toNodeId = first.ToNodeId;
            return true;
        }

        public bool HasAnyStartNode()
        {
            return nodes.OfType<StartNodeAsset>().Any();
        }

        public void InvalidateCache()
        {
            cacheBuilt = false;
            nodeLookup.Clear();
            outputLookup.Clear();
        }

        public void EnsureStableIds()
        {
            StoryIds.Ensure(ref graphId, "graph");

            foreach (var node in nodes.Where(node => node != null))
            {
                node.EnsureStableIds();
            }

            foreach (var connection in connections.Where(connection => connection != null))
            {
                connection.EnsureStableId();
            }

            InvalidateCache();
        }

#if UNITY_EDITOR
        public void Editor_AddNode(StoryNodeAsset node)
        {
            if (node == null || nodes.Contains(node))
            {
                return;
            }

            nodes.Add(node);
            if (node is StartNodeAsset && string.IsNullOrWhiteSpace(entryNodeId))
            {
                entryNodeId = node.NodeId;
            }

            InvalidateCache();
        }

        public void Editor_RemoveNode(StoryNodeAsset node)
        {
            if (node == null)
            {
                return;
            }

            nodes.Remove(node);
            connections.RemoveAll(connection => connection == null || connection.FromNodeId == node.NodeId || connection.ToNodeId == node.NodeId);

            if (entryNodeId == node.NodeId)
            {
                entryNodeId = nodes.OfType<StartNodeAsset>().FirstOrDefault()?.NodeId ?? string.Empty;
            }

            InvalidateCache();
        }

        public void Editor_AddConnection(StoryConnection connection)
        {
            if (connection == null)
            {
                return;
            }

            connection.EnsureStableId();
            if (connections.Any(existing =>
                    existing != null &&
                    existing.FromNodeId == connection.FromNodeId &&
                    existing.FromPortId == connection.FromPortId &&
                    existing.ToNodeId == connection.ToNodeId &&
                    existing.ToPortId == connection.ToPortId))
            {
                return;
            }

            connections.Add(connection);
            InvalidateCache();
        }

        public void Editor_RemoveConnection(string connectionId)
        {
            connections.RemoveAll(connection => connection != null && connection.ConnectionId == connectionId);
            InvalidateCache();
        }

        public void Editor_PruneInvalidConnections()
        {
            EnsureStableIds();
            var validNodeIds = new HashSet<string>(nodes.Where(node => node != null).Select(node => node.NodeId));
            var validPorts = nodes.Where(node => node != null)
                .ToDictionary(node => node.NodeId, node => new HashSet<string>(node.GetPorts().Select(port => port.Id)));

            connections.RemoveAll(connection =>
                connection == null ||
                string.IsNullOrWhiteSpace(connection.FromNodeId) ||
                string.IsNullOrWhiteSpace(connection.ToNodeId) ||
                !validNodeIds.Contains(connection.FromNodeId) ||
                !validNodeIds.Contains(connection.ToNodeId) ||
                !validPorts.TryGetValue(connection.FromNodeId, out var fromPorts) ||
                !fromPorts.Contains(connection.FromPortId) ||
                !validPorts.TryGetValue(connection.ToNodeId, out var toPorts) ||
                !toPorts.Contains(connection.ToPortId));
            InvalidateCache();
        }
#endif

        private void EnsureCache()
        {
            if (cacheBuilt)
            {
                return;
            }

            nodeLookup.Clear();
            outputLookup.Clear();

            foreach (var node in nodes.Where(node => node != null))
            {
                nodeLookup[node.NodeId] = node;
            }

            foreach (var connection in connections.Where(connection => connection != null))
            {
                var key = MakeOutputKey(connection.FromNodeId, connection.FromPortId);
                if (!outputLookup.TryGetValue(key, out var list))
                {
                    list = new List<StoryConnection>();
                    outputLookup[key] = list;
                }

                list.Add(connection);
            }

            cacheBuilt = true;
        }

        private static string MakeOutputKey(string nodeId, string portId)
        {
            return $"{nodeId}:{portId}";
        }

        private void OnValidate()
        {
            EnsureStableIds();
        }
    }
}
