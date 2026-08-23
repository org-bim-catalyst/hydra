---

description: "Task list for SPEC-032: Transcription 500 Fix & Mode-Switch Simplification"
---

# Tasks: Transcription 500 Fix & Mode-Switch Simplification

**Input**: Design documents from `/specs/032-transcription-and-mode-switch-fixes/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — constitution §10 (Testing Standards) requires them, and this feature exists
specifically because a coverage gap (no test for a non-2xx OpenAI transcription response) let the
underlying bug ship unnoticed (research.md Decision 5's Finding).

**Organization**: Tasks are grouped by user story (US1 P1, US2 P2, US3 P3) per spec.md.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Maps the task to spec.md's US1/US2/US3

## Path Conventions

Web app (existing structure): `src/AskLucy.{Domain,Application,Infrastructure,Web}` (backend),
`src/AskLucy.Web/ClientApp/src` (frontend SPA), `tests/AskLucy.*.Tests` (backend tests).

---

## Phase 1: Setup

**Purpose**: Confirm the working environment is ready — no new dependencies or scaffolding are
needed for this feature (it extends existing files/patterns only, per plan.md).

- [X] T001 Confirm branch `032-transcription-and-mode-switch-fixes` is checked out and
  `.specify/feature.json` points at `specs/032-transcription-and-mode-switch-fixes` (already done
  during `/speckit-plan`)
- [X] T002 Confirm `dotnet build` and `npx tsc -b --noEmit` (ClientApp) both succeed on the current
  tree before making changes, to establish a clean baseline (note: `src/AskLucy.Infrastructure/Ai/
  OpenAIProvider.cs` already carries one unrelated pre-existing uncommitted line —
  research.md Decision 6 — confirm it doesn't break the build)

---

## Phase 2: Foundational

**Purpose**: No shared blocking infrastructure is required — each user story below is
independently implementable and touches disjoint files (US1: backend classification + two
frontend files; US2: one frontend file; US3: verification only). Proceed directly to Phase 3.

---

## Phase 3: User Story 1 - Voice recordings transcribe reliably (Priority: P1) 🎯 MVP

**Goal**: Classify previously-unclassified 4xx transcription rejections into a real, actionable
error instead of a generic 500; fix the recording filename to reduce how often that rejection
happens at all.

**Independent Test**: Record real speech via Push-to-Talk (both gestures) → transcribes
successfully. Force a provider rejection (mocked 400 in tests) → user sees a specific error, not
"Transcription failed with 500". Existing 401/403/429/5xx handling is provably unchanged.

### Tests for User Story 1 ⚠️ Write first, confirm they fail before implementing

- [X] T003 [P] [US1] New file `tests/AskLucy.Infrastructure.Tests/Ai/OpenAIProviderTests.cs`:
  test that a mocked 400 response from the transcription endpoint throws
  `AiProviderRequestInvalidException` with the response body in its message; test that mocked
  401/403 still throws `AiProviderAuthenticationException`, mocked 429 still throws
  `AiProviderRateLimitedException` (with `RetryAfter` if present), and mocked 500/connection
  failure still throws `AiProviderUnavailableException` after one retry — asserting Decision 1's
  new branch doesn't alter any existing classification (contracts/error-classification-and-mode-
  toggle-contract.md §1)
- [X] T004 [P] [US1] New file `tests/AskLucy.Web.Tests/Middleware/
  AiProviderRequestInvalidExceptionMappingTests.cs`: test that `ProblemDetailsMiddleware` maps
  `AiProviderRequestInvalidException` to HTTP 400 with `type` =
  `https://hydra.bimcatalyst.com/problems/ai-provider-request-invalid` and a non-empty `detail`,
  mirroring the existing test pattern used for the sibling `AiProvider*Exception` mappings in the
  (untouched) `ProblemDetailsMiddlewareTests.cs`
- [X] T005 [P] [US1] Update `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.test.ts`:
  add/adjust a case asserting `transcribeAudio` throws an `ApiError` whose `.message`/`.detail`
  comes from a mocked 400 Problem Details JSON body, not a bare status-code string
