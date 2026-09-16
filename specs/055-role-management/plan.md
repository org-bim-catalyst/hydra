# Implementation Plan: Role Definition & Role Assignment Screens

**Branch**: `055-role-management` | **Date**: 2026-09-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/055-role-management/spec.md`

## Summary

Turn roles from two hard-coded names into manageable application-administration roles backed by a code-defined **permission catalogue** (16 View/Manage permissions over the 9 admin-panel areas), and add three admin screens: **Permissions** (browse catalogue + role coverage), **Roles** (CRUD + attach permissions), **Role assignments** (one role per user, single and bulk).

Technical approach ([research.md](./research.md)):

- Keep ASP.NET Identity as the role store — `ApplicationRole : IdentityRole` gains description/built-in/audit columns; new `RolePermissions` table; catalogue lives in Domain (*D1*). Built-in roles compute the full catalogue rather than storing rows (*D2*).
- A claims transformation replaces the JWT's stale role claims with the user's **current** role + `permission` claims on every request (cached, explicitly evicted) — this also fixes a latent specs/001 gap where role changes lagged by up to the 15-minute token lifetime (*D3*).
- Admin endpoints move from `AdministratorOrSuperUser` to `[RequirePermission]` attributes per area; the three new screens and role assignment stay reserved to built-in roles (*D4*, *D5*).
- One-role-per-user enforced by a unique index after de-duplicating existing data (*D7*); assignments commit atomically with an append-only `RoleAuditLog` (*D8*, *D9*).
- Frontend: session exposes `permissions`; nav, routes and page actions become permission-aware (*D11*).

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend); TypeScript 5 / React 19 (frontend) — existing stack.

**Primary Dependencies**: ASP.NET Core Identity + authorization (`IAuthorizationRequirementData`, `IClaimsTransformation`, `IAuthorizationMiddlewareResultHandler`), MediatR, FluentValidation, EF Core (SQL Server), `IMemoryCache`; MUI, TanStack Query, React Hook Form + Zod, React Router. No new packages.

**Storage**: SQL Server — extends `AspNetRoles`; role permissions stored as `AspNetRoleClaims` rows (`ClaimType = "permission"`), not a new table; new `RoleAuditLogs`; unique indexes on `AspNetUserRoles.UserId` and `AspNetRoleClaims(RoleId, ClaimType, ClaimValue)`. One migration, `AddRoleManagement` ([data-model.md](./data-model.md)).

**Testing**: xUnit + NSubstitute (Domain value objects, Application handlers), Persistence tests against SQL Server (repositories, migration de-dup, unique index), Web.Tests `WebApplicationFactory` (authorization matrix per permission, reflection coverage test, contracts); Vitest + RTL + axe (pages, nav filtering, a11y). Watch: `[LoggerMessage]` calls can't be asserted with NSubstitute `Received().Log`; use `tsc -b --noEmit`; Web.Tests need `PERSISTENCE_TESTS_CONNECTION_STRING`.

**Target Platform**: Existing ASP.NET Core Web API + React SPA on site4now.net (single instance).

**Project Type**: Web application (`Domain` / `Application` / `Infrastructure` / `Persistence` / `Web` + `ClientApp`).

**Performance Goals**: Lists and search < 2 s at 10,000 users / 200 roles (SC-008); per-request authorization resolution served from cache, ≤ 1 indexed query on miss.

**Constraints**: Existing Administrator/Super User capabilities unchanged (FR-009, SC-007); role changes effective on next request (FR-018); no contract breaks in `/api/v1`; server-side enforcement on every endpoint; no silent failures in UI.

**Scale/Scope**: 3 new controllers (~9 endpoints) + 1 new AI endpoint; 8 existing admin controllers re-annotated; ~10 commands/queries; 1 migration; 3 new pages + ~5 components; permission-aware updates to `AdminRoute`, `AdminShell`/`ADMIN_NAV`, account menu, and mutating actions on 9 existing admin pages.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|---|---|---|
| **I / §3 Dependency Rule** | PASS | Catalogue, `PermissionSet`, `RoleName`, `RoleAuditLog`, `SuperUserSafeguard` in Domain (no deps). Handlers + `IRoleRepository`, `IRoleAssignmentRepository`, `IRoleAuditLogRepository`, `IEffectivePermissionResolver` interfaces in Application. EF/Identity implementations in Persistence. Claims transformation, authorization handler/attribute in Web. |
| **§3 Domain purity** | PASS | `ApplicationRole` (Identity-derived) stays in Persistence, same precedent as `ApplicationUser`; Domain rules are value objects the handlers apply. Permission grants reuse `AspNetRoleClaims` (Identity infrastructure, Persistence-only) — Application never sees `IdentityRoleClaim<string>`, only `PermissionSet` returned by `IRoleRepository`. |
| **§3 CQRS** | PASS | Queries: `ListPermissions`, `ListRoles`, `GetRole`, `ListRoleAssignments`. Commands: `CreateRole`, `UpdateRole`, `DeleteRole`, `AssignRole`, `BulkAssignRole` — each returns only id/stamp/result summary. Existing `ChangeUserRoleCommand` delegates to `AssignRole` logic (no duplication). |
| **§3 Repository / UoW** | PASS | Aggregate-oriented methods; assignment replace + audit row in one `SaveChanges` (*D8*). |
| **§4 Error handling / Principle VIII** | PASS | Rule violations → domain/`UnauthorizedAccessException` → Problem Details 403; concurrency → 409 via existing middleware; every frontend query/mutation has visible error + retry. |
| **§5 Concurrency** | PASS | `ConcurrencyStamp` on roles; `expectedCurrentRoleId` on assignments; `sp_getapplock` around Super-User-removing changes. |
| **§5 Soft delete / auditing** | PASS (justified) | Roles are hard-deleted: they are configuration, not user content with retention needs; history preserved in `RoleAuditLogs` (spec Assumptions). Audit columns set explicitly on `ApplicationRole` (can't inherit `BaseEntity`). |
| **§5 Migrations** | PASS (documented) | Reversible except the duplicate-role clean-up, documented as irreversible with audit rows as record (data-model.md). |
| **§6 REST / versioning / Problem Details / pagination / OpenAPI** | PASS | Nouns under `/api/v1/admin/…`; bulk as `/actions/bulk-assign`; offset pagination on roles and assignments; catalogue bounded (16) so unpaginated; additive changes only, deprecated fields flagged in OpenAPI. |
| **§6 AuthN/AuthZ** | PASS with note | Enforced server-side on every endpoint. Authorization stays at the controller edge (policy attributes) rather than Application authorization handlers — this matches how every existing admin surface is authorized (Principle VII); moving all admin authorization into MediatR behaviors is a cross-cutting change outside this feature. Business authorization rules (privileged roles, last Super User, built-in immutability) *are* in Application/Domain. |
| **§6 Rate limiting** | PASS | New controllers use `admin-endpoints`. |
| **§7 UI** | PASS | MUI + existing `AdminShell`; RHF + Zod mirroring FluentValidation; lazy routes; light/dark; keyboard; responsive; copy centralized. |
| **§8 Security** | PASS | No privilege escalation path: role/permission/assignment management is never delegable (FR-002); plain Administrators can't touch built-in roles; denials audited; 403 bodies don't disclose required permission. |
| **§10 Testing** | PASS | Unit, integration, authorization-matrix, reflection coverage, a11y tests planned; existing admin suites must pass unchanged. |
| **§15 Scalability (stateless instances)** | NOTE | Per-instance authorization cache → see Complexity Tracking. |
| **III YAGNI** | PASS | No personas, no admin-defined permissions, no role hierarchy, no audit-log UI. |

**Post-Phase-1 re-check**: data model, contracts and quickstart introduce nothing beyond the gates above. The one open trade-off (per-instance cache) is recorded below. **Gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/055-role-management/
├── spec.md
├── plan.md                      # this file
├── research.md                  # Phase 0
├── data-model.md                # Phase 1
├── quickstart.md                # Phase 1
├── contracts/
│   └── admin-roles-api.md       # Phase 1
├── checklists/requirements.md
└── tasks.md                     # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/AskLucy.Domain/Authorization/
├── AdminArea.cs
├── AdminPermission.cs                 # record + AdminPermissionLevel
├── AdminPermissionCatalog.cs          # the 16 permissions
├── PermissionSet.cs                   # value object (normalize Manage⇒View, non-empty, known keys)
├── RoleName.cs                        # value object (length, reserved names)
├── RoleAuditLog.cs                    # append-only entity + RoleAuditAction
└── SuperUserSafeguard.cs

src/AskLucy.Application/
├── Abstractions/
│   ├── IRoleRepository.cs
│   ├── IRoleAssignmentRepository.cs
│   ├── IRoleAuditLogRepository.cs
│   ├── IEffectivePermissionResolver.cs
│   └── IAuthorizationCacheInvalidator.cs
├── Authorization/
│   ├── EffectivePermissionResolver.cs          # built-in ⇒ full catalogue; custom ⇒ stored ∩ catalogue
│   ├── Queries/ListPermissions/…
│   ├── Roles/Queries/{ListRoles,GetRole}/…
│   ├── Roles/Commands/{CreateRole,UpdateRole,DeleteRole}/…   # command, handler, validator
│   └── Assignments/
│       ├── Queries/ListRoleAssignments/…
│       └── Commands/{AssignRole,BulkAssignRole}/…
├── Authentication/Queries/GetSession/…          # + Permissions on SessionResult
└── Users/
    ├── LastSuperUserGuard.cs                    # delegates rule to Domain SuperUserSafeguard
    ├── PrivilegedRoleNames.cs                   # unchanged names
    └── Commands/ChangeUserRole/…                # delegates to AssignRole logic

src/AskLucy.Persistence/
├── Identity/ApplicationRole.cs
├── Identity/IdentityService.cs                  # role lookups by ApplicationRole
├── AskLucyDbContext.cs                          # IdentityDbContext<ApplicationUser, ApplicationRole, string>
├── DependencyInjection.cs                       # AddRoles<ApplicationRole>() + new repositories
├── Configurations/{ApplicationRoleConfiguration,RoleAuditLogConfiguration,UserRoleConfiguration,RoleClaimConfiguration}.cs   # RoleClaimConfiguration adds the unique index
├── Repositories/{RoleRepository,RoleAssignmentRepository,RoleAuditLogRepository}.cs
├── Repositories/UserAdminRepository.cs          # role column: any role, not only privileged
└── Migrations/<timestamp>_AddRoleManagement.cs

src/AskLucy.Web/
├── Auth/
│   ├── RequirePermissionAttribute.cs            # AuthorizeAttribute + IAuthorizationRequirementData
│   ├── PermissionRequirement.cs / PermissionAuthorizationHandler.cs
│   ├── CurrentAuthorizationClaimsTransformation.cs
│   ├── PermissionDeniedAuditResultHandler.cs    # IAuthorizationMiddlewareResultHandler
│   └── PermissionCatalogReconciler.cs           # IHostedService (retired keys)
├── Controllers/v1/
│   ├── AdminPermissionsController.cs            # new
│   ├── AdminRolesController.cs                  # new
│   ├── AdminRoleAssignmentsController.cs        # new
│   ├── AdminAiProvidersController.cs            # + PUT providers/{id}/default-model; permission attributes
│   └── {AdminDashboard,Users,AgentPolicies,AdminAgents,WorkflowPolicies,McpServers}Controller.cs  # permission attributes
├── Contracts/{AdminRoleContracts,AuthContracts}.cs
├── DevSeed/DevAdminSeeder.cs                    # Super User only
└── Program.cs                                   # register handler, transformation, result handler, reconciler

src/AskLucy.Web/ClientApp/src/
├── features/auth/hooks/usePermissions.ts        # usePermissions(), useCan(key)
├── features/admin/
│   ├── api/adminRolesApi.ts
│   ├── adminPermissions.ts                      # catalogue keys (single TS source of truth)
│   ├── adminNav.tsx                             # + permission / builtInOnly per item
│   ├── components/AdminShell.tsx                # filter nav
│   ├── components/{RoleEditorDialog,PermissionPicker,DeleteRoleDialog,AssignRoleDialog,BulkAssignRoleDialog}.tsx (+ tests)
│   ├── components/UserActionMenu.tsx            # role dialog uses real roles
│   └── pages/{AdminPermissionsPage,AdminRolesPage,AdminRoleAssignmentsPage}.tsx (+ .test, .a11y.test)
├── features/admin/pages/*  + features/{agents,workflows,mcp} admin pages   # hide Manage actions via useCan
├── routes/AdminRoute.tsx                        # permission / builtInOnly props
├── routes/router.tsx                            # 3 lazy routes; per-route permissions
└── components/account/useAccountMenuItems.tsx   # "Admin panel" when any admin permission

tests/
├── AskLucy.Domain.Tests/Authorization/{PermissionSetTests,RoleNameTests,AdminPermissionCatalogTests,SuperUserSafeguardTests}.cs
├── AskLucy.Application.Tests/Authorization/{CreateRole,UpdateRole,DeleteRole,AssignRole,BulkAssignRole,ListPermissions,EffectivePermissionResolver}Tests.cs
├── AskLucy.Application.Tests/Users/{ChangeUserRoleCommandHandlerTests,LastSuperUserGuardTests}.cs   # updated
├── AskLucy.Persistence.Tests/Authorization/{RoleRepositoryTests,RoleAssignmentRepositoryTests,AddRoleManagementMigrationTests}.cs
└── AskLucy.Web.Tests/Admin/{AdminRolesTests,AdminRoleAssignmentsTests,AdminPermissionsTests,PermissionEnforcementMatrixTests,AdminEndpointPermissionCoverageTests,RoleChangeTakesEffectNextRequestTests}.cs
```

