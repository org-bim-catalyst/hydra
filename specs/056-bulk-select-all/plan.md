# Implementation Plan: Bulk Select-All on Admin List Screens

**Branch**: `056-bulk-select-all` | **Date**: 2026-09-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/056-bulk-select-all/spec.md`

## Summary

Add "select all" (page-scoped) bulk selection to the Users, Roles, and Role assignments admin screens, each driving its own bulk action(s) — Users gets three (Lock/Unlock, Force 2FA reset, Delete), Roles gets one (Delete), Role assignments gets one (Assign role, the specs/055-role-management User Story 4 flow this project never actually built). A shared confirmation step shows both "selected on this page" and "eligible across every matching page" counts, letting the admin expand to the full result set instead of visiting every page.

Technical approach ([research.md](./research.md)):

- Four new Users commands mirror the four existing single-target ones exactly (same guards, same audit log), rather than one polymorphic "bulk user action" command (*D5*).
- One new `BulkDeleteRolesCommand` and one new `BulkAssignRoleCommand` — the latter's repository method (`IRoleAssignmentRepository.BulkReplaceRoleAsync`) already exists from prior work; only the command/controller/UI are new (*D5*, research F2).
- A shared `BulkActionOutcome`/`BulkActionSkip` result pair replaces having four-plus bespoke result shapes (*D2*).
- "All matching" resolves ids server-side via one new unbounded `ListEligibleIdsAsync` query per area at execution time — never trusts a client-cached count (*D3*, *D4*).
- **Correction to spec.md FR-002**: no existing rule stops a plain Administrator from lock/unlock/2FA-reset/delete-ing another Administrator or Super User today; bulk eligibility for these four actions matches that (self-exclusion + last-Super-User guard only), not the privileged-role shield spec.md's parenthetical assumed (*D1* — documented, not silently reinterpreted).
- Frontend: one shared `useBulkSelection` hook and one shared `BulkActionConfirmDialog`, used by all three pages (*D6*, *D7*).

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend); TypeScript 5 / React 19 (frontend) — existing stack, no change.

**Primary Dependencies**: MediatR, FluentValidation, EF Core — all already in use. No new packages.

**Storage**: SQL Server — **no schema change**. Reuses existing tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`) via new repository query methods only.

**Testing**: xUnit + NSubstitute (Application handler tests per new command/query), Web.Tests (`WebApplicationFactory` authorization-boundary tests, matching the existing no-live-DB pattern from specs/055); Vitest + RTL + axe for the shared hook/dialog and each page's integration.

**Target Platform**: Existing ASP.NET Core Web API + React SPA.

**Project Type**: Web application, same layout as specs/055.

**Performance Goals**: SC-002 — 500 eligible rows resolved and acted on without the client visiting a second page; the unbounded `ListEligibleIdsAsync` queries select only ids (no wide row data), bounded by realistic admin-panel scale (thousands, not millions, of users/roles).

**Constraints**: No new authorization surface — every bulk endpoint reuses its screen's existing policy/permission. No change to any existing single-row endpoint or command. Audit trail parity: a bulk action must produce the same per-row audit entries the single-row action already does, not a coarser "bulk operation" log line.

