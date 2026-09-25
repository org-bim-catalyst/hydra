# Contract: *View user content* permission rules

The key is `admin.operational-failures.content.view`. It is listed in
`AdminPermissionCatalog.SuperUserControlledKeys` and mirrored in `adminPermissions.ts`. Research D14
explains why it is built this way.

## Effective permissions

| Caller's role | Holds `content.view`? |
|---|---|
| Super User (built-in) | Always. It cannot be removed (FR-016f). |
| Administrator (built-in) | Only when a Super User has granted it to the Administrator role. Every other catalogue key is still automatic (FR-016g). |
| Custom role | When the role's stored grants include it |
| No role | Never |

`content.view` without `admin.operational-failures.view` grants nothing (FR-016e). The
investigation endpoints require view first.

## Mutations and who may perform them

| Endpoint (existing unless marked) | Rule when the actor is **not** a Super User | Response |
|---|---|---|
| `POST /api/v1/admin/roles` (create) | Refused if the requested keys include `content.view` | 403, detail "Only a Super User can grant or remove *View user content*." |
| `PUT /api/v1/admin/roles/{roleId}` (update) | Refused if the requested set differs from the stored set on `content.view`, whether it is **added or omitted** (FR-016h, FR-016i) | 403, same detail |
| `DELETE /api/v1/admin/roles/{roleId}`, `POST /api/v1/admin/roles/actions/bulk-delete` | Refused if the role, or any role in the bulk set, holds `content.view` (FR-016j). Bulk is all-or-nothing. | 403 |
| `PUT /api/v1/admin/role-assignments/{userId}`, `POST /api/v1/admin/role-assignments/actions/bulk-assign`, `PATCH /api/v1/users/{userId}/role` | Refused if the new role, or the user's current role being replaced or removed, holds `content.view` (FR-016j). All three paths share one guard. | 403 |
| **NEW** `PUT /api/v1/admin/roles/administrator/content-access` `{ granted: boolean }` | Super User only (for everyone else, 403 via `AdministratorOrSuperUser` policy plus an in-handler Super User check) | 200 `{ granted }`. Evicts every Administrator's permission cache. |

When the actor **is** a Super User, all of the above succeed under the existing rules.

## Audit (FR-016k)

- Every create, update or delete of a role that changes `content.view` already produces a
  `RoleAuditLog` row, whose before/after JSON includes the key. The new Administrator
  content-access endpoint writes a `RoleUpdated` row with
  `{"before":{"contentView":false},"after":{"contentView":true}}`.
- Assignments already write `RoleAssigned`, `RoleChanged` or `RoleRemoved`. Holding the key is
  derivable from the role at that time. No new audit action is added.

## UI contract

- The role editor's permission picker shows *View user content in failure investigations* under the
  *Operational failures* area. For non-Super-Users it is **disabled but keeps its current checked
  state**, with the tooltip "Only a Super User can grant this". The saved payload therefore always
  echoes the stored value.
- The Roles page shows the switch "Administrators may view user content" to Super Users only.
- The role-assignment picker marks roles that hold the key. For non-Super-Users it disables
  assigning those roles and replacing them, with the same tooltip.
- A 403 from any of these shows a toast with the server's `detail`, never silence (§2.VIII).