- [X] T006 [P] [US1] Update `src/AskLucy.Web/ClientApp/src/features/chat/voice/
  useVoiceRecorder.test.ts`: add cases asserting (a) the uploaded `File`'s name extension matches
  `blob.type` for webm/mp4/ogg/wav/mpeg **including when `blob.type` carries codec parameters**
  (e.g. `audio/webm;codecs=opus` → `.webm`, `audio/mp4;codecs=mp4a.40.2` → `.mp4`) and falls back
  to `.webm` only for a genuinely unrecognized type, and (b) a rejected `transcribeAudio` call
  (mocked `ApiError`) sets `error` to the `ApiError`'s message text (the real detail), not a
  generic string (addresses analysis finding U1: exact-match against `blob.type` would silently
  miss the codec-parameter case real browsers actually produce)

### Implementation for User Story 1

- [X] T007 [US1] Add `AiProviderRequestInvalidException(string message, Exception?
  innerException = null)` to `src/AskLucy.Application/Abstractions/IAIProvider.cs`, immediately
  after the three existing `AiProvider*Exception` types, matching their exact shape
- [X] T008 [US1] In `src/AskLucy.Infrastructure/Ai/OpenAIProvider.cs`'s `EnsureSuccessAsync`
  (~:334-358), after the existing 401/403 and 429 branches, add: any other 4xx status logs the
  response body server-side (new `OpenAIProviderLog.RequestRejectedByProvider` Warning-level log
  — the middleware only logs ≥500) then throws `AiProviderRequestInvalidException` carrying the
  body in its `Message` (internal diagnostic use only, never sent to the client as-is — see
  T009); threading `ILogger` into `EnsureSuccessAsync` requires updating its five call sites (do
  not touch the unrelated pre-existing `BuildChatPayload` line at `:253` — leave it exactly as it
  is, per research.md Decision 6)
- [X] T009 [US1] In `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs`'s `Map()` switch, add
  a case for `AiProviderRequestInvalidException` → 400 Bad Request, `type` =
  `https://hydra.bimcatalyst.com/problems/ai-provider-request-invalid`, `title` = "AI provider
  rejected the request", `detail` = a **fixed, safe string** ("The AI provider could not process
  this request. Please try again.") — matching the sibling `AiProvider*Exception` cases, which
  also never surface their own exception `Message` (the middleware's own doc comment forbids
  exposing a raw exception message to the client), positioned next to those two cases
- [X] T010 [US1] In `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts`'s
  `transcribeAudio`, replace the bare `throw new Error(...)` on `!response.ok` with parsing the
  JSON Problem Details body (tolerating a non-JSON/empty body) and throwing `new
  ApiError(response.status, problem?.detail ?? problem?.title ?? 'Transcription failed',
  problem?.detail)` — prefer `detail` over `title` as the message, since `useVoiceRecorder.ts`
  reads `err.message` directly and `detail` is the actionable sentence; importing `ApiError`
  alongside the existing `apiFetch` import from `../../../api/httpClient`
- [X] T011 [US1] In `src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceRecorder.ts`
  (~:127), replace the hardcoded `'recording.webm'` filename with one derived from `blob.type`:
  strip any `;codecs=...` parameter first (split on `;`, use the base MIME type — e.g.
  `audio/webm;codecs=opus` → `audio/webm`) before matching webm/mp4/ogg/wav/mpeg to their
  extension, else fallback to `.webm`, leaving the `type` field passed to `new File(...)`
  unchanged (addresses analysis finding U1)

**Checkpoint**: US1 is fully functional and independently testable — transcription failures are
now classified and surfaced with an actionable message, and the filename mismatch trigger is
fixed.

---

## Phase 4: User Story 2 - Mode switch takes exactly one click (Priority: P2)

**Goal**: Remove the two-click dropdown; the mode-switch icon toggles directly.

