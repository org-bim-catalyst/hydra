# Implementation Plan: Admin AI Model Catalog Management

**Branch**: `008-ai-model-catalog-management` | **Date**: 2026-07-31 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/008-ai-model-catalog-management/spec.md`

## Summary

Administrators can enable providers and configure credentials (specs 005/007), but the
model catalog under each provider is a fixed, hand-seeded set with no way to curate it.
This plan adds: an admin view of a provider's full model catalog (any status), the
ability to manually change a model's status (Available/Deprecated/Unavailable), and a
"sync from provider" action that calls each provider's already-implemented
`ListAvailableModelsAsync()` and produces a reviewable diff — applied only on explicit
confirmation, per the two clarifications resolved in spec.md (diff compares against the
*entire* catalog regardless of status, so a deliberately-deprecated model is never
re-proposed; a newly-synced model is added as **Unavailable**, requiring a separate manual
step to activate). Unlike spec 007, this is a **full-stack** change — the backend
capability (beyond `ListAvailableModelsAsync()` itself) does not exist yet.

## Technical Context

**Language/Version**: C# / .NET 10 (backend, `AskLucy.Application`/`AskLucy.Web`); TypeScript 5 / React 19 (frontend, `AskLucy.Web/ClientApp`).

**Primary Dependencies**: MediatR + FluentValidation (existing CQRS pipeline), EF Core (existing `AIModels` table — no schema change), MUI (`Table`, `Collapse`, `Dialog`), TanStack Query.

**Storage**: SQL Server via EF Core, reusing the `AIModels` table and `IAIModelRepository`/`IAIProviderRepository` already delivered under `005-multi-provider-ai-engine` — no migration needed (`Status`, capability flags, and `Pricing` columns already exist).

**Testing**: xUnit + NSubstitute for the new Application handlers (matching `SendChatMessageCommandHandlerTests.cs`'s faked-dependency style); Vitest + React Testing Library for the new frontend pieces (matching `AiProviderActionsMenu.test.tsx`'s confirm-dialog assertion style).

**Target Platform**: Web — extends the existing Admin AI Providers page (spec 007); no new page/route.

**Project Type**: Web application — additive to both the existing `AskLucy.Application`/`AskLucy.Web` backend and the `features/admin` frontend slice.

**Performance Goals**: No new performance budget. The sync check is a single on-demand admin action making one call to `IAIProvider.ListAvailableModelsAsync()` — not a hot path, not called by end users.

**Constraints**: Admin-only (FR-009); the sync check (FR-006) MUST NOT mutate the catalog; applying a diff MUST NOT ever delete a model row (FR-008); pricing MUST NOT be fabricated when unknown (existing constraint from spec 005, restated by FR-001).

**Scale/Scope**: A few dozen models per provider at most (spec Assumptions) — no pagination/search needed within one provider's model list.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Assessment |
|---|---|
| I. Clean Architecture & Dependency Rule | PASS — new Commands/Queries in `Application/Ai`, controller additions in `Web`; Domain is unchanged (reuses `AIModel.SetStatus`/`Create` as-is). |
| III. CQRS | PASS — the sync *check* (FR-005/006) is modeled as a **Query** (it never mutates state) even though it's reached via a `POST .../actions/sync` route, consistent with constitution §3's "Queries MUST NOT mutate state" taking priority over REST-action-shape verb choice (§6 already models non-CRUD actions as `POST .../actions/x` regardless of CQRS classification). Applying a diff and changing a model's status are Commands. |
| V. Dependency Inversion & Testability | PASS — new handlers depend only on existing `IAIModelRepository`/`IAIProviderRepository`/`IAIProviderResolver` abstractions; fully unit-testable with fakes, no DB/network needed. |
| VI. Separation of Concerns | PASS — the diff-matching rule (FR-006's clarified "compare against the entire catalog" logic) lives in the query handler (Application), not the controller; the controller only translates HTTP↔MediatR. |
| VIII. No Silent Failures (NON-NEGOTIABLE) | PASS — a sync failure (vendor unreachable) surfaces via the *existing* `AiProviderUnavailableException`/`AiProviderAuthenticationException`/`AiProviderRateLimitedException` → Problem Details mapping (already wired in `ProblemDetailsMiddleware` since spec 005) — no new error-swallowing path introduced. Every frontend action (status change, sync, apply) surfaces visible success/error feedback, reusing `AiProviderActionsMenu.tsx`'s existing Snackbar/Alert mechanism. |
| §6 API Standards | PASS — reuses the existing `AdminAiProvidersController` (`[Authorize(Policy = "AdministratorOrSuperUser")]`, `admin-endpoints` rate limit), extended with routes already scoped out in spec 005's `contracts/admin.md` (`GET .../models`, `PATCH /admin/ai/models/{id}`, `POST .../models/actions/sync[/apply]`). |
| §5 Database Principles | PASS — no migration; `AIModel.SetStatus`/`Create` already exist and already carry `CreatedAtUtc`/`ModifiedAtUtc`/`ModifiedBy` via the existing `SaveChanges` interceptor. |
| §7 UI Principles | PASS — extends `AdminAiProvidersPage.tsx` with an MUI `Collapse`-based per-provider model table (no new page/route); every state-changing action reuses the confirm-dialog pattern already built in `AiProviderActionsMenu.tsx` for spec 007 (FR-010), including its local-`useState` dialog state — the same accepted pre-existing pattern spec 007's plan.md flagged against §7's literal "dialogs... live in Zustand stores" wording, not a fresh deviation introduced here. |
| §10 Testing | Design requirement — unit tests for the diff-computation handler (the one genuinely new piece of logic) and component tests for the new confirm-gated UI actions, in the same change. |

No violations. **Complexity Tracking is not needed.**

**Post-Phase 1 re-check**: data-model.md and contracts/admin-ai-models.md confirm the
design adds no new Domain concepts (only new Application/Web surface over the existing
`AIModel` entity) and no schema change — every gate above still holds unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/008-ai-model-catalog-management/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
│   └── admin-ai-models.md
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by this command)
```

