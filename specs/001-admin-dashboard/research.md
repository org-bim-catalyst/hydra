# Research: Admin Dashboard & User Management Console

Phase 0 output. Each topic below resolves a Technical Context unknown by grounding it in the actual current codebase (`src/AskLucy.*`, `frontend/src`) rather than a generic default.

## Topic 1: `ApplicationUser` has no `CreatedAtUtc`, soft-delete, or audit columns today

**Finding**: `ApplicationUser : IdentityUser` (`src/AskLucy.Persistence/Identity/ApplicationUser.cs`) does not inherit `BaseEntity` (`src/AskLucy.Domain/Common/BaseEntity.cs`) — it can't, because `IdentityUser` already defines its own `string Id` primary key, conflicting with `BaseEntity.Id : Guid`. This means:
- `AuditSaveChangesInterceptor` — which populates `CreatedAtUtc`/`ModifiedAtUtc`/soft-delete only for `EntityEntry<BaseEntity>` — never touches `ApplicationUser` rows today.
- There is currently **no registration timestamp stored anywhere** for a user, and no soft-delete flag.

**Decision**: Add three plain properties directly to `ApplicationUser` (mirroring the shape of `BaseEntity`'s soft-delete fields, but not the type, since it can't share the base class): `CreatedAtUtc` (`DateTime`), `IsDeleted` (`bool`), `DeletedAtUtc` (`DateTime?`), `DeletedBy` (`string?`). `ConcurrencyStamp` already exists on `IdentityUser` and is already configured by Identity as the EF Core concurrency token — no new concurrency column is needed.