**Independent Test**: Click the mode-switch icon once from either mode → mode changes immediately,
no menu ever renders. Icon remains disabled mid-recording, exactly as before.

### Tests for User Story 2 ⚠️ Write first, confirm they fail before implementing

- [X] T012 [P] [US2] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/
  ChatComposer.test.tsx`: replace any test asserting the mode-switch click opens a `Menu`/
  `MenuItem` with a test asserting a single click on the mode-switch `IconButton` calls
  `onToggleMode` directly and that no `role="menu"` element ever appears in the DOM; keep/verify
  the existing disabled-while-recording assertion and the tooltip-label assertion

### Implementation for User Story 2

- [X] T013 [US2] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`,
  remove the `modeMenuAnchor` state, the `Menu`/`MenuItem` JSX block, and `handleToggleModeClick`;
  change the mode-switch `IconButton`'s `onClick` to call `onToggleMode` directly; remove the now-
  unused `Menu`/`MenuItem` imports (and the now-unused `useState` import); keep the existing
  `disabled={isModeSwitchBlocked}` guard unchanged; update the `Tooltip` text to describe the
  target mode directly ("Switch to Push-to-Talk" / "Switch to Continuous Conversation") since
  "click for options" no longer applies with the menu removed (FR-008)

**Checkpoint**: US1 and US2 both work independently; mode switching is a single click with no
regression to the disabled-while-recording guard or the tooltip label.

---

## Phase 5: User Story 3 - Push-to-Talk hold gesture keeps working (Priority: P3)

**Goal**: Verification-only — confirm the existing hold-to-talk gesture (implemented in
specs/031) is unaffected by US1/US2's changes.

**Independent Test**: Press-and-hold the mic, speak, release → transcribes immediately into the
message field with no extra tap; re-run existing specs/031 hold-gesture tests unchanged.

### Verification for User Story 3

- [X] T014 [P] [US3] Re-run the existing hold-gesture test cases in `src/AskLucy.Web/ClientApp/src/
  features/chat/voice/useVoiceRecorder.test.ts` and `src/AskLucy.Web/ClientApp/src/features/chat/
  pages/ChatPage.test.tsx` (from specs/031) — confirm all still pass unchanged after T007-T013; no
  new assertions required per spec.md User Story 3 (this is a regression check, not new behavior).
  Caught one real regression this task exists to catch: `ChatPage.test.tsx`'s
  `'preserves typed draft text across a conversation-mode switch (FR-009)'` test (not a hold-
  gesture test itself, but in the same file/describe block) still asserted the old two-click
  dropdown; updated to a single click, matching T012/T013 — `CollapsedVoiceControls.tsx` (a
  separate, unrelated component that also matched the "switch to..." text search) was confirmed
  to already call `onToggleMode` directly with no menu, so it needed no change.
- [ ] T015 [US3] Manual verification per quickstart.md Scenario 3: press-hold-release the mic in a
  running dev build, confirm transcript populates the message field immediately on release with no
  additional tap, then confirm Send still works normally

**Checkpoint**: All three user stories are independently functional; no regression to already-
correct behavior (hold gesture, provider-unavailable/rate-limited handling).

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T016 [P] Run `dotnet build` and the full backend test suite for the touched projects
  (`AskLucy.Application`, `AskLucy.Infrastructure`, `AskLucy.Web`, plus the two new test projects'
  test files) — confirm everything passes. Full solution build: 0 errors. Infrastructure.Tests:
  122/122 pass. Web.Tests: 296/297 pass — the one failure
  (`ReadinessHealthCheckTests.GetHealthReady_ShouldReturn200_WhenTheTestDatabaseIsFullyMigrated`)
  is the known pre-existing shared-CI-test-DB migration issue (unrelated to this feature; touches
  no persistence/migration code) documented in this session's memory, not a regression from T007-T013.
- [X] T017 [P] Run `npx tsc -b --noEmit` and the full ClientApp Vitest suite (per this session's
  established `tsc -b`, not `tsc --noEmit`, gotcha) — confirm everything passes. `tsc -b`: clean,
  0 errors. Vitest: 145 files / 651 tests, all pass.
