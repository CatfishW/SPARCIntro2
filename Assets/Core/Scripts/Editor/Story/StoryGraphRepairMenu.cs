using System.IO;
using System.Collections.Generic;
using System.Linq;
using ModularStoryFlow.Runtime.Graph;
using UnityEditor;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story.Editor
{
    public static class StoryGraphRepairMenu
    {
        private const string BedroomGraphPath = "Assets/StoryFlowBedroomGenerated/Graphs/BedroomIntroStory.asset";
        private const string RootGraphPath = "Assets/StoryGraph.asset";

        [MenuItem("Tools/Blocks/Story/Repair Story Graphs", priority = 301)]
        public static void RepairAllStoryGraphs()
        {
            var graphPaths = new List<string>
            {
                BedroomGraphPath,
                RootGraphPath
            };

            var discoveredPaths = AssetDatabase.FindAssets("t:StoryGraphAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path));

            graphPaths.AddRange(discoveredPaths);
            graphPaths = graphPaths.Distinct().ToList();

            var repairedCount = 0;
            for (var index = 0; index < graphPaths.Count; index++)
            {
                if (RepairGraphAtPath(graphPaths[index]))
                {
                    repairedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Repaired {repairedCount} story graph(s).");
        }

        [MenuItem("Tools/Blocks/Story/Repair Selected Story Graph", priority = 302)]
        public static void RepairSelectedStoryGraph()
        {
            if (Selection.activeObject is not StoryGraphAsset selectedGraph)
            {
                Debug.LogWarning("Select a StoryGraphAsset first, or use Tools/Blocks/Story/Repair Story Graphs.");
                return;
            }

            var graphPath = AssetDatabase.GetAssetPath(selectedGraph);
            if (RepairGraphAtPath(graphPath))
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static bool RepairGraphAtPath(string graphPath)
        {
            if (string.IsNullOrWhiteSpace(graphPath))
            {
                return false;
            }

            var graph = AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(graphPath);
            if (graph == null)
            {
                return false;
            }

            var nodes = LoadGraphNodes(graphPath);

            if (nodes.Count == 0)
            {
                Debug.LogWarning($"No node assets were found for {graphPath}.");
                return false;
            }

            var serializedGraph = new SerializedObject(graph);
            var nodesProperty = serializedGraph.FindProperty("nodes");
            nodesProperty.arraySize = nodes.Count;
            for (var i = 0; i < nodes.Count; i++)
            {
                nodesProperty.GetArrayElementAtIndex(i).objectReferenceValue = nodes[i];
            }

            var entryNode = nodes.OfType<StartNodeAsset>().FirstOrDefault();
            if (entryNode != null)
            {
                serializedGraph.FindProperty("entryNodeId").stringValue = entryNode.NodeId;
            }

            serializedGraph.ApplyModifiedPropertiesWithoutUndo();
            graph.InvalidateCache();
            EditorUtility.SetDirty(graph);

            AssetDatabase.ForceReserializeAssets(new[] { graphPath });
            AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        private static List<StoryNodeAsset> LoadGraphNodes(string graphPath)
        {
            var nodes = new List<StoryNodeAsset>();

            var nodeFolderPath = GetNodeFolderPath(graphPath);
            if (!string.IsNullOrWhiteSpace(nodeFolderPath) && AssetDatabase.IsValidFolder(nodeFolderPath))
            {
                nodes.AddRange(
                    AssetDatabase.FindAssets("t:StoryNodeAsset", new[] { nodeFolderPath })
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Select(path => AssetDatabase.LoadAssetAtPath<StoryNodeAsset>(path))
                        .Where(node => node != null));
            }

            if (nodes.Count == 0)
            {
                nodes.AddRange(
                    AssetDatabase.LoadAllAssetsAtPath(graphPath)
                        .OfType<StoryNodeAsset>()
                        .Where(node => node != null));
            }

            return nodes
                .Distinct()
                .OrderBy(node => node.EditorPosition.x)
                .ThenBy(node => node.EditorPosition.y)
                .ThenBy(node => node.NodeId)
                .ToList();
        }

        private static string GetNodeFolderPath(string graphPath)
        {
            var directory = Path.GetDirectoryName(graphPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            return $"{directory}/{Path.GetFileNameWithoutExtension(graphPath)}_Nodes";
        }
    }
}
