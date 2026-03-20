# Classroom Story Design

## Intent

The classroom scene should do three jobs before the lab:

1. Turn the player from passive arrival into an active participant.
2. Seed the biology knowledge needed for the shrinking mission.
3. Build emotional momentum so entering the lab feels like a chosen leap, not a random scene cut.

The scene should feel grounded and character-driven in the same way `Life is Strange` uses quiet space and optional conversations, while keeping the branching clarity and consequence signaling of `Detroit: Become Human`.

## Story Pillars

- Curiosity first: the player should want to ask questions, not be forced through exposition.
- Soft pressure: class starts soon, the demo is dangerous, and the room feels expectant.
- Optional depth: the main route stays readable, but extra NPC talks reward curiosity with richer science context.
- Learning through motive: every biology fact is tied to what the player is about to do in the lab.
- Human tone: classmates react differently to risk, which makes the science feel lived-in.

## Core Cast

- `Dr. Mira Sato`
  - Biology teacher leading the miniaturization field demonstration.
  - Calm, slightly severe, but clearly protective of the class.
  - Function: gives the mission frame, safety constraints, and the final approval to go to the lab.

- `Nia Park`
  - Smart but visibly anxious classmate sitting near the middle rows.
  - Function: turns biology into empathy. She worries about what happens if the player gets trapped inside the digestive tract.
  - Learning angle: stomach acid, mucus protection, and why navigation timing matters.

- `Theo Mercer`
  - Funny, skeptical classmate near the window.
  - Function: pressure-tests the premise with player-facing questions and jokes.
  - Learning angle: why entry is through the mouth, why the airway is dangerous, and what the epiglottis actually does.

- `Laptop Briefing`
  - Not just an objective prop. It is the authoritative mission board.
  - Learning angle: the route map from mouth to small intestine, the target zone, and why the mini rocket matters.

## Scene Flow

### Phase 1: Arrival and temperature check

- Player spawns into a sunlit classroom with visible atmosphere and low ambient chatter.
- The room is live before the player acts: classmates are already reacting to the upcoming lab.
- Short internal monologue:
  - "They’re really doing the live shrink demo today."
  - "If I’m going into a human body, I should know the route before I volunteer."

Goal:
- Establish tone and point the player toward the front of the room without hard-locking them.

### Phase 2: First anchor choice

The player can first approach either:

- the laptop at the teacher desk
- Dr. Mira
- one of the classmates

This preserves agency. The story should not fail if the player approaches "out of order."

Recommended systemic rule:
- Only the laptop briefing is mandatory.
- At least one NPC conversation is strongly encouraged and should unlock additional flavor, reassurance, or confidence.

### Phase 3: Knowledge triangle

The player explores three optional but meaningful knowledge beats:

- `Laptop Briefing`
  - Shows the mission route: mouth -> esophagus -> stomach -> pylorus -> small intestine.
  - Sets up the actual lab objective: shrink, board mini rocket, enter through the mouth, navigate to the absorption zone.
  - Key line:
    - "The mouth is the cleanest insertion point for a controlled entry. From there, the route is predictable. The danger is not getting in. The danger is timing everything after."

- `Talk to Theo`
  - Theo jokes about being swallowed on purpose.
  - The player can answer with:
    - "You’re impossible."
    - "You’re not wrong to ask."
    - "I just need the facts."
  - Dr. Mira or the player clarifies:
    - The epiglottis protects the airway during swallowing.
    - Entering the trachea would be catastrophic for the mission.
  - Tone:
    - Slightly playful, but informative.

- `Talk to Nia`
  - Nia worries about stomach acid and getting dissolved.
  - Player can:
    - reassure her
    - admit they are also nervous
    - deflect with humor
  - Conversation teaches:
    - stomach acid helps digest food
    - the stomach lining protects itself with mucus
    - the rocket must move quickly past the most hostile zone

Optional environmental interaction:

- `Examine board / front wall visual`
  - Could be a chalk diagram, pinned route card, or projected note.
  - Teaches:
    - the small intestine is where absorption happens
    - villi and microvilli massively increase surface area
    - the lab mission is targeting a place where microscopic travel matters

## Branching Structure

The important branching here is not "different ending in the classroom."
It is tone, trust, and readiness.

### Branch Set A: Player attitude

Early response to Dr. Mira can establish one of three tones:

