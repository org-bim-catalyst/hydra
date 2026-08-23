---

description: "Task list for SPEC-033: Hold-to-Talk Simplification & Self-Listening Fix"
---

# Tasks: Hold-to-Talk Simplification & Self-Listening Fix

**Input**: Design documents from `/specs/033-hold-to-talk-and-echo-fix/`

**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Included — constitution §10 requires them, and this feature exists specifically because
undetected gaps (an unclassified failure path, an unfixed pointer-capture bug, dead-but-inert
interruption code) shipped unnoticed before.

**Organization**: Tasks are grouped by user story (US1 P1, US2 P1, US3 P2) per spec.md.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Maps the task to spec.md's US1/US2/US3

## Path Conventions

Web app (existing structure): `src/AskLucy.Infrastructure` (backend),
`src/AskLucy.Web/ClientApp/src` (frontend SPA), `tests/AskLucy.Infrastructure.Tests` (backend
tests).

---

## Phase 1: Setup

- [X] T001 Confirm branch `033-hold-to-talk-and-echo-fix` and `.specify/feature.json` point at
  `specs/033-hold-to-talk-and-echo-fix` (already done during `/speckit-plan`)
- [X] T002 Confirm `dotnet build` and `npx tsc -b --noEmit` (ClientApp) both succeed on the current
  tree before making changes, to establish a clean baseline

---

## Phase 2: Foundational

**Purpose**: No shared blocking infrastructure — each user story touches disjoint files (US1:
one backend method; US2: one frontend component; US3: two frontend voice hooks). Proceed directly
to Phase 3.

---

## Phase 3: User Story 1 - Transcription reliability & deployment discipline (Priority: P1) 🎯 MVP

**Goal**: Close the remaining unclassified failure mode (malformed 2xx response) behind the
still-reproducing production 500, and make this round's fix's actual deployment an explicit,
verified part of "done."

**Independent Test**: A mocked malformed/empty 2xx transcription response produces a specific 502
Problem Details response, not a generic 500. Separately, `git log`/the merged PR confirms this
feature's (and SPEC-032's) changes are committed and deployed.

### Tests for User Story 1 ⚠️ Write first, confirm they fail before implementing

- [X] T003 [US1] Update `tests/AskLucy.Infrastructure.Tests/Ai/OpenAIProviderTests.cs`: add a test
  asserting a mocked 2xx response with an empty body, non-JSON body, or a JSON body missing the
  `text` property throws `AiProviderUnavailableException` (not an unhandled `JsonException`/
  `InvalidOperationException`); confirm the existing 400/401/403/429/500 tests from SPEC-032 still
  pass unchanged (regression)

### Implementation for User Story 1

- [X] T004 [US1] In `src/AskLucy.Infrastructure/Ai/OpenAIProvider.cs`'s `TranscribeAudioAsync`
  (`:187-206`), wrap the `JsonDocument.ParseAsync(stream)` /
  `document.RootElement.GetProperty("text")` block in a `try/catch` for `JsonException` and
  `InvalidOperationException`, rethrowing as `AiProviderUnavailableException("The AI service could
  not process your request. Please try again.")` (reuses the existing type/message/mapping — no
  new exception type, per research.md Decision 1)

**Checkpoint**: US1's code-level fix is complete and independently testable. The
"actually deployed" half of this story is verified in Phase 6 (T0XX, after implementation and
`/speckit-cicd`).

---

## Phase 4: User Story 2 - Pure hold-to-talk gesture (Priority: P1)

**Goal**: Fix the root-caused pointer-capture bug and simplify Push-to-Talk in the Expanded
panel's `ChatComposer` to a single press-and-hold-then-release gesture, with no dual tap-toggle
mode and no mid-recording Finish/Cancel buttons.

**Independent Test**: Press-and-hold the mic, speak, release — transcribes immediately every
time, regardless of hold duration. A quick tap behaves identically (brief hold, same
transcribe-on-release path), never leaving a recording running unattended. The Collapsed widget's
separate click-to-toggle flow is unaffected.

### Tests for User Story 2 ⚠️ Write first, confirm they fail before implementing

- [X] T005 [P] [US2] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/
  ChatComposer.test.tsx`: replace the tap-vs-hold-duration tests with tests asserting (a) a
  pointerdown followed immediately by pointerup calls `onStopCapture` (finish+transcribe),
  regardless of elapsed time, (b) `event.currentTarget.setPointerCapture` is called on
  pointerdown with the event's `pointerId`, (c) no `RecordingReviewControls`-rendered
  Finish/Cancel buttons (`getByRole('button', {name: /finished speaking/i})` /
  `/cancel recording/i`) ever appear while a Push-to-Talk recording is active in `ChatComposer`,
  and (d) `pointerleave`/`pointercancel`/`keyup(Space)` during an active recording also trigger
  `onStopCapture`

### Implementation for User Story 2

- [X] T006 [US2] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`,
  remove `HOLD_THRESHOLD_MS`, `captureStartedAtRef`, `suppressNextClickRef`, and
  `handleMicClick`; rewrite `handleMicPointerDown` to call
  `event.currentTarget.setPointerCapture(event.pointerId)` then `onStartCapture()`
  unconditionally; rewrite `handleMicPointerUp` (also used for `onPointerLeave`/
  `onPointerCancel`) to call `onStopCapture()` unconditionally, with no duration check;
  simplify `handleMicKeyDown`/`handleMicKeyUp` the same way (always start on keydown, always
  stop on keyup, no threshold)
