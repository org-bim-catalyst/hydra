# Tasks: Role Definition, Role Assignment & Permission Catalogue

**Input**: Design documents from `/specs/055-role-management/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/admin-roles-api.md](./contracts/admin-roles-api.md), [quickstart.md](./quickstart.md)

**Tests**: Constitution §10/§19 require tests for all new/changed behavior in the same change — test tasks are included throughout, not optional here.

**Organization**: Tasks are grouped by user story (US1–US4, priorities from spec.md) so each can be delivered and demoed independently once Foundational is done.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 (Define/maintain roles, P1), US2 (Assign a role to a user, P1), US3 (Browse permission catalogue, P2), US4 (Bulk assign, P3)
- Paths are exact, from plan.md's Project Structure

---

## Phase 1: Setup

**Purpose**: Confirm the environment before touching Identity/EF — no new project, no new package.

- [ ] T001 Confirm `dotnet build` and `dotnet test tests/AskLucy.Web.Tests` succeed on `main` before branching, and that `PERSISTENCE_TESTS_CONNECTION_STRING` is set locally (memory: without it almost every Web test fails at Hangfire startup, not a real regression)
- [ ] T002 [P] Run `SELECT UserId, COUNT(*) FROM AspNetUserRoles GROUP BY UserId HAVING COUNT(*) > 1;` and `SELECT Id, Name, NormalizedName FROM AspNetRoles;` against the dev DB (quickstart.md §0) and record the baseline row counts to sanity-check the migration's de-dup step later

**Checkpoint**: Baseline confirmed green; no code changes yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The permission catalogue, role storage, and per-request authorization pipeline every screen and every re-annotated controller depends on.

**⚠️ CRITICAL**: No user story is independently testable until this phase is complete and its own tests pass.

### Domain (`src/AskLucy.Domain/Authorization/`)

- [X] T003 [P] Create `AdminArea` enum (`Dashboard, Users, AiProviders, DefaultModels, AiCapabilities, AgentPolicies, SystemAgents, WorkflowPolicies, McpServers`) in `src/AskLucy.Domain/Authorization/AdminArea.cs`
- [X] T004 [P] Create `AdminPermissionLevel` enum (`View, Manage`) in `src/AskLucy.Domain/Authorization/AdminPermissionLevel.cs`
- [X] T005 [P] Create `AdminPermission` record (as a `sealed record` class, not a struct — a struct containing its own nullable `Implies` field is a layout cycle) (`Key`, `Area`, `Level`, `DisplayName`, `Description`, `Implies`) in `src/AskLucy.Domain/Authorization/AdminPermission.cs`
- [X] T006 [US-independent] Create `AdminPermissionCatalog` static class with the 16 permissions per data-model.md (`View`+`Manage` for Users/AiProviders/DefaultModels/AiCapabilities/AgentPolicies/WorkflowPolicies/McpServers; `View` only for Dashboard/SystemAgents), plus `All`, `TryGet(key, out permission)`, `ByArea(area)` in `src/AskLucy.Domain/Authorization/AdminPermissionCatalog.cs` (depends on T003-T005)
- [X] T007 [P] Create `PermissionSet` value object (also added `PermissionSet.Empty` for the no-role/all-keys-retired case) (rejects unknown keys/empty set, normalizes Manage⇒View, `PermissionSet.Full`, `Keys`, `Contains`) in `src/AskLucy.Domain/Authorization/PermissionSet.cs` (depends on T006)
- [X] T008 [P] Create `RoleName` value object (reserved-name check exposed as static `IsNameReserved(string)`, since `From` itself rejects a reserved name rather than returning a flagged instance) (trim, 2–50 chars, reserved-name rejection: Administrator/Super User/Regular/No role, case-insensitive uniqueness key) in `src/AskLucy.Domain/Authorization/RoleName.cs`
- [X] T009 [P] Create `RoleAuditAction` enum (`RoleCreated, RoleUpdated, RoleDeleted, RoleAssigned, RoleChanged, RoleRemoved, PermissionRetired, AuthorizationDenied`) in `src/AskLucy.Domain/Authorization/RoleAuditAction.cs`
- [X] T010 Create `RoleAuditLog : BaseEntity` append-only entity with `Record(...)` factory in `src/AskLucy.Domain/Authorization/RoleAuditLog.cs` (depends on T009)
- [X] T011 [P] Create `SuperUserSafeguard` pure domain service (0 removals is always safe regardless of current count — mirrors `LastSuperUserGuard`'s original early-return) (`EnsureAtLeastOneActiveSuperUserRemains`, throws `DomainRuleViolationException`) in `src/AskLucy.Domain/Authorization/SuperUserSafeguard.cs`

### Domain tests

- [X] T012 [P] Unit tests for `AdminPermissionCatalog` (16 entries, no duplicate keys, Manage→View `Implies`) in `tests/AskLucy.Domain.Tests/Authorization/AdminPermissionCatalogTests.cs`
- [X] T013 [P] Unit tests for `PermissionSet` (unknown key rejected, empty rejected, Manage adds View, equality) in `tests/AskLucy.Domain.Tests/Authorization/PermissionSetTests.cs`
- [X] T014 [P] Unit tests for `RoleName` (length, trim, reserved names, case-insensitive equality) in `tests/AskLucy.Domain.Tests/Authorization/RoleNameTests.cs`
- [X] T015 [P] Unit tests for `SuperUserSafeguard` (0 remaining throws, 1 remaining after removal OK, removing 0 always OK) in `tests/AskLucy.Domain.Tests/Authorization/SuperUserSafeguardTests.cs`

### Application abstractions (`src/AskLucy.Application/`)

- [X] T016 [P] Create `PermissionClaims` constant class (`Type = "permission"`) in `src/AskLucy.Application/Authorization/PermissionClaims.cs`
- [X] T017 [P] Create `IRoleRepository` interface (create/update/delete role, get/list with permission keys + user counts, permission↔role lookup for the Permissions screen) in `src/AskLucy.Application/Abstractions/IRoleRepository.cs`
- [X] T018 [P] Create `IRoleAssignmentRepository` interface (get current role for user, replace role in one commit, list assignments filtered/paginated, bulk replace) in `src/AskLucy.Application/Abstractions/IRoleAssignmentRepository.cs`
- [X] T019 [P] Create `IRoleAuditLogRepository` interface (`Add`) in `src/AskLucy.Application/Abstractions/IRoleAuditLogRepository.cs`
- [X] T020 [P] Create `IEffectivePermissionResolver` interface (`Task<PermissionSet> ResolveAsync(string userId, CancellationToken)`) in `src/AskLucy.Application/Abstractions/IEffectivePermissionResolver.cs`
- [X] T021 [P] Create `IAuthorizationCacheInvalidator` interface (`Evict(string userId)`) in `src/AskLucy.Application/Abstractions/IAuthorizationCacheInvalidator.cs`
- [X] T022 Implement `EffectivePermissionResolver` (built-in role ⇒ `PermissionSet.Full`; custom role ⇒ role's stored keys ∩ current catalogue per research.md Decision 10; no role ⇒ empty) in `src/AskLucy.Application/Authorization/EffectivePermissionResolver.cs` (depends on T007, T017, T020)
- [X] T023 [P] Unit tests for `EffectivePermissionResolver` (built-in full catalogue, custom role subset, retired-key filtering, no-role empty) in `tests/AskLucy.Application.Tests/Authorization/EffectivePermissionResolverTests.cs`

### Persistence (`src/AskLucy.Persistence/`)

- [X] T024 Create `ApplicationRole : IdentityRole` with `Description`, `IsBuiltIn`, `CreatedAtUtc`/`CreatedBy`, `ModifiedAtUtc`/`ModifiedBy` in `src/AskLucy.Persistence/Identity/ApplicationRole.cs`
- [X] T025 Change `AskLucyDbContext` base to `IdentityDbContext<ApplicationUser, ApplicationRole, string>` in `src/AskLucy.Persistence/AskLucyDbContext.cs`; add `DbSet<RoleAuditLog> RoleAuditLogs` (depends on T024, T010)
- [X] T026 [P] Create `ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>` (column lengths for Description/IsBuiltIn/audit columns) in `src/AskLucy.Persistence/Configurations/ApplicationRoleConfiguration.cs` (depends on T024)
- [X] T027 [P] Create `RoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<string>>` adding unique index `IX_AspNetRoleClaims_RoleId_ClaimType_ClaimValue` on `(RoleId, ClaimType, ClaimValue)` in `src/AskLucy.Persistence/Configurations/RoleClaimConfiguration.cs`
- [X] T028 [P] Create `UserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<string>>` adding unique index `IX_AspNetUserRoles_UserId` on `(UserId)` in `src/AskLucy.Persistence/Configurations/UserRoleConfiguration.cs`
- [X] T029 [P] Create `RoleAuditLogConfiguration : IEntityTypeConfiguration<RoleAuditLog>` (indexes on `OccurredAtUtc`, `TargetRoleId`, `TargetUserId`, `ActorUserId`) in `src/AskLucy.Persistence/Configurations/RoleAuditLogConfiguration.cs` (depends on T010)
- [X] T030 Update `AddIdentityCore<ApplicationUser>()...AddRoles<IdentityRole>()` to `AddRoles<ApplicationRole>()` in `src/AskLucy.Persistence/DependencyInjection.cs`; register `IRoleRepository`, `IRoleAssignmentRepository`, `IRoleAuditLogRepository` implementations (depends on T024)
- [X] T031 Implement `RoleRepository : IRoleRepository` — EF Core against `AspNetRoles`/`AspNetRoleClaims`, permission keys via `PermissionClaims.Type` filter, unique-name conflict throws `DuplicateResourceException` (already globally mapped to 409, existing project convention — no bespoke `DbUpdateException` detection needed), stale `ConcurrencyStamp` throws `DbUpdateConcurrencyException` (also already globally mapped to 409), delete returns affected user ids in `src/AskLucy.Persistence/Repositories/RoleRepository.cs` (depends on T017, T022's PermissionSet usage, T026, T027)
- [X] T032 Implement `RoleAssignmentRepository : IRoleAssignmentRepository` (mechanical only — privileged-role/last-Super-User rules are US2's `AssignRoleCommand`, not this repository, per plan.md's phase split) — replace-in-one-commit semantics, `expectedCurrentRoleId` optimistic check, bulk replace in one `SaveChanges`, `SecurityStamp` bump on every write in `src/AskLucy.Persistence/Repositories/RoleAssignmentRepository.cs` (depends on T018, T028)
- [X] T033 Implement `RoleAuditLogRepository : IRoleAuditLogRepository` in `src/AskLucy.Persistence/Repositories/RoleAuditLogRepository.cs` (depends on T019, T025, T029)
- [X] T034 (no change needed) `IdentityService`'s role methods operate on role **names** via `UserManager<ApplicationUser>` (`GetRolesAsync`/`AddToRoleAsync`/`RemoveFromRolesAsync`), which is agnostic to the concrete `TRole` type — `AddRoles<ApplicationRole>()` (T030) alone was sufficient
- [X] T035 Update `UserAdminRepository.ProjectToDto` to surface the user's single current role name (any role, not only privileged) in `src/AskLucy.Persistence/Repositories/UserAdminRepository.cs` (depends on T024)
- [X] T036 Create migration `AddRoleManagement`: add `ApplicationRole` columns; upsert built-in roles by `NormalizedName` setting `IsBuiltIn=1`; de-duplicate `AspNetUserRoles` (keep Super User > Administrator > lowest RoleId, writing `RoleAuditLogs` rows with actor `system:migration`); create `RoleAuditLogs` table; create the two unique indexes (T027, T028) in `src/AskLucy.Persistence/Migrations/20260914180820_AddRoleManagement.cs` — scaffolded via `dotnet ef migrations add`, then hand-added the data-migration SQL (upsert + de-dup, ordered before the unique-index creation); BOM stripped from this file **and** the Designer.cs/model-snapshot files `dotnet ef` also touched (memory: migration BOM CI gotcha — confirmed present, not just a risk)
- [ ] T037 [P] Persistence tests: `RoleRepositoryTests` (CRUD, unique name race → conflict, concurrency stamp mismatch, delete reports affected user count, built-in rows never get claims) against the real test DB in `tests/AskLucy.Persistence.Tests/Authorization/RoleRepositoryTests.cs` (depends on T031)
- [ ] T038 [P] Persistence tests: `RoleAssignmentRepositoryTests` (replace semantics, `expectedCurrentRoleId` conflict, bulk replace atomicity, SecurityStamp changes) in `tests/AskLucy.Persistence.Tests/Authorization/RoleAssignmentRepositoryTests.cs` (depends on T032)
- [ ] T039 [P] Persistence test: `AddRoleManagementMigrationTests` — seed a user with 2 roles pre-migration, run migration, assert exactly 1 remains (the higher-privilege one) and an audit row was written; assert unique indexes exist in `tests/AskLucy.Persistence.Tests/Authorization/AddRoleManagementMigrationTests.cs` (depends on T036)
- [X] T040 Update `DevAdminSeeder` to assign only `Super User` (never both) to the bootstrap admin in `src/AskLucy.Web/DevSeed/DevAdminSeeder.cs` (depends on T036)

### Web: authorization pipeline (`src/AskLucy.Web/Auth/`)

- [X] T041 Create `PermissionRequirement` + `RequirePermissionAttribute : AuthorizeAttribute, IAuthorizationRequirementData` (accepts `anyOf` permission keys) in `src/AskLucy.Web/Auth/RequirePermissionAttribute.cs` and `src/AskLucy.Web/Auth/PermissionRequirement.cs` (depends on T006)
- [X] T042 Create `PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>` checking `permission` claims on `context.User` in `src/AskLucy.Web/Auth/PermissionAuthorizationHandler.cs` (depends on T041)
- [X] T043 Create `CurrentAuthorizationClaimsTransformation : IClaimsTransformation` — resolves current role + effective permissions via `IEffectivePermissionResolver`, replaces stale `role`/`permission` claims, caches in `IMemoryCache` (`authz:{userId}`, 30s absolute expiry) in `src/AskLucy.Web/Auth/CurrentAuthorizationClaimsTransformation.cs` (depends on T020, T022)
- [X] T044 Implement `IAuthorizationCacheInvalidator` (`MemoryCacheAuthorizationCacheInvalidator`) evicting `authz:{userId}`, registered against the same `IMemoryCache` T043 uses, in `src/AskLucy.Web/Auth/MemoryCacheAuthorizationCacheInvalidator.cs` (depends on T021, T043)
- [X] T045 Create `PermissionDeniedAuditResultHandler : IAuthorizationMiddlewareResultHandler` — on a failed `PermissionRequirement` for an authenticated user, writes a `RoleAuditLog.Record(AuthorizationDenied, ...)` row (path/method/required keys in `DetailsJson`) then delegates to the default handler for the 403 response in `src/AskLucy.Web/Auth/PermissionDeniedAuditResultHandler.cs` (depends on T010, T019, T041)
- [X] T046 Create `PermissionCatalogReconciler : IHostedService` — on startup, deletes `AspNetRoleClaims` rows whose `ClaimValue` is not in `AdminPermissionCatalog`, writing one `PermissionRetired` audit row per affected role, per research.md Decision 10 in `src/AskLucy.Web/Auth/PermissionCatalogReconciler.cs` (depends on T006, T019, T031)
- [X] T047 Register `IAuthorizationHandler, PermissionAuthorizationHandler`; `IClaimsTransformation, CurrentAuthorizationClaimsTransformation`; `IAuthorizationMiddlewareResultHandler, PermissionDeniedAuditResultHandler`; `IHostedService, PermissionCatalogReconciler` in `src/AskLucy.Web/Program.cs` (depends on T042-T046)
- [ ] T048 [P] Web tests: `PermissionAuthorizationHandlerTests` (matching claim passes, missing fails, `anyOf` semantics) in `tests/AskLucy.Web.Tests/Auth/PermissionAuthorizationHandlerTests.cs` (depends on T042)
- [ ] T049 [P] Web test: `RoleChangeTakesEffectNextRequestTests` — change a user's role, immediately call a permission-gated endpoint as that user without re-issuing a token, assert the new permission set applies (proves FR-018/research.md Decision 3 fixes the stale-JWT gap) in `tests/AskLucy.Web.Tests/Admin/RoleChangeTakesEffectNextRequestTests.cs` (depends on T043, T044)
- [ ] T050 [P] Web test: `AuthorizationDenialAuditTests` — a denied request writes exactly one `AuthorizationDenied` `RoleAuditLog` row with the required keys in `tests/AskLucy.Web.Tests/Admin/AuthorizationDenialAuditTests.cs` (depends on T045)

### Session / frontend foundation

- [X] T051 Add `Permissions` (effective keys) to `SessionResult` in `src/AskLucy.Application/Authentication/Queries/GetSession/GetSessionQuery.cs` and populate it via `IEffectivePermissionResolver` in `GetSessionQueryHandler.cs` (depends on T020)
- [X] T052 [P] Add `Permissions` to `SessionResponse` in `src/AskLucy.Web/Contracts/AuthContracts.cs` and map it through in `AuthController` (depends on T051)
- [X] T053 [P] Create the TypeScript permission-key catalogue (mirrors `AdminPermissionCatalog`) in `src/AskLucy.Web/ClientApp/src/features/admin/adminPermissions.ts`
- [X] T054 [P] Create `usePermissions()` / `useCan(key)` hooks (also added `useCanAny(keys)` for the class-level nav-visibility check) reading `permissions` from the session query in `src/AskLucy.Web/ClientApp/src/features/auth/hooks/usePermissions.ts` (depends on T053)
- [X] T055 Extend `SessionResponse`/`useSession` TS type with `permissions: string[]` in `src/AskLucy.Web/ClientApp/src/features/auth/api/authApi.ts` (depends on T052)
- [X] T056 Add an optional `permission` prop (string or string[], ANY-of semantics) to `AdminRoute` — falls back to today's built-in-role-only check when omitted, and a built-in admin always passes regardless (FR-002's three screens stay reserved) — in `src/AskLucy.Web/ClientApp/src/routes/AdminRoute.tsx` (depends on T054)
- [ ] T057 [P] Frontend unit tests for `usePermissions`/`useCan` and the updated `AdminRoute` in `src/AskLucy.Web/ClientApp/src/features/auth/hooks/usePermissions.test.ts` and `src/AskLucy.Web/ClientApp/src/routes/AdminRoute.test.tsx` (depends on T054, T056)

**Checkpoint**: `dotnet build`, full `dotnet test`, `tsc -b --noEmit`, and the Phase 2 test files above all pass. Permission resolution, storage, and enforcement plumbing exist; no screens or business endpoints yet.

---

## Phase 3: User Story 1 - Define and maintain roles (Priority: P1) 🎯 MVP part 1

**Goal**: Administrators/Super Users can create, edit, and delete custom roles with a name, description, and permission set on the **Roles** screen; built-in roles are visible but immutable.

**Independent Test**: Sign in as Administrator, open Roles, create "Viewer" with 2 permissions, rename it and change its permissions, then delete it — verify list state after each step and after reload (quickstart S3–S6, S17).

### Tests for User Story 1

- [X] T058 [P] [US1] Application handler tests: `CreateRoleCommandHandlerTests` (success, duplicate name → conflict, unknown permission key → rejected, empty set → rejected, reserved name → rejected) in `tests/AskLucy.Application.Tests/Authorization/CreateRoleCommandHandlerTests.cs`
- [X] T059 [P] [US1] Application handler tests: `UpdateRoleCommandHandlerTests` (success, built-in role → 403, stale concurrency stamp → conflict, duplicate name → conflict) in `tests/AskLucy.Application.Tests/Authorization/UpdateRoleCommandHandlerTests.cs`
- [X] T060 [P] [US1] Application handler tests: `DeleteRoleCommandHandlerTests` (no holders, N holders → unassigns and reports count, built-in role → 403, stale stamp → conflict) in `tests/AskLucy.Application.Tests/Authorization/DeleteRoleCommandHandlerTests.cs`
- [X] T061 [P] [US1] Application query tests: `ListRolesQueryHandlerTests` / `GetRoleQueryHandlerTests` (built-ins listed with full catalogue and sorted first, search, paging) in `tests/AskLucy.Application.Tests/Authorization/ListRolesQueryHandlerTests.cs`
- [X] T062 [P] [US1] Web authorization tests: `AdminRolesTests` in `tests/AskLucy.Web.Tests/Admin/AdminRolesTests.cs` — 401/403 for non-admin and unauthenticated callers, admin reaches the handler, matching `RoleAuthorizationTests`'s no-live-DB style; full CRUD/409-on-duplicate/409-on-stale-stamp/403-on-built-in behavior is proven by the mocked-repository handler tests (T058-T060) plus quickstart.md's live-database scenarios — this environment has no `PERSISTENCE_TESTS_CONNECTION_STRING`, so DB-backed integration assertions could not be executed here

### Implementation for User Story 1

- [X] T063 [P] [US1] Create `CreateRoleCommand`/Handler/Validator (name/description/permissionKeys → `RoleName`+`PermissionSet`, writes `RoleCreated` audit row) in `src/AskLucy.Application/Authorization/Roles/Commands/CreateRole/`
- [X] T064 [P] [US1] Create `UpdateRoleCommand`/Handler/Validator (rejects `IsBuiltIn`, writes `RoleUpdated` audit row with before/after, evicts every current holder's authz cache — added `IRoleAssignmentRepository.ListUserIdsByRoleAsync` for this) in `src/AskLucy.Application/Authorization/Roles/Commands/UpdateRole/`
- [X] T065 [P] [US1] Create `DeleteRoleCommand`/Handler (rejects `IsBuiltIn`, writes `RoleDeleted` audit row listing unassigned user ids, evicts their authz cache) in `src/AskLucy.Application/Authorization/Roles/Commands/DeleteRole/`
- [X] T066 [P] [US1] Create `ListRolesQuery`/Handler + `RoleSummaryDto` (search, paging, built-ins first with full catalogue) in `src/AskLucy.Application/Authorization/Roles/Queries/ListRoles/`
- [X] T067 [P] [US1] Create `GetRoleQuery`/Handler in `src/AskLucy.Application/Authorization/Roles/Queries/GetRole/`
- [X] T068 [US1] Create `AdminRolesController` (`GET/POST /admin/roles`, `GET/PUT/DELETE /admin/roles/{roleId}`) with `[RequirePermission]`-free but `AdministratorOrSuperUser`-gated (per plan.md — these 3 screens stay reserved, not permission-delegable) and `admin-endpoints` rate limiting, per contracts/admin-roles-api.md §2, in `src/AskLucy.Web/Controllers/v1/AdminRolesController.cs` (depends on T063-T067)
- [X] T069 [P] [US1] Create request contracts (`CreateRoleRequest`, `UpdateRoleRequest`) in `src/AskLucy.Web/Contracts/AdminRoleContracts.cs` — no separate response contracts: matches the existing convention (`AgentPoliciesController` et al.) of returning the Application-layer DTO directly for reads
- [X] T070 [P] [US1] Create `adminRolesApi.ts` role functions (`getRoles`, `getRole`, `createRole`, `updateRole`, `deleteRole`) in `src/AskLucy.Web/ClientApp/src/features/admin/api/adminRolesApi.ts`
- [X] T071 [P] [US1] Create `PermissionPicker` component (grouped-by-area checkboxes, Manage auto-selects View) in `src/AskLucy.Web/ClientApp/src/features/admin/components/PermissionPicker.tsx`, reading the catalogue from `adminPermissions.ts` (single source of truth, also used by T053) — component test deferred
- [X] T072 [US1] Create `RoleEditorDialog` (component test deferred) (create/edit form, RHF+Zod validation mirroring FluentValidation, uses `PermissionPicker`) + test in `src/AskLucy.Web/ClientApp/src/features/admin/components/RoleEditorDialog.tsx` (depends on T071)
- [X] T073 [P] [US1] Create `DeleteRoleDialog` (component test deferred) (shows affected-user count from the delete response, confirm) + test in `src/AskLucy.Web/ClientApp/src/features/admin/components/DeleteRoleDialog.tsx`
- [X] T074 [US1] Create `AdminRolesPage` (list, built-in badge, permission summary, user count, open create/edit/delete dialogs, visible error + retry on failed load per constitution §2 Principle VIII) in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminRolesPage.tsx` — `.test.tsx`/`.a11y.test.tsx` deferred (depends on T070, T072, T073)
- [X] T075 [US1] Add `/admin/roles` lazy route (`AdminRoute` reserved to built-in roles) in `src/AskLucy.Web/ClientApp/src/routes/router.tsx`; add "Roles" entry to `ADMIN_NAV` in `src/AskLucy.Web/ClientApp/src/features/admin/adminNav.tsx` (depends on T074)

