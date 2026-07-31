# Tasks: Admin AI Model Catalog Management

**Input**: Design documents from `/specs/008-ai-model-catalog-management/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included, not optional — matches this project's constitution §10/§18 ("always
update or add tests when changing observable behavior"), same standard applied in specs
005 and 007. The diff-matching rule (research.md Decision 1) is the one genuinely new
piece of business logic in this feature and gets dedicated unit-test coverage, including
its regression case.

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P1/P2). Unlike
spec 007, this is **full-stack** — `AskLucy.Application`/`AskLucy.Web` (backend) and
`AskLucy.Web/ClientApp` (frontend) both change; no Domain or database change at all
(reuses `AIModel.SetStatus`/`Create` and all four providers' `ListAvailableModelsAsync()`
exactly as spec 005 delivered them).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unmet dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Every task names an exact file path

## Path Conventions

Backend: `src/AskLucy.Application/Ai/`, `src/AskLucy.Web/Controllers/v1/AdminAiProvidersController.cs`, `src/AskLucy.Web/Contracts/AiContracts.cs`.
Frontend: `src/AskLucy.Web/ClientApp/src/features/admin/{api,components,pages}/`.
See plan.md's Project Structure for the full tree.

---

## Phase 1: Setup

**Purpose**: Two small, independent additions nothing else strictly requires first, but
that later tasks will call into.

- [X] T001 [P] Create `AdminAiModelDto` (adds `Status` to the existing `ModelSummaryDto` shape: `id, modelKey, displayName, contextWindowTokens, maxOutputTokens, capabilities, pricing, releaseDate, status`) in `src/AskLucy.Application/Ai/AdminAiModelDto.cs`
- [X] T002 [P] Extend `AiAdminActionLog` with structured log entries for a model status change and a sync-apply (who, when, before/after status; how many added/marked-unavailable on apply) in `src/AskLucy.Application/Ai/AiAdminActionLog.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The one shared UI mechanism every user story renders into.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 Add an expand/collapse control to each provider row in `AdminAiProvidersPage.tsx` (MUI `Collapse`, toggle button) revealing an (initially empty) "Models" section for that provider — no data wired yet, in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx`

**Checkpoint**: Expanding a provider row shows an empty Models section. Nothing else is
wired yet — that's what each user story phase below adds.

---

## Phase 3: User Story 1 - Administrator reviews a provider's model catalog (Priority: P1) 🎯 MVP

**Goal**: Expanding a provider row shows every model for it, any status, with
capabilities, pricing (or "unknown"), and status.

**Independent Test**: Expand a provider with a mix of Available/Deprecated/Unavailable
models; confirm all are listed with capabilities, pricing-or-unknown, and status
(quickstart Scenario 1).

### Tests for User Story 1

- [X] T004 [P] [US1] Unit tests for `GetAdminAiModelsQueryHandler` (faked `IAIModelRepository`) — returns every model for a provider regardless of status, and `pricing` is `null` (never a fabricated zero) when unset, in `tests/AskLucy.Application.Tests/Ai/GetAdminAiModelsQueryHandlerTests.cs`
- [X] T005 [P] [US1] Auth/contract tests for `GET /api/v1/admin/ai/providers/{providerId}/models` (401 anonymous, 403 non-admin, pass-through for admin) extending `tests/AskLucy.Web.Tests/Ai/AdminAiProvidersControllerTests.cs`

### Implementation for User Story 1

- [X] T006 [US1] Create `GetAdminAiModelsQuery`/`Handler` (reads `IAIModelRepository.ListByProviderIdAsync(providerId)` — already returns every status) in `src/AskLucy.Application/Ai/Queries/GetAdminAiModels/GetAdminAiModelsQuery.cs` and `GetAdminAiModelsQueryHandler.cs` (depends on T001)
- [X] T007 [US1] Add `GET providers/{providerId:guid}/models` action to `AdminAiProvidersController.cs`, returning `AdminAiModelDto[]` (depends on T006)
- [X] T008 [P] [US1] Add `getModels(providerId)` to `adminAiProvidersApi.ts`, returning the admin `AdminAiModel[]` shape (with `status`) — distinct from the end-user `ModelSummary` type in the chat feature, in `src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiProvidersApi.ts`
- [X] T009 [US1] Wire `T003`'s Models section to `getModels` via `useQuery`, rendering a table (display name, capabilities, pricing-or-"unknown", status `Chip`) in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx` (depends on T003, T007, T008)

**Checkpoint**: User Story 1 is fully functional and independently testable — this is the
feature's MVP; an administrator can finally see what's really in the catalog (the gap
that started this whole feature).

---

## Phase 4: User Story 2 - Administrator manually curates a model's status (Priority: P1)

**Goal**: An administrator can change a model's status (Available/Deprecated/Unavailable),
confirm-gated, and end users immediately stop/start being able to select it.

**Independent Test**: Deprecate an Available model, confirm; confirm it's no longer
end-user-selectable and past conversations are unaffected; reinstate it (quickstart
Scenario 2).