### Source Code (repository root)

```text
src/AskLucy.Application/Ai/
├── AdminAiModelDto.cs                                  # NEW — admin view of one model (adds Status to the existing ModelSummaryDto shape)
├── Queries/GetAdminAiModels/                           # NEW — FR-001: every model for a provider, any status
│   ├── GetAdminAiModelsQuery.cs
│   └── GetAdminAiModelsQueryHandler.cs
├── Queries/GetProviderModelSyncDiff/                   # NEW — FR-005/006: read-only diff, never mutates
│   ├── GetProviderModelSyncDiffQuery.cs
│   ├── GetProviderModelSyncDiffQueryHandler.cs
│   └── ProviderModelSyncDiffDto.cs
├── Commands/UpdateAiModelStatus/                       # NEW — FR-002/003
│   ├── UpdateAiModelStatusCommand.cs
│   ├── UpdateAiModelStatusCommandHandler.cs
│   └── UpdateAiModelStatusCommandValidator.cs
├── Commands/ApplyProviderModelSync/                     # NEW — FR-007/008
│   ├── ApplyProviderModelSyncCommand.cs
│   ├── ApplyProviderModelSyncCommandHandler.cs
│   └── ApplyProviderModelSyncCommandValidator.cs
└── AiAdminActionLog.cs                                 # MODIFIED — add log entries for model status change + sync apply (existing class from spec 007)

src/AskLucy.Web/
├── Controllers/v1/AdminAiProvidersController.cs        # MODIFIED — 4 new actions (models list, status PATCH, sync, sync/apply)
└── Contracts/AiContracts.cs                            # MODIFIED — UpdateAiModelStatusRequest, ApplyProviderModelSyncRequest

src/AskLucy.Web/ClientApp/src/features/admin/
├── api/adminAiProvidersApi.ts                          # MODIFIED — getModels(providerId), updateModelStatus, syncModels, applyModelSync
├── components/
│   ├── AiModelStatusMenu.tsx (+ .test.tsx)             # NEW — per-model status change, confirm-gated (mirrors AiProviderActionsMenu.tsx)
│   └── ModelSyncDialog.tsx (+ .test.tsx)               # NEW — trigger sync → show diff → confirm/apply
└── pages/AdminAiProvidersPage.tsx (+ tests)            # MODIFIED — expandable per-provider row (MUI Collapse) showing its model table + "Sync from provider"
```

**Structure Decision**: Additive to the existing `AskLucy.Application/Ai`, `AskLucy.Web`,
and `features/admin` areas — no new project, no new page/route on the frontend (the model
catalog lives inside the existing AI Providers admin page via an expandable row), no new
backend project or controller (extends `AdminAiProvidersController`).
