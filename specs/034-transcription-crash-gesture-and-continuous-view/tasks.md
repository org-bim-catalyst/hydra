---

description: "Task list for SPEC-034: Transcription Crash Fix, Review-Gesture Restoration & Continuous Voice View"
---

# Tasks: Transcription Crash Fix, Review-Gesture Restoration & Continuous Voice View

**Input**: Design documents from `/specs/034-transcription-crash-gesture-and-continuous-view/`

**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Included — constitution §10 requires them, and this feature exists specifically because
a request-handling gap and a never-wired-up fix both went undetected without adequate coverage.

**Organization**: Tasks are grouped by user story (US1 P1, US2 P1, US3 P1) per spec.md.

## Format: `[ID] [P?] [Story] Description`

## Path Conventions

Web app (existing structure): `src/AskLucy.Web` (backend controller/config),
`src/AskLucy.Web/ClientApp/src` (frontend SPA), `tests/AskLucy.Web.Tests` (backend tests).

---

## Phase 1: Setup

- [X] T001 Confirm branch `034-transcription-crash-gesture-and-continuous-view` and
  `.specify/feature.json` point at this feature directory (already done during `/speckit-plan`)
- [X] T002 Confirm `dotnet build` and `npx tsc -b --noEmit` (ClientApp) both succeed on the
  current tree before making changes, to establish a clean baseline

---

## Phase 2: Foundational

**Purpose**: No shared blocking infrastructure — US1 (backend controller + logging config), US2
(`ChatComposer.tsx`), and US3 (new view + `ChatPage.tsx`) touch disjoint files. Proceed directly
to Phase 3.

---

## Phase 3: User Story 1 - No crash on malformed uploads, and failures are diagnosable (Priority: P1) 🎯 MVP

**Goal**: Close the actual third cause of the recurring transcription 500 (null `IFormFile`
binding) and fix production logging so any future failure is retrievable.

**Independent Test**: A request with a missing/empty file part returns 400, not 500. A normal
recording still works. A deliberately-triggered server exception is retrievable from a log file
after the fact.

### Tests for User Story 1 ⚠️ Write first, confirm they fail before implementing

- [X] T003 [P] [US1] New file `tests/AskLucy.Web.Tests/Ai/TranscriptionUploadGuardTests.cs`: test
  that `POST /api/v1/ai/transcriptions` with no `file` part (or an empty file) returns 400 with a
  specific title, not 500; same for `POST /api/v1/ai/transcriptions/microphone`; test that a
  well-formed upload still reaches the handler unchanged (regression, using the existing
  controller-test patterns from `tests/AskLucy.Web.Tests/Ai/*` for reference — do not edit those
  files, per the established pre-existing-dirty-files constraint from specs/032/033, only use
  them as a pattern reference)

### Implementation for User Story 1

- [X] T004 [US1] In `src/AskLucy.Web/Controllers/v1/AiController.cs`, add `if (file is null ||
  file.Length == 0) return BadRequest(new ProblemDetails { Title = "No audio file was provided",
  Status = 400 });` at the top of both `Transcribe` (`:205-213`) and `TranscribeMicrophone`
  (`:218-225`), before `file.OpenReadStream()` is called
- [X] T005 [P] [US1] Add the `Serilog.Sinks.File` NuGet package reference to
  `src/AskLucy.Web/AskLucy.Web.csproj`, and add `<StdoutLogEnabled>true</StdoutLogEnabled>` as a
  secondary measure (research.md Decision 2)
- [X] T006 [P] [US1] In `src/AskLucy.Web/appsettings.Production.json`'s `Serilog` section, add a
  `WriteTo` array entry for a rolling file sink (e.g. `App_Data/logs/asklucy-.log`, daily
  rolling) alongside the existing `Console` sink already configured in `Program.cs`