**Structure Decision**: Existing Clean Architecture web-application layout. The feature adds one new Domain/Application area (`Authorization`) and vertical slices per screen following the `/admin/*` pattern (specs/007, specs/047); cross-cutting authorization plumbing lives in `Web/Auth` next to the existing `HangfireDashboardAuthorizationFilter` and `HttpContextCurrentUserAccessor`.

## Complexity Tracking

| Violation / trade-off | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Per-instance `IMemoryCache` for effective permissions (§15 stateless scaling) | Meets FR-018 on the current single-instance host without a DB round-trip per request. Eviction is immediate on the instance that made the change; the 30 s absolute expiry bounds staleness on any other instance. | Uncached per-request DB lookup: extra query on every authenticated request on a slow shared host. Distributed cache: new infrastructure dependency (ADR-level) for a deployment that has one instance. **Revisit** (switch to `HybridCache` with distributed backplane) before scaling out — at that point FR-018 would degrade to "within 30 s" on other instances. |
| Authorization at controller edge instead of Application authorization handlers (§6 wording) | Consistent with all existing admin authorization (Principle VII); one reflection test proves full coverage. | MediatR permission behavior on ~40 existing requests — larger, riskier diff that would leave admin authorization split across two mechanisms mid-migration. |
| Hard delete of roles (§5 soft-delete default) | Roles are configuration; a soft-deleted role would still block its name (unique `NormalizedName`) and complicate the one-role constraint. | Soft delete + filtered unique index — adds state with no retention requirement; audit trail already preserves history. |
