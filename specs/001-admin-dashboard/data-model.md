# Data Model: Admin Dashboard & User Management Console

## `ApplicationUser` (existing entity, extended)

`src/AskLucy.Persistence/Identity/ApplicationUser.cs` — `IdentityUser` subclass, unchanged shape apart from three new properties (see `research.md` Topic 1):

| Field | Type | Notes |
|---|---|---|
| `CreatedAtUtc` | `DateTime` | New. Set at creation (`RegisterAsync`, external-login provisioning, `DevAdminSeeder`). Backfilled for existing rows to the migration-apply date. Drives FR-003's daily/30-day trend. |
| `IsDeleted` | `bool` | New. Computed convenience is not used here (unlike `BaseEntity.IsDeleted`, which derives from `DeletedAtUtc`) — kept as an explicit column so the EF Core global query filter can be a simple, indexable predicate. |
| `DeletedAtUtc` | `DateTime?` | New. Set by the soft-delete command; null otherwise. |
| `DeletedBy` | `string?` | New. The acting admin's user id. |
| `LockoutEnd` / `LockoutEnabled` | *(existing, from `IdentityUser`)* | Reused as-is for lock/unlock (FR-012/013) — no new column. |
| `TwoFactorEnabled` | *(existing, from `IdentityUser`)* | Reused as-is; force-reset clears it via the existing `DisableTwoFactorAsync` path (FR-015). |
| `EmailConfirmed` | *(existing, from `IdentityUser`)* | Reused as-is for the confirmed/pending dashboard split (FR-005). |
| `ConcurrencyStamp` | *(existing, from `IdentityUser`)* | Already the EF Core concurrency token — covers the concurrent-edit edge case without a new `RowVersion` column. |

