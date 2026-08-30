---
description: "Task list for 044-location-viewer-regression"
---

# Tasks: Location & Site-Boundary Regression Fix

**Input**: Design documents from `/specs/044-location-viewer-regression/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/chat-stream-events.md](./contracts/chat-stream-events.md), [quickstart.md](./quickstart.md)

**Tests**: INCLUDED — FR-010 explicitly mandates regression tests for the fail-fast, fail-slow, and stale-state paths. This is not an optional TDD preference; it is a spec requirement.

**Organization**: Grouped by user story. US1 and US2 are both P1 and both modify `SendChatMessageCommandHandler`, so US2 depends on US1's restructure — recorded honestly in Dependencies rather than pretending they are independent.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3, mapping to spec.md user stories

## Path Conventions

Clean Architecture backend at repo root: `src/AskLucy.Domain/`, `src/AskLucy.Application/`, `src/AskLucy.Web/`, tests under `tests/`. No client changes (research Decision 7).

---

## Phase 1: Setup

**Purpose**: Establish a trustworthy baseline before touching anything.

- [X] T001 Run `dotnet build AskLucy.sln` and `dotnet test tests/AskLucy.Application.Tests/AskLucy.Application.Tests.csproj`, and record that the suite is green at HEAD (`8e83b8f`) — any pre-existing failure must be identified now, not misattributed to this feature later

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Configuration surface that US1 and US2 both consume.

**⚠️ CRITICAL**: No user story work begins until this phase completes.

- [X] T002 Add `BoundaryTimeoutSeconds` (default `45`, `[Range(1, 300)]`) to `src/AskLucy.Application/SiteBoundaries/BoundaryScoringOptions.cs`, and extend its existing `IValidatableObject.Validate` with the cross-field invariant `BoundaryTimeoutSeconds > VisionTimeoutSeconds` (data-model.md §Configuration) — without it, vision can never finish inside the aggregate budget and would be silently disabled in production
- [X] T003 Add `"BoundaryTimeoutSeconds": 45` to the `BoundaryScoring` section of `src/AskLucy.Web/appsettings.json`, adjacent to the existing `VisionTimeoutSeconds`
- [X] T004 [P] Add option-validation tests in `tests/AskLucy.Application.Tests/SiteBoundaries/BoundaryScoringOptionsTests.cs` asserting the new cross-field rule fails validation when `BoundaryTimeoutSeconds <= VisionTimeoutSeconds` and passes at the defaults (45 / 30)

**Checkpoint**: Options bind and validate at startup.

---

## Phase 3: User Story 1 — Viewer shows the location even when boundary resolution fails (P1)

**Goal**: A failing boundary step can never prevent the viewer update, terminate the turn, or leave stale boundary state behind.

**Independent test**: Drive `SendChatMessageCommandHandler` with a confirmed location and a boundary substitute that throws; assert the `ConfirmedLocation` chunk is still delivered and no exception escapes.

### Tests first (quickstart Scenario 1 — must FAIL before T006)

- [X] T005 [US1] Add the fail-fast regression test to `tests/AskLucy.Application.Tests/Ai/SendChatMessageBoundaryIntegrationTests.cs`: boundary substitute throws `HttpRequestException`; assert a chunk carries `ConfirmedLocation`, no exception escapes `Handle`, and enumeration completes. **Run it and confirm it FAILS** — a passing test here means it is not exercising the real path (diagnosis observed: `Exception escaped handler: HttpRequestException`, `ConfirmedLocation delivered: False`)

### Implementation

- [X] T006 [US1] In `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs`, split the single trailing chunk in two: yield the chunk carrying `ConfirmedLocation` (plus `RetrievalOutcome`, `MemoryOutcome`, `ViewerZoom`) **before** the boundary await at line ~202, and yield a second chunk carrying only `ConfirmedBoundary` after it (research Decision 1, contract C-1/C-2)
- [X] T007 [US1] In `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs`, wrap the `boundaryResolutionService.ResolveAsync(...)` call in a catch-all that logs the cause and continues the turn without a boundary (contract H-1, FR-002). Every catch must log and produce a user-visible outcome — constitution §VIII forbids catch-and-discard
- [X] T008 [US1] In `src/AskLucy.Web/Controllers/v1/AiController.cs`, write and flush `__LOCATION__` (and its `RecordActiveLocationCommand`) **inside** the `await foreach` at line ~76 the moment a chunk carrying `ConfirmedLocation` arrives, instead of after the loop at line ~214 (contract C-1). **T006 alone is insufficient** — the controller's drain-then-write structure nullifies the handler reorder on its own
- [X] T009 [US1] Add `UserChat.ClearActiveBoundary(string actor)` to `src/AskLucy.Domain/Chats/UserChat.cs`, setting `ActiveBoundary = null` and stamping `ModifiedAtUtc`/`ModifiedBy`, mirroring `SetActiveBoundary`'s actor handling
- [X] T010 [US1] In `src/AskLucy.Application/Chats/Commands/RecordActiveLocation/RecordActiveLocationCommandHandler.cs`, after `SetActiveLocation`, call `ClearActiveBoundary` when `chat.ActiveBoundary?.SiteName` differs from the incoming location name using `StringComparison.OrdinalIgnoreCase` — atomically in the same unit of work (research Decision 4, contracts S-1/S-2)

### Verification

- [X] T011 [P] [US1] Extend `tests/AskLucy.Application.Tests/Ai/SendChatMessageBoundaryIntegrationTests.cs` with a per-exception-type theory covering `HttpRequestException`, `JsonException`, `InvalidOperationException`, and `IndexOutOfRangeException` — FR-002 covers *any* type, not only network faults. For each type also assert the cause is logged and the user-facing boundary-unavailable sentence is emitted without implying the *location* failed (FR-005, FR-006, quickstart Scenario 2) — T019 asserts this only for the budget-expiry path, leaving the throw path unverified
- [X] T012 [P] [US1] Add a controller-level ordering test in `tests/AskLucy.Web.Tests/Ai/AiControllerChatStreamTests.cs`: instantiate `AiController` with a substituted `ISender` and a `DefaultHttpContext` whose `Response.Body` is a `MemoryStream`; assert `__LOCATION__` is present in the response bytes **before** the boundary chunk is produced (quickstart Scenario 3 — this is the assertion T008 exists for)
- [X] T013 [P] [US1] Add `ClearActiveBoundary` unit tests in `tests/AskLucy.Domain.Tests/Chats/UserChatActiveBoundaryTests.cs` (the existing home of `SetActiveBoundary`'s tests — not `UserChatTests.cs`, which would split related tests across two files): clears the boundary, stamps the actor, and leaves `ActiveLocation` untouched (contract S-4)
- [X] T014 [P] [US1] Add stale-state tests in `tests/AskLucy.Application.Tests/Chats/RecordActiveLocationCommandHandlerTests.cs`: a differently-named site clears the stored boundary even when no replacement was produced, **and** a same-named site leaves it intact so FR-009 boundary reuse still works (quickstart Scenario 6 — an over-eager clear would silently re-hit Overpass every turn)

**Checkpoint**: T005 now passes. The headline regression — "Show me Al Safa Park 2" leaving the viewer empty — is closed.

---

## Phase 4: User Story 2 — Viewer updates promptly, not minutes later (P1)

**Goal**: A hanging boundary step cannot stall the turn; the aggregate budget bounds it.

**Independent test**: Configure a 1-second budget, substitute a boundary service that hangs, assert the location is delivered and the turn completes in roughly the budget.

**Depends on**: Phase 3 (T006/T007 restructure the same handler method this phase modifies).

### Tests first

- [X] T015 [US2] Add the fail-slow regression test to `tests/AskLucy.Application.Tests/Ai/SendChatMessageBoundaryIntegrationTests.cs`: `BoundaryTimeoutSeconds = 1` via `Options.Create(...)`, boundary substitute awaits `Task.Delay(Timeout.Infinite, ct)`; assert the `ConfirmedLocation` chunk is delivered and the turn completes near the budget rather than hanging (research Decision 6 — a configurable budget is what makes this testable without a 45-second test)

### Implementation

- [X] T016 [US2] In `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs`, bound the boundary call with `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` plus `CancelAfter(TimeSpan.FromSeconds(options.Value.BoundaryTimeoutSeconds))`, passing the linked token into `ResolveAsync` (research Decision 2, contract H-2). Use a linked token, **not** `Task.WaitAsync` — the latter abandons the await while Overpass/ESRI/Gemini keep consuming connections
- [X] T017 [US2] In `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs`, order the cancellation catches against the **original** request token: `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` re-throws as genuine user cancellation, and a following bare `catch (OperationCanceledException)` logs a budget timeout and continues (contract H-3, FR-007). Reversing these reports every user cancellation as a boundary timeout — `GeminiBoundaryVisionAnalyzer` has an identically-shaped guard that makes this easy to get backwards

### Verification

- [X] T018 [P] [US2] Add a cancellation-distinction test in `tests/AskLucy.Application.Tests/Ai/SendChatMessageBoundaryIntegrationTests.cs`: cancel the caller's token mid-boundary and assert `OperationCanceledException` propagates and is **not** logged as a boundary timeout (quickstart Scenario 4 — a timeout-only test will not catch a reversed guard)
- [X] T019 [P] [US2] Assert in `tests/AskLucy.Application.Tests/Ai/SendChatMessageBoundaryIntegrationTests.cs` that the budget-expiry path logs a timeout distinguishable from a provider failure, and emits the user-facing boundary-unavailable sentence (FR-005, FR-006, SC-006)

**Checkpoint**: SC-007 holds — the turn completes within the budget even with every dependency hanging.

---

## Phase 5: User Story 3 — Gemini Vision remains strictly additive (P2)

**Goal**: Vision may improve a boundary; it may never remove one. The `9b19695` geometry-correction capability survives intact.

**Independent test**: With OSM candidates resolving normally, fail each vision sub-step in turn and assert the deterministic boundary is still returned.

**Depends on**: Phase 2 only — independent of the handler work in Phases 3–4.

### Implementation

- [X] T020 [US3] Wrap the imagery-fetch and vision-analysis calls inside `AnalyzeWithVisionAsync` in `src/AskLucy.Application/SiteBoundaries/BoundaryResolutionService.cs` (line ~125) in a `try/catch` degrading to `BoundaryVisionAnalysis.NotConfigured` with the cause logged — making the method's existing XML contract ("never throws and never blocks resolution on failure") true, and satisfying contract B-3. Re-throw `OperationCanceledException` per contract B-2

### Verification

- [X] T021 [P] [US3] Extend `tests/AskLucy.Application.Tests/SiteBoundaries/BoundaryResolutionServiceTests.cs` with the degradation matrix from quickstart Scenario 5: imagery returns null / imagery throws / analyzer throws / analyzer times out — each must return the deterministically-scored OSM boundary unchanged
- [X] T022 [P] [US3] Add capability-preservation tests in `tests/AskLucy.Application.Tests/SiteBoundaries/BoundaryResolutionServiceTests.cs`: a plausible `observed_boundary_normalized` still produces corrected geometry with source `AiInterpretation`, and an implausible one is still rejected with the mapped geometry kept (FR-008 — this is the regression guard proving the fix did not quietly disable what `9b19695` added)
- [X] T023 [P] [US3] Verify `tests/AskLucy.Application.Tests/Agents/SiteBoundaryResolverToolTests.cs` still passes — the tool is the service's other caller and inherits T020's isolation

**Checkpoint**: Vision is provably optional and provably still capable.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T024 Run the full `dotnet test tests/AskLucy.Application.Tests/AskLucy.Application.Tests.csproj` suite — not only the touched files; handler-level tests in this repo carry their own assertions about streamed chunks and have been broken by narrower runs before
- [X] T025 [P] Run `dotnet test tests/AskLucy.Domain.Tests/AskLucy.Domain.Tests.csproj` and `dotnet test tests/AskLucy.Web.Tests/AskLucy.Web.Tests.csproj` for the new domain and controller tests
- [X] T026 [P] Confirm the no-client-change claim (research Decision 7) by running `npm test` and `npx tsc -b --noEmit` in `src/AskLucy.Web/ClientApp` — use `tsc -b`, since the root config uses project references and a bare `tsc --noEmit` silently checks nothing
- [X] T027 Run `dotnet format --verify-no-changes` and fix any violations, keeping System usings first per repo convention
- [X] T028 Supersede the stale site-boundary documentation that this feature invalidates (constitution §16 gate 3): in `specs/042-site-boundary-resolution/contracts/chat-pipeline-integration.md`, mark the “**No new time-budget ceiling is introduced for v1**” decision (line ~63) as superseded by spec 044 FR-003 — that recorded decision is what produced the fail-slow half of this regression — and correct the claim (line ~99) that `RecordActiveSiteBoundaryCommand` ordering “matches `RecordActiveLocationCommand`'s exactly”, which T008 no longer holds true; add a pointer to `specs/044-location-viewer-regression/` from `docs/SITE_BOUNDARY_RESOLUTION_ARCHITECTURE.md`
- [X] T029 Review the exact `git diff` before committing; confirm nothing outside the files named in this plan was modified — in particular that no part of the `8e83b8f` spec-043 work was disturbed (working rules 9 and 10)
- [ ] T030 Manual verification against the deployed app: "Show me Al Safa Park 2" moves the viewer within seconds and *before* any outline appears; the outline follows; vision still corrects the known positional offset. Then force degradation (invalid Gemini credential or `EnableAiVisionVerification: false`) and confirm the location still displays and the turn still completes

---

## Dependencies

```
Phase 1 (T001)
   ↓
