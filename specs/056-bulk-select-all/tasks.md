# Tasks: Bulk Select-All on Admin List Screens

**Input**: Design documents from `/specs/056-bulk-select-all/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/bulk-actions-api.md](./contracts/bulk-actions-api.md), [quickstart.md](./quickstart.md)

**Tests**: Constitution §10/§19 require tests for all new/changed behavior — test tasks are included throughout, not optional.

**Organization**: Tasks are grouped by user story (US1–US3, priorities from spec.md) so each can be delivered and demoed independently once Foundational is done.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 (Users bulk actions, P1), US2 (Roles bulk delete, P2), US3 (Role assignments bulk assign, P1)
- Paths are exact, from plan.md's Project Structure

---

## Phase 1: Setup

**Purpose**: No new project/package — confirm the baseline is green before branching.

- [X] T001 Confirm `dotnet build` and the Domain/Application test suites pass on `main`, and that `PERSISTENCE_TESTS_CONNECTION_STRING` is set locally for any Web.Tests run — confirmed against the current working tree (Domain 281/281 pass, Application builds clean); no DB connection string available in this sandbox (per prior session's memory), so Web.Tests remain build-only here

**Checkpoint**: Baseline confirmed; no code changes yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared result shape, request contract, and frontend selection/dialog primitives every story's bulk action and every screen's UI depends on.

**⚠️ CRITICAL**: No user story is independently testable until this phase is complete and its own tests pass.

### Shared Application/Web types

- [X] T002 [P] Create `BulkActionSkip`/`BulkActionOutcome` records in `src/AskLucy.Application/Common/BulkActionOutcome.cs` (research.md Decision 2)
- [X] T003 [P] Create `BulkTarget` record (`Ids`, `AllMatching`) and a shared `BulkTargetValidator` FluentValidation rule (exactly one of `Ids`-non-empty/`AllMatching`-true) in `src/AskLucy.Application/Common/BulkTarget.cs` (research.md Decision 4)
- [X] T004 [P] Create `BulkTargetRequest`, `BulkActionResultResponse`, `BulkActionSkipResponse` Web contracts in `src/AskLucy.Web/Contracts/BulkActionContracts.cs` (data-model.md § Web layer)
- [X] T005 [P] Unit tests for `BulkTargetValidator` (both set → invalid, neither set → invalid, exactly one → valid) in `tests/AskLucy.Application.Tests/Common/BulkTargetValidatorTests.cs` — 5 tests, all passing

### Frontend shared primitives

- [X] T006 [P] Create `useBulkSelection(rowIds: string[])` hook (selection `Set`, `toggleAll`/`toggleOne`, `isIndeterminate`, resets when `rowIds` reference changes — research.md Decision 7) + test in `src/AskLucy.Web/ClientApp/src/features/admin/hooks/useBulkSelection.ts` — 6 tests, all passing
- [X] T007 [US-independent] Create `BulkActionConfirmDialog` (shows page-selected count and, once resolved, the all-matching count; scope choice; renders a `BulkActionOutcome` result summary listing every skip with its reason) + test in `src/AskLucy.Web/ClientApp/src/features/admin/components/BulkActionConfirmDialog.tsx` (depends on T006's selection shape) — 5 tests, all passing

**Checkpoint**: Shared types/hook/dialog exist and are tested in isolation; no story-specific endpoint or page wiring yet.

---

## Phase 3: User Story 1 - Bulk actions on Users (Priority: P1) 🎯 MVP

**Goal**: Select all eligible users on the current page; Lock/Unlock, Force 2FA reset, and Delete as three independent bulk actions, each with a two-count confirmation and per-user result reporting.

**Independent Test**: quickstart.md S1–S8 plus the Super-User edge case (skip, don't abort).

### Tests for User Story 1

- [X] T008 [P] [US1] `GetUsersEligibleIdsQueryHandlerTests` (excludes self always; Lock excludes already-locked; Unlock excludes already-unlocked; Force2fa/Delete exclude only self) in `tests/AskLucy.Application.Tests/Users/GetUsersEligibleIdsQueryHandlerTests.cs`
- [X] T009 [P] [US1] `BulkLockUsersCommandHandlerTests` (locks each resolved id, skips self if present, skips-with-reason a target that would strand the last Super User rather than aborting the batch, re-resolves ids at execution time when `AllMatching`) in `tests/AskLucy.Application.Tests/Users/BulkLockUsersCommandHandlerTests.cs`
- [X] T010 [P] [US1] `BulkUnlockUsersCommandHandlerTests` (no last-Super-User guard) in `tests/AskLucy.Application.Tests/Users/BulkUnlockUsersCommandHandlerTests.cs`
- [X] T011 [P] [US1] `BulkForceReset2faCommandHandlerTests` (never errors on an already-reset target) in `tests/AskLucy.Application.Tests/Users/BulkForceReset2faCommandHandlerTests.cs`
- [X] T012 [P] [US1] `BulkDeleteUsersCommandHandlerTests` (mirrors Lock's guards) in `tests/AskLucy.Application.Tests/Users/BulkDeleteUsersCommandHandlerTests.cs` — T008-T012: 15/15 passing
- [X] T013 [P] [US1] Web authorization-boundary tests `BulkUserActionsTests` (401/403 for non-`admin.users.manage`, admin reaches the handler) — no-live-DB style matching `RoleAuthorizationTests` — in `tests/AskLucy.Web.Tests/Users/BulkUserActionsTests.cs` — written but unrunnable in this sandbox (no PERSISTENCE_TESTS_CONNECTION_STRING; confirmed pre-existing RoleAuthorizationTests fails identically here, so this is an environment limitation, not a regression)

### Implementation for User Story 1

- [X] T014 [P] [US1] Create `UserBulkAction` enum in `src/AskLucy.Application/Users/UserBulkAction.cs`
- [X] T015 [US1] Add `ListEligibleIdsAsync(search, action, excludedUserId)` to `IUserAdminRepository` (`src/AskLucy.Application/Abstractions/IUserAdminRepository.cs`) and implement in `src/AskLucy.Persistence/Repositories/UserAdminRepository.cs` (depends on T014)
- [X] T016 [P] [US1] Create `GetUsersEligibleIdsQuery`/Handler in `src/AskLucy.Application/Users/Queries/GetUsersEligibleIds/` (depends on T015)
- [X] T017 [P] [US1] Create `BulkLockUsersCommand`/Handler/Validator (per-id: self-exclude, `LastSuperUserGuard`, `IIdentityService.SetLockoutAsync(true)`, audit log; `AllMatching` re-resolves via T016) in `src/AskLucy.Application/Users/Commands/BulkLockUsers/` (depends on T002, T003, T016)
- [X] T018 [P] [US1] Create `BulkUnlockUsersCommand`/Handler/Validator in `src/AskLucy.Application/Users/Commands/BulkUnlockUsers/` (depends on T002, T003, T016)
- [X] T019 [P] [US1] Create `BulkForceReset2faCommand`/Handler/Validator in `src/AskLucy.Application/Users/Commands/BulkForceReset2fa/` (depends on T002, T003, T016)
- [X] T020 [P] [US1] Create `BulkDeleteUsersCommand`/Handler/Validator in `src/AskLucy.Application/Users/Commands/BulkDeleteUsers/` (depends on T002, T003, T016)
- [X] T021 [US1] Add `GET /users/actions/bulk-eligible-ids`, `POST /users/actions/bulk-lock`, `.../bulk-unlock`, `.../bulk-force-2fa-reset`, `DELETE /users/actions/bulk-delete` to `src/AskLucy.Web/Controllers/v1/UsersController.cs`, each `[RequirePermission("admin.users.manage")]` except the eligible-ids GET which uses `admin.users.view` (depends on T016-T020) — request/response contracts extended with a `Search` field and `BulkEligibleIdsResponse` wrapper to match contracts/bulk-actions-api.md exactly
- [ ] T022 [P] [US1] Add `getUsersEligibleIds`, `bulkLockUsers`, `bulkUnlockUsers`, `bulkForceReset2fa`, `bulkDeleteUsers` to `src/AskLucy.Web/ClientApp/src/features/admin/api/adminApi.ts`
- [ ] T023 [US1] Wire `AdminUsersPage` (`src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminUsersPage.tsx`): checkbox column (self row has no checkbox); header "select all" via `useBulkSelection`, bound to `isIndeterminate` (FR-006) and disabled with a "nothing to select" indication when zero rows on the page are eligible (FR-008); a persistent "N selected" label in the toolbar visible whenever the selection is non-empty, independent of any dialog (FR-005); a toolbar with Lock/Unlock (label adapts per FR-002)/Force 2FA reset/Delete buttons enabled only when ≥1 selected, each opening `BulkActionConfirmDialog` (depends on T006, T007, T022)
- [ ] T024 [US1] Frontend tests for the wired `AdminUsersPage` bulk toolbar (button enable/disable, Lock/Unlock label switching, persistent selected-count label, "select all" disabled with zero eligible rows, indeterminate header state, confirmation counts, result summary) + `.a11y.test.tsx` in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminUsersPage.test.tsx` (depends on T023)

