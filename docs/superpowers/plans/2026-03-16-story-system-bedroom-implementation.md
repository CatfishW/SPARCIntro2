# Bedroom Story System Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a fully playable bedroom intro story slice that validates all runtime-capable story nodes, supports deterministic stage skipping, and visually matches the target subtitle style.

**Architecture:** Generate the story assets and scene wiring with an editor installer so the runtime slice stays reproducible, then add a small set of runtime coordinators for presentation, interaction bridging, stage replay, and scene transition. Cover the behavior with edit-mode runner tests first, then save the generated scene and verify it visually in play mode.

**Tech Stack:** Unity 6, ModularStoryFlow runtime/editor, ItemInteractionSystem runtime, Unity UI, Unity Test Framework, Unity scene/editor APIs.

---

## Chunk 1: Tests And Runtime Seams

### Task 1: Add edit-mode test assembly and graph-builder helpers

**Files:**
- Create: `Assets/Core/com.modular.storyflow/Tests/Editor/ModularStoryFlow.Tests.Editor.asmdef`
- Create: `Assets/Core/com.modular.storyflow/Tests/Editor/StoryFlowTestGraphBuilder.cs`
- Test: `Assets/Core/com.modular.storyflow/Tests/Editor/StoryFlowRunnerNodeCoverageTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Core/com.modular.storyflow/Tests/Editor/StoryFlowRunnerNodeCoverageTests.cs` with tests that build in-memory graphs covering dialogue, choice, branch, action, signal, delay, timeline, wait-for-signal, and end behavior.

- [ ] **Step 2: Run test to verify it fails**

Run: Unity EditMode tests for `StoryFlowRunnerNodeCoverageTests`
Expected: FAIL because the test assembly and graph builder helper do not exist yet.

- [ ] **Step 3: Write minimal implementation**

Create the test asmdef with references to `ModularStoryFlow.Runtime.Core`, `ModularStoryFlow.Runtime.Player`, `ModularStoryFlow.Runtime.Channels`, `ModularStoryFlow.Runtime.Save`, and NUnit test assemblies, then add `StoryFlowTestGraphBuilder.cs` to create graphs and nodes with serialized fields using `SerializedObject`.

- [ ] **Step 4: Run test to verify it passes**

Run: Unity EditMode tests for `StoryFlowRunnerNodeCoverageTests`
Expected: PASS, with deterministic coverage for every node type.

### Task 2: Add stage replay and cleanup tests

**Files:**
- Create: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryStageReplayTests.cs`
- Create: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryDebugCoordinatorTests.cs`
- Create: `Assets/Core/Scripts/Runtime/Story/BedroomStoryStageDefinitions.cs`
- Create: `Assets/Core/Scripts/Runtime/Story/BedroomStoryStageReplayer.cs`
- Create: `Assets/Core/Scripts/Runtime/Story/BedroomStoryDebugCoordinator.cs`

- [ ] **Step 1: Write the failing test**

Create replay tests for `FreshSpawn`, `ArrivalDialogueComplete`, `LaptopObjectiveActive`, `LaptopResolved`, `DoorReady`, and `TransitionCommitted`, plus restart/jump cleanup behavior.

- [ ] **Step 2: Run test to verify it fails**

Run: Unity EditMode tests for `BedroomStoryStageReplayTests`
Expected: FAIL because the stage definitions and replayer do not exist.

- [ ] **Step 3: Write minimal implementation**

Add a dedicated stage enum/definitions file, a replay helper, and a debug coordinator that exposes current stage, current node, pending wait kind, tracked variables, state-machine values, restart, jump-to-stage, and force-resolve commands for dialogue, choice, timeline, and signal waits.

- [ ] **Step 4: Run test to verify it passes**

Run: Unity EditMode tests for `BedroomStoryStageReplayTests`
Expected: PASS, including no stale pending-operation state after restart or jump.

### Task 3: Add regression and failure-mode tests

