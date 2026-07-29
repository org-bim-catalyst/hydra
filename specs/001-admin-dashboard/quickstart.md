# Quickstart: Validating the Admin Dashboard & User Management Console

Validation guide, not an implementation guide — proves `spec.md`'s Success Criteria end-to-end. See `data-model.md` for entity/DTO details and `contracts/api-v1.md` for endpoint shapes.

## Prerequisites

- Everything from `specs/000-legacy-modernization/quickstart.md` already stood up (this feature is additive to that migration).
- At least two admin-capable seed accounts for role-hierarchy testing: one `Super User`, one plain `Administrator` (the existing `DevAdminSeeder` seeds a single `Administrator` today — add a second seed or promote one manually for this feature's manual testing).
- A handful of regular test users at varying states (locked, unconfirmed email, 2FA enabled) to populate meaningful dashboard metrics.

## 1. Apply the new migration

```bash
dotnet ef database update --project src/AskLucy.Persistence --startup-project src/AskLucy.Web
```

Expected: `AspNetUsers` gains `CreatedAtUtc`, `IsDeleted`, `DeletedAtUtc`, `DeletedBy`; existing rows are backfilled with a `CreatedAtUtc` of the migration-apply date (`research.md` Topic 1).

## 2. Validate the dashboard (User Story 1, SC-001/SC-006)

1. Log in as an Administrator/Super User, navigate to `/admin/dashboard`.
2. Confirm all six metrics render (total users, 30-day daily trend, active/locked split, confirmed/pending split, 2FA adoption, role distribution) and match `GET /api/v1/admin/dashboard/summary`'s response.
3. Resize the browser to a mobile width — confirm charts remain legible (SC-006).
4. Toggle light/dark theme — confirm chart colors follow the theme, not hardcoded values.
5. As a non-admin authenticated user, request `GET /api/v1/admin/dashboard/summary` directly — expect `403`.

## 3. Validate user management actions (User Story 2, SC-002/SC-003/SC-004)

1. As an Administrator, lock a test user's account (`POST /api/v1/users/{id}/actions/lock`) — confirm that user's next login attempt fails with a lockout result.
2. Unlock the same account — confirm login succeeds again.
3. As an Administrator (not Super User), attempt `PATCH /api/v1/users/{id}/role` with `{ "role": "Administrator" }` on any user — expect `403` (Clarifications).
4. As a Super User, repeat step 3 — expect success; confirm the target's effective permissions reflect the change on their next request.
5. Force a 2FA reset on a user who has TOTP enabled — confirm their `twoFactorEnabled` flag clears and they're prompted to re-enroll on next login requiring 2FA.
6. Soft-delete a test user — confirm they disappear from `GET /api/v1/users` and can no longer log in, while the underlying row still exists in the database (query it directly, bypassing the app, to confirm it wasn't hard-deleted).
7. As an Administrator, attempt to lock/2FA-reset/delete **your own currently-authenticated account** — expect `403` for all three (FR-022).
8. Submit a role-change or lock/unlock request with an extra unexpected field in the body — confirm it's ignored/rejected, never persisted (SC-004).

## 4. Validate search/sort/pagination (User Story 3, SC-005)

1. With more than one page of users seeded, call `GET /api/v1/users?search=<partial-email>` — confirm only matching users return.
2. Call with `sortBy=createdAtUtc&sortDescending=true` — confirm ordering.
3. Call with `page=2&pageSize=10` — confirm `totalCount` is accurate and no user appears on two pages.

## 5. Run automated tests

```bash
dotnet test                     # unit + integration, including new Admin/UserManagement suites
cd frontend && npm run test     # Vitest + RTL, including new admin dashboard/chart component tests
```

Expected: all suites pass, including negative-authorization tests for every new endpoint (non-admin → `403`; plain Administrator → `403` on privileged role changes; self-action → `403`).
