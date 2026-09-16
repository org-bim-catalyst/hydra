# Data Model: Bulk Select-All on Admin List Screens

**Feature**: `056-bulk-select-all` | Decisions referenced as *Dn* from [research.md](./research.md).

No new persisted entities or migrations — this feature is pure orchestration over existing rows (`ApplicationUser`, `ApplicationRole`, `AspNetUserRoles`) via existing repositories, plus one new query mode per area. Existing audit trails (`AdminActionLog`, `RoleAuditLog` — one row per affected target, written by the same per-target logic the single-row commands already use) are reused unchanged; a bulk action is simply N individual audited operations run together.

## Application layer (`AskLucy.Application`) — no persistence, in-memory shapes

### `BulkActionSkip` / `BulkActionOutcome` (*D2*)

```
public sealed record BulkActionSkip(string Id, string Reason);
public sealed record BulkActionOutcome(int SucceededCount, IReadOnlyList<BulkActionSkip> Skipped);
```

Lives in `Application/Common/BulkActionOutcome.cs` (alongside the existing `PagedResult<T>`). `Reason` values used across areas: `Self`, `LastSuperUser`, `AlreadyLocked`, `NotFound`, `Locked`, `PrivilegedRoleRequiresSuperUser`, `AlreadyAssigned` (the last two already exist as `BulkAssignSkipReason` — see below).

### `BulkTarget` (shared command-input shape, *D4*)

```
public sealed record BulkTarget(IReadOnlyList<string>? Ids, bool AllMatching);
```

Validated by a shared FluentValidation rule: exactly one of (`Ids` non-empty) or (`AllMatching == true`) — never both, never neither.

### Commands (four Users, one Roles, one Assignments — *D5*)

| Command | Request shape | Handler responsibility |
|---|---|---|
| `BulkLockUsersCommand` | `BulkTarget Target, string? Search` | For each resolved id (skip self, skip if already locked): guard last-Super-User (only counts if the target is currently an active Super User), then `IIdentityService.SetLockoutAsync(id, true)`; one `AdminActionLog` line per success, same as today's single-row `Lock`. |
| `BulkUnlockUsersCommand` | `BulkTarget Target, string? Search` | Same shape; unlock has no last-Super-User guard (unlocking never removes Super User status) or self-exclusion beyond "not resolvable as your own row" being moot (an admin's own row is never locked while they're using the panel). |
| `BulkForceReset2faCommand` | `BulkTarget Target, string? Search` | Skip self; else `IIdentityService.DisableTwoFactorAsync(id)` per id — never fails on an already-disabled target (Assumption in spec.md). |
| `BulkDeleteUsersCommand` | `BulkTarget Target, string? Search` | Skip self; guard last-Super-User; else `IUserAdminRepository.DeleteAsync(id, actorUserId)` per id. |
| `BulkDeleteRolesCommand` | `BulkTarget Target, string? Search` | For each resolved id: `IRoleRepository.DeleteAsync` (already rejects built-in internally, but built-in ids are never resolved into the target set to begin with — Decision 8 below); evict every affected holder's cache (reuses `IRoleAssignmentRepository.ListUserIdsByRoleAsync` + `IAuthorizationCacheInvalidator`, same as `UpdateRoleCommandHandler`). |
| `BulkAssignRoleCommand` | `RoleId string, BulkTarget Target, string? Search` | Resolves ids, then a single call to the already-existing `IRoleAssignmentRepository.BulkReplaceRoleAsync(roleId, ids, actorUserId)` (F2) — this command is thin; almost all logic already lives in the repository. |

Every handler, when `Target.AllMatching == true`, first resolves the live eligible-id set via the matching `Get<Area>EligibleIdsQuery` (never trusts a client-supplied count) — see *D3*.

### Queries (one per area, *D3*)

