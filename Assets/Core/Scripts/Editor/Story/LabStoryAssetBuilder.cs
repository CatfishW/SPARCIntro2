using ModularStoryFlow.Runtime.Actions;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.State;
using UnityEditor;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story.Editor
{
    internal sealed class LabStoryAssetBuilder
    {
        private readonly string rootFolder;
        private readonly string statesFolder;
        private readonly string signalsFolder;
        private readonly string actionsFolder;

        public LabStoryAssetBuilder(string rootFolder)
        {
            this.rootFolder = rootFolder;
            statesFolder = EnsureFolder(rootFolder, "StateMachines");
            signalsFolder = EnsureFolder(rootFolder, "Signals");
            actionsFolder = EnsureFolder(rootFolder, "Actions");
        }

        public LabStoryAssetBundle Build()
        {
            var progressionState = CreateOrLoadAsset<StoryStateMachineDefinition>($"{statesFolder}/LabStoryProgress.asset");
            ConfigureStateMachine(
                progressionState,
                "LabStory.Progress",
                "FreshSpawn",
                ("FreshSpawn", "Fresh Spawn"),
                ("MeetCap", "Meet CAP"),
                ("BodyInspectionReady", "Body Inspection Ready"),
                ("BodyInspected", "Body Inspected"),
                ("PuzzleReady", "Puzzle Ready"),
                ("PuzzleSolved", "Puzzle Solved"),
                ("ShrinkReady", "Shrink Ready"),
                ("Shrunk", "Shrunk"),
                ("RocketReady", "Rocket Ready"),
                ("CutsceneCommitted", "Cutscene Committed"));

            var capTalkedSignal = CreateSignal(LabStorySignals.CapTalked);
            var introBriefingCompletedSignal = CreateSignal(LabStorySignals.IntroBriefingCompleted);
            var bodyInspectedSignal = CreateSignal(LabStorySignals.BodyInspected);
            var puzzleStartedSignal = CreateSignal(LabStorySignals.PuzzleStarted);
            var puzzleSolvedSignal = CreateSignal(LabStorySignals.PuzzleSolved);
            var shrinkConfirmedSignal = CreateSignal(LabStorySignals.ShrinkConfirmed);
            var rocketEnteredSignal = CreateSignal(LabStorySignals.RocketEntered);
            var cutsceneCompletedSignal = CreateSignal(LabStorySignals.CutsceneCompleted);

            var setMeetCapAction = CreateSetStateAction(progressionState, "MeetCap");
            var setBodyInspectionReadyAction = CreateSetStateAction(progressionState, "BodyInspectionReady");
            var setBodyInspectedAction = CreateSetStateAction(progressionState, "BodyInspected");
            var setPuzzleReadyAction = CreateSetStateAction(progressionState, "PuzzleReady");
            var setPuzzleSolvedAction = CreateSetStateAction(progressionState, "PuzzleSolved");
            var setShrinkReadyAction = CreateSetStateAction(progressionState, "ShrinkReady");
            var setShrunkAction = CreateSetStateAction(progressionState, "Shrunk");
            var setRocketReadyAction = CreateSetStateAction(progressionState, "RocketReady");
            var setCutsceneCommittedAction = CreateSetStateAction(progressionState, "CutsceneCommitted");
            var startIntroBriefingAction = CreateOrLoadAsset<LabStartIntroBriefingActionAsset>($"{actionsFolder}/LabStartIntroBriefingAction.asset");

            var raisePuzzleStartedAction = CreateRaiseSignalAction(puzzleStartedSignal, "puzzle-started");

            return new LabStoryAssetBundle(
                progressionState,
                introBriefingCompletedSignal,
                capTalkedSignal,
                bodyInspectedSignal,
                puzzleStartedSignal,
                puzzleSolvedSignal,
                shrinkConfirmedSignal,
                rocketEnteredSignal,
                cutsceneCompletedSignal,
                setMeetCapAction,
                setBodyInspectionReadyAction,
                setBodyInspectedAction,
                setPuzzleReadyAction,
                setPuzzleSolvedAction,
                setShrinkReadyAction,
                setShrunkAction,
                setRocketReadyAction,
                setCutsceneCommittedAction,
                startIntroBriefingAction,
                raisePuzzleStartedAction);
        }

        private StorySignalDefinition CreateSignal(string signalId)
        {
            var signal = CreateOrLoadAsset<StorySignalDefinition>($"{signalsFolder}/{SanitizeAssetName(signalId)}.asset");
            var serialized = new SerializedObject(signal);
            serialized.FindProperty("signalId").stringValue = signalId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            signal.name = signalId;
            EditorUtility.SetDirty(signal);
            return signal;
        }

        private StorySetStateActionAsset CreateSetStateAction(StoryStateMachineDefinition stateMachine, string stateId)
        {
            var asset = CreateOrLoadAsset<StorySetStateActionAsset>($"{actionsFolder}/{SanitizeAssetName(stateId)}StateAction.asset");
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("stateMachine").objectReferenceValue = stateMachine;
            serialized.FindProperty("stateId").stringValue = stateId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private StoryRaiseSignalActionAsset CreateRaiseSignalAction(StorySignalDefinition signal, string payload)
        {
            var asset = CreateOrLoadAsset<StoryRaiseSignalActionAsset>($"{actionsFolder}/{SanitizeAssetName(signal.SignalId)}Raise.asset");
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("signal").objectReferenceValue = signal;
            serialized.FindProperty("payload").stringValue = payload;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private void ConfigureStateMachine(StoryStateMachineDefinition asset, string machineId, string defaultStateId, params (string id, string displayName)[] states)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("machineId").stringValue = machineId;
            serialized.FindProperty("defaultStateId").stringValue = defaultStateId;
            var statesProperty = serialized.FindProperty("states");
            statesProperty.arraySize = states.Length;
            for (var index = 0; index < states.Length; index++)
            {
                var element = statesProperty.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("Id").stringValue = states[index].id;
                element.FindPropertyRelative("DisplayName").stringValue = states[index].displayName;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            asset.name = machineId;
            EditorUtility.SetDirty(asset);
        }

        private static T CreateOrLoadAsset<T>(string assetPath) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static string EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent))
            {
                var parts = parent.Split('/');
                var current = "Assets";
                for (var index = 1; index < parts.Length; index++)
                {
                    var next = $"{current}/{parts[index]}";
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[index]);
                    }

                    current = next;
                }
            }

            var path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }

            return path;
        }

        private static string SanitizeAssetName(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "Asset"
                : value.Replace('.', '_').Replace('/', '_').Replace(' ', '_');
        }
    }

    internal readonly struct LabStoryAssetBundle
    {
        public LabStoryAssetBundle(
            StoryStateMachineDefinition progressionState,
            StorySignalDefinition introBriefingCompletedSignal,
            StorySignalDefinition capTalkedSignal,
            StorySignalDefinition bodyInspectedSignal,
            StorySignalDefinition puzzleStartedSignal,
            StorySignalDefinition puzzleSolvedSignal,
            StorySignalDefinition shrinkConfirmedSignal,
            StorySignalDefinition rocketEnteredSignal,
            StorySignalDefinition cutsceneCompletedSignal,
            StorySetStateActionAsset meetCapStateAction,
            StorySetStateActionAsset bodyInspectionReadyStateAction,
            StorySetStateActionAsset bodyInspectedStateAction,
            StorySetStateActionAsset puzzleReadyStateAction,
            StorySetStateActionAsset puzzleSolvedStateAction,
            StorySetStateActionAsset shrinkReadyStateAction,
            StorySetStateActionAsset shrunkStateAction,
            StorySetStateActionAsset rocketReadyStateAction,
            StorySetStateActionAsset cutsceneCommittedStateAction,
            LabStartIntroBriefingActionAsset startIntroBriefingAction,
            StoryRaiseSignalActionAsset puzzleStartedRaiseAction)
        {
            ProgressionState = progressionState;
            IntroBriefingCompletedSignal = introBriefingCompletedSignal;
            CapTalkedSignal = capTalkedSignal;
            BodyInspectedSignal = bodyInspectedSignal;
            PuzzleStartedSignal = puzzleStartedSignal;
            PuzzleSolvedSignal = puzzleSolvedSignal;
            ShrinkConfirmedSignal = shrinkConfirmedSignal;
            RocketEnteredSignal = rocketEnteredSignal;
            CutsceneCompletedSignal = cutsceneCompletedSignal;
            MeetCapStateAction = meetCapStateAction;
            BodyInspectionReadyStateAction = bodyInspectionReadyStateAction;
            BodyInspectedStateAction = bodyInspectedStateAction;
            PuzzleReadyStateAction = puzzleReadyStateAction;
            PuzzleSolvedStateAction = puzzleSolvedStateAction;
            ShrinkReadyStateAction = shrinkReadyStateAction;
            ShrunkStateAction = shrunkStateAction;
            RocketReadyStateAction = rocketReadyStateAction;
            CutsceneCommittedStateAction = cutsceneCommittedStateAction;
            StartIntroBriefingAction = startIntroBriefingAction;
            PuzzleStartedRaiseAction = puzzleStartedRaiseAction;
        }

        public StoryStateMachineDefinition ProgressionState { get; }
        public StorySignalDefinition IntroBriefingCompletedSignal { get; }
        public StorySignalDefinition CapTalkedSignal { get; }
        public StorySignalDefinition BodyInspectedSignal { get; }
        public StorySignalDefinition PuzzleStartedSignal { get; }
        public StorySignalDefinition PuzzleSolvedSignal { get; }
        public StorySignalDefinition ShrinkConfirmedSignal { get; }
        public StorySignalDefinition RocketEnteredSignal { get; }
        public StorySignalDefinition CutsceneCompletedSignal { get; }
        public StorySetStateActionAsset MeetCapStateAction { get; }
        public StorySetStateActionAsset BodyInspectionReadyStateAction { get; }
        public StorySetStateActionAsset BodyInspectedStateAction { get; }
        public StorySetStateActionAsset PuzzleReadyStateAction { get; }
        public StorySetStateActionAsset PuzzleSolvedStateAction { get; }
        public StorySetStateActionAsset ShrinkReadyStateAction { get; }
        public StorySetStateActionAsset ShrunkStateAction { get; }
        public StorySetStateActionAsset RocketReadyStateAction { get; }
        public StorySetStateActionAsset CutsceneCommittedStateAction { get; }
        public LabStartIntroBriefingActionAsset StartIntroBriefingAction { get; }
        public StoryRaiseSignalActionAsset PuzzleStartedRaiseAction { get; }
    }
}
