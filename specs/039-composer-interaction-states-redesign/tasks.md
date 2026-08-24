---

description: "Task list for Composer Interaction States Redesign"
---

# Tasks: Composer Interaction States Redesign

**Input**: Design documents from `/specs/039-composer-interaction-states-redesign/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. Not TDD-mandated by the spec, but constitution §10/§18 require test
coverage for changed observable behavior in the same PR that introduces it — every story
below includes its own test tasks.

**Organization**: Tasks are grouped by user story (spec.md priorities). All paths are under
`src/AskLucy.Web/ClientApp/src/features/chat/` unless noted otherwise.

> **Post-`/speckit-analyze` remediation history**: two rounds of analysis findings are folded
> into this revision.
> - **Round 1** (2026-08-25): F1/F2 (replay control's disabled/stop logic was incomplete), E1
>   (no safeguard against an indefinite hold-to-talk recording), C1 (the one-click
>   continuous-conversation hybrid's async-failure ordering was unspecified).
> - **Round 2** (2026-08-25): F4 (round 1's E1 fix had a wording gap that could leave a
>   short, tap-classified press still recording in the background — corrected in place, no
>   renumbering), E4 (starting a recording/listening session didn't stop an in-progress
>   manual replay — the reverse of F2 — added as a new task).
> - **Round 3** (2026-08-25): F5 (T034's E4 fix only demonstrably covers click-to-talk/
>   hold-to-talk; continuous-conversation entry (T020) is a third capture-start path living in
>   `ChatPage.tsx`'s own `onToggleMode` handler, not routed through `ChatComposer`'s
>   `onStartCapture` prop — cross-references added to both T020 and T034 so the implementer
>   routes all three through the same wrapped function; corrected in place, no renumbering).
> - **Implementation-time fix** (2026-08-25, found writing the T037 integration test, not by
>   `/speckit-analyze`): T032's `isReplayDisabled` formula disabled *every* reply whenever
>   *any* reply was playing, not just the one auto-speaking itself — directly contradicting
>   FR-023 ("starting playback on one reply MUST stop any other reply currently playing," a
>   requirement that's unreachable if that other reply's button is disabled). Corrected to
>   `(isThisMessagePlaying && !isManualReplay)` — see data-model.md's "Post-implementation
>   correctness fix" note. Also fixed: `handleStartCapture` (T034/E4) only wrapped the
>   Continuous-mode entry point, not Push-to-Talk's `recorder.start()` — now dispatches on the
>   *live* store's `conversationMode` (not a closure value, which goes stale inside
>   `handleToggleMode`'s async continuation) and covers all three entry paths as T034 always
>   intended. Three passes of `/speckit-analyze` didn't catch either — both were found by
>   writing real integration tests against the actual two-message/two-mode click sequences,
>   not by re-reading the design docs again.
>
> Task IDs from the pre-remediation version no longer match — this file has been renumbered
> twice (round 1 inserted T014/T015/T026; round 2 inserted T034). Use the IDs as they appear
> below.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US6)
- File paths are exact, relative to the repository root

---

## Phase 1: Setup

**Purpose**: Confirm the starting point before making changes. No new dependencies,
scaffolding, or project initialization is required — this feature reuses 100% existing
infrastructure (research.md).

- [X] T001 Run `npm test` and `npx tsc -b --noEmit` in `src/AskLucy.Web/ClientApp` to confirm
      a clean, green baseline before any changes (per quickstart.md's "Automated checks")

**Checkpoint**: Baseline confirmed green — safe to begin story work.

---

## Phase 2: Foundational

**Purpose**: N/A for this feature. There is no shared infrastructure, schema, or
authentication/routing layer to stand up first (research.md confirms no new persisted
state, no backend changes). The one piece of groundwork every other composer story builds
on — the empty↔typing visibility gate — is itself User Story 1's own deliverable (it is
independently valuable and testable on its own, matching the P1/MVP framing in spec.md), so
it is **not** duplicated here as a separate blocking phase. See "User Story Dependencies"
below for the resulting build order this implies for US2–US4/US6.

**Checkpoint**: N/A — proceed directly to Phase 3.

---

## Phase 3: User Story 1 - Compose and send a text message (Priority: P1) 🎯 MVP

**Goal**: The composer shows only the controls appropriate to whether the text field is
empty or has text, and always returns to its empty appearance after sending or clearing
(FR-001–FR-004).

**Independent Test**: Open a conversation, type text, verify the composer's controls swap
from voice-entry icons to a send icon, send the message, and verify the composer returns to
its empty starting appearance (spec.md US1).

### Implementation for User Story 1

- [X] T002 [US1] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`,
      change the attach/mic/continuous-conversation-action rendering gate from today's
      `!isRecordingActive` (the only current condition) to also require `value === ''` —
      i.e. hide attach, the mic button, and the mode-switch/continuous-conversation icon
      entirely (not just leave them enabled) whenever the text field is non-empty
      (contracts/composer-voice-states.md's Empty/Typing rows; FR-001, FR-002)
- [X] T003 [US1] In the same file, render the send action (`RiSendPlane2Fill`) only when
      `value !== ''` instead of always-mounted-and-disabled; keep its existing
      `disabled={disabled || !value.trim()}` condition (FR-003)
- [X] T004 [US1] Verify (and adjust if needed) that sending a message or manually clearing
      all text in `ChatComposer.tsx`/`ChatPage.tsx` already drives `value` back to `''`,
      which per T002/T003 now automatically returns the composer to its empty appearance
      (FR-004) — no separate "reset to Figure 1" state variable should be introduced

### Tests for User Story 1

- [X] T005 [P] [US1] In
      `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx`, add
      cases: empty state shows attach+mic+continuous-conversation and no send; typing shows
      only send; send disabled with empty/whitespace-only text, enabled with real text;
      composer returns to empty appearance after `onSend` and after manually clearing text
- [X] T006 [P] [US1] Create
      `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.a11y.test.tsx`
      (new file — none exists today) asserting no automated a11y violations (via the
      project's existing `vitest-axe` pattern, e.g. `ChatSidebar.a11y.test.tsx`) in both the
      empty and typing states, and that every visible control keeps a correct `aria-label`

**Checkpoint**: US1 fully functional and testable independently — this is the MVP.

---

## Phase 4: User Story 2 - Record and send a voice message via click-to-talk (Priority: P1)

**Goal**: Clicking (not holding) the mic from the empty composer starts a recording with
distinct cancel/confirm actions; cancel discards, confirm transcribes into the field and
moves to the typing state (FR-005–FR-007). **Depends on US1** (T002) being in place, since
the mic is only reachable in the empty state that US1 establishes.

**Independent Test**: From the empty composer, click the microphone action, verify a
recording view with distinct confirm/cancel actions appears, and verify each action produces
the documented outcome (spec.md US2).

### Implementation for User Story 2

- [X] T007 [US2] In `ChatComposer.tsx`, confirm the existing tap-gesture path
      (`isAwaitingTapReview` → `RecordingReviewControls`, research.md Decision 1) is reached
      only from the empty state under T002's new gate, and that no code path allows starting
      it once `value !== ''` (FR-005) — this should require no new gesture logic, only
      verifying T002's gate correctly subsumes the mic's click handler too
- [X] T008 [US2] Confirm `handleTapReviewCancel` (existing) discards the recording with no
      text change and leaves `value === ''`, so T002's gate naturally returns the composer to
      its empty appearance (FR-006) — fix if any dead/legacy branch bypasses this
- [X] T009 [US2] Confirm `handleTapReviewFinish`/`onStopCapture`'s existing transcription
      flow places transcribed text into `value` via the same `onChange` path hold-to-talk
      uses, so the composer lands in the typing state per T003's gate (FR-007) — no
      "append after existing text" logic is needed per the corrected FR-007, since
      click-to-talk is only reachable from empty

### Tests for User Story 2

- [X] T010 [P] [US2] In `ChatComposer.test.tsx`, add cases: clicking the mic from empty
      shows cancel+confirm and a recording indicator, with attach/continuous-conversation
      hidden; cancel returns to the empty appearance with no text; confirm (mocked
      transcription result) places text in the field and shows the typing-state appearance
- [X] T011 [P] [US2] Extend `ChatComposer.a11y.test.tsx` (T006) with a case covering the
      click-to-talk recording state (cancel/confirm controls both keep correct
      `aria-label`s, live recording indicator doesn't trap focus)

**Checkpoint**: US1 and US2 both independently functional.

---

## Phase 5: User Story 3 - Hold-to-talk quick voice capture (Priority: P2)

**Goal**: Press-and-hold the mic from the empty composer starts recording immediately with
no cancel/confirm controls; releasing transcribes directly into the field and moves to the
typing state; the recording can never be left open indefinitely, however briefly it was
held (FR-008–FR-011, plus the indefinite-recording safeguard from spec.md's Edge Cases).
**Depends on US1** (same reachability gate as US2).

**Independent Test**: From the empty composer, press and hold the microphone action, verify
the pressed/recording appearance with no cancel/confirm, release, and verify the transcription
lands in the field with the composer in its typing-state appearance (spec.md US3).

### Implementation for User Story 3

- [X] T012 [US3] In `ChatComposer.tsx`, confirm the existing hold-gesture path
      (`resolveGestureOnRelease`'s `heldMs >= HOLD_THRESHOLD_MS` branch, research.md
      Decision 1) already swaps the mic icon and shows only the waveform (no
      `RecordingReviewControls`) — this exists today; verify it still renders correctly once
      reachable only from T002's empty-state gate
- [X] T013 [US3] Swap the mic icon used during an active hold-recording from
      `RiMicOffLine`/`RiMicLine` to `RiMicFill` specifically for the hold branch (distinct
      from the tap/click branch's icon, per Figure 9 / FR-009) — add the `RiMicFill` import
      from `@remixicon/react` (confirmed present, plan.md Technical Context)
- [X] T014 [US3] **(analysis remediation E1, corrected per round-2 finding F4)** In
      `ChatComposer.tsx`, add a `document` `visibilitychange` (hidden) and `window` `blur`
      listener, attached only while `isCapturingRef.current` is `true` and removed once the
      gesture resolves, that calls `onStopCapture()` **directly** — do **not** route through
      `resolveGestureOnRelease()`, since that function re-derives tap-vs-hold from elapsed
      time and, for a press still under `HOLD_THRESHOLD_MS` at the moment of backgrounding,
      would only set `isAwaitingTapReview(true)` (leaving capture running, waiting for a
      Finish/Cancel click the user cannot reach with the tab hidden) instead of actually
      stopping it. The tab/window losing visibility makes the tap/hold distinction moot
      either way — neither a review UI nor a real release gesture is reachable — so this
      safeguard must unconditionally stop capture regardless of how long the press has been
      held (spec.md Edge Case: "the recording MUST have a safeguard so it cannot remain
      active indefinitely"; contracts/composer-voice-states.md)
- [X] T015 [US3] Confirm release-driven transcription (`onStopCapture`) places text into
      `value` and the composer transitions to the typing state per T003's gate (FR-010,
      FR-011) — same underlying path as T009, verify for the hold branch specifically
- [X] T016 [US3] Confirm a release under `HOLD_THRESHOLD_MS` (350ms) still correctly falls
      through to the click-to-talk review flow (T007) rather than auto-finishing — this is
      existing behavior (research.md Decision 1/spec.md Edge Cases) and must not regress

### Tests for User Story 3

- [X] T017 [P] [US3] In `ChatComposer.test.tsx`, add cases: pointer-down-and-hold past the
      threshold shows the `mic-fill` recording indicator with no cancel/confirm controls;
      release transcribes (mocked) directly into the field and shows the typing-state
      appearance; a release under the threshold instead shows the click-to-talk
      cancel/confirm controls (regression case for T016)
- [X] T018 [P] [US3] Extend `ChatComposer.a11y.test.tsx` with a case for the hold-active
      state (the non-interactive recording indicator doesn't confuse screen-reader users
      about the mic button's actual pressed/toggle state)
- [X] T019 [P] [US3] **(analysis remediation E1, extended per round-2 finding F4)** In
      `ChatComposer.test.tsx`, add cases: (1) simulate an active hold-classified recording
      (past `HOLD_THRESHOLD_MS`), dispatch a `visibilitychange`(hidden) event and, separately,
      a `blur` event, and assert `onStopCapture` is called in both cases exactly as it would
      be on a real pointer release; (2) simulate a still-tap-classified press (under
      `HOLD_THRESHOLD_MS`) and dispatch the same two events, asserting `onStopCapture` is
      **still** called directly and the composer does **not** end up showing the tap-review
      (cancel/confirm) controls with capture left running (regression coverage for T014/F4)

**Checkpoint**: US1, US2, US3 all independently functional; a hold-to-talk recording cannot
outlive the tab losing focus or the screen locking, regardless of how briefly it was held.

---

## Phase 6: User Story 4 - Hands-free continuous conversation (Priority: P2)

**Goal**: Activating the continuous-conversation action from the empty composer both
switches the persisted voice-mode preference and starts listening in one click — safely with
respect to the preference save's own success/failure — shows Lucy's avatar and mute/exit
controls, and lets the user type without leaving the mode (FR-012–FR-017; one-click hybrid
per spec.md Clarifications / research.md Decision 3). **Depends on US1** (T002's gate governs
when this action is visible).

**Independent Test**: From the empty composer, start continuous-conversation mode, verify
the agent's avatar and listening state appear, verify mute and exit actions, verify typing
mid-conversation reveals a send action without exiting the mode, and verify exiting returns
to the empty starting appearance (spec.md US4).

### Implementation for User Story 4

- [X] T020 [US4] **(reworded — analysis remediation C1)** In
      `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, change the
      `onToggleMode` handler passed to `ChatComposer` so that switching `PushToTalk` →
      `Continuous` **awaits** `voicePreferencesStore.update({ conversationMode: 'Continuous' })`
      before calling `onStartCapture()` — if the update rejects (the store rolls back and
      surfaces its existing `error`/Snackbar), capture must never start against a preference
      that didn't actually persist as Continuous. Switching `Continuous` → `PushToTalk` is the
      reverse priority: call `onStopCapture()` **immediately/synchronously** — listening stops
      with no delay regardless of the save's outcome — then `await` the inverse `update()` for
      its own pre-existing error surfacing if that save fails (data-model.md "Continuous
      Conversation Session", contracts/composer-voice-states.md). **Cross-reference (round-3
      finding F5)**: once T034 (US5) exists, this handler's "call `onStartCapture()`" step
      MUST be the same `handleStartCapture` reference T034 wraps (not a separate raw
      capture-start call) so that entering Continuous also stops an in-progress manual replay,
      same as click-to-talk/hold-to-talk — if T034 isn't implemented yet, this can be the raw
      capture-start call for now, but must be revisited when T034 lands.
- [X] T021 [US4] In `ChatComposer.tsx`, separate the Continuous-mode mic control into two
      distinct actions per Figure 4/6: a mute/unmute action (`RiMicOffLine`/`RiMicLine`,
      toggling audio *input* capture without leaving Continuous mode) and a stop/exit action
      (`RiStopLine`, calling the paired handler from T020) — today's single toggle button
      that conflates both must be split (FR-013, FR-014)
- [X] T022 [US4] In `ChatComposer.tsx`, under T002's gate, ensure Continuous
      idle-listening (`conversationMode === 'Continuous' && isListening && value === ''`)
      shows exactly the mute + exit actions (no attach/mic/continuous-conversation-entry),
      and Continuous typing (`value !== ''` while still in Continuous mode) shows only send,
      returning to Continuous idle-listening (not Empty) on send or on clearing text
      (FR-015–FR-017) — this same gate also means the continuous-conversation entry action is
      unreachable while any recording is in progress, resolving spec.md Edge Case 4 (switching
      into Continuous mid-recording) by construction rather than needing separate handling
- [X] T023 [US4] In `ChatPage.tsx` (or `ChatComposer.tsx`'s parent conversation view),
      conditionally render `LucyPortrait` (currently unused in this view,
      `src/AskLucy.Web/ClientApp/src/features/chat/branding/LucyPortrait.tsx`) as the
      circular avatar, shown only when `conversationMode === 'Continuous' && isListening`
      (FR-012, Figure 4/5/6)
- [X] T024 [P] [US4] In
      `src/AskLucy.Web/ClientApp/src/features/chat/components/CollapsedVoiceControls.tsx`,
      swap the mode-switch icon from `RiFingerprintLine` to `RiVoiceprintLine` (icon parity
      only, research.md Decision 8 — layout, `RiInfinityLine`, and handlers unchanged)

### Tests for User Story 4

- [X] T025 [P] [US4] In `ChatPage.test.tsx`, add a case: activating the continuous-
      conversation action calls both the mode-update and start-capture calls together (the
      one-click hybrid), and exiting calls both the inverse update and stop-capture together
- [X] T026 [P] [US4] **(new — analysis remediation C1)** In `ChatPage.test.tsx`, add a case:
      entering Continuous with a rejecting `voicePreferencesStore.update()` never calls
      `onStartCapture` and surfaces the store's existing error; exiting Continuous always
      calls `onStopCapture` immediately regardless of `update()`'s outcome (including when it
      rejects)
- [X] T027 [P] [US4] In `ChatComposer.test.tsx`, add cases: Continuous idle-listening shows
      mute+exit only; typing while in Continuous mode shows send only and, on send or clear,
      returns to the idle-listening appearance (not the Empty appearance) — distinguishing
      this from US1's Empty-return behavior
- [X] T028 [P] [US4] In `CollapsedVoiceControls.test.tsx`, update the existing
      fingerprint-icon assertion(s) to expect `RiVoiceprintLine` instead
- [X] T029 [P] [US4] Extend `ChatComposer.a11y.test.tsx` with a case for the Continuous
      idle-listening state (mute/exit `aria-label`s, avatar has appropriate `alt`/role)

**Checkpoint**: US1–US4 all independently functional; Settings → Voice mode preference still
works exactly as before, now reachable via one click from the composer too, and a failed
preference save can never leave capture running against an unpersisted mode.

---

## Phase 7: User Story 5 - Replay a spoken reply (Priority: P3)

**Goal**: Every completed assistant reply gets a replay/stop control in its lower-right
corner; a reply auto-speaking for the first time shows a disabled play control (never an
interactive stop); a user-initiated replay shows an interactive stop control; at most one
reply plays at a time; replay is unavailable while a recording/listening session is active,
and starting a recording/listening session stops an in-progress replay; stopping and
replaying always restarts from the beginning (FR-020–FR-026). **Independent of US1–US4** —
touches `MessageBubble.tsx` and new `ChatPage.tsx` state, not `ChatComposer.tsx`'s action row
itself (T034 wraps an existing pass-through prop, not `ChatComposer.tsx`'s own code).

**Independent Test**: Send a message that produces a spoken reply, wait for speech to finish,
then use the reply's control to start and stop playback, and verify only one reply can play
at a time (spec.md US5).

### Implementation for User Story 5

- [X] T030 [US5] **(reworded — analysis remediation F1)** In `ChatPage.tsx`, add
      `const [playingMessageId, setPlayingMessageId] = useState<string | null>(null)` **and**
      `const [isManualReplay, setIsManualReplay] = useState(false)` (the second flag
      distinguishes an auto-spoken reply from a user-initiated replay of the same message —
      data-model.md "Assistant Reply Playback"). Add a `handleReplay(message: ChatMessage)`
      callback that calls `tts.stop()` if `tts.isSpeaking`, then `tts.speak(message.content,
      language)`, `setPlayingMessageId(message.id ?? null)`, and `setIsManualReplay(true)`; a
      `handleStopReplay` callback calling `tts.stop()` and clearing both `playingMessageId`
      and `isManualReplay`; and an effect clearing both whenever `tts.isSpeaking` becomes
      `false` (contracts/reply-playback-control.md)
- [X] T031 [US5] **(reworded — analysis remediation F1)** In the same file, update the
      existing auto-speak effect (the one that speaks the newest reply on stream completion)
      to also call `setPlayingMessageId(last.id)` **and explicitly `setIsManualReplay(false)`**
      alongside its existing `tts.speak(...)` call, so an auto-played reply's own control
      stays disabled+play (FR-021) rather than becoming an interactive stop (research.md
      Decision 5; contracts/reply-playback-control.md)
- [X] T032 [US5] **(reworded — analysis remediation F1/F2; formula corrected post-
      implementation — see data-model.md)** In `ChatPage.tsx`, compute per message: `const
      isThisMessagePlaying = message.id === playingMessageId`; `const showStopIcon =
      isThisMessagePlaying && isManualReplay`; `const isRecordingOrListeningActive =
      (recording !== undefined && recording.phase !== 'idle') || isListening`; `const
      isReplayDisabled = isMutedPreference || !message.id || isRecordingOrListeningActive ||
      (isThisMessagePlaying && !isManualReplay)` — **not** `(playingMessageId !== null &&
      !showStopIcon)`, which would also disable every *other*, non-playing reply and make
      FR-023's "clicking a different reply's Replay stops the old one and starts the new one"
      scenario unreachable via the UI. Pass `showStopIcon`, `isReplayDisabled`,
      `onReplay={handleReplay}`, and `onStopReplay={handleStopReplay}` down to each
      `<MessageBubble />` (`ChatPage.tsx` around the existing `messages.map`/virtualizer
      render, `MessageBubble` invocation)
- [X] T033 [US5] **(reworded — analysis remediation F1)** In
      `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx`, add the new
      `showStopIcon`/`isReplayDisabled`/`onReplay`/`onStopReplay` props, and render an
      `IconButton` in the reply bubble's lower-right corner — only when `message.role ===
      'assistant' && message.id !== undefined` (research.md Decision 7) — showing
      `RiPlayFill` (`disabled` when `isReplayDisabled`, calling `onReplay(message)`) when
      `!showStopIcon`, or `RiStopFill` (always enabled, calling `onStopReplay()`) when
      `showStopIcon` is `true`
- [X] T034 [US5] **(new — analysis remediation E4, cross-reference added per round-3 finding
      F5)** In `ChatPage.tsx`, wrap the existing function this component already passes as
      `ChatComposer`'s `onStartCapture` prop (unchanged pre-existing behavior otherwise) into
      a single `handleStartCapture` so that, before delegating to it, it calls
      `handleStopReplay()` if `playingMessageId !== null && isManualReplay` — starting a new
      recording or continuous-listening session must stop an in-progress *manual* replay
      first (symmetric to F2's "replay disabled while recording/listening"; an auto-spoken
      reply is intentionally excluded from this guard). This covers all three entry paths
      (click-to-talk, hold-to-talk, continuous-conversation) **only if every internal call
      site in `ChatPage.tsx` that starts capture is updated to invoke this same
      `handleStartCapture` reference — including T020's `onToggleMode` handler (Continuous
      entry), which must call `handleStartCapture()` here, not a separate/raw capture-start
      call it may have used before this task existed.** If US4 (T020) was implemented before
      this task, revisit its `onToggleMode` handler to route through `handleStartCapture`.
      No change to `ChatComposer.tsx` or to T007/T012's own gesture-handling code is needed —
      only ensuring every `ChatPage.tsx`-side caller (including T020) uses the same wrapped
      reference (data-model.md "Assistant Reply Playback", contracts/reply-playback-control.md)

### Tests for User Story 5

- [X] T035 [P] [US5] **(reworded — analysis remediation F1/F2)** In
      `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.test.tsx`, add
      cases: no replay control on user messages or a message with no `id`; `RiPlayFill` shown
      and enabled when not playing/not muted/no recording-or-listening session active;
      disabled when `isReplayDisabled`; clicking it calls `onReplay`; `RiStopFill` shown and
      calls `onStopReplay` when `showStopIcon` is `true`
- [X] T036 [P] [US5] Create
      `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.a11y.test.tsx`
      (new file) asserting no a11y violations with the replay control present in both its
      play and stop states, and a correct `aria-label` for each
- [X] T037 [P] [US5] **(reworded — analysis remediation F1/F2)** In `ChatPage.test.tsx`, add
      cases: replaying message B while message A is playing stops A (`tts.stop()` called)
      before starting B; stopping mid-playback clears `playingMessageId`/`isManualReplay`;
      replaying after a stop calls `tts.speak` again from scratch (no resume/seek call exists
      to assert against — confirms FR-025 by construction); muting sets every bubble's
      `isReplayDisabled` to `true` regardless of `playingMessageId`; an auto-spoken reply's
      own `isReplayDisabled` is `true` and `showStopIcon` is `false` while it is speaking for
      the first time (F1); `isReplayDisabled` is `true` for every reply while a
      recording/listening session is active, even if nothing is currently playing (F2); **and
      (analysis remediation E4) starting capture while a manual replay is in progress calls
      `handleStopReplay` first (T034)**

**Checkpoint**: US1–US5 all independently functional.

---

## Phase 8: User Story 6 - Composer chrome cleanup (Priority: P3)

**Goal**: The saved-prompts action never appears in any composer state; the height controls
use the updated diagonal icons (FR-018, FR-019). **Independent of US1–US5** — the
saved-prompts removal simplifies `ChatComposer.tsx` (safe to do in any order relative to
US1–US4's edits to the same file, but recommended last to minimize merge churn against
those); the height-control change is in a different file entirely.

**Independent Test**: Open any composer state and confirm the saved-prompts action is absent
everywhere; use the height controls to confirm the updated icons while behavior is unchanged
(spec.md US6).

### Implementation for User Story 6

- [X] T038 [US6] In `ChatComposer.tsx`, delete the `onInsertPromptClick` prop, its rendering
      branch (the `RiArticleLine` `IconButton`), and the now-unused `RiArticleLine` import
      entirely (FR-018) — not merely made conditionally hidden
- [X] T039 [US6] Remove the corresponding `onInsertPromptClick` prop from `ChatComposerProps`
      usages and callers (`ChatPage.tsx`) that currently pass it in; if the "insert saved
      prompt" capability itself (spec 019) is invoked from elsewhere, leave that entry point
      alone — only this composer-row button is removed
- [X] T040 [P] [US6] In
      `src/AskLucy.Web/ClientApp/src/features/chat/components/ExpandedChatPanel.tsx`, swap
      `RiExpandVerticalLine` → `RiExpandDiagonalLine` and `RiCollapseVerticalLine` →
      `RiCollapseDiagonalLine` in the height-toggle `IconButton` (~line 160) and update the
      corresponding imports; `onToggleHeight`/`isFullHeight` logic unchanged (FR-019)

### Tests for User Story 6

- [X] T041 [P] [US6] In `ChatComposer.test.tsx`, add a case asserting no element with the
      saved-prompts `aria-label`/icon exists in any rendered state (empty, typing, recording,
      continuous)
- [X] T042 [P] [US6] In
      `src/AskLucy.Web/ClientApp/src/features/chat/components/ExpandedChatPanel.test.tsx`,
      update the existing height-toggle icon assertions to expect the diagonal icon variants
- [X] T043 [P] [US6] Extend `src/AskLucy.Web/ClientApp/src/features/chat/components/ExpandedChatPanel.a11y.test.tsx`
      (existing file) to confirm the re-iconed height toggle still passes a11y checks

**Checkpoint**: All six user stories independently functional — full feature complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final verification across the whole feature, not specific to one story.

- [X] T044 **(partially — see note)** Run the full quickstart.md manual validation pass
      (US1–US6 scenarios plus the three spot-checked edge cases). Every scenario is covered
      by an equivalent automated `fireEvent`/`userEvent` interaction against the real rendered
      component tree (ChatComposer.test.tsx, ChatPage.test.tsx, MessageBubble.test.tsx —
      179 tests across the feature's own files, all passing). A live authenticated
      browser pass through the actual app was attempted but not completed: the app requires
      real ASP.NET Identity login (no dev-auth bypass or seeded test account exists), and this
      environment's only reachable database is the shared CI instance
      (`site4now.net`/`db_a15752_asklucytest`) — registering a throwaway account against it to
      complete E2E verification was judged not worth the shared-state risk for this pass. The
      backend (`dotnet run`) and frontend (`npm run dev`) were confirmed to build and boot
      cleanly (backend already runs `npm run build` + copies to `wwwroot` before every build,
      confirming the changed ClientApp code compiles into a servable bundle), and the public
      landing page rendered correctly via a Playwright screenshot — but the composer itself,
      which sits behind login, was not visually confirmed in a real browser.
- [X] T045 [P] Run `npm run lint` and `npx tsc -b --noEmit` in `src/AskLucy.Web/ClientApp`
      across the full changed set and fix any violations — **0 lint errors** (11 pre-existing
      warnings elsewhere in the codebase, unrelated to this feature), **0 typecheck errors**
- [X] T046 [P] Run the full `npm test` suite (not just touched files — per this project's own
      convention, page-level tests like `ChatPage.test.tsx` carry independent assertions that
      can regress from component-level changes) and confirm zero regressions outside this
      feature's own new/updated cases — **715/715 tests passing across all 148 test files**,
      zero regressions. (The very first baseline run at T001 showed 40 failures under heavy
      concurrent load in this sandbox; a clean re-run — see T001 note — confirmed those were
      environmental flakiness, not real breakage, before or after this feature's changes.)
- [X] T047 Re-read `spec.md`'s Success Criteria (SC-001–SC-007) against the finished feature
      and confirm each is met by the implemented behavior — see the "Success Criteria Review"
      section below for the per-criterion result.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — run first.
- **Foundational (Phase 2)**: N/A for this feature (see note in that phase).
- **User Stories (Phase 3–8)**: See "User Story Dependencies" below — this feature's stories
  are **not** all mutually independent, because US1/US2/US3/US4 share edits to the same file
  (`ChatComposer.tsx`)'s action-visibility gate.
- **Polish (Phase 9)**: Depends on all six user stories being complete.

### User Story Dependencies

- **User Story 1 (P1, MVP)**: No dependency on other stories. Establishes the empty↔typing
  gate in `ChatComposer.tsx` that US2, US3, US4, and (partially) US6 build on.
- **User Story 2 (P1)**: Depends on US1 (T002) — the mic is only reachable in the state US1
  defines. No new gesture logic (research.md Decision 1) — mostly verification + the
  FR-007 typing-state-transition confirmation.
- **User Story 3 (P2)**: Depends on US1 (T002), same reachability reasoning as US2. No
  dependency on US2, though both touch the same gesture-handling code region in
  `ChatComposer.tsx` — implement sequentially (not in parallel) to avoid merge conflicts on
  the same file/function. T014/T019 (the E1/F4 safeguard) touch the same gesture-handling
  functions as T012/T013/T015/T016 and should land in the same pass.
- **User Story 4 (P2)**: Depends on US1 (T002). Independent of US2/US3's gesture code, but
  shares `ChatComposer.tsx` and adds to `ChatPage.tsx` — implement sequentially relative to
  US2/US3 for the same single-file reason.
- **User Story 5 (P3)**: **No dependency on US1–US4.** Touches `MessageBubble.tsx` and new,
  separate state/effects in `ChatPage.tsx` (different region than US4's `onToggleMode`
  change) — can be implemented and tested in parallel with US1–US4 by a different
  contributor. T034 (E4) wraps the pre-existing `onStartCapture` pass-through — that function
  already exists in `ChatPage.tsx` regardless of US1–US4's completion, so this doesn't create
  a new cross-story dependency.
- **User Story 6 (P3)**: The saved-prompts removal (T038/T039) touches `ChatComposer.tsx` —
  no hard dependency on US1–US4's logic, but implementing it last avoids repeated merge
  resolution in a file already being edited by four other stories. The height-icon swap
  (T040) is in `ExpandedChatPanel.tsx`, fully independent of every other story — can be done
  any time, in parallel with anything.

### Parallel Opportunities

- T005/T006 (US1 tests) can run in parallel with each other once T002–T004 land.
- US5 (Phase 7, `MessageBubble.tsx`/`ChatPage.tsx` replay state) can be implemented in
  parallel with US1–US4 (Phase 3–6, `ChatComposer.tsx`) by a different contributor — no file
  overlap.
- T040 (US6 height-icon swap, `ExpandedChatPanel.tsx`) can be done any time in parallel with
  any other story.
- T024 (US4's `CollapsedVoiceControls.tsx` icon swap) is a different file from the rest of
  US4's tasks — parallelizable against T020–T023.
- Within any single story, all tasks marked `[P]` (typically the test tasks) can run in
  parallel with each other once that story's implementation tasks are done.
- US2 and US3 both edit `ChatComposer.tsx`'s gesture-handling region — do **not** parallelize
  these two stories' implementation tasks against each other, even though both are otherwise
  independent of one another.

---

## Parallel Example: User Story 5 (fully independent of the composer stories)

```bash
# Once Phase 1 (Setup) is done, these can start immediately, in parallel with Phases 3-6:
Task: "T030 Add playingMessageId/isManualReplay state + handleReplay/handleStopReplay in ChatPage.tsx"
Task: "T033 Add replay/stop IconButton to MessageBubble.tsx"

# After state/handlers exist:
Task: "T034 Wrap onStartCapture pass-through to stop an in-progress manual replay"

# After T030-T034 land, tests in parallel:
Task: "T035 MessageBubble.test.tsx replay-control cases"
Task: "T036 MessageBubble.a11y.test.tsx (new file)"
Task: "T037 ChatPage.test.tsx replay-coordination cases"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 3: User Story 1 (T002–T006).
3. **STOP and VALIDATE**: run `ChatComposer.test.tsx`/`ChatComposer.a11y.test.tsx` and the
   US1 section of quickstart.md manually.
4. This is a legitimate, demoable MVP: the composer's empty/typing visual behavior is
   correct even before voice/continuous/replay/chrome work lands.

### Incremental Delivery

1. Setup → US1 (MVP: empty/typing gate correct).
2. Add US2 (click-to-talk) → validate → the composer's primary voice-input path works.
3. Add US3 (hold-to-talk, including the E1/F4 indefinite-recording safeguard) → validate →
   the fast voice-input path works and can't get stuck, even on a brief tap.
4. Add US4 (continuous conversation, including the C1 async-ordering fix) → validate →
   hands-free mode works end-to-end and survives a failed preference save.
5. Add US5 (replay, including the F1/F2/E4 disabled/stop-state and reverse-direction fixes)
   → validate → can be delivered any time after Setup, independent of the above (see
   Parallel Opportunities).
6. Add US6 (chrome cleanup) → validate → saved-prompts gone, height icons updated.
7. Phase 9 Polish → full quickstart.md pass, lint/typecheck/full-suite, Success Criteria
   review.

### Parallel Team Strategy

With two contributors:

1. Both complete Phase 1 (Setup) together.
2. Contributor A: US1 → US2 → US3 → US4 → US6's `ChatComposer.tsx` piece (T038/T039),
   sequentially, since all of these share `ChatComposer.tsx`.
3. Contributor B: US5 (fully independent, `MessageBubble.tsx`/`ChatPage.tsx` replay state) in
   parallel with A, plus US6's `ExpandedChatPanel.tsx` height-icon swap (T040) whenever
   convenient.
4. Both converge for Phase 9 Polish.

---

## Notes

- `[P]` tasks touch different files (or, within a story, are pure test additions after that
  story's implementation tasks) with no dependency on an incomplete task.
- Every implementation task names the exact file it touches — no task should require
  guessing a location.
- US2 and US3 are described as separate stories per spec.md's priorities, but per
  research.md Decision 1 almost all of their underlying gesture mechanics already exist and
  work today — their tasks above are weighted toward *verification and the new
  visibility-gate integration*, not building new gesture-recognition logic.
- Two internal spec.md inconsistencies were caught and corrected during the original
  task-generation pass (not new product decisions — mechanical fixes to align the spec with
  its own authoritative mockup images): (1) US2's click-to-talk entry was described as
  reachable from both the empty *and* typing composer states, contradicting FR-002/Figure 2
  (which shows no mic once typing starts) — corrected to empty-only, matching US3's
  hold-to-talk framing; (2) click-to-talk's confirm action was described as returning to the
  *empty* appearance despite placing text in the field — corrected to transition to the
  typing-state appearance, matching hold-to-talk's already-correct FR-010.
- A round-1 `/speckit-analyze` pass found and fixed four issues, all in the design docs
  rather than spec.md itself: **F1** (the replay control couldn't tell an auto-spoken reply
  from a user-initiated replay, so it would have shown an interactive Stop button during
  every automatic narration, contradicting FR-021), **F2** (the replay control's disabled
  condition was missing the "a recording/listening session is active" term required by
  spec.md's Edge Cases/Assumptions), **E1** (no task addressed the spec-mandated safeguard
  against an indefinite hold-to-talk recording if the tab loses focus or the screen locks),
  and **C1** (the one-click continuous-conversation hybrid didn't specify
  ordering/error-handling against the persisted preference save's own async
  success/failure). See T020/T026 (C1) and T030–T033/T035–T037 (F1/F2).
- A round-2 `/speckit-analyze` pass, re-checking the round-1 fixes themselves, found and
  fixed two more: **F4** (round 1's E1 fix routed through the elapsed-time gesture dispatcher,
  which would leave a short, still-tap-classified press recording in the background instead
  of actually stopping it — corrected to call `onStopCapture()` unconditionally) and **E4**
  (nothing stopped an in-progress *manual* replay when a new recording/listening session
  started — the reverse direction of F2 — added as T034/extended into T037). See T014/T019
  (E1/F4) and T034/T037 (E4).

---

## Success Criteria Review (T047)

- **SC-001** (zero layout glitches/incorrect action visibility across 100% of states) — ✅
  Met. `ChatComposer.test.tsx`'s "state-dependent action visibility" suite asserts the exact
  visible-control set for every defined state (empty, typing, click-review, hold-active,
  continuous idle-listening) explicitly.
- **SC-002** (95%+ click-to-talk success rate under normal conditions) — ⚠️ Not directly
  measurable by this pass. This is a real-world reliability statistic about the existing
  transcription service, not something a unit/integration test can assert — it requires
  production usage data or manual field testing over time. The underlying transcription call
  itself is unchanged by this feature (research.md Decision 1); no new failure mode was
  introduced.
- **SC-003** (hold-to-talk no slower than click-to-talk, by discrete-action count) — ✅ Met
  by construction: hold-to-talk is press+release (2 actions, no confirm step) vs.
  click-to-talk's click+click (2 actions, confirm required) — verified via
  `ChatComposer.test.tsx`'s hold-to-talk tests showing `onStopCapture` fires directly on
  release with no intermediate review step.
- **SC-004** (zero saved-prompts instances, all states) — ✅ Met. `ChatComposer.test.tsx`'s
  "saved-prompts action removed" suite checks all four reachable states explicitly.
- **SC-005** (start/mute/type/exit continuous mode, 100% return to empty on exit) — ✅ Met.
  `ChatPage.test.tsx`'s "one-click entry..." integration test exercises the full round trip
  end-to-end against the real component tree; exit reliably returns to the Push-to-Talk empty
  appearance.
- **SC-006** (never two replies playing simultaneously) — ✅ Met. Enforced structurally (one
  shared `useVoiceOutput` channel, `handleReplay` always stops before starting) and verified
  by `ChatPage.test.tsx`'s replay-coordination tests, including the FR-023 "switch between two
  playing replies" scenario that surfaced and led to a mid-implementation formula fix (see the
  "Implementation-time fix" note above).
- **SC-007** (every failure produces a visible message, zero silent failures) — ⚠️ Partially
  verified. Recording/permission failures reuse the pre-existing, still-tested
  `captureError`/`permissionState` Alert/Snackbar path (`ChatComposer.test.tsx`'s "microphone
  permission" suite), and the E1/F4 safeguard prevents the one new stuck-state risk this
  feature introduced (an orphaned hold-to-talk recording). The one gap flagged but not
  approved for fix during `/speckit-analyze` (finding E2) remains open: no dedicated test
  asserts what the replay control shows if `useVoiceOutput.speak()` itself throws during a
  manual replay. The clearing effect (`if (!tts.isSpeaking) { clear playingMessageId/
  isManualReplay }`) should cover it structurally, since `useVoiceOutput`'s own `finally`
  block sets `isSpeaking` false on error — but this specific path is untested.

**Net**: 5 of 7 criteria fully verified by this pass; SC-002 requires production data by its
nature; SC-007 is verified for every path this feature added except one previously-flagged,
not-approved-for-fix edge case (E2).