- `Curious`
  - The player asks how the body route works.
  - Reward: more explicit science dialogue.

- `Brave`
  - The player volunteers early.
  - Reward: NPCs react to confidence, but Dr. Mira tests whether the player is prepared or just reckless.

- `Nervous`
  - The player admits hesitation.
  - Reward: stronger bonding with Nia and more grounded emotional dialogue.

### Branch Set B: Relationship moment

The player gets one high-signal interpersonal branch before leaving:

- Comfort Nia
  - establishes player as careful and empathetic
  - good payoff in the lab when Nia radios support

- Debate Theo
  - establishes player as sharp and assertive
  - good payoff when Theo later references the airway or digestion jokes

- Press Dr. Mira for more truth
  - establishes player as responsible and skeptical
  - good payoff when the lab scene becomes more high stakes

### Branch Set C: Readiness gate

The player should not transition to the lab until:

- the laptop briefing is checked
- the player confirms willingness to proceed
- at least one science beat has landed

This can be implemented with:

- mandatory `LaptopChecked`
- one of:
  - `TeacherTalked`
  - `FriendTalked`
  - `SkepticTalked`
- final confirmation:
  - `VolunteerConfirmed`

## Recommended Scene Beats With Dialogue

### Dr. Mira opening

- "Settle in. Today’s demonstration is not a simulation."
- "In the lab next door, one of you will take a miniaturized entry vehicle into a living human digestive system."
- "If you treat that as a stunt, you will fail before you reach the stomach."

### Laptop briefing voice

- "Mission objective: reach the small intestine intact."
- "Reason: it is the primary site of nutrient absorption and the safest instructional target at micro scale."
- "Caution: the airway, stomach acidity, and transport timing remain mission-critical hazards."

### Nia branch

- Nia: "I know the science, I just... it feels different when it’s a person."
- Player response options:
  - "That’s why it matters."
  - "You don’t have to pretend this is easy."
  - "If I panic, I’m blaming you for manifesting it."
- Educational follow-up:
  - "The stomach is harsh, but not defenseless. Acid breaks food down. Mucus keeps the stomach from digesting itself."

### Theo branch

- Theo: "So the plan is: get swallowed on purpose, dodge acid, and call it education?"
- Player response options:
  - "That is a deeply irresponsible summary."
  - "Still a better plan than your test scores."
  - "What happens if the rocket hits the airway?"
- Educational follow-up:
  - "That is exactly why the epiglottis matters. If the route goes wrong at the throat, the mission ends before it begins."

### Final handoff

- Dr. Mira: "You’ve got the route. You’ve heard the risks. Last chance to stay in the classroom."
- Final options:
  - "I’m going."
  - "Give me the short version one more time."
  - "Why me?"
- Transition line:
  - "Lab door. Now. We brief once more, shrink once, and then we launch."

## Educational Payloads

The classroom should seed these facts because the lab and body scenes will depend on them:

- Swallowing route is controlled and distinct from breathing.
- The epiglottis helps keep material out of the airway.
- The stomach is acidic but self-protected.
- The small intestine is the major absorption site.
- Surface area from villi matters at micro scale.
- The mouth is a practical entry route because it connects to a natural internal pathway.

## How This Uses The Existing Interaction System

- NPCs should be normal `InteractableItem` targets with outline support.
- Prompt options should stay compact and readable:
  - `Talk`
  - `Ask`
  - `Reassure`
  - `Joke`
  - `Observe`
- Environmental objects should stay lower complexity:
  - `Observe`
  - `Inspect`
  - `Read`
  - `Open`

Recommended classroom story routing ids:

- `classroom.teacher.talk`
- `classroom.teacher.ask_safety`
- `classroom.friend.reassure`
- `classroom.friend.ask_acid`
- `classroom.skeptic.joke`
- `classroom.skeptic.ask_airway`
- `classroom.board.read_route`
- `classroom.laptop.open_brief`

## NPC System Requirements

The NPC system does not need full AI first. It does need strong interaction authoring.

### Required in first implementation

- stable `npcId`
- automatic `InteractableItem` setup
- automatic outline/prompt hookup
- dynamic option visibility and enabled state
- event payload for `(npcId, optionId, interactionId)`
- optional default `Observe` dialogue
- optional face-camera or face-player behavior while focused
- registry/lookup so scene bridges can find NPCs by id

