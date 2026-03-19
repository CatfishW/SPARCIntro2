using ModularStoryFlow.Runtime.Graph;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ModularStoryFlow.Editor.Graph
{
    public sealed class StoryGraphEditorWindow : EditorWindow
    {
        private ObjectField graphField;
        private StoryGraphView graphView;
        private StoryGraphAsset currentGraph;

        [MenuItem("Tools/Modular Story Flow/Graph Editor", priority = 0)]
        public static void OpenWindow()
        {
            GetWindow<StoryGraphEditorWindow>("Story Flow Graph");
        }

        public static void Open(StoryGraphAsset graph)
        {
            var window = GetWindow<StoryGraphEditorWindow>("Story Flow Graph");
            window.Focus();
            window.SetGraph(graph);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += HandleUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();

            var toolbar = new Toolbar();
            graphField = new ObjectField("Graph")
            {
                objectType = typeof(StoryGraphAsset),
                value = currentGraph,
                style = { minWidth = 260f }
            };
            graphField.RegisterValueChangedCallback(evt => SetGraph(evt.newValue as StoryGraphAsset));

            var refreshButton = new Button(() => graphView?.RefreshGraph()) { text = "Refresh" };
            var frameButton = new Button(() => graphView?.FrameAll()) { text = "Frame All" };
            var createButton = new Button(() => StoryGraphEditorUtility.CreateGraphAssetFromMenu()) { text = "New Graph" };

            toolbar.Add(graphField);
            toolbar.Add(refreshButton);
            toolbar.Add(frameButton);
            toolbar.Add(createButton);
            rootVisualElement.Add(toolbar);

            graphView = new StoryGraphView(this);
            rootVisualElement.Add(graphView);

            if (currentGraph != null)
            {
                graphView.SetGraph(currentGraph);
            }
        }

        private void SetGraph(StoryGraphAsset graph)
        {
            currentGraph = graph;
            titleContent = new GUIContent(currentGraph != null ? $"Story Flow: {currentGraph.name}" : "Story Flow Graph");

            if (graphField != null)
            {
                graphField.SetValueWithoutNotify(graph);
            }

            graphView?.SetGraph(currentGraph);
        }

        private void HandleUndoRedo()
        {
            graphView?.RefreshGraph();
            Repaint();
        }
    }
}
