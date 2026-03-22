using System;
using System.Collections;
using System.Reflection;
using Blocks.Gameplay.Core.Story;
using ItemInteraction;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UIImage = UnityEngine.UI.Image;
using ModularStoryFlow.Runtime.Bridges;

namespace Blocks.Gameplay.Core.Editor.Story
{
    public sealed class LabPersistentVerificationTests
    {
        private const string LabScenePath = "Assets/LabScene.unity";
        private const int BenchmarkFrames = 120;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.OpenScene(LabScenePath, OpenSceneMode.Single);
        }

        [Test]
        public void CapInputOnlyReturnsAfterDialogueTeardownWindow()
        {
            var director = UnityEngine.Object.FindFirstObjectByType<LabCapConversationDirector>(FindObjectsInactive.Include);
            Assert.That(director, Is.Not.Null, "Missing LabCapConversationDirector.");

            InvokeVoid(director, "ResolveReferences");
            var freeChatUi = UnityEngine.Object.FindFirstObjectByType<ClassroomNpcFreeChatUi>(FindObjectsInactive.Include);
            Assert.That(freeChatUi, Is.Not.Null, "Missing ClassroomNpcFreeChatUi after ResolveReferences.");

            freeChatUi.HideImmediate();
            SetPrivateField(director, "lastDialogueHideReadyTime", Time.unscaledTime + 999f);

            var routine = InvokeEnumerator(director, "RunFreeChatRoutine");
            Assert.That(routine, Is.Not.Null, "Could not start RunFreeChatRoutine.");
            Assert.That(routine.MoveNext(), Is.True, "RunFreeChatRoutine did not advance to its first wait.");

            var window = GetPrivateField<VisualElement>(freeChatUi, "window");
            Assert.That(freeChatUi.IsOpen, Is.True, "Free chat UI did not open.");
            Assert.That(window, Is.Not.Null, "Missing free chat input window.");
            Assert.That(window.resolvedStyle.opacity, Is.LessThanOrEqualTo(0.001f), "Input became visible before dialogue teardown completed.");

            SetPrivateField(director, "lastDialogueHideReadyTime", Time.unscaledTime - 1f);
            Assert.That(routine.MoveNext(), Is.True, "RunFreeChatRoutine did not resume after dialogue teardown time passed.");
            Assert.That(window.resolvedStyle.opacity, Is.GreaterThanOrEqualTo(0.999f), "Input did not become visible after dialogue teardown completed.");

            freeChatUi.HideImmediate();
        }

        [Test]
        public void FreeChatInputUsesComfortableSingleLineMetrics()
        {
            var freeChatUi = UnityEngine.Object.FindFirstObjectByType<ClassroomNpcFreeChatUi>(FindObjectsInactive.Include);
            Assert.That(freeChatUi, Is.Not.Null, "Missing ClassroomNpcFreeChatUi.");

            freeChatUi.Open("CAP");
            freeChatUi.SetInputVisible(true, immediate: true);

            var inputField = GetPrivateField<TextField>(freeChatUi, "inputField");
            var textInput = GetPrivateField<VisualElement>(freeChatUi, "inputTextInput");
            Assert.That(inputField, Is.Not.Null, "Missing free chat input field.");
            Assert.That(textInput, Is.Not.Null, "Missing free chat text input child.");
            Assert.That(inputField.style.height.value.value, Is.GreaterThanOrEqualTo(48f), "Free chat input field is too short and risks clipping typed text.");
            Assert.That(inputField.style.paddingTop.value.value, Is.EqualTo(0f));
            Assert.That(inputField.style.paddingBottom.value.value, Is.EqualTo(0f));
            Assert.That(textInput.style.paddingTop.value.value, Is.LessThanOrEqualTo(2f));
            Assert.That(textInput.style.paddingBottom.value.value, Is.LessThanOrEqualTo(2f));
            var fieldHeight = GetFiniteLength(inputField.resolvedStyle.height, inputField.style.height.value.value, 48f);
            var textInputHeight = GetFiniteLength(textInput.resolvedStyle.height, textInput.style.minHeight.value.value, fieldHeight);
            Assert.That(textInputHeight, Is.LessThanOrEqualTo(fieldHeight), "The inner text input is taller than the visible field and can clip text.");

            var configuredFontSize = GetFiniteLength(textInput.resolvedStyle.fontSize, textInput.style.fontSize.value.value, 18f);
            var availableTextHeight = textInputHeight - textInput.style.paddingTop.value.value - textInput.style.paddingBottom.value.value;
            Assert.That(availableTextHeight, Is.GreaterThanOrEqualTo(configuredFontSize + 6f), "The free chat input does not leave enough vertical room for the configured single-line font.");

            freeChatUi.HideImmediate();
        }

