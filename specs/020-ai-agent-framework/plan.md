# Implementation Plan: AI Agent Framework & Agent Runtime

**Branch**: `020-ai-agent-framework` | **Date**: 2026-08-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/020-ai-agent-framework/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Add a new `Agents` module that lets a user define, version, and run reusable AI agents — bounded multi-step planning + tool execution over the platform's *existing* AI Provider, RAG, Memory, Prompt, Knowledge Base, and Conversation engines, never duplicating them (FR-029–FR-033). Technical approach (research.md): reuse `IAIProvider`/`IAIProviderResolver` for every model call (structured JSON-mode calls for planning/tool-selection, streaming for the final result); reuse `IRagService`/`IMemoryService` verbatim for retrieval; extend the Memory Engine with one new command (`CreateMemoryCandidateCommand`) so agent-proposed memory writes flow through its existing `PendingApproval` lifecycle rather than a new one; delegate to the existing `ExecutePromptCommand` for prompt-execution tool calls; run executions as Hangfire background jobs (mirroring `DocumentProcessingPipeline`) with per-step status checks enabling pause/resume/cancel; push live progress over a new `AgentExecutionHub` SignalR hub (mirroring `DocumentProcessingHub`/`MemoryHub`, not SSE — research.md Decision 9); and defer both a real `Organization`/`Tenant` aggregate and a `SubscriptionTier` concept (neither exists in the codebase today) in favor of role-gated (`Administrator`/`Super User`) policies and per-user limit overrides with reserved forward-compatible columns.

## Technical Context

**Language/Version**: C# on .NET 10 (backend, all five existing `AskLucy.*` projects); TypeScript on React 19 + Vite (frontend, `src/AskLucy.Web/ClientApp`) — no new language/runtime, matches every prior feature in this repo.

