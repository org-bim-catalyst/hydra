# Tasks: Selective Model Sync Review

**Input**: Design documents from `/specs/009-selective-model-sync-review/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included, not optional — matches this project's constitution §10/§18, same
standard applied in specs 005/007/008. The one genuinely new piece of business logic
(best-effort per-row apply) and the one genuinely new UI behavior (filter + selection
interacting correctly) both get dedicated test coverage.

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P1/P2). This is
almost entirely a change to one existing file per layer — `ApplyProviderModelSyncCommand`
and friends (backend) and `ModelSyncDialog.tsx` (frontend) — no new page, route, or
controller.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unmet dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Every task names an exact file path

## Path Conventions

Backend: `src/AskLucy.Application/Ai/Commands/ApplyProviderModelSync/`, `src/AskLucy.Web/Controllers/v1/AdminAiProvidersController.cs`.
Frontend: `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.tsx`, `src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiProvidersApi.ts`.
See plan.md's Project Structure for the full tree.

---

## Phase 1: Setup

No setup tasks — this feature modifies existing files only; no new project, dependency, or
scaffold is needed.

---

## Phase 2: Foundational (Blocking Prerequisites)

No shared blocking prerequisite phase. US1 (filter) and US2 (selection) both start from
`ModelSyncDialog.tsx` as spec 008 left it. They are sequenced (US1 before US2, per the User
Story Dependencies note below) because US2's acceptance scenario 5 ("select-all only
selects filtered rows") requires the filter to already exist, but neither story needs a
separate scaffolding phase first.

---

## Phase 3: User Story 1 - Administrator narrows a long diff to the models they care about (Priority: P1) 🎯 MVP

**Goal**: A single shared search box narrows both diff sides to rows whose display name or
model key match, live as the administrator types.

**Independent Test**: Trigger a sync that returns a large diff; type a partial model name;
confirm only matching rows remain on both sides; clear the search to restore the full list;
type something matching nothing and confirm a clear "no rows match" message (quickstart
Scenario 1).

### Tests for User Story 1

- [X] T001 [P] [US1] Extend `ModelSyncDialog.test.tsx` with filter cases: typing a partial name narrows both `added` and `removedFromVendor` rows to matches (case-insensitive, by `displayName` or `modelKey`); clearing the box restores the full list; a filter matching nothing shows a "no rows match" message distinct from the existing "nothing to review" empty-diff state, in `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.test.tsx`

### Implementation for User Story 1

- [X] T002 [US1] Add a single shared filter `TextField` and `filterText` state to `ModelSyncDialog.tsx`; filter the `added` and `removedFromVendor` arrays before rendering (case-insensitive substring match against `displayName`/`modelKey`); render a "No rows match your search" message per side when the unfiltered side has rows but the filter narrows it to zero, in `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.tsx` (depends on T001)

**Checkpoint**: User Story 1 is fully functional and independently testable — a long diff
is now navigable, even though Confirm still applies the whole (unfiltered) diff until US2
lands.

---

## Phase 4: User Story 2 - Administrator selects exactly which models to apply (Priority: P1)

**Goal**: Per-row checkboxes on both diff sides, per-side select-all/none acting only on
currently-visible (filtered) rows, a live selected-count, and Confirm sending only the
selected subset. The backend applies that subset best-effort — a stale row is skipped and
reported individually rather than blocking the rest.

**Independent Test**: From a diff with several added and several removed-from-vendor
models, check only some rows on each side, confirm; verify only the checked models changed
status and the unchecked ones are unaffected; re-run a sync and confirm the unchecked
models still appear in the diff; separately, force one selected row to be stale and confirm
the rest of the selection still applies while that row is reported by name (quickstart
Scenario 2 + 4).

### Tests for User Story 2

- [X] T003 [P] [US2] Extend `ApplyProviderModelSyncCommandHandlerTests.cs` — a request mixing one stale `added` entry (its `modelKey` already exists) with one valid `added` entry results in the valid entry being created (`Unavailable`) and committed, and the stale entry appearing in the returned `Failed` list with a reason naming it; same for a mixed `removedFromVendor` request (one `id` that doesn't belong to the provider, one that does); assert `IUnitOfWork.SaveChangesAsync` is called exactly once regardless of how many rows failed, in `tests/AskLucy.Application.Tests/Ai/ApplyProviderModelSyncCommandHandlerTests.cs`
- [X] T004 [P] [US2] Create `ApplyProviderModelSyncCommandValidatorTests.cs` — per **FR-013**, a request with both `added` and `removedFromVendor` empty fails validation with a "Nothing to apply" message; a request with at least one entry on either side passes, in `tests/AskLucy.Application.Tests/Ai/ApplyProviderModelSyncCommandValidatorTests.cs`
- [X] T005 [P] [US2] Extend `ModelSyncDialog.test.tsx` — a checkbox per row on both sides; checking a subset and confirming calls `applyModelSync` with exactly that subset (not the full diff); "select all" on a side selects only that side's currently-visible (filtered) rows; deselecting one row after "select all" leaves the rest selected; a rendered selected-count reflects the total across both sides; **FR-005**: select a row, then change the filter text so that row is no longer visible, then confirm — the selected-count still includes it and the apply call still includes it in the request, proving a filter change never silently drops an existing selection, in `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.test.tsx`
- [X] T006 [P] [US2] Extend `ModelSyncDialog.test.tsx` — a mixed `{ appliedModelKeys, failed }` response from `applyModelSync` renders a success indication for the applied models and names each failed model with its reason, in `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.test.tsx`

### Implementation for User Story 2

- [X] T007 [P] [US2] Create `ApplyProviderModelSyncResultDto.cs` — `ApplyProviderModelSyncResultDto(IReadOnlyList<string> AppliedModelKeys, IReadOnlyList<SyncApplyFailureDto> Failed)` and `SyncApplyFailureDto(string ModelKey, string DisplayName, string Reason)`, in `src/AskLucy.Application/Ai/Commands/ApplyProviderModelSync/ApplyProviderModelSyncResultDto.cs`
- [X] T008 [US2] Change `ApplyProviderModelSyncCommand` from `IRequest` to `IRequest<ApplyProviderModelSyncResultDto>` in `src/AskLucy.Application/Ai/Commands/ApplyProviderModelSync/ApplyProviderModelSyncCommand.cs` (depends on T007)
- [X] T009 [US2] Rewrite `ApplyProviderModelSyncCommandHandler.Handle` to build `AppliedModelKeys`/`Failed` per row (research.md Decision 2): for each `added` entry, check whether its `modelKey` already exists in the provider's catalog — if so, skip and add to `Failed` with the existing stale-diff message; otherwise `Create`+`SetStatus(Unavailable, ...)` and record its `modelKey`. For each `removedFromVendor` entry, check whether its `id` resolves to a model belonging to this provider — if not, skip and add to `Failed`; otherwise `SetStatus(Unavailable, ...)` and record its `modelKey`. Call `SaveChangesAsync` exactly once at the end for whatever was not skipped, then return the result DTO, in `src/AskLucy.Application/Ai/Commands/ApplyProviderModelSync/ApplyProviderModelSyncCommandHandler.cs` (depends on T007, T008)
- [X] T010 [US2] Rewrite `ApplyProviderModelSyncCommandValidator` — remove the per-row `CustomAsync` rule that rejects the whole command on a stale row (that check moves into the handler per T009); add a rule (FR-013) requiring at least one of `Added`/`RemovedFromVendor` to be non-empty, failing with "Nothing to apply.", in `src/AskLucy.Application/Ai/Commands/ApplyProviderModelSync/ApplyProviderModelSyncCommandValidator.cs` (depends on T009)
- [X] T011 [US2] Update `AdminAiProvidersController.ApplyModelSync` to return `ActionResult<ApplyProviderModelSyncResultDto>` (`Ok(result)`) instead of `NoContent()`, in `src/AskLucy.Web/Controllers/v1/AdminAiProvidersController.cs` (depends on T009)
- [X] T012 [P] [US2] Update `applyModelSync` in `adminAiProvidersApi.ts` to return a new `ApplyProviderModelSyncResult` type (`{ appliedModelKeys: string[]; failed: { modelKey: string; displayName: string; reason: string }[] }`) instead of `void`, in `src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiProvidersApi.ts`
- [X] T013 [US2] Add per-row `Checkbox` state to `ModelSyncDialog.tsx` (keyed by `modelKey` for `added` rows, `id` for `removedFromVendor` rows), a "select all"/"select none" control per side that acts only on the currently-filtered/visible rows for that side, and a rendered count of total selected rows across both sides, in `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.tsx` (depends on T002, T005)
- [X] T014 [US2] Change Confirm's handler to build the apply request from only the selected rows (not the full diff) and, on response, render the `appliedModelKeys`/`failed` result — a success Snackbar naming the applied count when `failed` is empty, and a result view naming each failed model with its reason when `failed` is non-empty, in `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.tsx` (depends on T012, T013)

**Checkpoint**: User Stories 1 and 2 both work independently — this is the direct fix for
"it's all 98 or none," with or without User Story 3's Confirm-disabled guard.

---

## Phase 5: User Story 3 - Confirm is blocked when nothing would happen (Priority: P2)

**Goal**: Confirm is disabled whenever zero rows are selected across both sides, and
enables/disables live as selections change.

**Independent Test**: Open a diff, select nothing; confirm Confirm is disabled. Select one
row; confirm it becomes enabled. Deselect it; confirm it becomes disabled again (quickstart
Scenario 3).

### Tests for User Story 3

- [X] T015 [P] [US3] Extend `ModelSyncDialog.test.tsx` — Confirm is disabled when the selected count is zero (including immediately after opening a fresh diff, and after deselecting everything that was selected); Confirm is enabled as soon as at least one row anywhere is selected, in `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.test.tsx`

### Implementation for User Story 3

- [X] T016 [US3] Disable the Confirm button in `ModelSyncDialog.tsx` whenever the total selected-row count (across both sides) is zero, in `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.tsx` (depends on T013)

**Checkpoint**: All three user stories are independently functional — the feature is
complete per spec.md.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T017 [P] Extend `AdminAiProvidersPage.a11y.test.tsx`'s existing sync-dialog-open a11y case to also cover the filter box and at least one checked checkbox, per constitution §10
- [ ] T018 Run all 4 quickstart.md scenarios end-to-end and record results, including: Scenario 4's partial-failure case (no dedicated end-to-end automated test forces a real mid-flight staleness conflict, so this manual pass — or the closest reproducible approximation — is its confirmation alongside T003's unit coverage); and Scenario 2 step 6 / **SC-004** (unselected rows still appear in a later diff) — this has no *new* automated test in this feature specifically, since it holds because `GetProviderModelSyncDiffQuery` is unchanged and already covered by spec 008's `GetProviderModelSyncDiffQueryHandlerTests.cs` — this manual pass is SC-004's confirmation for this feature

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup / Foundational**: None — start directly on US1.
- **User Stories (Phase 3–5)**: US1 has no dependency on anything. US2 depends on US1's
  filter existing in `ModelSyncDialog.tsx` (its acceptance scenario 5 requires "select all"
  to respect the active filter) — genuinely sequenced after US1, not just file-adjacent.
  US3 depends on US2's selection state (T013) existing, since Confirm's disabled condition
  is a function of the selected count US2 introduces.
- **Polish (Phase 6)**: Depends on US1–US3 all being complete.

### User Story Dependencies

- **US1 (P1)**: No dependencies. The feature's MVP slice (a long diff becomes navigable).
- **US2 (P1)**: Depends on US1 (T002) for the filter that its select-all behavior must
  respect.
- **US3 (P2)**: Depends on US2 (T013) for the selection state Confirm's disabled condition
  reads.

### Within Each User Story

- Tests written first, confirmed failing, then implementation.
- Backend DTO (T007) before the command/handler/validator/controller chain that uses it
  (T008–T011).
- Story complete and independently checkpointed before moving to the next.

### Parallel Opportunities

- T003 and T004 (US2 backend tests) — different files, parallel-safe with each other.
- T005 and T006 (US2 frontend tests) — same file (`ModelSyncDialog.test.tsx`), so not
  parallel with each other, but both are parallel-safe against T003/T004 (different
  layer).
- T007 (new DTO file) and T012 (frontend API type) are parallel-safe — different files,
  no shared dependency on each other, both only depend on the *shape* already agreed in
  data-model.md.
- T013 and T014 both touch `ModelSyncDialog.tsx` and are sequenced, not parallel.

---

## Parallel Example: User Story 2 backend

```bash
# Different files, no shared dependency:
Task: "Extend ApplyProviderModelSyncCommandHandlerTests.cs for the partial-failure case"
Task: "Create ApplyProviderModelSyncCommandValidatorTests.cs for the empty-request rule"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 3 (US1) — a long diff becomes searchable.
2. **STOP and VALIDATE**: run quickstart.md Scenario 1.
3. Deploy/demo if ready — Confirm still applies the whole (now merely filtered-for-viewing)
   diff at this point, exactly as spec 008 shipped it.

### Incremental Delivery

1. US1 → diff is searchable → validate → deploy/demo.
2. US2 → the actual capability gap closes: apply only a chosen subset, with best-effort
   partial application → validate → deploy/demo.
3. US3 → Confirm-disabled safety guard → validate → deploy/demo.
4. Polish (Phase 6) → a11y, full quickstart pass.

---

## Notes

- `[P]` tasks touch different files with no unmet dependency on an incomplete task.
- `[Story]` labels trace every task back to spec.md for scope/priority audits.
- Zero Domain or database changes anywhere in this task list — verified against
  `AIModel.Create`/`SetStatus` (unchanged) and `GetProviderModelSyncDiffQuery` (unchanged)
  before writing it.
- Commit after each task or logical group; stop at any checkpoint to validate a story
  independently before continuing.
- Avoid: vague tasks, two tasks editing the same file marked `[P]`, cross-story
  dependencies that would break a story's independent testability.
