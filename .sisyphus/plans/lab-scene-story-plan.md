# LabScene Story + Implementation Plan

## Goal

Turn `Assets/LabScene.unity` from the current prototype-style lab checklist into a kid-friendly, authored vertical slice with:

1. explicit non-empty story content,
2. a CAP NPC using `Assets/SM_Peer_Agent_Mo/`,
3. simple branched dialogue,
4. a human-body inspection beat using `Assets/Core/Art/3D Casual Character/3D Characters Pro - Casual/Prefabs/Characters/Character_Basic.prefab`,
5. a shrink sequence using `Assets/Core/Art/Models/dna-lab-machine/`,
6. a simple UI Toolkit light-routing puzzle,
7. a rocket interaction using the existing item interaction system,
8. a final cutscene that ends on black.

## Architecture Decision

- Keep the existing **ModularStoryFlow + interaction bridge + scene transition** architecture.
- Stop treating Lab as a runtime-only prototype. Move it toward the **Bedroom authored asset path**:
  - generated/saved `StoryFlowProjectConfig`
  - generated/saved `StoryGraphAsset`
  - thin `LabStoryRuntimeBootstrap`
- Use the story graph only for **coarse progression gates**.
- Keep these responsibilities separate:
  - **Story graph**: progression and branch selection
  - **CAP/NPC controller**: animator state, reactions, optional motion
  - **Puzzle controller**: UI Toolkit minigame and success/failure outputs
  - **Cutscene controller**: control lock, fades, camera/sequence timing, ending black

## Story Tone Contract

- Audience: K-12
- Tone: friendly, curious, adventurous, never frightening
- Vocabulary: simple and concrete
- Dialogue length: usually 1-2 short sentences per beat
- Educational framing: the human body is presented as a safe learning mission, not horror or surgery

## Story Summary

The player wakes in a lab and meets **CAP**, a cheerful peer-guide robot/scientist helper. CAP explains that a tiny explorer mission is about to begin: the team will shrink, board a mini rocket, and travel into the human body through the mouth to learn how the digestive system works.

Before launch, the player must:

1. talk with CAP and choose a brave/curious response,
2. inspect the body model resting on the sci-fi lab machine,
3. power the shrinking machine by solving a simple light-routing puzzle,
4. shrink together with CAP,
5. board the rocket,
6. watch the final launch-and-entry cutscene,
7. end in black after entering the mouth.

## Player-Facing Plot Beats

### Beat 1 — Arrival and First Contact

- Player spawns in the lab.
- CAP is visible and idle near the central machines.
- CAP has a clear prompt like `Talk to CAP`.
- Intro subtitle/dialogue establishes:
  - this is a science mission,
  - the body model is the destination preview,
  - the shrink machine needs power first.

### Beat 2 — CAP Conversation

CAP introduces the mission in easy language:

> "Hi! I'm CAP. Today we're taking a super tiny science trip. We will shrink, ride a mini rocket, and explore how food travels through the body."

Player gets one simple branch set:

- **Curious**: `How small will we be?`
- **Brave**: `Let's do it!`
- **Careful**: `Is it safe?`

All branches converge. CAP responds supportively and unlocks the next objective.

### Beat 3 — Body Inspection

- Player goes to the **sci-fi lab machine** with the body model.
- The body model is framed as a learning preview.
- Interaction opens a short inspect presentation with 3 hotspot-style facts or step-through panels:
  - mouth: `This is where the journey starts.`
  - esophagus: `This tube carries food down to the stomach.`
  - stomach/intestine: `Food is broken down, and nutrients are collected later in the intestines.`

The inspect experience is lightweight and modal, not a giant subsystem.

### Beat 4 — Puzzle Setup

CAP says the shrink machine has no power and the player must help bend beams of light to reconnect the energy circuit.

Example line:

> "The shrink machine is almost ready, but the light power path is broken. Can you help me guide the beams into the energy nodes?"

### Beat 5 — Light-Bending Puzzle

Puzzle concept:

- one-screen UI Toolkit minigame,
- child-friendly,
- deterministic,
- 3 beam emitters / mirrors / targets max,
- rotate mirrors or swap beam pieces until all receivers glow,
- clear visual feedback and optional hint label.

Success line:

> "Great job! The machine is powered up."

Failure/reset experience should be soft and encouraging:

> "Almost there. Try turning the mirror so the light reaches the glowing node."

### Beat 6 — Shrink Sequence

- Player interacts with `dna-lab-machine`.
- CAP joins the moment narratively.
- Prefer a safe illusion-based shrink implementation:
  - control lock,
  - machine lights,
  - fade/flash,
  - camera/position change,
  - optionally small-scale environmental repositioning,
  - no risky true-scale gameplay unless existing systems make it safe.

CAP line:

> "Deep breath. Tiny mode starting now!"

### Beat 7 — Rocket Boarding

- After shrink + puzzle completion, the rocket becomes the active interactable.
- Existing item interaction system is reused.
- Rocket option label becomes something like `Enter Rocket`.
- Before unlock, it should remain blocked with clear feedback.

Blocked feedback example:

> "We still need to power the machine and shrink before launch."

### Beat 8 — Final Cutscene and End

The rocket interaction triggers the final cutscene:

1. CAP confirms launch.
2. Rocket door closes.
3. brief countdown / glow / motion.
4. cut to the mouth-entry approach.
5. enter darkness.
6. full black hold.

Final line before black:

> "Mission start. See you inside!"

End state:

- screen fades to full black,
- game ends cleanly,
- no extra UI lingering.

## Progression States

New Lab progression should replace the current workstation/arm/server-rack checklist with authored states like:

- `FreshSpawn`
- `MeetCap`
- `BodyInspectionReady`
- `BodyInspected`
- `PuzzleReady`
- `PuzzleSolved`
- `ShrinkReady`
- `Shrunk`
- `RocketReady`
- `CutsceneCommitted`

## Story Signals

Proposed replacements/extensions for `LabStorySignals`:

- `lab.cap.talked`
- `lab.body.inspected`
- `lab.puzzle.started`
- `lab.puzzle.solved`
- `lab.shrink.confirmed`
- `lab.rocket.entered`
- `lab.cutscene.completed`

## Scene Object Contract

Prefer explicit serialized references over hierarchy-name lookup.

Key scene references to own from a Lab scene root / context:

- `StoryFlowPlayer`
- `LabStoryUiRoot`
- `LabStoryInteractionBridge`
- `LabStorySceneTransition`
- `StoryNpcRegistry`
- CAP `StoryNpcAgent`
- CAP animator / visual root
- body machine root
- body display root / anchor
- body character instance
- dna shrink machine interactable
- rocket interactable
- puzzle world trigger or console interactable
- puzzle `UIDocument` / `PanelSettings`
- cutscene controller / optional `PlayableDirector`

## Reuse from Existing Repo

- Authored asset generation pattern:
  - `Assets/Core/Scripts/Editor/Story/BedroomStoryAssetBuilder.cs`
  - `Assets/Core/Scripts/Editor/Story/BedroomStoryGraphBuilder.cs`
  - `Assets/Core/Scripts/Editor/Story/BedroomStoryInstaller.cs`
- Runtime bootstrap path that prefers saved assets:
  - `Assets/Core/Scripts/Runtime/Story/BedroomStoryRuntimeBootstrap.cs`
- NPC interaction:
  - `Assets/Core/Scripts/Runtime/Story/StoryNpcAgent.cs`
  - `Assets/Core/Scripts/Runtime/Story/StoryNpcRegistry.cs`
  - `Assets/Core/Scripts/Runtime/Story/StoryNpcStorySignalBridge.cs`
- Player control lock for modal UI/cutscenes:
  - `Assets/Core/Scripts/Runtime/Story/ClassroomPlayerControlLock.cs`
- UI Toolkit modal patterns:
  - `Assets/Core/Scripts/Runtime/Story/ClassroomBodyKnowledgeBookUi.cs`
  - `Assets/Core/Scripts/Runtime/Story/ClassroomBodyKnowledgeQuizUi.cs`
  - `Assets/Core/Scripts/Runtime/Story/ClassroomNpcFreeChatUi.cs`
- Cutscene black-fade pattern:
  - `Assets/Core/Scripts/Runtime/Story/ClassroomSceneIntroCutscene.cs`
  - `Assets/Core/Scripts/Runtime/Story/LabStorySceneTransition.cs`

## Planned File Groups

### New / extended authored asset pipeline