**Files:**
- Create: `Assets/Core/com.modular.storyflow/Tests/Editor/StoryFlowRunnerFailureModeTests.cs`

- [ ] **Step 1: Write the failing test**

Create regression tests for no entry node, missing required connection, mismatched request IDs, wrong signal while waiting, stage jump during active wait, and restart during active dialogue/choice/timeline/signal waits.

- [ ] **Step 2: Run test to verify it fails**

Run: Unity EditMode tests for `StoryFlowRunnerFailureModeTests`
Expected: FAIL because the failure-mode helpers and cleanup behavior are not fully implemented yet.

- [ ] **Step 3: Write minimal implementation**

Extend the replay/debug/runtime helpers only as needed so the required failure cases can be asserted deterministically.

- [ ] **Step 4: Run test to verify it passes**

Run: Unity EditMode tests for `StoryFlowRunnerFailureModeTests`
Expected: PASS.

## Chunk 2: Runtime Presentation And Bridges

### Task 4: Add subtitle and choice UI presenters

**Files:**
- Create: `Assets/Core/Scripts/Runtime/Story/BedroomStorySubtitlePresenter.cs`
- Create: `Assets/Core/Scripts/Runtime/Story/BedroomStoryChoicePresenter.cs`
- Create: `Assets/Core/Scripts/Runtime/Story/BedroomStoryUiRoot.cs`
- Create: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryUiPresenterTests.cs`

- [ ] **Step 1: Write the failing test**

Create `BedroomStoryUiPresenterTests.cs` to assert dialogue request rendering, choice rendering, disabled-choice suppression, cleanup on restart/jump, and fade/reset state behavior.

- [ ] **Step 2: Run test to verify it fails**

Run: the relevant new/updated EditMode tests
Expected: FAIL because the presenter classes do not exist.

- [ ] **Step 3: Write minimal implementation**

Implement runtime UI objects using Unity UI, chosen here as the project-consistent default because the existing interaction system already builds runtime overlay UI with Unity UI components. Match the locked subtitle tokens from the spec, including bottom-center lower-third styling, divider, italic dialogue body, fade transitions, and disabled choice rendering.

- [ ] **Step 4: Run test to verify it passes**

Run: the relevant EditMode tests
Expected: PASS.

### Task 5: Add interaction bridge and scene transition service

**Files:**
- Create: `Assets/Core/Scripts/Runtime/Story/BedroomStoryInteractionBridge.cs`
- Create: `Assets/Core/Scripts/Runtime/Story/BedroomStorySceneTransition.cs`
- Create: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryInteractionBridgeTests.cs`
- Modify: `Assets/Core/ItemInteractionSystem/Runtime/Core/InteractionDirector.cs` only if runtime hooks are strictly needed

- [ ] **Step 1: Write the failing test**

Add tests for door gating rules, laptop signal emission, exact prompt text `Leave (Go to Class)`, and canonical transition trigger behavior.

- [ ] **Step 2: Run test to verify it fails**

Run: relevant EditMode tests
Expected: FAIL because the bridge/transition service do not exist.

- [ ] **Step 3: Write minimal implementation**

Implement a story-aware interaction bridge that updates the tablet and `EnteranceDoor` interactables based on progression state, emits `intro.laptop.checked` and `intro.door.confirmed`, and routes the final action to `BedroomStorySceneTransition` for `Assets/Core/TestScenes/ClassroomArrivalScene.unity`. If the scene asset does not exist yet, Task 5 may validate against the serialized target path while Task 6 creates and wires the actual scene asset.

- [ ] **Step 4: Run test to verify it passes**

Run: relevant EditMode tests
Expected: PASS.

## Chunk 3: Editor Installer, Assets, And Scene Wiring

### Task 6: Build the bedroom story installer/editor generator

