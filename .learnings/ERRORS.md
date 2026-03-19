## [ERR-20260317-001] unity-mcp-session-reload

**Logged**: 2026-03-17T00:00:00Z
**Priority**: high
**Status**: pending
**Area**: infra

### Summary
Unity MCP frequently disconnects or times out during script compilation and play-mode reload, interrupting runtime verification loops.

### Error
```
Unity plugin session disconnected while awaiting command_result
Unity session not available; please retry
MCP error -32001: Request timed out
```

### Context
- Triggered while fixing and verifying `Assets/Core/Scripts/Runtime/Story/BedroomStoryRuntimeBootstrap.cs`
- Common sequence: script recompile -> domain reload / play-mode transition -> Unity MCP tools temporarily unavailable
- This can leave the live runtime scene in a stale state if play mode was already active before compilation completed

### Suggested Fix
- After script recompiles, explicitly restart play mode and re-query the live hierarchy before drawing conclusions about missing components.
- Treat post-compile live-scene inspection as invalid until Unity MCP reconnects and confirms the active scene again.

### Metadata
- Reproducible: yes
- Related Files: Assets/Core/Scripts/Runtime/Story/BedroomStoryRuntimeBootstrap.cs, Assets/Core/TestScenes/BedRoomIntroScene.unity
- See Also: ERR-20260317-003

---

## [ERR-20260317-002] unity-mcp-gameview-overlay-capture

**Logged**: 2026-03-17T00:00:00Z
**Priority**: high
**Status**: pending
**Area**: infra

### Summary
Unity MCP game-view screenshots can omit `ScreenSpaceOverlay` UI even when the live Unity Game view visibly renders that overlay.

### Error
```
MCP screenshot showed only the 3D scene, while a native macOS screenshot of the Unity window showed the subtitle overlay correctly rendered.
```

### Context
- Happened while verifying `BedroomStorySubtitlePresenter` and `BedroomStoryChoicePresenter` in `Assets/Core/TestScenes/BedRoomIntroScene.unity`
- Runtime evidence proved the story emitted dialogue and the subtitle presenter called `Present(...)`
- `SubtitleBand` was active in play mode, but MCP game-view screenshots still hid the overlay
- A native desktop screenshot of the Unity window confirmed the overlay was actually visible

### Suggested Fix
- Do not rely solely on MCP game-view screenshots to verify `ScreenSpaceOverlay` UI.
- Cross-check with native desktop screenshots or Unity window captures when overlay UI appears missing.

### Metadata
- Reproducible: yes
- Related Files: Assets/Core/Scripts/Runtime/Story/BedroomStorySubtitlePresenter.cs, Assets/Core/Scripts/Runtime/Story/BedroomStoryChoicePresenter.cs, Assets/Core/TestScenes/BedRoomIntroScene.unity
- See Also: ERR-20260317-001

---

## [ERR-20260317-003] unity-mcp-editmode-runner-stall

**Logged**: 2026-03-17T00:00:00Z
**Priority**: high
**Status**: pending
**Area**: infra

### Summary
Unity MCP EditMode test execution and scene inspection can hang or time out repeatedly after scene YAML edits, even when repo-side file changes are correct.

### Error
```
Command 'run_tests' timed out after 30 seconds
TimeoutError
Unity session not ready for 'read_console' (ping not answered); please retry
```

### Context
- Happened while verifying `Bedroom_scene_look_items_have_unique_authored_dialogue_for_story_flow` after editing `Assets/Core/TestScenes/BedRoomIntroScene.unity`
- MCP scene queries (`get_active`, `find_gameobjects`) also timed out during the same window
- Repo-side grep/read confirmed the authored scene data existed, but Unity-side verification could not complete because the editor session stopped responding

### Suggested Fix
- Treat Unity MCP verification as temporarily unavailable when repeated test/query timeouts occur after scene edits.
- Fall back to direct repo-side validation for serialized scene changes, then retry MCP only after editor readiness is restored.
- Add a lightweight reconnect/readiness check before running focused Unity tests after YAML scene edits.

### Metadata
- Reproducible: yes
- Related Files: Assets/Core/TestScenes/BedRoomIntroScene.unity, Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryInstallerTests.cs
- See Also: ERR-20260317-001

---

## [ERR-20260317-004] agent-s-missing-gui_agents

**Logged**: 2026-03-17T00:00:00Z
**Priority**: medium
**Status**: pending
**Area**: infra

### Summary
Desktop automation via `agent_s` is currently unusable on this Mac because the installed CLI entrypoint is missing the `gui_agents` Python module.

### Error
```
ModuleNotFoundError: No module named 'gui_agents'
```

### Context
- Happened while trying to continue live Unity play-mode verification without switching scenes
- Both `agent_s --task ...` and the documented `agent_s_task` fallback failed with the same stack trace

### Suggested Fix
- Repair the local Agent-S Python environment so the `gui_agents` package is importable by `/Users/zladwu/.venvs/agent-s/bin/agent_s`.
- Re-verify the fallback wrapper after reinstalling the missing dependency.

### Metadata
- Reproducible: yes
- Related Files: /Users/zladwu/.venvs/agent-s/bin/agent_s, /Users/zladwu/.opencode/skills/agent-s/agent_s_task
- See Also: ERR-20260317-003

---