```
GetUsersEligibleIdsQuery(string? Search, UserBulkAction Action) : IRequest<IReadOnlyList<string>>
GetRolesEligibleIdsQuery(string? Search) : IRequest<IReadOnlyList<string>>                     // always "custom, non-built-in" — no action variant needed, only one bulk action exists
GetRoleAssignmentsEligibleIdsQuery(string RoleId, string? Search) : IRequest<IReadOnlyList<string>>
```

`UserBulkAction` enum: `Lock, Unlock, ForceReset2fa, Delete` — lets one query express the four slightly different eligibility rules (*D1*) without four near-identical queries.

## Persistence layer — repository additions (no schema change)

- `IUserAdminRepository` gains `Task<IReadOnlyList<string>> ListEligibleIdsAsync(string? search, UserBulkAction action, string excludedUserId, CancellationToken)` — same `WHERE` shape as `SearchAsync` minus paging, plus `action`-specific exclusions (*D1*): always excludes `excludedUserId` (the acting admin); for `Lock`/`Delete`, does not exclude anything else structurally (the last-Super-User check is a *count*, not a per-row exclusion — a target might be individually eligible yet still rejected at execution time if it turns out to be the last active Super User; the id list is the same for Lock/Delete/Unlock/ForceReset2fa except each already excludes locked-state-appropriate rows for Lock/Unlock: `Lock` excludes already-locked, `Unlock` excludes already-unlocked).
- `IRoleRepository` gains `Task<IReadOnlyList<string>> ListEligibleIdsAsync(string? search, CancellationToken)` — same as `SearchAsync` minus paging, filtered to `IsBuiltIn == false`.
- `IRoleAssignmentRepository` gains `Task<IReadOnlyList<string>> ListEligibleIdsAsync(string roleId, string? search, bool actorIsSuperUser, CancellationToken)` — same shape as `SearchAsync` minus paging, excluding locked users and (when `actorIsSuperUser` is false) users currently holding a built-in role, mirroring `AssignRoleCommandHandler`'s existing checks.

## Web layer — contracts

```
public sealed record BulkTargetRequest(IReadOnlyList<string>? Ids, bool AllMatching);
public sealed record BulkActionResultResponse(int SucceededCount, IReadOnlyList<BulkActionSkipResponse> Skipped);
public sealed record BulkActionSkipResponse(string Id, string Reason);
```

(Web contracts mirror the Application records 1:1, per the existing convention of a thin Contracts layer — see plan.md.)

## Validation rules (spec → model)

| Rule | Enforced by |
|---|---|
| Exactly one of `Ids`/`AllMatching` (FR-007) | Shared `BulkTargetValidator` (FluentValidation, referenced by each command's own validator) |
| Self excluded from every Users bulk action (FR-002) | `ListEligibleIdsAsync`'s `excludedUserId` parameter, always applied |
| No privileged-role exclusion for Users bulk actions beyond existing single-row behavior (FR-010, corrected per *D1*) | `ListEligibleIdsAsync` — no `PrivilegedRoleNames` filter |
| Built-in roles never selectable/actionable (FR-003) | `IRoleRepository.ListEligibleIdsAsync`'s `IsBuiltIn == false` filter; existing `DeleteAsync`'s own built-in check as defense in depth |
| Role-assignment eligibility mirrors FR-016/FR-020 (FR-004) | `IRoleAssignmentRepository.ListEligibleIdsAsync`, same predicate `AssignRoleCommandHandler` already applies per-row |
| Every bulk action reports per-id outcome, never silent (FR-009) | `BulkActionOutcome` returned by every command, rendered by the shared frontend result summary |
| Last-Super-User safeguard applies identically in bulk (FR-010) | Each Lock/Delete-users and Bulk-assign-role handler still calls the existing guard sequentially per resolved id (not batched away) |

## State transitions

No new entity lifecycle — each bulk command is N repeats of an already-modeled single-row transition (`ApplicationUser` lock state, soft-delete flag; `ApplicationRole` existence; `AspNetUserRoles` single-row replace), each individually audited exactly as today.
