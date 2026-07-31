# Implementation Plan: Selective Model Sync Review

**Branch**: `009-selective-model-sync-review` | **Date**: 2026-07-31 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-selective-model-sync-review/spec.md`

## Summary

Spec 008 shipped a sync-review dialog where Confirm applies the *entire* vendor diff as
one all-or-nothing batch. Live use surfaced that a real vendor's "added" side can be huge
(98 models observed), making the dialog unusable and giving the administrator no way to
narrow or choose a subset. This plan adds a shared text filter, per-row checkboxes with
per-side select-all/none, a live selected-count, and changes Confirm to submit only the
selected subset. It also changes `ApplyProviderModelSyncCommand` from an all-or-nothing
batch to a **best-effort, per-row** apply (per the clarification): a stale row (one that
no longer matches its expected catalog state) is skipped and reported individually rather
than blocking the rows that are still valid. This is the one genuine backend behavior
change — everything else (filter, selection, counts) is frontend-only, since the diff/apply
contract already accepted an arbitrary subset of the full diff.

## Technical Context

**Language/Version**: C# / .NET 10 (backend, `AskLucy.Application`/`AskLucy.Web`); TypeScript 5 / React 19 (frontend, `AskLucy.Web/ClientApp`).

**Primary Dependencies**: MediatR + FluentValidation (existing CQRS pipeline), EF Core (existing `AIModels` table — no schema change), MUI (`Checkbox`, `TextField`, `Dialog`), TanStack Query.

**Storage**: SQL Server via EF Core, reusing `AIModels`/`IAIModelRepository` exactly as spec 008 left them — no migration.

**Testing**: xUnit + NSubstitute for the revised `ApplyProviderModelSyncCommandHandler` (matching `ApplyProviderModelSyncCommandHandlerTests.cs`'s existing style, extended for the partial-failure case); Vitest + React Testing Library for `ModelSyncDialog.tsx`'s new filter/selection behavior (matching its existing `ModelSyncDialog.test.tsx`).

**Target Platform**: Web — extends the existing `ModelSyncDialog.tsx` inside the Admin AI Providers page; no new page/route/dialog.

**Project Type**: Web application — additive to the existing `AskLucy.Application/Ai`, `AskLucy.Web`, and `features/admin` areas from specs 005/007/008.

**Performance Goals**: No new performance budget. Filtering/selection operate client-side over an already-fetched diff (at most a few hundred rows per spec 008's Scale/Scope); the apply call remains a single request regardless of how many rows are selected.

**Constraints**: Admin-only (unchanged); a stale row MUST NOT block other rows in the same apply (FR-007a); no row is ever deleted (unchanged from spec 008); filtering MUST NOT discard selections (FR-005).

**Scale/Scope**: Same diff sizes as spec 008 anticipated (up to ~100 rows per side, per the reported OpenAI case) — client-side filtering/selection needs no virtualization at this scale.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Assessment |
|---|---|
| I. Clean Architecture & Dependency Rule | PASS — the only backend change is inside the existing `ApplyProviderModelSyncCommandHandler` (Application) and its response DTO; Domain (`AIModel.Create`/`SetStatus`) is unchanged. |
| III. CQRS | PASS — `ApplyProviderModelSyncCommand` remains a Command; it now returns a result DTO (`ApplyProviderModelSyncResultDto`) instead of nothing, which the constitution's CQRS rule explicitly allows ("Commands MUST NOT return full read models beyond what the caller needs to confirm the write" — a per-row applied/failed confirmation is exactly that, not an unrelated read). |
| V. Dependency Inversion & Testability | PASS — the revised handler still depends only on `IAIModelRepository`/`IUnitOfWork`/`ICurrentUserAccessor`; still fully unit-testable with fakes. |
| VI. Separation of Concerns | PASS — the per-row staleness check moves from the validator into the handler (see research.md Decision 2) because it now drives *which rows apply* rather than *whether the whole request is rejected* — that's business logic belonging in the handler, not validation belonging in FluentValidation, which the constitution restricts to input-shape checks. |
| §5 Database Principles ("one business transaction, one SaveChanges") | PASS, with a design decision recorded in research.md Decision 2 — best-effort apply is implemented as an in-memory precondition check per row (skip-and-report stale rows) *before* any mutation, so every row that passes is still written in exactly one `SaveChangesAsync` call. No per-row transactions are introduced; a true concurrent-write DB exception mid-`SaveChanges` remains unmodeled, consistent with spec 008's original scope. |
| VIII. No Silent Failures (NON-NEGOTIABLE) | PASS — every failed row is named individually with a reason in the response (FR-007b), not swallowed; the frontend surfaces a mixed success/failure result via the existing Snackbar/Alert pattern (no new error-handling mechanism). |
| §6 API Standards | PASS — reuses the existing `POST .../models/actions/sync/apply` route; response changes from `204 No Content` to `200 OK` with a body, which is an additive, non-breaking refinement of the same endpoint version (no consumer currently depends on the empty body). |
| §7 UI Principles | PASS — `Checkbox`/`TextField` are existing MUI components already used elsewhere in the codebase; no new shared component introduced. Selection/filter state is local `useState` inside `ModelSyncDialog.tsx`, following the same precedent spec 007/008 already established (and spec 008's plan.md already recorded) for confirm-dialog-local UI state living outside Zustand. |
| §10 Testing | Design requirement — extend `ApplyProviderModelSyncCommandHandlerTests.cs` for the partial-failure case and `ModelSyncDialog.test.tsx` for filter/selection/partial-result rendering, in this change. |

No violations. **Complexity Tracking is not needed.**

**Post-Phase 1 re-check**: data-model.md and contracts/selective-sync-apply.md confirm the
design adds one new Application DTO (`ApplyProviderModelSyncResultDto`) and changes one
handler's internals — no new Domain concept, no schema change, no new controller. Every
gate above still holds unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Application/Ai/Commands/ApplyProviderModelSync/
├── ApplyProviderModelSyncCommand.cs             # MODIFIED — IRequest → IRequest<ApplyProviderModelSyncResultDto>
├── ApplyProviderModelSyncCommandHandler.cs       # MODIFIED — per-row best-effort apply (FR-007a/FR-007b)
├── ApplyProviderModelSyncCommandValidator.cs     # MODIFIED — drop the per-row stale-diff rejection (moves to handler); keep shape/non-empty checks
└── ApplyProviderModelSyncResultDto.cs            # NEW — { AppliedModelKeys, Failed: SyncApplyFailureDto[] }

src/AskLucy.Web/
├── Controllers/v1/AdminAiProvidersController.cs  # MODIFIED — sync/apply action returns 200 OK + ApplyProviderModelSyncResultDto instead of 204
└── Contracts/AiContracts.cs                      # unchanged request shape (ApplyProviderModelSyncRequest already carries an arbitrary added/removedFromVendor list)

src/AskLucy.Web/ClientApp/src/features/admin/
├── api/adminAiProvidersApi.ts                    # MODIFIED — applyModelSync's return type becomes ApplyProviderModelSyncResult
└── components/ModelSyncDialog.tsx (+ .test.tsx)  # MODIFIED — shared filter box, per-row Checkbox, per-side select-all/none, selected count, Confirm sends only selected rows, renders applied/failed result
```

**Structure Decision**: Additive/modifying change entirely within the existing
`AskLucy.Application/Ai/Commands/ApplyProviderModelSync`, `AskLucy.Web`, and
`features/admin` areas from specs 005/007/008 — no new project, page, route, dialog, or
controller. `GetProviderModelSyncDiffQuery` (the diff computation) is untouched.

## Complexity Tracking

No Constitution Check violations — this section is not needed.
