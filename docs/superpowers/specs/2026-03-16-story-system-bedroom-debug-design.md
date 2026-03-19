# Story System Bedroom Debug Design

## Goal

Turn `Assets/Core/TestScenes/BedRoomIntroScene.unity` into a fully testable story-system vertical slice that validates every runtime-capable `ModularStoryFlow` node type in one coherent playable flow, with deterministic stage skipping, working interaction gates, and subtitle presentation visually matched to the provided reference.

## Project Context

- The project already includes the runtime node set in `Assets/Core/com.modular.storyflow/Runtime/Core/Graph/Nodes/StoryNodes.cs`: `Start`, `End`, `Dialogue`, `Choice`, `Branch`, `Action`, `Signal`, `Timeline`, `Wait For Signal`, and `Delay`.
- The active scene is `Assets/Core/TestScenes/BedRoomIntroScene.unity` and it already contains interactables with `ItemInteraction.InteractableItem` plus `SelectableOutline`.
- The bedroom scene already has an `EnteranceDoor` object, existing outline support, and no in-scene `StoryFlowPlayer` object yet.
- The current story package includes a minimal sample overlay in `Assets/Core/com.modular.storyflow/Samples~/QuickStart/Runtime/QuickStartOverlay.cs`, but no production subtitle presentation matching the target screenshot.
- The repo is not currently a git repository in this workspace, so the design assumes local spec/planning artifacts will be written without a commit step unless the project is later attached to git.

## Design Principles

1. Validate runtime truth, not editor-only setup.
2. Exercise every node type in authored flow and in automated runner tests.
3. Make skip/debug behavior semantically equivalent to completing prior stages in order.
4. Reuse existing interaction and scene patterns where possible instead of inventing parallel systems.
5. Treat visual QA as a first-class requirement: subtitle layout, prompt wording, and interaction timing must be grounded in screenshots and play-mode verification.

## Runtime Story Design

### Authored flow

The bedroom intro graph should stay immersive while intentionally covering all runtime nodes.

Suggested authored flow:

1. `Start`
2. `Action` sets initial variables/state such as `intro.started = true`, `objective = bedroom_intro`, and player progression state machine to `BedroomArrival`
3. `Dialogue` for self-talk on spawn:
   - “what day is it today?”
   - brief pause and environmental reflection
4. `Delay` for a short breath/settling beat before the next thought
5. `Dialogue` guiding the player toward the laptop and class objective
6. `Choice` to express mood or intent, such as “Check the laptop first”, “Look around a little more”, or “Head out once ready”
7. `Branch` evaluating state/variables so optional exploration routes can feed back into the main goal
8. `Signal` to notify gameplay systems that the laptop/room exploration phase is active
9. `Wait For Signal` that completes when a gameplay bridge reports the prerequisite interaction done (for example laptop checked, or debug-force-advance invoked)
10. `Timeline` node for a short class-prep or objective-confirmation beat; if no authored cinematic asset exists, use a lightweight timeline cue bridge that still exercises request/resolve behavior
11. `Action` updates progression state to `DoorReady`, enables the leave interaction option, and records skip checkpoint data
12. `Dialogue` confirming the player is now ready to leave
13. Door interaction emits the `intro.door.confirmed` external signal, and the graph resumes only through the canonical `Wait For Signal` node
14. Final `Action` triggers scene transition to the class scene and marks state as `TransitioningToClass`
15. `End`

This authored graph should include at least one optional branch path and one unavailable/disabled choice case so both availability logic and alternate routing are exercised in a realistic context.

### Canonical exit path

The door exit path must use one authored structure only:

1. `Action` marks the intro as door-ready and exposes the door interaction.
2. `Dialogue` gives the final “ready to leave” line.
3. `Wait For Signal` listens for the `intro.door.confirmed` external signal.
4. `Action` performs the scene-transition request.
5. `End`

Normal play, forced wait resolution, and stage replay must all target this same path. There should be no alternate direct-resume route around the wait node.

### Node coverage mapping