- [X] T007 [US2] In the same file's render logic, stop rendering `RecordingReviewControls` during
  a Push-to-Talk recording; keep the mic `IconButton` mounted as the single, same element
  throughout press → recording → releasing → `'transcribing'` (visual state changes only —
  color/icon/disabled — never a component swap); keep the `VoiceAnalyzer` waveform rendering
  alongside it unchanged; remove the now-unused `RecordingReviewControls` import if no longer
  referenced elsewhere in this file

**Checkpoint**: US2 is independently functional — Push-to-Talk in the Expanded panel is a single,
reliable hold gesture. `CollapsedVoiceControls.tsx`, `RecordingReviewControls.tsx`,
`useVoiceRecorder.ts`, and `ChatPage.tsx`'s `voiceControlsProps` wiring remain untouched (verify
via `git status` in T0XX).

---

## Phase 5: User Story 3 - No self-listening in Continuous mode (Priority: P2)

**Goal**: Fully mute microphone input for the duration of Lucy's spoken replies, removing the
now-superseded mid-response interruption feature and its dead-code remnants.

**Independent Test**: During `AiSpeaking`, the mic's audio track is disabled; on leaving
`AiSpeaking`, it's re-enabled. No `'Interrupted'` state transition occurs at any point.

### Tests for User Story 3 ⚠️ Write first, confirm they fail before implementing

- [X] T008 [P] [US3] Update `src/AskLucy.Web/ClientApp/src/features/chat/voice/
  useConversationAudio.test.ts`: add/adjust tests asserting `recognition.setInputMuted(true)` is
  called when `voiceState.state` becomes `'AiSpeaking'` and `setInputMuted(false)` is called when
  it leaves that state (turn completion and `stop()`); remove/replace any existing test asserting
  `'Interrupted'` state or duck/undo-duck behavior — that mechanism no longer exists

### Implementation for User Story 3

- [X] T009 [P] [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/voice/
  useSpeechRecognition.ts`, add a `setInputMuted(muted: boolean): void` function toggling
  `streamRef.current?.getAudioTracks().forEach(t => { t.enabled = !muted })` (safe no-op if no
  stream is active), export it from the hook's return object; add `echoCancellation: true` to the
  `audioConstraint` object used by `getUserMedia` (both the `boolean` and `MediaTrackConstraints`
  branches); remove the `onLocalSpeechLikely` option/parameter and the peak-amplitude-threshold
  check in the audio worklet's `onmessage` handler that calls it (dead per research.md Decision 4).
  Also update `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.test.ts`:
  remove/replace the existing `onLocalSpeechLikely`-fires-on-loud-audio test (lines ~426-454,
  which will otherwise reference a removed feature — caught by `/speckit-analyze`, finding C1)
  with a test asserting `setInputMuted(true)` disables the active stream's audio tracks and
  `setInputMuted(false)` re-enables them
- [X] T010 [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.ts`,
  remove `handleLocalSpeechLikely`, `isDuckedRef`, `duckTimeoutRef`, `clearDuckTimeout`, the
  `onLocalSpeechLikely` option passed to `useSpeechRecognition`, and the `'Interrupted'`-related
  branch in `handleFinalTranscript` (the `wasInterruption`/`synthesis.abort()`/`analyzer.reset()`
  early path — confirm via the updated T008 tests whether any of this remains reachable through a
  different path, e.g. the user's own explicit `stop()`, and preserve only what's still needed);
  add `recognition.setInputMuted(true)` where `voiceState.setState('AiSpeaking')` is called in
  `runAssistantTurn`'s `onAudioChunk` callback, and `recognition.setInputMuted(false)` immediately
  after `analyzer.reset()` in `runAssistantTurn` (post-turn) and in `stop()`

**Checkpoint**: All three user stories are independently functional; Continuous mode no longer
self-triggers on Lucy's own voice, at the deliberate cost of mid-response interruption (per the
resolved clarification).

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T011 [P] Run `dotnet build` and the full `AskLucy.Infrastructure.Tests` suite — confirm
  everything passes, including the T003 addition and all pre-existing tests. Full solution build:
  0 errors. Infrastructure.Tests: 126/126 pass. Web.Tests: 296/297 pass — the one failure is the
  same known pre-existing shared-CI-test-DB migration issue noted in SPEC-032 (unrelated to this
  feature; this round touched no persistence/migration code).
