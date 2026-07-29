# Tasks: Admin Dashboard & User Management Console

**Input**: Design documents from `/specs/001-admin-dashboard/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/api-v1.md](./contracts/api-v1.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (§10 Testing Standards) treats automated test coverage as non-negotiable for Application/Domain logic and every new authorization boundary, and every prior phase of the sibling SPEC-000 feature included test tasks (e.g. `RoleAuthorizationTests.cs`, `NoSecretLeakageTests.cs`) — this feature follows the same established project convention.

**Organization**: Tasks are grouped by user story from `spec.md`. US1 and US2 are both P1; US3 is P2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Maps the task to US1 (Dashboard), US2 (User management actions), or US3 (Search/sort/pagination)

## Path Conventions

Existing web-application layout (`plan.md` § Project Structure): `src/AskLucy.*` (Clean Architecture backend), `tests/AskLucy.*.Tests`, `frontend/src`.

---

## Phase 1: Setup

**Purpose**: New dependency this feature requires before any story-specific frontend work can begin.

- [X] T001 Add `d3` and `@types/d3` to `frontend/package.json` dependencies and run `npm install` (`research.md` Topic 6)

**Checkpoint**: Frontend can import `d3` in any component.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema/infrastructure changes shared by more than one user story. No story-specific behavior lives here.

**⚠️ CRITICAL**: Must complete before US1 (needs `CreatedAtUtc`), US2's delete action (needs `IsDeleted`), or US3 (needs `CreatedAtUtc` for sort, and the query filter to exclude soft-deleted rows).

- [X] T002 Add `CreatedAtUtc` (`DateTime`), `IsDeleted` (`bool`), `DeletedAtUtc` (`DateTime?`), `DeletedBy` (`string?`) properties to `ApplicationUser` in `src/AskLucy.Persistence/Identity/ApplicationUser.cs` (`research.md` Topic 1, `data-model.md` § ApplicationUser)
- [X] T003 [P] Create `ApplicationUserConfiguration` in `src/AskLucy.Persistence/Configurations/ApplicationUserConfiguration.cs`: `HasQueryFilter(u => !u.IsDeleted)`, index on `CreatedAtUtc`, index on `IsDeleted` — depends on T002
- [X] T004 Set `CreatedAtUtc = DateTime.UtcNow` at every existing `ApplicationUser` creation call site: `IdentityService.RegisterAsync` and the external-login auto-provisioning path in `src/AskLucy.Persistence/Identity/IdentityService.cs`, and `src/AskLucy.Web/DevSeed/DevAdminSeeder.cs` — depends on T002
- [X] T005 Add EF Core migration (`dotnet ef migrations add AddApplicationUserAuditColumns --project src/AskLucy.Persistence --startup-project src/AskLucy.Web`) in `src/AskLucy.Persistence/Migrations/`: adds the three columns + two indexes, backfills `CreatedAtUtc` for existing rows to the migration-apply UTC timestamp (`research.md` Topic 1 accepted approximation) — depends on T002, T003
- [X] T006 [P] Integration test: EF Core global query filter excludes a soft-deleted `ApplicationUser` from `dbContext.Users` queries by default, in `tests/AskLucy.Persistence.Tests/ApplicationUserQueryFilterTests.cs` — depends on T003

**Checkpoint**: Schema supports soft-delete and registration-trend queries; all three user stories can now proceed.

---

## Phase 3: User Story 1 - Administrator views platform health at a glance (Priority: P1) 🎯 MVP

**Goal**: An Administrator/Super User opens the Admin Dashboard and sees six live metrics rendered as d3.js charts, matching theme and viewport.

**Independent Test**: Log in as Administrator/Super User, navigate to the dashboard, confirm all six metrics render and match `GET /api/v1/admin/dashboard/summary`; confirm a non-admin gets `403` on the same endpoint.

### Tests for User Story 1

- [X] T007 [P] [US1] Contract test: `GET /api/v1/admin/dashboard/summary` returns `200` with the full `DashboardSummaryDto` shape for an Administrator/Super User, and `403` for a non-admin authenticated user, in `tests/AskLucy.Web.Tests/Admin/AdminDashboardTests.cs`
- [X] T008 [P] [US1] Unit test: `GetAdminDashboardSummaryQueryHandler` assembles correct counts/trend/distribution from a faked `IAdminDashboardRepository`, in `tests/AskLucy.Application.Tests/Admin/GetAdminDashboardSummaryQueryHandlerTests.cs`
- [X] T009 [P] [US1] Integration test: `AdminDashboardRepository.GetSummaryAsync` returns correct aggregates (total count, 30-day zero-filled daily trend, active/locked split, confirmed/pending split, 2FA count, role distribution) against a seeded test database, in `tests/AskLucy.Persistence.Tests/AdminDashboardRepositoryTests.cs`

### Implementation for User Story 1

- [X] T010 [P] [US1] Create `DashboardSummaryDto`, `DailyUserCountDto`, `RoleCountDto` records in `src/AskLucy.Application/Admin/DashboardSummaryDto.cs` (`data-model.md` § DashboardSummaryDto)
- [X] T011 [P] [US1] Create `IAdminDashboardRepository` interface in `src/AskLucy.Application/Abstractions/IAdminDashboardRepository.cs` — depends on T010
- [X] T012 [US1] Create `GetAdminDashboardSummaryQuery`/Handler in `src/AskLucy.Application/Admin/Queries/GetDashboardSummary/` — depends on T011
- [X] T013 [US1] Implement `AdminDashboardRepository` in `src/AskLucy.Persistence/Repositories/AdminDashboardRepository.cs`: grouped-by-day count over `CreatedAtUtc` for the trailing 30 days (zero-filled), grouped-by-lockout-state, grouped-by-`EmailConfirmed`, grouped-by-`TwoFactorEnabled`, grouped-by-role via join against `AspNetUserRoles`/`AspNetRoles` (`research.md` Topic 5) — depends on T011, T002
- [X] T014 [US1] Register `IAdminDashboardRepository` → `AdminDashboardRepository` in `src/AskLucy.Persistence/DependencyInjection.cs` — depends on T013
- [X] T015 [US1] Create `AdminDashboardController` in `src/AskLucy.Web/Controllers/v1/AdminDashboardController.cs`: `GET /api/v1/admin/dashboard/summary`, `[Authorize(Policy = "AdministratorOrSuperUser")]` — depends on T012
- [X] T016 [P] [US1] Create `adminApi.ts` dashboard-summary fetch function and `useAdminDashboard` hook (TanStack Query) in `frontend/src/features/admin/api/adminApi.ts` and `frontend/src/features/admin/hooks/useAdminDashboard.ts`
- [X] T017 [P] [US1] Create `NewUsersTrendChart.tsx` (d3 line/bar chart, 30-day daily trend, empty-state handling) in `frontend/src/features/admin/charts/NewUsersTrendChart.tsx` — reads colors from `useTheme()`/`frontend/src/theme/tokens/palette.ts`, not hardcoded hex (`research.md` Topic 6) — depends on T001
- [X] T018 [P] [US1] Create `RoleDistributionChart.tsx` (d3 donut/pie chart) in `frontend/src/features/admin/charts/RoleDistributionChart.tsx` — depends on T001
- [X] T019 [P] [US1] Create `StatusSplitChart.tsx` (shared d3 chart for active/locked and confirmed/pending splits) in `frontend/src/features/admin/charts/StatusSplitChart.tsx` — depends on T001
- [X] T020 [US1] Create `AdminDashboardPage.tsx` in `frontend/src/features/admin/pages/AdminDashboardPage.tsx`: total-users stat tile + the three charts, responsive layout, empty/zero-state — depends on T016, T017, T018, T019
- [X] T021 [US1] Wire `/admin/dashboard` route through the existing `AdminRoute` guard in the app's router configuration — depends on T020
- [X] T022 [P] [US1] Frontend component test: `AdminDashboardPage` renders all six metrics from a mocked API response (MSW), in `frontend/src/features/admin/pages/AdminDashboardPage.test.tsx` — depends on T020
- [X] T023 [P] [US1] Automated a11y check (axe) for `AdminDashboardPage` per constitution §10, in `frontend/src/features/admin/pages/AdminDashboardPage.a11y.test.tsx` — depends on T020

**Checkpoint**: Dashboard is fully functional and independently testable/demoable.

---

## Phase 4: User Story 2 - Administrator manages a user's account (Priority: P1)

**Goal**: An Administrator/Super User can lock, unlock, change a user's role (with the Super-User-only privilege-escalation guard), force a 2FA reset, and soft-delete a user account — each requiring confirmation and rejecting self-targeting.

**Independent Test**: Perform each of the five actions against a test user one at a time; confirm success and list reflection; confirm a non-admin session is rejected against each underlying endpoint; confirm self-targeting and plain-Administrator role-escalation attempts are rejected with `403`.

### Tests for User Story 2

- [X] T024 [P] [US2] Contract test: `POST /api/v1/users/{id}/actions/lock` and `.../unlock` return `204` for a valid target and `403` for a non-admin caller and for a self-targeted call, in `tests/AskLucy.Web.Tests/Users/LockUnlockUserTests.cs`
- [X] T025 [P] [US2] Contract test: `PATCH /api/v1/users/{id}/role` returns `204` for a Super User granting/revoking Administrator/Super User, `204` for a plain Administrator changing a regular user's role, and `403` for a plain Administrator attempting to grant/revoke Administrator/Super User, in `tests/AskLucy.Web.Tests/Users/ChangeUserRoleTests.cs`
- [X] T026 [P] [US2] Contract test: `POST /api/v1/users/{id}/actions/force-2fa-reset` returns `204` for a valid target and `403` for a self-targeted call, in `tests/AskLucy.Web.Tests/Users/ForceReset2faTests.cs`
- [X] T027 [P] [US2] Contract test: `DELETE /api/v1/users/{id}` returns `204`, the user subsequently disappears from `GET /api/v1/users`, and returns `403` for a self-targeted call, in `tests/AskLucy.Web.Tests/Users/DeleteUserTests.cs`
- [X] T028 [P] [US2] Unit test: `LockUserCommandHandler`/`UnlockUserCommandHandler` reject self-targeting and call `IIdentityService.SetLockoutAsync` otherwise, with a faked `IIdentityService`, in `tests/AskLucy.Application.Tests/Users/LockUnlockUserCommandHandlerTests.cs`
- [X] T029 [P] [US2] Unit test: `ChangeUserRoleCommandHandler` enforces the Topic 4 privilege guard (Super User can grant/revoke Administrator/Super User; plain Administrator cannot; plain Administrator can still change a regular user's role) with a faked `IIdentityService`/`ICurrentUserAccessor`, in `tests/AskLucy.Application.Tests/Users/ChangeUserRoleCommandHandlerTests.cs`
- [X] T030 [P] [US2] Unit test: `ForceReset2faCommandHandler` rejects self-targeting and delegates to `IIdentityService.DisableTwoFactorAsync` otherwise, in `tests/AskLucy.Application.Tests/Users/ForceReset2faCommandHandlerTests.cs`
- [X] T031 [P] [US2] Unit test: `DeleteUserCommandHandler` rejects self-targeting and sets `IsDeleted`/`DeletedAtUtc`/`DeletedBy` via a faked `IUserAdminRepository` otherwise, in `tests/AskLucy.Application.Tests/Users/DeleteUserCommandHandlerTests.cs`

### Implementation for User Story 2

- [X] T032 [US2] Add `SetLockoutAsync(userId, locked: bool)`, `ChangeRoleAsync(userId, newRole)`, and `CountActiveSuperUsersAsync()` to `IIdentityService` in `src/AskLucy.Application/Abstractions/IIdentityService.cs` (`data-model.md` § "Where each handler gets its data access from")
- [X] T033 [US2] Implement `SetLockoutAsync`/`ChangeRoleAsync`/`CountActiveSuperUsersAsync` in `IdentityService` (`src/AskLucy.Persistence/Identity/IdentityService.cs`) using `UserManager.SetLockoutEnabledAsync`/`SetLockoutEndDateAsync`, `UserManager.GetRolesAsync`/`RemoveFromRolesAsync`/`AddToRoleAsync`, and `UserManager.GetUsersInRoleAsync("Super User")` filtered to non-locked accounts (`research.md` Topics 2, 4). **`ChangeRoleAsync` must treat `"Regular"` as "remove current privileged roles, add nothing"** — it is a sentinel, not a real `AspNetRoles` row; never call `AddToRoleAsync(user, "Regular")` (`data-model.md` § Commands) — depends on T032
- [X] T034 [P] [US2] Create `LockUserCommand`/Handler/Validator in `src/AskLucy.Application/Users/Commands/LockUser/` — self-action guard throws `UnauthorizedAccessException`; logs `AdminUserActionPerformed` (FR-021) — depends on T032
- [X] T035 [P] [US2] Create `UnlockUserCommand`/Handler/Validator in `src/AskLucy.Application/Users/Commands/UnlockUser/` — logs `AdminUserActionPerformed` — depends on T032
- [X] T036 [P] [US2] Create `ChangeUserRoleCommand`/Handler/Validator in `src/AskLucy.Application/Users/Commands/ChangeUserRole/` — privilege-escalation guard throws `UnauthorizedAccessException` (`research.md` Topic 4); logs `AdminUserActionPerformed` including old/new role — depends on T032
- [X] T036a [US2] Add a "last remaining Super User" guard shared by `ChangeUserRoleCommandHandler`, `LockUserCommandHandler`, and `DeleteUserCommandHandler` (FR-023): before applying the action, call `IIdentityService.CountActiveSuperUsersAsync()` (T032/T033); if the action's target is currently an active Super User and the count is exactly 1, throw `UnauthorizedAccessException`. Add unit tests in `tests/AskLucy.Application.Tests/Users/LastSuperUserGuardTests.cs` — depends on T032, T033, T034, T036, T040
- [X] T037 [P] [US2] Create `ForceReset2faCommand`/Handler/Validator in `src/AskLucy.Application/Users/Commands/ForceReset2fa/` — self-action guard; delegates to existing `IIdentityService.DisableTwoFactorAsync` (`research.md` Topic 3); logs `AdminUserActionPerformed`
- [X] T038 [P] [US2] Add `DeleteAsync(userId, actorUserId)` to `IUserAdminRepository` in `src/AskLucy.Application/Abstractions/IUserAdminRepository.cs`
- [X] T039 [US2] Implement `DeleteAsync` in `UserAdminRepository` (`src/AskLucy.Persistence/Repositories/UserAdminRepository.cs`): sets `IsDeleted = true`, `DeletedAtUtc = now`, `DeletedBy = actorUserId` — depends on T038, T002
- [X] T040 [P] [US2] Create `DeleteUserCommand`/Handler/Validator in `src/AskLucy.Application/Users/Commands/DeleteUser/` — self-action guard; logs `AdminUserActionPerformed` — depends on T039
- [X] T041 [US2] Add `POST /{userId}/actions/lock`, `POST /{userId}/actions/unlock`, `PATCH /{userId}/role`, `POST /{userId}/actions/force-2fa-reset`, `DELETE /{userId}` actions to `UsersController` in `src/AskLucy.Web/Controllers/v1/UsersController.cs`, all `[Authorize(Policy = "AdministratorOrSuperUser")]` — depends on T034, T035, T036, T037, T040
- [X] T042 [P] [US2] Create `UserActionMenu.tsx` (lock/unlock/role-change/force-2fa-reset/delete actions with MUI confirmation dialogs, FR-017) in `frontend/src/features/admin/components/UserActionMenu.tsx`
- [X] T043 [US2] Extend `adminApi.ts` with the five new action calls in `frontend/src/features/admin/api/adminApi.ts` — depends on T042
- [X] T044 [US2] Wire `UserActionMenu` into `AdminUsersPage.tsx` (`frontend/src/features/admin/pages/AdminUsersPage.tsx`) as a per-row action column, invalidating the user list query on success — depends on T043
- [X] T045 [P] [US2] Frontend component test: `UserActionMenu` shows a confirmation dialog before firing each destructive action and calls the correct API function on confirm, in `frontend/src/features/admin/components/UserActionMenu.test.tsx` — depends on T042

**Checkpoint**: All five user-management actions work end-to-end, independently of US1/US3.

---

## Phase 5: User Story 3 - Administrator finds a specific user quickly (Priority: P2)

**Goal**: The admin user list supports partial name/email search, sorting, and pagination instead of an unbounded flat table.

**Independent Test**: Seed more users than one page; search by partial email, sort by a column, and page through results, confirming correct filtering/ordering/counts.

### Tests for User Story 3

- [X] T046 [P] [US3] Contract test: `GET /api/v1/users?search=&sortBy=&sortDescending=&page=&pageSize=` returns correctly filtered/sorted/paginated `PagedResult<UserAdminDto>`, and excludes soft-deleted users, in `tests/AskLucy.Web.Tests/Users/GetUsersTests.cs`
- [X] T047 [P] [US3] Unit test: `GetUsersQueryHandler` passes search/sort/page parameters through to a faked `IUserAdminRepository.SearchAsync` and returns its `PagedResult`, in `tests/AskLucy.Application.Tests/Users/GetUsersQueryHandlerTests.cs`
- [X] T048 [P] [US3] Integration test: `UserAdminRepository.SearchAsync` filters by partial email/name match (parameterized, not string-interpolated SQL — constitution §8), sorts by `email`/`createdAtUtc`, and paginates correctly against a seeded test database, in `tests/AskLucy.Persistence.Tests/UserAdminRepositorySearchTests.cs`

### Implementation for User Story 3

- [X] T049 [P] [US3] Create `PagedResult<T>` record in `src/AskLucy.Application/Users/PagedResult.cs` (`data-model.md` § PagedResult)
- [X] T050 [US3] Replace `GetAllUsersQuery` with `GetUsersQuery(Search, SortBy, SortDescending, Page, PageSize)`/Handler in `src/AskLucy.Application/Users/Queries/GetUsers/` (removing the old `GetAllUsers/` folder) — depends on T049
- [X] T051 [US3] Add `SearchAsync(search, sortBy, sortDescending, page, pageSize)` to `IUserAdminRepository` in `src/AskLucy.Application/Abstractions/IUserAdminRepository.cs` — depends on T049
- [X] T052 [US3] Implement `SearchAsync` in `UserAdminRepository` (`src/AskLucy.Persistence/Repositories/UserAdminRepository.cs`) using parameterized `.Contains(...)`/`EF.Functions.Like`, `OrderBy`/`OrderByDescending`, `Skip`/`Take` (`research.md` Topic 7) — depends on T051
- [X] T053 [US3] Update `GET /api/v1/users` in `UsersController` (`src/AskLucy.Web/Controllers/v1/UsersController.cs`) to accept `search`/`sortBy`/`sortDescending`/`page`/`pageSize` query parameters and call `GetUsersQuery` — depends on T050, T052
- [X] T054 [US3] Add search input, sortable column headers, and pagination controls to `AdminUsersPage.tsx` (`frontend/src/features/admin/pages/AdminUsersPage.tsx`), reading/writing `PagedResult` via TanStack Query — depends on T053
- [X] T055 [P] [US3] Frontend component test: `AdminUsersPage` search/sort/pagination controls update the query and re-render correctly, in `frontend/src/features/admin/pages/AdminUsersPage.test.tsx` — depends on T054

**Checkpoint**: All three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Cleanup and final validation spanning all three stories.

- [X] T056 [P] Update `specs/000-legacy-modernization/contracts/api-v1.md`'s `/api/v1/users` row to cross-reference this feature's `contracts/api-v1.md` additions (keep the base contract accurate, don't duplicate content)
- [X] T057 [P] Author `docs/adr/00XX-interim-admin-security-controls.md` recording the accepted interim decision that admin actions are logged via structured Serilog only, not a separate immutable audit-trail store (constitution §8), per `plan.md` § Complexity Tracking, flagging the pre-existing project-wide gap for a future initiative
- [X] T057a [P] Add an `admin-endpoints` rate-limit policy in `src/AskLucy.Web/Program.cs` (mirroring the existing `ai-endpoints` `AddRateLimiter` policy), applied via `[EnableRateLimiting("admin-endpoints")]` to `UsersController`'s admin actions and `AdminDashboardController` — a generous per-user limit (e.g. 60 req/min) closes the constitution §6 gap for this feature's new endpoints rather than carrying it forward
- [X] T058 Run `specs/001-admin-dashboard/quickstart.md` end-to-end against a local environment and confirm every step passes
- [X] T059 [P] Run `dotnet test` and `cd frontend && npm run test` and confirm all new and existing suites pass with no regressions

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: No dependency on Setup; BLOCKS US1's dashboard trend, US2's delete action, and all of US3.
- **User Story 1 (Phase 3)**: Depends on Foundational (T002) for `CreatedAtUtc`. Independent of US2/US3.
- **User Story 2 (Phase 4)**: Depends on Foundational (T002) for the delete action only; lock/unlock/role/2FA-reset have no schema dependency. Independent of US1/US3.
- **User Story 3 (Phase 5)**: Depends on Foundational (T002/T003) for sorting by `createdAtUtc` and for the soft-delete query filter to exclude deleted users from search results. Independent of US1/US2, though it edits the same `UsersController.cs`/`AdminUsersPage.tsx` files US2 touches — do US2 before US3 if working sequentially to avoid merge friction (not a hard requirement).
- **Polish (Phase 6)**: Depends on all three stories being complete.

### Parallel Opportunities

- T001 (Setup) can run alongside T002–T006 (Foundational) — different files.
- Within Foundational: T003, T006 marked [P].
- Once Foundational completes, US1, US2, and US3 phases can proceed in parallel by different developers (US2 and US3 share `UsersController.cs`/`AdminUsersPage.tsx`, so those two specific tasks — T041/T044 vs. T053/T054 — should not be edited simultaneously by two people).
- Within US1: T007–T009 (tests) in parallel; T010, T011 in parallel; T017, T018, T019 (independent chart components) in parallel; T022, T023 in parallel.
- Within US2: T024–T031 (tests) all in parallel; T034–T038 (independent command folders) in parallel.
- Within US3: T046–T048 (tests) in parallel; T049 standalone.

---

## Parallel Example: User Story 1

```bash
# Tests, launched together:
Task: "Contract test for GET /api/v1/admin/dashboard/summary in tests/AskLucy.Web.Tests/Admin/AdminDashboardTests.cs"
Task: "Unit test for GetAdminDashboardSummaryQueryHandler in tests/AskLucy.Application.Tests/Admin/GetAdminDashboardSummaryQueryHandlerTests.cs"
Task: "Integration test for AdminDashboardRepository.GetSummaryAsync in tests/AskLucy.Persistence.Tests/AdminDashboardRepositoryTests.cs"