- `Start`: graph entry.
- `Dialogue`: inner monologue, objective reminder, ready-to-leave confirmation.
- `Choice`: mood/exploration decision.
- `Branch`: route based on prior choice or objective-completion variable/state.
- `Action`: set variables/states, enable interactables, trigger scene transition, store checkpoints.
- `Signal`: notify interaction/gameplay bridges.
- `Timeline`: request and resolve a short authored cue.
- `Wait For Signal`: wait for laptop completion and/or door confirmation.
- `Delay`: pacing beat between monologues.
- `End`: successful graph completion after scene-transition handoff.

## Systems To Add Or Adapt

### 1. Story scene bootstrap

Add a scene-local story bootstrap in `BedRoomIntroScene` that wires:

- `StoryFlowPlayer`
- project config asset
- the bedroom intro graph asset
- channel-driven UI presenter(s)
- interaction-to-story bridge(s)
- debug/stage-skip controls

The bootstrap should be explicit and discoverable in-scene so runtime debugging does not depend on hidden sample setup.

### 2. Subtitle presenter

Create a dedicated runtime subtitle presenter for the bedroom story slice, using UI Toolkit or another project-consistent UI method, but with the following locked visual requirements:

- centered speaker name above body text
- thin horizontal white divider between speaker and body
- italic dialogue body
- large readable white text
- dark translucent lower-third backing rather than a hard opaque panel
- centered alignment and compact cinematic spacing similar to the provided screenshot

The presenter should subscribe to `DialogueRequests` and `AdvanceCommands` through story channels, support auto-advance and manual continue, and degrade safely when speaker or body is absent.

#### Locked subtitle tokens

Use these exact implementation tokens unless runtime constraints force a documented deviation:

- Reference source: the user-provided chat screenshot from this conversation, normalized into `docs/superpowers/reference/story-subtitle-target.html` for a repo-local implementation reference. During implementation, also export a screenshot capture to `docs/superpowers/reference/story-subtitle-target.png` before final QA so Unity screenshots can be compared against a stable image file.
- Anchoring: bottom-center lower third, centered horizontally, with the container anchored approximately 8% above the bottom edge of the game view.
- Max content width: 72% of the viewport width, capped at roughly 980 px on desktop.
- Backing panel: black at 55-65% opacity with soft edges; it should read as a band, not a hard boxed card.
- Speaker line: non-italic sans-serif, medium-to-semi-bold, white, centered, target size 30-34 px at 1920x1080.
- Divider: 1 px white line at roughly 80-90% opacity spanning the text column width.
- Body line: white, centered, italic for all spoken dialogue lines, target size 26-32 px at 1920x1080, line-height 1.25-1.35.
- Text shadow: soft dark shadow only, enough to preserve readability over the scene.
- Motion: fade in/out only; target duration 0.12-0.2 seconds. No typewriter effect, bounce, or slide motion.
- Safe area: the full subtitle band must remain inside mobile/TV-safe margins when scaled down.

### 3. Choice presenter

Add a matching choice UI surface that can:

- show the prompt and available options
- visibly disable unavailable options when a choice is shown but not selectable
- route selection back through `ChoiceSelections`
- coexist with the subtitle presentation without conflicting layout

It does not need to mimic the reference screenshot exactly, but it should feel stylistically compatible with the subtitle layer.

Disabled choices must follow one rule set end-to-end:

- unavailable options remain visible when the authored node intends them to be shown
- unavailable options render in a disabled visual state
- unavailable options cannot be focused, highlighted as valid picks, or submitted
- unavailable options must never emit `ChoiceSelections`
- if the UI layer supports secondary helper text, it may show a locked reason, but locked-reason copy is optional and not required for this slice

### 4. Bedroom interaction bridge

Add a runtime bridge between `ItemInteraction.InteractableItem` and story signals/state. Responsibilities:

- listen for item option invocations for the laptop and `EnteranceDoor`
- gate whether the door option is visible/enabled based on story progression
- publish the appropriate story external signals when gameplay interaction completes
- optionally update prompt labels at runtime so the door specifically shows `Leave (Go to Class)` when the story is ready

The bridge must preserve existing outline/focus behavior by reusing the current interaction components rather than replacing them.

The door gating contract is fixed as follows:

