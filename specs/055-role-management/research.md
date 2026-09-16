# Research: Role Definition, Role Assignment & Permission Catalogue

**Feature**: `055-role-management` | **Date**: 2026-09-14 | **Spec**: [spec.md](./spec.md)

Findings from the current codebase that constrain the design, then one decision per open question.

## Current-state findings

| # | Finding | Where | Consequence |
|---|---------|-------|-------------|
| F1 | Every admin endpoint is gated by one policy, `AdministratorOrSuperUser` = `RequireRole("Administrator", "Super User")`, applied at controller class level. | `Program.cs:152-153`, 8 admin controllers + `UsersController` admin actions | Permission enforcement must replace this per area without weakening built-in access. |
| F2 | Role claims are baked into the JWT at sign-in/refresh (`IdentityService.GetClaimsAsync`), access-token lifetime 15 min. | `IdentityService.cs:164`, `appsettings*.json` `AccessTokenLifetimeMinutes: 15` | A role change today takes effect only after the next refresh (≤15 min) — **specs/001's "next request" claim is not actually met**. FR-018 needs a fix, not a reuse. |
| F3 | Identity is registered with plain `IdentityRole`; `AspNetRoles` has only `Id/Name/NormalizedName/ConcurrencyStamp`. Built-in roles are **not** seeded by migration — only `DevAdminSeeder` creates them (dev). | `Persistence/DependencyInjection.cs:46`, `DevSeed/DevAdminSeeder.cs` | Migration must upsert the two built-in rows idempotently (prod already has them). |
| F4 | `AspNetUserRoles` allows many roles per user; `ChangeRoleAsync` only strips *privileged* roles. `DevAdminSeeder` gives the dev admin **both** Administrator and Super User. | `IdentityService.cs:336`, `DevAdminSeeder.cs:19` | One-role rule (FR-012) needs a data clean-up + DB constraint; seeder must change. |
| F5 | "Regular" is a sentinel string, never a real role row. | `PrivilegedRoleNames.cs` | Keep as "no role" (null `roleId` in the new API); reserve the name. |
| F6 | Built-in-only privileges outside the admin panel: Hangfire dashboard, `DocumentProcessingHub` org-wide group, org document dashboard (`DocumentProcessingController:47`), boundary diagnostics, `AiController:683`, AI rate-limit tier, Problem Details detail exposure. | `HangfireDashboardAuthorizationFilter`, `DocumentProcessingHub:32`, `Program.cs:163`, `ProblemDetailsMiddleware:420` | These stay on role names (FR-009), untouched. |
| F7 | The **Default models** and **AI capabilities** pages call the same `/api/v1/admin/ai/providers*` endpoints as **AI providers**; the default model is set via `PATCH providers/{id}` (`DefaultModelId`/`ClearDefaultModel`). | `adminAiProvidersApi.ts`, `AdminAiProvidersController.cs:37` | Area boundaries don't match endpoint boundaries — needs an explicit mapping (Decision 5). |
| F8 | Audit pattern for admin modules: an append-only Domain entity (`McpAuditLog : BaseEntity`, `Record(...)` factory, `DetailsJson`) + `I…AuditLogRepository.Add`, committed in the same unit of work. Users module only logs (`AdminActionLog`, accepted interim scope). | `Domain/Mcp/McpAuditLog.cs`, `Application/Users/AdminActionLog.cs` | New `RoleAuditLog` mirrors `McpAuditLog`. |
| F9 | `ProblemDetailsMiddleware` already maps `UnauthorizedAccessException`→403 and `DbUpdateConcurrencyException`→409. `IMemoryCache` is registered (Infrastructure). | `ProblemDetailsMiddleware.cs:158,317`, `Infrastructure/DependencyInjection.cs:209` | Reuse; no new error plumbing. |
| F10 | Frontend admin gating reads role names: `AdminRoute` (session `roles`), `useIsAdmin` (decoded JWT) → account-menu "Admin panel" link and org document dashboard. `ADMIN_NAV` is one list rendered by `AdminShell`. | `routes/AdminRoute.tsx`, `hooks/useIsAdmin.ts`, `features/admin/adminNav.tsx` | Admin-panel entry/nav become permission-driven; org document dashboard stays role-driven (F6). |

## Decision 1 — Where roles and permissions live

- **Decision**: Keep roles in ASP.NET Identity. Introduce `ApplicationRole : IdentityRole` (Persistence) with `Description`, `IsBuiltIn`, and audit columns. A role's permissions are stored as **`AspNetRoleClaims`** rows with `ClaimType = PermissionClaims.Type` (constant `"permission"`) and `ClaimValue` = a catalogue key, written/removed via `RoleManager<ApplicationRole>.AddClaimAsync`/`RemoveClaimAsync`. The **permission catalogue is still code**, in Domain (`AdminPermission` smart-enum + `AdminPermissionCatalog`), not a table — a claim's `ClaimValue` is validated against it on every write, exactly as it would be against a bespoke join table.
- **Rationale**: Identity already owns role names, normalization, `ConcurrencyStamp`, user-role links, *and* a role-claims table built for this — reusing it is Principle VII (convention over configuration) rather than adding a parallel `RolePermissions` table that would do the same job. A catalogue in code is what guarantees every permission is enforced (FR-027) — a stored permission nothing checks is a silent no-op, regardless of whether it's stored as a claim row or a join-table row.
- **Alternatives**: (a) Custom Domain `Role` aggregate + own tables, drop Identity roles — rejected, duplicates Identity and breaks `IsInRole` callers (F6). (b) New bespoke `RolePermissions` join table — rejected in favor of (revised): `AspNetRoleClaims` already exists and does the same job with one less table; the only extra cost is adding a unique index to it (Decision 1b) instead of designing one from scratch. (c) Permissions table seeded by migration — rejected: invites admin-editable rows and drifts from code.

