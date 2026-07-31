# Implementation Plan: Multi-Provider AI Engine

**Branch**: `005-multi-provider-ai-engine` | **Date**: 2026-07-30 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/005-multi-provider-ai-engine/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Replace the single, hardcoded `IAIProvider`/`OpenAIProvider` pairing (which FR-022 of the
legacy-modernization spec explicitly froze at one vendor) with a provider-agnostic AI Engine
that supports OpenAI, Anthropic, Google Gemini, and OpenRouter behind one abstraction,
selectable per conversation/message, with an admin-curated model catalog, generation
parameters, usage/cost tracking, provider health monitoring, and side-by-side model
comparison — while adding zero new architectural layers and reusing existing entities
(`Message`, `UserChat`) wherever their shape already matches the new requirements, per
constitution §III (Simplicity First) and §9 (AI Principles: provider/model abstraction).

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend, `AskLucy.*` projects); TypeScript 5.x / React 19 (frontend, `src/AskLucy.Web/ClientApp`)

**Primary Dependencies**: ASP.NET Core 10, EF Core 10 (SQL Server provider), MediatR (CQRS), FluentValidation, ASP.NET Identity + JWT, Serilog, ASP.NET Core Data Protection (already registered — reused for credential encryption); Vite, MUI, TanStack Query, Zustand, React Hook Form — frontend HTTP calls go through the existing hand-rolled `apiFetch`/`fetch` wrapper in `ClientApp/src/api/httpClient.ts`, not Axios (CLAUDE.md lists Axios as the project default, but no Axios dependency exists in this codebase yet — following the established `apiFetch` convention per constitution §VII, Convention Over Configuration, rather than introducing a second HTTP client).

**Storage**: SQL Server via EF Core Code-First migrations (`AskLucy.Persistence`), same `AskLucyDbContext`/`DefaultConnection` used by every other feature.

**Testing**: xUnit v3 + NSubstitute + FluentAssertions (backend unit/integration, per-layer `tests/AskLucy.*.Tests` projects); Vitest + React Testing Library (frontend unit/component); Playwright (`tests/AskLucy.E2E.Tests`) for end-to-end.

**Target Platform**: ASP.NET Core web API + React SPA (`ClientApp`), same deployment target as the rest of the application (no new target platform introduced).

**Project Type**: Web application (existing backend Clean Architecture solution + frontend SPA) — not the generic template's Option 1/2/3; see Project Structure below for the actual layout.

**Performance Goals**: Provider/model selection and message send add no perceptible latency beyond the model's own response time (spec SC-001, ≤10s of non-model overhead); provider health status reflects an outage within one health-check interval (SC-006).

**Constraints**: Every AI-invoking endpoint MUST stay behind the existing `ai-endpoints` rate-limit policy (constitution §6); provider credentials MUST be encrypted at rest and never returned by any read endpoint (FR-004, FR-031); streaming behavior MUST be provider-agnostic on the wire (FR-012) even though OpenAI, Anthropic, and Google Gemini each use structurally different streaming/request formats.

**Scale/Scope**: Not numerically specified in the spec (Assumptions). Inferred consistent with the existing single-deployment-per-tenant scale already implied by constitution §1 (no multi-tenant sharding, no new scale requirement beyond what the current chat feature already handles) — see research.md Decision 8.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see "Post-Design Constitution Check" at the end of this document.*

