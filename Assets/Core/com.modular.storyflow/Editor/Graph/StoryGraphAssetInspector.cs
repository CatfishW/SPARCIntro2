using ModularStoryFlow.Runtime.Graph;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace ModularStoryFlow.Editor.Graph
{
    [CustomEditor(typeof(StoryGraphAsset))]
    public sealed class StoryGraphAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            GUILayout.Space(8f);

            if (GUILayout.Button("Open Graph Editor", GUILayout.Height(28f)))
            {
                StoryGraphEditorWindow.Open((StoryGraphAsset)target);
            }
        }

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            var graph = EditorUtility.InstanceIDToObject(instanceId) as StoryGraphAsset;
            if (graph == null)
            {
                return false;
            }

            StoryGraphEditorWindow.Open(graph);
            return true;
        }
    }
}