**Primary Dependencies**: MediatR (CQRS), FluentValidation, AutoMapper, Entity Framework Core (SQL Server), Hangfire (background job runner — already registered in `src/AskLucy.Infrastructure/DependencyInjection.cs`), SignalR (real-time hubs), ASP.NET Core Identity (auth/roles). No new NuGet/npm package is introduced by this feature (research.md's Technology Summary table).

**Storage**: SQL Server via EF Core Code-First migrations against the existing `AskLucyDbContext`; every new entity inherits `BaseEntity` (Guid v7 keys, soft delete via query filter + `AuditSaveChangesInterceptor`, `RowVersion` optimistic concurrency) — see data-model.md.

**Testing**: xUnit for `AskLucy.Domain.Tests` / `AskLucy.Application.Tests` / `AskLucy.Persistence.Tests` / `AskLucy.Infrastructure.Tests` / `AskLucy.Web.Tests`; Playwright (`*.spec.ts`) for `AskLucy.E2E.Tests` — matches the Prompt Library/Memory feature test-folder conventions exactly (research.md item 15).

**Target Platform**: ASP.NET Core Web API (`AskLucy.Web`) hosting the embedded React SPA (`ClientApp`) — single existing deployable, no new platform or service introduced.

**Project Type**: Web application — extends the existing modular monolith's five backend projects plus the existing frontend SPA; no new project/solution entry.

**Performance Goals**: SC-002 (execution step/tool-activity visibility within 2s of the underlying change) and SC-009 (cancellation reaches a stopped state within 5s) — both satisfied by SignalR push (research.md Decision 9) plus a per-step status re-check in the Hangfire runner loop (research.md Decision 8), not by any new streaming infrastructure.

**Constraints**: No private model chain-of-thought is ever persisted or pushed (FR-035); every tool call is permission-checked immediately before execution, never cached across steps (FR-022/FR-023); the Agent Runtime never calls an AI provider SDK, a vector store, or performs a memory write directly — only through the existing `IAIProvider`/`IRagService`/`IMemoryService` abstractions plus one new Memory command (FR-029/FR-030/FR-032, research.md Decisions 3–5); loop/budget protection (FR-039/FR-040) must be enforced inside the runner loop itself, not delegated to any external service.

**Scale/Scope**: Per-user concurrent-execution cap, administrator-configurable, defaulting to a modest platform value (FR-042, research.md Decision 2); no multi-tenant data partitioning required this release since one deployment already is the tenant boundary (research.md Decision 1). New surface: 2 aggregates spanning 15 entities (data-model.md), ~53 functional requirements, 3 REST controllers, 1 SignalR hub, 8 built-in `IAgentTool` implementations, 1 Hangfire runner job, 1 new frontend feature area.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Section | Status | Notes |
|---|---|---|
| I. Clean Architecture & Dependency Rule | **PASS** | New `Domain/Agents`, `Application/Agents`, `Infrastructure/Agents`, `Persistence/Configurations/Agents` follow the exact existing layer boundaries; no new project, no outward-pointing reference introduced. |
| II. SOLID | **PASS** | `IAgentTool` per-tool classes satisfy OCP (new tool = new class, zero edits to the runtime — contracts/agent-tool-contract.md); narrow, single-purpose interfaces (`IAgentPlanner`, `IAgentExecutionRunner`, `IAgentExecutionNotifier`), no god interfaces. |
| III. Simplicity First (DRY/KISS/YAGNI) | **PASS** | research.md Decisions 1/2 explicitly *avoid* building `Organization`/`SubscriptionTier` aggregates the codebase doesn't need yet — the simpler, more restrictive design is chosen deliberately, not as a shortcut. |
| IV. Composition over Inheritance | **PASS** | Tools composed via `IAgentTool`, no inheritance hierarchy; `AgentInstructions`/`AgentExecutionPolicy` are owned value-type compositions on `Agent`/`AgentVersion`, not subclasses. |
| V. Dependency Inversion & Testability | **PASS** | Every external capability (`IAIProvider`, `IRagService`, `IMemoryService`, `IDocumentRepository`, `IBackgroundJobClient`, `IAgentExecutionNotifier`) is consumed via an interface already or newly defined in `Application`; the runner and every tool are unit-testable with all dependencies faked. |
| VI. Separation of Concerns | **PASS** | Controllers stay thin (contracts/agents-api.md delegates every action to a Command/Query); authorization lives in Application-layer guards (`AgentOwnershipGuard`), not controller `if` checks. |
| VII. Convention over Configuration | **PASS** | The single largest design driver in research.md — Hangfire (not a new job runner), SignalR (not SSE, Decision 9), the existing feature-folder CQRS layout, the existing `BaseEntity`/audit-interceptor/query-filter soft-delete pattern, the existing `*OwnershipGuard` shape — all reused rather than reinvented. |
| VIII. No Silent Failures (NON-NEGOTIABLE) | **PASS, enforced by design** | Every runner failure path writes an `AgentExecutionError` row *and* transitions `AgentExecution.Status` to `Failed` *and* pushes `ExecutionFailed` (data-model.md/contracts/agent-execution-events.md) — never a caught-and-discarded exception; `IRagService`/`IMemoryService`'s existing never-throws contracts are surfaced as ordinary tool results, not swallowed. |
| §3 Architecture Rules | **PASS** | No `Domain`→`Application`/`Infrastructure` reference; `Application/Agents` depends only on `Domain` and `Application/Abstractions`; `Infrastructure/Agents` (hub, Hangfire job class) depends on `Application`/`Domain` only, implementing interfaces Application defines. |
| §3 CQRS rules | **PASS** | Every mutation is a MediatR command with one handler; queries never mutate; cross-cutting concerns (validation, logging) via existing `IPipelineBehavior`s, not per-handler duplication. |
| §3 Domain events | **PASS** | `AgentCreated`, `AgentPublished`, `AgentExecutionStarted/Completed/Failed/Cancelled` raised from aggregates, dispatched post-commit — matches existing event-raising convention. |
| §5 Database Principles | **PASS** | `BaseEntity` inheritance, explicit indexes on every FK/status/owner column used in a query path (data-model.md), `RowVersion` concurrency, soft delete via query filter — all inherited conventions, no new pattern. |
| §6 API Standards | **PASS** | Nouns/plural/kebab, `/api/v1/...`, action sub-resources (`.../actions/{verb}`), Problem Details errors, cursor pagination on every list endpoint, `[Authorize]` by default — contracts/agents-api.md. |
| §7 UI Principles | **PASS** | New `ClientApp/src/features/agents` follows the exact existing feature-folder shape (`api/`, `hooks/`, `components/`, `pages/`); MUI theme, no bespoke styling system. |
| §8 Security | **PASS, with explicit gap resolution** | research.md Decision 1 resolves the tenant-scoping gap (role-gated, not a fictional tenant boundary); FR-046–FR-050 map directly onto the existing ownership-guard + audit-log conventions; prompt-injection defense reuses the existing `<user_memory>`/`<context>` framing pattern (`RetrievalPromptFraming`) so tool/RAG output is never structurally indistinguishable from an instruction. |
| §9 AI Principles | **PASS** | Provider/model abstraction (research.md Decision 3), memory stored distinctly with explicit approval (Decision 5), agent tool set explicit/scoped/bounded (contracts/agent-tool-contract.md) — this section of the constitution describes almost exactly this feature already. |
| §10 Testing Standards | **PASS (planned)** | Test-folder plan mirrors Prompts/Memory exactly (research.md item 15); unit/integration/security/E2E categories all mapped in spec.md's own Testing section. |
| §11–§19 (Git/CI/CD/Docs/Observability/Performance/Quality Gates/Decision Making/AI Agent Rules/DoD) | **PASS** | No deviation requested; `docs/ARCHITECTURE.md` §15/§21, `docs/DOMAIN_SERVICES.md` §21, `docs/ENTITY_MODEL.md` §10/§11 will be rewritten to match this plan during implementation (documentation-is-part-of-implementation, constitution §13), not before. |

No violation requires an entry in Complexity Tracking — every non-obvious choice in this plan (Decisions 1/2 especially) *reduces* scope/complexity relative to what the spec's literal wording might suggest, which is the opposite of a constitution violation.

## Post-Design Constitution Check

*Re-checked after Phase 1 (data-model.md, contracts/, quickstart.md).* No new violation introduced by the detailed design: the `AgentExecutionHub`/Hangfire runner split (research.md Decision 8/9) keeps `Infrastructure` implementing `Application`-owned interfaces exactly per §3; the one cross-feature addition (`CreateMemoryCandidateCommand` inside `Application/Memory`, research.md Decision 5) extends an existing aggregate's write path rather than reaching into it from `Application/Agents`, so no new `Application`-to-`Application` sibling coupling is introduced beyond the already-established `ISender`-mediated cross-command delegation pattern (`InsertPromptIntoConversationCommandHandler`'s precedent). Gate: **PASS**, unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/020-ai-agent-framework/
├── plan.md                       # This file (/speckit-plan command output)
├── research.md                   # Phase 0 output (/speckit-plan command)
├── data-model.md                 # Phase 1 output (/speckit-plan command)
├── quickstart.md                 # Phase 1 output (/speckit-plan command)
├── contracts/                    # Phase 1 output (/speckit-plan command)
│   ├── agents-api.md
│   ├── agent-execution-events.md
│   └── agent-tool-contract.md
├── checklists/
│   └── requirements.md
└── tasks.md                      # Phase 2 output (/speckit-tasks command — NOT created by /speckit-plan)
```

### Source Code (repository root)

This is the existing Ask Lucy Clean Architecture modular monolith (`docs/ARCHITECTURE.md` §4/§7) — the feature adds new folders inside the five existing backend projects plus the existing frontend, introducing zero new projects:

```text
src/
├── AskLucy.Domain/
│   └── Agents/                              # NEW — entities + enums from data-model.md
│       ├── Agent.cs, AgentVersion.cs, AgentTool.cs, AgentKnowledgeBase.cs,
│       │   AgentMemoryPolicy.cs, AgentExecution.cs, AgentExecutionStep.cs,
│       │   AgentExecutionEvent.cs, AgentToolCall.cs, AgentApproval.cs,
│       │   AgentExecutionError.cs, AgentExecutionUsage.cs, AgentExecutionCost.cs,
│       │   AgentPolicy.cs, AgentUserExecutionLimit.cs, AgentAuditLog.cs
│       └── (enums, AgentInstructions.cs, AgentExecutionPolicy.cs value objects)
│
├── AskLucy.Application/
│   ├── Abstractions/                        # EXTENDED — new repo/service interfaces added here (existing flat convention)
│   │   ├── IAgentRepository.cs, IAgentExecutionRepository.cs, IAgentPolicyRepository.cs
│   │   └── IAgentExecutionNotifier.cs, IAgentExecutionRunner.cs
│   ├── Agents/                               # NEW feature folder — mirrors Prompts/ shape exactly
│   │   ├── Commands/
│   │   │   ├── CreateAgent/ UpdateAgent/ DeleteAgent/ ArchiveAgent/ RestoreAgent/
│   │   │   │   DuplicateAgent/ PublishAgentVersion/
│   │   │   ├── StartAgentExecution/ PauseAgentExecution/ ResumeAgentExecution/
│   │   │   │   CancelAgentExecution/ ApproveAgentAction/ RejectAgentAction/
│   │   │   └── CreateAgentPolicy/ UpdateAgentPolicy/ DeleteAgentPolicy/
│   │   │       SetAgentUserExecutionLimit/
│   │   ├── Queries/
│   │   │   ├── GetAgent/ ListAgents/ ListAgentVersions/ GetAgentVersion/
│   │   │   └── GetAgentExecution/ ListAgentExecutions/ GetAgentExecutionEvents/
│   │   │       GetAgentExecutionSteps/ GetAgentToolCalls/ GetAgentExecutionUsage/
│   │   ├── Authorization/
│   │   │   ├── AgentOwnershipGuard.cs
│   │   │   └── AgentExecutionOwnershipGuard.cs
│   │   ├── Tools/                            # IAgentTool implementations (contracts/agent-tool-contract.md)
│   │   │   ├── IAgentTool.cs, AgentToolResult.cs, AgentToolExecutionContext.cs
│   │   │   ├── ConversationTool.cs, KnowledgeSearchTool.cs, DocumentSearchTool.cs,
│   │   │   │   MemorySearchTool.cs, MemoryWriteTool.cs, PromptExecutionTool.cs,
│   │   │   │   FileReadTool.cs, FileMetadataTool.cs
│   │   │   └── AgentToolCatalog.cs
│   │   └── Runtime/                          # planner + orchestration logic (provider-agnostic, testable without Hangfire/SignalR)
│   │       ├── IAgentPlanner.cs, AgentPlanner.cs
│   │       ├── AgentExecutionOrchestrator.cs  # the step loop; called by the Hangfire job, unit-testable standalone
│   │       ├── AgentBudgetGuard.cs            # FR-040 limits
│   │       └── AgentDuplicateToolCallDetector.cs  # FR-039
│   └── Memory/
│       └── Commands/
│           └── CreateMemoryCandidate/         # NEW — one command, extends the existing Memory feature (research.md Decision 5)
│               ├── CreateMemoryCandidateCommand.cs
│               └── CreateMemoryCandidateCommandHandler.cs
│
├── AskLucy.Infrastructure/
│   └── Agents/                               # NEW
│       ├── AgentExecutionHub.cs               # SignalR hub (contracts/agent-execution-events.md)
│       ├── AgentExecutionNotifier.cs          # IAgentExecutionNotifier, wraps IHubContext<AgentExecutionHub>
│       └── AgentExecutionRunnerJob.cs         # IAgentExecutionRunner, dispatched via Hangfire (research.md Decision 8)
│
├── AskLucy.Persistence/
│   ├── Configurations/Agents/                 # NEW — one IEntityTypeConfiguration<T> per entity, EF Fluent API
│   └── Repositories/Agents/                   # NEW — IAgentRepository/IAgentExecutionRepository/IAgentPolicyRepository implementations
│   # AskLucyDbContext gains new DbSets + one new EF Core migration
│
└── AskLucy.Web/
    ├── Controllers/v1/
    │   ├── AgentsController.cs                # NEW
    │   ├── AgentExecutionsController.cs        # NEW
    │   └── AgentPoliciesController.cs          # NEW
    └── ClientApp/src/features/agents/          # NEW — mirrors features/prompts/ shape exactly (research.md item 14)
        ├── api/agentsApi.ts, agentExecutionsApi.ts, agentPoliciesApi.ts
        ├── hooks/useAgents.ts, useAgentMutations.ts, useAgentExecution.ts,
        │         useAgentVersions.ts, useAgentExecutionHub.ts
        ├── components/AgentBuilder.tsx, ToolSelector.tsx, ExecutionConsole.tsx,
        │              ExecutionTimeline.tsx, ApprovalDialog.tsx, VersionHistory.tsx,
        │              ExecutionHistoryList.tsx, TestingConsole.tsx
        └── pages/AgentLibraryPage.tsx, AgentBuilderPage.tsx, AgentExecutionPage.tsx

