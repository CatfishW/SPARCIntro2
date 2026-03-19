# Quick Start Sample

This sample gives you a lightweight end-to-end setup without requiring any prefabs or UI assets up front.

## What it includes

- `QuickStartOverlay` — a tiny IMGUI overlay that listens to Story Flow channels and renders dialogue + choices.
- `QuickStartPulsePlayableAsset` — a minimal custom `PlayableAsset` used to prove timeline/PlayableDirector integration.
- `QuickStartSampleInstaller` — a menu command that generates a sample graph, variables, actions, conditions, signals, timeline cue binding, and scene objects.

## Install

1. Import the **Quick Start** sample from the Package Manager.
2. Run **Tools > Modular Story Flow > Samples > Install Quick Start**.
3. Enter Play Mode.
4. Use the overlay in the top-left corner to progress through the sample story.

## What the generated sample demonstrates

- branching narrative graph authoring
- dialogue requests
- player choices
- condition-gated choice availability
- action nodes
- state machine mutation and branching
- signal emission
- timeline request + PlayableDirector bridge
- save-ready runtime session state

The generated content is placed under `Assets/StoryFlowQuickStartGenerated`.
