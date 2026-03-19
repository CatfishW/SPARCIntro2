# Modular Story Flow

Modular Story Flow is a Unity 6-ready UPM package for branching story, gameplay flow, and narrative orchestration.

It is designed around a simple rule:

- **high cohesion**: each node, condition, action, channel, and bridge has one job
- **low coupling**: runtime systems communicate through ScriptableObject assets and event channels, not hard scene references

## Included

- GraphView-based node editor
- branching graph assets backed by ScriptableObject node sub-assets
- dialogue nodes
- choice nodes with conditional availability
- branch nodes with reusable condition assets
- action nodes with reusable action assets
- timeline request nodes
- signal nodes and external signal waits
- state machine definitions and branching by state
- JSON save/load provider
- runtime player with channel-driven integration
- one-click setup/configure wizard
- quick-start sample installer

## Package layout

- `Runtime/Core` — graph assets, nodes, actions, conditions, variables, states, and integration assets
- `Runtime/Channels` — typed ScriptableObject event channels
- `Runtime/Player` — pure runner + MonoBehaviour wrapper + project config
- `Runtime/Save` — save snapshot models and providers
- `Runtime/Bridges` — timeline and signal integration helpers
- `Editor` — graph editor, inspectors, and setup tooling
- `Samples~/QuickStart` — importable sample overlay + installer

## Install

Add the package as a local package or Git package in Unity Package Manager.

After import:

1. Open **Tools > Modular Story Flow > Setup Wizard**
2. Choose a root folder under `Assets/`
3. Click **One-Click Generate**

The wizard creates:

- project config
- channels
- graph registry
- variable catalog
- state machine catalog
- timeline catalog
- JSON save provider
- optional prefabs for a player and timeline bridge

## Authoring flow

1. Create a `StoryGraphAsset`
2. Open it in the graph editor
3. Add nodes from the search menu
4. Connect ports
5. Create reusable condition/action assets as needed
6. Add your graph assets to the generated registry
7. Start a graph from `StoryFlowPlayer`

## Runtime integration

The runtime is presentation-agnostic. Your game can respond using:

- ScriptableObject channels
- code subscriptions on `StoryFlowPlayer`
- bridge components like `StoryTimelineDirectorBridge`

This lets the same package work in 2D, 3D, VR, diegetic UI, or menu-driven games.

## Core assets

### Nodes
- Start
- End
- Dialogue
- Choice
- Branch
- Action
- Signal
- Timeline
- Wait For Signal
- Delay

### Conditions
- Variable Condition
- State Equals Condition
- Random Chance Condition

### Actions
- Set Variable Action
- Set State Action
- Raise Signal Action
- Composite Action

### Integration assets
- Signal Definition
- Timeline Cue
- Timeline Catalog
- Graph Registry

## Save system

`StoryFlowPlayer` can save or load through any `StorySaveProviderAsset`.

Included default provider:

- `JsonFileStorySaveProviderAsset`

The save snapshot contains:

- graph id
- current node id
- pending wait state
- variable values
- state machine values
- visit history

## Extending the package

### Add a custom node

Create a class that inherits `StoryNodeAsset` and annotate it with `StoryNodeAttribute`.

Implement:

- `GetPorts()`
- `Execute(IStoryExecutionContext context)`

### Add a custom condition

Create a ScriptableObject that inherits `StoryConditionAsset` and implement `Evaluate(...)`.

### Add a custom action

Create a ScriptableObject that inherits `StoryActionAsset` and implement `Execute(...)`.

### Add a custom save provider

Create a ScriptableObject that inherits `StorySaveProviderAsset` and implement:

- `Save`
- `TryLoad`
- `Delete`
- `GetDebugPath`

## Quick start sample

Import the **Quick Start** sample in Package Manager, then run:

**Tools > Modular Story Flow > Samples > Install Quick Start**

It generates a complete sample graph and a tiny overlay-driven demo under `Assets/StoryFlowQuickStartGenerated`.

## Notes

- The editor is isolated from runtime code. If you replace the editor implementation later, runtime assets and player logic stay intact.
- The runtime intentionally avoids UI dependencies.
- Timelines are requested by cue id and resolved by a catalog, so gameplay scenes never need direct references back into the graph.
