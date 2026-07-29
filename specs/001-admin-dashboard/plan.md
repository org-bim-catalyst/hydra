# Implementation Plan: Admin Dashboard & User Management Console

**Branch**: `001-admin-dashboard` | **Date**: 2026-07-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-admin-dashboard/spec.md`

## Summary

Restore and modernize the legacy Control Panel as an additive feature on top of SPEC-000's minimal `/admin/users` read-only grid: a d3.js-charted Admin Dashboard (six platform-health metrics) and a full user-management console (search/sort/pagination plus lock/unlock, force 2FA reset, role change with a Super-User-only privilege-escalation guard, and soft-delete). Technical approach: extend the existing Clean Architecture `Users` vertical slice (new commands/queries, one new repository method group) rather than introducing a new bounded context, since every action operates on the existing `ApplicationUser`/role model; add three new columns to `ApplicationUser` to support soft-delete and the registration-trend chart (data that doesn't exist in the schema today, see `research.md` Topic 1); add `d3` as a new frontend dependency per the spec's explicit stakeholder-mandated charting constraint.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend, matches existing `src/AskLucy.*`); TypeScript (strict) / React 19 (frontend, matches existing `frontend/`)

**Primary Dependencies**: MediatR, FluentValidation, AutoMapper, EF Core 10, ASP.NET Core Identity (backend — all already referenced, no new backend packages); React 19, MUI, TanStack Query, Zustand, React Hook Form (frontend, already referenced) **plus new**: `d3` + `@types/d3` (frontend-only new dependency, per spec Assumptions/`research.md` Topic 6)

**Storage**: SQL Server via EF Core Code-First (existing `AskLucyDbContext`) — one new migration adding `CreatedAtUtc`/`IsDeleted`/`DeletedAtUtc`/`DeletedBy` to the existing `AspNetUsers` table; no new tables (`research.md` Topic 1, Topic 5)

**Testing**: xUnit (existing `tests/AskLucy.*.Tests` projects); Vitest + React Testing Library (existing `frontend`); Playwright (existing `tests/AskLucy.E2E.Tests`) — all existing harnesses, extended with new test files, no new tooling

**Target Platform**: Same ASP.NET Core WebAPI + React SPA, same `site4now.net` shared-hosting deployment target inherited from SPEC-000 — no infrastructure/hosting change

**Project Type**: Web application (existing frontend + backend solution) — this feature is additive within it, not a new project

**Performance Goals**: Dashboard summary query and paginated user list respond within normal interactive page-load expectations (sub-second) at the inherited <100-user scale; no new SLA beyond what the existing app already targets

**Constraints**: Dashboard charts MUST be built directly with d3.js, not a wrapper charting library (binding stakeholder constraint, spec.md § Assumptions); every admin action MUST be server-side authorized and MUST NOT accept unexpected fields (FR-018/FR-019, inherited from SPEC-000's mass-assignment closure); role-escalation guard MUST live in the Application layer, not a blanket policy (`research.md` Topic 4)

**Scale/Scope**: Fewer than 100 total registered users, low concurrency (inherited from SPEC-000's scale assumption) — sizes the pagination/aggregate-query approach in `research.md` Topic 5/7

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design below.*

| Principle / Rule | Assessment |
|---|---|
| §2.I Clean Architecture / Dependency Rule | **Pass.** New code slots into the existing layering: Application (`Users`/`Admin` commands/queries), Persistence (repository + EF configuration), WebAPI (controller actions) — no new project, no outward-pointing dependency. |
| §2.II–VII SOLID/Simplicity/Composition/DIP/SoC/Convention | **Pass.** Each new admin action is its own single-purpose command+handler (SRP), reusing existing `IIdentityService`/`IUserAdminRepository` seams (DIP) rather than duplicating Identity logic (`research.md` Topics 2–3). No new inheritance introduced. |
| §3 CQRS / Repository / DI rules | **Pass.** Every write is a MediatR command with one handler; `GetUsersQuery`/`GetAdminDashboardSummaryQuery` are read-only; repository methods are aggregate-oriented (`LockAsync`, `GetSummaryAsync`, etc.), not a leaky `IQueryable` escape hatch. |
| §4 Coding standards | **Pass.** Nullable reference types, structured Serilog logging (named properties, no string concatenation), `CancellationToken` propagated — matching existing sibling code (`UpdateUserCommandHandler` et al.) that this feature extends. |
| §5 Database Principles | **Pass, with a noted structural exception already accepted in SPEC-000.** `ApplicationUser` cannot inherit `BaseEntity` (conflicting `Id` types — `research.md` Topic 1), so its new audit-like columns are added directly rather than via the generic `AuditSaveChangesInterceptor`. This mirrors the same accepted exception SPEC-000 already made for `ApplicationUser` living outside the `BaseEntity` convention; not a new deviation introduced by this feature. Global soft-delete query filter, indexes on `CreatedAtUtc`/`IsDeleted`, and reuse of the existing `ConcurrencyStamp` token all follow §5 as written. |
| §6 API Standards | **Pass.** Versioned (`/api/v1/...`), Problem Details errors (reusing the existing middleware/format), offset pagination (explicitly acceptable for "small stable admin lists"), non-CRUD verbs (`lock`/`unlock`/`force-2fa-reset`) modeled as `/actions/...` sub-resources per §6's own example, role-change modeled as a plain `PATCH .../role` since it maps cleanly to a resource update. |
| §6 Rate limiting | **Resolved in-scope (updated post-`/speckit-analyze`).** §6 states every public endpoint is rate-limited; today only AI-invoking endpoints have a `RequireRateLimiting` policy, and the existing `GET/PATCH /api/v1/users` (from SPEC-000) lack one. Rather than carrying this gap forward a second time, this feature closes it: `tasks.md` T057a adds an `admin-endpoints` rate-limit policy (mirroring the existing `ai-endpoints` policy) applied to every endpoint this feature introduces. |
| §7 UI Principles | **Deviation, justified — see Complexity Tracking.** The "compose from the existing MUI theme before writing a bespoke component" default is intentionally overridden for charts only, per the spec's explicit d3.js constraint. Everything else (layout, non-chart components, theming, responsiveness, a11y) still uses MUI as normal. |
| §8 Security | **Pass, with one flagged pre-existing gap.** Self-lockout hard block, mass-assignment prevention, server-side role-gating, and DTO-only responses (no Identity secrets) are all satisfied. §8's "authorization denials... MUST be written to an immutable audit trail distinct from general application logs" is **not yet implemented anywhere in this codebase** for any feature (confirmed: no audit-log table/mechanism exists in `src/` today) — this feature logs admin actions via structured Serilog only (FR-021), consistent with the spec's explicit Non-Goal excluding audit-log infrastructure. This is a project-wide, pre-existing constitution gap, not something newly introduced here; see Complexity Tracking for the recommended resolution. |
| §10 Testing Standards | **Pass.** New unit tests (Application handlers, faked repositories), integration tests (EF Core query filter behavior, migration), contract tests (403 cases for every new endpoint — non-admin, self-action, plain-Administrator-vs-role-escalation), frontend component/a11y tests for charts — all using existing harnesses, planned in Phase 1/`tasks.md`. |

**Overall gate result**: Pass, with one explicitly justified deviation recorded in Complexity Tracking (d3.js-over-MUI for charts) and one accepted interim deviation, tracked via ADR (T057): Serilog-only audit logging pending a project-wide immutable-audit-trail initiative. The previously-flagged admin-endpoint rate-limiting gap is no longer deferred — it's resolved in-scope by T057a (added post-`/speckit-analyze`).

## Project Structure

### Documentation (this feature)

```text
specs/001-admin-dashboard/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── api-v1.md        # Phase 1 output — additive to specs/000-legacy-modernization/contracts/api-v1.md
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by this command)
```

### Source Code (repository root)

```text
src/AskLucy.Application/
├── Users/
│   ├── Commands/
│   │   ├── UpdateUser/                  # existing, unchanged
│   │   ├── LockUser/                    # new
│   │   ├── UnlockUser/                  # new
│   │   ├── ChangeUserRole/              # new — contains the Topic 4 privilege-escalation guard
│   │   ├── ForceReset2fa/               # new — delegates to existing IIdentityService.DisableTwoFactorAsync
│   │   └── DeleteUser/                  # new — soft delete
│   ├── Queries/
│   │   ├── GetUsers/                    # replaces GetAllUsers — adds search/sort/pagination
│   │   └── GetMyProfile/                # existing, unchanged
│   ├── PagedResult.cs                    # new, shared generic
│   └── UserAdminDto.cs                    # existing, unchanged shape
└── Admin/
    └── Queries/
        └── GetDashboardSummary/          # new
            ├── GetAdminDashboardSummaryQuery.cs
            └── GetAdminDashboardSummaryQueryHandler.cs