Phase 2 (T002-T004)  ── blocking for everything
   ↓
   ├─────────────────────────────┐
   ↓                             ↓
Phase 3 US1 (T005-T014)      Phase 5 US3 (T020-T023)
   ↓                             │   independent of US1/US2
Phase 4 US2 (T015-T019)          │
   ↓                             ↓
   └──────────► Phase 6 (T024-T030) ◄──┘
```

**Story dependencies**:

- **US1 → US2**: not independent. T016/T017 modify the same handler method T006/T007 restructure. Attempting them in parallel produces conflicting edits to `SendChatMessageCommandHandler.cs`.
- **US3**: fully independent of US1 and US2 — different file (`BoundaryResolutionService.cs`), no shared state. Can proceed in parallel with Phase 3 once Phase 2 is done.

## Parallel Execution Opportunities

| Phase | Parallel tasks | Why safe |
|---|---|---|
| 2 | T004 alongside T003 | Test file vs appsettings — different files |
| 3 | T011, T012, T013, T014 | Four different test files, all after implementation lands |
| 4 | T018, T019 | Same file but additive, independent test methods |
| 5 | T021, T022, T023 | Independent test methods/files |
| 6 | T025, T026 | Different test runners entirely |
| Cross-phase | **Phase 5 (US3) alongside Phase 3 (US1)** | Different source files; the largest real parallelism available |

## Implementation Strategy

**MVP scope — Phase 1 + Phase 2 + Phase 3 (US1)**: closes the reported regression. "Show me Al Safa Park 2" displays in the viewer regardless of boundary outcome, the turn always completes, and stale boundary state is repaired. Shippable on its own.

**Increment 2 — Phase 4 (US2)**: closes the fail-slow half. Lower urgency than it was at spec time, since `8e83b8f` already bounded the vision call at 30s, but the aggregate is still unbounded at ~90s worst case.

**Increment 3 — Phase 5 (US3)**: hardens vision as strictly additive and locks in the `9b19695` capability with tests. Independent, so it can be done first if preferred.

**Recommended order**: Phase 1 → 2 → 3 → 4 → 5 → 6, running Phase 5 in parallel with Phase 3 if working in more than one sitting.

**Per constitution §16**, this change touches a path with stated performance goals (SC-002, SC-007), so a performance review is required at merge. Accessibility and security gates do not apply — no UI and no auth/data-access surface is touched.