        [UnityTest]
        public IEnumerator RouteLightOpensOnlyAfterMachineInteraction()
        {
            var bridge = UnityEngine.Object.FindFirstObjectByType<LabStoryInteractionBridge>(FindObjectsInactive.Include);
            var sceneContext = UnityEngine.Object.FindFirstObjectByType<LabSceneContext>(FindObjectsInactive.Include);
            Assert.That(bridge, Is.Not.Null, "Missing LabStoryInteractionBridge.");
            Assert.That(sceneContext, Is.Not.Null, "Missing LabSceneContext.");

            sceneContext.ResolveRuntimeReferences();
            var bodyUi = sceneContext.BodyInspectionUi;
            var puzzleUi = sceneContext.LightPuzzleUi;
            Assert.That(bodyUi, Is.Not.Null, "Missing LabBodyInspectionUi.");
            Assert.That(puzzleUi, Is.Not.Null, "Missing LabLightPuzzleUi.");

            bodyUi.Open();
            bodyUi.Close();
            puzzleUi.HideImmediate();
            SetPrivateField(bridge, "puzzleReady", true);
            SetPrivateField(bridge, "bodyInspectionCompleted", true);
            SetPrivateField(bridge, "puzzleSolved", false);
            SetPrivateField(bridge, "shrinkReady", false);
            SetPrivateField(bridge, "puzzleOpenRequestedByMachine", false);

            InvokeVoid(bridge, "QueuePuzzleOpenIfNeeded");
            Assert.That(puzzleUi.IsOpen, Is.False, "Route Light should stay closed until the shrink machine is used.");

            InvokeVoid(bridge, "ApplyScenePresentation");
            var machine = sceneContext.DnaMachineInteractable;
            Assert.That(machine, Is.Not.Null, "Missing shrink machine interactable.");
            var option = machine.FindOption("use");
            Assert.That(option, Is.Not.Null, "Missing shrink machine use option.");
            InvokeVoid(bridge, "HandleDnaMachineTriggered", new InteractionInvocation(null, machine, option));

            var openRoutine = InvokeEnumerator(bridge, "OpenLightPuzzleNextFrame");
            Assert.That(openRoutine, Is.Not.Null, "Could not start the deferred light puzzle open routine.");
            while (openRoutine.MoveNext())
            {
                yield return openRoutine.Current;
            }

            Assert.That(puzzleUi.IsOpen, Is.True, "Route Light popup did not open LabLightPuzzleUi.");
            var puzzleDocument = GetPrivateField<UIDocument>(puzzleUi, "uiDocument");
            Assert.That(puzzleDocument, Is.Not.Null, "LabLightPuzzleUi did not bind a UIDocument.");
            Assert.That(puzzleDocument.gameObject.name, Is.EqualTo("LabLightPuzzleUiRoot"), "LabLightPuzzleUi should render on its own runtime UIDocument root.");
            Assert.That(puzzleDocument.rootVisualElement.Q<VisualElement>("LabPuzzleRoot"), Is.Not.Null, "LabLightPuzzleUi did not attach its overlay to the live UIDocument tree.");
            puzzleUi.HideImmediate();
        }

