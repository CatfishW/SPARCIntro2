# Life Is Strange Style Item Interaction System

This package gives you a self-contained interaction flow for Unity 6000.3.6f1 projects:

- center-screen focus detection via raycast
- selected item outlining
- direct item options bound to 4 slots (Top / Left / Right / Bottom)
- 3D inspection with mouse rotation and zoom
- blurred background during inspection using a captured frame + blur shader
- low-coupling event hooks for story flow integration
- URP-friendly implementation with no hard dependency on renderer features

## Main runtime components

### InteractionDirector
The orchestration hub. It owns focus detection, prompt display, and inspection state.

### InteractableItem
Add this to any item you want the player to look at. Give it a `storyId`, visible label, and option list.

### SelectableOutline
Adds a runtime-generated outline shell around the focused item.

### DefaultInteractionInputSource
Default keyboard and mouse input implementation.

### InteractionRouteRelay
Optional no-code bridge that listens for `(storyId, optionId)` pairs and fires UnityEvents.

## Quick setup

1. Unzip this folder into either:
   - `Packages/com.openai.life-is-strange-item-interaction`
   - or anywhere inside `Assets/`
2. In Unity, add an `InteractionDirector` to your scene.
3. Assign your gameplay camera if needed.
4. Add `InteractableItem` to any object with a collider.
5. Add `SelectableOutline` to the same object.
6. Configure the options list on the item.
7. Press Play.

## Editor shortcuts

From the Unity top menu:

- `GameObject > Life Is Strange Interaction > Install Interaction Director`
- `GameObject > Life Is Strange Interaction > Make Selected Interactable`

## Default controls

- `1` = Top option
- `2` = Left option
- `3` = Right option
- `4` = Bottom option
- `LMB hold + drag` = rotate inspected item
- `Mouse Wheel` = zoom inspected item
- `RMB` or `Esc` = close inspection

You can replace the input implementation by writing your own component that inherits `InteractionInputSource`.

## Story flow integration

The cleanest integration path is:

- set a stable `storyId` on every `InteractableItem`
- set stable option ids such as `look`, `inspect`, `read`, `take`, `use`
- subscribe to `InteractionDirector.OptionInvoked`

Example:

```csharp
using ItemInteraction;
using UnityEngine;

public class StoryFlowBridge : MonoBehaviour
{
    [SerializeField] private InteractionDirector director;

    private void OnEnable()
    {
        director.OptionInvoked += HandleInvocation;
    }

    private void OnDisable()
    {
        director.OptionInvoked -= HandleInvocation;
    }

    private void HandleInvocation(InteractionInvocation invocation)
    {
        Debug.Log($"Story event: {invocation.StoryId} / {invocation.OptionId}");
        // Forward into your own story graph, dialogue runner, quest system, etc.
    }
}
```

For designers who prefer no code, add `InteractionRouteRelay` and define routes in the Inspector.

## Inspection authoring notes

If an item should inspect differently from its world model:

- assign a custom `customInspectionPrefab` in `defaultInspection`
- or enable `useInspectionOverride` on a specific option and set an option-specific presentation

If you leave the prefab empty, the system automatically clones the visual hierarchy from the item and strips it down to render-only components for inspection.

## Design choices

- **High cohesion**: focus logic, prompt UI, inspection UI, outline rendering, and story routing are separated into dedicated classes.
- **Low coupling**: the story flow system only needs the `storyId` and `optionId` payload, or a simple relay.
- **Self-contained**: no imported packages, no custom editor windows required, and no external post-process stack dependency.

## Notes

- Items need colliders for focus raycasts.
- The inspection background is a captured frame blurred in UI, so it stays stable while the inspected object rotates.
- The preview object is rendered by a separate off-screen camera into a RenderTexture.
