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
    internal sealed class StoryNodeView : Node
    {
        private static readonly HashSet<string> HiddenProperties = new HashSet<string>
        {
            "m_Script",
            "nodeId",
            "editorPosition"
        };

        private readonly Dictionary<string, Port> ports = new Dictionary<string, Port>();

        public StoryNodeView(StoryNodeAsset nodeAsset)
        {
            NodeAsset = nodeAsset;
            viewDataKey = nodeAsset.NodeId;
            title = nodeAsset.DisplayTitle;
            SetPosition(nodeAsset.EditorPosition);

            BuildPorts();
            BuildInspector();
            RefreshExpandedState();
            RefreshPorts();
        }

        public event Action<StoryNodeView> InspectorChanged;

        public StoryNodeAsset NodeAsset { get; }

        public Port GetPort(string portId)
        {
            ports.TryGetValue(portId, out var port);
            return port;
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            if (NodeAsset == null)
            {
                return;
            }

            NodeAsset.EditorPosition = newPos;
            EditorUtility.SetDirty(NodeAsset);
        }

        private void BuildPorts()
        {
            ports.Clear();
            inputContainer.Clear();
            outputContainer.Clear();

            foreach (var portDefinition in NodeAsset.GetPorts())
            {
                var direction = portDefinition.Direction == StoryPortDirection.Input ? Direction.Input : Direction.Output;
                var capacity = portDefinition.Capacity == StoryPortCapacity.Single ? Port.Capacity.Single : Port.Capacity.Multi;
                var port = Port.Create<Edge>(Orientation.Horizontal, direction, capacity, typeof(bool));
                port.portName = portDefinition.DisplayName;
                port.userData = portDefinition.Id;

                ports[portDefinition.Id] = port;

                if (direction == Direction.Input)
                {
                    inputContainer.Add(port);
                }
                else
                {
                    outputContainer.Add(port);
                }
            }
        }

        private void BuildInspector()
        {
            extensionContainer.Clear();
            var serializedObject = new SerializedObject(NodeAsset);

            extensionContainer.Add(new IMGUIContainer(() =>
            {
                if (NodeAsset == null || serializedObject == null || serializedObject.targetObject == null)
                {
                    return;
                }

                try
                {
                    serializedObject.UpdateIfRequiredOrScript();
                }
                catch (MissingReferenceException)
                {
                    return;
                }
                catch (NullReferenceException)
                {
                    return;
                }

                EditorGUI.BeginChangeCheck();
                var iterator = serializedObject.GetIterator();
                var enterChildren = true;

                while (iterator.NextVisible(enterChildren))
                {
                    if (!HiddenProperties.Contains(iterator.name))
                    {
                        EditorGUILayout.PropertyField(iterator, true);
                    }

                    enterChildren = false;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    NodeAsset.EnsureStableIds();
                    EditorUtility.SetDirty(NodeAsset);
                    InspectorChanged?.Invoke(this);
                }
            }));
        }
    }
}