**Scale/Scope**: 3 new Application queries (`Get<Area>EligibleIdsQuery`), 6 new commands (4 Users + 1 Roles + 1 Assignments), 3 new repository methods, 7 new controller endpoints, 1 shared result-shape record pair, 1 shared frontend hook + 1 shared dialog component, changes to all three existing admin pages' tables (checkbox column + toolbar).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|---|---|---|
| **§3 Dependency Rule / CQRS** | PASS | New queries/commands in Application, one handler each; repositories implement new interface methods in Persistence; controllers depend only on `ISender`. |
| **§3 Repository & UoW** | PASS | `ListEligibleIdsAsync` is aggregate-oriented (ids only, no leaky `IQueryable` exposure); each bulk command's per-id work reuses the existing single-target repository calls unchanged. |
| **Principle III (DRY/YAGNI)** | PASS | Shared `BulkActionOutcome` (*D2*) and shared frontend hook/dialog (*D6*) instead of N duplicated shapes/components; no bulk action invented beyond what spec.md and its Q&A actually asked for. |
| **Principle VIII / §4 No silent failures** | PASS | Every bulk command returns a per-id outcome; the frontend result summary renders every skip with its reason — matches constitution's explicit "no silent failures" requirement exactly as spec.md FR-009/SC-004 demand. |
| **§5 Concurrency** | PASS | "All matching" ids are re-resolved at execution time (not trusted from the confirmation dialog's earlier read), so a row that changed in between is simply excluded from that run, not silently mis-applied — addresses spec.md's race edge case without needing per-row concurrency tokens (bulk actions here are idempotent-ish state-sets, not edits with a stale-value risk). |
| **§6 REST / Problem Details / rate limiting** | PASS | New endpoints are `/actions/` sub-resources per existing convention; reuse `admin-endpoints` rate-limit policy; `200` with a per-row outcome for partial success (matches existing `BulkAssignResult`/`ApplyProviderModelSyncResult` precedent), `400` only for a malformed request. |
| **§6 AuthN/AuthZ** | PASS | No new policy — Users bulk actions require `admin.users.manage` (already used by the single-row actions post specs/055); Roles/Role-assignments bulk actions stay on the reserved `AdministratorOrSuperUser` policy, matching their screens' existing controllers exactly. |
| **§7 UI** | PASS | Checkbox column + toolbar pattern via MUI `Checkbox`/`Toolbar`, matching existing table conventions; shared hook/dialog justified by 3 consumers (§7's "≥2 features" bar). |
| **§8 Security** | PASS | No privilege change: bulk eligibility is exactly as permissive/restrictive as the single-row action it batches (*D1*) — corrected in research.md rather than silently over- or under-scoping. |
| **§10 Testing** | PASS | Unit tests per new command/query; authorization-boundary Web tests per new endpoint (no-live-DB style, matching specs/055's own tests); frontend tests for the shared hook/dialog plus each page. |
| **AI Coding Agent Rules (§18)** | PASS | FR-002's mismatch with actual system behavior is surfaced explicitly (research.md Decision 1, this plan's Summary) rather than silently picked one way — the user can override if the stricter reading was actually intended. |

No violations requiring justification — Complexity Tracking table is empty.

**Post-Phase-1 re-check**: data-model.md and contracts/bulk-actions-api.md introduce nothing beyond what the gates above already cover (no schema change, no new authz surface, one shared result shape). **Gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/056-bulk-select-all/
├── spec.md
├── plan.md                      # this file
├── research.md                  # Phase 0
├── data-model.md                # Phase 1
├── quickstart.md                # Phase 1
├── contracts/
│   └── bulk-actions-api.md      # Phase 1
├── checklists/requirements.md
└── tasks.md                     # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/AskLucy.Application/
├── Common/BulkActionOutcome.cs                          # BulkActionSkip + BulkActionOutcome (shared, D2)
├── Users/
│   ├── UserBulkAction.cs                                 # enum: Lock, Unlock, ForceReset2fa, Delete
│   ├── Queries/GetUsersEligibleIds/{Query,Handler}.cs
│   └── Commands/
│       ├── BulkLockUsers/{Command,Handler,Validator}.cs
│       ├── BulkUnlockUsers/{Command,Handler,Validator}.cs
│       ├── BulkForceReset2fa/{Command,Handler,Validator}.cs
│       └── BulkDeleteUsers/{Command,Handler,Validator}.cs
└── Authorization/
    ├── Roles/
    │   ├── Queries/GetRolesEligibleIds/{Query,Handler}.cs
    │   └── Commands/BulkDeleteRoles/{Command,Handler,Validator}.cs
    └── Assignments/
        ├── Queries/GetRoleAssignmentsEligibleIds/{Query,Handler}.cs
        └── Commands/BulkAssignRole/{Command,Handler,Validator}.cs   # specs/055 US4, built here

src/AskLucy.Application/Abstractions/
├── IUserAdminRepository.cs        # + ListEligibleIdsAsync
├── IRoleRepository.cs             # + ListEligibleIdsAsync
└── IRoleAssignmentRepository.cs   # + ListEligibleIdsAsync (BulkReplaceRoleAsync already exists)

src/AskLucy.Persistence/Repositories/
├── UserAdminRepository.cs         # + ListEligibleIdsAsync
├── RoleRepository.cs              # + ListEligibleIdsAsync
└── RoleAssignmentRepository.cs    # + ListEligibleIdsAsync

src/AskLucy.Web/
├── Contracts/BulkActionContracts.cs   # BulkTargetRequest, BulkActionResultResponse, BulkActionSkipResponse
├── Controllers/v1/
│   ├── UsersController.cs             # + 4 bulk-action endpoints + 1 eligible-ids endpoint
│   ├── AdminRolesController.cs        # + bulk-delete + eligible-ids endpoints
│   └── AdminRoleAssignmentsController.cs  # + bulk-assign + eligible-ids endpoints

src/AskLucy.Web/ClientApp/src/
├── features/admin/hooks/useBulkSelection.ts             # shared (D6), + test
├── features/admin/components/BulkActionConfirmDialog.tsx # shared (D6), + test
├── features/admin/api/adminApi.ts                        # + bulk endpoints for Users
├── features/admin/api/adminRolesApi.ts                    # + bulk endpoints for Roles/Assignments
├── features/admin/pages/AdminUsersPage.tsx                 # + checkbox column, toolbar, 3 actions
├── features/admin/pages/AdminRolesPage.tsx                  # + checkbox column, toolbar, delete action
└── features/admin/pages/AdminRoleAssignmentsPage.tsx         # + checkbox column, toolbar, assign action

tests/
├── AskLucy.Application.Tests/
│   ├── Users/{BulkLockUsers,BulkUnlockUsers,BulkForceReset2fa,BulkDeleteUsers}CommandHandlerTests.cs
│   ├── Users/GetUsersEligibleIdsQueryHandlerTests.cs
│   └── Authorization/{BulkDeleteRolesCommandHandlerTests,BulkAssignRoleCommandHandlerTests,GetRolesEligibleIdsQueryHandlerTests,GetRoleAssignmentsEligibleIdsQueryHandlerTests}.cs
└── AskLucy.Web.Tests/
    ├── Users/BulkUserActionsTests.cs
    └── Admin/{AdminRolesBulkDeleteTests,AdminRoleAssignmentsBulkAssignTests}.cs
```

**Structure Decision**: Existing Clean Architecture layout, additive vertical slices per area following the exact pattern specs/055 already established (query/command → repository method → controller endpoint → frontend page), plus one small shared frontend module (hook + dialog) justified by three consumers.

## Complexity Tracking

*No violations — table intentionally empty. The one deliberate deviation from a literal reading of the spec (Decision 1, Users bulk-action eligibility) is a correction against discovered actual system behavior, not an added architectural complexity, and is called out above rather than hidden.*