- [ ] T018 Run quickstart.md Scenarios 1 and 2 manually against a local dev build (Scenario 3
  covered by T015)
- [X] T019 Re-verify `git status` shows only this feature's intended files as modified (plus the
  one pre-existing `OpenAIProvider.cs` line per research.md Decision 6, and no accidental staging
  of the unrelated ~190-file mechanical test pattern per Decision 5). Confirmed: 204 total dirty
  files; exactly 14 are this feature's intended set (10 modified + 3 new files + `.specify/
  feature.json`, counting `specs/032-.../` as one); the remaining 190 are the known pre-existing
  unrelated noise, untouched by this feature's work.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Empty — no blocking work; proceed directly from Setup.
- **User Story 1 (Phase 3)**: Depends on Setup only. Fully backend + two frontend files; no
  dependency on US2/US3.
- **User Story 2 (Phase 4)**: Depends on Setup only. Touches only `ChatComposer.tsx`/its test —
  disjoint from US1's files. Can run in parallel with US1 if staffed separately.
- **User Story 3 (Phase 5)**: Depends on Setup only for its own tasks, but is only meaningful to
  run *after* US1 and US2 land (it verifies no regression from them) — sequence last in practice
  even though it has no file-level dependency.
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### Within Each User Story

- Tests (T003-T006, T012) before their corresponding implementation (T007-T011, T013).
- T007 (exception type) before T008 (throws it) before T009 (middleware maps it) — same
  dependency chain as the code's own call order.
- T010/T011 (frontend) are independent of T007-T009 (backend) and of each other — can run in
  parallel.

### Parallel Opportunities

- T003, T004, T005, T006 (all different new/existing test files) can be written in parallel.
- T010 and T011 (different frontend files) can run in parallel with each other and with T007-T009
  (backend).
- US2's T012/T013 can proceed in parallel with all of US1, since they touch entirely disjoint
  files.
- T016 and T017 (backend vs frontend verification) can run in parallel.

---

## Parallel Example: User Story 1

```bash
# Tests, in parallel:
Task: "New OpenAIProviderTests.cs — 4xx classification"
Task: "New AiProviderRequestInvalidExceptionMappingTests.cs — middleware mapping"
Task: "Update aiApi.test.ts — ApiError detail surfacing"
Task: "Update useVoiceRecorder.test.ts — filename + error surfacing"

# Implementation, backend chain then frontend in parallel:
Task: "IAIProvider.cs — new exception type"      # then
Task: "OpenAIProvider.cs — throw it on other 4xx" # then
Task: "ProblemDetailsMiddleware.cs — map it to 400"
# in parallel with the above:
Task: "aiApi.ts — surface ApiError.detail"
Task: "useVoiceRecorder.ts — derive filename from blob.type"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 3: User Story 1 (T003-T011).
3. **STOP and VALIDATE**: quickstart.md Scenario 1, plus T003-T006's automated tests passing.
4. This alone resolves the production-blocking P1 bug; US2/US3 can follow immediately after.

### Incremental Delivery

1. Setup → User Story 1 → validate → (optional deploy checkpoint).
2. Add User Story 2 → validate → (optional deploy checkpoint).
3. Add User Story 3 (verification only) → validate no regressions.
4. Phase 6 Polish → full quickstart.md pass → ready for `/speckit-cicd`.

## Notes

- [P] tasks touch different files with no dependency on each other.
- Per research.md Decision 5/6: new backend test coverage goes into two brand-new files (never
  the pre-existing-dirty `ProblemDetailsMiddlewareTests.cs` or `tests/AskLucy.Web.Tests/Ai/*`);
  `OpenAIProvider.cs` itself is edited in place with the user's explicit approval to bundle its
  one pre-existing unrelated line.
- Commit after each user story phase, not after each individual task, consistent with this
  session's established `/speckit-cicd` pattern of one intentional, reviewable commit set per
  feature.
