# API Contract: Permissions, Roles & Role Assignments

**Feature**: `055-role-management` | Base path `/api/v1` | All errors are RFC 7807 Problem Details with `traceId`.

**Authorization for every endpoint in §1–§3**: `[Authorize(Policy = "AdministratorOrSuperUser")]` (FR-002 — never grantable by a custom role) + rate-limit policy `admin-endpoints`.

Common errors: `401` unauthenticated · `403` not Administrator/Super User, or rule violation (built-in role edit, privileged-role change by a plain Administrator, last Super User) · `404` unknown role/user · `409` concurrency conflict or duplicate name · `422`/`400` validation (FluentValidation shape).

---

## 1. Permissions

### `GET /admin/permissions`

Query: `area?` (`AdminArea` string), `roleId?`

`200`

```json
{
  "areas": [
    {
      "area": "McpServers",
      "displayName": "MCP servers",
      "permissions": [
        {
          "key": "admin.mcp-servers.view",
          "displayName": "View MCP servers",
          "description": "See registered MCP servers, their tools, health and audit log.",
          "level": "View",
          "roles": [
            { "id": "…", "name": "Super User", "isBuiltIn": true },
            { "id": "…", "name": "Administrator", "isBuiltIn": true },
            { "id": "…", "name": "Moderator", "isBuiltIn": false }
          ]
        }
      ]
    }
  ]
}
```

Unpaginated (16 permissions; bounded by the catalogue). `roleId` filters to permissions the role includes. No POST/PUT/PATCH/DELETE exists on this resource → `405` (FR-031).

---

## 2. Roles

### `GET /admin/roles`

Query: `search?` (name contains), `page=1`, `pageSize=20` (max 100).

`200` `PagedResult<RoleSummaryDto>`

```json
{
  "items": [
    {
      "id": "…",
      "name": "Moderator",
      "description": "Manages MCP servers and agent policies",
      "isBuiltIn": false,
      "permissionKeys": ["admin.agent-policies.manage", "admin.agent-policies.view", "admin.mcp-servers.manage", "admin.mcp-servers.view"],
      "userCount": 4,
      "modifiedAtUtc": "2026-09-14T10:12:00Z",
      "concurrencyStamp": "…"
    }
  ],
  "totalCount": 3, "page": 1, "pageSize": 20
}
```

Built-in roles are always listed first and return the full catalogue in `permissionKeys`.

### `GET /admin/roles/{roleId}` → `200 RoleSummaryDto`

### `POST /admin/roles`

```json
{ "name": "Moderator", "description": "…", "permissionKeys": ["admin.mcp-servers.manage"] }
```

`201` + `Location` → `{ "id": "…", "concurrencyStamp": "…" }` (stored keys are normalized: Manage adds View).
`409` name taken · `400` unknown key / empty set / reserved or invalid name.

### `PUT /admin/roles/{roleId}`

```json
{ "name": "Moderator", "description": "…", "permissionKeys": ["…"], "concurrencyStamp": "…" }
```

`200 { "concurrencyStamp": "…" }` · `403` built-in · `409` stale stamp or name taken.

### `DELETE /admin/roles/{roleId}?concurrencyStamp=…`

`200 { "unassignedUserCount": 4 }` · `403` built-in · `409` stale stamp.

---

## 3. Role assignments

### `GET /admin/role-assignments`

Query: `search?` (email/first/last name), `roleId?` (a role id, or `none` for users with no role), `assignableOnly=false` (excludes locked users), `page=1`, `pageSize=20` (max 100).

`200` `PagedResult<RoleAssignmentDto>`

```json
{
  "items": [
    {
      "userId": "…", "email": "a@b.com", "firstName": "Ana", "lastName": "Lee",
      "isLockedOut": false,
      "role": { "id": "…", "name": "Viewer", "isBuiltIn": false },
      "canChange": true
    }
  ],
  "totalCount": 1, "page": 1, "pageSize": 20
}
```

`role` is `null` for users with no role. `canChange` is `false` when the caller is a plain Administrator and the user holds a built-in role (UI hint only; the server re-checks). Soft-deleted users never appear.

### `PUT /admin/role-assignments/{userId}`

```json
{ "roleId": "…", "expectedCurrentRoleId": "…" }
```

`roleId: null` removes the role. `expectedCurrentRoleId: null` means "I saw no role".
`204` · `403` privileged-role rule / last Super User · `404` user or role · `409` user's current role ≠ `expectedCurrentRoleId` · `400` target locked.

### `POST /admin/role-assignments/actions/bulk-assign`

```json
{ "roleId": "…", "userIds": ["…", "…"] }
```

1–100 distinct ids. Permitted changes commit together; disallowed ones are skipped (FR-019).

`200`

```json
{
  "assignedCount": 4,
  "skipped": [
    { "userId": "…", "reason": "PrivilegedRoleRequiresSuperUser" },
    { "userId": "…", "reason": "UserLocked" }
  ]
}
```

`reason` ∈ `PrivilegedRoleRequiresSuperUser`, `LastSuperUser`, `UserLocked`, `UserNotFound`, `AlreadyAssigned`.

---

## 4. Changes to existing endpoints

| Endpoint | Change | Compatibility |
|---|---|---|
| `GET /auth/session` | Response adds `permissions: string[]` (effective permission keys). | Additive |
| `GET /users` | `role` may now be any role name (custom roles), still `"Regular"` for no role. Authorization → `users.view`. | Additive value range |
| `PATCH /users/{userId}/role` | Body `role` accepts any existing role name or `"Regular"`; delegates to the same assignment command. Marked **deprecated** in OpenAPI in favour of `PUT /admin/role-assignments/{userId}`. Still `AdministratorOrSuperUser`. | Backward compatible |
| **new** `PUT /admin/ai/providers/{id}/default-model` | `{ "defaultModelId": "…" \| null }` → `204`. Requires `admin.default-models.manage`. | New |
| `PATCH /admin/ai/providers/{id}` | `defaultModelId`/`clearDefaultModel` marked deprecated; when present, additionally requires `admin.default-models.manage`. | Backward compatible |
| All catalogue-area admin endpoints | `AdministratorOrSuperUser` → `[RequirePermission]` per [research.md Decision 5](../research.md#decision-5--area--endpoint-mapping-resolves-f7). Built-in roles hold every permission, so their access is unchanged. | No change for existing callers |

## 5. Authorization-denial behaviour

A request failing a permission requirement returns `403` Problem Details (`type: …/forbidden`, no detail about which permission, to avoid catalogue probing by non-admins) and writes one `AuthorizationDenied` audit row (FR-024).