- `CreatedAtUtc` is set explicitly at both existing user-creation call sites (`IdentityService.RegisterAsync`, `DevAdminSeeder`) and at the external-login auto-provisioning path, since the generic interceptor doesn't cover this type.
- A global EF Core query filter (`HasQueryFilter(u => !u.IsDeleted)`) is added to the `ApplicationUser` entity configuration. Because `UserManager`/`SignInManager` and `UserAdminRepository` all read through `AskLucyDbContext.Users`, this filter transparently makes a soft-deleted user (a) invisible to `GetAllUsersQuery`/`GetByIdAsync` (satisfies FR-016's "no longer... appear in the active user list") and (b) unable to authenticate (`FindByEmailAsync` returns null, so login fails the same way as "no such user" — no separate check needed in the login path).

**Alternatives considered**:
- *Have `ApplicationUser` inherit `BaseEntity`*: rejected — impossible without changing its primary key type away from `string`, which would break every existing Identity/EF Core convention and cascade through the whole auth stack for no benefit.
- *Reuse `LockoutEnd = DateTimeOffset.MaxValue` to represent "deleted"*: rejected — conflates two semantically distinct admin actions (US2 scenarios 1/2 "lock/unlock" vs. scenario 6 "delete"); an admin must be able to unlock a locked account and separately know a deleted account is gone for good, which a single overloaded field cannot express.
- *Hard delete via `UserManager.DeleteAsync`*: rejected — contradicts constitution §5's soft-delete convention for user-facing records and the spec's explicit FR-016/Assumptions requirement.

**Migration note**: existing production rows have no real historical registration date. The EF Core migration backfills `CreatedAtUtc` for pre-existing rows to the migration's applied date (documented as a known, accepted approximation — the "new users, trailing 30 days" chart will simply show a one-time spike of all pre-existing users on the day the migration runs, which self-corrects within 30 days).

## Topic 2: Lock/unlock reuses existing ASP.NET Identity lockout, no new mechanism needed

**Finding**: `IdentityService.ValidateCredentialsAsync` already calls `signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)`, which internally honors `LockoutEnd`/`LockoutEnabled` and returns `SignInResult.LockedOut` for a locked-out user. `IdentityUser` already exposes `LockoutEnd`.

**Decision**: "Lock" = `userManager.SetLockoutEnabledAsync(user, true)` + `userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)` (indefinite lock, since there's no "temporary lock for N minutes" concept in this feature). "Unlock" = `userManager.SetLockoutEndDateAsync(user, null)`. No new column, no new login-path code — the existing `CheckPasswordSignInAsync` call already enforces it. Exposed as a new `IIdentityService.SetLockoutAsync(userId, locked: bool)` method (implemented in `IdentityService.cs` alongside `RegisterAsync`/`DisableTwoFactorAsync`) rather than as raw column writes in `UserAdminRepository`, since lockout is `UserManager`-mediated state, not a plain column.

**Alternatives considered**: A bespoke `IsLocked` boolean — rejected as pure duplication of a capability `IdentityUser` already provides and the login path already checks.

## Topic 3: Force-2FA-reset reuses the existing self-service disable operation, targeted at another user

**Finding**: `IIdentityService.DisableTwoFactorAsync(userId)` already exists (`IdentityService.cs`, backing the user's own Settings-page "disable 2FA" action) and does exactly what an admin-forced reset needs: `userManager.SetTwoFactorEnabledAsync(user, false)` + `userManager.ResetAuthenticatorKeyAsync(user)`.

**Decision**: The new admin action is a thin `ForceReset2faCommand`/handler that calls the *same* `IIdentityService.DisableTwoFactorAsync(targetUserId)` method, just invoked from an `AdministratorOrSuperUser`-gated endpoint instead of the caller's own `/me` scope. No changes to `IIdentityService` itself.

**Alternatives considered**: A separate "admin 2FA reset" identity-service method duplicating the disable logic — rejected as needless duplication (constitution §3, DRY) when the existing method already does the right thing given an arbitrary `userId`.

## Topic 4: Role-escalation control is a data-dependent rule, so it lives in the Application handler, not a blanket ASP.NET Core policy

**Finding**: The existing `AdministratorOrSuperUser` policy (`Program.cs`) treats "Administrator" and "Super User" as equally privileged for every other admin endpoint. The new rule from Clarifications ("only Super User may grant/revoke Administrator or Super User") is conditional on the **target value being written**, not just the caller's role — an ASP.NET Core declarative `[Authorize(Policy=...)]` attribute can't express "reject only when `request.NewRole` is one of these two values."

**Decision**: Keep `[Authorize(Policy = "AdministratorOrSuperUser")]` on the endpoint (so a non-admin never reaches the handler at all), then add an explicit, named authorization check inside `ChangeUserRoleCommandHandler` — before it calls the new `IIdentityService.ChangeRoleAsync(userId, newRole)` — if `request.NewRole` is `"Administrator"` or `"Super User"` and the caller is not themself in the `"Super User"` role, throw `UnauthorizedAccessException` — `ProblemDetailsMiddleware.cs` already maps this exact exception type to a `403` Problem Details response (used elsewhere in the codebase for the same purpose), so no new exception type or middleware mapping is needed. This matches constitution §6's "Authorization decisions... are enforced in Application-layer authorization handlers, not scattered `if` checks in controllers" — the check is centralized in one handler, not duplicated per controller action. The same self-action guard (FR-022) in `LockUserCommandHandler`/`ForceReset2faCommandHandler`/`DeleteUserCommandHandler` reuses the identical `UnauthorizedAccessException` pattern.

**Alternatives considered**: A second ASP.NET Core policy (`SuperUserOnly`) applied unconditionally to the whole role-change endpoint — rejected because it would also block a plain Administrator from changing a *regular* user's role, which FR-014 explicitly still allows.

## Topic 5: Dashboard summary is one query handler over existing tables, not a new persisted read model

**Finding**: At the stated scale (<100 users, inherited from SPEC-000), a single query handler running a handful of `GROUP BY`/`COUNT` aggregates directly against `AskLucyDbContext.Users`/`UserRoles` on demand is well within acceptable latency — no caching, materialized view, or background job is warranted.

**Decision**: `GetAdminDashboardSummaryQuery` → one handler in `Application/Admin/Queries/GetDashboardSummary/`, executed against `Infrastructure`/`Persistence` via a new `IAdminDashboardRepository.GetSummaryAsync()` that issues a small number of EF Core aggregate queries (total count; grouped-by-day count for the last 30 days; grouped-by-lockout-state count; grouped-by-email-confirmed count; grouped-by-two-factor-enabled count; grouped-by-role count via a join against `AspNetUserRoles`/`AspNetRoles`), assembled into a single `DashboardSummaryDto`.

**Alternatives considered**: A precomputed/cached summary table refreshed on a schedule — rejected as premature (YAGNI) at this scale; revisit only if the user count assumption changes materially.

## Topic 6: d3.js integration approach on top of the existing MUI/React stack

**Finding**: `frontend/package.json` has no charting library today (`recharts`, `@mui/x-charts`, etc. are all absent) — this is a net-new frontend dependency. The spec's Assumptions section records d3.js as a binding stakeholder constraint (not a default MUI-first choice), because the constitution's UI Principles otherwise default to "compose from the existing MUI theme... before a bespoke component is written."

**Decision**: Add `d3` + `@types/d3` to `frontend/package.json`. Each chart is a small, self-contained React component under `frontend/src/features/admin/charts/` that owns a `ref`'d SVG element and uses d3 purely for scales/shapes/axes/data-join (`d3-scale`, `d3-shape`, `d3-array`), while React owns the component lifecycle/re-render — the common, supported pattern for combining React with d3 (React renders the SVG container; d3 computes attributes; avoid letting d3 touch the DOM outside its own subtree to prevent React/d3 fighting over the same nodes). Colors are read from the MUI theme palette tokens (`frontend/src/theme/tokens/palette.ts`) at render time via `useTheme()`, not hardcoded hex values, so charts automatically follow the existing light/dark toggle (`useThemeStore`) per constitution §7 ("components MUST NOT hardcode colors that bypass the theme").

**Alternatives considered**: `@mui/x-charts` or `recharts` (a d3 wrapper) — explicitly excluded by the spec's stated constraint, not by a technical limitation; `visx` (Airbnb's d3+React primitives) — a reasonable alternative but adds another dependency surface when the spec already specifies "d3.js" by name.

## Topic 7: Search/sort/pagination on the admin user list

**Finding**: `GetAllUsersQuery`/`UserAdminRepository.ListAllAsync` today returns the entire user table unpaginated (acceptable when the UI was a static table, not acceptable once FR-009/010/011 require search/sort/pagination).

**Decision**: Replace `GetAllUsersQuery` with `GetUsersQuery(string? Search, string? SortBy, bool SortDescending, int Page, int PageSize)` returning a `PagedResult<UserAdminDto>` (`Items`, `TotalCount`). Filtering by partial name/email uses a parameterized `EF.Functions.Like` (or `.Contains(...)`, translated to `LIKE` by the SQL Server provider) — never string-interpolated SQL, per constitution §8. Offset-based pagination is explicitly acceptable per constitution §6 for "small stable admin lists" at this scale.

**Alternatives considered**: Cursor-based pagination — rejected as unnecessary complexity for an admin list bounded by "<100 users."
