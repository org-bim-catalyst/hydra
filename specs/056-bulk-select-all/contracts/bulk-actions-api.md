# API Contract: Bulk Select-All Actions

**Feature**: `056-bulk-select-all` | Base path `/api/v1` | All errors are RFC 7807 Problem Details.

Every endpoint below reuses the same authorization the screen's existing endpoints already carry: Users bulk actions → `[RequirePermission("admin.users.manage")]`; Roles bulk delete → `AdministratorOrSuperUser` (reserved, matches `AdminRolesController`); Role assignment bulk assign → `AdministratorOrSuperUser` (reserved, matches `AdminRoleAssignmentsController`). No new authorization surface.

Common request shape:

```json
{ "ids": ["id-1", "id-2"], "allMatching": false, "search": null }
```

or

```json
{ "ids": null, "allMatching": true, "search": "ana" }
```

`search` MUST be supplied (or explicitly null/empty to mean "no filter") whenever `allMatching` is `true`, so the server resolves the exact same result set the confirmation dialog counted.

Common response shape:

```json
{ "succeededCount": 18, "skipped": [ { "id": "user-9", "reason": "Self" }, { "id": "user-14", "reason": "LastSuperUser" } ] }
```

`200 OK` always (a bulk action's partial success is not itself an error — matches the existing `BulkAssignResult` precedent). `400` only for a malformed request (e.g. both/neither of `ids`/`allMatching`).

---

## 1. Users

### `GET /users/actions/bulk-eligible-ids`

Query: `search?`, `action` (`Lock|Unlock|ForceReset2fa|Delete`, required)

`200 { "ids": ["user-1", "user-2", ...] }` — unbounded, always excludes the caller's own id; for `Lock` excludes already-locked users, for `Unlock` excludes already-unlocked users.

### `POST /users/actions/bulk-lock`

Body: common request shape. `200` → common response shape. Reuses `LockUserCommandHandler`'s per-user logic (self-exclusion defensive re-check, last-Super-User guard) N times; a target that would strand the system is skipped with reason `LastSuperUser`, not aborting the batch.

### `POST /users/actions/bulk-unlock`

Same shape. No last-Super-User guard (never applicable).

### `POST /users/actions/bulk-force-2fa-reset`

Same shape. Never produces a `Reason` beyond `Self`/`NotFound` — resetting an already-reset user still counts as succeeded (spec.md Assumption).

### `POST /users/actions/bulk-delete`

Same shape as bulk-lock; soft-deletes each resolved id via the existing `IUserAdminRepository.DeleteAsync`.

---

## 2. Roles

### `GET /admin/roles/actions/bulk-eligible-ids`

Query: `search?`

`200 { "ids": [...] }` — every custom (non-built-in) role id matching `search`, unbounded.

### `POST /admin/roles/actions/bulk-delete`

Body: common request shape. `200` → response shape extended with the existing per-role "unassigned user count" detail:

```json
{ "succeededCount": 4, "skipped": [], "unassignedUserCounts": { "role-1": 2, "role-2": 0 } }
```

Each successfully deleted role's holders lose the role (matches specs/055 FR-007); their authorization caches are evicted the same way `DeleteRoleCommandHandler` already does per-role.

---

## 3. Role assignments

### `GET /admin/role-assignments/actions/bulk-eligible-ids`

Query: `roleId` (required), `search?`

`200 { "ids": [...] }` — every user id matching `search` that `roleId` could legally be assigned to right now (locked users and, unless the caller is a Super User, built-in-role holders excluded), unbounded.

### `POST /admin/role-assignments/actions/bulk-assign`

Body: common request shape **plus** `"roleId": "role-1"`. `200` → common response shape, `Reason` values as already defined by `BulkAssignSkipReason` (`PrivilegedRoleRequiresSuperUser`, `LastSuperUser`, `UserLocked`, `UserNotFound`, `AlreadyAssigned`). This is the specs/055-role-management User Story 4 endpoint, implemented as part of this feature (research.md F2) — its repository method (`IRoleAssignmentRepository.BulkReplaceRoleAsync`) already existed; only the command/controller/UI are new.

---

## 4. No changes to existing endpoints

The existing single-row endpoints (`POST /users/{userId}/actions/lock`, `DELETE /admin/roles/{roleId}`, `PUT /admin/role-assignments/{userId}`, etc.) are untouched — this feature is purely additive.