### Decision 1b — Claim storage details

- `PermissionClaims.Type = "permission"` (Application constant, referenced by Persistence and Web).
- Migration adds unique index `IX_AspNetRoleClaims_RoleId_ClaimType_ClaimValue` on `AspNetRoleClaims(RoleId, ClaimType, ClaimValue)` — `AspNetRoleClaims` ships with no unique constraint by default, so this is required to prevent duplicate grants and to race-detect concurrent attaches the same way a table PK would.
- Reading a custom role's permissions: `dbContext.RoleClaims.Where(c => c.RoleId == roleId && c.ClaimType == PermissionClaims.Type).Select(c => c.ClaimValue)`. Reading "roles that include permission X" (Permissions screen, FR-030): filter by `ClaimValue == key` with the same index.
- Built-in roles still carry **no** claim rows — Decision 2 (computed, not stored) is unaffected by this storage choice.

## Decision 2 — Built-in role permissions are computed, not stored

- **Decision**: `Administrator` and `Super User` have **no** `RolePermissions` rows; effective permissions = full catalogue, computed at resolution time.
- **Rationale**: FR-008 requires built-ins to include future permissions automatically; storing rows would need a backfill every time the catalogue grows.
- **Alternatives**: Seed rows per catalogue change — rejected (easy to forget; a missed backfill silently locks admins out of a new area).

## Decision 3 — Effective permissions per request (fixes F2 / meets FR-018)

- **Decision**: An `IClaimsTransformation` (`CurrentAuthorizationClaimsTransformation`, Web/Auth) replaces the JWT's `role` claims with the user's **current** role and adds one `permission` claim per effective permission, via Application's `IEffectivePermissionResolver`. Results are cached in `IMemoryCache` per user (key `authz:{userId}`) with **explicit eviction** on every role change/assignment/deletion and a **30-second absolute expiry** safety net.
- **Rationale**: Every existing `IsInRole` caller (F6), the new permission handler, SignalR hubs, and Hangfire all read `HttpContext.User`, so one transformation makes all of them current without touching them. Caching avoids a DB round-trip per request on the shared host.
- **Alternatives**: (a) Shorten JWT lifetime — rejected, still not "next request" and increases refresh load. (b) Uncached DB lookup per request — rejected for the site4now IO profile (see memory: short-timeout/IO issues). (c) Distributed cache / version counter — deferred: deployment is single-instance today; see plan Complexity Tracking for the multi-instance caveat.

## Decision 4 — Enforcement mechanism: endpoint permission attributes

- **Decision**: `RequirePermissionAttribute(params string[] anyOf) : AuthorizeAttribute, IAuthorizationRequirementData` producing a `PermissionRequirement`, checked by `PermissionAuthorizationHandler` against `permission` claims. Controllers: class-level `[RequirePermission(<Area>.View)]`, mutating actions add `[RequirePermission(<Area>.Manage)]` (attributes AND together; Manage implies View so both pass). The three new controllers and `PATCH /users/{id}/role` keep `AdministratorOrSuperUser`.
- **Rationale**: Matches the established convention (policy at the controller edge, F1 — Principle VII); `IAuthorizationRequirementData` (.NET 8+) needs no custom policy provider. A reflection test can prove every admin action carries either a permission or the reserved policy (SC-010).
- **Alternatives**: MediatR `IPipelineBehavior` + `IRequirePermissions` on ~40 requests — closer to constitution §6's wording, but would diverge from how every other admin surface is authorized and double the change surface. Rejected for this feature; noted in plan Constitution Check.

## Decision 5 — Area ↔ endpoint mapping (resolves F7)

- **Decision**: Read endpoints shared by several pages accept *any of* the relevant View permissions; writes map to exactly one area:

