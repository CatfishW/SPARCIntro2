using System;
using System.Collections.Generic;
using System.Linq;
using ModularStoryFlow.Runtime.Bridges;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Graph;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.Player;
using ModularStoryFlow.Runtime.Save;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace ModularStoryFlow.Editor.Setup
{
    public readonly struct StoryFlowGeneratedProject
    {
        public StoryFlowGeneratedProject(
            StoryFlowProjectConfig config,
            StoryGraphRegistry graphRegistry,
            StoryVariableCatalog variableCatalog,
            StoryStateMachineCatalog stateMachineCatalog,
            StoryTimelineCatalog timelineCatalog,
            StoryFlowChannels channels,
            StorySaveProviderAsset saveProvider)
        {
            Config = config;
            GraphRegistry = graphRegistry;
            VariableCatalog = variableCatalog;
            StateMachineCatalog = stateMachineCatalog;
            TimelineCatalog = timelineCatalog;
            Channels = channels;
            SaveProvider = saveProvider;
        }

        public StoryFlowProjectConfig Config { get; }
        public StoryGraphRegistry GraphRegistry { get; }
        public StoryVariableCatalog VariableCatalog { get; }
        public StoryStateMachineCatalog StateMachineCatalog { get; }
        public StoryTimelineCatalog TimelineCatalog { get; }
        public StoryFlowChannels Channels { get; }
        public StorySaveProviderAsset SaveProvider { get; }
    }

    public static class StoryFlowProjectGenerator
    {
        public static StoryFlowGeneratedProject Generate(string rootFolder, bool createPlayerPrefab, bool createTimelineBridgePrefab, bool scanProjectAssets)
        {
            rootFolder = string.IsNullOrWhiteSpace(rootFolder) ? "Assets/StoryFlow" : rootFolder.Replace("\\", "/");

            EnsureFolderHierarchy(rootFolder);
            var channelsFolder = EnsureFolder(rootFolder, "Channels");
            var configFolder = EnsureFolder(rootFolder, "Config");
            var dataFolder = EnsureFolder(rootFolder, "Data");
            var prefabsFolder = EnsureFolder(rootFolder, "Prefabs");

            var dialogueRequests = CreateOrLoadAsset<StoryDialogueRequestChannel>($"{channelsFolder}/DialogueRequests.asset");
            var advanceCommands = CreateOrLoadAsset<StoryAdvanceCommandChannel>($"{channelsFolder}/AdvanceCommands.asset");
            var choiceRequests = CreateOrLoadAsset<StoryChoiceRequestChannel>($"{channelsFolder}/ChoiceRequests.asset");
            var choiceSelections = CreateOrLoadAsset<StoryChoiceSelectionChannel>($"{channelsFolder}/ChoiceSelections.asset");
            var timelineRequests = CreateOrLoadAsset<StoryTimelineRequestChannel>($"{channelsFolder}/TimelineRequests.asset");
            var timelineResults = CreateOrLoadAsset<StoryTimelineResultChannel>($"{channelsFolder}/TimelineResults.asset");
            var raisedSignals = CreateOrLoadAsset<StorySignalRaisedChannel>($"{channelsFolder}/RaisedSignals.asset");
            var externalSignals = CreateOrLoadAsset<StoryExternalSignalChannel>($"{channelsFolder}/ExternalSignals.asset");
            var stateChanged = CreateOrLoadAsset<StoryStateChangedChannel>($"{channelsFolder}/StateChanged.asset");
            var nodeNotifications = CreateOrLoadAsset<StoryNodeNotificationChannel>($"{channelsFolder}/NodeNotifications.asset");
            var graphNotifications = CreateOrLoadAsset<StoryGraphNotificationChannel>($"{channelsFolder}/GraphNotifications.asset");
            var channels = CreateOrLoadAsset<StoryFlowChannels>($"{configFolder}/StoryFlowChannels.asset");

            channels.Configure(
                dialogueRequests,
                advanceCommands,
                choiceRequests,
                choiceSelections,
                timelineRequests,
                timelineResults,
                raisedSignals,
                externalSignals,
                stateChanged,
                nodeNotifications,
                graphNotifications);
            EditorUtility.SetDirty(channels);

            var graphRegistry = CreateOrLoadAsset<StoryGraphRegistry>($"{dataFolder}/StoryGraphRegistry.asset");
            var variableCatalog = CreateOrLoadAsset<StoryVariableCatalog>($"{dataFolder}/StoryVariableCatalog.asset");
            var stateMachineCatalog = CreateOrLoadAsset<StoryStateMachineCatalog>($"{dataFolder}/StoryStateMachineCatalog.asset");
            var timelineCatalog = CreateOrLoadAsset<StoryTimelineCatalog>($"{dataFolder}/StoryTimelineCatalog.asset");
            var saveProvider = CreateOrLoadAsset<JsonFileStorySaveProviderAsset>($"{configFolder}/StoryFlowSaveProvider.asset");
            var projectConfig = CreateOrLoadAsset<StoryFlowProjectConfig>($"{configFolder}/StoryFlowProjectConfig.asset");

            if (scanProjectAssets)
            {
                graphRegistry.SetGraphs(FindAssetsByType<StoryGraphAsset>());
                variableCatalog.SetVariables(FindAssetsByBaseType<StoryVariableDefinition>());
                stateMachineCatalog.SetStateMachines(FindAssetsByType<StoryStateMachineDefinition>());

                foreach (var cue in FindAssetsByType<StoryTimelineCue>())
                {
                    timelineCatalog.AddOrReplaceBinding(cue.CueId, cue.name, timelineCatalog.ResolvePlayableAsset(cue.CueId));
                }

                EditorUtility.SetDirty(graphRegistry);
                EditorUtility.SetDirty(variableCatalog);
                EditorUtility.SetDirty(stateMachineCatalog);
                EditorUtility.SetDirty(timelineCatalog);
            }

            projectConfig.Configure(graphRegistry, variableCatalog, stateMachineCatalog, timelineCatalog, channels, saveProvider);
            EditorUtility.SetDirty(projectConfig);

            if (createPlayerPrefab)
            {
                CreateOrReplacePlayerPrefab($"{prefabsFolder}/StoryFlowPlayer.prefab", projectConfig, graphRegistry.Graphs.Count == 1 ? graphRegistry.Graphs[0] : null);
            }

            if (createTimelineBridgePrefab)
            {
                CreateOrReplaceTimelineBridgePrefab($"{prefabsFolder}/StoryTimelineBridge.prefab", projectConfig);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new StoryFlowGeneratedProject(
                projectConfig,
                graphRegistry,
                variableCatalog,
                stateMachineCatalog,
                timelineCatalog,
                channels,
                saveProvider);
        }

        private static void CreateOrReplacePlayerPrefab(string prefabPath, StoryFlowProjectConfig config, StoryGraphAsset initialGraph)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                AssetDatabase.DeleteAsset(prefabPath);
            }

            var root = new GameObject("StoryFlowPlayer");
            var player = root.AddComponent<StoryFlowPlayer>();
            player.ProjectConfig = config;
            player.InitialGraph = initialGraph;
            player.PlayOnStart = initialGraph != null;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void CreateOrReplaceTimelineBridgePrefab(string prefabPath, StoryFlowProjectConfig config)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                AssetDatabase.DeleteAsset(prefabPath);
            }

            var root = new GameObject("StoryTimelineBridge");
            root.AddComponent<PlayableDirector>();
            var bridge = root.AddComponent<StoryTimelineDirectorBridge>();

            var serializedObject = new SerializedObject(bridge);
            serializedObject.FindProperty("projectConfig").objectReferenceValue = config;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void EnsureFolderHierarchy(string rootFolder)
        {
            if (!rootFolder.StartsWith("Assets"))
            {
                throw new ArgumentException("The setup root folder must live under Assets/.");
            }

            var parts = rootFolder.Split('/');
            var current = "Assets";
            for (var i = 1; i < parts.Length; i++)
            {
                current = EnsureFolder(current, parts[i]);
            }
        }

        private static string EnsureFolder(string parent, string child)
        {
            var combined = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(combined))
            {
                AssetDatabase.CreateFolder(parent, child);
            }

            return combined;
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

        private static List<T> FindAssetsByType<T>() where T : UnityEngine.Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath<T>(path))
                .Where(asset => asset != null)
                .Distinct()
                .OrderBy(asset => asset.name)
                .ToList();
        }

        private static List<TBase> FindAssetsByBaseType<TBase>() where TBase : UnityEngine.Object
        {
            var results = new List<TBase>();
            var added = new HashSet<string>();

            var allTypes = TypeCache.GetTypesDerivedFrom<TBase>()
                .Where(type => !type.IsAbstract)
                .Select(type => type.Name)
                .ToList();

            if (!typeof(TBase).IsAbstract)
            {
                allTypes.Add(typeof(TBase).Name);
            }

            foreach (var typeName in allTypes.Distinct())
            {
                foreach (var guid in AssetDatabase.FindAssets($"t:{typeName}"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!added.Add(path))
                    {
                        continue;
                    }

                    var asset = AssetDatabase.LoadAssetAtPath<TBase>(path);
                    if (asset != null)
                    {
                        results.Add(asset);
                    }
                }
            }

            return results.OrderBy(asset => asset.name).ToList();
        }
    }
}