- before `DoorReady`, `EnteranceDoor` may still outline on focus, but the leave option is hidden and the prompt text is not shown
- after `DoorReady`, the top-level visible interaction option becomes `Leave (Go to Class)`
- once visible, the option is enabled and invoking it emits the `intro.door.confirmed` external signal exactly once per successful interaction
- there is no pre-ready rejection click path for this slice; the user should not be able to click a visible-but-blocked leave action

### 5. Scene transition action path

Introduce a clean scene-transition path at the end of the story flow. Because no existing runtime scene-loading pattern was found for this slice, the design should add one focused component that receives a story-driven event/action and performs the class-scene load.

Requirements:

- transition only after prerequisite nodes/stages are completed
- no silent load on graph start
- explicit target scene configuration
- easy to trigger from both normal play and debug replay path

Target scene for this slice: `Assets/Core/TestScenes/ClassroomArrivalScene.unity`. If the asset does not already exist, implementation should create it and add it to build settings for verification.

### 6. Skip/debug harness

Add a debug surface specifically for story QA that can:

- show current stage/checkpoint
- jump to a named stage
- replay prior stages in sequence instead of directly mutating end-state only
- optionally force-resolve waits (timeline completion, external signal, continue)
- restart the story cleanly
- expose current node, pending wait type, key variables, and current state machine values for debugging

Implementation requirement: “skip to stage N” must call the same progression logic that a normal playthrough would use for prior stages wherever practical, so history, notifications, and gating conditions remain truthful. Where direct state hydration is unavoidable, the harness must still recreate the same externally visible story state as sequential completion.

Supported canonical stages for this slice are milestone-based, not node-index-based:

- `FreshSpawn`: scene loaded, graph not yet advanced beyond initial bootstrap, no objective completed
- `ArrivalDialogueComplete`: opening self-talk and delay beat have completed; objective to check the laptop is active
- `LaptopObjectiveActive`: choice/branch content is available and the story is waiting for laptop-related completion
- `LaptopResolved`: laptop interaction signal has been processed, optional branch content has been normalized back into the main path, timeline beat may run or has just completed depending on configuration
- `DoorReady`: leave interaction is visible and enabled on `EnteranceDoor`, final pre-exit dialogue has completed, story is waiting on `intro.door.confirmed`
- `TransitionCommitted`: door-confirm signal received, scene transition action has fired, graph is ending or ended

Replay semantics:

- stages represent canonical business milestones, not exact previous node cursors
- when replaying to a stage, the harness may choose the canonical authored branch rather than reproducing every optional branch variant
- replay must still apply all required variable, state, history, and gating side effects in sequence so the destination stage is behaviorally equivalent to normal play

## Data Model And Progression Design

### Variables

Add only the minimal variables needed for deterministic progression and node coverage, for example:

- `intro.started` (bool)
- `intro.checkedLaptop` (bool)
- `intro.optionalExploreCount` (int)
- `intro.selectedMood` or `intro.path` (string)
- `intro.readyToLeave` (bool)

### State machine

Add one focused story state machine for the intro progression, such as:

- `BedroomArrival`
- `InvestigatingMorning`
- `LaptopResolved`
- `DoorReady`
- `TransitioningToClass`

Branches and UI gates should prefer state/variable reads from the runner rather than duplicate booleans scattered in MonoBehaviours.

### Signals

Reserve explicit signal definitions for:

- `intro.laptop.checked`
- `intro.door.confirmed`
- `intro.debug.forceAdvance`
- `intro.timeline.completed`
- `intro.timeline.cancelled`

Signal naming should be stable and gameplay-readable so both authored graph connections and runtime debug tools remain understandable.

## Automated Verification Design

### Edit-mode tests for runner behavior

Add focused tests for `StoryFlowRunner` that prove:

- each node type executes without null-path regressions
- dialogue nodes request advance and continue correctly
- choice nodes accept valid selections and reject invalid/unavailable selections
- branch nodes route correctly from variable/state conditions
- action nodes mutate variable/state stores as expected
- signal nodes publish signal payloads
- delay nodes unblock after `Tick`
- wait-for-signal nodes resume only on matching signals
- timeline nodes route completed vs cancelled correctly
- end nodes complete the graph

