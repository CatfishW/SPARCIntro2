using System;
using System.IO;
using ModularStoryFlow.Runtime.Graph;
using UnityEditor;
using UnityEngine;

namespace ModularStoryFlow.Editor.Graph
{
    /// <summary>
    /// Reusable editor helpers for creating and mutating graph assets.
    /// </summary>
    public static class StoryGraphEditorUtility
    {
        [MenuItem("Assets/Create/Story Flow/Story Graph Asset", priority = 120)]
        public static void CreateGraphAssetFromMenu()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Story Graph",
                "StoryGraph",
                "asset",
                "Choose a location for the new story graph asset.");

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var graph = CreateGraphAsset(path);
            Selection.activeObject = graph;
            StoryGraphEditorWindow.Open(graph);
        }

        public static StoryGraphAsset CreateGraphAsset(string assetPath)
        {
            var graph = ScriptableObject.CreateInstance<StoryGraphAsset>();
            AssetDatabase.CreateAsset(graph, assetPath);

            var startNode = AddNode(graph, typeof(StartNodeAsset), new Vector2(120f, 200f));
            graph.EntryNodeId = startNode.NodeId;

            SaveGraph(graph);
            return graph;
        }

        public static T AddNode<T>(StoryGraphAsset graph, Vector2 position) where T : StoryNodeAsset
        {
            return AddNode(graph, typeof(T), position) as T;
        }

        public static StoryNodeAsset AddNode(StoryGraphAsset graph, Type nodeType, Vector2 position)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (nodeType == null || !typeof(StoryNodeAsset).IsAssignableFrom(nodeType))
            {
                throw new ArgumentException("nodeType must derive from StoryNodeAsset.", nameof(nodeType));
            }

            var node = ScriptableObject.CreateInstance(nodeType) as StoryNodeAsset;
            if (node == null)
            {
                throw new InvalidOperationException($"Could not create node of type {nodeType.FullName}.");
            }

            node.name = nodeType.Name;
            node.EnsureStableIds();
            node.EditorPosition = new Rect(position.x, position.y, 280f, 220f);

            Undo.RegisterCreatedObjectUndo(node, $"Create {nodeType.Name}");
            var graphPath = AssetDatabase.GetAssetPath(graph);
            var nodeAssetPath = CreateNodeAssetPath(graphPath, nodeType.Name, node.NodeId);
            if (!string.IsNullOrWhiteSpace(nodeAssetPath))
            {
                AssetDatabase.CreateAsset(node, nodeAssetPath);
            }
            else
            {
                AssetDatabase.AddObjectToAsset(node, graph);
            }

            graph.Editor_AddNode(node);
            EditorUtility.SetDirty(node);
            EditorUtility.SetDirty(graph);
            return node;
        }

        public static void Connect(StoryGraphAsset graph, StoryNodeAsset fromNode, string fromPortId, StoryNodeAsset toNode, string toPortId = StoryNodeAsset.DefaultInputPortId)
        {
            if (graph == null || fromNode == null || toNode == null)
            {
                return;
            }

            var connection = new StoryConnection
            {
                FromNodeId = fromNode.NodeId,
                FromPortId = fromPortId,
                ToNodeId = toNode.NodeId,
                ToPortId = toPortId
            };

            graph.Editor_AddConnection(connection);
            EditorUtility.SetDirty(graph);
        }

        public static void SaveGraph(StoryGraphAsset graph)
        {
            if (graph == null)
            {
                return;
            }

            graph.Editor_PruneInvalidConnections();
            EditorUtility.SetDirty(graph);
            var graphPath = AssetDatabase.GetAssetPath(graph);
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrWhiteSpace(graphPath))
            {
                AssetDatabase.ForceReserializeAssets(new[] { graphPath });
                AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.Refresh();
        }

        private static string CreateNodeAssetPath(string graphPath, string nodeTypeName, string nodeId)
        {
            if (string.IsNullOrWhiteSpace(graphPath))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(graphPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            var folderName = $"{Path.GetFileNameWithoutExtension(graphPath)}_Nodes";
            var nodeFolderPath = $"{directory}/{folderName}";
            if (!AssetDatabase.IsValidFolder(nodeFolderPath))
            {
                AssetDatabase.CreateFolder(directory, folderName);
            }

            return $"{nodeFolderPath}/{nodeTypeName}_{nodeId}.asset";
        }
    }
}