        [Test]
        public void LabLightPuzzleUiLivesOnSingleUiRootLikeFreeChat()
        {
            var puzzleUi = UnityEngine.Object.FindFirstObjectByType<LabLightPuzzleUi>(FindObjectsInactive.Include);
            var freeChatUi = UnityEngine.Object.FindFirstObjectByType<ClassroomNpcFreeChatUi>(FindObjectsInactive.Include);
            Assert.That(puzzleUi, Is.Not.Null, "Missing LabLightPuzzleUi.");
            Assert.That(freeChatUi, Is.Not.Null, "Missing ClassroomNpcFreeChatUi.");
            Assert.That(puzzleUi.gameObject.name, Is.EqualTo("LabLightPuzzleUiRoot"), "LabLightPuzzleUi should live on the same root object as its UIDocument, matching the free chat pattern.");
            Assert.That(puzzleUi.GetComponent<UIDocument>(), Is.Not.Null, "LabLightPuzzleUi root should carry its own UIDocument.");
            Assert.That(freeChatUi.gameObject.name, Is.EqualTo("ClassroomNpcFreeChatUiRoot"));
            Assert.That(freeChatUi.GetComponent<UIDocument>(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator DnaMachineUseOptionInvokesRouteLightWithoutLocalUnityEvent()
        {
            var bridge = UnityEngine.Object.FindFirstObjectByType<LabStoryInteractionBridge>(FindObjectsInactive.Include);
            var sceneContext = UnityEngine.Object.FindFirstObjectByType<LabSceneContext>(FindObjectsInactive.Include);
            Assert.That(bridge, Is.Not.Null, "Missing LabStoryInteractionBridge.");
            Assert.That(sceneContext, Is.Not.Null, "Missing LabSceneContext.");

            sceneContext.ResolveRuntimeReferences();
            var machine = sceneContext.DnaMachineInteractable;
            var puzzleUi = sceneContext.LightPuzzleUi;
            Assert.That(machine, Is.Not.Null, "Missing shrink machine interactable.");
            Assert.That(puzzleUi, Is.Not.Null, "Missing LabLightPuzzleUi.");

            SetPrivateField(bridge, "puzzleReady", true);
            SetPrivateField(bridge, "bodyInspectionCompleted", true);
            SetPrivateField(bridge, "puzzleSolved", false);
            SetPrivateField(bridge, "shrinkReady", false);
            puzzleUi.HideImmediate();

            InvokeVoid(bridge, "ApplyScenePresentation");

            var option = machine.FindOption("use");
            Assert.That(option, Is.Not.Null, "Missing shrink machine use option.");
            option.onInvoked.RemoveAllListeners();

            InvokeVoid(bridge, "RegisterSceneHooks");
            machine.InvokeOption(null, option);

            yield return null;
            yield return null;

            if (!puzzleUi.IsOpen)
            {
                var openRoutine = InvokeEnumerator(bridge, "OpenLightPuzzleNextFrame");
                if (openRoutine != null)
                {
                    while (openRoutine.MoveNext())
                    {
                        yield return openRoutine.Current;
                    }
                }
            }

            Assert.That(puzzleUi.IsOpen, Is.True, "The shrink machine use option should invoke Route Light even when On Invoked() has no listeners.");
            puzzleUi.HideImmediate();
            InvokeVoid(bridge, "UnregisterSceneHooks");
        }

        [Test]
        public void CapConversationLocksInteractionsWhileRunning()
        {
            var director = UnityEngine.Object.FindFirstObjectByType<LabCapConversationDirector>(FindObjectsInactive.Include);
            var interactionDirector = UnityEngine.Object.FindFirstObjectByType<InteractionDirector>(FindObjectsInactive.Include);
            Assert.That(director, Is.Not.Null, "Missing LabCapConversationDirector.");
            Assert.That(interactionDirector, Is.Not.Null, "Missing InteractionDirector.");

            InvokeVoid(director, "ResolveReferences");
            var routine = InvokeEnumerator(director, "RunConversationRoutine");
            Assert.That(routine, Is.Not.Null, "Could not start RunConversationRoutine.");
            Assert.That(routine.MoveNext(), Is.True, "RunConversationRoutine did not advance to first dialogue wait.");

            Assert.That(GetPrivateFieldValue<bool>(interactionDirector, "lockInteractions"), Is.True, "Interaction system should lock while CAP conversation is active.");

            InvokeVoid(director, "CleanupConversationState");
            Assert.That(GetPrivateFieldValue<bool>(interactionDirector, "lockInteractions"), Is.False, "Interaction system should unlock after CAP conversation cleanup.");
        }

        [Test]
        public void CapHasRootColliderForPromptFocus()
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<LabCapNpcController>(FindObjectsInactive.Include);
            Assert.That(controller, Is.Not.Null, "Missing LabCapNpcController.");

            InvokeVoid(controller, "ConfigureForLabMission");

            var collider = controller.GetComponent<Collider>();
            Assert.That(collider, Is.Not.Null, "CAP requires a root collider so the interaction prompt can focus it.");
            Assert.That(collider.enabled, Is.True, "CAP collider must stay enabled for prompt focus checks.");
            Assert.That(collider.isTrigger, Is.False, "CAP collider should block physics focus rays instead of acting as a trigger-only volume.");
        }

        [Test]
        public void InteractionDirectorStartsUnlockedInLabScene()
        {
            var interactionDirector = UnityEngine.Object.FindFirstObjectByType<InteractionDirector>(FindObjectsInactive.Include);
            Assert.That(interactionDirector, Is.Not.Null, "Missing InteractionDirector.");

            interactionDirector.gameObject.SetActive(false);
            interactionDirector.gameObject.SetActive(true);

            Assert.That(GetPrivateFieldValue<bool>(interactionDirector, "lockInteractions"), Is.False, "Lab interaction prompts should not boot in a locked state.");
        }

        [Test]
        public void LabGeneratedStoryFlowAssetsAreStructurallyValid()
        {
            var config = AssetDatabase.LoadAssetAtPath<ModularStoryFlow.Runtime.Player.StoryFlowProjectConfig>("Assets/StoryFlowLabGenerated/Config/StoryFlowProjectConfig.asset");
            var graph = AssetDatabase.LoadAssetAtPath<ModularStoryFlow.Runtime.Graph.StoryGraphAsset>("Assets/StoryFlowLabGenerated/Graphs/LabMissionStory.asset");

            Assert.That(config, Is.Not.Null, "Missing generated StoryFlow project config for Lab.");
            Assert.That(graph, Is.Not.Null, "Missing generated Lab graph asset.");
            Assert.That(config.Channels, Is.Not.Null, "Generated Lab StoryFlow config is missing channels.");
            Assert.That(config.Channels.DialogueRequests, Is.Not.Null, "Generated Lab channels are missing DialogueRequests.");
            Assert.That(config.Channels.ChoiceRequests, Is.Not.Null, "Generated Lab channels are missing ChoiceRequests.");
            Assert.That(config.Channels.ExternalSignals, Is.Not.Null, "Generated Lab channels are missing ExternalSignals.");
            Assert.That(config.GraphRegistry, Is.Not.Null, "Generated Lab config is missing graph registry.");
            Assert.That(config.GraphRegistry.Resolve(graph.GraphId), Is.EqualTo(graph), "Generated Lab graph registry does not resolve the Lab graph.");
            Assert.That(graph.Nodes.Count, Is.GreaterThanOrEqualTo(2), "Generated Lab graph should contain the mission flow nodes.");
            Assert.That(graph.Connections.Count, Is.GreaterThan(0), "Generated Lab graph should contain node connections.");
        }

        [Test]
        public void PlayerDialogueTemporarilyDisablesOtherItemInteractions()
        {
            var uiRoot = UnityEngine.Object.FindFirstObjectByType<LabStoryUiRoot>(FindObjectsInactive.Include);
            var interactionDirector = UnityEngine.Object.FindFirstObjectByType<InteractionDirector>(FindObjectsInactive.Include);
            var presenter = UnityEngine.Object.FindFirstObjectByType<BedroomStorySubtitlePresenter>(FindObjectsInactive.Include);
            var machine = UnityEngine.Object.FindFirstObjectByType<LabSceneContext>(FindObjectsInactive.Include)?.DnaMachineInteractable;

            Assert.That(uiRoot, Is.Not.Null, "Missing LabStoryUiRoot.");
            InvokeVoid(uiRoot, "EnsurePresenters");
            Assert.That(interactionDirector, Is.Not.Null, "Missing InteractionDirector.");
            presenter = UnityEngine.Object.FindFirstObjectByType<BedroomStorySubtitlePresenter>(FindObjectsInactive.Include);
            Assert.That(presenter, Is.Not.Null, "Missing BedroomStorySubtitlePresenter.");
            Assert.That(machine, Is.Not.Null, "Missing shrink machine interactable.");

            presenter.Present(new ModularStoryFlow.Runtime.Events.StoryDialogueRequest
            {
                SpeakerId = "You",
                SpeakerDisplayName = "You",
                Body = "Testing player dialogue lock.",
                AutoAdvance = true,
                AutoAdvanceDelaySeconds = 1f
            });

            InvokeVoid(interactionDirector, "Update");
            Assert.That(GetPrivateField<InteractableItem>(interactionDirector, "currentFocus"), Is.Null, "Other items should not stay focusable while player dialogue is visible.");

            presenter.HideImmediate();
        }

        [Test]
        public void ActiveItemInteractionTemporarilyDisablesOtherItemInteractions()
        {
            var interactionDirector = UnityEngine.Object.FindFirstObjectByType<InteractionDirector>(FindObjectsInactive.Include);
            var freeChatUi = UnityEngine.Object.FindFirstObjectByType<ClassroomNpcFreeChatUi>(FindObjectsInactive.Include);

            Assert.That(interactionDirector, Is.Not.Null, "Missing InteractionDirector.");
            Assert.That(freeChatUi, Is.Not.Null, "Missing ClassroomNpcFreeChatUi.");

            freeChatUi.Open("CAP");
            InvokeVoid(interactionDirector, "Update");
            Assert.That(GetPrivateField<InteractableItem>(interactionDirector, "currentFocus"), Is.Null, "Other items should not stay focusable while a free-chat item interaction is open.");

            freeChatUi.HideImmediate();
        }

        [Test]
        public void LightPuzzleTemporarilyDisablesOtherItemInteractions()
        {
            var interactionDirector = UnityEngine.Object.FindFirstObjectByType<InteractionDirector>(FindObjectsInactive.Include);
            var puzzleUi = UnityEngine.Object.FindFirstObjectByType<LabLightPuzzleUi>(FindObjectsInactive.Include);

            Assert.That(interactionDirector, Is.Not.Null, "Missing InteractionDirector.");
            Assert.That(puzzleUi, Is.Not.Null, "Missing LabLightPuzzleUi.");

            puzzleUi.Open();
            InvokeVoid(interactionDirector, "Update");
            Assert.That(GetPrivateField<InteractableItem>(interactionDirector, "currentFocus"), Is.Null, "Other items should not stay focusable while the light puzzle is open.");

            puzzleUi.HideImmediate();
        }

        [Test]
        public void ShrinkMachineIncludesInspectOptionWithoutReplacingRouteLight()
        {
            var bridge = UnityEngine.Object.FindFirstObjectByType<LabStoryInteractionBridge>(FindObjectsInactive.Include);
            var sceneContext = UnityEngine.Object.FindFirstObjectByType<LabSceneContext>(FindObjectsInactive.Include);
            Assert.That(bridge, Is.Not.Null, "Missing LabStoryInteractionBridge.");
            Assert.That(sceneContext, Is.Not.Null, "Missing LabSceneContext.");

            sceneContext.ResolveRuntimeReferences();
            SetPrivateField(bridge, "puzzleReady", true);
            SetPrivateField(bridge, "puzzleSolved", false);
            SetPrivateField(bridge, "shrinkReady", false);
            InvokeVoid(bridge, "ApplyScenePresentation");

            var machine = sceneContext.DnaMachineInteractable;
            Assert.That(machine, Is.Not.Null, "Missing shrink machine interactable.");
            Assert.That(machine.FindOption("use")?.label, Is.EqualTo("Route Light"));
            Assert.That(machine.FindOption("inspect"), Is.Not.Null, "Shrink machine should expose Inspect alongside Route Light.");
            Assert.That(machine.FindOption("inspect")?.opensInspection, Is.True, "Shrink machine Inspect should open the shared inspection overlay.");
        }

        [UnityTest]
        public IEnumerator MechanicalArmActivateKeepsRootPositionWhileAnimatingJoints()
        {
            var arm = UnityEngine.Object.FindFirstObjectByType<LabMechanicalArmController>(FindObjectsInactive.Include);
            Assert.That(arm, Is.Not.Null, "Missing LabMechanicalArmController on Mechanical arm 2.");

            var armTransform = arm.transform;
            var rootPosition = armTransform.position;
            var shoulder = armTransform.Find("Mechanical Arm 1/Point005");
            Assert.That(shoulder, Is.Not.Null, "Missing shoulder pivot Point005 on Mechanical arm 2.");
            var initialShoulderRotation = shoulder.localRotation;

            var interactable = arm.GetComponent<InteractableItem>();
            Assert.That(interactable, Is.Not.Null, "Mechanical arm controller requires an InteractableItem.");
            var option = interactable.FindOption("activate");
            Assert.That(option, Is.Not.Null, "Mechanical arm missing Activate option.");

            interactable.InvokeOption(null, option);
            yield return null;
            yield return null;

            Assert.That(Vector3.Distance(armTransform.position, rootPosition), Is.LessThan(0.0001f), "Mechanical arm root position should stay fixed during activation animation.");
            Assert.That(shoulder.localRotation, Is.Not.EqualTo(initialShoulderRotation), "Mechanical arm activation should animate the local shoulder pivot.");
        }

        [Test]
        public void LabFreeChatActionNormalizationMapsDoorDanceAndFollowCommands()
        {
            var director = UnityEngine.Object.FindFirstObjectByType<LabCapConversationDirector>(FindObjectsInactive.Include);
            Assert.That(director, Is.Not.Null, "Missing LabCapConversationDirector.");

            var actions = (System.Collections.Generic.List<string>)director.GetType()
                .GetMethod("NormalizeLabActions", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, new object[] { new[] { "open_the_door", "dance", "follow_me", "stop", "none" } });

            Assert.That(actions, Is.Not.Null);
            Assert.That(actions, Is.EquivalentTo(new[] { "open_door", "dance", "follow_player", "stop_following" }));
        }

        [Test]
        public void CapLabActionsCanOpenDoorAndToggleFollow()
        {
            var director = UnityEngine.Object.FindFirstObjectByType<LabCapConversationDirector>(FindObjectsInactive.Include);
            var sceneContext = UnityEngine.Object.FindFirstObjectByType<LabSceneContext>(FindObjectsInactive.Include);
            Assert.That(director, Is.Not.Null, "Missing LabCapConversationDirector.");
            Assert.That(sceneContext, Is.Not.Null, "Missing LabSceneContext.");
            sceneContext.ResolveRuntimeReferences();

            var door = sceneContext.DoorController;
            var cap = sceneContext.CapNpcController;
            Assert.That(door, Is.Not.Null, "Missing LabDoorController.");
            Assert.That(cap, Is.Not.Null, "Missing LabCapNpcController.");

            door.SetUnlocked(false);
            door.SetOpenImmediate(false);
            cap.SetFollowPlayer(false);

            var routine = InvokeEnumerator(director, "ExecuteLabActionsRoutine", new System.Collections.Generic.List<string> { "open_door", "follow_player", "stop_following" });
            Assert.That(routine, Is.Not.Null, "Could not start ExecuteLabActionsRoutine.");
            while (routine.MoveNext())
            {
            }

            var interactable = door.GetComponent<InteractableItem>();
            Assert.That(interactable, Is.Not.Null, "Door should still have an InteractableItem.");
            Assert.That(interactable.FindOption("use")?.label, Is.EqualTo("Close Door"), "CAP opening the door should leave the door open.");
            Assert.That(GetPrivateFieldValue<bool>(cap, "followPlayer"), Is.False, "Stop following should leave CAP idle at the end of the action batch.");
        }

        [Test]
        public void CapInfersLabActionsFromPlainLanguagePrompt()
        {
            var director = UnityEngine.Object.FindFirstObjectByType<LabCapConversationDirector>(FindObjectsInactive.Include);
            Assert.That(director, Is.Not.Null, "Missing LabCapConversationDirector.");

            var actions = (System.Collections.Generic.List<string>)director.GetType()
                .GetMethod("BuildReliableLabActions", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, new object[]
                {
                    "could you open the door for me, dance a little, then follow me and stop following when I ask",
                    new string[0]
                });

            Assert.That(actions, Is.Not.Null);
            Assert.That(actions, Does.Contain("open_door"));
            Assert.That(actions, Does.Contain("dance"));
            Assert.That(actions, Does.Contain("follow_player"));
            Assert.That(actions, Does.Contain("stop_following"));
        }

        [UnityTest]
        public IEnumerator CapDanceActionUsesDedicatedDanceController()
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<LabCapNpcController>(FindObjectsInactive.Include);
            Assert.That(controller, Is.Not.Null, "Missing LabCapNpcController.");

            var animator = GetPrivateField<Animator>(controller, "animator");
            var idleController = GetPrivateField<RuntimeAnimatorController>(controller, "idleController");
            var danceController = GetPrivateField<RuntimeAnimatorController>(controller, "danceController");
            var danceClip = GetPrivateField<AnimationClip>(controller, "danceClip");
            Assert.That(animator, Is.Not.Null, "Missing CAP animator.");
            Assert.That(idleController, Is.Not.Null, "Missing CAP idle controller.");
            Assert.That(danceController, Is.Not.Null, "Missing CAP dance controller.");
            Assert.That(danceClip, Is.Not.Null, "Missing CAP dance clip.");

            controller.PlayDance();
            yield return null;

            Assert.That(animator.runtimeAnimatorController, Is.EqualTo(danceController), "CAP dance should switch to the dedicated dance controller.");
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            Assert.That(clips, Is.Not.Empty, "CAP dance should play an animation clip.");
            Assert.That(clips[0].clip, Is.EqualTo(danceClip), "CAP dance should play the dedicated Dance_1 clip.");
        }

        [Test]
        public void LabSceneHasBedroomStyleTimelineBridge()
        {
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<LabStoryRuntimeBootstrap>(FindObjectsInactive.Include);
            var timelineBridge = UnityEngine.Object.FindFirstObjectByType<StoryTimelineDirectorBridge>(FindObjectsInactive.Include);
            Assert.That(bootstrap, Is.Not.Null, "Missing LabStoryRuntimeBootstrap.");
            Assert.That(timelineBridge, Is.Not.Null, "Missing StoryTimelineDirectorBridge in Lab scene.");
            Assert.That(timelineBridge.gameObject.name, Is.EqualTo("LabStoryTimelineBridge"), "Lab timeline bridge should live on its own dedicated root like the bedroom scene bridge.");
            Assert.That(timelineBridge.GetComponent<UnityEngine.Playables.PlayableDirector>(), Is.Not.Null, "Lab timeline bridge requires a PlayableDirector.");

            var assignedBridge = GetPrivateField<StoryTimelineDirectorBridge>(bootstrap, "timelineBridge");
            Assert.That(assignedBridge, Is.EqualTo(timelineBridge), "Lab runtime bootstrap should hold a serialized reference to the scene timeline bridge.");
        }

        [Test]
        public void CapTalkingSwapsMouthMaterial()
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<LabCapNpcController>(FindObjectsInactive.Include);
            Assert.That(controller, Is.Not.Null, "Missing LabCapNpcController.");

            var renderers = controller.GetComponentsInChildren<Renderer>(true);
            Renderer mouthRenderer = null;
            for (var index = 0; index < renderers.Length; index++)
            {
                var candidate = renderers[index];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.transform.name.IndexOf("Mo_LP", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    mouthRenderer = candidate;
                    break;
                }
            }

            Assert.That(mouthRenderer, Is.Not.Null, "Could not find CAP mouth renderer.");
            var originalMaterialName = mouthRenderer.sharedMaterials.Length > 0 && mouthRenderer.sharedMaterials[0] != null
                ? mouthRenderer.sharedMaterials[0].name
                : string.Empty;

            controller.SetTalking(true);
            var talkingMaterialName = mouthRenderer.sharedMaterials.Length > 0 && mouthRenderer.sharedMaterials[0] != null
                ? mouthRenderer.sharedMaterials[0].name
                : string.Empty;
            Assert.That(talkingMaterialName, Does.Contain("M_MOUTH_OPEN"), "CAP talking state did not swap to the mouth-open material.");

            controller.SetTalking(false);
            var resetMaterialName = mouthRenderer.sharedMaterials.Length > 0 && mouthRenderer.sharedMaterials[0] != null
                ? mouthRenderer.sharedMaterials[0].name
                : string.Empty;
            Assert.That(resetMaterialName, Does.Not.Contain("M_MOUTH_OPEN"), "CAP mouth renderer did not restore its default material after talking.");
            Assert.That(resetMaterialName, Is.Not.EqualTo(string.Empty));
            Assert.That(originalMaterialName, Is.Not.EqualTo(string.Empty));
        }

