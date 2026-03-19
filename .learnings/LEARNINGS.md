## [LRN-20260317-001] correction

**Logged**: 2026-03-17T00:00:00Z
**Priority**: high
**Status**: pending
**Area**: frontend

### Summary
Bedroom scene dialogue convention is first-person `You`, including the opening intro line.

### Details
I incorrectly claimed the opening subtitle speaker `Max` was expected in the bedroom scene. The user clarified that in the bedroom scene it should all be `You`, so the bootstrap-authored opening line also needs to use `You` rather than a character name.

### Suggested Action
Update the bedroom runtime bootstrap intro dialogue speaker to `You` and add a regression that locks the opening bedroom line to the scene convention.

### Metadata
- Source: user_feedback
- Related Files: Assets/Core/Scripts/Runtime/Story/BedroomStoryRuntimeBootstrap.cs, Assets/Core/com.modular.storyflow/Tests/Editor/BedroomStoryRuntimeBootstrapTests.cs
- Tags: unity, story, dialogue, speaker-name

---