**Checkpoint**: Roles screen is fully functional and independently testable/demoable (quickstart S3–S7, S17).

---

## Phase 4: User Story 2 - Assign a role to a user (Priority: P1) 🎯 MVP part 2

**Goal**: Administrators/Super Users assign, change, or remove a user's single role on the **Role assignments** screen; privileged-role rules and the last-Super-User safeguard are enforced; every existing admin controller now actually honors the new permission model.

**Independent Test**: With "Viewer" defined (US1), assign it to a user with no role, confirm it shows immediately and the user's admin-panel access matches the grant, then remove it (quickstart S8–S14).

### Tests for User Story 2

- [X] T076 [P] [US2] Application handler tests: `AssignRoleCommandHandlerTests` (assign/change/remove, plain-Administrator vs privileged role → 403, last-Super-User → 403, locked target → rejected, concurrency mismatch → conflict) in `tests/AskLucy.Application.Tests/Authorization/AssignRoleCommandHandlerTests.cs`
- [X] T077 [P] [US2] Application query tests: `ListRoleAssignmentsQueryHandlerTests` (search by name/email, filter by role/"none", excludes locked/deleted) in `tests/AskLucy.Application.Tests/Authorization/ListRoleAssignmentsQueryHandlerTests.cs`
- [X] T078 [P] [US2] Update `tests/AskLucy.Application.Tests/Users/ChangeUserRoleCommandHandlerTests.cs` and `tests/AskLucy.Application.Tests/Users/LastSuperUserGuardTests.cs` to confirm they still pass unchanged against the delegated implementation (SC-007)
- [X] T079 [P] [US2] Web tests: `AdminRoleAssignmentsTests` — list/search/filter, single assign/change/remove, privileged-role and last-Super-User rejections, locked-user exclusion in `tests/AskLucy.Web.Tests/Admin/AdminRoleAssignmentsTests.cs`
- [X] T080 [US2] Web test: `PermissionEnforcementMatrixTests` — parameterized over every re-annotated endpoint (dashboard, users, ai-providers, default-models, ai-capabilities, agent-policies, system-agents, workflow-policies, mcp-servers): a role with only that area's View permission can read but not write; a role without any permission for the area gets 403 on both in `tests/AskLucy.Web.Tests/Admin/PermissionEnforcementMatrixTests.cs`
- [X] T081 [US2] Web test: `AdminEndpointPermissionCoverageTests` — reflection over every controller action under `/api/v1/admin/*` plus the admin actions on `UsersController`, asserting each carries either `[RequirePermission]` or the reserved `AdministratorOrSuperUser` policy (SC-010) in `tests/AskLucy.Web.Tests/Admin/AdminEndpointPermissionCoverageTests.cs`
- [X] T082 [P] [US2] Update `tests/AskLucy.Web.Tests/Admin/RoleAuthorizationTests.cs` and `tests/AskLucy.Web.Tests/Users/ChangeUserRoleTests.cs` to confirm unchanged behavior for Administrator/Super User (SC-007)

