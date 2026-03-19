using System;
using System.Collections.Generic;
using System.Linq;
using ModularStoryFlow.Runtime.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace ModularStoryFlow.Editor.Graph
{
    internal sealed class StoryGraphSearchWindowProvider : ScriptableObject, ISearchWindowProvider
    {
        private EditorWindow window;
        private StoryGraphView graphView;
        private Texture2D indentationIcon;

        public void Initialize(EditorWindow editorWindow, StoryGraphView targetGraphView)
        {
            window = editorWindow;
            graphView = targetGraphView;

            indentationIcon = new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            indentationIcon.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
            indentationIcon.Apply();
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var result = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Create Story Node"), 0)
            };

            var nodeTypes = TypeCache.GetTypesDerivedFrom<StoryNodeAsset>()
                .Where(type => !type.IsAbstract && type.GetCustomAttributes(typeof(StoryNodeAttribute), false).Length > 0)
                .OrderBy(type =>
                {
                    var attribute = (StoryNodeAttribute)type.GetCustomAttributes(typeof(StoryNodeAttribute), false)[0];
                    return attribute.MenuPath;
                });

            var addedGroups = new HashSet<string>();

            foreach (var nodeType in nodeTypes)
            {
                if (nodeType == typeof(StartNodeAsset) && graphView.CurrentGraph != null && graphView.CurrentGraph.HasAnyStartNode())
                {
                    continue;
                }

                var attribute = (StoryNodeAttribute)nodeType.GetCustomAttributes(typeof(StoryNodeAttribute), false)[0];
                var path = attribute.MenuPath.Split('/');
                for (var depth = 1; depth < path.Length; depth++)
                {
                    var groupPath = string.Join("/", path.Take(depth));
                    if (!addedGroups.Add(groupPath))
                    {
                        continue;
                    }

                    result.Add(new SearchTreeGroupEntry(new GUIContent(path[depth - 1]), depth));
                }

                result.Add(new SearchTreeEntry(new GUIContent(attribute.DisplayName, indentationIcon))
                {
                    level = path.Length,
                    userData = nodeType
                });
            }

            return result;
        }

        public bool OnSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            if (!(searchTreeEntry.userData is Type nodeType) || graphView == null || window == null)
            {
                return false;
            }

            var localMousePosition = context.screenMousePosition - window.position.position;
            var graphMousePosition = graphView.contentViewContainer.worldTransform.inverse.MultiplyPoint3x4(localMousePosition);
            graphView.CreateNode(nodeType, graphMousePosition);
            return true;
        }
    }
}