- [X] T012 [P] Run `npx tsc -b --noEmit` and the full ClientApp Vitest suite — confirm everything
  passes, including `ChatComposer.test.tsx`'s full suite (not just the US2 additions) and
  `CollapsedVoiceControls.test.tsx`/`CollapsedChatControl.test.tsx` (regression-proving the
  untouched Collapsed-widget flow still works). `tsc -b`: clean. First full run surfaced 6
  failures in `ChatPage.test.tsx`/`ChatPage.a11y.test.tsx` — their own integration-level tests
  still asserted the old "Finished speaking" button/tap-to-toggle flow (same class of gap T014
  in SPEC-032 caught: a component's own unit tests were updated, but a page-level test elsewhere
  exercising it wasn't). Fixed by rewriting to pointerdown/pointerup and, for two of them,
  restoring a `waitFor` between press and release that the rewrite had dropped — the recorder's
  `start()` is genuinely async (`getUserMedia`/`AudioContext` setup) before `phase` becomes
  `'recording'`, so an immediate `fireEvent.pointerUp` right after `pointerDown` with no flush
  can race ahead of that and make `finish()`'s `phase !== 'recording'` guard no-op (test-only
  timing artifact — `fireEvent` is synchronous where a real hold always has some duration).
  Final: 145 files / 653 tests, all pass.
- [ ] T013 Run quickstart.md Scenarios 1–3 manually against a local dev build (mic/speaker
  hardware required for Scenario 3)
- [X] T014 Re-verify `git status` shows only this feature's (and SPEC-032's still-pending) intended
  files as modified; confirm `RecordingReviewControls.tsx`, `CollapsedVoiceControls.tsx`,
  `useVoiceRecorder.ts`, and `ChatPage.tsx` are NOT among the changed files (research.md Decision
  3's scope boundary). Confirmed: `RecordingReviewControls.tsx`, `CollapsedVoiceControls.tsx`,
  and `ChatPage.tsx` (non-test) are clean. `useVoiceRecorder.ts` shows modified, but that diff is
  SPEC-032's own still-uncommitted filename-extension fix from last round (predates this
  feature's work) — SPEC-033 itself never opened this file.
- [ ] T015 Run this feature's full `/speckit-cicd` pass to completion — commit, push, PR, CI,
  merge, and **explicitly verify the deployed production build reflects this commit** (FR-004/
  SC-003) — this task is itself a functional requirement of the feature, not optional follow-up

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Empty — proceed directly from Setup.
- **User Story 1 (Phase 3)**: Depends on Setup only. Backend-only; fully independent of US2/US3.
- **User Story 2 (Phase 4)**: Depends on Setup only. Touches only `ChatComposer.tsx`/its test —
  disjoint from US1 (backend) and US3 (`useSpeechRecognition.ts`/`useConversationAudio.ts`). Can
  run in parallel with both.
- **User Story 3 (Phase 5)**: Depends on Setup only. Disjoint files from US1/US2.
- **Polish (Phase 6)**: Depends on all three user stories being complete. T015 (the `/speckit-cicd`
  pass) is the final task and depends on everything else passing first.

### Within Each User Story

- Tests (T003, T005, T008) before their corresponding implementation (T004, T006-T007, T009-T010).
- T006 before T007 (gesture-handling logic before the render-logic change that depends on it being
  correct) — same file, sequential.
- T009 before T010 (the hook exposing `setInputMuted` must exist before the caller uses it) — but
  both are `[P]`-eligible against US1/US2's files since they don't touch those.

### Parallel Opportunities

- T003 (US1 test) and T005/T008 (US2/US3 tests) can be written in parallel — different files.
- T006-T007 (US2) and T009-T010 (US3) can proceed fully in parallel — no shared files.
- T011 and T012 (backend vs frontend full-suite verification) can run in parallel.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 3: User Story 1 (T003-T004).
3. **STOP and VALIDATE**: the malformed-response test passes; existing classification tests
   unaffected.
4. This alone closes the last known gap behind the recurring production 500; US2/US3 can follow
   immediately after, and all three should ship together in one `/speckit-cicd` pass (T015) so the
   "was it actually deployed" question this feature exists to resolve doesn't recur.

### Incremental Delivery

1. Setup → User Story 1 → validate.
2. Add User Story 2 → validate (including the Collapsed-widget regression check).
3. Add User Story 3 → validate (mute/unmute timing, no dead-code remnants).
4. Phase 6 Polish → full quickstart.md pass → `/speckit-cicd` to completion (T015) — this feature
   is not "done" until this task closes.

## Notes

- [P] tasks touch different files with no dependency on each other.
- Per research.md Decision 3: `RecordingReviewControls.tsx`, `CollapsedVoiceControls.tsx`,
  `useVoiceRecorder.ts`, and `ChatPage.tsx` are deliberately **not** touched by any task in this
  list — verify this stays true (T014) rather than assuming it.
- Per research.md Decision 2/plan.md's Constraints: T015 (the `/speckit-cicd` pass, including
  deployment verification) is elevated to a functional requirement (FR-004) — do not report this
  feature complete without it, unlike prior rounds where CI/CD was a separate, optional follow-up
  step.