- `Assets/Core/Scripts/Editor/Story/LabStoryAssetBuilder.cs`
- `Assets/Core/Scripts/Editor/Story/LabStoryGraphBuilder.cs`
- extend `Assets/Core/Scripts/Editor/Story/LabStoryInstaller.cs`
- generated assets under a new folder such as `Assets/StoryFlowLabGenerated/`

### Runtime story orchestration

- slim down / refactor `Assets/Core/Scripts/Runtime/Story/LabStoryRuntimeBootstrap.cs`
- extend `Assets/Core/Scripts/Runtime/Story/LabStorySignals.cs`
- replace checklist-based `Assets/Core/Scripts/Runtime/Story/LabStoryInteractionBridge.cs`

### CAP + lab-specific controllers

- `Assets/Core/Scripts/Runtime/Story/LabCapConversationDirector.cs`
- `Assets/Core/Scripts/Runtime/Story/LabCapNpcController.cs`
- `Assets/Core/Scripts/Runtime/Story/LabSceneContext.cs`
- `Assets/Core/Scripts/Runtime/Story/LabBodyInspectionUi.cs`
- `Assets/Core/Scripts/Runtime/Story/LabShrinkSequenceController.cs`
- `Assets/Core/Scripts/Runtime/Story/LabLightPuzzleUi.cs`
- `Assets/Core/Scripts/Runtime/Story/LabFinalCutsceneController.cs`

## Puzzle Specification

- UI Toolkit runtime overlay
- one puzzle screen
- max 3-4 interactive pieces
- one clear solution
- visible goal nodes
- reset button
- close disabled until solved or explicitly cancelled if story allows it
- optional hint text using child-friendly language

## Verification Contract

### Happy path

1. Player spawns in LabScene.
2. CAP is present, animated, and interactable.
3. CAP dialogue presents at least one branch.
4. Completing CAP talk unlocks body inspection.
5. Body inspection completes and unlocks puzzle/shrink stage.
6. Puzzle opens and can be solved.
7. Solving puzzle unlocks shrink interaction.
8. Shrink sequence runs without breaking controls.
9. Rocket remains locked before prerequisites and unlocks after them.
10. Rocket interaction triggers final cutscene.
11. Final visual state is full black.

### Blocked path checks

1. Cannot use rocket before CAP conversation.
2. Cannot use rocket before puzzle solve.
3. Cannot bypass the shrink beat if it is required.
4. No dialogue branch should soft-lock progression.
5. New console errors must remain at zero.

## Safe Assumptions

- Implement as a **linear vertical slice** with one meaningful branch in CAP dialogue.
- Use illusion-based shrink rather than risky global rescaling.
- Keep body inspection educational and simple.
- Use UI Toolkit because the repo already has runtime UI Toolkit patterns.
- Reuse existing interaction and fade systems instead of inventing a new quest framework.

## First Implementation Slice

The first pass should deliver a fully playable golden path with simple art behavior and polished-enough text:

1. authored Lab story assets wired into the scene,
2. CAP in scene with idle animator and talk interaction,
3. body inspection modal,
4. puzzle modal,
5. shrink sequence,
6. rocket gate,
7. ending cutscene to black.

Polish like richer CAP movement, extra branches, extra visual effects, and advanced cinematics can come after the golden path works end to end.

## Executable Task Breakdown

### Task 1 — Author generated Lab story assets

**Goal**

- Create a Bedroom-style generated asset pipeline for Lab so the scene uses saved `StoryFlowProjectConfig` and `StoryGraphAsset` instead of only runtime-created graph data.

**Primary files**

- `Assets/Core/Scripts/Editor/Story/LabStoryAssetBuilder.cs`
- `Assets/Core/Scripts/Editor/Story/LabStoryGraphBuilder.cs`
- `Assets/Core/Scripts/Editor/Story/LabStoryInstaller.cs`
- generated assets under `Assets/StoryFlowLabGenerated/`

**QA**

- Tool: `read`, `lsp_diagnostics`, Unity asset generation via installer/menu or editor script path.
- Steps:
  1. confirm builder files compile cleanly,
  2. run the Lab installer/generation path,
  3. verify `StoryFlowLabGenerated` contains config, channels, graph, variable/state assets,
  4. verify graph/config asset paths are the same ones referenced by Lab bootstrap.
