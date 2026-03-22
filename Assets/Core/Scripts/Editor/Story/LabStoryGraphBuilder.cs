using System.Linq;
using ModularStoryFlow.Editor.Graph;
using ModularStoryFlow.Runtime.Actions;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Integration;
using UnityEditor;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story.Editor
{
    internal sealed class LabStoryGraphBuilder
    {
        private readonly string graphPath;

        public LabStoryGraphBuilder(string graphPath)
        {
            this.graphPath = graphPath;
        }

        public StoryGraphAsset Build(LabStoryAssetBundle assets)
        {
            var existing = AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(graphPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(graphPath);
            }

            var graph = StoryGraphEditorUtility.CreateGraphAsset(graphPath);
            var start = graph.GetEntryNode() as StartNodeAsset;
            var delay = StoryGraphEditorUtility.AddNode<DelayNodeAsset>(graph, new Vector2(320f, 120f));
            var introBriefingAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(640f, 120f));
            var waitIntro = StoryGraphEditorUtility.AddNode<WaitSignalNodeAsset>(graph, new Vector2(960f, 120f));
            var meetCapAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(1280f, 120f));
            var waitCap = StoryGraphEditorUtility.AddNode<WaitSignalNodeAsset>(graph, new Vector2(1600f, 120f));
            var bodyReadyAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(1920f, 120f));
            var waitBody = StoryGraphEditorUtility.AddNode<WaitSignalNodeAsset>(graph, new Vector2(2240f, 120f));
            var bodyInspectedAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(2560f, 120f));
            var puzzleReadyAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(2880f, 120f));
            var waitPuzzle = StoryGraphEditorUtility.AddNode<WaitSignalNodeAsset>(graph, new Vector2(3200f, 120f));
            var puzzleSolvedAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(3520f, 120f));
            var shrinkReadyAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(3840f, 120f));
            var waitShrink = StoryGraphEditorUtility.AddNode<WaitSignalNodeAsset>(graph, new Vector2(4160f, 120f));
            var shrunkAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(4480f, 120f));
            var rocketReadyAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(4800f, 120f));
            var waitRocket = StoryGraphEditorUtility.AddNode<WaitSignalNodeAsset>(graph, new Vector2(5120f, 120f));
            var cutsceneCommittedAction = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(5440f, 120f));
            var waitCutscene = StoryGraphEditorUtility.AddNode<WaitSignalNodeAsset>(graph, new Vector2(5760f, 120f));
            var end = StoryGraphEditorUtility.AddNode<EndNodeAsset>(graph, new Vector2(6080f, 120f));

            ConfigureDelay(delay, 0.5f);
            ConfigureActionNode(introBriefingAction, assets.StartIntroBriefingAction);
            ConfigureWaitSignal(waitIntro, assets.IntroBriefingCompletedSignal);
            ConfigureActionNode(meetCapAction, assets.MeetCapStateAction);
            ConfigureWaitSignal(waitCap, assets.CapTalkedSignal);
            ConfigureActionNode(bodyReadyAction, assets.BodyInspectionReadyStateAction);
            ConfigureWaitSignal(waitBody, assets.BodyInspectedSignal);
            ConfigureActionNode(bodyInspectedAction, assets.BodyInspectedStateAction);
            ConfigureActionNode(puzzleReadyAction, assets.PuzzleReadyStateAction, assets.PuzzleStartedRaiseAction);
            ConfigureWaitSignal(waitPuzzle, assets.PuzzleSolvedSignal);
            ConfigureActionNode(puzzleSolvedAction, assets.PuzzleSolvedStateAction);
            ConfigureActionNode(shrinkReadyAction, assets.ShrinkReadyStateAction);
            ConfigureWaitSignal(waitShrink, assets.ShrinkConfirmedSignal);
            ConfigureActionNode(shrunkAction, assets.ShrunkStateAction);
            ConfigureActionNode(rocketReadyAction, assets.RocketReadyStateAction);
            ConfigureWaitSignal(waitRocket, assets.RocketEnteredSignal);
            ConfigureActionNode(cutsceneCommittedAction, assets.CutsceneCommittedStateAction);
            ConfigureWaitSignal(waitCutscene, assets.CutsceneCompletedSignal);

            StoryGraphEditorUtility.Connect(graph, start, StoryNodeAsset.DefaultOutputPortId, delay);
            StoryGraphEditorUtility.Connect(graph, delay, StoryNodeAsset.DefaultOutputPortId, introBriefingAction);
            StoryGraphEditorUtility.Connect(graph, introBriefingAction, StoryNodeAsset.DefaultOutputPortId, waitIntro);
            StoryGraphEditorUtility.Connect(graph, waitIntro, StoryNodeAsset.DefaultOutputPortId, meetCapAction);
            StoryGraphEditorUtility.Connect(graph, meetCapAction, StoryNodeAsset.DefaultOutputPortId, waitCap);
            StoryGraphEditorUtility.Connect(graph, waitCap, StoryNodeAsset.DefaultOutputPortId, bodyReadyAction);
            StoryGraphEditorUtility.Connect(graph, bodyReadyAction, StoryNodeAsset.DefaultOutputPortId, waitBody);
            StoryGraphEditorUtility.Connect(graph, waitBody, StoryNodeAsset.DefaultOutputPortId, bodyInspectedAction);
            StoryGraphEditorUtility.Connect(graph, bodyInspectedAction, StoryNodeAsset.DefaultOutputPortId, puzzleReadyAction);
            StoryGraphEditorUtility.Connect(graph, puzzleReadyAction, StoryNodeAsset.DefaultOutputPortId, waitPuzzle);
            StoryGraphEditorUtility.Connect(graph, waitPuzzle, StoryNodeAsset.DefaultOutputPortId, puzzleSolvedAction);
            StoryGraphEditorUtility.Connect(graph, puzzleSolvedAction, StoryNodeAsset.DefaultOutputPortId, shrinkReadyAction);
            StoryGraphEditorUtility.Connect(graph, shrinkReadyAction, StoryNodeAsset.DefaultOutputPortId, waitShrink);
            StoryGraphEditorUtility.Connect(graph, waitShrink, StoryNodeAsset.DefaultOutputPortId, shrunkAction);
            StoryGraphEditorUtility.Connect(graph, shrunkAction, StoryNodeAsset.DefaultOutputPortId, rocketReadyAction);
            StoryGraphEditorUtility.Connect(graph, rocketReadyAction, StoryNodeAsset.DefaultOutputPortId, waitRocket);
            StoryGraphEditorUtility.Connect(graph, waitRocket, StoryNodeAsset.DefaultOutputPortId, cutsceneCommittedAction);
            StoryGraphEditorUtility.Connect(graph, cutsceneCommittedAction, StoryNodeAsset.DefaultOutputPortId, waitCutscene);
            StoryGraphEditorUtility.Connect(graph, waitCutscene, StoryNodeAsset.DefaultOutputPortId, end);

            StoryGraphEditorUtility.SaveGraph(graph);
            return graph;
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
            var actionsProperty = serialized.FindProperty("actions");
            var validActions = configuredActions?.Count(action => action != null) ?? 0;
            actionsProperty.arraySize = validActions;
            var writeIndex = 0;
            if (configuredActions != null)
            {
                for (var index = 0; index < configuredActions.Length; index++)
                {
                    var action = configuredActions[index];
                    if (action == null)
                    {
                        continue;
                    }

                    actionsProperty.GetArrayElementAtIndex(writeIndex).objectReferenceValue = action;
                    writeIndex++;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(node);
        }
    }
}