        [Test]
        public void CapDialogueUsesSubtitleSpeechBlips()
        {
            var uiRoot = UnityEngine.Object.FindFirstObjectByType<LabStoryUiRoot>(FindObjectsInactive.Include);
            Assert.That(uiRoot, Is.Not.Null, "Missing LabStoryUiRoot.");
            InvokeVoid(uiRoot, "EnsurePresenters");

            var presenter = UnityEngine.Object.FindFirstObjectByType<BedroomStorySubtitlePresenter>(FindObjectsInactive.Include);
            Assert.That(presenter, Is.Not.Null, "Missing BedroomStorySubtitlePresenter.");

            presenter.Present(new ModularStoryFlow.Runtime.Events.StoryDialogueRequest
            {
                SpeakerId = "CAP",
                SpeakerDisplayName = "CAP",
                Body = "Testing CAP speech blips.",
                AutoAdvance = true,
                AutoAdvanceDelaySeconds = 1f
            });

            Assert.That(GetPrivateFieldValue<bool>(presenter, "playCapSpeechBlips"), Is.True, "CAP dialogue should enable subtitle speech blips.");
            presenter.HideImmediate();
        }

        [Test]
        public void InteractionPromptPresenterUsesNeoBrutalistStyle()
        {
            var host = new GameObject("PromptPresenterTestHost", typeof(RectTransform));
            try
            {
                var presenter = host.AddComponent<InteractionPromptPresenter>();
                InvokeVoid(presenter, "EnsureInitialized");

                var promptBackdrop = GetPrivateField<UIImage>(presenter, "promptBackdrop");
                var connectorSegment = GetPrivateField<UIImage>(presenter, "connectorSegmentA");
                var titleUnderline = GetPrivateField<UIImage>(presenter, "titleUnderline");

                Assert.That(promptBackdrop, Is.Not.Null, "Prompt backdrop was not built.");
                Assert.That(connectorSegment, Is.Not.Null, "Prompt connector segment was not built.");
                Assert.That(titleUnderline, Is.Not.Null, "Prompt underline was not built.");
                Assert.That(promptBackdrop.color.a, Is.GreaterThan(0.75f), "Prompt background should be visibly filled for neo-brutalist style.");
                Assert.That(connectorSegment.color.r, Is.GreaterThanOrEqualTo(0.95f), "Connector line should stay white.");
                Assert.That(connectorSegment.color.g, Is.GreaterThanOrEqualTo(0.95f), "Connector line should stay white.");
                Assert.That(connectorSegment.color.b, Is.GreaterThanOrEqualTo(0.95f), "Connector line should stay white.");
                Assert.That(titleUnderline.color.r, Is.LessThanOrEqualTo(0.15f), "Title underline should be a dark brutalist accent.");
                Assert.That(titleUnderline.color.g, Is.LessThanOrEqualTo(0.15f), "Title underline should be a dark brutalist accent.");
                Assert.That(titleUnderline.color.b, Is.LessThanOrEqualTo(0.2f), "Title underline should be a dark brutalist accent.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void LabSceneMainCameraBenchmarkHitsHundredFpsAtCurrentProfile()
        {
            var mainCamera = Camera.main;
            Assert.That(mainCamera, Is.Not.Null, "Missing Camera.main.");

            LogAssert.ignoreFailingMessages = true;

            var gameViewSize = GetMainGameViewSize();
            var scale = ReadCurrentRenderScale();
            var width = Mathf.Max(320, Mathf.RoundToInt(gameViewSize.x * scale));
            var height = Mathf.Max(180, Mathf.RoundToInt(gameViewSize.y * scale));

            var target = new RenderTexture(width, height, 24);
            var previousTarget = mainCamera.targetTexture;
            var startTime = Time.realtimeSinceStartupAsDouble;

            try
            {
                mainCamera.targetTexture = target;
                for (var index = 0; index < BenchmarkFrames; index++)
                {
                    mainCamera.Render();
                }
            }
            finally
            {
                mainCamera.targetTexture = previousTarget;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }

            LogAssert.ignoreFailingMessages = false;
            var elapsedSeconds = Math.Max(0.0001d, Time.realtimeSinceStartupAsDouble - startTime);
            var fps = BenchmarkFrames / elapsedSeconds;
            Assert.That(fps, Is.GreaterThanOrEqualTo(100d), $"Main camera benchmark was {fps:0.0} FPS at {width}x{height} using renderScale {scale:0.##}.");
        }

        private static float ReadCurrentRenderScale()
        {
            var assetPath = "Assets/Core/Settings/PC_RPAsset.asset";
            var lines = System.IO.File.ReadAllLines(assetPath);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (!line.TrimStart().StartsWith("m_RenderScale:", StringComparison.Ordinal))
                {
                    continue;
                }

                var raw = line.Substring(line.IndexOf(':') + 1).Trim();
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                {
                    return Mathf.Clamp(value, 0.1f, 1f);
                }
            }

            return 1f;
        }

        private static Vector2 GetMainGameViewSize()
        {
            var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            var method = gameViewType?.GetMethod("GetSizeOfMainGameView", BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                return new Vector2(1920f, 1080f);
            }

            var result = method.Invoke(null, null);
            return result is Vector2 size ? size : new Vector2(1920f, 1080f);
        }

        private static IEnumerator InvokeEnumerator(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            return method?.Invoke(instance, null) as IEnumerator;
        }

        private static IEnumerator InvokeEnumerator(object instance, string methodName, object argument)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            return method?.Invoke(instance, new[] { argument }) as IEnumerator;
        }

        private static void InvokeVoid(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(instance, null);
        }

        private static void InvokeVoid(object instance, string methodName, object argument)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(instance, new[] { argument });
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(instance, value);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
            where T : class
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(instance) as T;
        }

        private static T GetPrivateFieldValue<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return default;
            }

            return (T)field.GetValue(instance);
        }

        private static float GetFiniteLength(float resolvedValue, float styleFallback, float defaultValue)
        {
            if (!float.IsNaN(resolvedValue) && !float.IsInfinity(resolvedValue) && resolvedValue > 0f)
            {
                return resolvedValue;
            }

            if (!float.IsNaN(styleFallback) && !float.IsInfinity(styleFallback) && styleFallback > 0f)
            {
                return styleFallback;
            }

            return defaultValue;
        }
    }
}
