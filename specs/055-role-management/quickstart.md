# Quickstart: Validating Role Management

**Feature**: `055-role-management` — proves the spec's user stories end-to-end. Contracts: [contracts/admin-roles-api.md](./contracts/admin-roles-api.md). Model: [data-model.md](./data-model.md).

## Prerequisites

- Local SQL Server connection in `appsettings.Development.json`; `PERSISTENCE_TESTS_CONNECTION_STRING` set for Web/Persistence tests (without it almost every Web test fails at Hangfire startup).
- Three accounts: **SU** (Super User), **AD** (Administrator), **U1…U5** (no role).

## 0. Pre-migration data check (run against prod before deploying)

```sql
SELECT UserId, COUNT(*) AS Roles FROM AspNetUserRoles GROUP BY UserId HAVING COUNT(*) > 1;
SELECT Id, Name, NormalizedName FROM AspNetRoles;
```

Expected: the first query lists only accounts the migration will de-duplicate (keeps Super User); the second shows `Administrator` and `Super User`. Anything unexpected → stop and review before migrating.

## 1. Build, migrate, test

```powershell
dotnet ef database update --project src/AskLucy.Persistence --startup-project src/AskLucy.Web
dotnet test tests/AskLucy.Domain.Tests
dotnet test tests/AskLucy.Application.Tests
dotnet test tests/AskLucy.Persistence.Tests
dotnet test tests/AskLucy.Web.Tests
cd src/AskLucy.Web/ClientApp; npx tsc -b --noEmit; npx vitest run
```

Expected: all pass, including the existing `RoleAuthorizationTests`, `ChangeUserRoleTests`, `LastSuperUserGuardTests` **unchanged in intent** (SC-007), and the permission coverage test asserting every `/api/v1/admin/*` and admin `UsersController` action carries a permission or the reserved policy (SC-010). Run the **full** frontend suite — page-level tests assert independently of component tests.

## 2. Scenarios (run the app, sign in as indicated)

| # | As | Do | Expect | Spec |
|---|---|---|---|---|
| S1 | U1 | Open `/admin/dashboard` | Redirected to `/studio`; `GET /api/v1/admin/permissions` → 403 | FR-002 |
| S2 | AD | Admin panel nav | Shows **Permissions**, **Roles**, **Role assignments** plus all existing sections | FR-001 |
| S3 | AD | Roles → create "Viewer" with *View dashboard* + *View users* | Listed, 0 users, summary shows 2 permissions | US1-AS1 |
| S4 | AD | Create "viewer " | Inline "name already taken" | US1-AS2 |
| S5 | AD | Create "Mod" with *Manage MCP servers* only | Saved with *View MCP servers* added automatically | FR-004 |
| S6 | AD | Try editing/deleting **Administrator** | No actions offered; `PUT /admin/roles/{id}` → 403 | US1-AS3 |
| S7 | AD | Permissions → filter area *MCP servers* | *Manage MCP servers* lists Super User, Administrator, Mod; clicking Mod opens it on Roles | US3 |
| S8 | AD | Role assignments → U1 → assign "Viewer" | Row shows Viewer immediately | US2-AS1 |
| S9 | U1 (already signed in) | Next navigation | "Admin panel" appears in account menu; nav shows only Dashboard + Users; Users page has no lock/delete/edit actions; `POST /users/{id}/actions/lock` → 403 and an `AuthorizationDenied` row exists | FR-005, FR-018, FR-024 |
| S10 | U1 | Open `/admin/mcp-servers` directly | Redirected; API → 403 | FR-005 |
| S11 | AD | Assign "Mod" to U1 | Replaces Viewer — U1 holds exactly one role (`SELECT COUNT(*) FROM AspNetUserRoles WHERE UserId=@u1` = 1) | US2-AS2, FR-012 |
| S12 | AD | Edit "Mod": remove *Manage MCP servers* | U1's next MCP write → 403, no re-login | US1-AS6 |
| S13 | AD | Try assigning **Administrator** to U2; try changing SU's role | Not offered; direct `PUT` → 403 | US2-AS4, FR-016 |
| S14 | SU (only Super User) | Change own role to Administrator | Rejected: last Super User | US2-AS5, FR-017 |
| S15 | AD | Bulk-assign "Viewer" to U2…U5 + SU | 4 assigned; SU skipped with `PrivilegedRoleRequiresSuperUser` shown in summary | US4, FR-019 |
| S16 | AD in two tabs | Edit "Viewer" in both, save both | Second save → "changed by someone else, reload" | FR-022 |
| S17 | AD | Delete "Viewer" (held by 4) | Confirmation says 4 users will have no role; afterwards they have none | US1-AS5, FR-007 |
| S18 | AD | Lock U5, open assignment picker | U5 not offered as a target | FR-020 |
| S19 | AD | Kill the API mid-save (or block the request in devtools) on each screen | Visible error with Retry; no silent failure | FR-025 |
| S20 | SU | Hangfire dashboard, org document dashboard, AI rate-limit tier | Unchanged for SU/AD; unavailable to a "Mod" holder | FR-009 |
| S21 | — | `SELECT Action, ActorUserId, TargetRoleName, TargetUserId, DetailsJson FROM RoleAuditLogs ORDER BY OccurredAtUtc` | One row per create/update/delete/assign/change/remove/denial above, with before/after | FR-023, SC-004 |

## 3. Accessibility & theming

Run the new pages' `*.a11y.test.tsx` (axe). Manually: keyboard-only through create role → pick permissions → save → assign; toggle light/dark; resize to 400px wide.

## 4. Performance spot-check (SC-008)

With 10,000 users and 200 roles seeded, `GET /admin/role-assignments?search=a` and `GET /admin/roles` each return in < 2 s on the target host; check the SQL plan for the role-assignment search uses `IX_AspNetUserRoles_UserId`.
