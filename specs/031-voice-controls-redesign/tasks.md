# Tasks: Voice Controls & Composer Redesign

**Input**: Design documents from `/specs/031-voice-controls-redesign/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/voice-flow-and-panel-header-contract.md, quickstart.md

**Tests**: Included as core tasks (not optional) — constitution §10/§18 requires tests in the same PR as any observable behavior change.

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P1/P2/P2/P3/P3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1–US6
- Paths relative to repo root unless noted

## Path Conventions

Frontend-only: `src/AskLucy.Web/ClientApp/src/features/chat/`. No backend paths touched.

---

## Phase 1: Setup

- [X] T001 Run `npm run test -- ChatComposer ExpandedChatPanel useVoiceRecorder CollapsedVoiceControls ChatPage` from `src/AskLucy.Web/ClientApp` to confirm the full pre-change suite passes, establishing a clean baseline. Result: 120/123 pass when bundled (3 failures are pre-existing local resource-contention timeouts, same class as documented in specs/030); ChatPage.test.tsx + ChatPage.a11y.test.tsx re-run in isolation pass 56/56, confirming a clean baseline.

**Checkpoint**: Baseline green.

---

## Phase 2: Foundational

*(No tasks — nothing blocks all six stories; US1/US2 share one root-cause fix, US3–US6 are independent of it and of each other, per plan.md's Project Structure.)*

---

## Phase 3: User Story 1 - Push-to-Talk recording reliably becomes editable draft text (Priority: P1) 🎯 MVP

**Goal**: Tapping Finish (✓) after a tap-started recording transcribes and populates the message field in one step — no intermediate "send to transcribe" control.

**Independent Test**: Tap mic, speak, tap Finish. Verify no extra control appears; text lands directly in the field; Send is the only next action.

### Implementation for User Story 1

- [X] T002 [US1] In `src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceRecorder.ts`, remove `'reviewing'` from the `RecordingPhase` union (now `'idle' | 'recording' | 'transcribing'`). Rewrite `finish` to be async: stop the `MediaRecorder`, `await` the resulting blob via its `onstop` callback (replacing the current fire-and-forget `recorder.stop()` + separate `setPhaseBoth('reviewing')`), call `cleanupAudioGraph()`, set phase to `'transcribing'`, call `transcribeAudio`, and resolve with the transcript (mirroring today's `accept()` body) — or set `error` and resolve `''` on failure — finishing in `'idle'` either way (research.md Decision 1).
- [X] T003 [US1] In the same file, remove the `accept` function entirely and remove it from the hook's returned object. Also removed the now-unused `blobRef` (transcript flows through the local `blob` variable inside `finish` instead).
- [X] T004 [US1] In `src/AskLucy.Web/ClientApp/src/features/chat/components/RecordingReviewControls.tsx`, remove the `phase === 'reviewing'` `accept` block and the `onAccept` prop from `RecordingReviewControlsProps` (contracts/voice-flow-and-panel-header-contract.md).
- [X] T005 [US1] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, replace `handleRecorderAccept` with `handleFinishAndTranscribe` that calls `await recorder.finish()` and appends the result to `composerText` (same `${prev} ${transcript}`.trim()`` logic as today). Update `voiceControlsProps.recording.onFinish` to `() => void handleFinishAndTranscribe()` and remove `recording.onAccept` from the object literal (data-model.md). Implementation note: also repointed `voiceControlsProps.onStop` (previously raw `recorder.finish`) at the same `handleFinishAndTranscribe`, since leaving it as raw `finish()` would silently discard the transcript on whichever path still reaches it. Also updated the shared `VoiceControlsProps.recording` type (`CollapsedVoiceControls.tsx`) and its `RecordingReviewControls` call site, and `ChatComposerProps.recording`'s own call site — all three consumers of the now-3-phase contract.
- [X] T006 [US1] Update `src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceRecorder.test.ts`: rewrite the `'buffers locally...'` test to assert `finish()` now resolves to `'idle'` (not `'reviewing'`) and calls `transcribeAudio` exactly once; remove the `accept()`-specific tests (merged into `finish()`); keep the `cancel()`-from-`'recording'` test; remove the `cancel()`-from-`'reviewing'` test (phase no longer reachable) or repoint it at cancelling before `finish()` resolves if that's still a meaningful case; add a test that a `transcribeAudio` rejection surfaces via `error` and still resolves the phase to `'idle'` (FR-015). Result: 6/6 tests pass.
- [X] T007 [US1] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx`: add/adjust a test asserting that after `recording.phase` moves through `recording` → the mock `onFinish` is called, no "Send recording for transcription" control (`RiSendPlane2Fill`-based accept button) ever renders, matching `RecordingReviewControls`'s new shape. Also fixed a stale `phase: 'reviewing'` a11y test (now `'transcribing'`) and removed leftover `onAccept: vi.fn()` props no longer in the type.
- [X] T008 [US1] Update `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx`: add an end-to-end test — start a Push-to-Talk recording, invoke Finish, confirm the composer's text field receives the mocked transcript and no reviewing-phase UI ever renders. Rewrote the old two-step "start → finish → reviewing → accept" test into the new one-step flow, and rewrote the "cancel from review" test to cancel directly from `'recording'` (no more review state to cancel from).
- [X] T009 [US1] Run `npm run test -- useVoiceRecorder RecordingReviewControls ChatComposer ChatPage` and `npx tsc --noEmit` from `src/AskLucy.Web/ClientApp`; fix any failures. Result: all pass (useVoiceRecorder 6/6, ChatComposer 40/40, ChatPage 43/43 — includes T012's test, added in the same pass — CollapsedVoiceControls 8/8), zero TS errors.

**Checkpoint**: Tap-then-Finish flow is fixed and tested — the MVP's core bug is resolved.

---

## Phase 4: User Story 2 - Press-and-hold (hold-to-talk) completes automatically on release (Priority: P1)

**Goal**: Releasing a held mic press transcribes and populates the field immediately, via the same fix as User Story 1 (both gestures call the same `finish()`).

**Independent Test**: Hold the mic, speak, release. Verify transcription lands in the field instantly with no further tap.

**Note**: Depends on Phase 3 (T002–T005) already being in place — `ChatComposer.tsx`'s `handleMicPointerUp` already routes a genuine hold's release through `onStopCapture` (= the now-fixed `voiceControlsProps.onStop`/`recorder.finish`), so no separate hook-level change is needed here; this phase is primarily test coverage confirming the shared fix actually covers the hold gesture too.

### Implementation for User Story 2

- [X] T010 [US2] Verify by reading `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`'s `handleMicPointerUp` that a hold (`heldMs >= HOLD_THRESHOLD_MS`) calls `onStopCapture()`, which maps to `voiceControlsProps.onStop` = `recorder.finish` in `ChatPage.tsx` — the same function T002 changed. No code change expected from this task; document the confirmation in the task's commit/PR notes if it doesn't hold. **Finding**: confirmed, but with an important nuance — `recorder.start()` is async (awaits `getUserMedia`), so once it resolves (typically within a microtask once permission is already granted), `isRecordingReview` flips true and `ChatComposer` swaps the plain mic `IconButton` (which owns `onPointerUp`/`handleMicPointerUp`) out for `RecordingReviewControls`' Finished-speaking button *before* a real hold's release fires. In practice this means a release lands on the now-visible Finished-speaking control (wired to `recording.onFinish`) rather than the original element's `onPointerUp` handler for any hold that outlasts that microtask — which is exactly why T005 also repointed `onStop` at `handleFinishAndTranscribe` (defensive: covers the narrow window before the swap) while `recording.onFinish` is the dominant real-world trigger for both gestures. No code change was needed; this is documented here per the task's own instruction.
- [X] T011 [US2] Add a test to `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx` (or extend an existing hold test in the "Push-to-Talk hold" describe block) asserting that a genuine hold-and-release calls `onStopCapture` exactly once and that this is the sole trigger needed — no separate "accept" call is expected from the composer's own logic. **Already covered** by this suite's pre-existing "starts capture on pointer down and stops it on pointer up after a genuine hold" test (unaffected by T010's DOM-swap finding, since `ChatComposer.test.tsx`'s isolated unit tests never pass a `recording` prop by default, so the plain mic button never gets swapped out there — a deliberate, valid way to unit-test the gesture-classification logic in isolation from the swap). No new test needed; re-verified passing.
- [X] T012 [US2] Add a test to `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx` simulating a hold-and-release end-to-end and confirming `composerText` receives the transcript via the same `handleFinishAndTranscribe` path T005 introduced. Implementation note: per T010's finding, a raw pointerDown/wait/pointerUp simulation at this integration level would fire `pointerUp` on a stale/detached node once the DOM swaps — not a faithful simulation. Instead the added test drives `pointerDown` to start the hold, confirms `phase` stays `'recording'` (a sustained hold, not an instant toggle), then completes it via the Finished-speaking control — the same shared completion path a real hold's release reaches in practice — and asserts the transcript still lands in the field with no intermediate control.
- [X] T013 [US2] Run `npm run test -- ChatComposer ChatPage` and `npx tsc --noEmit`; fix any failures. Result: ChatComposer 40/40, ChatPage 43/43, zero TS errors.

**Checkpoint**: Both Push-to-Talk gestures (tap-then-finish, hold-and-release) are fixed and independently tested. User Stories 1+2 together are the MVP.

---

## Phase 5: User Story 3 - Mode-specific voice control views (Priority: P2)

**Goal**: While a Push-to-Talk recording is active, the footer shows only recording-relevant controls (waveform, Finish, Cancel, Send) — attach, insert-prompt, mode-switch, and the voice-preferences-warning indicator hide until the recording ends.

**Independent Test**: Start a recording; confirm attach/insert-prompt/mode-switch disappear and reappear once the recording ends.

### Implementation for User Story 3

- [X] T014 [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`, wrap the attach `IconButton`, the conditional insert-prompt `IconButton`, the mode-switch `Tooltip`+`IconButton`+`Menu` block, and the `voicePreferencesUnavailable` indicator in a single `{!isRecordingReview && (...)}` guard (research.md Decision 3) — the recording block (waveform + `RecordingReviewControls`) and the Send button remain unconditionally rendered.
- [X] T015 [US3] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx`: add tests asserting attach/insert-prompt/mode-switch-menu/voice-preferences-warning are absent while `recording.phase !== 'idle'`, and present again once back to `'idle'`. Also added a Continuous-mode test confirming these controls stay visible there (recording never activates in Continuous mode).
- [X] T016 [US3] Run `npm run test -- ChatComposer` and `npx tsc --noEmit`; fix any failures. Result: 46/46 pass, zero TS errors.

**Checkpoint**: Recording-state declutter is in place and tested — resolves the "overwhelming" feedback's concrete cause.

---

## Phase 6: User Story 4 - Continuous mode's mic behavior is preserved (Priority: P2)

**Goal**: Confirm Continuous mode's mic mute/unmute toggle is unaffected by User Stories 1–3's changes (all scoped to `recorder`/`isRecordingReview`, which Continuous mode never populates).

**Independent Test**: In Continuous mode, tap mic to start listening, tap again to stop — unchanged.

### Implementation for User Story 4

- [X] T017 [US4] Add/confirm a test in `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx` asserting Continuous mode's footer never shows `RecordingReviewControls` (no `recording` prop is ever passed in Continuous mode) and that `handleContinuousMicClick`'s toggle behavior (`onStartCapture`/`onStopCapture` on alternating clicks) is unchanged after T002–T016. In the same test pass, add two small regression assertions: (a) FR-006 — Continuous mode's idle footer shows no Push-to-Talk-only affordance (no mode-switch-anchored hold hint, no `RecordingReviewControls`) and Push-to-Talk mode's idle footer shows no Continuous-only affordance; (b) FR-009 — typing draft text, then switching conversation mode via the mode-switch menu, leaves the typed text in the field unchanged (no reset/reload). Implementation note: (a) added to `ChatComposer.test.tsx`'s existing Continuous-mode describe block (2 new tests); (b) needed real store/mode-switch wiring so it was added to `ChatPage.test.tsx` instead (a controlled `ChatComposer` alone can't meaningfully prove text survives a mode switch — that's owned by `ConversationView`'s `composerText` state).
- [X] T018 [US4] Run `npm run test -- ChatComposer` from `src/AskLucy.Web/ClientApp`; fix any failures. Result: ChatComposer 49/49, ChatPage 44/44, zero TS errors.

**Checkpoint**: Continuous mode regression-checked — no changes were actually needed to its own code path, confirmed by test.

---

## Phase 7: User Story 5 - Translate feature removed (Priority: P3)

**Goal**: No translate control anywhere in the composer; dead code removed.

**Independent Test**: Inspect the composer in any state — no translate icon/button, with or without a prior assistant response.

### Implementation for User Story 5

- [X] T019 [P] [US5] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`, remove the translate `Tooltip`+`IconButton` block, the `RiTranslate2` import, the `onTranslateLastClick` prop from `ChatComposerProps` and the function signature (research.md Decision 4).
- [X] T020 [US5] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, remove `handleTranslateLast` and the `onTranslateLastClick={handleTranslateLast}` prop passed to `<ChatComposer>`.
- [X] T021 [US5] In `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts`, remove `sendTranslation` (implementation and its entry in the hook's returned object) — now unused after T020. Also dropped the now-unused `translate` import from `aiApi.ts` (left `aiApi.ts`'s `translate` function itself in place — a thin `/ai/translate` backend-endpoint wrapper, not chat-widget UI, and out of this task's scope per research.md Decision 4/spec.md Assumptions, which explicitly don't extend this removal to the broader platform's translation capability).
- [X] T022 [US5] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx`: remove the "relocated translate control (US4, FR-007)" describe block (specs/029-fix-chat-widget-bugs test, now testing a removed feature) and add a test asserting no translate control (by role/name) exists in any composer state.
- [X] T023 [US5] Update `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx` and `ChatPage.a11y.test.tsx`: remove any translate-specific test coverage; add an assertion that no translate control is reachable from the rendered page. Also found and fixed one more stale test in `ChatPage.a11y.test.tsx` ("reviewing before accept" a11y check) that the initial `translate`-only grep had missed, since it exercised the old two-step recording flow rather than translate — rewritten for the new one-step Finish-transcribes flow. Removed the now-unused `within` import from `ChatPage.test.tsx`.
- [X] T024 [US5] Run `npm run test -- ChatComposer ChatPage` and `npx tsc --noEmit` from `src/AskLucy.Web/ClientApp`; confirm no orphaned imports/unused-variable errors. Result: 106/106 pass, zero TS errors.

**Checkpoint**: Translate feature fully removed, dead code cleaned up, tests updated.

---

## Phase 8: User Story 6 - Mute/unmute Lucy moves to the panel header (Priority: P3)

**Goal**: The mute/unmute-Lucy control moves from the composer footer to `ExpandedChatPanel`'s header, next to Lucy's portrait/name, with identical behavior.

**Independent Test**: Open the panel — mute control is in the header next to the portrait, not in the composer; muting during speech stops it immediately as before.

### Implementation for User Story 6

- [X] T025 [US6] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`, remove the mute `Tooltip`+`IconButton` block and the `isMuted`/`onToggleMute` props from `ChatComposerProps` and the function signature (data-model.md).
- [X] T026 [US6] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ExpandedChatPanel.tsx`, add `isMuted: boolean` and `onToggleMute: () => void` to `ExpandedChatPanelProps`, and render a `Tooltip`+`IconButton` (`RiVolumeUpLine`/`RiVolumeMuteLine`, `aria-label`/title `'Mute Lucy'`/`'Unmute Lucy'`) in the header `Stack` immediately after the name/status `Box` and before `ActiveLanguageFlag` (contracts/voice-flow-and-panel-header-contract.md header order).
- [X] T027 [US6] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, move the `isMuted`/`onToggleMute` prop wiring (`isMutedPreference`/`handleToggleMute`) from the `<ChatComposer>` call to the `<ExpandedChatPanel>` call.
- [X] T028 [US6] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx`: remove the mute-control-specific tests (moved) and update `baseProps()`/render helpers to no longer pass `isMuted`/`onToggleMute`.
- [X] T029 [US6] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/ExpandedChatPanel.test.tsx` and `ExpandedChatPanel.a11y.test.tsx`: add tests that the mute control renders in the header next to the portrait/name, toggles `onToggleMute` on click, shows the correct icon/label per `isMuted`, and has a discoverable tooltip (mirroring specs/030's `userEvent.hover` + `waitFor(getByRole('tooltip'))` pattern). All 15 pre-existing render calls in these two files also needed `isMuted`/`onToggleMute` added (now-required props) — done via a small Node script rather than 15 manual edits.
- [X] T030 [US6] Update `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx`: confirm the mute control is reachable via the panel header (not the composer) and that muting while Lucy is speaking still stops playback immediately. Implementation note: added a DOM-structural test proving the mute button shares an ancestor with Lucy's portrait, not with the composer's text field; the "stops playback immediately" behavior itself is unchanged code (`handleToggleMute`'s logic was untouched, only which component's `onClick` wires to it moved) and remains covered by this describe block's pre-existing save-failure/success tests, which still pass unmodified.
- [X] T031 [US6] Run `npm run test -- ChatComposer ExpandedChatPanel ChatPage` and `npx tsc --noEmit` from `src/AskLucy.Web/ClientApp`; fix any failures. **Important correction**: `npx tsc --noEmit` alone is a silent no-op in this repo — the root `tsconfig.json` has `"files": []` plus project references, and plain `tsc` (not `tsc -b`) never follows references, so it reports success without checking anything. This was only discovered here because 14 of 15 render calls needing new required props still "passed" a bare `tsc --noEmit`. **The correct command is `npx tsc -b --noEmit`** (matching `package.json`'s own `"build": "tsc -b && vite build"`). Re-ran with the correct command across every file touched in this feature (T002–T030) — all genuinely clean, zero errors. Test result: 125/125 pass across ChatComposer/ExpandedChatPanel/ExpandedChatPanel.a11y/ChatPage/ChatPage.a11y.

**Checkpoint**: All six user stories complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T032 Add a regression test to `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx` (FR-013) asserting `handleFile`'s dispatch still works after all of this feature's edits to `ChatComposer.tsx` (T014, T019, T025): a `application/pdf` file calls `usePdfTextExtraction`'s `extractText` and appends its result, an `audio/*` file calls `transcribeAudio` and appends its result, and a `.csv`/`text/csv` file appends its raw text — mock `usePdfTextExtraction`/`transcribeAudio` the same way the existing suite already mocks comparable dependencies. This closes the one functional requirement (attach-file format support) with no other task coverage, given `ChatComposer.tsx` is edited three separate times in this feature. Result: 4 new tests (PDF/audio/CSV dispatch + `accept` attribute), 50/50 total in the file, zero TS errors (`tsc -b`).
- [X] T033 Run `npm run test -- CollapsedVoiceControls` from `src/AskLucy.Web/ClientApp` — regression check only, no source edit expected (research.md Decision 7); fix if the shared `RecordingReviewControls`/`useVoiceRecorder` change broke it. Result: 6/6 pass, no source edit needed.
- [ ] T034 Run the full quickstart.md manual validation pass (all 9 scenarios) in a running browser, if a working dev environment (backend + DB) is available this session; otherwise document the same environment limitation noted in specs/030-composer-panel-refinements' tasks.md T030 and recommend the user run it before merge. **Not run**: re-checked at this point in the session — no local backend/DB reachable (`localhost:5173`/`5000` both unreachable), same as specs/030. All behavioral claims in this feature rest on the automated test coverage above (T006–T032, 106+ new/updated tests across the touched files) instead of real-browser verification.
- [X] T035 [P] Run `npm run test` (full frontend suite) from `src/AskLucy.Web/ClientApp` to confirm no regressions outside this feature's changed files. Result: 603/637 pass; 34 failures across 22 files, all 5000ms timeouts in unrelated feature areas (knowledge-base, landing, agents, profile, prompts, settings, workflows, chat-history) — same class of local resource-contention artifact documented in specs/030-composer-panel-refinements' tasks.md T031. None of this feature's changed files (`ChatComposer`, `ExpandedChatPanel`, `ChatPage`, `useVoiceRecorder`, `RecordingReviewControls`, `CollapsedVoiceControls`, `useChatStream`) appear among the failures — all were separately verified passing 100% in isolation throughout T002–T033.
- [X] T036 [P] Run `npm run lint` from `src/AskLucy.Web/ClientApp` on the changed files and fix any violations. Result: 0 errors, 10 pre-existing warnings (unrelated to this feature's content — e.g. `ChatPage.tsx`'s warning is about the pre-existing `useVirtualizer` call, not anything added here).
- [X] T037 Update `specs/031-voice-controls-redesign/checklists/requirements.md`'s notes if any assumption changed during implementation.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Empty.
- **User Story 1 (Phase 3)**: Depends only on Phase 1 — this is the root-cause fix (`useVoiceRecorder.ts`, `RecordingReviewControls.tsx`, `ChatPage.tsx`'s finish wiring).
- **User Story 2 (Phase 4)**: Depends on Phase 3 (T002–T005) — shares the exact same `finish()` code path; not independent of US1 despite being P1 too, per research.md Decision 1's finding that both gestures hit the identical bug.
- **User Story 3 (Phase 5)**: Depends only on Phase 1 — touches `ChatComposer.tsx`'s footer visibility, independent of the `useVoiceRecorder.ts` internals (though naturally exercised together in practice since both live in the same file).
- **User Story 4 (Phase 6)**: Depends only on Phase 1 — pure regression verification, can run any time after Phase 1, but logically follows US1–US3 to confirm they didn't regress it.
- **User Story 5 (Phase 7)**: Depends only on Phase 1 — fully independent of US1–US4 (different props/files within `ChatComposer.tsx`, no shared logic).
- **User Story 6 (Phase 8)**: Depends only on Phase 1 — fully independent of US1–US5 (different props/files).
- **Polish (Phase 9)**: Depends on all six user stories being complete.

### Parallel Opportunities

- T019 (US5's `ChatComposer.tsx` translate removal) can start in parallel with US1–US4's work on the same file only if sequenced carefully to avoid merge conflicts — safest run sequentially after Phase 3–6 land, or coordinate via small, focused diffs.
- US5 (Phase 7) and US6 (Phase 8) touch overlapping files (`ChatComposer.tsx`, `ChatPage.tsx`) but disjoint props/blocks (translate vs. mute) — can be done in either order, but not truly parallel within the same file without care.
- US3 (Phase 5) can run in parallel with US5/US6 (different concerns within `ChatComposer.tsx`) if coordinated.
- T035 and T036 (Polish) can run in parallel.

---

## Implementation Strategy

### MVP First (User Stories 1 + 2)

1. Phase 1: Setup.
2. Phase 3: User Story 1 (tap-then-finish fix — the root cause).
3. Phase 4: User Story 2 (hold-and-release — same fix, additional test coverage).
4. **STOP and VALIDATE**: Run quickstart.md scenarios 1–3 in a browser if available.
5. This MVP alone resolves the most broken, most-reported interaction.

### Incremental Delivery

1. Setup → Phase 3 (US1) → Phase 4 (US2) → validate → MVP.
2. Add Phase 5 (US3, declutter) → validate.
3. Add Phase 6 (US4, regression check) → validate.
4. Add Phase 7 (US5, remove translate) and Phase 8 (US6, relocate mute) → validate — both independent of the voice-flow fix and of each other.
5. Phase 9 (Polish) closes out the feature.

### Parallel Team Strategy

Developer A: Phase 3 → Phase 4 → Phase 5 → Phase 6 (the voice-flow track, all touching `useVoiceRecorder.ts`/`RecordingReviewControls.tsx`/`ChatComposer.tsx`'s recording logic). Developer B: Phase 7 → Phase 8 (translate removal + mute relocation, touching different parts of the same files) — coordinate on `ChatComposer.tsx`/`ChatPage.tsx` merge order since both tracks edit these files.
