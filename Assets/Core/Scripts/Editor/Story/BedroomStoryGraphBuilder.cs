using ModularStoryFlow.Editor.Graph;
using ModularStoryFlow.Runtime.Actions;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Integration;
using UnityEditor;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story.Editor
{
    internal sealed class BedroomStoryGraphBuilder
    {
        private readonly string graphPath;

        public BedroomStoryGraphBuilder(string graphPath)
        {
            this.graphPath = graphPath;
        }

        public StoryGraphAsset Build(BedroomStoryAssetBundle assets)
        {
            var existing = AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(graphPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(graphPath);
            }

            var graph = StoryGraphEditorUtility.CreateGraphAsset(graphPath);
            var start = graph.GetEntryNode() as StartNodeAsset;
            var fadeDelay = StoryGraphEditorUtility.AddNode<DelayNodeAsset>(graph, new Vector2(320f, 120f));
            var firstDialogue = StoryGraphEditorUtility.AddNode<DialogueNodeAsset>(graph, new Vector2(640f, 120f));
            var secondDialogue = StoryGraphEditorUtility.AddNode<DialogueNodeAsset>(graph, new Vector2(960f, 120f));
            var objectiveDialogue = StoryGraphEditorUtility.AddNode<DialogueNodeAsset>(graph, new Vector2(1280f, 120f));
            var objectiveAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(1600f, 120f));
            var waitLaptop = StoryGraphEditorUtility.AddNode<WaitSignalNodeAsset>(graph, new Vector2(1920f, 120f));
            var laptopResolvedAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(2240f, 120f));
            var readyDialogue = StoryGraphEditorUtility.AddNode<DialogueNodeAsset>(graph, new Vector2(2560f, 120f));
            var leaveDialogue = StoryGraphEditorUtility.AddNode<DialogueNodeAsset>(graph, new Vector2(2880f, 120f));
            var doorReadyAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(3200f, 120f));
            var waitDoor = StoryGraphEditorUtility.AddNode<WaitSignalNodeAsset>(graph, new Vector2(3520f, 120f));
            var exitDialogue = StoryGraphEditorUtility.AddNode<DialogueNodeAsset>(graph, new Vector2(3840f, 120f));
            var transitionAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(4160f, 120f));
            var end = StoryGraphEditorUtility.AddNode<EndNodeAsset>(graph, new Vector2(4480f, 120f));

            ConfigureDelay(fadeDelay, 0.6f);
            ConfigureDialogue(firstDialogue, string.Empty, "...What day is it today?", true, 1.85f);
            ConfigureDialogue(secondDialogue, string.Empty, "Feels like I'm forgetting something.", true, 1.85f);
            ConfigureDialogue(objectiveDialogue, string.Empty, "Maybe I should check my laptop... then head to school.", true, 2.25f);
            ConfigureActionNode(objectiveAction, assets.LaptopObjectiveStateAction);
            ConfigureWaitSignal(waitLaptop, assets.LaptopSignal);
            ConfigureActionNode(
                laptopResolvedAction,
                assets.LaptopCheckedAction,
                assets.CanonicalPathAction,
                assets.LaptopResolvedStateAction);
            ConfigureDialogue(readyDialogue, string.Empty, "Right... I need to go.", true, 1.45f);
            ConfigureDialogue(leaveDialogue, string.Empty, "Better head out now.", true, 1.6f);
            ConfigureActionNode(
                doorReadyAction,
                assets.IntroReadyAction,
                assets.DoorReadyStateAction,
                assets.TimelineCompletedRaiseAction);
            ConfigureWaitSignal(waitDoor, assets.DoorSignal);
            ConfigureDialogue(exitDialogue, string.Empty, "Alright... here goes.", true, 1.35f);
            ConfigureActionNode(transitionAction, assets.TransitionStateAction);

            StoryGraphEditorUtility.Connect(graph, start, StoryNodeAsset.DefaultOutputPortId, fadeDelay);
            StoryGraphEditorUtility.Connect(graph, fadeDelay, StoryNodeAsset.DefaultOutputPortId, firstDialogue);
            StoryGraphEditorUtility.Connect(graph, firstDialogue, StoryNodeAsset.DefaultOutputPortId, secondDialogue);
            StoryGraphEditorUtility.Connect(graph, secondDialogue, StoryNodeAsset.DefaultOutputPortId, objectiveDialogue);
            StoryGraphEditorUtility.Connect(graph, objectiveDialogue, StoryNodeAsset.DefaultOutputPortId, objectiveAction);
            StoryGraphEditorUtility.Connect(graph, objectiveAction, StoryNodeAsset.DefaultOutputPortId, waitLaptop);
            StoryGraphEditorUtility.Connect(graph, waitLaptop, StoryNodeAsset.DefaultOutputPortId, laptopResolvedAction);
            StoryGraphEditorUtility.Connect(graph, laptopResolvedAction, StoryNodeAsset.DefaultOutputPortId, readyDialogue);
            StoryGraphEditorUtility.Connect(graph, readyDialogue, StoryNodeAsset.DefaultOutputPortId, leaveDialogue);
            StoryGraphEditorUtility.Connect(graph, leaveDialogue, StoryNodeAsset.DefaultOutputPortId, doorReadyAction);
            StoryGraphEditorUtility.Connect(graph, doorReadyAction, StoryNodeAsset.DefaultOutputPortId, waitDoor);
            StoryGraphEditorUtility.Connect(graph, waitDoor, StoryNodeAsset.DefaultOutputPortId, exitDialogue);
            StoryGraphEditorUtility.Connect(graph, exitDialogue, StoryNodeAsset.DefaultOutputPortId, transitionAction);
            StoryGraphEditorUtility.Connect(graph, transitionAction, StoryNodeAsset.DefaultOutputPortId, end);

            StoryGraphEditorUtility.SaveGraph(graph);
            return graph;
        }

        private static void ConfigureDialogue(DialogueNodeAsset node, string speaker, string body, bool autoAdvance, float autoAdvanceDelaySeconds)
        {
            var serialized = new SerializedObject(node);
            serialized.FindProperty("speakerDisplayName").stringValue = speaker;
            serialized.FindProperty("body").stringValue = body;
            serialized.FindProperty("autoAdvance").boolValue = autoAdvance;
            serialized.FindProperty("autoAdvanceDelaySeconds").floatValue = autoAdvanceDelaySeconds;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(node);
        }

        private static void ConfigureDelay(DelayNodeAsset node, float seconds)
        {
            var serialized = new SerializedObject(node);
            serialized.FindProperty("seconds").floatValue = seconds;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(node);
        }

        private static void ConfigureWaitSignal(WaitSignalNodeAsset node, StorySignalDefinition signal)
        {
            var serialized = new SerializedObject(node);
            serialized.FindProperty("signal").objectReferenceValue = signal;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(node);
        }

        private static void ConfigureActionNode(ActionNodeAsset node, params StoryActionAsset[] configuredActions)
        {
            var serialized = new SerializedObject(node);
            var actions = serialized.FindProperty("actions");
            var actionCount = 0;

            if (configuredActions != null)
            {
                for (var index = 0; index < configuredActions.Length; index++)
                {
                    if (configuredActions[index] != null)
                    {
                        actionCount++;
                    }
                }
            }

            actions.arraySize = actionCount;
            var writeIndex = 0;
            if (configuredActions != null)
            {
                for (var index = 0; index < configuredActions.Length; index++)
                {
                    var configuredAction = configuredActions[index];
                    if (configuredAction == null)
                    {
                        continue;
                    }

                    actions.GetArrayElementAtIndex(writeIndex).objectReferenceValue = configuredAction;
                    writeIndex++;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(node);
        }
    }
}