### Tests for User Story 2

- [X] T010 [P] [US2] Unit tests for `UpdateAiModelStatusCommandHandler` (faked repository) — any status transition is allowed, in `tests/AskLucy.Application.Tests/Ai/UpdateAiModelStatusCommandHandlerTests.cs`
- [X] T011 [P] [US2] Component tests for `AiModelStatusMenu` — status-change menu opens a confirm dialog per target status; Cancel does not call the API; Confirm does, in `src/AskLucy.Web/ClientApp/src/features/admin/components/AiModelStatusMenu.test.tsx`

### Implementation for User Story 2

- [X] T012 [US2] Create `UpdateAiModelStatusCommand`/`Handler`/`Validator` (calls the existing `AIModel.SetStatus`, logs via `AiAdminActionLog`) in `src/AskLucy.Application/Ai/Commands/UpdateAiModelStatus/` (depends on T002)
- [X] T013 [US2] Add `PATCH models/{id:guid}` action (relative to the controller's existing `api/v1/admin/ai` base route, matching T007/T021's phrasing) to `AdminAiProvidersController.cs` (depends on T012)
- [X] T014 [P] [US2] Add `updateModelStatus(id, status)` to `adminAiProvidersApi.ts`
- [X] T015 [US2] Create `AiModelStatusMenu.tsx` (per-model status-change menu, confirm-gated per FR-010, mirrors `AiProviderActionsMenu.tsx`'s `pendingAction`/`CONFIRM_COPY`/Snackbar pattern — including its Snackbar/Alert feedback on every success/error, per FR-011) and wire it into each row of `T009`'s model table in `src/AskLucy.Web/ClientApp/src/features/admin/components/AiModelStatusMenu.tsx` and `AdminAiProvidersPage.tsx` (depends on T009, T014)

**Checkpoint**: User Stories 1 and 2 both work independently — this alone is the direct
fix for "I can't curate the catalog at all," with or without User Story 3.

---

## Phase 5: User Story 3 - Administrator syncs the catalog from the vendor (Priority: P2)

**Goal**: An administrator triggers a check against the vendor's own model list, reviews a
diff, and explicitly confirms before anything changes; a newly-added model starts
Unavailable (per spec.md's second clarification) and a deliberately-deprecated model is
never re-proposed (per the first clarification).

**Independent Test**: Sync a provider whose vendor catalog has diverged; confirm a diff
appears and nothing changes until confirmed; confirm afterward that added models are
Unavailable and re-syncing doesn't re-propose them (quickstart Scenario 3).

### Tests for User Story 3

- [X] T016 [P] [US3] Unit tests for `GetProviderModelSyncDiffQueryHandler` (faked `IAIModelRepository`/`IAIProviderResolver`) — covering all four catalog-status × vendor-listing combinations: a vendor model not in the catalog at all is `added`; a currently-Available catalog model the vendor no longer lists is `removedFromVendor`; **a Deprecated/Unavailable catalog model the vendor still lists is neither** (regression case for the first clarification); **a Deprecated/Unavailable catalog model the vendor also no longer lists is neither** (FR-006's explicit "never surfaced on this side either" exclusion) — in `tests/AskLucy.Application.Tests/Ai/GetProviderModelSyncDiffQueryHandlerTests.cs`
- [X] T017 [P] [US3] Unit tests for `ApplyProviderModelSyncCommandHandler` — each `added` entry is created with status Unavailable (never Available), each `removedFromVendor` entry's status becomes Unavailable, no row is ever deleted, in `tests/AskLucy.Application.Tests/Ai/ApplyProviderModelSyncCommandHandlerTests.cs`
- [X] T018 [P] [US3] Component tests for `ModelSyncDialog` — triggering sync shows the diff (or a clear "nothing to review" state); dismissing calls no apply; confirming calls apply with exactly the reviewed diff, in `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.test.tsx`

### Implementation for User Story 3

- [X] T019 [P] [US3] Create `ProviderModelSyncDiffDto` and `GetProviderModelSyncDiffQuery`/`Handler` implementing research.md Decision 1's matching rule in `src/AskLucy.Application/Ai/Queries/GetProviderModelSyncDiff/`
- [X] T020 [P] [US3] Create `ApplyProviderModelSyncCommand`/`Handler`/`Validator` implementing research.md Decision 2 (`AIModel.Create` then immediately `SetStatus(Unavailable, ...)` for each `added` entry; `SetStatus(Unavailable, ...)` for each `removedFromVendor` entry; validator rejects a stale diff — an `added.modelKey` that already exists, or a `removedFromVendor.id` that doesn't belong to the provider) in `src/AskLucy.Application/Ai/Commands/ApplyProviderModelSync/` (depends on T002)
- [X] T021 [US3] Add `POST providers/{providerId:guid}/models/actions/sync` and `POST providers/{providerId:guid}/models/actions/sync/apply` actions to `AdminAiProvidersController.cs` (depends on T019, T020)
- [X] T022 [P] [US3] Add `syncModels(providerId)` and `applyModelSync(providerId, diff)` to `adminAiProvidersApi.ts`
- [X] T023 [US3] Create `ModelSyncDialog.tsx` (trigger → shows diff or "nothing to review" → Confirm calls apply, Dismiss calls neither, confirm-gated per FR-010, with Snackbar/Alert feedback on every success/error per FR-011) and wire a "Sync from provider" button into `T003`'s expanded row in `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.tsx` and `AdminAiProvidersPage.tsx` (depends on T003, T022)

**Checkpoint**: All three user stories are independently functional — the feature is
complete per spec.md.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T024 [P] Extend `AdminAiProvidersPage.a11y.test.tsx` to cover the expanded-row state (model table + status menu + sync dialog open) for automated a11y violations, per constitution §10
- [X] T025 [P] Verify non-administrator denial for all four new endpoints, extending `tests/AskLucy.Web.Tests/Ai/AdminAiProvidersControllerTests.cs`'s existing 401/403 theory cases with the four new routes
- [ ] T026 Run all 4 quickstart.md scenarios end-to-end and record results, including the Scenario 3 step 3 regression check (re-syncing doesn't re-propose a just-applied model) and Scenario 2's attribution check (FR-004/SC-003 — past conversations keep their original provider/model display after a status change); FR-004/SC-003 has no dedicated automated test, so this manual pass is its only verification

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup only in the sense that it can run
  alongside it — start immediately.
- **User Stories (Phase 3–5)**: All depend on Foundational (T003) being in place, since
  every story renders into the expanded row it creates. US1 is the MVP. US2 depends on
  US1's rendered model table (T009) to attach its per-row menu to. US3 is functionally
  independent of US2 but shares the same expanded-row container (T003) and is sequenced
  after US1/US2 in this list for that reason, not a hard requirement.
- **Polish (Phase 6)**: Depends on US1–US3 all being complete.

### User Story Dependencies

- **US1 (P1)**: Foundational only. The feature's MVP.
- **US2 (P1)**: Foundational + US1's rendered table (T009) — genuinely sequenced after
  US1, since the status menu attaches to rows US1 renders.
- **US3 (P2)**: Foundational only, functionally — but shares files with US1/US2
  (`AdminAiProvidersPage.tsx`, `adminAiProvidersApi.ts`), so sequenced last to avoid
  simultaneous edits to the same regions.

### Within Each User Story

- Tests written first, confirmed failing, then implementation.
- Story complete and independently checkpointed before moving to the next.

### Parallel Opportunities

- T001 and T002 (Setup) — different files, no dependencies, fully parallel.
- Within each story, the backend query/command creation task and its test task are
  `[P]` against each other's siblings in *other* stories, but sequenced within the story
  per the "tests before implementation" rule.
- T019 and T020 (US3, different files: the query vs. the command) are parallel-safe with
  each other.
- Frontend `adminAiProvidersApi.ts` additions (T008, T014, T022) each touch the same file
  across different stories — sequenced by story, not parallel across stories, same
  reasoning as spec 007's shared-file tasks.

---

## Parallel Example: Setup

```bash
Task: "Create AdminAiModelDto in src/AskLucy.Application/Ai/AdminAiModelDto.cs"
Task: "Extend AiAdminActionLog in src/AskLucy.Application/Ai/AiAdminActionLog.cs"
```

## Parallel Example: User Story 3

```bash
# The diff query and the apply command are different files, no shared state:
Task: "Create GetProviderModelSyncDiffQuery/Handler in src/AskLucy.Application/Ai/Queries/GetProviderModelSyncDiff/"
Task: "Create ApplyProviderModelSyncCommand/Handler/Validator in src/AskLucy.Application/Ai/Commands/ApplyProviderModelSync/"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3 (US1) — administrators can finally see the real catalog.
4. **STOP and VALIDATE**: run quickstart.md Scenario 1.
5. Deploy/demo if ready.

### Incremental Delivery

1. Setup + Foundational → expandable row shell ready.
2. US1 → MVP (view catalog) → validate → deploy/demo.
3. US2 (manual curation) → validate → deploy/demo — this alone fixes the reported gap.
4. US3 (vendor sync) → validate → deploy/demo — the efficiency layer on top.
5. Polish (Phase 6) → a11y, access-control verification, full quickstart pass.

---

## Notes

- `[P]` tasks touch different files with no unmet dependency on an incomplete task.
- `[Story]` labels trace every task back to spec.md for scope/priority audits.
- Zero Domain or database changes anywhere in this task list — verified against the
  already-shipped `AIModel` entity and all four providers' `ListAvailableModelsAsync()`
  (research.md) before writing it.
- Commit after each task or logical group; stop at any checkpoint to validate a story
  independently before continuing.
- Avoid: vague tasks, two tasks editing the same file marked `[P]`, cross-story
  dependencies that would break a story's independent testability.