# Chart components, launched together (each is an independent new file):
Task: "Create NewUsersTrendChart.tsx in frontend/src/features/admin/charts/NewUsersTrendChart.tsx"
Task: "Create RoleDistributionChart.tsx in frontend/src/features/admin/charts/RoleDistributionChart.tsx"
Task: "Create StatusSplitChart.tsx in frontend/src/features/admin/charts/StatusSplitChart.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 + User Story 2 — both P1)

1. Complete Phase 1 (Setup) + Phase 2 (Foundational).
2. Complete Phase 3 (US1 — dashboard) and Phase 4 (US2 — user actions); both are P1 and together restore the legacy Control Panel's core value. Either order works since they're independent; doing US2 before US3 avoids the `UsersController.cs`/`AdminUsersPage.tsx` merge-friction note above.
3. **STOP and VALIDATE**: Run `quickstart.md` §§ 2–3 against the two P1 stories.
4. Deploy/demo — this is the MVP.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. Add US1 → validate independently → demo.
3. Add US2 → validate independently → demo (MVP complete once both P1 stories ship).
4. Add US3 → validate independently → demo (P2, usability enhancement as the user base grows).
5. Polish phase → cross-cutting validation, ADR, full regression run.

### Parallel Team Strategy

With two-plus developers: complete Setup + Foundational together, then split US1/US2/US3 across developers. Flag the shared-file note above (`UsersController.cs`, `AdminUsersPage.tsx`) between whoever takes US2 and whoever takes US3 to avoid overwriting each other's edits.