src/AskLucy.Application/Abstractions/
├── IIdentityService.cs                    # extended: SetLockoutAsync, ChangeRoleAsync (UserManager-mediated state)
├── IUserAdminRepository.cs                # extended: DeleteAsync, SearchAsync (plain-column state, matches existing UpdateAsync pattern)
└── IAdminDashboardRepository.cs           # new

src/AskLucy.Persistence/
├── Identity/ApplicationUser.cs            # extended: CreatedAtUtc, IsDeleted, DeletedAtUtc, DeletedBy
├── Identity/IdentityService.cs            # extended: SetLockoutAsync, ChangeRoleAsync
├── Configurations/ApplicationUserConfiguration.cs   # new — query filter + indexes
├── Repositories/UserAdminRepository.cs    # extended
├── Repositories/AdminDashboardRepository.cs  # new
└── Migrations/                             # new migration: add columns + indexes, backfill CreatedAtUtc

src/AskLucy.Web/Controllers/v1/
└── UsersController.cs                     # extended with new action routes; new AdminDashboardController.cs

tests/
├── AskLucy.Application.Tests/Users/        # new handler tests (lock/unlock/role/2fa-reset/delete, privilege guard)
├── AskLucy.Application.Tests/Admin/        # new dashboard summary handler test
├── AskLucy.Persistence.Tests/               # new: query filter excludes soft-deleted users; migration backfill
└── AskLucy.Web.Tests/Admin/              # extended RoleAuthorizationTests.cs + new self-action/privilege-escalation 403 tests