| Endpoint(s) | Required |
|---|---|
| `GET admin/dashboard/summary` | `dashboard.view` |
| `GET users` | `users.view` |
| `PATCH users/{id}`, `actions/lock`, `actions/unlock`, `actions/force-2fa-reset`, `DELETE users/{id}` | `users.manage` |
| `PATCH users/{id}/role` | built-in only (reserved) |
| `GET admin/ai/providers`, `GET admin/ai/providers/{id}/models` | any of `ai-providers.view`, `default-models.view`, `ai-capabilities.view` |
| `PATCH admin/ai/providers/{id}` (`IsEnabled`), credential PUT/DELETE, `check-health`, `PATCH models/{id}`, model sync + apply | `ai-providers.manage` |
| **new** `PUT admin/ai/providers/{id}/default-model` | `default-models.manage` |
| `PATCH admin/ai/providers/{id}` carrying `DefaultModelId`/`ClearDefaultModel` | `ai-providers.manage` **and** `default-models.manage` (fields marked deprecated in OpenAPI; frontend moves to the new endpoint) |
| `GET admin/ai/capabilities` / `PUT admin/ai/capabilities/{c}` | `ai-capabilities.view` / `.manage` |
| `admin/agent-policies` GET / POST, PUT, DELETE, `user-limits` | `agent-policies.view` / `.manage` |
| `GET admin/agents/system` | `system-agents.view` |
| `admin/workflow-policies` GET / writes | `workflow-policies.view` / `.manage` |
| `admin/mcp/servers` GETs (incl. health, references, tools, audit-log) / writes & actions | `mcp-servers.view` / `.manage` |

- **Rationale**: No existing contract breaks (the PATCH still accepts the fields); the split gives Default models a real, independently-grantable write path.
- **Alternatives**: Merge AI providers + Default models into one area — rejected, contradicts the spec's area list.

## Decision 6 — View-only areas

- **Decision**: Dashboard and System agents expose only a *View* permission (they have no mutating endpoints). Catalogue = 16 permissions (7 areas × View+Manage + 2 View-only). Spec FR-004 updated accordingly.
- **Rationale**: A Manage permission that guards nothing would violate FR-027.

## Decision 7 — One role per user: constraint and migration

- **Decision**: Unique index `IX_AspNetUserRoles_UserId`. Migration first de-duplicates: for users holding >1 role keep `Super User` > `Administrator` > any other (lowest `RoleId` tiebreak), recording removals in `RoleAuditLogs` with actor `system:migration`. `DevAdminSeeder` assigns only `Super User` when bootstrapping.
- **Rationale**: FR-012 must hold even for writes that bypass the Application layer; a DB constraint is the only guarantee. Keeping the most privileged role never reduces access for the affected user (SC-007).
- **Alternatives**: Application-only enforcement — rejected (seeders, scripts, future code paths).

## Decision 8 — Assignment writes, concurrency, and the last-Super-User race

- **Decision**: `IRoleAssignmentRepository` (Persistence) replaces a user's `AspNetUserRoles` row and adds the audit row in **one `SaveChanges`** (not `UserManager.RemoveFromRole` + `AddToRole`, which are two commits), and bumps the user's `SecurityStamp`. Optimistic concurrency: the client sends `expectedCurrentRoleId`; a mismatch → 409. Role edits use `ApplicationRole.ConcurrencyStamp` (already an EF concurrency token) → `DbUpdateConcurrencyException` → 409 (F9). Any change that removes Super User from someone runs inside a transaction holding `sp_getapplock('asklucy:super-user-guard', Exclusive)` before counting active Super Users.
- **Rationale**: Constitution §5 (one commit per business transaction, concurrency tokens). Without the app lock, two Super Users demoting each other concurrently could both pass `CountActiveSuperUsersAsync` (FR-017/SC-006).
- **Alternatives**: Serializable isolation on the count query — rejected, broader locking on `AspNetUserRoles`.

## Decision 9 — Audit trail

- **Decision**: New `RoleAuditLog : BaseEntity` (Domain/Authorization), append-only, mirroring `McpAuditLog`. Actions: `RoleCreated`, `RoleUpdated`, `RoleDeleted`, `RoleAssigned`, `RoleChanged`, `RoleRemoved`, `PermissionRetired`, `AuthorizationDenied`. Denials (FR-024) are written by a custom `IAuthorizationMiddlewareResultHandler` when a `PermissionRequirement` fails for an authenticated user. The existing `AdminActionLog` structured log line is kept as well.
- **Rationale**: Constitution §8 requires an immutable audit trail distinct from logs; the Users module's log-only interim scope doesn't satisfy FR-021/FR-023.
- **Alternatives**: Structured logs only — rejected (not an audit trail; spec requires before/after values queryable later).

## Decision 10 — Retired permissions (FR-011)

- **Decision**: Resolution ignores keys not in the catalogue (secure immediately). A startup `IHostedService` (`PermissionCatalogReconciler`) deletes orphaned `RolePermissions` rows and writes one `PermissionRetired` audit row per role affected. Newly added catalogue permissions need no action (custom roles simply don't have them; built-ins compute them — Decision 2).

## Decision 11 — Frontend permission awareness

- **Decision**: `SessionResponse` gains `permissions: string[]` (additive). New `usePermissions()` / `useCan(key)` hooks read the session query. `AdminRoute` takes an optional `permission` prop (area View) or `builtInOnly`; `ADMIN_NAV` items declare `permission`/`builtInOnly` and `AdminShell` filters them. Account-menu "Admin panel" shows when the user has any admin permission and links to the first permitted area. Pages hide mutating affordances when `useCan(<area>.manage)` is false. `useIsAdmin` stays for the org document dashboard (F6).
- **Rationale**: Server remains the security boundary (constitution §8); the UI only avoids offering actions that would 403.