tests/
├── AskLucy.Domain.Tests/Agents/               # entity invariant tests
├── AskLucy.Application.Tests/Agents/          # StartAgentExecutionTests, AgentApprovalWorkflowTests,
│                                               #   AgentToolPermissionTests, AgentBudgetGuardTests,
│                                               #   AgentDuplicateToolCallDetectionTests, AgentCrossUserSecurityTests,
│                                               #   AgentPlannerTests, MemoryWriteToolIntegrationTests, ...
├── AskLucy.Persistence.Tests/Agents/          # concurrency/RowVersion, query-filter/soft-delete tests
├── AskLucy.Infrastructure.Tests/Agents/       # AgentExecutionRunnerJobTests (Hangfire job), AgentExecutionHubTests
├── AskLucy.Web.Tests/Agents/                  # controller/authorization/Problem-Details tests
└── AskLucy.E2E.Tests/
    ├── AgentCreateAndRun.spec.ts
    ├── AgentToolApproval.spec.ts
    ├── AgentExecutionHistory.spec.ts
    └── AgentVersioning.spec.ts
```

**Structure Decision**: Web application (existing modular monolith) — extends the existing five backend projects (`AskLucy.Domain/Application/Infrastructure/Persistence/Web`) and the existing frontend (`AskLucy.Web/ClientApp`) with a new `Agents` feature area in each, following the identical folder shape the Prompt Library (specs/019) and AI Memory System (specs/018) features already established (research.md items 13–15). No new project, no new deployable, no new repository-root folder.

## Complexity Tracking

> No entries — the Constitution Check above recorded zero violations. Where this plan's design might look non-obvious (two new entities not named in the spec — `AgentUserExecutionLimit`, and the reserved-but-unused `AgentPolicy.OrganizationId`), the choice reduces scope relative to inventing new `SubscriptionTier`/`Organization` aggregates the codebase doesn't have yet (research.md Decisions 1–2), which is a complexity *reduction*, not a violation requiring justification.
