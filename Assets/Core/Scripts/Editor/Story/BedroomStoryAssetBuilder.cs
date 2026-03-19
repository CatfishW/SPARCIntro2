using Blocks.Gameplay.Core.Story;
using ModularStoryFlow.Editor.Setup;
using ModularStoryFlow.Runtime.Actions;
using ModularStoryFlow.Runtime.Conditions;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;
using UnityEditor;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story.Editor
{
    internal sealed class BedroomStoryAssetBuilder
    {
        private readonly string rootFolder;
        private readonly string variablesFolder;
        private readonly string statesFolder;
        private readonly string signalsFolder;
        private readonly string timelinesFolder;
        private readonly string conditionsFolder;
        private readonly string actionsFolder;

        public BedroomStoryAssetBuilder(string rootFolder)
        {
            this.rootFolder = rootFolder;
            variablesFolder = EnsureFolder(rootFolder, "Variables");
            statesFolder = EnsureFolder(rootFolder, "StateMachines");
            signalsFolder = EnsureFolder(rootFolder, "Signals");
            timelinesFolder = EnsureFolder(rootFolder, "Timelines");
            conditionsFolder = EnsureFolder(rootFolder, "Conditions");
            actionsFolder = EnsureFolder(rootFolder, "Actions");
        }

        public BedroomStoryAssetBundle Build()
        {
            var introReady = CreateOrLoadAsset<StoryBooleanVariableDefinition>($"{variablesFolder}/IntroReady.asset");
            ConfigureVariable(introReady, "intro.readyToLeave", false);
            var laptopChecked = CreateOrLoadAsset<StoryBooleanVariableDefinition>($"{variablesFolder}/LaptopChecked.asset");
            ConfigureVariable(laptopChecked, "intro.checkedLaptop", false);
            var canonicalPath = CreateOrLoadAsset<StoryBooleanVariableDefinition>($"{variablesFolder}/CanonicalPath.asset");
            ConfigureVariable(canonicalPath, "intro.pathCanonical", true);

            var progressionState = CreateOrLoadAsset<StoryStateMachineDefinition>($"{statesFolder}/BedroomStoryProgress.asset");
            ConfigureStateMachine(
                progressionState,
                "BedroomStory.Progress",
                "FreshSpawn",
                ("FreshSpawn", "FreshSpawn"),
                ("LaptopObjectiveActive", "LaptopObjectiveActive"),
                ("LaptopResolved", "LaptopResolved"),
                ("DoorReady", "DoorReady"),
                ("TransitionCommitted", "TransitionCommitted"));

            var laptopSignal = CreateSignal(BedroomStorySignals.LaptopChecked);
            var doorSignal = CreateSignal(BedroomStorySignals.DoorConfirmed);
            var debugSignal = CreateSignal(BedroomStorySignals.DebugForceAdvance);
            var timelineCompletedSignal = CreateSignal(BedroomStorySignals.TimelineCompleted);
            var timelineCancelledSignal = CreateSignal(BedroomStorySignals.TimelineCancelled);

            var timelineCue = CreateTimelineCue("BedroomMorningCue");
            var timelinePlayable = CreateOrLoadAsset<BedroomStoryPulsePlayableAsset>($"{timelinesFolder}/BedroomStoryPulsePlayable.asset");

            var laptopCheckedCondition = CreateVariableCondition(laptopChecked, true);
            var canonicalPathCondition = CreateVariableCondition(canonicalPath, true);
            var doorReadyCondition = CreateStateCondition(progressionState, "DoorReady");

            var markLaptopAction = CreateSetVariableAction(laptopChecked, true);
            var markReadyAction = CreateSetVariableAction(introReady, true);
            var canonicalPathAction = CreateSetVariableAction(canonicalPath, true);
            var setLaptopObjectiveState = CreateSetStateAction(
                progressionState,
                "LaptopObjectiveActive",
                "SetLaptopObjectiveActiveState");
            var setLaptopResolvedState = CreateSetStateAction(progressionState, "LaptopResolved");
            var setDoorReadyState = CreateSetStateAction(progressionState, "DoorReady");
            var setTransitionState = CreateSetStateAction(
                progressionState,
                "TransitionCommitted",
                "SetTransitionCommittedState");
            var raiseTimelineCompleteAction = CreateRaiseSignalAction(timelineCompletedSignal, "timeline-complete");

            return new BedroomStoryAssetBundle(
                introReady,
                laptopChecked,
                canonicalPath,
                progressionState,
                laptopSignal,
                doorSignal,
                debugSignal,
                timelineCompletedSignal,
                timelineCancelledSignal,
                timelineCue,
                timelinePlayable,
                laptopCheckedCondition,
                canonicalPathCondition,
                doorReadyCondition,
                setLaptopObjectiveState,
                markLaptopAction,
                canonicalPathAction,
                setLaptopResolvedState,
                markReadyAction,
                setDoorReadyState,
                raiseTimelineCompleteAction,
                setTransitionState);
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

        private StoryTimelineCue CreateTimelineCue(string cueId)
        {
            var cue = CreateOrLoadAsset<StoryTimelineCue>($"{timelinesFolder}/{cueId}.asset");
            var serialized = new SerializedObject(cue);
            serialized.FindProperty("cueId").stringValue = cueId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            cue.name = cueId;
            EditorUtility.SetDirty(cue);
            return cue;
        }

        private StoryVariableConditionAsset CreateVariableCondition(StoryVariableDefinition variable, bool expected)
        {
            var asset = CreateOrLoadAsset<StoryVariableConditionAsset>($"{conditionsFolder}/{SanitizeAssetName(variable.Key)}Condition.asset");
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("variable").objectReferenceValue = variable;
            serialized.FindProperty("comparisonOperator").enumValueIndex = (int)StoryComparisonOperator.Equals;
            serialized.FindProperty("booleanValue").boolValue = expected;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private StoryStateEqualsConditionAsset CreateStateCondition(StoryStateMachineDefinition stateMachine, string expectedState)
        {
            var asset = CreateOrLoadAsset<StoryStateEqualsConditionAsset>($"{conditionsFolder}/{SanitizeAssetName(expectedState)}StateCondition.asset");
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("stateMachine").objectReferenceValue = stateMachine;
            serialized.FindProperty("expectedStateId").stringValue = expectedState;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private StorySetVariableActionAsset CreateSetVariableAction(StoryVariableDefinition variable, bool value)
        {
            var asset = CreateOrLoadAsset<StorySetVariableActionAsset>($"{actionsFolder}/{SanitizeAssetName(variable.Key)}Set.asset");
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("variable").objectReferenceValue = variable;
            serialized.FindProperty("booleanValue").boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private StorySetStateActionAsset CreateSetStateAction(
            StoryStateMachineDefinition stateMachine,
            string stateId,
            string explicitAssetName = null)
        {
            var assetBaseName = string.IsNullOrWhiteSpace(explicitAssetName)
                ? $"{SanitizeAssetName(stateId)}StateAction"
                : SanitizeAssetName(explicitAssetName);
            var asset = CreateOrLoadAsset<StorySetStateActionAsset>($"{actionsFolder}/{assetBaseName}.asset");
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

        private void ConfigureVariable(StoryBooleanVariableDefinition variable, string key, bool defaultValue)
        {
            var serialized = new SerializedObject(variable);
            serialized.FindProperty("key").stringValue = key;
            serialized.FindProperty("defaultValue").boolValue = defaultValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            variable.name = key;
            EditorUtility.SetDirty(variable);
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

    internal readonly struct BedroomStoryAssetBundle
    {
        public BedroomStoryAssetBundle(
            StoryBooleanVariableDefinition introReady,
            StoryBooleanVariableDefinition laptopChecked,
            StoryBooleanVariableDefinition canonicalPath,
            StoryStateMachineDefinition progressionState,
            StorySignalDefinition laptopSignal,
            StorySignalDefinition doorSignal,
            StorySignalDefinition debugSignal,
            StorySignalDefinition timelineCompletedSignal,
            StorySignalDefinition timelineCancelledSignal,
            StoryTimelineCue timelineCue,
            BedroomStoryPulsePlayableAsset timelinePlayable,
            StoryVariableConditionAsset laptopCheckedCondition,
            StoryVariableConditionAsset canonicalPathCondition,
            StoryStateEqualsConditionAsset doorReadyCondition,
            StorySetStateActionAsset laptopObjectiveStateAction,
            StorySetVariableActionAsset laptopCheckedAction,
            StorySetVariableActionAsset canonicalPathAction,
            StorySetStateActionAsset laptopResolvedStateAction,
            StorySetVariableActionAsset introReadyAction,
            StorySetStateActionAsset doorReadyStateAction,
            StoryRaiseSignalActionAsset timelineCompletedRaiseAction,
            StorySetStateActionAsset transitionStateAction)
        {
            IntroReady = introReady;
            LaptopChecked = laptopChecked;
            CanonicalPath = canonicalPath;
            ProgressionState = progressionState;
            LaptopSignal = laptopSignal;
            DoorSignal = doorSignal;
            DebugSignal = debugSignal;
            TimelineCompletedSignal = timelineCompletedSignal;
            TimelineCancelledSignal = timelineCancelledSignal;
            TimelineCue = timelineCue;
            TimelinePlayable = timelinePlayable;
            LaptopCheckedCondition = laptopCheckedCondition;
            CanonicalPathCondition = canonicalPathCondition;
            DoorReadyCondition = doorReadyCondition;
            LaptopObjectiveStateAction = laptopObjectiveStateAction;
            LaptopCheckedAction = laptopCheckedAction;
            CanonicalPathAction = canonicalPathAction;
            LaptopResolvedStateAction = laptopResolvedStateAction;
            IntroReadyAction = introReadyAction;
            DoorReadyStateAction = doorReadyStateAction;
            TimelineCompletedRaiseAction = timelineCompletedRaiseAction;
            TransitionStateAction = transitionStateAction;
        }

        public StoryBooleanVariableDefinition IntroReady { get; }
        public StoryBooleanVariableDefinition LaptopChecked { get; }
        public StoryBooleanVariableDefinition CanonicalPath { get; }
        public StoryStateMachineDefinition ProgressionState { get; }
        public StorySignalDefinition LaptopSignal { get; }
        public StorySignalDefinition DoorSignal { get; }
        public StorySignalDefinition DebugSignal { get; }
        public StorySignalDefinition TimelineCompletedSignal { get; }
        public StorySignalDefinition TimelineCancelledSignal { get; }
        public StoryTimelineCue TimelineCue { get; }
        public BedroomStoryPulsePlayableAsset TimelinePlayable { get; }
        public StoryVariableConditionAsset LaptopCheckedCondition { get; }
        public StoryVariableConditionAsset CanonicalPathCondition { get; }
        public StoryStateEqualsConditionAsset DoorReadyCondition { get; }
        public StorySetStateActionAsset LaptopObjectiveStateAction { get; }
        public StorySetVariableActionAsset LaptopCheckedAction { get; }
        public StorySetVariableActionAsset CanonicalPathAction { get; }
        public StorySetStateActionAsset LaptopResolvedStateAction { get; }
        public StorySetVariableActionAsset IntroReadyAction { get; }
        public StorySetStateActionAsset DoorReadyStateAction { get; }
        public StoryRaiseSignalActionAsset TimelineCompletedRaiseAction { get; }
        public StorySetStateActionAsset TransitionStateAction { get; }
    }
}