**EF Core configuration additions** (`src/AskLucy.Persistence/Configurations/`, new `ApplicationUserConfiguration`):
- `HasQueryFilter(u => !u.IsDeleted)` — global filter; every existing read path (`UserManager`, `UserAdminRepository`, login) automatically excludes soft-deleted users.
- Index on `CreatedAtUtc` (supports the 30-day trend `GROUP BY`).
- Index on `IsDeleted` (supports the filter itself at scale, cheap even at <100 rows, consistent with constitution §5's "every column used in a WHERE... MUST be covered by an index").

**Migration**: one new EF Core migration adding the three columns plus the two indexes; backfills `CreatedAtUtc` for existing rows (see `research.md` Topic 1 for the accepted approximation).

## `Role` / `AspNetUserRoles` (existing, unchanged schema — new business rule only)

No schema change. New rule enforced in `ChangeUserRoleCommandHandler` (`research.md` Topic 4): only a caller holding `"Super User"` may add/remove the `"Administrator"` or `"Super User"` role for any target user; a plain `"Administrator"` may only change a target's role when neither the current nor the new role is one of those two.

## `DashboardSummaryDto` (new, computed/read-only — not a persisted entity)

Assembled by `GetAdminDashboardSummaryQueryHandler` from live aggregate queries; never stored.

```csharp
public sealed record DashboardSummaryDto(
    int TotalUsers,
    IReadOnlyList<DailyUserCountDto> NewUsersLast30Days, // one entry per day, zero-filled for days with no signups
    int ActiveUsers,
    int LockedOutUsers,
    int EmailConfirmedUsers,
    int EmailPendingUsers,
    int TwoFactorEnabledUsers,
    IReadOnlyList<RoleCountDto> RoleDistribution); // one entry per existing role, including "Regular" (no role)

public sealed record DailyUserCountDto(DateOnly Date, int NewUsers);

public sealed record RoleCountDto(string RoleName, int UserCount);
```

## `UserAdminDto` (existing, unchanged shape)

`src/AskLucy.Application/Users/UserAdminDto.cs` — no new fields required by this feature; lock state is already exposed via `LockoutEnabled`, and the list/search/sort/pagination changes only affect how many/which rows are returned, not the DTO shape.

## `PagedResult<T>` (new, generic — Application-layer shared type)

```csharp
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
```

Used by the evolved `GetUsersQuery` (replacing today's unpaginated `GetAllUsersQuery`, `research.md` Topic 7).

## Commands (new, Application layer — `Application/Users/Commands/`)

| Command | Fields | Handler behavior |
|---|---|---|
| `LockUserCommand` | `UserId` | Rejects (`UnauthorizedAccessException` → `403`, FR-022) if `UserId` == caller's id. Also rejects (FR-023) if the target is the last active Super User. Sets `LockoutEnabled = true`, `LockoutEnd = DateTimeOffset.MaxValue`. Logs a security event (FR-021). |
| `UnlockUserCommand` | `UserId` | Sets `LockoutEnd = null`. Logs a security event. |
| `ChangeUserRoleCommand` | `UserId`, `NewRole` | `NewRole` ∈ `{"Administrator", "Super User", "Regular"}`. **`"Regular"` is a sentinel, not a seeded `IdentityRole`** — it means "remove all current privileged role memberships, assign none." The handler MUST branch on this before calling `IIdentityService.ChangeRoleAsync`: if `NewRole == "Regular"`, only call `RemoveFromRolesAsync`; never call `AddToRoleAsync(user, "Regular")`. Enforces the Topic 4 privilege rule and the FR-023 last-Super-User guard; throws `UnauthorizedAccessException` (already mapped to `403` Problem Details by `ProblemDetailsMiddleware.cs` — no new exception type needed) if either is violated. Logs a security event including old and new role. |
| `ForceReset2faCommand` | `UserId` | Rejects (`UnauthorizedAccessException` → `403`, FR-022) if `UserId` == caller's id. Delegates to `IIdentityService.DisableTwoFactorAsync(UserId)` (Topic 3). Logs a security event. |
| `DeleteUserCommand` | `UserId` | Rejects (`UnauthorizedAccessException` → `403`, FR-022) if `UserId` == caller's id. Also rejects (FR-023) if the target is the last active Super User. Sets `IsDeleted = true`, `DeletedAtUtc = now`, `DeletedBy = caller`. Logs a security event. |

Each command has exactly one handler and a FluentValidation validator (non-empty `UserId`; `NewRole` restricted to the existing closed set of role names), per constitution §3 (CQRS rules).

**Where each handler gets its data access from** (matches the codebase's existing split, not a new pattern):
- `LockUserCommandHandler`, `UnlockUserCommandHandler`, `ChangeUserRoleCommandHandler`, `ForceReset2faCommandHandler` all call **new methods added to `IIdentityService`** (`SetLockoutAsync(userId, locked: bool)`, `ChangeRoleAsync(userId, newRole)`, plus the already-existing `DisableTwoFactorAsync`) — because lockout and role membership are `UserManager<ApplicationUser>` concerns (`AspNetUserRoles`/`AspNetRoles`, `LockoutEnd`/`LockoutEnabled`), exactly like registration/2FA already are. `ChangeRoleAsync` internally uses `UserManager.GetRolesAsync`/`RemoveFromRolesAsync`/`AddToRoleAsync` (not raw `AspNetUserRoles` table writes) and is where the Topic 4 privilege-escalation guard lives.
- The FR-023 "last remaining Super User" guard (`LockUserCommandHandler`, `ChangeUserRoleCommandHandler`, `DeleteUserCommandHandler`) calls a fourth new `IIdentityService` method: `Task<int> CountActiveSuperUsersAsync(CancellationToken)`, implemented as `userManager.GetUsersInRoleAsync("Super User")` filtered to accounts where `LockoutEnd is null or <= now` (soft-deleted accounts are already excluded automatically by the `ApplicationUser` global query filter, `research.md` Topic 1). Each guarded handler calls this, and if the target is currently one of the counted active Super Users and the count is exactly 1, the action is rejected before it runs.
- `DeleteUserCommandHandler` and the evolved `GetUsersQueryHandler` continue to go through **`IUserAdminRepository`** (extended with `DeleteAsync`/`SearchAsync`), which directly manipulates plain `ApplicationUser` columns via `AskLucyDbContext` — matching the existing `UpdateAsync` pattern for `FirstName`/`LastName`, since `IsDeleted`/`DeletedAtUtc`/`DeletedBy` are plain columns, not `UserManager`-mediated state.

## Queries (new/evolved, Application layer — `Application/Users/Queries/`, `Application/Admin/Queries/`)

| Query | Fields | Returns |
|---|---|---|
| `GetUsersQuery` (replaces `GetAllUsersQuery`) | `Search?`, `SortBy`, `SortDescending`, `Page`, `PageSize` | `PagedResult<UserAdminDto>` |
| `GetAdminDashboardSummaryQuery` | *(none)* | `DashboardSummaryDto` |

## Security/audit logging (FR-021)

Every command above logs a single structured Serilog event (`ILogger<T>`, named properties per constitution §4) of the shape: `AdminUserActionPerformed { ActorUserId, ActorRole, TargetUserId, Action, Detail, TimestampUtc }`. This is written to the existing structured log sink — **not** a new audit-log UI or table (explicitly out of scope per the spec's Non-Goals); it feeds whatever operational log tooling already consumes Serilog output.