- Expected result:
  - saved Lab project config and graph exist on disk,
  - no new compile errors,
  - Lab is no longer dependent on runtime-only graph construction.

### Task 2 — Thin the Lab runtime bootstrap and wire authored assets

**Goal**

- Update Lab bootstrap to prefer persisted assets, keep channels consistent, and inject only orchestration/runtime wiring.

**Primary files**

- `Assets/Core/Scripts/Runtime/Story/LabStoryRuntimeBootstrap.cs`
- `Assets/Core/Scripts/Runtime/Story/LabStorySignals.cs`

**QA**

- Tool: `read`, `lsp_diagnostics`, Unity Play Mode boot, `unityMCP_read_console`.
- Steps:
  1. verify bootstrap contains saved asset path fields,
  2. verify persisted asset load path is used before fallback generation,
  3. enter Play Mode in `LabScene`,
  4. confirm Lab story player starts without runtime asset errors.
- Expected result:
  - `StoryFlowPlayer` gets a valid config/graph,
  - no missing-channel or missing-graph runtime errors,
  - session IDs remain available to other Lab controllers.

### Task 3 — Build Lab scene context and explicit serialized references

**Goal**

- Replace fragile hierarchy-path assumptions with one explicit scene-owned reference root for CAP, body machine, body anchor, shrink machine, puzzle trigger, rocket, and cutscene controller.

**Primary files**

- `Assets/Core/Scripts/Runtime/Story/LabSceneContext.cs`
- `Assets/LabScene.unity`

**QA**

- Tool: Unity scene inspection, `unityMCP_find_gameobjects`, `unityMCP_manage_scene`, `unityMCP_read_console`.
- Steps:
  1. inspect `LabScene` hierarchy,
  2. confirm one Lab scene context/root exists,
  3. verify all critical references are serialized and non-null,
  4. enter Play Mode and ensure no null-reference errors are emitted by scene setup.
- Expected result:
  - all progression-critical scene objects are explicitly wired,
  - no core beat depends on artist-renamable hierarchy paths.

### Task 4 — Integrate CAP NPC foundation

**Goal**

- Place CAP in LabScene using `SM_Peer_Agent_Mo`, with animator, interactable prompt, and `StoryNpcAgent` plumbing.

**Primary files**

- `Assets/LabScene.unity`
- `Assets/Core/Scripts/Runtime/Story/LabCapNpcController.cs`

**QA**

- Tool: Unity scene inspection, `unityMCP_manage_components`, `unityMCP_manage_scene`, `unityMCP_read_console`, Play Mode.
- Steps:
  1. verify CAP instance exists in scene,
  2. verify Animator + `StoryNpcAgent` + interactable setup are present,
  3. focus CAP in Play Mode,
  4. confirm prompt/options appear and no console errors fire.
- Expected result:
  - CAP idles correctly,
  - CAP can be targeted/interacted with,
  - CAP is usable as the first story gate.

### Task 5 — Implement CAP branched conversation

**Goal**

- Add the K12-friendly CAP dialogue branch and converge it cleanly into body-inspection progression.

**Primary files**

- generated graph assets under `Assets/StoryFlowLabGenerated/`
- `Assets/Core/Scripts/Runtime/Story/LabCapConversationDirector.cs`
- any related Lab story signal/bridge files

**QA**

- Tool: Play Mode, `unityMCP_read_console`, screenshot if useful.
- Steps:
  1. talk to CAP,
  2. verify at least one branch choice is presented,
  3. choose each branch in separate runs,
  4. confirm all branches converge to unlock the next beat,
  5. confirm no branch dead-ends the player.
- Expected result:
  - CAP dialogue is readable and age-appropriate,
  - progression unlocks after any valid branch choice.

### Task 6 — Implement body inspection beat

**Goal**

- Place `Character_Basic.prefab` on the sci-fi lab machine and provide a short inspect experience that teaches the route in simple language.

**Primary files**

- `Assets/LabScene.unity`
- `Assets/Core/Scripts/Runtime/Story/LabBodyInspectionUi.cs`
- bridge/context wiring files as needed

**QA**

- Tool: Play Mode, `unityMCP_manage_scene`, `unityMCP_read_console`.
- Steps:
  1. inspect the body machine,
  2. verify the body model is present and framed correctly,
  3. verify the inspect UI/modal opens,
  4. complete the inspect flow,
  5. confirm progression unlocks puzzle/shrink readiness.
