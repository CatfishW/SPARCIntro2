using System.Collections.Generic;
using System.Linq;
using ModularStoryFlow.Editor.Graph;
using ModularStoryFlow.Editor.Setup;
using ModularStoryFlow.Samples.QuickStart;
using ModularStoryFlow.Runtime.Actions;
using ModularStoryFlow.Runtime.Conditions;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.Player;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace ModularStoryFlow.Samples.QuickStart.Editor
{
    public static class QuickStartSampleInstaller
    {
        private const string RootFolder = "Assets/StoryFlowQuickStartGenerated";

        [MenuItem("Tools/Modular Story Flow/Samples/Install Quick Start", priority = 200)]
        public static void InstallQuickStart()
        {
            var generated = StoryFlowProjectGenerator.Generate(RootFolder, false, false, false);

            var variablesFolder = EnsureFolder($"{RootFolder}/SampleData", "Variables");
            var statesFolder = EnsureFolder($"{RootFolder}/SampleData", "StateMachines");
            var signalsFolder = EnsureFolder($"{RootFolder}/SampleData", "Signals");
            var timelinesFolder = EnsureFolder($"{RootFolder}/SampleData", "Timelines");
            var conditionsFolder = EnsureFolder($"{RootFolder}/SampleData", "Conditions");
            var actionsFolder = EnsureFolder($"{RootFolder}/SampleData", "Actions");
            var graphsFolder = EnsureFolder($"{RootFolder}/SampleData", "Graphs");

            var hasAccessCode = CreateOrLoadAsset<StoryBooleanVariableDefinition>($"{variablesFolder}/HasAccessCode.asset");
            ConfigureBooleanVariable(hasAccessCode, "HasAccessCode", true);

            var vaultDoorState = CreateOrLoadAsset<StoryStateMachineDefinition>($"{statesFolder}/VaultDoorState.asset");
            ConfigureStateMachine(vaultDoorState, "VaultDoorState", "Locked", ("Locked", "Locked"), ("Open", "Open"));

            var vaultOpenedSignal = CreateOrLoadAsset<StorySignalDefinition>($"{signalsFolder}/VaultOpenedSignal.asset");
            ConfigureNamedAsset(vaultOpenedSignal, "VaultOpened", "signalId");

            var vaultOpenCue = CreateOrLoadAsset<StoryTimelineCue>($"{timelinesFolder}/VaultOpenCue.asset");
            ConfigureNamedAsset(vaultOpenCue, "VaultOpenCue", "cueId");

            var pulsePlayable = CreateOrLoadAsset<QuickStartPulsePlayableAsset>($"{timelinesFolder}/QuickStartPulsePlayable.asset");
            ConfigurePulsePlayable(pulsePlayable, 1.25d, "Quick Start timeline completed.");

            var hasAccessCodeCondition = CreateOrLoadAsset<StoryVariableConditionAsset>($"{conditionsFolder}/HasAccessCodeCondition.asset");
            ConfigureVariableCondition(hasAccessCodeCondition, hasAccessCode, true);

            var vaultOpenCondition = CreateOrLoadAsset<StoryStateEqualsConditionAsset>($"{conditionsFolder}/VaultOpenCondition.asset");
            ConfigureStateCondition(vaultOpenCondition, vaultDoorState, "Open");

            var openVaultStateAction = CreateOrLoadAsset<StorySetStateActionAsset>($"{actionsFolder}/OpenVaultStateAction.asset");
            ConfigureStateAction(openVaultStateAction, vaultDoorState, "Open");

            var raiseSignalAction = CreateOrLoadAsset<StoryRaiseSignalActionAsset>($"{actionsFolder}/RaiseVaultOpenedSignal.asset");
            ConfigureRaiseSignalAction(raiseSignalAction, vaultOpenedSignal, "Vault door opening.");

            var compositeAction = CreateOrLoadAsset<StoryCompositeActionAsset>($"{actionsFolder}/OpenVaultCompositeAction.asset");
            ConfigureCompositeAction(compositeAction, openVaultStateAction, raiseSignalAction);

            generated.TimelineCatalog.AddOrReplaceBinding(vaultOpenCue.CueId, vaultOpenCue.name, pulsePlayable);
            EditorUtility.SetDirty(generated.TimelineCatalog);

            var graphPath = $"{graphsFolder}/QuickStartStory.asset";
            if (AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(graphPath) != null)
            {
                AssetDatabase.DeleteAsset(graphPath);
            }

            var graph = StoryGraphEditorUtility.CreateGraphAsset(graphPath);
            var start = graph.GetEntryNode() as StartNodeAsset;
            var intro = StoryGraphEditorUtility.AddNode<DialogueNodeAsset>(graph, new Vector2(360f, 140f));
            var choice = StoryGraphEditorUtility.AddNode<ChoiceNodeAsset>(graph, new Vector2(760f, 140f));
            var action = StoryGraphEditorUtility.AddNode<ActionNodeAsset>(graph, new Vector2(1160f, 40f));
            var timeline = StoryGraphEditorUtility.AddNode<TimelineNodeAsset>(graph, new Vector2(1520f, 40f));
            var branch = StoryGraphEditorUtility.AddNode<BranchNodeAsset>(graph, new Vector2(1880f, 40f));
            var success = StoryGraphEditorUtility.AddNode<DialogueNodeAsset>(graph, new Vector2(2240f, -120f));
            var failed = StoryGraphEditorUtility.AddNode<DialogueNodeAsset>(graph, new Vector2(2240f, 80f));
            var leave = StoryGraphEditorUtility.AddNode<DialogueNodeAsset>(graph, new Vector2(1160f, 250f));
            var end = StoryGraphEditorUtility.AddNode<EndNodeAsset>(graph, new Vector2(2600f, 20f));

            ConfigureDialogue(intro, "System", "The sealed vault awaits your command.");
            ConfigureChoice(choice, hasAccessCodeCondition);
            ConfigureActionNode(action, compositeAction);
            ConfigureTimelineNode(timeline, vaultOpenCue, true);
            ConfigureBranchNode(branch, vaultOpenCondition);
            ConfigureDialogue(success, "Vault", "Access granted. The door slides open.");
            ConfigureDialogue(failed, "Vault", "The sequence failed and the vault remains sealed.");
            ConfigureDialogue(leave, "Narrator", "You decide to leave the mystery untouched for now.");

            choice.EnsureStableIds();
            branch.EnsureStableIds();
            EditorUtility.SetDirty(choice);
            EditorUtility.SetDirty(branch);

            var choicePorts = choice.GetPorts().Where(port => port.Direction == StoryPortDirection.Output).ToList();
            var branchPorts = branch.GetPorts().Where(port => port.Direction == StoryPortDirection.Output).ToList();

            StoryGraphEditorUtility.Connect(graph, start, StoryNodeAsset.DefaultOutputPortId, intro);
            StoryGraphEditorUtility.Connect(graph, intro, StoryNodeAsset.DefaultOutputPortId, choice);
            StoryGraphEditorUtility.Connect(graph, choice, choicePorts[0].Id, action);
            StoryGraphEditorUtility.Connect(graph, choice, choicePorts[1].Id, leave);
            StoryGraphEditorUtility.Connect(graph, action, StoryNodeAsset.DefaultOutputPortId, timeline);
            StoryGraphEditorUtility.Connect(graph, timeline, TimelineNodeAsset.CompletedPortId, branch);
            StoryGraphEditorUtility.Connect(graph, timeline, TimelineNodeAsset.CancelledPortId, failed);
            StoryGraphEditorUtility.Connect(graph, branch, branchPorts[0].Id, success);
            StoryGraphEditorUtility.Connect(graph, branch, BranchNodeAsset.ElsePortId, failed);
            StoryGraphEditorUtility.Connect(graph, success, StoryNodeAsset.DefaultOutputPortId, end);
            StoryGraphEditorUtility.Connect(graph, failed, StoryNodeAsset.DefaultOutputPortId, end);
            StoryGraphEditorUtility.Connect(graph, leave, StoryNodeAsset.DefaultOutputPortId, end);

            StoryGraphEditorUtility.SaveGraph(graph);

            RefreshProjectCatalogs(generated);

            var player = Object.FindFirstObjectByType<StoryFlowPlayer>();
            if (player == null)
            {
                player = new GameObject("StoryFlow Quick Start").AddComponent<StoryFlowPlayer>();
            }

            player.ProjectConfig = generated.Config;
            player.InitialGraph = graph;
            player.PlayOnStart = true;

            var overlay = player.GetComponent<QuickStartOverlay>() ?? player.gameObject.AddComponent<QuickStartOverlay>();
            overlay.ProjectConfig = generated.Config;

            var bridgeObject = Object.FindFirstObjectByType<ModularStoryFlow.Runtime.Bridges.StoryTimelineDirectorBridge>();
            if (bridgeObject == null)
            {
                var go = new GameObject("Quick Start Timeline Bridge");
                go.AddComponent<PlayableDirector>();
                bridgeObject = go.AddComponent<ModularStoryFlow.Runtime.Bridges.StoryTimelineDirectorBridge>();
            }

            var bridgeSerialized = new SerializedObject(bridgeObject);
            bridgeSerialized.FindProperty("projectConfig").objectReferenceValue = generated.Config;
            bridgeSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeObject = player.gameObject;
            EditorGUIUtility.PingObject(player.gameObject);

            EditorUtility.DisplayDialog(
                "Quick Start Installed",
                $"Sample content was generated under '{RootFolder}'.\nPress Play Mode to run the sample overlay.",
                "OK");
        }

        private static void RefreshProjectCatalogs(StoryFlowGeneratedProject generated)
        {
            generated.GraphRegistry.SetGraphs(FindAssetsByType<StoryGraphAsset>());
            generated.VariableCatalog.SetVariables(FindVariableAssets());
            generated.StateMachineCatalog.SetStateMachines(FindAssetsByType<StoryStateMachineDefinition>());

            foreach (var cue in FindAssetsByType<StoryTimelineCue>())
            {
                generated.TimelineCatalog.AddOrReplaceBinding(cue.CueId, cue.name, generated.TimelineCatalog.ResolvePlayableAsset(cue.CueId));
            }

            EditorUtility.SetDirty(generated.GraphRegistry);
            EditorUtility.SetDirty(generated.VariableCatalog);
            EditorUtility.SetDirty(generated.StateMachineCatalog);
            EditorUtility.SetDirty(generated.TimelineCatalog);
            EditorUtility.SetDirty(generated.Config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void ConfigureBooleanVariable(StoryBooleanVariableDefinition asset, string key, bool defaultValue)
        {
            asset.name = key;
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("key").stringValue = key;
            serialized.FindProperty("defaultValue").boolValue = defaultValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureStateMachine(StoryStateMachineDefinition asset, string machineId, string defaultStateId, params (string id, string displayName)[] states)
        {
            asset.name = machineId;
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("machineId").stringValue = machineId;
            serialized.FindProperty("defaultStateId").stringValue = defaultStateId;

            var statesProperty = serialized.FindProperty("states");
            statesProperty.arraySize = states.Length;
            for (var i = 0; i < states.Length; i++)
            {
                var element = statesProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Id").stringValue = states[i].id;
                element.FindPropertyRelative("DisplayName").stringValue = states[i].displayName;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureNamedAsset(ScriptableObject asset, string idValue, string propertyName)
        {
            asset.name = idValue;
            var serialized = new SerializedObject(asset);
            serialized.FindProperty(propertyName).stringValue = idValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigurePulsePlayable(QuickStartPulsePlayableAsset asset, double durationSeconds, string message)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("durationSeconds").doubleValue = durationSeconds;
            serialized.FindProperty("logMessage").stringValue = message;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureVariableCondition(StoryVariableConditionAsset asset, StoryVariableDefinition variable, bool booleanValue)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("variable").objectReferenceValue = variable;
            serialized.FindProperty("comparisonOperator").enumValueIndex = 0;
            serialized.FindProperty("booleanValue").boolValue = booleanValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureStateCondition(StoryStateEqualsConditionAsset asset, StoryStateMachineDefinition stateMachine, string expectedStateId)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("stateMachine").objectReferenceValue = stateMachine;
            serialized.FindProperty("expectedStateId").stringValue = expectedStateId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureStateAction(StorySetStateActionAsset asset, StoryStateMachineDefinition stateMachine, string stateId)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("stateMachine").objectReferenceValue = stateMachine;
            serialized.FindProperty("stateId").stringValue = stateId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureRaiseSignalAction(StoryRaiseSignalActionAsset asset, StorySignalDefinition signal, string payload)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("signal").objectReferenceValue = signal;
            serialized.FindProperty("payload").stringValue = payload;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureCompositeAction(StoryCompositeActionAsset asset, params StoryActionAsset[] actions)
        {
            var serialized = new SerializedObject(asset);
            var actionsProperty = serialized.FindProperty("actions");
            actionsProperty.arraySize = actions.Length;
            for (var i = 0; i < actions.Length; i++)
            {
                actionsProperty.GetArrayElementAtIndex(i).objectReferenceValue = actions[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureDialogue(DialogueNodeAsset node, string speaker, string body)
        {
            var serialized = new SerializedObject(node);
            serialized.FindProperty("speakerDisplayName").stringValue = speaker;
            serialized.FindProperty("body").stringValue = body;
            serialized.FindProperty("autoAdvance").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(node);
        }

        private static void ConfigureChoice(ChoiceNodeAsset node, StoryVariableConditionAsset hasAccessCodeCondition)
        {
            var serialized = new SerializedObject(node);
            serialized.FindProperty("prompt").stringValue = "How do you handle the vault?";

            var options = serialized.FindProperty("options");
            options.arraySize = 2;

            var useCode = options.GetArrayElementAtIndex(0);
            useCode.FindPropertyRelative("Label").stringValue = "Use access code";
            useCode.FindPropertyRelative("AvailabilityCondition").objectReferenceValue = hasAccessCodeCondition;
            useCode.FindPropertyRelative("HideWhenUnavailable").boolValue = false;

            var leave = options.GetArrayElementAtIndex(1);
            leave.FindPropertyRelative("Label").stringValue = "Walk away";
            leave.FindPropertyRelative("AvailabilityCondition").objectReferenceValue = null;
            leave.FindPropertyRelative("HideWhenUnavailable").boolValue = false;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            node.EnsureStableIds();
            EditorUtility.SetDirty(node);
        }

        private static void ConfigureActionNode(ActionNodeAsset node, StoryActionAsset action)
        {
            var serialized = new SerializedObject(node);
            var actions = serialized.FindProperty("actions");
            actions.arraySize = 1;
            actions.GetArrayElementAtIndex(0).objectReferenceValue = action;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(node);
        }

        private static void ConfigureTimelineNode(TimelineNodeAsset node, StoryTimelineCue cue, bool waitForCompletion)
        {
            var serialized = new SerializedObject(node);
            serialized.FindProperty("cue").objectReferenceValue = cue;
            serialized.FindProperty("waitForCompletion").boolValue = waitForCompletion;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(node);
        }

        private static void ConfigureBranchNode(BranchNodeAsset node, StoryConditionAsset stateCondition)
        {
            var serialized = new SerializedObject(node);
            serialized.FindProperty("elseLabel").stringValue = "Else";

            var branches = serialized.FindProperty("branches");
            branches.arraySize = 1;
            var branch = branches.GetArrayElementAtIndex(0);
            branch.FindPropertyRelative("Label").stringValue = "Door Open";
            branch.FindPropertyRelative("Condition").objectReferenceValue = stateCondition;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            node.EnsureStableIds();
            EditorUtility.SetDirty(node);
        }

        private static T CreateOrLoadAsset<T>(string assetPath) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static string EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent))
            {
                var parts = parent.Split('/');
                var current = "Assets";
                for (var i = 1; i < parts.Length; i++)
                {
                    var next = $"{current}/{parts[i]}";
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[i]);
                    }

                    current = next;
                }
            }

            var combined = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(combined))
            {
                AssetDatabase.CreateFolder(parent, child);
            }

            return combined;
        }


        private static List<StoryVariableDefinition> FindVariableAssets()
        {
            var results = new List<StoryVariableDefinition>();
            var seenPaths = new HashSet<string>();

            var typeNames = TypeCache.GetTypesDerivedFrom<StoryVariableDefinition>()
                .Where(type => !type.IsAbstract)
                .Select(type => type.Name)
                .ToList();

            foreach (var typeName in typeNames.Distinct())
            {
                foreach (var guid in AssetDatabase.FindAssets($"t:{typeName}"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!seenPaths.Add(path))
                    {
                        continue;
                    }

                    var asset = AssetDatabase.LoadAssetAtPath<StoryVariableDefinition>(path);
                    if (asset != null)
                    {
                        results.Add(asset);
                    }
                }
            }

            return results;
        }

        private static List<T> FindAssetsByType<T>() where T : Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath<T>(path))
                .Where(asset => asset != null)
                .Distinct()
                .ToList();
        }
    }
}