frontend/src/features/admin/
├── pages/AdminUsersPage.tsx                # extended: search/sort/pagination + row actions
├── pages/AdminDashboardPage.tsx             # new
├── charts/                                 # new — d3-based chart components
│   ├── NewUsersTrendChart.tsx
│   ├── RoleDistributionChart.tsx
│   └── StatusSplitChart.tsx                # active/locked and confirmed/pending, shared shape
├── api/adminApi.ts                          # new — dashboard summary + user-management action calls
├── hooks/useAdminDashboard.ts                # new
└── components/UserActionMenu.tsx             # new — lock/unlock/role/2fa-reset/delete row actions + confirm dialogs

frontend/src/routes/AdminRoute.tsx            # existing, unchanged — reused for the new /admin/dashboard route
```

**Structure Decision**: Extend the existing single-solution web-application layout (`src/AskLucy.*` Clean Architecture backend + `frontend/` React SPA) exactly as SPEC-000 established it. No new backend project, no new frontend package — this feature lives entirely inside the existing `Users` Application slice plus a small new `Admin` slice for the dashboard-only query, and inside the existing `frontend/src/features/admin` folder.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Dashboard charts use raw d3.js instead of composing from the existing MUI theme/component library (§7 default) | The feature's Input/Assumptions explicitly mandate d3.js by name as a binding stakeholder decision, not a technical default left open to planning | Using `@mui/x-charts` or a d3-wrapper library (`recharts`) would satisfy §7's default but directly contradict the spec's stated constraint — the spec's explicit instruction overrides the general default for this one feature only |
| Admin actions are logged via structured Serilog only, not a separate immutable audit-trail store (§8) | No immutable audit-trail mechanism exists anywhere in this codebase yet for *any* feature (confirmed by inspection) — building one is a cross-cutting, project-wide initiative out of proportion to this feature, and the spec's Non-Goals explicitly exclude audit-log infrastructure | Building a bespoke audit store just for this feature's five actions would duplicate effort once a real project-wide audit-trail initiative lands, and would leave authentication/authorization-denial events (already unaudited today) still uncovered — the gap is better tracked and closed holistically. Tracked via `docs/adr/00XX-interim-admin-security-controls.md` (`tasks.md` T057, matching SPEC-000's own precedent of documenting accepted deviations) as a known, accepted interim state |
