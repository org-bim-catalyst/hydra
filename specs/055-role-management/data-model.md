# Data Model: Role Definition, Role Assignment & Permission Catalogue

**Feature**: `055-role-management` | **Date**: 2026-09-14 | Decisions referenced as *Dn* from [research.md](./research.md).

## Domain (AskLucy.Domain/Authorization) — no persistence

### `AdminArea` (enum)

`Dashboard, Users, AiProviders, DefaultModels, AiCapabilities, AgentPolicies, SystemAgents, WorkflowPolicies, McpServers`

### `AdminPermissionLevel` (enum)

`View, Manage`

### `AdminPermission` (record / smart enum)

| Field | Type | Notes |
|---|---|---|
| `Key` | string | Stable identifier, `admin.<area-kebab>.<level>` e.g. `admin.mcp-servers.manage`. Never reused once retired. |
| `Area` | `AdminArea` | |
| `Level` | `AdminPermissionLevel` | |
| `DisplayName` | string | e.g. "Manage MCP servers" |
| `Description` | string | Plain-language statement of what it allows |
| `Implies` | `AdminPermission?` | `Manage` → matching `View`; `null` for View |

### `AdminPermissionCatalog` (static, read-only)

The 16 permissions (*D6*):

| Area | View | Manage |
|---|---|---|
| Dashboard | ✅ | — |
| Users | ✅ | ✅ |
| AI providers | ✅ | ✅ |
| Default models | ✅ | ✅ |
| AI capabilities | ✅ | ✅ |
| Agent policies | ✅ | ✅ |
| System agents | ✅ | — |
| Workflow policies | ✅ | ✅ |
| MCP servers | ✅ | ✅ |

API: `All`, `TryGet(key, out permission)`, `ByArea(area)`.

### `PermissionSet` (value object)

- Constructed from a collection of keys; **rejects** unknown keys and empty sets (FR-004, "at least one").
- **Normalizes**: adding a Manage key adds its View key (FR-004).
- Equality by the normalized key set; exposes `Keys` (sorted) and `Contains(key)`.
- `PermissionSet.Full` = entire catalogue (built-in roles, *D2*).

### `RoleName` (value object)

- Trimmed; 2–50 characters; not whitespace-only.
- Reserved (case-insensitive): `Administrator`, `Super User`, `Regular`, `No role`.
- Equality/uniqueness by `ToUpperInvariant()` of the trimmed value (matches Identity `NormalizedName`).

### `RoleAuditLog : BaseEntity` (append-only, *D9*)

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid (v7) | |
| `Action` | `RoleAuditAction` | `RoleCreated, RoleUpdated, RoleDeleted, RoleAssigned, RoleChanged, RoleRemoved, PermissionRetired, AuthorizationDenied` |
| `ActorUserId` | string | `system:migration` / `system:reconciler` for non-human writes |
| `TargetRoleId` | string? | No FK — survives role deletion |
| `TargetRoleName` | string? | Name at time of event |
| `TargetUserId` | string? | No FK |
| `DetailsJson` | string | Before/after: `{ "before": {...}, "after": {...} }`; denials: `{ "requiredAnyOf": [...], "path": "...", "method": "..." }` |
| `OccurredAtUtc` | DateTime | |

Factory: `RoleAuditLog.Record(action, actorUserId, targetRoleId, targetRoleName, targetUserId, detailsJson)`. No setters, no update/delete paths.

### `SuperUserSafeguard` (domain service, pure)

`EnsureAtLeastOneActiveSuperUserRemains(int activeSuperUsersBefore, int superUsersBeingRemoved)` → throws `DomainRuleViolationException` when the result would be `< 1`. Replaces the rule body of the existing `LastSuperUserGuard` (which keeps loading the counts) so single, bulk, and deletion paths share one rule (Principle III).

## Persistence (AskLucy.Persistence)