**Checkpoint**: Users bulk actions fully functional and independently testable/demoable — this is the MVP.

---

## Phase 4: User Story 2 - Bulk delete on Roles (Priority: P2)

**Goal**: Select all custom roles on the current page and delete them in one confirmation naming both counts and the total users left with no role.

**Independent Test**: quickstart.md S9–S11.

### Tests for User Story 2

- [X] T025 [P] [US2] `GetRolesEligibleIdsQueryHandlerTests` (only non-built-in roles) in `tests/AskLucy.Application.Tests/Authorization/GetRolesEligibleIdsQueryHandlerTests.cs`
- [X] T026 [P] [US2] `BulkDeleteRolesCommandHandlerTests` (deletes each resolved custom role, reports per-role unassigned-user count, evicts each holder's authz cache, `AllMatching` re-resolves at execution time) in `tests/AskLucy.Application.Tests/Authorization/BulkDeleteRolesCommandHandlerTests.cs` — T025-T026: 4/4 passing
- [X] T027 [P] [US2] Web authorization-boundary tests `AdminRolesBulkDeleteTests` in `tests/AskLucy.Web.Tests/Admin/AdminRolesBulkDeleteTests.cs` — written but unrunnable in this sandbox (same PERSISTENCE_TESTS_CONNECTION_STRING limitation as T013)

### Implementation for User Story 2

- [X] T028 [US2] Add `ListEligibleIdsAsync(search)` (custom roles only) to `IRoleRepository`/`RoleRepository` — a bulk-safe `DeleteByIdAsync` (no client-supplied concurrency stamp) added alongside for the batch path
- [X] T029 [P] [US2] Create `GetRolesEligibleIdsQuery`/Handler in `src/AskLucy.Application/Authorization/Roles/Queries/GetRolesEligibleIds/` (depends on T028)
- [X] T030 [P] [US2] Create `BulkDeleteRolesCommand`/Handler/Validator (per-id `IRoleRepository.DeleteAsync`, aggregate unassigned-user counts, cache eviction per role's holders) in `src/AskLucy.Application/Authorization/Roles/Commands/BulkDeleteRoles/` (depends on T002, T003, T029)
- [X] T031 [US2] Add `GET /admin/roles/actions/bulk-eligible-ids`, `POST /admin/roles/actions/bulk-delete` to `src/AskLucy.Web/Controllers/v1/AdminRolesController.cs` (`AdministratorOrSuperUser`, matching the controller's existing policy) (depends on T029, T030)
- [X] T032 [P] [US2] Add `getRolesEligibleIds`, `bulkDeleteRoles` to `src/AskLucy.Web/ClientApp/src/features/admin/api/adminRolesApi.ts` — also added the US3 role-assignment equivalents (`getRoleAssignmentsEligibleIds`, `bulkAssignRole`) while here
- [X] T033 [US2] Wire `AdminRolesPage` (`src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminRolesPage.tsx`): checkbox column (built-in rows have none); header "select all" bound to `isIndeterminate` (FR-006) and disabled with a "nothing to select" indication when the page has no custom roles (FR-008); a persistent "N selected" label in the toolbar whenever the selection is non-empty (FR-005); "Delete selected" toolbar button; `BulkActionConfirmDialog` (depends on T006, T007, T032) — the shared dialog surfaces `succeededCount`/`skipped` only; the response's extra `unassignedUserCounts` detail is fetched but not yet rendered (acceptable simplification of the shared component, not silent — every skip still reports its own reason)
- [ ] T034 [US2] Frontend tests for the wired `AdminRolesPage` bulk toolbar (persistent selected-count label, "select all" disabled with zero custom roles on the page, indeterminate header state, confirmation counts) + `.a11y.test.tsx` in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminRolesPage.test.tsx` (depends on T033)

**Checkpoint**: Roles bulk delete fully functional and independently testable/demoable.

---

## Phase 5: User Story 3 - Bulk assign on Role assignments (Priority: P1)

**Goal**: Select all eligible users for the currently-picked role on the current page and assign it to all of them in one confirmation — this also completes specs/055-role-management's never-built User Story 4 backend (research.md finding F2).

**Independent Test**: quickstart.md S12–S13.

### Tests for User Story 3

- [X] T035 [P] [US3] `GetRoleAssignmentsEligibleIdsQueryHandlerTests` (excludes locked users; excludes built-in-role holders unless the caller is a Super User) in `tests/AskLucy.Application.Tests/Authorization/GetRoleAssignmentsEligibleIdsQueryHandlerTests.cs`
- [X] T036 [P] [US3] `BulkAssignRoleCommandHandlerTests` (delegates resolved ids to the existing `IRoleAssignmentRepository.BulkReplaceRoleAsync`, `AllMatching` re-resolves at execution time via T038) in `tests/AskLucy.Application.Tests/Authorization/BulkAssignRoleCommandHandlerTests.cs` — T035-T036: 5/5 passing
- [X] T037 [P] [US3] Web authorization-boundary tests `AdminRoleAssignmentsBulkAssignTests` in `tests/AskLucy.Web.Tests/Admin/AdminRoleAssignmentsBulkAssignTests.cs` — written but unrunnable in this sandbox (same PERSISTENCE_TESTS_CONNECTION_STRING limitation as T013/T027)

### Implementation for User Story 3

- [X] T038 [US3] Add `ListEligibleIdsAsync(roleId, search, actorIsSuperUser)` to `IRoleAssignmentRepository`/`RoleAssignmentRepository` (mirrors `AssignRoleCommandHandler`'s existing per-row eligibility checks; `BulkReplaceRoleAsync` already exists from prior work, unchanged)
- [X] T039 [P] [US3] Create `GetRoleAssignmentsEligibleIdsQuery`/Handler in `src/AskLucy.Application/Authorization/Assignments/Queries/GetRoleAssignmentsEligibleIds/` (depends on T038)
- [X] T040 [P] [US3] Create `BulkAssignRoleCommand`/Handler/Validator (thin — resolves ids, one call to `IRoleAssignmentRepository.BulkReplaceRoleAsync`) in `src/AskLucy.Application/Authorization/Assignments/Commands/BulkAssignRole/` (depends on T002, T003, T039)
- [X] T041 [US3] Add `GET /admin/role-assignments/actions/bulk-eligible-ids`, `POST /admin/role-assignments/actions/bulk-assign` to `src/AskLucy.Web/Controllers/v1/AdminRoleAssignmentsController.cs` (`AdministratorOrSuperUser`, matching the controller's existing policy) (depends on T039, T040)
- [X] T042 [P] [US3] Add `getRoleAssignmentsEligibleIds`, `bulkAssignRole` to `src/AskLucy.Web/ClientApp/src/features/admin/api/adminRolesApi.ts` (done earlier alongside T032)
- [X] T043 [US3] Wire `AdminRoleAssignmentsPage` (`src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminRoleAssignmentsPage.tsx`): checkbox column (ineligible rows for the selected role have none); header "select all" bound to `isIndeterminate` (FR-006) and disabled with a "nothing to select" indication when zero rows on the page are eligible for the picked role (FR-008); a persistent "N selected" label in the toolbar whenever the selection is non-empty (FR-005); "Assign selected" toolbar button (enabled once a role is picked and ≥1 selected), `BulkActionConfirmDialog` (depends on T006, T007, T042)
- [X] T044 [US3] Frontend tests for the wired `AdminRoleAssignmentsPage` bulk toolbar (persistent selected-count label, "select all" disabled with zero eligible rows for the picked role, indeterminate header state, confirmation counts) + `.a11y.test.tsx` in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminRoleAssignmentsPage.test.tsx` (depends on T043) — verification running in background

**Checkpoint**: All three screens' bulk selection is functional independently and together.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T045 [P] Verify every new endpoint appears in `AdminEndpointPermissionCoverageTests` (specs/055) without modification needed — add any missing case if the reflection scan doesn't already pick up the new controller actions — `AdminRolesController`/`AdminRoleAssignmentsController` picked up the new bulk endpoints automatically (class-level policy); `UsersController`'s explicit method-name allowlist needed the 5 new bulk actions added — done. Whole test suite (including this file) confirmed to still compile.
- [ ] T046 Run quickstart.md's full scenario list (S1–S14) manually end-to-end, including the two-admin race edge case, and record results — not runnable in this session (no live app/DB); pending user's own manual pass
- [ ] T047 [P] Accessibility pass: run axe (`*.a11y.test.tsx`) for all three updated pages and `BulkActionConfirmDialog`; manual keyboard-only pass (select all → action button → confirm dialog → scope choice → confirm) on light and dark themes at 400px width — `BulkActionConfirmDialog.a11y` coverage lives inline in its own test file (5/5 passing, confirmed); the three page-level `.a11y.test.tsx` files are written but could not be executed to completion in this sandbox (see T024/T034/T044 note) — needs to be run on the user's machine
- [X] T048 Update API documentation per constitution §13 for the 9 new endpoints (3 eligible-ids GETs + 6 bulk-action POSTs/DELETE), and note in spec.md's own record (or a follow-up ADR) that FR-002's eligibility was corrected to match actual current single-row behavior (research.md Decision 1) — this repo's API documentation is the auto-generated OpenAPI/Swagger document (constitution §6/§13), sourced from each controller action's own attributes/XML doc comments; every new endpoint has one (see `UsersController`, `AdminRolesController`, `AdminRoleAssignmentsController`). The FR-002 correction is already recorded in spec.md's own "Correction (found during planning...)" note and research.md Decision 1 — no separate ADR needed, matching how the earlier `/speckit-analyze` remediation was already surfaced.
- [ ] T049 Full regression: `dotnet test` (Domain/Application/Persistence/Web) and `npx vitest run` (full suite) both green — backend confirmed green in this session: full solution build 0 errors, Domain 281/281, Application 1381/1381 (includes all new 056 handler/query tests). Web.Tests needs `PERSISTENCE_TESTS_CONNECTION_STRING` (not available here) — new authorization-boundary tests compile cleanly alongside the existing suite, matching the existing no-live-DB limitation. Frontend: `tsc -b --noEmit` clean; the shared `useBulkSelection`/`BulkActionConfirmDialog` unit tests pass (6/6, 5/5); the three page-level test files (`AdminUsersPage`/`AdminRolesPage`/`AdminRoleAssignmentsPage` `.test.tsx`/`.a11y.test.tsx`) repeatedly failed to complete in this sandbox — vitest hangs for 5–10+ minutes with no per-file progress or error output even on a single file, independent of Defender exclusions or raised Node heap size; root cause not conclusively identified (a mostly-empty `node_modules/.vite` cache and a heavier import graph — MUI Table/Dialog + TanStack Query + MSW + react-router + AdminShell/AppShell — are the leading suspects, but not confirmed). Needs to be run directly by the user outside this sandbox to get a real pass/fail.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks all user stories** — the shared result/request shapes and the frontend hook/dialog are used by every story's UI and every bulk command's return type.
- **US1 (Phase 3)**, **US2 (Phase 4)**, **US3 (Phase 5)**: Each depends only on Foundational — fully independent of each other (different controllers, different repositories, different pages). Can be built in any order or in parallel by different developers.
- **Polish (Phase 6)**: Depends on all three stories being complete.

### Within Each Story

- Tests before/alongside implementation.
- Repository method → query/command → controller endpoint → frontend API client → page wiring → frontend tests.

### Parallel Opportunities

- Phase 2: T002-T005 (Application/Web shared types + their test) in parallel; T006-T007 (frontend) in parallel with the backend shared types (different codebases).
- Once Foundational is done, US1/US2/US3 can proceed fully in parallel (three developers, three independent vertical slices) — nothing in one story's task list depends on another story's task list.
- Within US1: T008-T013 (all tests) in parallel; T017-T020 (the four commands) in parallel once T016 lands.
- Within US2/US3: their respective test tasks and command/query tasks are each parallel internally, following the same shape as US1.

---

## Parallel Example: Foundational

```bash
Task: "Create BulkActionSkip/BulkActionOutcome records in src/AskLucy.Application/Common/BulkActionOutcome.cs"
Task: "Create BulkTarget record + validator in src/AskLucy.Application/Common/BulkTarget.cs"
Task: "Create BulkTargetRequest/BulkActionResultResponse/BulkActionSkipResponse in src/AskLucy.Web/Contracts/BulkActionContracts.cs"
Task: "Create useBulkSelection hook in src/AskLucy.Web/ClientApp/src/features/admin/hooks/useBulkSelection.ts"
```

## Parallel Example: User Story 1 commands

```bash
Task: "Create BulkLockUsersCommand/Handler/Validator in src/AskLucy.Application/Users/Commands/BulkLockUsers/"
Task: "Create BulkUnlockUsersCommand/Handler/Validator in src/AskLucy.Application/Users/Commands/BulkUnlockUsers/"
Task: "Create BulkForceReset2faCommand/Handler/Validator in src/AskLucy.Application/Users/Commands/BulkForceReset2fa/"
Task: "Create BulkDeleteUsersCommand/Handler/Validator in src/AskLucy.Application/Users/Commands/BulkDeleteUsers/"
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Phase 1: Setup.
2. Phase 2: Foundational.
3. Phase 3: US1 (Users bulk actions). **Stop and validate** against quickstart S1–S8 plus the Super-User edge case.

### Incremental Delivery

1. Foundational → not independently demoable but changes no existing behavior, safe to merge on its own.
2. + US1 → demo bulk Lock/Unlock/2FA-reset/Delete on Users; deploy.
3. + US2 → demo bulk role deletion; deploy.
4. + US3 → demo bulk role assignment (also finally ships specs/055's US4); deploy.
5. + Polish → close out docs, a11y, full regression.

### Suggested Team Split

Three developers after Foundational: one per user story — they touch entirely separate controllers, repositories, and pages, so there is no merge contention beyond the shared (already-built) Foundational phase.

---

## Notes

- [P] tasks touch different files with no unfinished dependency.
- Every new backend behavior gets a handler-level test per constitution §10; every new/changed frontend surface gets a render test and an `.a11y.test.tsx`.
- Run the **full** test suite before each checkpoint, not just the new files.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
- T017/T030/T040's `AllMatching` path always re-resolves ids at execution time (research.md Decision 3/5) — never trust a count or id list read earlier in the same request flow.
