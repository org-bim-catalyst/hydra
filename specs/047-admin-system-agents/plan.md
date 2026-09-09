# Implementation Plan: Admin Visibility of System Agents

**Branch**: `047-admin-system-agents` | **Date**: 2026-09-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/047-admin-system-agents/spec.md`

## Summary

Add a read-only admin screen that lists every system-owned `Agent` (`IsSystemOwned == true`,
`OwnerId == Agent.SystemOwnerId`) with its name, status, current published version number, and
last-updated timestamp — visible only to Administrator/Super User roles. The existing personal
Agents page and its `ListAgentsQuery` (owner-scoped to the caller) are untouched; this is an
additive query + admin route, mirroring the existing `/admin/ai-providers` pattern exactly
(same authorization policy, same controller/query/repository layering, same admin nav/route
registration style).

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend), TypeScript 5 / React 19 (frontend) — matches the rest of the solution, no new stack introduced.

**Primary Dependencies**: MediatR (query), EF Core (repository read), MUI + TanStack Query (frontend) — all already in use by the `/admin/ai-providers` feature this mirrors.

**Storage**: SQL Server — reads the existing `Agents`/`AgentVersions` tables (specs/020, extended by specs/045 for system agents). No schema change.

**Testing**: xUnit (backend: repository + query handler + controller authorization tests), Vitest/RTL (frontend: page rendering + empty state), matching existing admin-feature test conventions.

**Target Platform**: ASP.NET Core Web API + React SPA (existing site4now.net deployment).

**Project Type**: Web application (existing `Domain`/`Application`/`Infrastructure`/`Persistence`/`Web` backend + `ClientApp` frontend).

**Performance Goals**: N/A beyond standard admin-page expectations — the list is expected to stay small (today: 5 system agents, per `SystemAgentDefinitions.All`); no pagination performance target.

**Constraints**: Must not alter `ListAgentsQuery`/`ListByOwnerAsync`'s existing owner-scoped behavior (FR-007). Must reuse the existing `AdministratorOrSuperUser` authorization policy already enforced on `AdminAiProvidersController` — no new role/policy introduced.

**Scale/Scope**: One new query, one new repository method, one new controller endpoint, one new DTO, one new admin page + nav/route entry. No new domain behavior — the `Agent`/`AgentVersion` entities already carry every field needed (`IsSystemOwned`, `Status`, `PublishedVersionNumber`, `Versions`, `ModifiedAtUtc`/`CreatedAtUtc` from `BaseEntity`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **§3 Dependency Rule** — PASS. New query lives in `Application/Agents/Queries/`; new repository method added to the existing `IAgentRepository` interface (Application) and implemented in `Infrastructure`/`Persistence`; controller depends only on `ISender` (MediatR), consistent with every other admin controller. No layer depends outward.
- **§3 CQRS rules** — PASS. Pure read query (`GetSystemAgentsQuery : IRequest<IReadOnlyList<AdminSystemAgentDto>>`), no mutation, one handler.
- **§3 Repository & Unit of Work rules** — PASS. New method is aggregate-oriented (`ListSystemOwnedAsync`), not a leaky `IQueryable` escape hatch — matches `ListByOwnerAsync`'s existing shape.
- **§6 API Standards** — PASS. New endpoint `GET /api/v1/admin/agents/system` follows existing noun/plural, versioned, kebab conventions and lives under the same `/api/v1/admin/...` prefix as `AdminAiProvidersController`. Read-only list, small/bounded (≤ a handful of rows) — offset/no pagination is acceptable per §6's "small stable admin lists" carve-out, matching `GetAdminAiProvidersQuery`'s own precedent (also unpaginated).
- **§6 AuthN/AuthZ** — PASS. Reuses the existing `[Authorize(Policy = "AdministratorOrSuperUser")]` policy already applied to `AdminAiProvidersController`; no new policy needed (FR-005).
- **§7 UI Principles** — PASS. New page composed from existing MUI components/admin page shell (mirrors `AdminAiProvidersPage.tsx`), respects existing theming, no bespoke components introduced.
- **§10 Testing Standards** — Addressed in Phase 2 tasks: unit tests for the query handler (repository faked), a controller authorization test (anonymous/non-admin/admin), and a frontend render test including the empty state.
- **Principle III (YAGNI)** — PASS. No pagination, no filtering/sorting UI, no edit affordance — scope is exactly what the spec's 3 user stories ask for, nothing added speculatively.

No violations requiring justification — Complexity Tracking table is empty.

**Post-Phase-1 re-check**: `data-model.md` confirms no new entities/migrations (pure projection
over existing `Agent`/`AgentVersion` fields); `contracts/admin-system-agents-api.md` confirms the
single new endpoint reuses the existing `AdministratorOrSuperUser` policy and an unpaginated
small-list response, matching the gate's pre-design assumptions exactly. No new violations
introduced by the design phase.

## Project Structure

### Documentation (this feature)

```text
specs/047-admin-system-agents/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
└── tasks.md              # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/AskLucy.Application/Agents/Queries/GetSystemAgents/
├── GetSystemAgentsQuery.cs
├── GetSystemAgentsQueryHandler.cs
└── AdminSystemAgentDto.cs

src/AskLucy.Application/Abstractions/IAgentRepository.cs        # + ListSystemOwnedAsync method
src/AskLucy.Persistence/Repositories/AgentRepository.cs          # + implementation

src/AskLucy.Web/Controllers/v1/AdminAgentsController.cs          # new controller, GET /api/v1/admin/agents/system

src/AskLucy.Web/ClientApp/src/features/admin/
├── api/adminApi.ts                                              # + getSystemAgents()
├── pages/AdminSystemAgentsPage.tsx                               # new page
├── pages/AdminSystemAgentsPage.test.tsx
├── pages/AdminSystemAgentsPage.a11y.test.tsx
└── adminNav.tsx                                                  # + nav entry

src/AskLucy.Web/ClientApp/src/routes/router.tsx                  # + lazy route registration

tests/AskLucy.Application.Tests/Agents/Queries/GetSystemAgentsQueryHandlerTests.cs
tests/AskLucy.Web.Tests/Agents/AdminAgentsControllerTests.cs
tests/AskLucy.Infrastructure.Tests/Repositories/AgentRepositoryTests.cs   # + ListSystemOwnedAsync case
```

**Structure Decision**: Existing Clean Architecture web-application layout (`Domain`/`Application`/`Infrastructure`/`Persistence`/`Web` + `ClientApp`). This feature adds one vertical slice (query → repository method → controller → frontend page) following the exact structure already established by `/admin/ai-providers` (specs/007) — no new architectural pattern introduced.

## Complexity Tracking

*No violations — table intentionally empty.*
