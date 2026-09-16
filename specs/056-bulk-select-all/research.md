# Research: Bulk Select-All on Admin List Screens

**Feature**: `056-bulk-select-all` | **Date**: 2026-09-15 | **Spec**: [spec.md](./spec.md)

## Current-state findings

| # | Finding | Where | Consequence |
|---|---|---|---|
| F1 | `LockUserCommandHandler`, `UnlockUserCommandHandler`, `ForceReset2faCommandHandler`, `DeleteUserCommandHandler` each take one `UserId`, guard only self-action, and (Lock/Delete only) call `LastSuperUserGuard` — none of them exclude a privileged-role-holding target from a *different* admin acting on them. There is **no existing rule** today that stops a plain Administrator from locking/deleting/force-2FA-resetting another Administrator or a Super User. | `Application/Users/Commands/{LockUser,UnlockUser,ForceReset2fa,DeleteUser}` | See Decision 1 — corrects spec.md FR-002's parenthetical, which assumed a rule that doesn't exist for these four actions (it exists only for role changes, specs/055 FR-016). |
| F2 | specs/055-role-management's User Story 4 (bulk role assignment) was **speced but never implemented** — `BulkAssignRoleCommand`, its controller endpoint, and `BulkAssignRoleDialog` don't exist. `IRoleAssignmentRepository.BulkReplaceRoleAsync` and `BulkAssignResult`/`BulkAssignSkipReason` *do* already exist (written ahead of schedule) and match this feature's needs closely. | `Application/Abstractions/IRoleAssignmentRepository.cs`, `Persistence/Repositories/RoleAssignmentRepository.cs` | This feature must build the User Story 3 bulk-assign command/endpoint/UI as new work, not just add "select all" to something that already exists — spec.md's assumption ("reusing that feature's existing bulk-assignment action") is only half true: the repository plumbing exists, the command/controller/UI do not. |
| F3 | `GetUsersQuery`, `ListRolesQuery`, `ListRoleAssignmentsQuery` each already return `PagedResult<T>.TotalCount` for the *current filter* — but that count is "everything matching the search," not "everything eligible for this specific bulk action" (it includes the acting admin's own row, locked users, etc.). | `Application/Users/Queries/GetUsers`, `Authorization/Roles/Queries/ListRoles`, `Authorization/Assignments/Queries/ListRoleAssignments` | The all-matching-pages count and target-id resolution needs a dedicated eligible-only query per area (Decision 3), not a reuse of the existing list `TotalCount`. |
| F4 | Every existing single-target command returns nothing (`IRequest`, no response) and reports success only by not throwing; failure is one exception per call. | same as F1 | A bulk command needs its own per-row result shape — reintroduces exactly the `BulkAssignResult`-style pattern already used for role assignment (F2), generalized to the other two areas (Decision 2). |
| F5 | `UserAdminDto`/`IUserAdminRepository` has no notion of "is this the last active Super User" or "is this row privileged" beyond the `Role` string already on the DTO — `PrivilegedRoleNames.All.Contains(role)` is how existing code checks it. | `Application/Users/PrivilegedRoleNames.cs` | Reused as-is for the Lock/Delete last-Super-User exclusion (Decision 1). |

## Decision 1 — Users bulk-action eligibility (corrects spec.md FR-002)

- **Decision**: A user row is eligible for a Users bulk action when: it is not the acting admin's own row, AND (for Lock and Delete specifically) excluding it would not strand the system with zero active Super Users. Force 2FA reset and Unlock have no privileged-role or last-Super-User exclusion beyond self. There is **no** "a plain Administrator cannot select a privileged-role-holding user" exclusion for these four actions — that rule exists only for role changes (specs/055 FR-016), not for lock/unlock/2FA/delete, and no such protection exists today for the single-row version of any of these four actions either (F1).
- **Rationale**: Constitution Principle III (no invented requirements) — matching the actual, currently-shipped single-row behavior keeps bulk and single-row eligibility identical, which is what spec.md FR-010 actually requires ("every existing... rule MUST apply identically whether selected individually or via select-all"). Inventing a new privileged-role shield for bulk-only would make bulk *stricter* than single-row action on the same targets, which is not "identical" and not asked for.
- **Alternatives**: Introduce the privileged-role exclusion as spec.md's parenthetical literally states — rejected: it would silently make one admin unable to bulk-lock another Administrator while still being able to lock them one row at a time via the existing per-row menu, an inconsistency worse than the one it tries to avoid. If tighter protection for privileged users is wanted, that is a follow-up feature touching the single-row actions too, not something to smuggle in only for bulk.

## Decision 2 — Shared bulk-result shape

- **Decision**: One generalized pair, reused by all four bulk operations:
  ```
  BulkActionOutcome(int SucceededCount, IReadOnlyList<BulkActionSkip> Skipped)
  BulkActionSkip(string Id, string Reason)   // Reason is a short machine code, e.g. "Self", "LastSuperUser", "AlreadyLocked", "NotFound", "Locked", "PrivilegedRoleRequiresSuperUser"
  ```
  Generalizes the existing `BulkAssignResult`/`BulkAssignSkipReason` (F2) rather than keeping a separate, differently-shaped result per area.
- **Rationale**: Constitution Principle III (DRY) — four near-identical result shapes would be pure duplication; one generic pair, with an area-specific `Reason` enum's string value, keeps the frontend's result-summary component (Decision 6) reusable across all three screens too.
- **Alternatives**: Keep `BulkAssignResult` role-specific and add three more bespoke result types — rejected as needless duplication now that a fourth (and by counting, sixth: Lock, Unlock, Force2FA, Delete, Role-delete, Role-assign) consumer exists.

## Decision 3 — Resolving "all matching pages" server-side, and the confirmation counts

- **Decision**: Each area's existing list query gains an additional lightweight sibling: `Get<Area>EligibleIdsQuery(filters..., action)` returning `IReadOnlyList<string>` — every id matching the current filter/search AND eligible for the specific action, unbounded (no paging). The confirmation dialog calls this once (already needed to resolve "all matching" targets) and its `Count` **is** the second confirmation number — no separate count-only endpoint. The page-scoped number is just the selection's local length; no server round-trip needed for it.
- **Rationale**: One id-resolution query serves both needs (the confirmation's count text and, if the admin picks that scope, the actual target-id list the bulk command runs against) — avoids a race between "count I showed you" and "ids I actually acted on" (spec.md's edge case: the true count changing between dialog-open and confirm). The bulk command re-resolves ids at execution time when scope is "all matching" rather than trusting a list the client fetched moments earlier, so a row that became ineligible in between is simply not included, not silently mis-acted-on.
- **Alternatives**: Reuse the existing list query's `TotalCount` for the second number — rejected (F3: it counts ineligible rows too, so the displayed number would not match what actually gets acted on). Have the client fetch every page and merge locally — rejected: defeats the point of a server-side "all matching" scope and doesn't scale to spec.md SC-002's 500-row example.

## Decision 4 — Bulk command request shape (explicit ids vs. filter-resolved)

- **Decision**: Each bulk command takes a discriminated target: `TargetIds: string[]?` (page scope — the client's own selection) XOR `AllMatching: true` plus the same filter parameters the screen's list query already takes (`Search`, and for Role assignments `RoleId`). Exactly one of the two must be set; validated by each command's FluentValidation validator.
- **Rationale**: Keeps the wire contract close to what the client already has in hand for the common (page) case, while the "all matching" case never requires the client to enumerate potentially hundreds of ids — it just re-sends the filter it's already displaying.
- **Alternatives**: Always resolve ids server-side, never accept an explicit list — rejected: page-scoped selection can include manually-adjusted picks (a deselected row, F-006), which a filter alone cannot express.

## Decision 5 — New commands and endpoints

| Area | New command(s) | New endpoint | Reuses |
|---|---|---|---|
| Users | `BulkLockUsersCommand`, `BulkUnlockUsersCommand`, `BulkForceReset2faCommand`, `BulkDeleteUsersCommand` (four, mirroring the four existing single-target commands — not one polymorphic "bulk user action" command, to keep each's eligibility/guard logic simple and independently testable, Principle II) | `POST /api/v1/users/actions/bulk-lock`, `.../bulk-unlock`, `.../bulk-force-2fa-reset`, `DELETE /api/v1/users/actions/bulk-delete` (sub-resource actions per constitution §6, mirroring the existing singular `/actions/lock` etc.) | `IIdentityService`, `LastSuperUserGuard`, `IUserAdminRepository` |
| Roles | `BulkDeleteRolesCommand` | `POST /api/v1/admin/roles/actions/bulk-delete` | `IRoleRepository.DeleteAsync` (called per-id), `IAuthorizationCacheInvalidator` |
| Role assignments | `BulkAssignRoleCommand` (the specs/055 US4 command, built now) | `POST /api/v1/admin/role-assignments/actions/bulk-assign` | `IRoleAssignmentRepository.BulkReplaceRoleAsync` (already exists, F2) |
| all | `Get<Area>EligibleIdsQuery` × 3 (Users/Roles/RoleAssignments) | `GET .../actions/bulk-eligible-ids` per area | existing repositories' `SearchAsync`, extended with an unbounded (no-paging) mode |

- **Rationale**: Matches constitution §6 (non-CRUD verbs as `/actions/` sub-resources) and §3 (CQRS, one handler per command) exactly as the existing single-row actions already do.

## Decision 6 — Frontend: one reusable bulk-selection toolbar pattern

- **Decision**: A shared `useBulkSelection(pageRowIds: string[])` hook (selection Set, `toggleAll`/`toggleOne`, indeterminate flag) and a shared `BulkActionConfirmDialog` (shows the two counts, scope choice, and renders a `BulkActionOutcome` afterward) — both used by all three pages, not reimplemented per page.
- **Rationale**: Constitution §7 (design system: a shared component needs ≥2 consumers or a foundational-primitive justification — this has three) and Principle III (DRY): the selection/indeterminate logic and the two-count confirmation are identical across screens; only which ids/action to call differs.
- **Alternatives**: Bespoke selection state per page — rejected, exactly the duplication constitution §7 exists to prevent.

## Decision 7 — Selection lifecycle

- **Decision**: Selection state resets (empties) whenever the page's own query key changes — i.e., on page/pageSize/search/roleFilter/sort change — matching spec.md's edge case ("changes the sort, filter/search, or page — the page-scoped selection is cleared"). Implemented by keying the selection hook's internal state to the same dependency array as the list query.
- **Rationale**: Simplest correct behavior matching the spec literally; avoids a selection silently referring to rows no longer visible.
