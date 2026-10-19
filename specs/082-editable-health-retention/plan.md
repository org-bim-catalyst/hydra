# Implementation Plan: Editable Provider Health-Check Retention

**Branch**: `082-editable-health-retention` | **Date**: 2026-10-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/082-editable-health-retention/spec.md`

## Summary

Move the provider health-check retention window from deployment configuration into a stored, editable singleton setting while keeping `ProviderHealthCheck:RetentionDays` as the fallback. The existing Admin **Provider health checks** page gains an edit control backed by an `If-Match`-guarded GET/PUT pair; every successful change writes an append-only audit row. The daily retention job reads the stored value and falls back to configuration when the row is missing.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (backend); TypeScript 5.x / React 19 (frontend)

**Primary Dependencies**: ASP.NET Core, MediatR (CQRS), FluentValidation, EF Core 10, TanStack Query, MUI

**Storage**: SQL Server — new singleton table `ProviderHealthRetentionSettings` and append-only `ProviderHealthRetentionAuditLogs`

**Testing**: xUnit + FluentAssertions + NSubstitute (backend unit/integration), `CustomWebApplicationFactory` for endpoint tests, Vitest + Testing Library + MSW (frontend)

**Target Platform**: ASP.NET Core web API + React SPA (browser)

**Project Type**: Web application (Clean Architecture backend + React client)

**Performance Goals**: No observable page-load change (one singleton read); the daily job performs one extra singleton read per run

**Constraints**: Optimistic concurrency via `If-Match` base64 row version; 1–365 day range; stored value wins over configuration; no redeployment required for a change; all errors surfaced as Problem Details / visible UI

**Scale/Scope**: One setting row, a handful of audit rows per year; no change to health-check volume or the deletion query

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Result |
|-----------|-------|--------|
| §2 I Clean Architecture | Domain entity has no outer dependencies; command/query handlers in Application; EF mapping in Persistence; job in Infrastructure; controller in Web | PASS — requires moving `ProviderHealthCheckOptions` from Infrastructure to Application so the handler can read the fallback without an outward dependency |
| §2 II SRP | Setting entity, audit entity, validator, handler, job each have one reason to change | PASS |
| §3 CQRS / one handler | `GetProviderHealthRetentionQuery` + `UpdateProviderHealthRetentionCommand`, one handler each | PASS |
| §3 Repository / UoW | One `IProviderHealthRetentionRepository`; a single `SaveChangesAsync` per command | PASS |
| §5 Database | `BaseEntity` audit columns + `RowVersion`; one reversible migration; seed via `HasData` | PASS |
| §6 Concurrency | Stale row version rejected as 409 with a caller-visible message; never left to bubble as 500 | PASS |
| §7 API | RFC 7807 Problem Details; explicit 400/403/409/428 responses; existing `admin-endpoints` rate limit; OpenAPI attributes | PASS |
| §9 Error handling | No silent failure: validation, conflict and save errors reach the admin UI | PASS |
| §10 Frontend | TanStack Query for read/mutation; MUI components; permission-gated affordance | PASS |
| §11 i18n | The Provider health page is not a localized surface today; new strings follow the existing page's convention. No new namespace is introduced | PASS |
| Security | GET requires `admin.operational-failures.view`; PUT requires `admin.operational-failures.manage`; input validated; change audited | PASS |
| Testing | Domain + Application unit tests, Infrastructure job tests, endpoint tests, updated frontend tests | PASS |

No violations requiring justification.

## Project Structure

### Documentation (this feature)

```text
specs/082-editable-health-retention/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── admin-provider-health-retention-api.md
├── checklists/
│   └── requirements.md
└── tasks.md              # created later by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── AskLucy.Domain/
│   └── Ai/
│       ├── ProviderHealthRetentionSetting.cs      # new singleton entity
│       └── ProviderHealthRetentionAuditLog.cs     # new append-only audit entity
├── AskLucy.Application/
│   ├── Abstractions/
│   │   └── IProviderHealthRetentionRepository.cs  # new
│   └── Ai/
│       ├── ProviderHealthCheckOptions.cs          # moved from Infrastructure/Ai
│       ├── ProviderHealthRetentionDtos.cs         # new DTO + source enum
│       ├── Commands/UpdateProviderHealthRetention/{Command,Handler,Validator}.cs
│       └── Queries/GetProviderHealthRetention/{Query,Handler}.cs
├── AskLucy.Infrastructure/
│   └── Ai/
│       ├── ProviderHealthCheckRetentionJob.cs     # reads stored value, falls back
│       ├── ProviderHealthCheckHostedService.cs    # import update only
│       └── DependencyInjection.cs                 # options namespace/import update
├── AskLucy.Persistence/
│   ├── AskLucyDbContext.cs                        # two new DbSets
│   ├── Configurations/
│   │   ├── ProviderHealthRetentionSettingConfiguration.cs   # new (HasData seed)
│   │   └── ProviderHealthRetentionAuditLogConfiguration.cs  # new
│   ├── Repositories/ProviderHealthRetentionRepository.cs    # new
│   ├── DependencyInjection.cs                     # repository registration
│   └── Migrations/                                # new migration + snapshot
└── AskLucy.Web/
    ├── Controllers/v1/AdminProviderHealthRetentionController.cs  # GET extended + PUT
    ├── Contracts/ProviderHealthRetentionContracts.cs            # PUT request
    └── ClientApp/src/features/admin/providerHealth/
        ├── providerHealthRetentionApi.ts          # DTO fields + update function
        ├── AdminProviderHealthRetentionPage.tsx   # edit form
        └── AdminProviderHealthRetentionPage.test.tsx

tests/
├── AskLucy.Domain.Tests/Ai/ProviderHealthRetentionSettingTests.cs
├── AskLucy.Application.Tests/Ai/
│   ├── GetProviderHealthRetentionQueryHandlerTests.cs
│   └── UpdateProviderHealthRetentionCommandHandlerTests.cs
├── AskLucy.Infrastructure.Tests/Ai/ProviderHealthCheckRetentionJobTests.cs
└── AskLucy.Web.Tests/OperationalFailures/AdminProviderHealthRetentionEndpointTests.cs
```

**Structure Decision**: The existing solution layout is reused unchanged. Provider-health domain types live under `Domain/Ai` next to `ProviderHealthCheck`; application handlers follow the `Application/Ai` feature folder already used for AI administration; the Web controller and page are extended in place rather than replaced.

## Complexity Tracking

No constitution violations. No additional projects, abstractions or dependencies are introduced.