To make these tests deterministic, the implementation should add lightweight test seams around graph construction and event capture rather than relying on scene objects. Time-based waits can be advanced by explicit `Tick(float)` calls, and timeline/dialogue/choice requests should be captured from runner events and resolved using their emitted request payloads.

### Stage replay tests

Add tests around the skip/debug orchestration to prove:

- jumping to `ArrivalDialogueComplete`, `LaptopObjectiveActive`, `LaptopResolved`, and `DoorReady` reproduces prior progression side effects in order
- history is populated in sequence rather than only the destination node
- story waits are resolved through supported command paths
- door-ready gating is true only after the prior prerequisite stage replay finishes
- restart or stage-jump during an active wait clears stale UI and leaves no orphaned pending operation handlers behind

### Regression tests for failure modes

Add targeted tests for likely breakages:

- no graph entry node
- missing connection on required port
- mismatched request IDs
- signal mismatch while waiting
- stage jump while another wait is still active
- restart while dialogue, choice, timeline, or signal waits are active

## Visual QA Design

Visual QA is required for completion, not optional polish.

### Subtitle QA checklist

- speaker name is centered and visually separated from the body
- divider thickness and width read like the reference image
- body is italic and large enough to match the cinematic feeling
- lower-third backing is dark/translucent without hiding the scene excessively
- layout works on the target camera framing in the bedroom
- multiline dialogue wraps cleanly and remains centered

### Interaction QA checklist

- `EnteranceDoor` outlines correctly on focus
- prompt appears only when the player is within interaction range and story state allows it
- prompt text is exactly `Leave (Go to Class)` when ready
- interacting before readiness is prevented or redirected intentionally
- interacting after readiness progresses the story and scene transition path correctly

### Runtime flow QA checklist

- fresh playthrough reaches class transition end-to-end
- every authored optional branch remains recoverable back into the main objective
- skip to each stage produces the same visible UI/gameplay state as a normal playthrough at that stage
- no stuck waits, duplicate prompts, or stale dialogue remain onscreen after transitions
- restart or jump during an active subtitle/choice/timeline state clears the presentation layer before the new stage becomes active

## Architecture Boundaries

To keep the implementation understandable and testable, use these boundaries:

- **Story runtime assets**: graph, variables, states, conditions, actions, signals
- **Scene bootstrap**: scene wiring and initialization only
- **Presentation**: subtitle and choice UI, channel-driven only
- **Gameplay bridge**: converts item interaction events to story signals and gating updates
- **Transition service**: loads the next scene when story progression requests it
- **QA/debug tools**: stage skipping, status reporting, forced wait resolution
- **Checkpoint ownership**: canonical stage definitions live in a dedicated debug coordinator; runner variables/states remain the source of truth for gameplay progression; debug checkpoint metadata is transient scene-local state and must not be written into save data unless save/load is being explicitly tested
- **Tests**: runner behavior and stage replay logic

No one class should own story execution, UI, interaction gating, and scene loading at the same time.

## Risks And Mitigations

### Risk: skip path diverges from real play path

Mitigation: centralize stage progression helpers and test replay order explicitly.

### Risk: scene UI looks correct in isolation but not over actual gameplay framing

Mitigation: require play-mode screenshots from the bedroom scene for final subtitle approval.

### Risk: interaction system and story system fight over prompt visibility

Mitigation: keep story bridge responsible for only option visibility/enabled state and leave focus/outline to the existing interaction system.

### Risk: timeline node is under-tested if content is missing

Mitigation: create a minimal cue/bridge that still exercises request and resolution pathways, even if the cinematic content is lightweight.

## Success Criteria

The work is successful when:

1. Every runtime-capable story node type is exercised in automated tests and in the authored bedroom intro slice.
2. The player can start in the bedroom, move around, receive self-talk subtitles, complete prerequisite interactions, and leave through `EnteranceDoor` into class.
3. The subtitle style visually matches the provided screenshot closely enough that speaker placement, divider, italics, spacing, and backing all read as the same design language.
4. A debug operator can jump to any supported stage and get the same effective progression state as if earlier stages had been completed in sequence.
5. The full flow works in play mode without stuck waits, broken prompts, or invalid scene transitions.