| Principle / Rule | Status | Notes |
|---|---|---|
| I. Clean Architecture & Dependency Rule | PASS | New `AIProvider`/`AIModel`/`ProviderHealthCheck`/`UserAiPreference` entities live in `Domain`; the `IAIProvider`/`IAIProviderResolver` abstractions live in `Application`; vendor HTTP clients live in `Infrastructure.Ai` (existing project); EF configs/migrations live in `Persistence`. No layer references outward. |
| II. SOLID | PASS | One `IAIProvider` implementation per vendor (OCP: adding a 5th vendor = new class + DI registration, zero edits to existing providers or Application code). `IAIProviderResolver` is a narrow, single-purpose interface (ISP). |
| III. Simplicity First (DRY/KISS/YAGNI) | PASS, with a deliberate collapse | The spec's "Message Usage" and "Conversation Model Settings" key entities are **not** modeled as new tables — `Message` (already has `Provider`/`Model`/`GenerationParametersJson`/token counts since SPEC-002) and `UserChat` are extended instead, since a parallel 1:1 table would just be a join with no independent lifecycle. `ModelPricing` is an owned value object on `AIModel`, not a separate table, since pricing has no independent lifecycle from the model row it prices in this spec's scope. See research.md Decision 2. |
| IV. Composition Over Inheritance | PASS | Four sibling `IAIProvider` implementations, no shared base class beyond what already exists (none needed — each vendor's HTTP shape differs enough that a shared base would fight the differences, per research.md Decision 1). |
| V. Dependency Inversion & Testability | PASS | `IAIProviderResolver` is the only new seam Application depends on; every handler remains unit-testable with it faked. |
| VI. Separation of Concerns | PASS | Vendor HTTP/JSON mapping stays in `Infrastructure.Ai`; provider/model selection policy stays in `Application`; controllers stay thin (mirrors existing `AiController`/`AdminDashboardController`). |
| VII. Convention Over Configuration | PASS | New admin endpoints follow the exact `[Authorize(Policy = "AdministratorOrSuperUser")]` + `[EnableRateLimiting("admin-endpoints")]` shape already used by `AdminDashboardController`; new options classes follow the existing `IOptions<T>` + `ValidateOnStart` convention. |
| VIII. No Silent Failures | PASS | FR-028/FR-029/FR-030 require translated, user-visible errors and preserved partial-stream state — extends the existing `AiProviderUnavailableException` → Problem Details pattern per vendor. |
| §3 Architecture Rules (layering, CQRS, DI) | PASS | All new behavior is MediatR commands/queries; no controller touches `DbContext` or a vendor SDK directly. |
| §5 Database Principles | PASS, flag one item | Surrogate keys (Guid v7), indexed FKs, soft-delete/audit via existing interceptor — all reused as-is. `ProviderHealthCheck` is an append-only log table (no soft delete needed; health history has no user-facing deletion requirement). |
| §6 API Standards (rate limiting, Problem Details, versioning) | PASS | Reuses `/api/v1/...`, RFC 7807 Problem Details, and the existing `ai-endpoints`/`admin-endpoints` rate-limit policies (already present in `Program.cs`) — no new rate-limit policy required for FR-033 (see research.md Decision 6). |
| §8 Security | PASS | Credentials encrypted via the already-registered `IDataProtectionProvider` (mirrors `SignedUrlService`'s exact pattern), never round-tripped in any DTO. |
| §9 AI Principles | PASS, one noted exception | "Streaming is the default; non-streaming is the exception, justified by the use case." The new model-comparison endpoint (User Story 7 / FR-024–FR-026) is non-streaming by design (N simultaneous SSE streams multiplexed to one client is materially more complex for a P4, lower-usage feature) — justified exception, recorded in Complexity Tracking. |
| §16/§17 Quality Gates / Decision Making | ACTION NOEDED | Two decisions here (keyed-DI multi-provider resolution; collapsing spec entities into existing tables) are the kind of "new architectural pattern" / "expensive to reverse" choices §17 says need an ADR. Recorded as a follow-up in Complexity Tracking — not a gate failure, but must be written before/alongside implementation, not skipped. |

No unjustified violations. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/005-multi-provider-ai-engine/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── providers.md
│   ├── chat.md
│   ├── preferences.md
│   ├── usage.md
│   └── admin.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

This is an existing Clean Architecture solution (backend) + React SPA (frontend), not a
greenfield layout — new code slots into the established structure:

```text
src/
├── AskLucy.Domain/
│   └── Ai/                              # NEW: AIProvider, AIModel, ProviderHealthCheck,
│                                         #      UserAiPreference, ModelPricing value object
├── AskLucy.Application/
│   ├── Abstractions/
│   │   ├── IAIProvider.cs               # REVISED: model/parameters become per-call, not fixed
│   │   └── IAIProviderResolver.cs       # NEW
│   └── Ai/
│       ├── Commands/SendChatMessage/    # REVISED: provider/model/params flow through
│       ├── Commands/CompareModels/      # NEW (User Story 7)
│       ├── Commands/AdminProviders/     # NEW (enable/disable, credentials, model status)
│       └── Queries/                     # NEW (catalog, health, usage, preferences)
├── AskLucy.Infrastructure/
│   └── Ai/
│       ├── OpenAIProvider.cs            # REVISED: keyed registration, per-call model
│       ├── AnthropicProvider.cs         # NEW
│       ├── GoogleGeminiProvider.cs      # NEW
│       ├── OpenRouterProvider.cs        # NEW
│       ├── AiProviderResolver.cs        # NEW (keyed-DI lookup)
│       └── ProviderHealthCheckHostedService.cs  # NEW (mirrors WhisperWarmupHostedService)
├── AskLucy.Persistence/
│   ├── Configurations/                  # NEW: AIProviderConfiguration, AIModelConfiguration,
│   │                                     #      ProviderHealthCheckConfiguration,
│   │                                     #      UserAiPreferenceConfiguration; REVISED:
│   │                                     #      MessageConfiguration, UserChatConfiguration
│   ├── Repositories/                    # NEW: AIProviderRepository, AIModelRepository, etc.
│   └── Migrations/                      # NEW migration(s) for the above
└── AskLucy.Web/
    ├── Controllers/v1/
    │   ├── AiController.cs              # REVISED: accepts provider/model/params
    │   ├── AiProvidersController.cs     # NEW: user-facing catalog/preferences/usage
    │   └── AdminAiProvidersController.cs # NEW: admin provider/credential/model/health mgmt
    └── ClientApp/src/features/
        ├── chat/                        # REVISED: provider/model picker, parameter panel,
        │                                #   usage display, comparison view
        ├── settings/                    # REVISED: new "AI Providers" tab (defaults/params)
        └── admin/                       # REVISED: new provider management page

tests/
├── AskLucy.Domain.Tests/Ai/
├── AskLucy.Application.Tests/Ai/
├── AskLucy.Infrastructure.Tests/Ai/      # provider mock/contract tests (per spec Testing section)
├── AskLucy.Web.Tests/Controllers/
└── AskLucy.E2E.Tests/                    # Playwright: multi-provider chat, comparison flow
```

**Structure Decision**: Extend the four existing backend projects (`AskLucy.Domain`,
`AskLucy.Application`, `AskLucy.Infrastructure`, `AskLucy.Persistence`, `AskLucy.Web`) and the
existing `ClientApp` React SPA — no new project, no new deployable, consistent with
constitution §3's fixed solution shape. All new code is additive within `Ai`-named
subfolders already established by the current single-provider implementation.

## Complexity Tracking

> Recorded per constitution §16/§17 — none of these are Constitution Check *failures*, but each is a deliberate, non-default choice that needs to be visible rather than silently made.

| Decision | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Model comparison (FR-024–FR-026) is non-streaming, deviating from the constitution's streaming-by-default AI principle | Streaming N provider responses concurrently to one client over one SSE channel requires interleaving/multiplexing logic with no precedent in this codebase, for a P4 (lowest-priority) story | Building a multiplexed multi-stream SSE protocol now, before the platform has a single working streaming multi-provider chat, is premature — YAGNI until comparison usage data justifies it |
| Keyed DI (`AddKeyedScoped<IAIProvider>`) for provider resolution — a pattern with no prior use in this codebase | Four (soon more) `IAIProvider` implementations must be selected at runtime by a string key from admin/user configuration, not resolved by type alone | A `Dictionary<string, IAIProvider>` built by hand in a factory class is the same idea with more boilerplate and no compile-time DI-container validation; keyed DI is a first-class .NET 8+ feature, not a novel dependency |
| `Message`/`UserChat` extended instead of new `MessageUsage`/`ConversationModelSettings` tables from the spec's Key Entities list | Those two spec entities already exist as columns on `Message`/`UserChat` (added in SPEC-002) with an identical 1:1 relationship and no independent lifecycle | A separate table joined 1:1 on every read would violate constitution §III (DRY/no premature abstraction) for zero benefit — nothing in the spec requires `Message`/`UserChat` and their usage/settings data to vary independently |

**Follow-up required before/alongside implementation**: constitution §17 requires an ADR for
a new architectural pattern (keyed-DI provider resolution) and for the entity-collapse
decision above (expensive to reverse once messages start being written against the new
columns). `/speckit-tasks` should include a task to author
`docs/adr/000X-multi-provider-ai-engine.md` covering both, with alternatives considered.

## Post-Design Constitution Check

*Re-evaluated after Phase 1 (data-model.md, contracts/, quickstart.md) — see those files.*

No new violations were introduced during design. The data model in `data-model.md` confirms
the Complexity Tracking entries above are the only deviations from a "straight down the
middle" implementation, and both are justified and recorded. One additional design
tension surfaced while writing `contracts/chat.md`'s model-comparison endpoints — reusing
`Message` for comparison candidates initially implied mutating `IsIncludedInContext` after
the fact, which would have broken `Message`'s existing append-only/immutable invariant
(constitution §VIII adjacent — data integrity of an audit-style record). Resolved by having
`POST /compare` stay fully ephemeral (no persistence) and `POST /compare/.../continue`
write every candidate message once, with its final `IsIncludedInContext` value already
decided at insert time — no entity was weakened and no new violation was introduced. The
contracts in `contracts/` all follow existing REST/Problem Details/rate-limit conventions
with no other exceptions. Gate: **PASS** — ready for `/speckit-tasks`.