- Expected result:
  - body inspection is understandable and non-empty,
  - completion raises the expected Lab progression signal/state.

### Task 7 — Implement UI Toolkit light puzzle

**Goal**

- Build the child-friendly one-screen light-routing minigame and hook its success into Lab progression.

**Primary files**

- `Assets/Core/Scripts/Runtime/Story/LabLightPuzzleUi.cs`
- any `UXML` / `USS` assets created for the puzzle
- scene wiring in `Assets/LabScene.unity`

**QA**

- Tool: Play Mode, `unityMCP_manage_ui`, `unityMCP_read_console`, `lsp_diagnostics`.
- Steps:
  1. open the puzzle from the intended trigger,
  2. verify the UI renders,
  3. solve the puzzle once,
  4. verify solved state unlocks the next beat,
  5. reopen/reset to confirm retry behavior works if supported.
- Expected result:
  - puzzle is deterministic and comprehensible,
  - puzzle success emits the correct Lab signal,
  - no UI Toolkit runtime errors occur.

### Task 8 — Implement shrink sequence

**Goal**

- Use the DNA machine to trigger a safe shrink beat with control lock, feedback, and post-shrink progression.

**Primary files**

- `Assets/Core/Scripts/Runtime/Story/LabShrinkSequenceController.cs`
- `Assets/LabScene.unity`

**QA**

- Tool: Play Mode, `unityMCP_read_console`, camera screenshots if needed.
- Steps:
  1. attempt shrink before prerequisites and confirm blocked feedback if intended,
  2. complete prerequisites,
  3. trigger shrink sequence,
  4. verify controls return cleanly afterward,
  5. verify rocket beat becomes available.
- Expected result:
  - shrink sequence completes without breaking player state,
  - progression advances and controls remain stable.

### Task 9 — Gate and implement rocket interaction

**Goal**

- Reuse the existing interaction system so the rocket stays locked until the player is ready, then exposes `Enter Rocket`.

**Primary files**

- `Assets/LabScene.unity`
- `Assets/Core/Scripts/Runtime/Story/LabStoryInteractionBridge.cs`
- related Lab scene context/controller files

**QA**

- Tool: Play Mode, `unityMCP_read_console`.
- Steps:
  1. try rocket interaction before CAP/puzzle/shrink completion,
  2. verify blocked feedback,
  3. complete all prerequisites,
  4. verify rocket option becomes enabled and interaction succeeds.
- Expected result:
  - rocket is cleanly gated,
  - existing interaction semantics are preserved.

### Task 10 — Implement final cutscene and black ending

**Goal**

- Trigger the final rocket sequence and end the game on full black.

**Primary files**

- `Assets/Core/Scripts/Runtime/Story/LabFinalCutsceneController.cs`
- optional timeline assets / cutscene assets
- `Assets/Core/Scripts/Runtime/Story/LabStorySceneTransition.cs` if extended

**QA**

- Tool: Play Mode, screenshots, `unityMCP_read_console`.
- Steps:
  1. trigger rocket entry on the happy path,
  2. verify cutscene starts immediately,
  3. verify control lock/fade behavior stays stable,
  4. verify final frame/state is full black.
- Expected result:
  - rocket entry commits the ending sequence,
  - the game ends on black with no lingering UI/errors.

### Task 11 — End-to-end regression verification

**Goal**

- Validate the full Lab golden path plus key blocked-path cases after all systems are wired.

**QA**

- Tool: Play Mode, `unityMCP_read_console`, targeted screenshots, `lsp_diagnostics` on all changed files.
- Steps:
  1. run full golden path from spawn to black ending,
  2. rerun with blocked-path attempts on rocket/puzzle/shrink,
  3. verify each changed script is diagnostics-clean,
  4. confirm Unity console stays clean of new errors.
- Expected result:
  - the scene is fully completable,
  - invalid order is blocked cleanly,
  - no new compile/runtime regressions remain.

## Atomic Commit Strategy

If commits are requested later, keep them aligned with verification boundaries:

1. `author lab story assets and bootstrap references`
2. `add cap npc and branched lab conversation gate`
3. `add body inspection and puzzle-driven shrink flow`
4. `gate rocket entry and add final black-ending cutscene`
5. `harden lab flow verification and fix regression gaps`