### Strong second-step features

- per-stage option profiles
- one-shot and repeatable conversation nodes
- local memory flags such as `hasMetPlayer`, `sharedKeyFact`, `relationshipDelta`
- simple bark system when the player enters range
- animation hooks for idle, greet, think, react
- waypoint/schedule support for scenes where NPCs move between beats

### Good long-term features

- story-variable-driven option gating
- lightweight portrait / mood integration with subtitle UI
- companion radio callbacks in later scenes based on classroom relationship outcomes
- debug inspector showing live option state and last interaction id

## Implementation Direction

Recommended split:

- Keep the main `ClassroomStoryRuntimeBootstrap` focused on scene-wide progression and transition to lab.
- Use NPC components to own local interaction prompts and emit stable story-facing route ids.
- Let optional NPC conversations teach facts and adjust tone without forcing every optional branch through one linear graph.

That keeps the scene readable, preserves player freedom, and avoids turning the story graph into an unreadable state explosion.

## Implemented Runtime Branch Map (Classroom -> Lab)

This section reflects the runtime branch map now used by classroom scripts:

1. `FreshSpawn`:
- Intro delay and mission framing subtitle.
- Story state set to `BriefingActive`.

2. `BriefingActive`:
- Player must engage at least one meaningful classroom branch.
- Teacher `Talk` branch now starts with `Scripted Talk / Free Chat / Leave`.

3. `ExplorationActive`:
- Science evidence is gathered via NPC and environment interactions:
`TeacherSafetyExplained`, `FriendTalked`, `SkepticTalked`, `BoardExamined`, `DeskExamined`, `ShelfBookRead`, `ClockChecked`.
- Volunteer option unlocks when evidence threshold is met.

4. `DoorReady`:
- Set when volunteer confirmation is accepted or explicit `LabClearanceEarned` signal is raised.
- Entrance door interaction changes to lab-ready state.

5. `TransitionCommitted`:
- Door confirmation raises `DoorConfirmed`.
- Scene transition commits to `LabMiniEntryScene`.

## Free Chat LLM Protocol

Free chat uses `https://game.agaii.org/mllm/v1` with:

- model auto-detection from `GET /models`,
- streaming output parsing from `/chat/completions`,
- concise output guardrails (`max_tokens` cap + concise system prompt),
- `chat_template_kwargs.enable_thinking=false`.

Required response contract:

```
SAY: <short in-character reply>
ACTIONS: <comma-separated action tokens or none>
```

Action tokens currently supported:

- `dance`
- `jump`
- `surprised`
- `lie_down`
- `follow_player_short`
- `go_talk_nia`
- `go_talk_theo`
- `go_talk_mira`
- `start_quiz`

## Camera/Control Rules

- Only NPC conversation flows use presentation mode:
  - movement lock
  - HUD hidden
  - speaker-focus conversation camera movement
- Item interactions (`board`, `teacher desk`, `reference shelf`, `wall clock`) are non-presentation and do not move the camera.
- Free chat runs inside NPC presentation mode and restores controls/HUD/camera cleanly on close.

## Hidden Knowledge Quiz Flow

The hidden quiz is a UI Toolkit overlay triggerable by NPC action (`start_quiz`):

- countdown start
- timed rounds
- per-question options
- immediate feedback/explanations
- score + completion grade
- clean control lock release on exit

The quiz reinforces mission-critical knowledge:

- airway vs esophagus route
- epiglottis role
- stomach acidity timing risk
- small intestine absorption target

## Voiceover Pipeline

Classroom dialogue voice path now supports:

1. Static pre-generated clips in `Resources/Audio/Story/Classroom`.
2. Runtime TTS fallback for missing lines via `ClassroomNpcRuntimeVoiceoverService`.
3. Runtime clip caching and subtitle presenter integration via `ClassroomStoryRuntimeVoiceCache`.

Subtitle typewriter timing is adjusted to clip duration when audio is present.

## Ambient NPC Chatter

Ambient NPC-to-NPC chatter runs continuously on a light cadence:

- selects NPC pairs every ~10-18 seconds,
- requests short two-line exchanges from LLM,
- renders world chat bubbles over speakers.

To avoid API/audio spam, ambient chatter is text-only and does not trigger voice generation.
