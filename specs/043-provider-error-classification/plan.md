# Implementation Plan: AI Provider Failure Classification & Accurate Health Reporting

**Branch**: `043-provider-error-classification` | **Date**: 2026-08-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/043-provider-error-classification/spec.md`

## Summary

Provider-side failures currently reach administrators as the API's unmapped-exception fallback — *"An unexpected error occurred. Please try again."* — because the catalog-listing path throws exception types (`CryptographicException`, `TaskCanceledException`, `JsonException`) that `ProblemDetailsMiddleware.Map` does not recognise, and because every vendor's non-2xx response is collapsed to one of only two translated cases. Provider health compounds this: it is a bare boolean whose recorded reason is never exposed, and a stopped background cycle leaves a stale status rendering as current fact.

The approach is a **single shared failure classifier in Infrastructure** that reads each vendor's own machine-readable reason code, plus an **abstract exception base carrying a `Kind`** that the existing four sealed exception types inherit — so every current `catch`-by-type site and every Problem Details mapping keeps working while one property becomes the single source of classification. Health records that same `Kind`, the admin DTO exposes it with a server-computed staleness horizon, and an on-demand re-check action closes the loop. Separately, the domain's reject-on-zero rule for token limits is removed (they become genuinely optional, following the `Pricing` owned-type precedent), and the site-boundary vision call gets its own 30-second budget via a linked cancellation token.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript 5 / React 19 (Vite)

**Primary Dependencies**: ASP.NET Core, EF Core (SQL Server), MediatR, FluentValidation, Serilog, `Microsoft.AspNetCore.DataProtection`; MUI v7, TanStack Query, Zustand

**Storage**: SQL Server via EF Core code-first migrations. Two additive column pairs (`AIProviders`, `ProviderHealthChecks`) and one nullability relaxation (`AIModels`).

**Testing**: xUnit across `AskLucy.Domain.Tests`, `AskLucy.Application.Tests`, `AskLucy.Infrastructure.Tests`, `AskLucy.Web.Tests`; Vitest + Testing Library + axe for `ClientApp`. Provider adapters test against `StubHttpMessageHandler` (already present in `tests/AskLucy.Infrastructure.Tests/Ai/`).

**Target Platform**: ASP.NET Core web service on Windows (site4now shared host), SPA client

**Project Type**: Web application — Clean Architecture backend (`Domain`/`Application`/`Infrastructure`/`Persistence`/`Web`) plus a React SPA under `src/AskLucy.Web/ClientApp`

**Performance Goals**: Health checks stay off the user-request path (existing 2-minute background interval, unchanged). On-demand re-check returns within one provider round-trip. Vision enhancement adds ≤30s worst case to boundary resolution (down from a possible 120s today).

**Constraints**: No user-visible message may carry a credential, raw vendor body, exception type name, or stack trace (FR-013). Classification is disclosed to administrators only (FR-015a). `Application` may not reference `HttpClient` or EF Core (constitution §3), so response classification is necessarily an Infrastructure concern behind an Application-owned vocabulary.

**Scale/Scope**: 4 providers, ~4 admin screens' worth of UI, single-digit thousands of catalog model rows. The affected vendor list is ~97 models with no published token metadata.

## Constitution Check

*GATE: evaluated before Phase 0 and re-evaluated after Phase 1 design.*

| Principle / Rule | Assessment | Verdict |
|---|---|---|
| **VIII. No Silent Failures (NON-NEGOTIABLE)** | This feature exists to satisfy it. Every classified failure reaches Problem Details and a visible admin UI path (FR-010/FR-015). The one deliberate non-throwing path — the boundary vision analyzer — returns an explicit "unavailable, here's why" value that the caller surfaces, which is the sanctioned "caller-visible failure", not a swallow. | PASS |
| **I. Clean Architecture / dependency rule** | `AiProviderFailureKind` (a plain enum) and the exception hierarchy live in `Application/Abstractions`, which already owns them. Response classification touches `HttpResponseMessage`, so it lives in `Infrastructure/Ai`. No new Application→Infrastructure reference. | PASS |
| **III. Simplicity First (DRY/KISS/YAGNI)** | One classifier shared by four providers replaces four divergent `EnsureSuccessAsync` bodies. The exception base collapses five `ProblemDetailsMiddleware` switch arms into one. Net reduction in branching despite added capability. | PASS |
| **§3 Infrastructure isolation** | Vendor reason codes never escape Infrastructure; Application sees only `AiProviderFailureKind`. Adding a fifth provider means implementing one classifier hook, no Application change. | PASS |
| **§3 CQRS** | The on-demand re-check is a MediatR command (state-changing: it writes a health record) with one handler. No controller logic. | PASS |
| **§5 Migrations** | Three additive/relaxing changes, one migration, reversible `Down` documented (see data-model.md § Migration). No destructive drop, so no two-step deploy needed. | PASS |
| **§5 Concurrency** | `AIProvider` and `AIModel` already carry `RowVersion`; no new concurrent-write surface. | PASS |
| **§6 Problem Details** | Classification rides as an extension member (`providerFailure`), matching the existing `reason` / `referencingAgentTools` / `violations` precedent. No ad-hoc error shape. | PASS |
| **§6 REST conventions** | `POST .../providers/{id}/actions/check-health` follows the documented sub-resource-action form and the sibling `models/actions/sync`. | PASS |
| **§8 Security / §14 secrets** | Classification is derived from the vendor body but the body is never placed in an exception message or Problem Details; it is logged server-side truncated. Disclosure is gated on the administrator role (FR-015a). | PASS |
| **§6 Pagination** | `GET providers/{id}/models` is unpaginated, and US4 grows its realistic payload from ~2 rows to ~100. Accepted under §6's explicit carve-out for "small stable admin lists": bounded by one vendor's catalog, administrator-only, and read behind an expand/collapse. Revisit if any provider's catalog exceeds a few hundred rows. Separately, FR-028a now requires following the **vendor's** own list pagination — see research.md Decision 11. | PASS (accepted) |
| **§10 Testing** | Regression tests in the same PR: classifier table-driven unit tests per vendor, handler tests, analyzer failure-mode tests, and a11y-covered UI tests. | PASS |
| **§14 Observability** | Every classified failure logged via `LoggerMessage` source-generated delegates with provider + kind + vendor reason code as structured fields. | PASS |

**No violations. Complexity Tracking section omitted — nothing to justify.**

### Post-design re-evaluation (after Phase 1)

Re-checked against the artifacts produced in Phase 1. The design introduces four new types and one new endpoint; each was re-tested against the dependency rule:

- `AiProviderFailureKind` and the exception hierarchy — plain types in `Application/Abstractions`, no Infrastructure reference. **PASS**
- `AiProviderResponseClassifier` — reads `HttpResponseMessage`, therefore correctly placed in `Infrastructure/Ai`. **PASS**
- `IProviderHealthFreshnessPolicy` — interface in `Application`, implementation in `Infrastructure` over `ProviderHealthCheckOptions`. This is the only new seam, and it exists precisely so the Application layer can honour FR-019 without reading Infrastructure configuration. **PASS**
- `CheckAiProviderHealthCommand` — one MediatR command, one handler, writes through `IUnitOfWork` in a single `SaveChanges`. **PASS**
- Admin-gated disclosure — decided once in `ProblemDetailsMiddleware`, which holds `HttpContext` directly; no per-endpoint duplication, no new policy. **PASS**

The migration was re-examined against §5: additive columns plus one widening, reversible `Down` with the backfill its lossy round-trip requires documented in data-model.md § Migration. No destructive change, so no two-step deploy. **PASS**

**Still no violations.**

One item carried forward from `/speckit-clarify` as *Deferred* is resolved here rather than left open: FR-025's concurrency guard is satisfied by the controller's existing `admin-endpoints` rate-limit policy plus a pending-state-disabled trigger in the UI; no new mechanism is introduced. See research.md Decision 8.

## Project Structure

### Documentation (this feature)

```text
specs/043-provider-error-classification/
├── plan.md              # This file
├── research.md          # Phase 0 output — 10 decisions
├── data-model.md        # Phase 1 output — entity deltas + migration
├── quickstart.md        # Phase 1 output — validation guide
├── contracts/
│   ├── admin-provider-health-api.md
│   └── provider-failure-classification.md
├── checklists/
│   └── requirements.md  # from /speckit-specify, re-validated by /speckit-clarify
└── tasks.md             # NOT created by /speckit-plan
```

### Source Code (repository root)

```text
src/
├── AskLucy.Domain/
│   └── Ai/
│       ├── AIProvider.cs                      # + HealthFailureKind, HealthFailureReason; UpdateHealthStatus signature
│       └── AIModel.cs                         # ContextWindowTokens/MaxOutputTokens → int?; drop reject-on-zero
├── AskLucy.Application/
│   ├── Abstractions/
│   │   ├── IAIProvider.cs                     # + AiProviderFailureKind, AiProviderException base,
│   │   │                                      #   5 new exception types; ProviderModelInfo limits → int?;
│   │   │                                      #   CheckHealthAsync → Task<ProviderHealthResult>
│   │   └── IProviderHealthFreshnessPolicy.cs  # NEW — Application-owned view of the check interval
│   └── Ai/
│       ├── AdminAiProviderDto.cs              # + failure kind/reason + healthStaleAfterUtc
│       ├── AdminAiModelDto.cs                 # limits → int?
│       ├── ModelSummaryDto.cs                 # limits → int?
│       └── Commands/CheckAiProviderHealth/    # NEW — command, handler, result DTO
├── AskLucy.Infrastructure/
│   ├── Ai/
│   │   ├── AiProviderResponseClassifier.cs    # NEW — shared vendor-reason → Kind mapping
│   │   ├── GoogleGeminiProvider.cs            # EnsureSuccess delegates; ListAvailableModels wrapped
│   │   ├── OpenAIProvider.cs                  # same + stop substituting 0 for token limits
│   │   ├── AnthropicProvider.cs               # same
│   │   ├── OpenRouterProvider.cs              # same
│   │   ├── ProviderHealthCheckHostedService.cs# records Kind alongside IsHealthy
│   │   └── ProviderHealthFreshnessPolicy.cs   # NEW — implements the Application abstraction
│   └── Boundaries/
│       └── GeminiBoundaryVisionAnalyzer.cs    # 30s linked-CTS budget; timeout ≠ cancellation
├── AskLucy.Persistence/
│   ├── Configurations/{AIProviderConfiguration,ProviderHealthCheckConfiguration,AIModelConfiguration}.cs
│   └── Migrations/                            # one migration
└── AskLucy.Web/
    ├── Middleware/ProblemDetailsMiddleware.cs # base-type arm + admin-gated disclosure
    ├── Controllers/v1/AdminAiProvidersController.cs  # + check-health action
    └── ClientApp/src/features/admin/
        ├── api/adminAiProvidersApi.ts
        ├── components/{ProviderHealthCell,AiProviderActionsMenu,ModelSyncDialog,ProviderModelsSection}.tsx
        └── pages/AdminAiProvidersPage.tsx

tests/
├── AskLucy.Domain.Tests/Ai/                   # AIModel optional-limits rules
├── AskLucy.Application.Tests/Ai/              # check-health handler, DTO staleness, sync apply
├── AskLucy.Infrastructure.Tests/
│   ├── Ai/                                    # classifier table tests per vendor (StubHttpMessageHandler)
│   └── Boundaries/                            # vision failure-mode + timeout fallback
└── AskLucy.Web.Tests/                         # ProblemDetails mapping + admin-gated disclosure
```

**Structure Decision**: Existing Clean Architecture layout, unchanged. This feature adds no project and no new architectural seam — it extends the `Application/Abstractions` provider vocabulary that already exists, adds one Infrastructure helper alongside the four provider adapters it serves, and threads two nullable columns through the persistence layer. The frontend change is confined to the existing `features/admin` slice.
