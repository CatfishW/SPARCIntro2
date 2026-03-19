# Look Option Dialogue Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Look` interaction options emit story-flow subtitle dialogue with speaker `"You"`, populate unique look lines for bedroom items, and preserve the existing bedroom story progression.

**Architecture:** Add generic look-dialogue metadata and a story-dialogue emission path to `ItemInteractionSystem.Runtime`, then wire the bedroom scene items with unique lines. Keep progression logic in the bedroom story bridge unchanged so look dialogue remains reusable across future scenes without affecting graph state unless explicitly authored later.

**Tech Stack:** Unity 6, C#, ItemInteractionSystem runtime, ModularStoryFlow runtime channels/events, Unity Test Framework, YAML scene serialization

---

## File Map

- Modify: `Assets/Core/ItemInteractionSystem/Runtime/ItemInteractionSystem.Runtime.asmdef`
  - Add the minimal story-flow runtime assembly references needed for dialogue payload/channel use.
- Modify: `Assets/Core/ItemInteractionSystem/Runtime/Core/InteractableItem.cs`
  - Add generic look-dialogue fields and the runtime emission path for `Look` option invocations.
- Potentially modify: `Assets/Core/Scripts/Runtime/Story/BedroomStoryInteractionBridge.cs`
  - Only if needed to keep bedroom progression/session flow aligned after generic interaction changes.
- Modify: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryUiPresenterTests.cs`
  - Keep existing subtitle behavior coverage intact if any API signatures change.
- Add or modify: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomLookDialogueTests.cs`
  - Cover generic `Look` -> `StoryDialogueRequest` behavior with speaker `"You"`.
- Modify: `Assets/Core/TestScenes/BedRoomIntroScene.unity`
  - Populate unique `lookDialogueBody` values for every current bedroom interactable with a `Look` option.

## Chunk 1: Generic Look Dialogue Runtime

### Task 1: Add failing test for look-option dialogue emission