**Files:**
- Create: `Assets/Core/Scripts/Editor/Story/BedroomStoryInstaller.cs`
- Create: `Assets/Core/Scripts/Editor/Story/BedroomStoryAssetBuilder.cs`
- Create: `Assets/Core/Scripts/Editor/Story/BedroomStoryGraphBuilder.cs`

- [ ] **Step 1: Write the failing test**

Add a minimal editor test or smoke assertion that generated assets exist after installer execution, or verify via scripted setup command in editor.

- [ ] **Step 2: Run test to verify it fails**

Run: the editor smoke test or installer invocation
Expected: FAIL because the installer does not exist yet.

- [ ] **Step 3: Write minimal implementation**

Create an editor menu installer that owns generated content under `Assets/StoryFlowBedroomGenerated/` and:
- generates project config assets under a dedicated bedroom story folder
- creates variables, states, signals, timeline cue, actions, conditions
- builds the authored graph covering all 10 node types
- creates/updates `Assets/Core/TestScenes/ClassroomArrivalScene.unity`
- wires `StoryFlowPlayer`, timeline bridge, UI root, interaction bridge, stage replayer/debug coordinator into `BedRoomIntroScene`
- registers the graph and scene target cleanly

- [ ] **Step 4: Run test to verify it passes**

Run: installer invocation and smoke verification
Expected: generated assets and scene wiring exist without console errors.

### Task 7: Save and verify generated scene configuration

**Files:**
- Modify: `Assets/Core/TestScenes/BedRoomIntroScene.unity`
- Modify: `Assets/Core/TestScenes/ClassroomArrivalScene.unity`
- Modify: build settings via Unity editor APIs

- [ ] **Step 1: Write the failing test**

Capture a verification script/assertion that the scene has story bootstrap objects, configured interactables, and build-settings entry for `ClassroomArrivalScene`.

- [ ] **Step 2: Run test to verify it fails**

Run: installer/verification pass before scene save
Expected: FAIL because the scene is not yet wired.

- [ ] **Step 3: Write minimal implementation**

Execute the installer, persist both scenes, and ensure the active bedroom scene includes the story objects and the class scene is in build settings.

- [ ] **Step 4: Run test to verify it passes**

Run: verification pass again
Expected: PASS.

## Chunk 4: Visual QA And Final Verification

### Task 8: Run automated verification

**Files:**
- Test: `Assets/Core/com.modular.storyflow/Tests/Editor/StoryFlowRunnerNodeCoverageTests.cs`
- Test: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryStageReplayTests.cs`

- [ ] **Step 1: Run focused EditMode tests**

Run: Unity EditMode tests for the new story assemblies
Expected: PASS.

- [ ] **Step 2: Run broader validation**

Run: additional relevant EditMode tests or script validation commands if created
Expected: PASS with no compile errors or warnings related to the new story slice.

- [ ] **Step 3: Check Unity console cleanliness**

Confirm there are no new compile/runtime errors in the Unity console after installer execution.

### Task 9: Run visual play-mode QA and grounding

**Files:**
- Reference: `docs/superpowers/reference/story-subtitle-target.html`
- Create: `docs/superpowers/reference/story-subtitle-target.png`

- [ ] **Step 1: Enter play mode in `Assets/Core/TestScenes/BedRoomIntroScene.unity`**

- [ ] **Step 2: Verify fresh-play story path**

Confirm the player can move, see the opening subtitle lines, resolve laptop interaction, and unlock the door.

- [ ] **Step 3: Verify stage jumps**

Jump to each canonical stage and confirm truthful progression state.

- [ ] **Step 4: Capture screenshots**

Export `docs/superpowers/reference/story-subtitle-target.png` from the approved HTML reference, then use Unity screenshots to compare subtitle framing, divider, italics, prompt text, and door interaction against the design target.

- [ ] **Step 5: Fix visual/runtime mismatches and re-run**

Iterate until subtitle grounding and interaction flow are correct.

- [ ] **Step 6: Re-check console after play-mode QA**

Confirm there are no new runtime errors after entering/exiting play mode and using stage jumps.