### `ApplicationRole : IdentityRole` — table `AspNetRoles` (extended)

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | nvarchar(450) | no | existing PK |
| `Name` / `NormalizedName` | nvarchar(256) | yes | existing; **unique index on `NormalizedName`** already exists (`RoleNameIndex`) |
| `ConcurrencyStamp` | nvarchar(max) | yes | existing; concurrency token (*D8*) |
| `Description` | nvarchar(250) | yes | **new** |
| `IsBuiltIn` | bit | no, default 0 | **new** |
| `CreatedAtUtc` / `CreatedBy` | datetime2 / nvarchar(450) | no / yes | **new**, set explicitly (can't inherit `BaseEntity` — same precedent as `ApplicationUser`) |
| `ModifiedAtUtc` / `ModifiedBy` | datetime2 / nvarchar(450) | yes | **new** |

`AskLucyDbContext` becomes `IdentityDbContext<ApplicationUser, ApplicationRole, string>`; `AddRoles<ApplicationRole>()`.

### `AspNetRoleClaims` (existing table, extended — *D1/D1b*)

A role's permissions are `IdentityRoleClaim<string>` rows, not a new table:

| Column | Value for a permission grant |
|---|---|
| `RoleId` | FK → `AspNetRoles.Id` (existing FK, cascade delete — Identity default) |
| `ClaimType` | `"permission"` (`PermissionClaims.Type`) |
| `ClaimValue` | Catalogue key; not an FK (catalogue is code) |

**New**: unique index `IX_AspNetRoleClaims_RoleId_ClaimType_ClaimValue` on `(RoleId, ClaimType, ClaimValue)` — `AspNetRoleClaims` has no unique constraint by default; this prevents duplicate grants and detects concurrent attach races. Never rows for built-in roles (*D2*). Other claim types may appear in this table in future without conflict (`ClaimType` scopes the query).

### `AspNetUserRoles` (existing) — constraint added

Unique index **`IX_AspNetUserRoles_UserId`** (*D7*). Existing PK `(UserId, RoleId)` and `IX_AspNetUserRoles_RoleId` unchanged.

### `RoleAuditLogs` (new)

Columns per the Domain entity + `BaseEntity` audit columns. Indexes: `OccurredAtUtc`, `TargetRoleId`, `TargetUserId`, `ActorUserId`.

## Relationships

```text
ApplicationUser 1 ──── 0..1 AspNetUserRoles ──── 1 ApplicationRole 1 ──── 0..* AspNetRoleClaims(ClaimType="permission") ···· AdminPermissionCatalog (code)
RoleAuditLog ···· (soft references by id/name — no FKs)
```

## Validation rules (spec → model)

| Rule | Enforced by |
|---|---|
| Name 2–50, trimmed, unique case-insensitive, not reserved (FR-003, FR-009) | `RoleName` + FluentValidation (shape) + `RoleNameIndex` unique index (race) → 409 |
| Description ≤ 250 (FR-003) | FluentValidation + column length |
| ≥ 1 permission, known keys only, Manage ⇒ View (FR-004) | `PermissionSet` |
| Built-ins immutable & undeletable (FR-008) | Application handlers check `IsBuiltIn` → 403 |
| At most one role per user (FR-012) | `IX_AspNetUserRoles_UserId` + repository replace semantics |
| Only Super User touches Administrator/Super User (FR-016) | Assign/Bulk handlers via `ICurrentUserAccessor.IsInRole` (current, *D3*) |
| ≥ 1 active Super User (FR-017) | `SuperUserSafeguard` inside `sp_getapplock` transaction (*D8*) |
| Locked/soft-deleted users not targets (FR-020) | Assignment handlers load target via `IUserAdminRepository` (global soft-delete filter) and reject locked |
| Conflicting concurrent edits (FR-022) | `ConcurrencyStamp` (roles), `expectedCurrentRoleId` (assignments) → 409 |

## State transitions

**Role**: `(none) → Active` (create) → `Active` (update: name/description/permissions) → `(deleted)`; deletion removes its `AspNetUserRoles` rows (holders → no role) and `RolePermissions` rows in the same commit, plus one `RoleDeleted` audit row listing affected user ids.

**User's role**: `No role ⇄ Role A → Role B` (replace, never add); each transition writes `RoleAssigned` / `RoleChanged` / `RoleRemoved`, evicts `authz:{userId}` cache, and bumps `SecurityStamp`.

## Migration `AddRoleManagement` (one logical change)

1. Add `AspNetRoles` columns; create `RoleAuditLogs`.
2. Upsert built-in roles by `NormalizedName` (`ADMINISTRATOR`, `SUPER USER`): insert if missing, set `IsBuiltIn = 1`.
3. De-duplicate `AspNetUserRoles` (keep Super User > Administrator > lowest `RoleId`), inserting `RoleRemoved` audit rows with actor `system:migration`.
4. Create `IX_AspNetUserRoles_UserId` (unique) and `IX_AspNetRoleClaims_RoleId_ClaimType_ClaimValue` (unique) on the existing `AspNetRoleClaims` table.

`Down`: drops the two new indexes, `RoleAuditLogs`, and the `AspNetRoles` columns (`AspNetRoleClaims` itself is untouched — it's Identity's own table). Step 3's removed duplicate assignments are **not restored** (documented irreversible part; the audit rows record them). Save the migration file without BOM (repo CI gotcha).