- [X] T007 [US1] Verify `Program.cs`'s `.ReadFrom.Configuration(context.Configuration)` correctly
  picks up the new `Serilog:WriteTo` array from `appsettings.Production.json` at startup (may
  require the `Serilog.Settings.Configuration` package if not already referenced — confirm before
  assuming it's needed)

**Checkpoint**: US1 is independently functional and testable — malformed uploads return a
specific 400, well-formed uploads are unaffected, and production exceptions are now retrievable.

---

## Phase 4: User Story 2 - Push-to-Talk supports both tap-with-review and hold-to-auto-finish (Priority: P1)

**Goal**: Restore the dual gesture on `ChatComposer.tsx`'s Push-to-Talk mic control, reusing
`RecordingReviewControls` for the tap path while preserving specs/033's hold-path pointer-capture
fix exactly.

**Independent Test**: A tap shows waveform + confirm/discard and waits; confirm transcribes,
discard cancels. A hold shows only the waveform throughout and auto-completes on release.

### Tests for User Story 2 ⚠️ Write first, confirm they fail before implementing

- [X] T008 [P] [US2] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/
  ChatComposer.test.tsx`: add tests asserting (a) a pointerdown followed by a quick pointerup
  (elapsed < threshold) shows `RecordingReviewControls` (Finish ✓ / Cancel ✗ by
  aria-label/role) and does NOT call `onStopCapture`, (b) tapping Finish calls `onStopCapture`
  and hides the review controls, (c) tapping Cancel calls `recording.onCancelRecording` and
  hides the review controls without calling `onStopCapture`, (d) a pointerdown followed by a
  pointerup after the threshold elapses calls `onStopCapture` directly with no review controls
  ever appearing (regression — the specs/033 hold path), (e) `setPointerCapture` is still called
  on pointerdown (regression — the specs/033 bug fix, reuse the prototype-stub pattern from
  specs/033's own tests)

### Implementation for User Story 2

- [X] T009 [US2] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`,
  reintroduce `HOLD_THRESHOLD_MS` and a press-start timestamp ref; add `isAwaitingTapReview`
  local state; rewrite `handleMicPointerUp`/`handleMicKeyUp` to check elapsed time on release:
  under threshold sets `isAwaitingTapReview(true)` and does NOT call `onStopCapture`; at or over
  threshold calls `onStopCapture()` directly as today (specs/033 behavior, unchanged); leave
  `handleMicPointerDown`/`handleMicKeyDown` and `setPointerCapture` untouched
  (research.md Decision 3)
- [X] T010 [US2] In the same file's render logic, re-import `RecordingReviewControls`; when
  `isAwaitingTapReview` is true, render it (Finish → call the same finish function as
  `onStopCapture`, then reset `isAwaitingTapReview`; Cancel → call `recording.onCancelRecording`,
  then reset `isAwaitingTapReview`) in place of the mic `IconButton`, alongside the waveform; when
  false, render the mic button as today (same element, `setPointerCapture`-protected)

**Checkpoint**: US2 is independently functional — both gestures work as specified, and
`CollapsedVoiceControls.tsx`/`useVoiceRecorder.ts`/`ChatPage.tsx`'s `voiceControlsProps` wiring
remain untouched (same scope boundary as specs/033).

---

## Phase 5: User Story 3 - Continuous mode opens a dedicated voice view (Priority: P1)

**Goal**: Build a focused voice view (Exit + Mute, Lucy's reactive presence) that opens on an
explicit switch into Continuous mode, built on `useConversationAudio` (finally wired up for real),
replacing `ChatPage.tsx`'s previously-separate, never-fixed inline Continuous-mode implementation.

**Independent Test**: Switching into Continuous mode opens the dedicated view with only Exit/Mute
visible; Mute silences Lucy without closing the view; Exit stops the session and returns to the
normal chat view; reloading a chat with Continuous as the saved preference does not auto-open it.

### Tests for User Story 3 ⚠️ Write first, confirm they fail before implementing

- [X] T011 [P] [US3] New file `src/AskLucy.Web/ClientApp/src/features/chat/components/
  ContinuousVoiceView.test.tsx`: test that it renders exactly two interactive controls (Exit,
  Mute) and the presence visualization, with no composer/attach/send elements present; test that
  tapping Mute calls the passed `onToggleMute` without calling `onExit`; test that tapping Exit
  calls `onExit`; test keyboard operability (both controls reachable/activatable via keyboard,
  visible focus states) per constitution §7
- [X] T012 [P] [US3] Update `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx`:
  add a test asserting clicking the mode-switch button into Continuous mode renders
  `ContinuousVoiceView` (or its Exit/Mute controls) and hides the normal composer; add a test
  asserting loading a chat with Continuous as the saved `conversationMode` preference does NOT
  render the voice view or call `getUserMedia` until an explicit action occurs (resolved
  clarification); add a test asserting Exit returns to the normal composer view; remove/update
  any existing test asserting the old inline Continuous-mode behavior (`recognition`-instance
  auto-start/mute effects) that this feature supersedes — check `ChatPage.a11y.test.tsx` for the
  same and update in parallel
- [X] T013 [P] [US3] Update `src/AskLucy.Web/ClientApp/src/features/chat/voice/
  useConversationAudio.test.ts` only if needed — this hook's own internals are unchanged by this
  feature (research.md Decision 4's "Scope carried forward" note); this task is a verification
  pass, not expected to require edits unless T014/T015 reveal an integration gap

### Implementation for User Story 3

- [X] T014 [US3] New file `src/AskLucy.Web/ClientApp/src/features/chat/components/
  ContinuousVoiceView.tsx`: a presentational component taking `voiceState`, `errorMessage`,
  `getReactiveIntensity`, `isMuted`, `onToggleMute`, `onExit` as props; renders a full-presentation
  `SceneBackground` (reusing `AiPresenceCard`'s existing lazy-import pattern) driven by
  `getReactiveIntensity`, plus exactly two `IconButton`s (Exit, Mute/Unmute) with clear
  `aria-label`s and `Tooltip`s, matching this codebase's existing icon-button conventions
  (research.md Decision 5)
- [X] T015 [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`: replace the
  `recognition` instance, `handleFinalTranscriptRef`/`handleFinalTranscript`, and the two effects
  at `:396-443` that exist solely to drive Continuous mode with a single `useConversationAudio`
  instance (wired to `chatId`/`language`/`providerId`/`modelId`/`generationParameters` and the
  existing message-sending/streaming callbacks this page already owns); add a transient
  `isVoiceViewActive` boolean state; `handleToggleMode`, when switching Push-to-Talk → Continuous,
  sets it to `true` in addition to updating the persisted preference as today; when
  `isVoiceViewActive` is true, render `<ContinuousVoiceView>` in place of the normal composer/
  message-list area (research.md Decision 6); its `onExit` calls the `useConversationAudio`
  instance's `stop()`/`cancelListening()` and sets `isVoiceViewActive` to `false`; loading a chat
  with Continuous as the saved preference leaves `isVoiceViewActive` at its default `false`

**Checkpoint**: All three user stories are independently functional. `ChatPage.tsx` no longer has
two parallel Continuous-mode implementations — the old inline one is fully removed, not left
disabled.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T016 [P] Run `dotnet build` and the full backend test suite (`AskLucy.Web.Tests` at
  minimum) — confirm everything passes, including T003's new tests. `dotnet build` succeeded
  (0 errors, pre-existing NU1903 Microsoft.OpenApi advisory only — see
  [[openapi_version_ceiling]], unrelated to this feature)
- [X] T017 [P] Run `npx tsc -b --noEmit` and the full ClientApp Vitest suite — confirm everything
  passes, including `ChatComposer.test.tsx`, the new `ContinuousVoiceView.test.tsx`,
  `ChatPage.test.tsx`/`ChatPage.a11y.test.tsx`, and `CollapsedVoiceControls.test.tsx`/
  `CollapsedChatControl.test.tsx` (regression-proving the untouched Collapsed-widget flow).
  `tsc -b --noEmit` is clean across the whole ClientApp. Full-suite Vitest run reported 19/146
  files (29/665 tests) failing, all on `Error: Test timed out in 5000ms` — spread across
  unrelated, untouched areas (knowledge-base, landing, prompts, profile, settings, workflows) as
  well as the new `ContinuousVoiceView.test.tsx`; re-running every one of those files in isolation
  (including `ContinuousVoiceView.test.tsx`, which passed 9/9) passed cleanly, confirming this is
  local resource-contention flakiness from running 665 tests (many with axe a11y scans) against a
  5s default timeout in one process, not a regression from this feature's changes — same category
  of pre-existing shared-infra flakiness as [[ci_shared_sql_fulltext_broken_2026-08]], just at the
  local-machine level instead of CI's shared DB level
- [X] T018 Run quickstart.md Scenarios 1–3 manually against a local dev build where possible
  (Scenario 1's log-file check and Scenario 3's live mic/speaker checks need a real environment).
  No browser/mic/IIS environment is available in this session, so each scenario's automated
  equivalent was used as the closest verification: Scenario 1 steps 1-2 ⇔
  `TranscriptionUploadGuardTests.cs` (400 on missing/empty file, well-formed upload unaffected);
  step 3 (log file actually receiving writes) genuinely needs a deployed IIS/production
  environment — deferred to T020's production verification, per tasks.md's own note that this is
  elevated scope this round. Scenario 2 ⇔ `ChatComposer.test.tsx`'s tap/hold describe blocks (tap
  shows Finish/Cancel and waits; Cancel discards without transcribing; hold shows only the
  waveform and auto-transcribes on release). Scenario 3 ⇔ `ChatPage.test.tsx` (Exit/Mute-only
  view on mode switch, Exit returns to composer with draft preserved, no auto-open on load) +
  `ContinuousVoiceView.test.tsx` (exactly two controls, keyboard operability) +
  `ChatPage.a11y.test.tsx`; genuine live mic/speaker audio quality (self-listening mute
  effectiveness) still needs a real device and is not fully provable by jsdom mocks.
- [X] T019 Re-verify `git status` shows only this feature's intended files as modified; confirm
  `RecordingReviewControls.tsx`, `CollapsedVoiceControls.tsx`, `useVoiceRecorder.ts`,
  `useSpeechRecognition.ts`, and `useConversationAudio.ts` are NOT among the changed files (their
  own internals are unchanged by this feature per research.md — only what calls them changes).
  Confirmed: of that list, only `useConversationAudio.ts` shows modified — its deliberate
  `startTurn()` try/catch fix (see research.md); the other four are untouched. The repo carries a
  large, long-standing pile of unrelated modified files across Application/Infrastructure/
  Persistence/Web/Tests predating this session (same pre-existing-dirty-files state as specs/032/
  033 — not to be staged or cleaned here). Two findings surfaced during this check:
  (1) `src/AskLucy.Web/appsettings.Production.json` is gitignored (`.gitignore:392`, holds live
  secrets per [[prod_oauth_500_appsettings_deploy]]) — T006's Serilog file-sink addition is
  present in the local working copy but **cannot reach production through git/CI at all**; it
  requires the same manual out-of-band deploy step that broke deployments once before, so T020
  must call this out explicitly rather than assume the commit carries it. (2) `specs/
  033-hold-to-talk-and-echo-fix/tasks.md` has an uncommitted edit unrelated to this feature — its
  own T015 being marked `[X]` with the SPEC-032/033 deploy-verification note, apparently written
  after that round's squash-merge (`18ecb8b`) rather than before it. Harmless and accurate
  (documents already-shipped, already-verified work), but folding it into this feature's `/
  speckit-cicd` commit as a small separate doc-only commit is cleaner than leaving it stranded
  indefinitely.
- [X] T020 Run this feature's full `/speckit-cicd` pass to completion — commit, push, PR, CI,
  merge, and verify the deployed production build reflects this commit, including confirming the
  new log file sink is actually writable/written-to in the production environment (not just
  configured) — a repeat of specs/032's process gap must not recur. Branch
  `034-transcription-crash-gesture-and-continuous-view` → commit `ae8523d` (20 files, exactly the
  intended set) + carryover doc commit `1530e28` (specs/033 T015 note) → PR #312 → CI green
  (frontend 2m28s, backend 13m41s) → squash-merged as `5add8cb` → post-merge `main` CI/deploy run
  green (backend 14m0s, Deploy to site4now.net 3m17s) → verified `https://hydra.bimcatalyst.com
  /health` and `/health/ready` both return 200 → local + remote feature branch deleted → local
  `main` fast-forwarded to `5add8cb`, 0 ahead/behind `origin/main`, `git fsck` clean.
  **Exception, called out explicitly per T019's finding**: the log-file-sink half of this
  verification could NOT be completed end-to-end. `appsettings.Production.json` is gitignored
  (holds live secrets) and was therefore never part of the commit/PR/CI/deploy pipeline at all —
  the `Serilog:WriteTo` file-sink entry only exists in this session's local working copy, not on
  the production server. Confirming it is "actually writable/written-to in the production
  environment" requires someone with server access to (1) add the same `WriteTo` entry to the
  real `appsettings.Production.json` on the server, (2) restart the app pool, and (3) verify
  `App_Data/logs/asklucy-*.log` receives writes — flagged in the PR description as a required
  follow-up, not silently treated as done.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Empty — proceed directly from Setup.
- **User Story 1 (Phase 3)**: Depends on Setup only. Backend-only; fully independent of US2/US3.
- **User Story 2 (Phase 4)**: Depends on Setup only. Touches only `ChatComposer.tsx`/its test —
  disjoint from US1 (backend) and US3 (`ChatPage.tsx`, new component). Can run in parallel with
  both.
- **User Story 3 (Phase 5)**: Depends on Setup only. T014 (new component) before T015 (wiring it
  into `ChatPage.tsx`) — same-feature dependency, not cross-story.
- **Polish (Phase 6)**: Depends on all three user stories being complete. T020 (the `/speckit-cicd`
  pass) is the final task.

### Within Each User Story

- Tests (T003, T008, T011-T013) before their corresponding implementation (T004-T007, T009-T010,
  T014-T015).
- T009 before T010 (gesture-handling logic before the render-logic change that depends on it) —
  same file, sequential.
- T014 before T015 (the new component must exist before `ChatPage.tsx` renders it).

### Parallel Opportunities

- T003 (US1 test), T008 (US2 test), T011-T013 (US3 tests) can be written in parallel.
- T005 and T006 (different files, same US1 logging fix) can run in parallel.
- US2's T009-T010 and US3's T014-T015 can proceed fully in parallel — no shared files.
- T016 and T017 (backend vs frontend full-suite verification) can run in parallel.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 3: User Story 1 (T003-T007).
3. **STOP and VALIDATE**: the malformed-upload guard test passes; a deliberately-triggered
   exception is retrievable from the new log file.
4. This alone closes the actual root cause behind the three-times-recurring production 500; US2/
   US3 can follow immediately after.

### Incremental Delivery

1. Setup → User Story 1 → validate.
2. Add User Story 2 → validate (including Collapsed-widget regression check).
3. Add User Story 3 → validate (view entry/exit, no auto-open on load, old plumbing fully removed).
4. Phase 6 Polish → full quickstart.md pass → `/speckit-cicd` to completion (T020), including
   confirming the log sink is actually writing in production this time.

## Notes

- Per research.md Decision 3/4: `RecordingReviewControls.tsx`, `CollapsedVoiceControls.tsx`,
  `useVoiceRecorder.ts`, `useSpeechRecognition.ts`, and `useConversationAudio.ts`'s own internals
  are deliberately **not** touched by any task in this list — only what calls them changes. Verify
  this stays true (T019) rather than assuming it.
- T020's deployment-verification scope is elevated the same way specs/033's was — this round adds
  an extra check (the log sink actually receiving writes in production), since the whole reason
  this feature exists is that two prior "verified" fixes turned out to be incomplete/misdirected.
