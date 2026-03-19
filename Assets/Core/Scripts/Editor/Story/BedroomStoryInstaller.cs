using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blocks.Gameplay.Core;
using Blocks.Gameplay.Core.Customization;
using Blocks.Gameplay.Core.Story;
using ModularStoryFlow.Editor.Setup;
using ModularStoryFlow.Runtime.Bridges;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.Player;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;
using ItemInteraction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Story.Editor
{
    public static class BedroomStoryInstaller
    {
        private const string GeneratedRoot = "Assets/StoryFlowBedroomGenerated";
        private const string ConfigPath = GeneratedRoot + "/Config/StoryFlowProjectConfig.asset";
        private const string ChannelsPath = GeneratedRoot + "/Config/StoryFlowChannels.asset";
        private const string GraphRegistryPath = GeneratedRoot + "/Data/StoryGraphRegistry.asset";
        private const string VariableCatalogPath = GeneratedRoot + "/Data/StoryVariableCatalog.asset";
        private const string StateMachineCatalogPath = GeneratedRoot + "/Data/StoryStateMachineCatalog.asset";
        private const string TimelineCatalogPath = GeneratedRoot + "/Data/StoryTimelineCatalog.asset";
        private const string GraphPath = GeneratedRoot + "/Graphs/BedroomIntroStory.asset";
        private const string TimelineCuePath = GeneratedRoot + "/Data/Timelines/BedroomMorningCue.asset";
        private const string TimelinePlayablePath = GeneratedRoot + "/Data/Timelines/BedroomStoryPulsePlayable.asset";
        private const string BedroomScenePath = "Assets/Core/TestScenes/BedRoomIntroScene.unity";
        private const string ClassroomScenePath = "Assets/Core/TestScenes/ClassroomArrivalScene.unity";
        private const string BlocksPanelSettingsPath = "Assets/Blocks/Common/BlocksPanelSettings.asset";
        private const string CharacterCustomizationCatalogAssetPath = "Assets/Core/Resources/CharacterCustomizationCatalog.asset";
        private const string WardrobeObjectName = "BedroomWardrobes";
        private const string CustomizationPanelObjectName = "CharacterCustomizationPanel";
        private const string PrimarySpawnPadPath = "_Spawns/Pfb_SpawnPad";
        private const string SecondarySpawnPadPath = "_Spawns/Pfb_SpawnPad (1)";
        private const float WakeUpSpawnClearance = 0.95f;
        private const float WakeUpSpawnSpacing = 0.45f;
        private const float WakeUpVerticalOffset = 0.08f;

        [MenuItem("Tools/Blocks/Story/Install Bedroom Story Slice", priority = 300)]
        public static void Install()
        {
            var originalScene = SceneManager.GetActiveScene().path;
            EditorSceneManager.SaveOpenScenes();
            if (AssetDatabase.IsValidFolder(GeneratedRoot))
            {
                AssetDatabase.DeleteAsset(GeneratedRoot);
                AssetDatabase.Refresh();
            }

            var generatedProject = StoryFlowProjectGenerator.Generate(GeneratedRoot, false, false, false);
            EnsureFolder(GeneratedRoot, "Graphs");
            var config = ResolveGeneratedAsset(ConfigPath, generatedProject.Config);
            var channels = ResolveGeneratedAsset(ChannelsPath, generatedProject.Channels);
            var graphRegistry = ResolveGeneratedAsset(GraphRegistryPath, generatedProject.GraphRegistry);
            var variableCatalog = ResolveGeneratedAsset(VariableCatalogPath, generatedProject.VariableCatalog);
            var stateMachineCatalog = ResolveGeneratedAsset(StateMachineCatalogPath, generatedProject.StateMachineCatalog);
            var timelineCatalog = ResolveGeneratedAsset(TimelineCatalogPath, generatedProject.TimelineCatalog);
            if (config == null || channels == null || graphRegistry == null || variableCatalog == null || stateMachineCatalog == null || timelineCatalog == null)
            {
                throw new System.InvalidOperationException("Bedroom story generated assets failed to load after generation.");
            }

            var builder = new BedroomStoryAssetBuilder(GeneratedRoot + "/Data");
            var bundle = builder.Build();
            config = ResolveGeneratedAsset(ConfigPath, config);
            channels = ResolveGeneratedAsset(ChannelsPath, channels);
            graphRegistry = ResolveGeneratedAsset(GraphRegistryPath, graphRegistry);
            variableCatalog = ResolveGeneratedAsset(VariableCatalogPath, variableCatalog);
            stateMachineCatalog = ResolveGeneratedAsset(StateMachineCatalogPath, stateMachineCatalog);
            timelineCatalog = ResolveGeneratedAsset(TimelineCatalogPath, timelineCatalog);
            if (config == null || channels == null || graphRegistry == null || variableCatalog == null || stateMachineCatalog == null || timelineCatalog == null)
            {
                throw new System.InvalidOperationException("Bedroom story generated assets became unavailable during install.");
            }

            var timelineCue = AssetDatabase.LoadAssetAtPath<StoryTimelineCue>(TimelineCuePath);
            var timelinePlayable = AssetDatabase.LoadAssetAtPath<PlayableAsset>(TimelinePlayablePath);

            timelineCatalog.AddOrReplaceBinding(
                timelineCue != null ? timelineCue.CueId : "BedroomMorningCue",
                timelineCue != null ? timelineCue.name : "BedroomMorningCue",
                timelinePlayable);
            EditorUtility.SetDirty(timelineCatalog);

            var graphBuilder = new BedroomStoryGraphBuilder(GraphPath);
            var graph = graphBuilder.Build(bundle);
            RepairGraphNodeReferences(GraphPath, graph);

            graphRegistry.SetGraphs(new[] { graph });
            variableCatalog.SetVariables(new[] { bundle.IntroReady, bundle.LaptopChecked, bundle.CanonicalPath });
            stateMachineCatalog.SetStateMachines(new[] { bundle.ProgressionState });

            EditorUtility.SetDirty(graphRegistry);
            EditorUtility.SetDirty(variableCatalog);
            EditorUtility.SetDirty(stateMachineCatalog);
            EditorUtility.SetDirty(config);

            EnsureClassroomScene();
            var bedroomScene = EditorSceneManager.OpenScene(BedroomScenePath, OpenSceneMode.Single);
            WireBedroomScene(config, channels, graph);
            EditorSceneManager.MarkSceneDirty(bedroomScene);
            EditorSceneManager.SaveScene(bedroomScene);

            var classroomScene = EditorSceneManager.OpenScene(ClassroomScenePath, OpenSceneMode.Single);
            RemoveBedroomStoryObjects();
            EditorSceneManager.MarkSceneDirty(classroomScene);
            EditorSceneManager.SaveScene(classroomScene);

            EnsureBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!string.IsNullOrWhiteSpace(originalScene) && originalScene != BedroomScenePath)
            {
                EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
            }
        }

        private static void EnsureClassroomScene()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(ClassroomScenePath);
            if (existing != null)
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("ClassroomArrivalRoot");
            var camera = new GameObject("Main Camera");
            camera.tag = "MainCamera";
            var cameraComponent = camera.AddComponent<Camera>();
            cameraComponent.transform.position = new Vector3(0f, 1.8f, -8f);
            cameraComponent.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
            new GameObject("Directional Light").AddComponent<Light>().type = LightType.Directional;
            root.transform.position = Vector3.zero;
            EditorSceneManager.SaveScene(scene, ClassroomScenePath);
        }

        private static void WireBedroomScene(StoryFlowProjectConfig config, StoryFlowChannels channels, ModularStoryFlow.Runtime.Graph.StoryGraphAsset graph)
        {
            var player = Object.FindFirstObjectByType<StoryFlowPlayer>();
            if (player == null)
            {
                player = new GameObject("BedroomStoryFlowPlayer").AddComponent<StoryFlowPlayer>();
            }

            player.ProjectConfig = config;
            player.InitialGraph = graph;
            player.PlayOnStart = true;

            var serializedPlayer = new SerializedObject(player);
            SetBooleanIfPresent(serializedPlayer, "autoLoadSaveOnStart", false);
            SetStringIfPresent(serializedPlayer, "saveSlot", "quick");
            serializedPlayer.ApplyModifiedPropertiesWithoutUndo();

            var uiRoot = player.GetComponent<BedroomStoryUiRoot>() ?? player.gameObject.AddComponent<BedroomStoryUiRoot>();
            uiRoot.Configure(channels);
            var sceneTransition = player.GetComponent<BedroomStorySceneTransition>() ?? player.gameObject.AddComponent<BedroomStorySceneTransition>();
            sceneTransition.Configure(ClassroomScenePath, "ClassroomArrivalScene");

            var laptop = FindOrCreateRoomInteractable("_Environment/Room/MacBook", "MacBook", "Laptop");
            var door = FindOrCreateDoorInteractable();
            var bed = FindOrCreateRoomInteractable("_Environment/Room/Bed", "Bed", "Bed");
            var desk = FindOrCreateRoomInteractable("_Environment/Room/Office+Desk", "Office+Desk", "Desk");
            var mirror = FindOrCreateRoomInteractable("_Environment/Room/BathroomWashing.001", "BathroomWashing.001", "Mirror")
                         ?? FindOrCreateRoomInteractable("_Environment/Room/BathroomWashing", "BathroomWashing", "Mirror");
            var wardrobe = FindOrCreateRoomInteractable("_Environment/Room/BedroomWardrobes", "BedroomWardrobes", "Wardrobe");
            var coffeeMaker = FindOrCreateRoomInteractable("_Environment/Room/Coffee+Maker", "Coffee+Maker", "Coffee Maker");
            var oven = FindOrCreateRoomInteractable("_Environment/Room/Oven.001", "Oven.001", "Oven");
            var bridge = player.GetComponent<BedroomStoryInteractionBridge>() ?? player.gameObject.AddComponent<BedroomStoryInteractionBridge>();
            ConfigureBridge(bridge, channels, laptop, door, bed, desk, mirror, wardrobe, coffeeMaker, oven, sceneTransition);

            var timelineBridge = Object.FindFirstObjectByType<StoryTimelineDirectorBridge>();
            if (timelineBridge == null)
            {
                var timelineObject = new GameObject("BedroomStoryTimelineBridge");
                timelineObject.AddComponent<PlayableDirector>();
                timelineBridge = timelineObject.AddComponent<StoryTimelineDirectorBridge>();
            }

            var serializedTimelineBridge = new SerializedObject(timelineBridge);
            serializedTimelineBridge.FindProperty("projectConfig").objectReferenceValue = config;
            serializedTimelineBridge.ApplyModifiedPropertiesWithoutUndo();

            if (door != null)
            {
                door.options.Clear();
                door.options.Add(new InteractionOption
                {
                    id = "leave",
                    label = "Leave",
                    slot = InteractionOptionSlot.Top,
                    visible = false,
                    enabled = false
                });
            }

            if (laptop != null)
            {
                laptop.options.Clear();
                laptop.options.Add(new InteractionOption
                {
                    id = "open",
                    label = "Check Laptop",
                    slot = InteractionOptionSlot.Top,
                    visible = true,
                    enabled = true
                });
            }

            ConfigureLookDialogueItem(mirror, "Mirror", "I look half asleep.");
            ConfigureLookDialogueItem(desk, "Desk", "I should probably be more prepared for class.");
            ConfigureLookDialogueItem(bed, "Bed", "Tempting... but no.");
            ConfigureLookInspectDialogueItem(coffeeMaker, "Coffee Maker", "Cold coffee clings to the plate. I meant to clean it before class.");
            ConfigureLookInspectDialogueItem(oven, "Oven", "It should probably stay off unless I actually want to bake something.");
            ConfigureWakeUpSpawnSetup(player.gameObject);

            EnsureCharacterCustomizationIntegration(player.gameObject, wardrobe);
            ConfigureWardrobeDecorations(wardrobe);
        }

        private static void ConfigureWakeUpSpawnSetup(GameObject storyRoot)
        {
            var primarySpawnPad = GameObject.Find(PrimarySpawnPadPath)?.transform;
            var secondarySpawnPad = GameObject.Find(SecondarySpawnPadPath)?.transform;
            var bed = GameObject.Find("_Environment/Room/Bed") ?? GameObject.Find("Bed");
            if (primarySpawnPad == null || secondarySpawnPad == null || bed == null || !TryGetObjectBounds(bed, out var bedBounds))
            {
                return;
            }

            var laptop = GameObject.Find("_Environment/Room/MacBook") ?? GameObject.Find("MacBook");
            var roomDirection = Vector3.right;
            if (laptop != null && TryGetObjectBounds(laptop, out var laptopBounds))
            {
                roomDirection = laptopBounds.center - bedBounds.center;
            }

            roomDirection = Vector3.ProjectOnPlane(roomDirection, Vector3.up);
            if (roomDirection.sqrMagnitude < 0.0001f)
            {
                roomDirection = Vector3.right;
            }

            roomDirection.Normalize();
            var sideDirection = Vector3.Cross(Vector3.up, roomDirection).normalized;
            if (sideDirection.sqrMagnitude < 0.0001f)
            {
                sideDirection = Vector3.forward;
            }

            var wakeUpCenter = bedBounds.center + (roomDirection * (Mathf.Max(bedBounds.extents.x, bedBounds.extents.z) + WakeUpSpawnClearance));
            wakeUpCenter.y = bedBounds.min.y + WakeUpVerticalOffset;

            var wakeUpRotation = Quaternion.LookRotation(-roomDirection, Vector3.up);
            primarySpawnPad.SetPositionAndRotation(wakeUpCenter + (sideDirection * WakeUpSpawnSpacing), wakeUpRotation);
            secondarySpawnPad.SetPositionAndRotation(wakeUpCenter - (sideDirection * WakeUpSpawnSpacing), wakeUpRotation);

            ConfigureGameManagerSpawns(primarySpawnPad, secondarySpawnPad);
            ConfigureRuntimeWakeUpAnchor(storyRoot, PrimarySpawnPadPath);
            EditorUtility.SetDirty(primarySpawnPad);
            EditorUtility.SetDirty(secondarySpawnPad);
        }

        private static void ConfigureGameManagerSpawns(Transform primarySpawnPad, Transform secondarySpawnPad)
        {
            var gameManager = Object.FindFirstObjectByType<GameManager>();
            if (gameManager == null)
            {
                return;
            }

            var serialized = new SerializedObject(gameManager);
            var spawnPoints = serialized.FindProperty("spawnPoints");
            spawnPoints.arraySize = 2;
            spawnPoints.GetArrayElementAtIndex(0).objectReferenceValue = primarySpawnPad;
            spawnPoints.GetArrayElementAtIndex(1).objectReferenceValue = secondarySpawnPad;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(gameManager);
        }

        private static void ConfigureRuntimeWakeUpAnchor(GameObject storyRoot, string wakeUpAnchorPath)
        {
            if (storyRoot == null)
            {
                return;
            }

            var bootstrap = storyRoot.GetComponent<BedroomStoryRuntimeBootstrap>() ?? storyRoot.AddComponent<BedroomStoryRuntimeBootstrap>();
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("wakeUpAnchorPath").stringValue = wakeUpAnchorPath;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrap);
        }

        private static bool TryGetObjectBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
            {
                return false;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (hasBounds)
            {
                return true;
            }

            var colliders = target.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (collider == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }

        private static InteractableItem FindOrCreateRoomInteractable(string hierarchyPath, string fallbackName, string displayName)
        {
            var target = GameObject.Find(hierarchyPath) ?? GameObject.Find(fallbackName);
            if (target == null)
            {
                return null;
            }

            var interactable = target.GetComponent<InteractableItem>();
            if (interactable == null)
            {
                interactable = target.AddComponent<InteractableItem>();
            }

            var outline = target.GetComponent<SelectableOutline>();
            if (outline == null)
            {
                outline = target.AddComponent<SelectableOutline>();
            }

            interactable.displayName = displayName;
            interactable.promptAnchor = EnsurePromptAnchor(interactable);
            interactable.inspectionSourceRoot = interactable.inspectionSourceRoot != null ? interactable.inspectionSourceRoot : target.transform;
            interactable.outline = outline;
            interactable.isInteractable = true;
            return interactable;
        }

        private static InteractableItem FindOrCreateDoorInteractable()
        {
            var doorGameObject = GameObject.Find("_Environment/Room/EnteranceDoor") ?? GameObject.Find("EnteranceDoor");
            if (doorGameObject == null)
            {
                return null;
            }

            var interactable = doorGameObject.GetComponent<InteractableItem>();
            if (interactable == null)
            {
                interactable = doorGameObject.AddComponent<InteractableItem>();
            }

            var outline = doorGameObject.GetComponent<SelectableOutline>();
            if (outline == null)
            {
                outline = doorGameObject.AddComponent<SelectableOutline>();
            }

            interactable.displayName = "Entrance Door";
            interactable.promptAnchor = EnsurePromptAnchor(interactable);
            interactable.inspectionSourceRoot = doorGameObject.transform;
            interactable.outline = outline;
            interactable.isInteractable = true;
            return interactable;
        }

        private static T ResolveGeneratedAsset<T>(string expectedPath, T fallback) where T : Object
        {
            if (fallback != null)
            {
                return fallback;
            }

            var asset = AssetDatabase.LoadAssetAtPath<T>(expectedPath);
            if (asset != null)
            {
                return asset;
            }

            var searchFolders = new[] { GeneratedRoot };
            var assetPath = AssetDatabase.FindAssets($"t:{typeof(T).Name}", searchFolders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => path.StartsWith(GeneratedRoot));

            return string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        private static void EnsureBuildSettings()
        {
            var existing = EditorBuildSettings.scenes.ToList();
            if (existing.All(scene => scene.path != ClassroomScenePath))
            {
                existing.Add(new EditorBuildSettingsScene(ClassroomScenePath, true));
                EditorBuildSettings.scenes = existing.ToArray();
            }
        }

        private static void RemoveBedroomStoryObjects()
        {
            var player = Object.FindFirstObjectByType<StoryFlowPlayer>();
            if (player != null && player.gameObject.name == "BedroomStoryFlowPlayer")
            {
                Object.DestroyImmediate(player.gameObject);
            }

            var timelineBridge = Object.FindFirstObjectByType<StoryTimelineDirectorBridge>();
            if (timelineBridge != null && timelineBridge.gameObject.name == "BedroomStoryTimelineBridge")
            {
                Object.DestroyImmediate(timelineBridge.gameObject);
            }
        }

        private static string EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent))
            {
                var parts = parent.Split('/');
                var current = "Assets";
                for (var index = 1; index < parts.Length; index++)
                {
                    var next = current + "/" + parts[index];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[index]);
                    }

                    current = next;
                }
            }

            var path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }

            return path;
        }

        private static void ConfigureBridge(
            BedroomStoryInteractionBridge bridge,
            StoryFlowChannels channels,
            InteractableItem laptop,
            InteractableItem door,
            InteractableItem bed,
            InteractableItem desk,
            InteractableItem mirror,
            InteractableItem wardrobe,
            InteractableItem coffeeMaker,
            InteractableItem oven,
            BedroomStorySceneTransition transition)
        {
            if (bridge == null)
            {
                return;
            }

            var serializedBridge = new SerializedObject(bridge);
            SetObjectReferenceIfPresent(serializedBridge, "channels", channels);
            SetObjectReferenceIfPresent(serializedBridge, "laptopInteractable", laptop);
            SetObjectReferenceIfPresent(serializedBridge, "enteranceDoorInteractable", door);
            SetObjectReferenceIfPresent(serializedBridge, "bedInteractable", bed);
            SetObjectReferenceIfPresent(serializedBridge, "deskInteractable", desk);
            SetObjectReferenceIfPresent(serializedBridge, "mirrorInteractable", mirror);
            SetObjectReferenceIfPresent(serializedBridge, "wardrobeInteractable", wardrobe);
            SetObjectReferenceIfPresent(serializedBridge, "coffeeMakerInteractable", coffeeMaker);
            SetObjectReferenceIfPresent(serializedBridge, "ovenInteractable", oven);
            SetObjectReferenceIfPresent(serializedBridge, "sceneTransition", transition);
            serializedBridge.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bridge);
        }

        private static void ConfigureLookDialogueItem(InteractableItem interactable, string displayName, string line)
        {
            if (interactable == null)
            {
                return;
            }

            interactable.displayName = displayName;
            interactable.storyId = $"room.{ToStoryIdSegment(displayName)}";
            interactable.lookDialogueSpeaker = "You";
            interactable.lookDialogueBody = line;
            interactable.lookDialogueDisplayDurationSeconds = 2.25f;
            interactable.options.Clear();
            interactable.options.Add(new InteractionOption
            {
                id = "look",
                label = "Look",
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
            EditorUtility.SetDirty(interactable);
        }

        private static void ConfigureLookInspectDialogueItem(InteractableItem interactable, string displayName, string line)
        {
            if (interactable == null)
            {
                return;
            }

            interactable.displayName = displayName;
            interactable.storyId = $"room.{ToStoryIdSegment(displayName)}";
            interactable.lookDialogueSpeaker = "You";
            interactable.lookDialogueBody = line;
            interactable.lookDialogueDisplayDurationSeconds = 2.25f;
            interactable.options.Clear();
            interactable.options.Add(new InteractionOption
            {
                id = "look",
                label = "Look",
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
            interactable.options.Add(new InteractionOption
            {
                id = "inspect",
                label = "Inspect",
                slot = InteractionOptionSlot.Bottom,
                visible = true,
                enabled = true,
                opensInspection = true
            });
            EditorUtility.SetDirty(interactable);
        }

        private static void ConfigureWardrobeDecorations(InteractableItem wardrobeInteractable)
        {
            if (wardrobeInteractable == null || wardrobeInteractable.transform == null)
            {
                return;
            }

            var wardrobeRoot = wardrobeInteractable.transform;
            var transforms = wardrobeRoot.GetComponentsInChildren<Transform>(true);
            var decorationIndex = 0;
            for (var index = 0; index < transforms.Length; index++)
            {
                var candidate = transforms[index];
                if (candidate == null || candidate == wardrobeRoot || !IsWardrobeDecorationName(candidate.name))
                {
                    continue;
                }

                var displayName = GetWardrobeDecorationDisplayName(candidate.name);
                var interactable = PrepareWardrobeDecorationInteractable(candidate.gameObject, displayName);
                ConfigureLookDialogueItem(
                    interactable,
                    displayName,
                    GetWardrobeDecorationLine(candidate.name, decorationIndex));
                decorationIndex++;
            }
        }

        private static InteractableItem PrepareWardrobeDecorationInteractable(GameObject target, string displayName)
        {
            if (target == null)
            {
                return null;
            }

            var interactable = target.GetComponent<InteractableItem>() ?? target.AddComponent<InteractableItem>();
            interactable.displayName = displayName;
            interactable.promptAnchor = EnsurePromptAnchor(interactable);
            interactable.inspectionSourceRoot = interactable.inspectionSourceRoot != null ? interactable.inspectionSourceRoot : interactable.transform;
            interactable.outline = interactable.outline != null ? interactable.outline : interactable.GetComponent<SelectableOutline>() ?? interactable.gameObject.AddComponent<SelectableOutline>();
            interactable.isInteractable = true;
            return interactable;
        }

        private static void EnsureCharacterCustomizationIntegration(GameObject playerRoot, InteractableItem wardrobeInteractable)
        {
            EnsureCharacterCustomizationCatalogAssetExists();

            if (playerRoot == null)
            {
                return;
            }

            var panelTransform = playerRoot.transform.Find(CustomizationPanelObjectName);
            var panelObject = panelTransform != null ? panelTransform.gameObject : new GameObject(CustomizationPanelObjectName);
            if (panelObject.transform.parent != playerRoot.transform)
            {
                panelObject.transform.SetParent(playerRoot.transform, false);
            }

            var uiDocument = panelObject.GetComponent<UIDocument>() ?? panelObject.AddComponent<UIDocument>();
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(BlocksPanelSettingsPath);
            if (panelSettings != null)
            {
                uiDocument.panelSettings = panelSettings;
            }

            var customizationPanel = panelObject.GetComponent<CharacterCustomizationPanel>() ?? panelObject.AddComponent<CharacterCustomizationPanel>();

            var wardrobeObject = wardrobeInteractable != null ? wardrobeInteractable.gameObject : GameObject.Find(WardrobeObjectName);
            if (wardrobeObject == null)
            {
                return;
            }

            var wardrobeItem = wardrobeObject.GetComponent<InteractableItem>();
            if (wardrobeItem == null)
            {
                return;
            }

            var wardrobeHook = wardrobeObject.GetComponent<CharacterCustomizationWardrobeHook>() ?? wardrobeObject.AddComponent<CharacterCustomizationWardrobeHook>();
            var serializedHook = new SerializedObject(wardrobeHook);
            SetObjectReferenceIfPresent(serializedHook, "interactableItem", wardrobeItem);
            SetObjectReferenceIfPresent(serializedHook, "customizationPanel", customizationPanel);
            SetStringIfPresent(serializedHook, "optionId", "change_character");
            SetStringIfPresent(serializedHook, "optionLabel", "Change Character");
            var slotProperty = serializedHook.FindProperty("slot");
            if (slotProperty != null)
            {
                slotProperty.enumValueIndex = (int)InteractionOptionSlot.Top;
            }
            serializedHook.ApplyModifiedPropertiesWithoutUndo();
            wardrobeHook.InstallOption();

            EditorUtility.SetDirty(panelObject);
            EditorUtility.SetDirty(wardrobeHook);
            EditorUtility.SetDirty(wardrobeItem);
        }

        private static bool IsWardrobeDecorationName(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                   && (name.IndexOf("shoe", System.StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("rack", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetWardrobeDecorationDisplayName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "Shoes";
            }

            if (rawName.IndexOf("rack", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Shoe Rack";
            }

            return "Shoes";
        }

        private static string GetWardrobeDecorationLine(string rawName, int decorationIndex)
        {
            if (!string.IsNullOrWhiteSpace(rawName) && rawName.IndexOf("rack", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "The rack is packed with shoes I probably should have sorted months ago.";
            }

            return decorationIndex == 0
                ? "A neat pair of shoes. Small details like this make the room feel lived in."
                : "Another pair of shoes, waiting for a day that feels a little more put together.";
        }

        private static Transform EnsurePromptAnchor(InteractableItem interactable)
        {
            if (interactable == null || interactable.transform == null)
            {
                return null;
            }

            var root = interactable.transform;
            var anchor = root.Find("PromptAnchor");
            if (anchor == null)
            {
                var anchorObject = new GameObject("PromptAnchor");
                anchorObject.hideFlags = HideFlags.HideInHierarchy;
                anchor = anchorObject.transform;
                anchor.SetParent(root, false);
            }

            if (TryGetCombinedBounds(interactable.gameObject, out var bounds))
            {
                var anchorPosition = GetPromptAnchorPosition(bounds);
                anchor.position = anchorPosition;
            }
            else
            {
                anchor.localPosition = new Vector3(0f, 0.2f, 0f);
            }

            return anchor;
        }

        private static Vector3 GetPromptAnchorPosition(Bounds bounds)
        {
            // Use a lower anchor for tall meshes (wardrobes, beds) so prompts don't float near ceilings.
            var objectHeight = Mathf.Max(bounds.size.y, 0.01f);
            var normalizedHeight = objectHeight >= 1.8f
                ? 0.14f
                : objectHeight >= 0.8f
                    ? 0.32f
                    : 0.62f;
            var anchorY = Mathf.Lerp(bounds.min.y, bounds.max.y, normalizedHeight);
            return new Vector3(bounds.center.x, anchorY, bounds.center.z);
        }

        private static bool TryGetCombinedBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
            {
                return false;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (hasBounds)
            {
                return true;
            }

            var colliders = target.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (collider == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }

        private static string ToStoryIdSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "item";
            }

            var chars = new char[value.Length];
            var count = 0;
            for (var index = 0; index < value.Length; index++)
            {
                var c = value[index];
                if (!char.IsLetterOrDigit(c))
                {
                    continue;
                }

                chars[count++] = char.ToLowerInvariant(c);
            }

            return count > 0 ? new string(chars, 0, count) : "item";
        }

        private static void EnsureCharacterCustomizationCatalogAssetExists()
        {
            var existingCatalog = AssetDatabase.LoadAssetAtPath<CharacterCustomizationCatalog>(CharacterCustomizationCatalogAssetPath);
            if (existingCatalog != null)
            {
                return;
            }

            EditorApplication.ExecuteMenuItem("Tools/Blocks/Character Customization/Regenerate Catalog");
        }

        private static void RepairGraphNodeReferences(string graphPath, ModularStoryFlow.Runtime.Graph.StoryGraphAsset graph)
        {
            if (graph == null || string.IsNullOrWhiteSpace(graphPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(graphPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var nodeFolder = $"{directory}/{Path.GetFileNameWithoutExtension(graphPath)}_Nodes";
            if (!AssetDatabase.IsValidFolder(nodeFolder))
            {
                return;
            }

            var nodes = AssetDatabase.FindAssets("t:StoryNodeAsset", new[] { nodeFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ModularStoryFlow.Runtime.Graph.StoryNodeAsset>)
                .Where(node => node != null)
                .OrderBy(node => node.EditorPosition.x)
                .ThenBy(node => node.EditorPosition.y)
                .ThenBy(node => node.NodeId)
                .ToList();

            if (nodes.Count == 0)
            {
                return;
            }

            var serializedGraph = new SerializedObject(graph);
            var nodesProperty = serializedGraph.FindProperty("nodes");
            if (nodesProperty == null)
            {
                return;
            }

            nodesProperty.arraySize = nodes.Count;
            for (var index = 0; index < nodes.Count; index++)
            {
                nodesProperty.GetArrayElementAtIndex(index).objectReferenceValue = nodes[index];
            }

            serializedGraph.ApplyModifiedPropertiesWithoutUndo();
            graph.InvalidateCache();
            EditorUtility.SetDirty(graph);
            AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceUpdate);
        }

        private static void SetObjectReferenceIfPresent(SerializedObject serializedObject, string propertyName, Object value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetStringIfPresent(SerializedObject serializedObject, string propertyName, string value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.stringValue = value;
            }
        }

        private static void SetBooleanIfPresent(SerializedObject serializedObject, string propertyName, bool value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }
    }
}