**Files:**
- Create: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomLookDialogueTests.cs`
- Modify: `Assets/Core/ItemInteractionSystem/Runtime/Core/InteractableItem.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void Look_option_emits_story_dialogue_with_you_as_default_speaker()
{
    var itemObject = new GameObject("Lamp");
    var item = itemObject.AddComponent<InteractableItem>();
    item.lookDialogueSpeaker = string.Empty;
    item.lookDialogueBody = "The bulb is cold. Nobody turned this on last night.";

    var option = item.FindOption("look");
    Assert.That(option, Is.Not.Null);

    StoryDialogueRequest received = null;
    item.StoryDialogueRequested += request => received = request;

    item.InvokeOption(null, option);

    Assert.That(received, Is.Not.Null);
    Assert.That(received.SpeakerDisplayName, Is.EqualTo("You"));
    Assert.That(received.Body, Is.EqualTo("The bulb is cold. Nobody turned this on last night."));

    Object.DestroyImmediate(itemObject);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `Unity EditMode test: ModularStoryFlow.Tests.BedroomLookDialogueTests.Look_option_emits_story_dialogue_with_you_as_default_speaker`
Expected: FAIL because `InteractableItem` does not yet expose look-dialogue fields/events.

- [ ] **Step 3: Write minimal implementation**

Implement in `InteractableItem`:
- serializable fields for `lookDialogueSpeaker` and `lookDialogueBody`
- an event for emitted story dialogue payloads
- logic in `InvokeOption(...)` that only fires for `look` and non-empty dialogue bodies
- default speaker fallback to `"You"`

- [ ] **Step 4: Run test to verify it passes**

Run the single EditMode test again.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Core/ItemInteractionSystem/Runtime/ItemInteractionSystem.Runtime.asmdef Assets/Core/ItemInteractionSystem/Runtime/Core/InteractableItem.cs Assets/Core/com.modular.storyflow/Tests/Editor/BedroomLookDialogueTests.cs
git commit -m "feat: emit story dialogue from look interactions"
```

### Task 2: Add failing test that inspect still behaves independently

**Files:**
- Modify: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomLookDialogueTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void Inspect_option_does_not_emit_look_dialogue()
{
    var itemObject = new GameObject("Desk");
    var item = itemObject.AddComponent<InteractableItem>();
    item.lookDialogueBody = "A cramped little desk.";

    var inspect = item.FindOption("inspect");
    Assert.That(inspect, Is.Not.Null);

    StoryDialogueRequest received = null;
    item.StoryDialogueRequested += request => received = request;

    item.InvokeOption(null, inspect);

    Assert.That(received, Is.Null);

    Object.DestroyImmediate(itemObject);
}
```

- [ ] **Step 2: Run test to verify it fails or is red for the right reason**

Run the single test.
Expected: FAIL if the implementation accidentally emits for all options.

- [ ] **Step 3: Adjust implementation minimally**

Restrict dialogue emission to `option.id == "look"` only.

- [ ] **Step 4: Run both look-dialogue tests**

Expected: both PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Core/ItemInteractionSystem/Runtime/Core/InteractableItem.cs Assets/Core/com.modular.storyflow/Tests/Editor/BedroomLookDialogueTests.cs
git commit -m "fix: keep inspect separate from look dialogue"
```

## Chunk 2: Bedroom Scene Wiring

### Task 3: Add failing scene-data regression for configured bedroom look dialogue

**Files:**
- Modify: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryInstallerTests.cs`
- Modify: `Assets/Core/TestScenes/BedRoomIntroScene.unity`

- [ ] **Step 1: Write the failing test**

Create a scene-data test that loads the bedroom scene asset and asserts every current `InteractableItem` with a `look` option has a non-empty `lookDialogueBody` and `lookDialogueSpeaker == "You"` or empty-for-default.

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL because scene items are not yet populated.

- [ ] **Step 3: Populate unique lines in the scene**

Update each existing bedroom interactable that exposes `Look` with unique authored text.

- [ ] **Step 4: Run the scene-data test again**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Core/TestScenes/BedRoomIntroScene.unity Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryInstallerTests.cs
git commit -m "feat: add authored look dialogue to bedroom items"
```

## Chunk 3: Story Flow Safety Verification

### Task 4: Re-run existing bedroom story regressions

**Files:**
- Test only: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryRuntimeBootstrapTests.cs`
- Test only: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryUiPresenterTests.cs`
- Test only: `Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryInstallerTests.cs`

- [ ] **Step 1: Run focused story-flow regressions**

Run:
- bootstrap type test
- subtitle/choice presenter tests
- existing generated-asset integrity and runner tests that we rely on for the current debug context

- [ ] **Step 2: Verify there are no new failures**

Expected: current approved UI/bootstrap tests remain green; known generated-asset corruption regressions stay consistent unless separately fixed.

- [ ] **Step 3: Commit if any test-only adjustments were needed**

```bash
git add relevant-test-files
git commit -m "test: preserve bedroom story flow coverage"
```

## Chunk 4: Live Scene Verification

### Task 5: Verify progression still works in `BedRoomIntroScene`

**Files:**
- Runtime verification only in `Assets/Core/TestScenes/BedRoomIntroScene.unity`

- [ ] **Step 1: Enter play mode in the current bedroom scene**

Expected: opening subtitle still appears in the real Unity Game view.

- [ ] **Step 2: Trigger at least one `Look` option and verify the item-specific line appears with speaker `You`**

Expected: subtitle updates without breaking the main interaction UI.

- [ ] **Step 3: Continue through laptop progression**

Expected: laptop-related story flow still advances.

- [ ] **Step 4: Verify door-ready state and leave option activation**

Expected: door label/visibility still matches progression.

- [ ] **Step 5: Verify classroom transition still triggers**

Expected: end-to-end bedroom flow still works.

- [ ] **Step 6: Commit final integrated change**

```bash
git add Assets/Core/ItemInteractionSystem/Runtime/ItemInteractionSystem.Runtime.asmdef Assets/Core/ItemInteractionSystem/Runtime/Core/InteractableItem.cs Assets/Core/TestScenes/BedRoomIntroScene.unity Assets/Core/com.modular.storyflow/Tests/Editor/BedroomLookDialogueTests.cs Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryInstallerTests.cs
git commit -m "feat: add reusable look dialogue to item story flow"
```

Plan complete and saved to `docs/superpowers/plans/2026-03-17-look-option-dialogue-implementation.md`. Ready to execute?
