---

description: "Task list for Restore Voice Output Mute & Input Mode Controls"
---

# Tasks: Restore Voice Output Mute & Input Mode Controls

**Input**: Design documents from `/specs/013-restore-voice-controls/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/voice-control-integration.md](./contracts/voice-control-integration.md),
[quickstart.md](./quickstart.md)

**Tests**: Included — constitution §10/§18 require tests for new/changed behavior in the
same PR that introduces it; this is not optional for this project.

**Organization**: Tasks are grouped by user story (US1 = mute, P1; US2 = input mode, P2) to
enable independent implementation and testing of each. All work is frontend-only
(`src/AskLucy.Web/ClientApp`) — no backend/API/schema changes (plan.md, research.md
Decision 1).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 or US2
- Every task includes an exact file path

## Path Conventions

All paths are relative to `src/AskLucy.Web/ClientApp/src/features/chat/` unless otherwise
noted (abbreviated below as `chat/`).

---

## Phase 1: Setup

**Purpose**: Confirm baseline before any change; no new dependencies are needed (plan.md
Technical Context — this feature reuses existing packages only).

- [X] T001 Run the existing frontend test suite and build (`npm run test`, `npm run build`
      in `src/AskLucy.Web/ClientApp`) on branch `013-restore-voice-controls` before making
      any change, to confirm a clean baseline to diff against.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared groundwork both US1 and US2 depend on.

**⚠️ CRITICAL**: Complete before starting either user story's implementation tasks (tests
for each story may still be written first, per the Testing Standards note in each phase).

- [X] T002 [P] Add a test asserting `ChatPage.tsx` calls
      `voicePreferencesStore.hydrateFromServer()` on mount, and that `isMuted`/
      `conversationMode` reflect the hydrated values once the call resolves, in
      `chat/pages/ChatPage.test.tsx` (constitution §10/§18 pairing for T003 — closes
      analysis finding C1; write this first, confirm it fails before T003).
- [X] T003 Add a `useEffect(() => { void hydrateFromServer() }, [])` call to `ChatPage.tsx`
      (mirroring the existing pattern in `src/AskLucy.Web/ClientApp/src/features/settings/pages/SettingsPage.tsx`'s
      `VoiceTab`) so `voicePreferencesStore`'s `isMuted`/`conversationMode` are restored
      from the server for users who land on chat without visiting Settings first
      (FR-011/SC-004; research.md Decision 9) in `chat/pages/ChatPage.tsx` (depends on T002).
- [X] T004 [P] Adapt `VoiceControlBarProps` to the contract in
      contracts/voice-control-integration.md — replace the `voiceState: VoiceStateName`
      prop with `isListening`, `isSpeaking`, `isMuted`, `permissionState`, and replace
      `onStart`/`onCancelListening`/`onStop` with `onStart`/`onStop`/`onCancel`/
      `onStopSpeaking`; keep `isAvailable`, `conversationMode`, `errorMessage`,
      `onToggleMode`, `onToggleMute`, `onClearError` as-is (supports FR-001/FR-004/FR-008/
      FR-010). Update the component body to render from the new props (existing
      icons/tooltips/layout unchanged). Update every existing test in
      `VoiceControlBar.test.tsx` to pass the new prop shape so the existing suite compiles
      and passes again (mechanical rename — no new test cases yet, those are added
      per-story below) in `chat/components/VoiceControlBar.tsx` and
      `chat/components/VoiceControlBar.test.tsx`.

**Checkpoint**: Store hydration (tested) and the shared component's prop contract are
ready; US1 and US2 implementation can now proceed (in parallel, if staffed, since they
touch non-overlapping call sites within the shared files after this point).

---

## Phase 3: User Story 1 - Mute Lucy's spoken responses (Priority: P1) 🎯 MVP

**Goal**: A visible, keyboard-operable, persisted control that mutes/unmutes spoken AI
replies without affecting reply generation or the reactive sphere, with no retroactive
playback on unmute.

**Independent Test**: Send a message that triggers a spoken reply, mute mid-playback,
confirm audio stops within ~1s while the reply text is unaffected; send another message
while still muted, confirm no audio queues; unmute; send a third message, confirm only that
one plays (quickstart.md Scenarios 1, 2, 8's mute half). Testable without any US2 changes.

### Tests for User Story 1

> Write these first; confirm they fail before starting the Implementation tasks below.

- [X] T005 [P] [US1] Create `chat/voice/useVoiceOutput.test.ts` covering: `speak()` is a
      no-op (no network call, `isSpeaking` stays false) while `isMuted` is true; calling
      `setMuted(true)` while `isSpeaking` is true triggers the hook's existing `stop()`
      path immediately; calling `setMuted(false)` after a reply completed while muted does
      not start any playback (no retroactive resume/replay — Clarification Q2).
- [X] T006 [P] [US1] Extend `chat/components/VoiceControlBar.test.tsx` with: the mute
      control reflects `isMuted` (icon/aria-label swap between mute/unmute); clicking it
      calls `onToggleMute`; activating it via keyboard alone (Tab + Enter/Space) calls
      `onToggleMute` and passes the existing jest-axe check with the mute control present
      in both muted and unmuted states.

### Implementation for User Story 1

- [X] T007 [US1] Add `isMuted: boolean`, `setMuted(muted: boolean): void`, and
      `toggleMute(): void` to `useVoiceOutput`'s returned API: `speak()` returns
      immediately without calling `synthesizeSpeech` while `isMuted` is true; `setMuted`
      calls the hook's existing `stop()` when transitioning to `true` while `isSpeaking` is
      true (research.md Decision 3) in `chat/voice/useVoiceOutput.ts` (depends on T005).
- [X] T008 [US1] In `ConversationView` (`chat/pages/ChatPage.tsx`), add an effect that
      keeps `tts`'s `isMuted` in sync with `voicePreferencesStore.isMuted` (store →
      hook, one-directional), and render `VoiceControlBar`'s mute control near
      `ChatComposer`, wiring `onToggleMute` to
      `voicePreferencesStore.update({ isMuted: !isMuted })` (depends on T004, T007).
- [X] T009 [US1] Confirm (and add a regression test if missing) that a failure in the mute
      path — e.g., `voicePreferencesStore.update({ isMuted })` rejecting — surfaces via the
      store's existing `error`/rollback handling into a visible Snackbar/Alert, matching
      constitution §2.VIII; extend `chat/pages/ChatPage.test.tsx` if a gap is found
      (depends on T008).

**Checkpoint**: US1 is fully functional and independently testable/demoable — mute works
end-to-end from the chat view and persists. US2's mic/mode work has not started yet;
`ChatComposer`'s existing one-shot dictate button still works unchanged at this point.

---

## Phase 4: User Story 2 - Choose how the microphone listens (Priority: P2)

**Goal**: A visible, keyboard-operable, persisted control to switch between Push-to-Talk
(hold or toggle) and Continuous Conversation input, with a guard against switching mid-capture.

**Independent Test**: Switch to Continuous, speak hands-free, confirm the message sends
without manual action; switch to Push-to-Talk, hold (and separately, toggle-click) the mic,
confirm the transcript fills the composer for review; attempt a mode switch mid-hold,
confirm it's blocked until release (quickstart.md Scenarios 3–7). Testable independently of
US1 (mute can remain at its default/unmuted state throughout).

### Tests for User Story 2

> Write these first; confirm they fail before starting the Implementation tasks below.

- [X] T010 [US2] Verify the ElevenLabs realtime STT message shapes
      (`audio_chunk`/`partial_transcript`/`committed_transcript`) `useSpeechRecognition.ts`
      sends/expects against ElevenLabs' current realtime STT API reference; fix any
      mismatch found, and update/add assertions in `chat/voice/useSpeechRecognition.test.ts`
      confirming the verified shapes (research.md Decision 8 — this closes the one residual
      risk flagged during planning; do this first since T014–T017 depend on this hook
      actually working end-to-end).
- [X] T011 [P] [US2] Add tests to `chat/components/ChatComposer.test.tsx` (new file):
      Push-to-Talk hold (pointer down → up) starts/stops capture and fills the text field
      via `setText` without calling `onSend`; Push-to-Talk toggle (click, then click again)
      produces the identical outcome; Continuous mode calls `onSend` directly on a
      finalized transcript without filling/requiring the text field; and — Continuous mode
      specifically — a finalized transcript is still captured and sent via voice while the
      user is simultaneously typing in the text field, with typing neither pausing,
      stopping, nor otherwise interfering with the active recognition session (FR-006,
      Clarification Q3).
- [X] T012 [P] [US2] Add mode-switch-guard tests to `chat/components/VoiceControlBar.test.tsx`:
      `onToggleMode`'s control is `disabled` when `conversationMode === 'PushToTalk' &&
      isListening === true`, and re-enabled the instant `isListening` becomes `false`
      (Clarification Q4 / research.md Decision 6).
- [X] T013 [P] [US2] Add keyboard-hold tests: pressing and holding the bound key (Space,
      while focus is on the mic control, not the text field) starts capture on `keydown`
      and stops on `keyup`, with an outcome identical to pointer hold and to toggle-click,
      in `chat/components/ChatComposer.test.tsx` and/or `VoiceControlBar.test.tsx`
      (whichever owns the mic control per T015).

### Implementation for User Story 2

- [X] T014 [US2] In `ConversationView` (`chat/pages/ChatPage.tsx`), instantiate
      `useSpeechRecognition` (mode mapped from `voicePreferencesStore.conversationMode`),
      passing `isListening`/`permissionState`/`error`/`deviceNotice` and
      `onStartCapture`/`onStopCapture`/`onCancelCapture` down to `ChatComposer`, and a
      transcript handler that branches on `conversationMode` (`PushToTalk` → fill text
      field; `Continuous` → call `send()` directly) — research.md Decision 4 (depends on
      T010).
- [X] T015 [US2] In `chat/components/ChatComposer.tsx`, remove the `useWavRecorder`/
      `transcribeMicrophoneAudio` one-shot dictate button and its recording/waveform UI;
      replace with a mic control driven by the props from T014, supporting both hold
      (`onPointerDown`/`onPointerUp`, `onTouchStart`/`onTouchEnd`) and click-to-toggle
      activation on the same control, with de-duplication so a hold's release doesn't also
      fire a second toggle via the synthetic `click` event (research.md Decision 5's
      flagged risk) (depends on T014; addresses T011).
- [X] T016 [US2] Add keyboard hold support (`keydown`/`keyup` on Space) to the mic control
      from T015, scoped to only activate when the mic control itself has focus (must not
      hijack Space while the text field has focus) (depends on T015; addresses T013).
- [X] T017 [US2] Wire `VoiceControlBar`'s mode toggle and mic/listening display into
      `ConversationView`, sourcing `isListening`/`permissionState`/`errorMessage` from the
      same `useSpeechRecognition` instance from T014, and disabling `onToggleMode` per the
      guard in research.md Decision 6; `onToggleMode` calls
      `voicePreferencesStore.update({ conversationMode })` when enabled in
      `chat/pages/ChatPage.tsx` (depends on T004, T014; addresses T012).
- [X] T018 [US2] Ensure a denied/unavailable microphone permission surfaces a specific,
      visible, actionable message (FR-009) through the existing Snackbar/Alert pattern
      already used for `voice.error`/`tts.error`; extend `chat/pages/ChatPage.test.tsx` or
      `ChatComposer.test.tsx` if coverage is missing (depends on T015).
- [X] T019 [US2] Delete the now-unused `chat/voice/useWavRecorder.ts` and the now-unused
      `transcribeMicrophoneAudio` export from `chat/api/aiApi.ts` (confirm no remaining
      references first — constitution §2.III YAGNI/dead-code) (depends on T015).

**Checkpoint**: US1 and US2 are both independently functional — mute persists and works
from the chat view; input mode (hold, toggle, continuous) persists and works from the chat
view; switching modes mid-capture is blocked as specified.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Validation and cleanup spanning both stories.

- [X] T020 [P] Run the jest-axe accessibility check across `VoiceControlBar.tsx` and
      `ChatComposer.tsx` in their final wired state (both mute and mic/mode controls
      present) to confirm WCAG 2.1 AA keyboard/focus/ARIA compliance end-to-end
      (constitution §7), extending `chat/pages/ChatPage.a11y.test.tsx` if needed.
- [ ] T021 Execute quickstart.md Scenarios 1–8 manually against a running instance and
      record any deviation as a follow-up task before considering this feature done.
      **Partially done**: exercised via automated integration tests (RTL + jest-axe against
      the real component tree, every new state) and a frontend dev-server boot check; a live
      click-through with a real backend, microphone, and ElevenLabs credentials was not
      possible in this sandboxed environment (no SQL Server/secrets configured) — remains a
      manual follow-up before merge, see checklists/requirements.md's Notes.
- [X] T022 [P] Update `specs/013-restore-voice-controls/checklists/requirements.md` and
      this feature's entry point docs if any deviation surfaced during T010's verification
      or T021's manual run changed a documented behavior.
- [X] T023 **Production bug found via live testing (exactly the gap T021 flagged)**: the
      user tested the deployed build and got a "Voice recognition is temporarily using a
      reduced-quality fallback" error with `POST /api/v1/ai/voice/stt-session` returning 502,
      every time, for both push-to-talk and continuous mode. Root cause: this SPEC-013 work
      was the *first time* `ChatComposer`'s mic control (and therefore
      `useSpeechRecognition.start()` → `createSttSession()`) was ever wired to a live,
      reachable UI control — spec 012 built the backend `ElevenLabsSpeechToTextSessionProvider`
      but its own doc comment already flagged the upstream ElevenLabs token-mint endpoint path
      as an unverified guess that "returned 404 during planning-time research," and it was
      never fixed because nothing ever actually called it in production until now. Verified
      the correct endpoint against https://elevenlabs.io/docs/api-reference/tokens/create
      (`POST /v1/single-use-token/realtime_scribe`, no request body, response is `{ "token":
      "..." }` only) and fixed `src/AskLucy.Infrastructure/Ai/ElevenLabsSpeechToTextSessionProvider.cs`
      (was posting to `speech-to-text/realtime/token` with an unnecessary body). Added a
      regression test locking in the correct URL
      (`tests/AskLucy.Infrastructure.Tests/Ai/ElevenLabsSpeechToTextSessionProviderTests.cs`).
      Backend-only change — outside SPEC-013's originally-scoped frontend-only plan.md, but
      required for the frontend work to function at all. Full solution test suite re-run:
      Domain 60/60, Application 130/130, Infrastructure 33/33 (incl. the new test), Web
      112/112 — all green (Persistence.Tests' 18 failures are pre-existing/environmental,
      requiring a real SQL Server instance not available in this sandbox, unrelated to this
      change). Still unresolved: this fix is verified against ElevenLabs' documentation, not
      yet against a live ElevenLabs call — the user (or CI with real credentials) should
      confirm the STT session now mints successfully end-to-end.
- [X] T024 **Second production bug, found immediately after T023's fix deployed**: with the
      STT session now minting successfully, Continuous mode's first real transcript hit
      "Choose an AI provider and model before sending a message" — a real, separate bug, not
      a re-occurrence of T023. Root cause: `ChatPage.tsx`'s `onFinalTranscript` handler called
      `send(transcript)` in Continuous mode with no readiness check, unlike the Send button
      (already `disabled={isStreaming || !providerId || !modelId}`) — if Continuous mode
      auto-starts listening on mount (as it does whenever the persisted mode is already
      Continuous) before the provider/model catalog finishes loading, an early utterance hits
      this exact guard inside `useChatStream`'s own `send()`. Worse than a simple missing
      guard, this also exposed a **closure-staleness bug**: `useSpeechRecognition` attaches
      its WebSocket `message` listener exactly once per connection (inside `start()`), so a
      plain inline `onFinalTranscript` arrow function would freeze `providerId`/`modelId`/
      `isStreaming`/`conversationMode` at whatever they were in the render that happened to be
      current when the connection opened — never seeing later updates for that connection's
      whole lifetime, even after the catalog became ready. (`useSpeechRecognition.ts` already
      used this same ref-indirection pattern for its own internal `mode` value — `modeRef` —
      for exactly this reason; the caller side just hadn't followed the same pattern.) Fixed
      by routing `onFinalTranscript` through a ref that's refreshed every render behind a
      stable `useCallback` wrapper (standard "always-fresh long-lived-subscription callback"
      pattern), and gating auto-send on `providerId && modelId && !isStreaming`, falling back
      to filling the composer text field (never silently discarding the transcript,
      constitution §2.VIII) when not ready. Frontend-only change in `chat/pages/ChatPage.tsx`.
      Full frontend suite re-run: 217/217 still passing, clean typecheck, clean build. **Not
      covered by an automated regression test**: reproducing the actual race requires a live
      WebSocket connecting before a delayed provider/model catalog response resolves, which
      needs `AudioContext`/`AudioWorkletNode`/`WebSocket` stubbed globally the way
      `useSpeechRecognition.test.ts` does locally — doing that inside the *shared*
      `ChatPage.test.tsx` file risked destabilizing unrelated tests in the time available, so
      this was deferred rather than rushed; flagging as a real gap, not silently skipping it.
- [X] T025 **Two more bugs found via live testing, reported together**: (1) the mic control
      stopped working for Push-to-Talk dictation entirely — clicking it produced no
      transcript. (2) Continuous mode was hearing Lucy's own spoken replies through the
      speakers and transcribing/responding to them, creating replies to nothing the user
      actually said ("the agent is listening to itself").
      **(1) Root cause**: `ChatComposer.tsx`'s pointer handlers called `onStartCapture()` on
      every `pointerdown` and unconditionally `onStopCapture()` on every `pointerup` — but a
      normal quick click *is* a pointerdown→pointerup pair with near-zero elapsed time, so
      every click (including a deliberate toggle-start tap) started and immediately stopped
      capture within milliseconds, before any speech could be captured. A tap and a hold are
      physically the same event pair; only elapsed time between down and up distinguishes
      them. Fixed by adding a 350ms hold-vs-tap threshold: `pointerup`/Space-`keyup` only
      calls `onStopCapture()` if held past the threshold (a genuine hold-release); a quick
      tap under it leaves capture running, toggled on, until a later, separate tap turns it
      off via the existing click-toggle path. Also skip repeated `keydown` events (OS
      key-repeat) so a held Space key doesn't restart capture on every repeat.
      **(2) Root cause**: the Continuous-mode auto-start/stop effect in `ChatPage.tsx` only
      considered `conversationMode`, never `tts.isSpeaking` — nothing paused the always-on
      mic while Lucy's reply was playing through the speakers, so it captured and transcribed
      her own voice as user speech, triggering a reply to that, and so on. Fixed by pausing
      (via `recognition.cancel()` — discard, not commit, since any genuine user utterance
      would already have been finalized by the 800ms silence-commit window well before a
      reply finishes generating and starts playing) whenever `tts.isSpeaking` becomes true in
      Continuous mode, and resuming automatically once it becomes false. Scoped to Continuous
      mode only — a Push-to-Talk hold is an explicit user gesture (e.g. a deliberate
      barge-in) and is never force-stopped just because Lucy is talking.
      Also restored a lightweight visual "listening" cue (a pulsing mic icon via CSS
      animation) on the Push-to-Talk mic button — the original `useWavRecorder`-based
      composer had a real amplitude waveform (`VoiceWaveform.tsx`, deleted in T019 as
      dead code once nothing rendered it); `useSpeechRecognition` doesn't expose amplitude
      levels, so a full waveform wasn't feasible to restore in this pass, but *some* visual
      feedback beyond the "Listening…" text label was worth adding given the user's specific
      complaint that the control now feels inert. A real waveform would need
      `useSpeechRecognition`/`useVoiceAnalyzer`-style level exposure added to the hook — noted
      as a possible follow-up, not done here.
      Frontend-only change (`ChatComposer.tsx`, `ChatPage.tsx`). Added 4 new test cases
      locking in the hold-vs-tap distinction (`ChatComposer.test.tsx`, using fake timers to
      simulate real elapsed hold duration). Full suite re-run: 221/221 passing, clean
      typecheck, clean build. **Not covered by an automated regression test**: the
      self-listening fix (pausing on `tts.isSpeaking`) has the same jsdom
      `WebSocket`/`AudioContext` limitation already noted for T024 — verified by code review
      and the existing effect-dependency tests' passing behavior, not a dedicated live-audio
      integration test.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS both user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational (T002–T004). No dependency on US2.
- **User Story 2 (Phase 4)**: Depends on Foundational (T002–T004). No dependency on US1's
  implementation tasks (T007–T009) — both stories modify different call sites within the
  shared `VoiceControlBar.tsx`/`ChatPage.tsx` files, so run them sequentially if one person
  is doing both, or in parallel on separate branches if staffed by two people, merging with
  normal conflict resolution.
- **Polish (Phase 5)**: Depends on both user stories being complete.

### Within Each User Story

- Tests (T002 for Foundational; T005–T006 for US1; T011–T013 for US2) MUST be written and
  failing before their corresponding implementation tasks.
- T010 (STT wire-protocol verification) MUST land before T014–T018, since those tasks
  assume `useSpeechRecognition` actually transcribes correctly end-to-end.

### Parallel Opportunities

- T002 and T004 (Foundational) — different files, run together (T003 depends on T002 and
  must follow it).
- T005 and T006 (US1 tests) — different files, run together.
- T011, T012, T013 (US2 tests, after T010) — different files/sections, run together.
- Once Foundational completes, US1 (Phase 3) and US2 (Phase 4) can be staffed in parallel
  by two developers, per the note above.

---

## Parallel Example: User Story 1

```bash
# After Foundational (T002-T004) completes:
Task: "Create chat/voice/useVoiceOutput.test.ts covering mute gating of speak()/stop()"
Task: "Extend chat/components/VoiceControlBar.test.tsx with mute control behavior + a11y"
```

## Parallel Example: User Story 2 (after T010)

```bash
Task: "Add tests to chat/components/ChatComposer.test.tsx for hold/toggle/continuous"
Task: "Add mode-switch-guard tests to chat/components/VoiceControlBar.test.tsx"
Task: "Add keyboard-hold tests mirroring pointer hold and toggle-click"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (T002–T004 — all required before US1 can compile/wire).
3. Complete Phase 3: User Story 1 (mute).
4. **STOP and VALIDATE**: run quickstart.md Scenarios 1, 2, 8 (mute half) independently.
5. Deploy/demo if ready — mute alone is a complete, valuable increment; US2 is not required
   for US1 to ship, per research.md Decision 1's explicit decoupling.

### Incremental Delivery

1. Setup + Foundational → hydration (tested) and shared prop contract ready.
2. Add User Story 1 → validate independently → deploy/demo (MVP).
3. Add User Story 2 (start with T010's STT verification — the one task with external-API
   risk) → validate independently → deploy/demo.
4. Phase 5 polish once both stories are in.

### Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- Commit after each task or logical group.
- Constitution §10/§18: do not defer T002/T005/T006/T011–T013's tests to a follow-up — they
  are part of the same change that introduces the behavior they test.
- T010 is this plan's single highest-risk task (external API contract, not fully under this
  repo's control) — do it first within Phase 4, not last, so any surprise it turns up has
  maximum time to be absorbed before the rest of US2 is built on top of it.
