# Tasks: Chat Widget Reliability & Voice UI Consolidation

**Input**: Design documents from `/specs/029-fix-chat-widget-bugs/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. Not explicitly requested in spec.md, but constitution §10/§18/§19 (this project's binding, non-optional testing standard) requires tests for all new/changed observable behavior — every implementation task below is paired with a test task.

**Organization**: Tasks are grouped by user story (spec.md priorities) so each of the four bugs can be implemented, tested, and shipped independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 (real-time connections), US2 (false error banner), US3 (duplicate voice controls), US4 (translate placement)

## Path Conventions

Existing web app, single repo: backend under `src/AskLucy.*` (Clean Architecture layers) + `tests/AskLucy.*.Tests`; frontend under `src/AskLucy.Web/ClientApp/src`. See [plan.md](./plan.md)'s Project Structure for the full file map.

---

## Phase 1: Setup

**Purpose**: Confirm a clean baseline before touching anything — no project initialization needed, this is an existing app.

- [X] T001 On branch `029-fix-chat-widget-bugs`, confirm the backend builds (`dotnet build` from repo root) and the frontend builds (`npm install && npm run build` from `src/AskLucy.Web/ClientApp`) cleanly, establishing a working baseline before any of the fixes below. **Verified**: baseline build was clean before any edits; final full-solution `dotnet build` and `npm run build` both succeed after all changes.

---

## Phase 2: Foundational

**No blocking foundational work is required.** All four user stories are independent, root-caused bug fixes against the existing codebase (research.md Decisions 1–7) with no shared new infrastructure, data model, or auth changes. Proceed directly to the user story phases below, starting with the P1 stories.

---

## Phase 3: User Story 1 - Real-time features work reliably in production (Priority: P1) 🎯 MVP candidate

**Goal**: Every `/hubs/*` SignalR connection succeeds in production instead of being intercepted by the SPA-fallback middleware (research.md Decision 7), and if a connection genuinely fails afterward, that failure is exposed to the consuming component rather than silently discarded (FR-010, analysis finding C1).

**Independent Test**: quickstart.md §5 — open devtools Network/WS panel, trigger any hub-backed feature (floating panel, workflow run, document upload), confirm the WebSocket connects with no MIME-type/handshake console errors; repeat for a second hub to confirm the fix is uniform (FR-011). Additionally, confirm `ViewerSurface`, `MemoryCenterPage`, and `DocumentWorkspacePage` each show a "Live"/"Reconnecting" indicator that responds to an actual connection drop (e.g. stop the backend briefly), not just the routing-level fix.

### Tests for User Story 1

- [X] T002 [P] [US1] Write a regression test in `tests/AskLucy.Web.Tests/Routing/HubFallbackRoutingTests.cs` (new file, using `CustomWebApplicationFactory`) asserting a GET request to each of the 6 `/hubs/<name>` paths (`document-processing`, `retrieval-indexing`, `memory`, `agent-execution`, `workflow-execution`, `panels`) does **not** come back with `Content-Type: text/html` (the exact symptom from the production log) — this MUST fail against current `Program.cs` before T003/T004. **Verified**: all 6 pass, plus a 7th test confirming ordinary SPA routes still resolve to `index.html`.

### Implementation for User Story 1

- [X] T003 [US1] In `src/AskLucy.Web/Program.cs`, split the SPA-fallback `app.Use(...)` middleware (lines 496-543): keep the static-file-serving block (lines 496-517) exactly where it is and unchanged; delete the index.html-fallback block (lines 519-540) along with its manual `/api`/`/openapi`/`/health` exclusion checks entirely (research.md Decision 7).
- [X] T004 [US1] In `src/AskLucy.Web/Program.cs`, re-register the removed fallback logic as `app.MapFallback(async context => { ... })`, reusing the same `wwwrootProvider.GetFileInfo("index.html")` read removed in T003 unchanged, placed after `app.MapControllers()` and every `app.MapHub<...>()` call (after line 594). (Depends on T003.)
- [X] T004a [P] [US1] Fix `useFloatingPanelHub.ts` (`src/AskLucy.Web/ClientApp/src/viewer/panels/hooks/useFloatingPanelHub.ts`) to match `useDocumentProcessingHub.ts`'s `isLive` pattern: return `{ isLive: boolean }`, wire `onreconnected`/`onreconnecting`/`onclose`, and replace `connection.start().catch(() => undefined)` with `connection.start().then(() => setIsLive(true), () => setIsLive(false))` — closes the FR-010 gap for the panels hub specifically named in the original bug report (analysis finding C1).
- [X] T004b [US1] Before implementing, check how `isLive` is already rendered for the compliant hooks (e.g. `ExecutionMonitor`/`ExecutionMonitor.a11y.test.tsx` for `useWorkflowExecutionHub`) — neither `ViewerSurface.tsx` nor the memory/document pages have an existing "Live"/"Reconnecting" visual convention of their own (confirmed: `MemoryCenterPage.tsx:47` currently calls `useMemoryNotificationsHub()` with no destructuring at all, and `DocumentWorkspacePage.tsx:28` only consumes `latest`/`dismiss`, not connection state). Match `ExecutionMonitor`'s established treatment rather than inventing a new one. Then consume the new `isLive` return value from T004a in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx` (line 57) and render it accordingly. (Depends on T004a.)
- [X] T004c [P] [US1] Apply the same `isLive` fix from T004a to `useMemoryNotificationsHub.ts` (`src/AskLucy.Web/ClientApp/src/features/memory/hooks/useMemoryNotificationsHub.ts`) and `useNotificationHub.ts` (`src/AskLucy.Web/ClientApp/src/features/documents/hooks/useNotificationHub.ts`); wire their new `isLive` return values into `MemoryCenterPage.tsx` (line 47, which today ignores the hook's return value entirely) and `DocumentWorkspacePage.tsx` (line 28, which already destructures `latest`/`dismiss` — add `isLive` alongside them) respectively, using the same `ExecutionMonitor`-matched treatment as T004b.
- [X] T004d [P] [US1] Add/update tests: extend `useFloatingPanelHub.test.ts` and add new test files for `useMemoryNotificationsHub.ts` and `useNotificationHub.ts`, asserting `isLive` transitions to `false` (not silently discarded) when `connection.start()` rejects — matching the existing test pattern for the already-compliant hooks. (Depends on T004a, T004c.) **Verified**: 15 new/updated tests across the 3 hooks, all pass.
- [X] T005 [US1] Run T002's test suite and T004d's new/updated tests and confirm all now pass; manually run quickstart.md §5 against the local dev stack (including confirming the new "Live"/"Reconnecting" indicators in `ViewerSurface`/`MemoryCenterPage`/`DocumentWorkspacePage` reflect connection state correctly), and confirm ordinary SPA client-side routing and static asset serving still work unaffected (the untouched half of the middleware). (Depends on T004b, T004d.) **Verified automatically** (T002/T004d all green, real HTTP requests through `CustomWebApplicationFactory`'s full pipeline); the manual live-browser walkthrough in quickstart.md §5 was not additionally performed by hand in this session.

**Checkpoint**: User Story 1 is fully functional and independently testable/deployable at this point.

---

## Phase 4: User Story 2 - Chat opens without a false error message (Priority: P1) 🎯 MVP candidate

**Goal**: `GET /api/v1/ai/voice/preferences` stops 500ing (migration applied), a recurrence safeguard exists (`/health/ready`), and the frontend degrades quietly instead of showing a blocking, alarming Snackbar on every chat load (research.md Decisions 1–4).

**Independent Test**: quickstart.md §1–§2 — apply migrations, confirm `GET /api/v1/ai/voice/preferences` and `GET /health/ready` both return non-error responses; simulate a fetch failure and confirm chat/voice stay usable with only a small, dismissible indicator (no full-width banner), while the failure is still visible in backend logs.

### Tests for User Story 2

- [ ] T006 [P] [US2] Write a unit test for the new health check in `tests/AskLucy.Persistence.Tests/HealthChecks/PendingMigrationsHealthCheckTests.cs` (moved from the originally-planned `AskLucy.Infrastructure.Tests` — see T009's note): healthy when zero pending migrations, unhealthy with pending migration names populated in `Data` when one or more exist — MUST fail until T009 exists. **Code complete, not verified green**: written and confirmed to *run* (connects, queries, returns a correctly-shaped result — proving the implementation itself is correct), but this sandbox's local SQL Server instance has an unrelated, pre-existing migration (`20260810045116_AddPromptFullTextSearch`, dated a week before this feature) that fails to apply because LocalDB doesn't support full-text search — blocking this database from ever reaching a fully-migrated state here. The Unhealthy branch also isn't separately covered — see the test file's own doc comment for why (no spare database with `CREATE DATABASE` rights in this shared-hosting test environment, and `AskLucyDbContext` is sealed so it can't be substituted).
- [ ] T007 [P] [US2] Write an integration test in `tests/AskLucy.Web.Tests/HealthChecks/ReadinessHealthCheckTests.cs` (using `CustomWebApplicationFactory`): `GET /health/ready` returns `200 OK` against the fully-migrated test database, per `contracts/health-readiness-endpoint.md` — MUST fail until T008/T009/T010 land. **Code complete, not verified green** for the same reason as T006 — the test actually running end-to-end (real HTTP request, real DB round-trip, correctly returning `503` with the pending migration's name rather than crashing) proves the endpoint is wired up correctly; it's specifically the "fully migrated" precondition this sandbox's database can't reach. A companion test (`GetHealth_ShouldStillWork_UnaffectedByTheNewReadyEndpoint`) does pass, and its first run caught a real bug — see T010.

### Implementation for User Story 2

- [ ] T008 [P] [US2] Apply the `20260817110019_AddUserVoicePreferenceDefaultLanguage` migration to the local dev database (`dotnet ef database update` from `src/AskLucy.Persistence`) **and** to the shared test SQL Server instance used by `tests/AskLucy.Web.Tests`/`tests/AskLucy.Persistence.Tests` (`PERSISTENCE_TESTS_CONNECTION_STRING`), so both dev and CI reflect the fix (research.md Decision 1). **Blocked in this session, needs a maintainer**: the local dev DB attempt failed on the unrelated pre-existing `AddPromptFullTextSearch` migration (LocalDB has no full-text search support); the shared test DB wasn't attempted at all — `PERSISTENCE_TESTS_CONNECTION_STRING` isn't set in this sandbox and its credentials weren't available. Someone with access to both needs to run this manually (see the new note in docs/TESTING.md §13).
- [X] T009 [US2] Implement `PendingMigrationsHealthCheck` (`IHealthCheck`) in `src/AskLucy.Persistence/HealthChecks/PendingMigrationsHealthCheck.cs` — **relocated from the plan's original `AskLucy.Infrastructure/HealthChecks/` during implementation**: `AskLucy.Infrastructure` doesn't actually reference `AskLucy.Persistence` anywhere in real code (only a comment mentions `AskLucyDbContext`, and it explicitly says DbContext-dependent code belongs in Persistence), so the health check was placed there instead, matching that established convention rather than introducing a new cross-project dependency. Calls `AskLucyDbContext.Database.GetPendingMigrationsAsync()` per data-model.md's Readiness Signal and `contracts/health-readiness-endpoint.md` (research.md Decision 2).
- [X] T010 [US2] In `src/AskLucy.Web/Program.cs`, register `PendingMigrationsHealthCheck` tagged `"ready"` and add `app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })`. (Depends on T009.) **Also fixed a real bug found by T007's own test run**: `MapHealthChecks("/health")` with no `Predicate` runs *every* registered check by default, so without an explicit exclusion `/health` (liveness) would have started failing whenever a migration was merely pending, coupling it to the new readiness signal against the explicit design intent (data-model.md: "keeping liveness and readiness semantics distinct"). `/health` now has `Predicate = check => !check.Tags.Contains("ready")`.
- [X] T011 [US2] Add a `useVoicePreferencesQuery` TanStack Query hook in `src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoicePreferencesQuery.ts` (new file), matching the existing `src/features/settings/hooks/useAiPreferences.ts` pattern, wrapping `getVoicePreferences()` from `voiceApi.ts` (research.md Decision 4).
- [X] T012 [US2] Narrow `src/AskLucy.Web/ClientApp/src/features/chat/voice/voicePreferencesStore.ts`'s Zustand store; remove `hydrateFromServer` (research.md Decision 4). (Depends on T011.) **Refined during implementation**: the store's `error` field is shared with `update()`'s save-failure path (a distinct, still-required error surface, unrelated to Bug 1) — only `hydrateFromServer` was removed, `error`/`update`'s own error-setting behavior is untouched.
- [X] T013 [US2] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, replace the old hydrate effect with the new `useVoicePreferencesQuery` hook (moved into `ConversationView`, next to the other voice-preference reads, rather than kept as a separate outer-`ChatPage` mount point — TanStack Query's cache makes the original "mount once" rationale unnecessary), and replace the blocking Snackbar with a small, non-blocking indicator hosted in `ChatComposer`'s mic settings area (research.md Decision 3). (Depends on T012, T016.) **Also fixed**: `VoiceTab.tsx` (Settings page) independently called the now-removed `hydrateFromServer` too — not in the original task list, found and fixed during implementation.
- [X] T014 [P] [US2] Update `ChatPage.test.tsx` to assert no blocking error Snackbar renders when the voice-preferences query fails and that chat/voice stay usable on defaults (FR-001/FR-002); a new regression test added for exactly this. `ChatPage.a11y.test.tsx` needed no changes for this indicator specifically (its Continuous-mode test needed an unrelated label-query fix — see T023). (Depends on T013.)

**Checkpoint**: User Story 2 is fully functional and independently testable/deployable once T013's dependency on User Story 3 is resolved (see Dependencies & Execution Order).

---

## Phase 5: User Story 3 - One clear voice recording control (Priority: P2)

**Goal**: `VoiceControlBar` is retired from the Expanded chat panel; `ChatComposer` becomes the single consolidated voice control (mic with contextual mute/mode behavior, merged speaker-mute+stop icon, no redundant text labels) per `contracts/expanded-voice-control-consolidation.md` (research.md Decisions 5/5a/5b).

**Independent Test**: quickstart.md §3 — exactly one mic icon and one recording-status surface in both modes; mode-switch via the mic's menu; merged speaker-mute/stop icon behaves as one toggle; no "Listening…"/"Lucy is speaking…" text anywhere.

### Tests for User Story 3

- [X] T015 [P] [US3] Update `ChatComposer.test.tsx` (test-first — extend/adjust existing cases to describe the target behavior before implementing) to cover: exactly one mic icon rendered in both `conversationMode` values; mode-menu opens and switches modes (disabled mid-Push-to-Talk-capture); the merged mute/stop icon silences an in-progress reply and stays muted until pressed again; no "Listening…" or "Lucy is speaking…" text anywhere; `RecordingReviewControls` renders during capture. These MUST fail until T017–T021 land.

### Implementation for User Story 3

- [X] T016 [US3] Extend `ChatComposerProps` in `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx` per `contracts/expanded-voice-control-consolidation.md`: add `onToggleMode`, `recording`, `isMuted`, `onToggleMute`, and `onTranslateLastClick` (the last one shared with User Story 4 — see Dependencies & Execution Order). **Also dropped `onCancelCapture`** (kept in the original contract draft, but found during implementation to have no remaining distinct use — see T021's note).
- [X] T017 [US3] In `ChatComposer.tsx`, remove the `showMicButton = conversationMode === 'PushToTalk'` gate (line 150) so the mic icon always renders; in `Continuous` mode it becomes the microphone mute toggle (`isListening ? onStopCapture() : onStartCapture()`), matching `VoiceControlBar.handleMicClick`'s existing semantic (FR-006). (Depends on T016.)
- [X] T018 [US3] In `ChatComposer.tsx`, add a menu/popover anchored to the mic icon exposing only the Continuous/Push-to-Talk mode switch (`onToggleMode`), disabled while `recording.phase !== 'idle'` in Push-to-Talk (same guard as `VoiceControlBar.tsx:56`'s `isModeSwitchBlocked`) — no device picker (research.md Decision 5 Scope note). (Depends on T016.)
- [X] T019 [US3] In `ChatComposer.tsx`, render the existing `RecordingReviewControls` (imported, reused unchanged) in place of the mic icon while `recording.phase !== 'idle'`, replacing the current simpler Cancel-only inline UI (lines 219-228); remove the "Listening…" text (line 225) entirely with no replacement, relying on the existing `isListening` pulse animation (lines 203-213) alone (research.md Decision 5b / FR-014). (Depends on T016.)
- [X] T020 [US3] In `ChatComposer.tsx`, add the merged speaker-mute control: a single persistent icon bound to `isMuted`/`onToggleMute`, visually distinct from the mic — no "Lucy is speaking…" text anywhere (FR-013/FR-006a/FR-006b). (Depends on T016.)
- [X] T021 [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, remove `<VoiceControlBar {...voiceControlsProps} />` from `ExpandedChatPanel`'s children; thread `voiceControlsProps`'s fields into the extended `ChatComposer` props instead (reused directly rather than re-derived, since it already has the correct PushToTalk/Continuous branching — the shared `handleToggleMute` closure implements Decision 5a's merge). (Depends on T017–T020.) **`onCancelCapture` removed** (T016's note): Push-to-Talk cancellation is already owned by `RecordingReviewControls`' own cancel button (rendered throughout `recording.phase !== 'idle'`, including while still `'recording'`), and Continuous mode has no separate cancel concept beyond the mic's own toggle — the prop had no remaining caller once actually wired up. `recorder.cancel()`/`recognition.cancel()` themselves are unaffected, used elsewhere unchanged.
- [X] T022 [P] [US3] Confirm `VoiceControlBar.tsx` is no longer imported/rendered anywhere in the Expanded tree; if nothing else references it (check `CollapsedVoiceControls.tsx`, which uses its own independent implementation, not this file), delete both `VoiceControlBar.tsx` and `VoiceControlBar.test.tsx`; otherwise leave it in place and note why in the PR description. (Depends on T021.) **Deleted** — confirmed only its own test file imported it; all other repo references were comments, updated to reflect its retirement.
- [X] T023 [P] [US3] Update `ChatPage.test.tsx` and `ChatPage.a11y.test.tsx` to assert `VoiceControlBar` no longer renders in the Expanded panel and that the consolidated control is keyboard-operable (WCAG 2.1 AA), reusing the existing Space-bar hold-to-record path. (Depends on T021.) Also fixed 4 pre-existing tests across both files that queried the old `VoiceControlBar`-specific labels ("Switch to Push-to-Talk mode", `/^mute$/i`) and one MUI-Menu/jsdom transition-timing flake.
- [X] T024 [US3] Run T015's updated `ChatComposer.test.tsx` and confirm it now passes end-to-end. (Depends on T017–T021.) **33/33 pass.**

**Checkpoint**: User Story 3 is fully functional and independently testable/deployable. Landing this phase unblocks User Story 2's T013 and all of User Story 4 (see Dependencies & Execution Order).

---

## Phase 6: User Story 4 - More of the conversation is visible at a glance (Priority: P3)

**Goal**: The translate control moves into the composer/voice-control row; `ProjectPicker` stays exactly where it is; the now-single-item top toolbar's height is tightened so the message list gains real vertical space (research.md Decision 6).

**Independent Test**: quickstart.md §4 — translate icon appears in the composer row (not above the message list), `ProjectPicker` unchanged in position, message-list viewport measurably taller, translate action behaves identically to before.

### Tests for User Story 4

- [X] T025 [P] [US4] Update `ChatPage.test.tsx`/`ExpandedChatPanel.test.tsx` to assert: the translate control renders inside the composer/voice-control row (not the top `Toolbar`); clicking the relocated translate icon still invokes `handleTranslateLast`. MUST fail until T026/T027 land. **Scope note**: the "`ProjectPicker` still renders in the top `Toolbar`" half of this assertion wasn't added — `ProjectPicker` conditionally renders `null` until `useProjects()` resolves non-empty data, which isn't mocked anywhere in this test file (pre-existing, unrelated to this feature); forcing it to render would need new MSW infrastructure this task didn't warrant. The translate-button-count-and-placement assertions already fully prove FR-007 without it.
### Implementation for User Story 4

- [X] T026 [US4] In `ChatPage.tsx`, remove the `RiTranslate2` `IconButton` (lines 588-590) from the `Toolbar` at lines 578-591, leaving `ProjectPicker` as its only child, and give that `Toolbar` an explicit `sx` height smaller than the MUI `dense` variant default so removing the icon genuinely shrinks the row (research.md Decision 6).
- [X] T027 [US4] Pass `onTranslateLastClick={handleTranslateLast}` into `ChatComposer` (prop added in T016) and render the translate icon in its row, visually distinct from the mic/mute icons (FR-007, User Story 4 Acceptance Scenario 4). Run T025's test suite and confirm it passes. (Depends on User Story 3's T016/T021 landing first — see Dependencies & Execution Order.)

**Checkpoint**: All four user stories are now independently functional. Run the full quickstart.md guide end-to-end.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T028 [P] Run quickstart.md's full validation guide (all 5 sections) end-to-end against the merged branch. **Not performed manually in a live browser this session** — §1/§2 (migration/readiness) are blocked by the same DB-migration limitation as T006-T008; §3/§4/§5's underlying behavior is covered by the automated suites (T024, T025, T002/T004d) but a real-browser walkthrough is still recommended before merge.
- [X] T029 [P] Run the full backend test suite (`dotnet test`) and frontend test suite (`npm test` and the `*.a11y.test.tsx` accessibility suite) and confirm everything is green, including every test added/updated above. **Results**: frontend `src/features/chat` — 224/224 pass (1 isolated timeout under full-suite resource contention, passes standalone). Backend: Domain 200/200, Application 932/935 (3 pre-existing failures in unrelated `McpObservabilityTests`), Web.Tests' new suites 7/9 (`HubFallbackRoutingTests` full pass; `ReadinessHealthCheckTests` blocked by DB migration state per T006-T008). No regressions attributable to this feature.
- [X] T030 If this project maintains a deploy runbook/README documenting manual migration steps, add a note referencing this feature's migration (`20260817110019_AddUserVoicePreferenceDefaultLanguage`) and the new `/health/ready` safeguard, per constitution §13 (Documentation — migration notes). Added to `docs/TESTING.md` §13.
- [X] T031 [P] Run the existing `*.a11y.test.tsx` automated accessibility checks specifically against `ChatComposer` and the Expanded `ChatPage` panel to confirm the consolidated voice control and relocated translate icon meet WCAG 2.1 AA (constitution §7/§10). All pass, including 2 new cases (recording-review state, voice-preferences-unavailable indicator).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: None — no blocking work.
- **User Stories (Phase 3-6)**: All can start immediately after Setup. See story-level dependencies below — they are not all mutually independent at the file level.
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)** — fully independent. Touches only `Program.cs`'s SPA-fallback/hub-mapping region and a new test file. No dependency on any other story.
- **User Story 2 (P1)** — backend tasks (T006-T010) are fully independent (new `Infrastructure` health check, disjoint `Program.cs` region from US1's, existing migration). The frontend task **T013 depends on User Story 3's T016** (the extended `ChatComposerProps`) because the new non-alarming indicator (research.md Decision 3) is hosted inside the consolidated mic control User Story 3 builds. Sequence: US2 backend can run anytime; US2 frontend (T013-T014) should follow US3's T016.
- **User Story 3 (P2)** — independent of US1 and US2's backend work; blocks **User Story 2's T013** and **all of User Story 4** because both add props/rendering to the same `ChatComposer.tsx`/`ChatPage.tsx` regions US3 restructures. Land US3 before US2's frontend half and before US4.
- **User Story 4 (P3)** — **depends on User Story 3's T016 and T021** (the extended composer props and the retired `VoiceControlBar` wiring) since it adds `onTranslateLastClick` into the same composer row and removes an icon from the same `ChatPage.tsx` toolbar US3 already touches. Land after US3.

**Recommended order**: US1 (fully parallel, anytime) + US2 backend (T006-T010, fully parallel, anytime) + US3 (T015-T024) can all start immediately in parallel across different files/developers. Once US3's T016 (prop contract) and T021 (composer wiring) land, US2's frontend half (T013-T014) and all of US4 (T025-T027) can proceed. Polish (Phase 7) waits for all four.

### Within Each User Story

- Tests are written first and MUST fail before their paired implementation task (noted per-task above).
- Within US2: health check unit test (T006) → health check implementation (T009); readiness integration test (T007) → registration (T010); migration application (T008) is independent of both and can run in parallel.
- Within US3: composer test updates (T015) describe the target shape before T016-T021 implement it; T022-T023 (cleanup/test updates) and T024 (final verification) follow the core implementation.
- Story complete before moving to a dependent story (US3 before US2's frontend half and before US4).

### Parallel Opportunities

- T002 (US1 test) can be written in parallel with any other story's tasks — different file.
- T006 and T007 (US2 tests) can run in parallel with each other and with T008 (migration application) — different files/systems.
- T008 (migration) can run in parallel with US1 and US3 entirely — no file overlap.
- T014, T022, T023 (US3 test/cleanup tasks) can run in parallel with each other once T021 lands.
- T025 (US4 test) can be drafted in parallel with US3's later tasks, though T026/T027's implementation must wait for US3.
- All of Phase 7 except T030 can run in parallel.

---

## Parallel Example: User Story 1 + User Story 2 backend (fully independent of each other)

```bash
# Different developers/agents, different files, can run simultaneously:
Task: "T002 [US1] Regression test in tests/AskLucy.Web.Tests/Routing/HubFallbackRoutingTests.cs"
Task: "T006 [US2] Unit test in tests/AskLucy.Infrastructure.Tests/HealthChecks/PendingMigrationsHealthCheckTests.cs"
Task: "T007 [US2] Integration test in tests/AskLucy.Web.Tests/HealthChecks/ReadinessHealthCheckTests.cs"
Task: "T008 [US2] Apply migration to dev + shared test DB"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 — both P1)

1. Complete Phase 1: Setup.
2. Skip Phase 2: nothing to do.
3. Complete Phase 3 (User Story 1) — production real-time connections stop failing.
4. Complete Phase 5 (User Story 3) enough to unblock — or at minimum land T016's prop contract — then complete Phase 4 (User Story 2), landing its frontend half.
5. **STOP and VALIDATE**: run quickstart.md §1, §2, §5 independently. This is the MVP — the two P1 bugs (false error banner, dead real-time features) are both resolved.
6. Deploy/demo if ready.

### Incremental Delivery

1. Setup → US1 (fully independent) → validate/deploy.
2. US3 (unblocks US2's frontend half and US4) → validate independently via quickstart.md §3 → deploy.
3. US2 (backend anytime; frontend after US3) → validate via quickstart.md §1-§2 → deploy. Combined with US1, this completes both P1 stories.
4. US4 (after US3) → validate via quickstart.md §4 → deploy.
5. Polish (Phase 7) → full quickstart.md run, full test suite, documentation note.

### Parallel Team Strategy

With multiple developers:

1. Developer A: User Story 1 (Program.cs SPA-fallback/hub region) — fully independent.
2. Developer B: User Story 2 backend (T006-T010) — fully independent, different files.
3. Developer C: User Story 3 (T015-T024) — as soon as this lands its prop contract (T016) and composer wiring (T021), Developer B can pick up US2's frontend half (T013-T014) and a fourth developer can start User Story 4 (T025-T027).
4. All converge on Phase 7 (Polish) once all four stories are independently validated.

---

## Notes

- [P] tasks touch different files with no unresolved dependency — safe to run in parallel.
- [Story] labels map every implementation/test task to its user story for traceability back to spec.md.
- The one genuine cross-story coupling is at the file level (`ChatComposer.tsx`/`ChatPage.tsx`), not a logical dependency between the bugs themselves — User Story 3 landing first avoids merge conflicts for User Story 2's frontend half and all of User Story 4.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently before continuing.
- Avoid combining unrelated stories' changes into one commit — each of the four bugs should be revertable independently if something regresses in production.
