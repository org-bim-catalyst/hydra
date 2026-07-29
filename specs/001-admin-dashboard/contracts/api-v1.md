# API Contract Additions: `/api/v1/admin`, `/api/v1/users` (Admin Dashboard & User Management)

Additive to `specs/000-legacy-modernization/contracts/api-v1.md`'s `/api/v1/users` section — that contract's `GET /api/v1/users` and `PATCH /api/v1/users/{id}` are unchanged in shape (still `AdministratorOrSuperUser`-gated, still DTO-projected). Everything below is new. All error responses use the existing Problem Details format (see the base contract); all endpoints require the `AdministratorOrSuperUser` policy unless noted.

## Dashboard

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/v1/admin/dashboard/summary` | Administrator/Super User | Returns `DashboardSummaryDto` (`data-model.md`) — total users, 30-day daily signup trend, active/locked split, confirmed/pending split, 2FA adoption count, role distribution. |

```json
{
  "totalUsers": 42,
  "newUsersLast30Days": [ { "date": "2026-06-29", "newUsers": 0 }, { "date": "2026-06-30", "newUsers": 2 } ],
  "activeUsers": 39,
  "lockedOutUsers": 3,
  "emailConfirmedUsers": 40,
  "emailPendingUsers": 2,
  "twoFactorEnabledUsers": 11,
  "roleDistribution": [
    { "roleName": "Super User", "userCount": 1 },
    { "roleName": "Administrator", "userCount": 2 },
    { "roleName": "Regular", "userCount": 39 }
  ]
}
```

## User Management (evolves the existing `GET /api/v1/users`)

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/v1/users?search=&sortBy=&sortDescending=&page=&pageSize=` | Administrator/Super User | Replaces the unpaginated legacy-migration listing. `search` matches partial email or name (server-side, parameterized). `sortBy` ∈ `{email, createdAtUtc}` (extensible). Returns `PagedResult<UserAdminDto>`. |
| POST | `/api/v1/users/{id}/actions/lock` | Administrator/Super User | Locks the target account (FR-012). Non-CRUD verb modeled as a sub-resource action per constitution §6. `403` if `id` is the caller's own id (FR-022). |
| POST | `/api/v1/users/{id}/actions/unlock` | Administrator/Super User | Unlocks the target account (FR-013). |
| PATCH | `/api/v1/users/{id}/role` | Administrator/Super User | Body: `{ "role": "Administrator" | "Super User" | "Regular" }`. Maps cleanly to a resource update, so `PATCH .../role` (not an `/actions/` verb) per constitution §6's "verbs live in HTTP methods, not URLs" for CRUD-shaped operations. `403` if the caller is a plain Administrator attempting to grant/revoke `Administrator`/`Super User` (Clarifications, `research.md` Topic 4). |
| POST | `/api/v1/users/{id}/actions/force-2fa-reset` | Administrator/Super User | Force-clears the target's TOTP enrollment (FR-015). `403` if `id` is the caller's own id (FR-022). |
| DELETE | `/api/v1/users/{id}` | Administrator/Super User | Soft-deletes the target account (FR-016). `403` if `id` is the caller's own id (FR-022). |

```json
// GET /api/v1/users?search=jane&page=1&pageSize=20 → 200
{
  "items": [
    { "id": "...", "email": "jane@example.com", "firstName": "Jane", "lastName": "Doe", "emailConfirmed": true, "twoFactorEnabled": false, "lockoutEnabled": false }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 20
}
```

```json
// 403 shape for every self-action / privilege-escalation rejection above (existing Problem Details format)
{
  "type": "https://asklucy.io/problems/insufficient-privilege",
  "title": "Insufficient privilege",
  "status": 403,
  "detail": "Only a Super User can grant or revoke the Administrator or Super User role.",
  "traceId": "00-4bf9...-01"
}
```

## Explicitly not part of this contract

No bulk import/export endpoints, no audit-log query endpoint, no new role-management endpoints beyond the single `PATCH .../role` action — per the feature's stated Non-Goals.
