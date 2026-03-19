using System;
using System.Collections.Generic;
using System.Linq;
using ModularStoryFlow.Runtime.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ModularStoryFlow.Editor.Graph
{
    internal sealed class StoryGraphView : GraphView
    {
        private readonly StoryGraphSearchWindowProvider searchProvider;
        private readonly Dictionary<string, StoryNodeView> nodeViews = new Dictionary<string, StoryNodeView>();
        private bool suppressGraphChanges;

        public StoryGraphView(EditorWindow window)
        {
            name = "StoryGraphView";
            style.flexGrow = 1f;

            var grid = new GridBackground();
            grid.StretchToParentSize();
            Insert(0, grid);

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            searchProvider = ScriptableObject.CreateInstance<StoryGraphSearchWindowProvider>();
            searchProvider.Initialize(window, this);

            nodeCreationRequest = context => SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), searchProvider);
            graphViewChanged = OnGraphViewChanged;
        }

        public StoryGraphAsset CurrentGraph { get; private set; }

        public void SetGraph(StoryGraphAsset graph)
        {
            CurrentGraph = graph;
            RefreshGraph();
        }

        public void RefreshGraph()
        {
            suppressGraphChanges = true;
            try
            {
                DeleteCurrentElements();
                nodeViews.Clear();

                if (CurrentGraph == null)
                {
                    return;
                }

                CurrentGraph.EnsureStableIds();

                foreach (var nodeAsset in CurrentGraph.Nodes.Where(node => node != null))
                {
                    CreateNodeView(nodeAsset);
                }

                foreach (var connection in CurrentGraph.Connections.Where(connection => connection != null))
                {
                    if (!nodeViews.TryGetValue(connection.FromNodeId, out var fromView) ||
                        !nodeViews.TryGetValue(connection.ToNodeId, out var toView))
                    {
                        continue;
                    }

                    var outputPort = fromView.GetPort(connection.FromPortId);
                    var inputPort = toView.GetPort(connection.ToPortId);

                    if (outputPort == null || inputPort == null)
                    {
                        continue;
                    }

                    var edge = outputPort.ConnectTo(inputPort);
                    edge.userData = connection.ConnectionId;
                    AddElement(edge);
                }

                FrameAll();
            }
            finally
            {
                suppressGraphChanges = false;
            }
        }

        public void CreateNode(Type nodeType, Vector2 position)
        {
            if (CurrentGraph == null)
            {
                return;
            }

            if (nodeType == typeof(StartNodeAsset) && CurrentGraph.HasAnyStartNode())
            {
                return;
            }

            suppressGraphChanges = true;
            try
            {
                var nodeAsset = StoryGraphEditorUtility.AddNode(CurrentGraph, nodeType, position);
                StoryGraphEditorUtility.SaveGraph(CurrentGraph);
                CreateNodeView(nodeAsset);
            }
            finally
            {
                suppressGraphChanges = false;
            }
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return ports.Where(candidate =>
                candidate != startPort &&
                candidate.node != startPort.node &&
                candidate.direction != startPort.direction).ToList();
        }

        private StoryNodeView CreateNodeView(StoryNodeAsset nodeAsset)
        {
            var view = new StoryNodeView(nodeAsset);
            view.InspectorChanged += HandleNodeInspectorChanged;
            nodeViews[nodeAsset.NodeId] = view;
            AddElement(view);
            return view;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (suppressGraphChanges || CurrentGraph == null)
            {
                return change;
            }

            var requiresRefresh = false;

            if (change.elementsToRemove != null)
            {
                foreach (var edge in change.elementsToRemove.OfType<Edge>().ToList())
                {
                    if (edge.userData is string connectionId)
                    {
                        CurrentGraph.Editor_RemoveConnection(connectionId);
                        requiresRefresh = true;
                    }
                }

                foreach (var nodeView in change.elementsToRemove.OfType<StoryNodeView>().ToList())
                {
                    CurrentGraph.Editor_RemoveNode(nodeView.NodeAsset);
                    if (nodeViews.ContainsKey(nodeView.NodeAsset.NodeId))
                    {
                        nodeViews.Remove(nodeView.NodeAsset.NodeId);
                    }

                    UnityEngine.Object.DestroyImmediate(nodeView.NodeAsset, true);
                    requiresRefresh = true;
                }
            }

            if (change.edgesToCreate != null)
            {
                foreach (var edge in change.edgesToCreate)
                {
                    if (!(edge.output?.node is StoryNodeView fromNode) || !(edge.input?.node is StoryNodeView toNode))
                    {
                        continue;
                    }

                    var connection = new StoryConnection
                    {
                        FromNodeId = fromNode.NodeAsset.NodeId,
                        FromPortId = edge.output.userData as string,
                        ToNodeId = toNode.NodeAsset.NodeId,
                        ToPortId = edge.input.userData as string
                    };
                    connection.EnsureStableId();
                    edge.userData = connection.ConnectionId;

                    if (edge.input.capacity == Port.Capacity.Single)
                    {
                        var existingConnections = CurrentGraph.Connections
                            .Where(existing => existing != null &&
                                               existing.ToNodeId == connection.ToNodeId &&
                                               existing.ToPortId == connection.ToPortId)
                            .Select(existing => existing.ConnectionId)
                            .ToList();

                        foreach (var existingConnectionId in existingConnections)
                        {
                            CurrentGraph.Editor_RemoveConnection(existingConnectionId);
                            requiresRefresh = true;
                        }
                    }

                    CurrentGraph.Editor_AddConnection(connection);
                    requiresRefresh = true;
                }
            }

            StoryGraphEditorUtility.SaveGraph(CurrentGraph);
            if (requiresRefresh)
            {
                schedule.Execute(RefreshGraph).ExecuteLater(0);
            }

            return change;
        }

        private void HandleNodeInspectorChanged(StoryNodeView _)
        {
            if (CurrentGraph == null)
            {
                return;
            }

            StoryGraphEditorUtility.SaveGraph(CurrentGraph);
            RefreshGraph();
        }

        private void DeleteCurrentElements()
        {
            var allElements = graphElements.ToList();
            foreach (var element in allElements)
            {
                RemoveElement(element);
            }
        }
    }
}