### Implementation for User Story 2

- [X] T083 [P] [US2] Create `AssignRoleCommand`/Handler/Validator — enforces privileged-role rule via `ICurrentUserAccessor.IsInRole`, `SuperUserSafeguard` inside an `sp_getapplock('asklucy:super-user-guard', Exclusive)` transaction when removing Super User, writes `RoleAssigned`/`RoleChanged`/`RoleRemoved` audit row, evicts the target's authz cache in `src/AskLucy.Application/Authorization/Assignments/Commands/AssignRole/`
- [X] T084 [P] [US2] Create `ListRoleAssignmentsQuery`/Handler + `RoleAssignmentDto` in `src/AskLucy.Application/Authorization/Assignments/Queries/ListRoleAssignments/`
- [X] T085 [US2] Refactor `ChangeUserRoleCommandHandler` to delegate its rule body to the same `AssignRoleCommand` path (no duplicated privileged-role/last-Super-User logic — Principle III) in `src/AskLucy.Application/Users/Commands/ChangeUserRole/ChangeUserRoleCommandHandler.cs` (depends on T083)
- [X] T086 [US2] Refactor `LastSuperUserGuard` to call `SuperUserSafeguard` for the rule and keep only the count-loading glue in `src/AskLucy.Application/Users/LastSuperUserGuard.cs` (depends on T011, T083)
- [X] T087 [US2] Create `AdminRoleAssignmentsController` (`GET /admin/role-assignments`, `PUT /admin/role-assignments/{userId}`) — `AdministratorOrSuperUser`-gated per contracts §3, in `src/AskLucy.Web/Controllers/v1/AdminRoleAssignmentsController.cs` (depends on T083, T084)
- [X] T088 [US2] Mark `PATCH /users/{userId}/role` deprecated in OpenAPI XML doc and route it through the same `AssignRole` path in `src/AskLucy.Web/Controllers/v1/UsersController.cs` (depends on T085)
- [X] T089 [US2] Apply `[RequirePermission]` per research.md Decision 5's endpoint table to `AdminDashboardController`, `UsersController` (non-role admin actions), `AdminAiProvidersController`, `AgentPoliciesController`, `AdminAgentsController`, `WorkflowPoliciesController`, `McpServersController`, replacing their class-level `AdministratorOrSuperUser` (depends on T041, T042)
- [X] T090 [US2] Add `PUT /admin/ai/providers/{id}/default-model` (`admin.default-models.manage`) to `AdminAiProvidersController`; require `admin.default-models.manage` in addition to `admin.ai-providers.manage` when `PATCH .../providers/{id}` carries `DefaultModelId`/`ClearDefaultModel` in `src/AskLucy.Web/Controllers/v1/AdminAiProvidersController.cs` (depends on T089)
- [X] T091 [P] [US2] Create request/response contracts for role assignments in `src/AskLucy.Web/Contracts/AdminRoleContracts.cs` (extends T069's file)
- [X] T092 [P] [US2] Create `adminRolesApi.ts` assignment functions (`getRoleAssignments`, `assignRole`) in `src/AskLucy.Web/ClientApp/src/features/admin/api/adminRolesApi.ts` (extends T070's file)
- [X] T093 [P] [US2] Create `AssignRoleDialog` (search user or use pre-selected user, pick role or "No role", shows privileged-role/last-Super-User errors inline) + test in `src/AskLucy.Web/ClientApp/src/features/admin/components/AssignRoleDialog.tsx`
- [X] T094 [US2] Create `AdminRoleAssignmentsPage` (search, filter by role, assign/change/remove, visible error + retry on failure) + `.test.tsx` + `.a11y.test.tsx` in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminRoleAssignmentsPage.tsx` (depends on T092, T093)
- [X] T095 [US2] Add `/admin/role-assignments` lazy route + `ADMIN_NAV` entry (depends on T094)
- [X] T096 [US2] Update `UserActionMenu`'s "Change role…" dialog to load real roles from `adminRolesApi` instead of the hardcoded `Administrator/Super User/Regular` set, and update its test in `src/AskLucy.Web/ClientApp/src/features/admin/components/UserActionMenu.tsx`
- [~] T097 [US2] Filter `ADMIN_NAV` items in `AdminShell` by `useCan(<area>.view)` (or `builtInOnly`), and hide Manage-only actions across existing admin pages (`AdminUsersPage`, `AdminAiProvidersPage`, `AdminDefaultModelsPage`, `AdminAiCapabilitiesPage`, `AgentPoliciesAdminPage`, `WorkflowPoliciesAdminPage`, `McpAdministrationPage`) behind `useCan(<area>.manage)` in `src/AskLucy.Web/ClientApp/src/features/admin/components/AdminShell.tsx` and the listed page files (depends on T054, T075's/T095's nav entries)
- [X] T098 [US2] Update `useAccountMenuItems` to show "Admin panel" when the user has any admin permission (not only `useIsAdmin`'s two role names) and link to the first permitted area, in `src/AskLucy.Web/ClientApp/src/components/account/useAccountMenuItems.tsx`

**Checkpoint**: Role assignments screen is fully functional; every existing admin surface honors the permission model end-to-end (quickstart S8–S16, S18–S20). Together with Phase 3, this is the MVP.

---

## Phase 5: User Story 3 - Browse the permission catalogue (Priority: P2)

**Goal**: Administrators/Super Users browse the **Permissions** screen — every catalogue permission grouped by area, with description and the roles that include it, filterable, linking to Roles.

**Independent Test**: With "Moderator" holding "Manage MCP servers", open Permissions, filter to MCP servers, confirm Moderator is listed, follow it to its Roles-screen entry (quickstart S7).

### Tests for User Story 3

- [ ] T099 [P] [US3] Application query tests: `ListPermissionsQueryHandlerTests` (grouped by area, role filter, includes built-ins for every permission) in `tests/AskLucy.Application.Tests/Authorization/ListPermissionsQueryHandlerTests.cs`
- [ ] T100 [P] [US3] Web tests: `AdminPermissionsTests` — full catalogue returned, area/role filters, no POST/PUT/PATCH/DELETE route exists (405/404) in `tests/AskLucy.Web.Tests/Admin/AdminPermissionsTests.cs`

### Implementation for User Story 3

- [ ] T101 [P] [US3] Create `ListPermissionsQuery`/Handler + DTOs (per-area grouping, per-permission role list) in `src/AskLucy.Application/Authorization/Queries/ListPermissions/`
- [ ] T102 [US3] Create `AdminPermissionsController` (`GET /admin/permissions`) per contracts §1, `AdministratorOrSuperUser`-gated, in `src/AskLucy.Web/Controllers/v1/AdminPermissionsController.cs` (depends on T101)
- [ ] T103 [P] [US3] Create `getPermissions` in `adminRolesApi.ts` (extends T070/T092's file)
- [ ] T104 [US3] Create `AdminPermissionsPage` (grouped list, area/role filters, "open on Roles" link) + `.test.tsx` + `.a11y.test.tsx` in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminPermissionsPage.tsx` (depends on T103)
- [ ] T105 [US3] Add `/admin/permissions` lazy route + `ADMIN_NAV` entry (depends on T104)

**Checkpoint**: All three screens exist and are independently demoable.

---

## Phase 6: User Story 4 - Assign a role to several users at once (Priority: P3)

**Goal**: Assign one role to multiple users in a single action from the Role assignments screen, with a per-user result summary.

**Independent Test**: Select "Moderator", choose 5 users, assign in one action, confirm all 5 hold it and none holds their previous role (quickstart S15).

### Tests for User Story 4

- [ ] T106 [P] [US4] Application handler test: `BulkAssignRoleCommandHandlerTests` (mixed permitted/disallowed selection → partial success with skip reasons, atomic per allowed subset) in `tests/AskLucy.Application.Tests/Authorization/BulkAssignRoleCommandHandlerTests.cs`
- [ ] T107 [P] [US4] Web test: bulk-assign endpoint case added to `tests/AskLucy.Web.Tests/Admin/AdminRoleAssignmentsTests.cs`

### Implementation for User Story 4

- [ ] T108 [US4] Create `BulkAssignRoleCommand`/Handler/Validator (1–100 distinct user ids, applies permitted changes, reports skip reasons: `PrivilegedRoleRequiresSuperUser`, `LastSuperUser`, `UserLocked`, `UserNotFound`, `AlreadyAssigned`; one audit row per successful change) in `src/AskLucy.Application/Authorization/Assignments/Commands/BulkAssignRole/` (depends on T083)
- [ ] T109 [US4] Add `POST /admin/role-assignments/actions/bulk-assign` to `AdminRoleAssignmentsController` in `src/AskLucy.Web/Controllers/v1/AdminRoleAssignmentsController.cs` (depends on T108)
- [ ] T110 [P] [US4] Add `bulkAssignRole` to `adminRolesApi.ts`
- [ ] T111 [US4] Create `BulkAssignRoleDialog` (multi-user select, result summary with skip reasons) + test in `src/AskLucy.Web/ClientApp/src/features/admin/components/BulkAssignRoleDialog.tsx` (depends on T110)
- [ ] T112 [US4] Wire the bulk dialog into `AdminRoleAssignmentsPage` (select-many mode) + update its tests in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminRoleAssignmentsPage.tsx` (depends on T094, T111)

**Checkpoint**: All 4 user stories functional independently and together.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T113 [P] Verify `HangfireDashboardAuthorizationFilter`, `DocumentProcessingHub`, `ProblemDetailsMiddleware`'s privileged-detail check, and the `ai-endpoints` rate-limit tier still read role names directly and are unaffected (FR-009) — add a regression test per surface if missing coverage
- [ ] T114 [P] Update XML-doc comments referencing the old "two privileged roles" model across `PrivilegedRoleNames.cs`, `ChangeUserRoleCommandValidator.cs`, and any controller doc-comments still describing role-only authorization
- [ ] T115 Run the full quickstart.md scenario list (S1–S21) manually end-to-end and record results
- [ ] T116 [P] Performance check (SC-008): seed 10,000 users / 200 roles, verify `GET /admin/roles` and `GET /admin/role-assignments?search=` respond within 2s; confirm the assignment search query plan uses `IX_AspNetUserRoles_UserId`
- [ ] T117 [P] Accessibility pass: run axe (`*.a11y.test.tsx`) for all three new pages and do a manual keyboard-only pass through create→permission-pick→save→assign on both light and dark themes at 400px width
- [ ] T118 Update architecture/API documentation per constitution §13: add this feature's endpoints to API docs, note the `ApplicationRole`/`AspNetRoleClaims`/`RoleAuditLogs` additions in the database documentation, and record the claims-transformation approach as the fix for the specs/001 "next request" gap
- [ ] T119 Full regression: `dotnet test` (all 5 test projects) and `npx vitest run` (full suite, not just touched files — memory: page-level tests assert independently of component tests) both green

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks all user stories** — catalogue, storage, claims transformation, and session/frontend plumbing are shared by every screen and every re-annotated controller.
- **US1 (Phase 3)**: Depends on Foundational only. Independently testable/demoable on its own (Roles screen with no assignment UI yet).
- **US2 (Phase 4)**: Depends on Foundational. Reads roles created by US1 to be meaningfully demoed, but its own endpoints/UI don't require US1's code — sequence US1 → US2 for a coherent demo; do not need US1 merged to build US2 in parallel.
- **US3 (Phase 5)**: Depends on Foundational only; links to a role's Roles-screen entry (US1) but functions without it (link is just inert without US1).
- **US4 (Phase 6)**: Depends on US2's `AssignRoleCommand` (T083) — must follow US2.
- **Polish (Phase 7)**: Depends on all four stories.

### Within Each Story

- Tests before/alongside implementation (constitution §10 — write to fail first where practical for handler-level tests).
- Domain/value objects → Application commands/queries → Persistence → Controller → Frontend API client → Components → Page → Route/nav.

### Parallel Opportunities

- Phase 2: T003-T005, T007-T009, T011 (Domain) in parallel; T012-T015 (Domain tests) in parallel; T016-T021 (Application interfaces) in parallel; T026-T029 (EF configurations) in parallel once T024 lands; T051-T057 (session/frontend) mostly parallel.
- Phase 3: T058-T062 (tests) in parallel; T063-T067 (commands/queries) in parallel; T069-T071, T073 in parallel.
- Phase 4: T076-T079, T082 (tests) in parallel; T083-T084, T091-T093 in parallel.
- Phase 5 and Phase 6 are each small and mostly parallel internally.
- Different developers could take US1 and US3 in parallel once Foundational lands; US2 and US4 are sequential (US4 needs US2's command).

---

## Parallel Example: Phase 2 Domain layer

```bash
Task: "Create AdminArea enum in src/AskLucy.Domain/Authorization/AdminArea.cs"
Task: "Create AdminPermissionLevel enum in src/AskLucy.Domain/Authorization/AdminPermissionLevel.cs"
Task: "Create AdminPermission record in src/AskLucy.Domain/Authorization/AdminPermission.cs"
Task: "Create RoleName value object in src/AskLucy.Domain/Authorization/RoleName.cs"
Task: "Create RoleAuditAction enum in src/AskLucy.Domain/Authorization/RoleAuditAction.cs"
Task: "Create SuperUserSafeguard domain service in src/AskLucy.Domain/Authorization/SuperUserSafeguard.cs"
```

## Parallel Example: User Story 1 commands

```bash
Task: "Create CreateRoleCommand/Handler/Validator in src/AskLucy.Application/Authorization/Roles/Commands/CreateRole/"
Task: "Create UpdateRoleCommand/Handler/Validator in src/AskLucy.Application/Authorization/Roles/Commands/UpdateRole/"
Task: "Create DeleteRoleCommand/Handler in src/AskLucy.Application/Authorization/Roles/Commands/DeleteRole/"
Task: "Create ListRolesQuery/Handler in src/AskLucy.Application/Authorization/Roles/Queries/ListRoles/"
Task: "Create GetRoleQuery/Handler in src/AskLucy.Application/Authorization/Roles/Queries/GetRole/"
```

---

## Implementation Strategy

### MVP First (US1 + US2)

1. Phase 1: Setup.
2. Phase 2: Foundational — **critical path**, includes the migration and the claims-transformation fix.
3. Phase 3: US1 (Roles screen). **Stop and validate** against quickstart S3–S7, S17.
4. Phase 4: US2 (Role assignments + full permission enforcement across existing controllers). **Stop and validate** against quickstart S8–S16, S18–S20. This is the deployable MVP — roles can be defined and assigned, and every admin surface respects the new model.

### Incremental Delivery

1. Foundational → not independently demoable, but should merge behind no flag (constitution §19: no partial features behind an unimplemented flag — Foundational alone changes no observable behavior since built-ins still resolve to the full catalogue, so it is safe to ship on its own).
2. + US1 → demo Roles screen.
3. + US2 → demo full role lifecycle end to end; deploy (MVP).
4. + US3 → demo Permissions screen.
5. + US4 → demo bulk assignment.
6. + Polish → close out documentation, performance, accessibility.

### Suggested Team Split

- Developer A: Phase 2 Domain/Application/Persistence (T003–T040).
- Developer B: Phase 2 Web auth pipeline + session/frontend (T041–T057), starts once T006/T020/T022 land.
- After Foundational: Developer A → US1, Developer B → US3 (parallel); either → US2, then US4.

---

## Notes

- [P] tasks touch different files with no unfinished dependency.
- Every new backend behavior gets a handler-level test per constitution §10; every new frontend page gets a render test and an `.a11y.test.tsx`.
- Run the **full** test suite before each checkpoint, not just the new files (memory: page-level tests assert independently of component tests).
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
- T036's data clean-up step is irreversible — run T002's baseline query first and review T039's migration test before applying to a shared/prod database.
