# Implementation Plan: Workflow & Tool Orchestration Engine

**Branch**: `022-workflow-orchestration-engine` | **Date**: 2026-08-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/022-workflow-orchestration-engine/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Add a new `Workflows` module that lets a user visually and programmatically build reusable, deterministic orchestration graphs — Start/End, AI Prompt, AI Agent, RAG Search, Memory Search, Document Processing, File Operation, MCP Tool, Native Tool, Transform, Condition, Parallel, Merge, Human Approval, Validation, and Delay nodes — over the platform's *existing* engines, never duplicating them (FR-019–FR-024). Technical approach (research.md): the Workflow Engine is a composition layer, not a second execution runtime — RAG Search/Memory Search/Document Processing/File Operation/MCP Tool/Native Tool nodes are thin adapters that resolve and call the exact same `IAgentTool` instances the Agent Runtime already uses via `AgentToolCatalog`/`McpToolRegistry` (Decision 1); the AI Prompt node reuses `IPromptRepository`/`PromptVariableResolver` for resolution and `IAIProvider`/`IAIProviderResolver` for generation, mirroring `AgentExecutionOrchestrator`'s own `ModelReasoning` step (Decision 2); the AI Agent node invokes `AgentExecutionOrchestrator.RunAsync` in-process, treating the agent as opaque (Decision 3); a new hand-rolled, closed-grammar expression evaluator (no scripting/eval library) backs Condition/Transform/Validation nodes and idempotency-key resolution (Decision 6); executions run as a new Hangfire background job (`WorkflowExecutionRunnerJob`) wrapping a new, pure `WorkflowExecutionOrchestrator`, structurally identical to `AgentExecutionRunnerJob`/`AgentExecutionOrchestrator` (Decision 7); live progress pushes over a new `WorkflowExecutionHub` SignalR hub, mirroring `AgentExecutionHub` (Decision 8); Event-Driven triggers are backed by three new, minimal MediatR post-commit notifications from the Documents/KnowledgeBases modules (Decision 12) — the one genuine cross-module touch this feature requires; and the frontend Designer introduces exactly one new dependency, `@xyflow/react`, for node-graph canvas editing (Decision 16).

## Technical Context

**Language/Version**: C# on .NET 10 (backend, all five existing `AskLucy.*` projects); TypeScript on React 19 + Vite (frontend, `src/AskLucy.Web/ClientApp`) — no new language/runtime, matches every prior feature in this repo.

**Primary Dependencies**: MediatR (CQRS), FluentValidation, AutoMapper, Entity Framework Core (SQL Server), Hangfire (background jobs — already registered), SignalR (real-time hubs), ASP.NET Core Identity (auth/roles), `JsonSchema.Net` (schema validation, already introduced by spec 021). One new backend NuGet package: none. One new frontend npm dependency: `@xyflow/react` (React Flow — node-graph canvas, research.md Decision 16). No scripting/expression NuGet package is introduced — the expression engine is new, hand-written Application-layer code (research.md Decision 6), deliberately not a dependency.

**Storage**: SQL Server via EF Core Code-First migrations against the existing `AskLucyDbContext`; every new entity inherits `BaseEntity` (Guid v7 keys, soft delete via query filter + `AuditSaveChangesInterceptor`, `RowVersion` optimistic concurrency) — see data-model.md. One new migration adds all `Workflow*` tables.

**Testing**: xUnit for `AskLucy.Domain.Tests` / `AskLucy.Application.Tests` / `AskLucy.Persistence.Tests` / `AskLucy.Infrastructure.Tests` / `AskLucy.Web.Tests`; Playwright (`*.spec.ts`) for `AskLucy.E2E.Tests` — matches the Agent Framework/MCP Integration test-folder conventions exactly.

**Target Platform**: ASP.NET Core Web API (`AskLucy.Web`) hosting the embedded React SPA (`ClientApp`) — single existing deployable, no new platform or service introduced.

**Project Type**: Web application — extends the existing modular monolith's five backend projects plus the existing frontend SPA; no new project/solution entry.

**Performance Goals**: SC-002 (node status visibility within 2s) and SC-007 (cancellation reaches a stopped state within 5s) — both satisfied by SignalR push (research.md Decision 8) plus a per-node status re-check in the Hangfire runner loop (research.md Decision 7), identical to how spec 020 already meets its equivalent goals. SC-012 (event-triggered execution starts within 1 minute for 95% of matching events) — satisfied by synchronous MediatR notification dispatch immediately after the triggering commit (research.md Decision 12), not a polling interval.

**Constraints**: No arbitrary user-supplied code execution anywhere in the graph — Condition/Transform/Validation/idempotency-key expressions are restricted to the closed grammar in contracts/workflow-expression-engine.md (FR-027/FR-062, constitution NON-NEGOTIABLE); every node's permission/risk inheritance comes from the underlying capability it wraps, never a workflow-author-declared override (FR-058); a workflow's own approval-policy configuration can never weaken the platform-mandatory approval baseline a wrapped `IAgentTool`'s `High`/`Critical` risk level already enforces (FR-036); private model chain-of-thought is never persisted or pushed (FR-053); loops must always be bounded (FR-032, never inferred, never unbounded).

**Scale/Scope**: Per-user concurrent-execution cap, administrator-configurable, defaulting to a modest platform value (FR-069/FR-070, research.md Decision 11) — same shape as spec 020's existing cap, a second independent limit (a user's agent-execution cap and workflow-execution cap are tracked separately). New surface: 3 aggregates spanning 15 entities (data-model.md), 70 functional requirements, 3 REST controllers, 1 SignalR hub, 12 new `IWorkflowNodeExecutor` implementations (6 of which are thin adapters over existing `IAgentTool`s — RagSearch, MemorySearch, DocumentProcessing, FileOperation, McpTool, NativeTool; plus AiPrompt, AiAgent, Condition, Transform, Validation, Merge), 1 Hangfire runner job, 1 new expression-evaluator component, 3 new cross-module MediatR notifications, 1 new frontend feature area, 1 new npm dependency.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Section | Status | Notes |
|---|---|---|
| I. Clean Architecture & Dependency Rule | **PASS** | New `Domain/Workflows`, `Application/Workflows`, `Infrastructure/Workflows`, `Persistence/Configurations/Workflows` follow the exact existing layer boundaries; no new project, no outward-pointing reference. `Application/Workflows` depends on `Application/Agents` (for `AgentExecutionOrchestrator`, `IAgentTool`, `AgentToolCatalog`) exactly the way `Application/Mcp` already depends on `Application/Agents` (spec 021 precedent) — a sibling-feature-folder dependency within the same layer, not a Dependency Rule violation. |
| II. SOLID | **PASS** | `IWorkflowNodeExecutor` per-node-type classes satisfy OCP (new node type = new class, contracts/workflow-node-contract.md); narrow interfaces (`IWorkflowExpressionEvaluator`, `IWorkflowExecutionRunner`, `IWorkflowExecutionNotifier`), no god interfaces. |
| III. Simplicity First (DRY/KISS/YAGNI) | **PASS** | research.md Decision 1 is the single largest DRY win in this plan — it avoids reimplementing RAG/Memory/MCP/Native tool execution entirely; Decision 4 explicitly defers a new `FileWriteTool` rather than inventing unreviewed write infrastructure; Decision 5 extracts one shared `PolicyConditionMatcher` rather than duplicating `AgentPolicyEvaluator`'s matching rule. |
| IV. Composition over Inheritance | **PASS** | Nodes composed via `IWorkflowNodeExecutor`, no inheritance hierarchy; `WorkflowExecutionPolicy`/`WorkflowRetryPolicy` are owned value-type compositions, not subclasses. |
| V. Dependency Inversion & Testability | **PASS** | Every external capability (`IAgentTool`/`AgentToolCatalog`, `AgentExecutionOrchestrator`, `IPromptRepository`, `IAIProvider`, `IJsonSchemaValidator`, `IBackgroundJobClient`, `IWorkflowExecutionNotifier`) is consumed via an interface already or newly defined in `Application`; the orchestrator and every node executor are unit-testable with all dependencies faked, matching `AgentExecutionOrchestrator`'s own testability shape exactly. |
| VI. Separation of Concerns | **PASS** | Controllers stay thin (contracts/workflows-api.md delegates every action to a Command/Query); authorization lives in `WorkflowOwnershipGuard` (Application layer), not controller `if` checks. |
| VII. Convention over Configuration | **PASS** | The dominant design driver throughout research.md — Hangfire (not a new job runner), SignalR (not SSE), MediatR notifications (not a new pub/sub library), `JsonSchema.Net` (not a second schema validator), the existing feature-folder CQRS layout, the existing `BaseEntity`/audit-interceptor/soft-delete pattern, the existing `*OwnershipGuard` shape — all reused. The one new dependency (`@xyflow/react`) is justified because no existing dependency covers node-graph canvas editing at all (Decision 16), not chosen over an existing alternative. |
| VIII. No Silent Failures (NON-NEGOTIABLE) | **PASS, enforced by design** | Every orchestrator failure path writes a `WorkflowError` row *and* transitions `WorkflowExecution.Status` to `Failed` *and* pushes `WorkflowFailed` (data-model.md/contracts/workflow-execution-events.md) — mirrors `AgentExecutionOrchestrator`'s catch-once-at-top-level pattern exactly, never a caught-and-discarded exception. A dropped SignalR connection surfaces a visible reconnecting state on the frontend (quickstart.md Scenario 6), never a silently frozen monitor. |
| §3 Architecture Rules | **PASS** | No `Domain`→`Application`/`Infrastructure` reference; `Infrastructure/Workflows` (hub, Hangfire job class) depends on `Application`/`Domain` only, implementing interfaces Application defines — identical shape to `Infrastructure/Agents`. |
| §3 CQRS rules | **PASS** | Every mutation is a MediatR command with one handler; queries never mutate; cross-cutting concerns via existing `IPipelineBehavior`s. |
| §3 Domain events | **PASS, with one deliberate extension** | `WorkflowCreated`/`WorkflowPublished`/etc. raised from aggregates per the established pattern. research.md Decision 12 additionally introduces the codebase's *first* real cross-module post-commit MediatR notification (`DocumentUploadedNotification` et al.) — explicitly called out and justified in research.md as the smallest possible touch, not a silent architectural expansion (constitution §18's "explain trade-offs before touching more than one layer's public contract" is satisfied by that decision's own Rationale/Alternatives-considered sections). |
| §5 Database Principles | **PASS** | `BaseEntity` inheritance, explicit indexes on every FK/status/owner column on a query path (data-model.md), `RowVersion` concurrency, soft delete via query filter — no new pattern. |
| §6 API Standards | **PASS** | Nouns/plural/kebab, `/api/v1/...`, action sub-resources, Problem Details errors, cursor pagination, `[Authorize]` by default — contracts/workflows-api.md. |
| §7 UI Principles | **PASS** | New `ClientApp/src/features/workflows` follows the existing feature-folder shape (`api/`, `hooks/`, `components/`, `pages/`); MUI theme wraps the React Flow canvas (its own rendering is unstyled/headless by default, themed via CSS variables bound to the MUI palette) — no bespoke styling system beyond that necessary integration. |
| §8 Security | **PASS** | Permission inheritance from the underlying capability (FR-058) is the security backbone of the entire node-executor design (Decision 1); prompt-injection defense is inherited for free from every wrapped tool's existing defenses, plus FR-060's explicit "external content never overrides instructions" rule enforced structurally (contracts/workflow-expression-engine.md §3: external content can influence what an expression evaluates to, never what the expression *is*). |
| §9 AI Principles | **PASS** | Provider/model abstraction (Decision 2), agent-as-opaque-component (Decision 3), token usage/cost tracked per execution (FR-054) — this feature adds zero new AI-provider-facing code, only new call sites into existing abstractions. |
| §10 Testing Standards | **PASS (planned)** | Test-folder plan mirrors Agents/MCP exactly; spec.md's own Testing section already enumerates Unit/Integration/Security/E2E/Performance categories mapped 1:1 onto this plan's new components (expression engine, node executors, budget guard, concurrency cap, event triggers). |
| §11–§19 (Git/CI/CD/Docs/Observability/Performance/Quality Gates/Decision Making/AI Agent Rules/DoD) | **PASS** | No deviation requested; `docs/ARCHITECTURE.md`, `docs/DOMAIN_SERVICES.md`, `docs/ENTITY_MODEL.md` will be updated to reflect this plan during implementation (documentation-is-part-of-implementation, constitution §13), not before. |

No violation requires an entry in Complexity Tracking — the one design choice that might look non-obvious at a glance (a second, workflow-specific `WorkflowApproval`/`WorkflowPolicy` pair instead of literally reusing `AgentApproval`/`AgentPolicy`) is justified in research.md Decision 5 as avoiding a worse ambiguous-ownership problem, not as added complexity for its own sake.

## Post-Design Constitution Check

*Re-checked after Phase 1 (data-model.md, contracts/, quickstart.md).* No new violation introduced by the detailed design: `WorkflowNode`/`WorkflowConnection`/`WorkflowVariable` materializing only at publish time from `Workflow.DraftDefinitionJson` (research.md Decision 19) keeps every entity's ownership single and unambiguous, consistent with §3/§5; the `IWorkflowNodeExecutor` registry (contracts/workflow-node-contract.md) keeps `WorkflowExecutionOrchestrator` closed for modification exactly as `AgentExecutionOrchestrator`'s tool-catalog lookup already does (§2.II OCP); the one cross-module addition (three MediatR notifications published from existing Documents/KnowledgeBases command handlers, research.md Decision 12) extends those handlers' existing post-commit step rather than reaching into their internals from `Application/Workflows`, so no new `Application`-to-`Application` sibling coupling is introduced beyond the pattern spec 020's `CreateMemoryCandidateCommand` cross-command delegation already established. Gate: **PASS**, unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/022-workflow-orchestration-engine/
├── plan.md                              # This file (/speckit-plan command output)
├── research.md                          # Phase 0 output (/speckit-plan command)
├── data-model.md                        # Phase 1 output (/speckit-plan command)
├── quickstart.md                        # Phase 1 output (/speckit-plan command)
├── contracts/                           # Phase 1 output (/speckit-plan command)
│   ├── workflows-api.md
│   ├── workflow-node-contract.md
│   ├── workflow-execution-events.md
│   └── workflow-expression-engine.md
├── checklists/
│   └── requirements.md
└── tasks.md                             # Phase 2 output (/speckit-tasks command — NOT created by /speckit-plan)
```

### Source Code (repository root)

This is the existing Ask Lucy Clean Architecture modular monolith — the feature adds new folders inside the five existing backend projects plus the existing frontend, introducing zero new projects:

```text
src/
├── AskLucy.Domain/
│   └── Workflows/                            # NEW — entities + enums from data-model.md
│       ├── Workflow.cs, WorkflowVersion.cs, WorkflowNode.cs, WorkflowConnection.cs,
│       │   WorkflowVariable.cs, WorkflowExecution.cs, WorkflowExecutionNode.cs,
│       │   WorkflowExecutionEvent.cs, WorkflowApproval.cs, WorkflowError.cs,
│       │   WorkflowExecutionUsage.cs, WorkflowExecutionCost.cs, WorkflowPolicy.cs,
│       │   WorkflowUserExecutionLimit.cs, WorkflowAuditLog.cs,
│       │   WorkflowConcurrencyLimitExceededException.cs
│       └── (enums; WorkflowExecutionPolicy.cs, WorkflowRetryPolicy.cs value objects)
│
├── AskLucy.Application/
│   ├── Abstractions/                          # EXTENDED — new repo/service interfaces (existing flat convention)
│   │   ├── IWorkflowRepository.cs, IWorkflowExecutionRepository.cs, IWorkflowPolicyRepository.cs
│   │   ├── IWorkflowAuditLogRepository.cs, IWorkflowExecutionNotifier.cs, IWorkflowExecutionRunner.cs
│   │   └── IWorkflowExpressionEvaluator.cs
│   ├── Common/
│   │   └── PolicyConditionMatcher.cs          # NEW — extracted, shared by AgentPolicyEvaluator and WorkflowPolicyEvaluator (research.md Decision 5)
│   ├── Workflows/                             # NEW feature folder — mirrors Agents/ shape
│   │   ├── Commands/
│   │   │   ├── CreateWorkflow/ UpdateWorkflow/ DeleteWorkflow/ ArchiveWorkflow/ RestoreWorkflow/
│   │   │   │   DuplicateWorkflow/ ValidateWorkflow/ PublishWorkflowVersion/
│   │   │   ├── StartWorkflowExecution/ PauseWorkflowExecution/ ResumeWorkflowExecution/
│   │   │   │   CancelWorkflowExecution/ RetryWorkflowExecutionNode/
│   │   │   │   ApproveWorkflowNode/ RejectWorkflowNode/ RequestWorkflowNodeChanges/
│   │   │   └── CreateWorkflowPolicy/ UpdateWorkflowPolicy/ DeleteWorkflowPolicy/
│   │   │       SetWorkflowUserExecutionLimit/
│   │   ├── Queries/
│   │   │   ├── GetWorkflow/ ListWorkflows/ ListWorkflowVersions/ GetWorkflowVersion/
│   │   │   │   GetWorkflowStatistics/
│   │   │   └── GetWorkflowExecution/ ListWorkflowExecutions/ GetWorkflowExecutionEvents/
│   │   │       GetWorkflowExecutionNodes/ GetWorkflowExecutionUsage/ GetWorkflowApproval/
│   │   ├── Authorization/
│   │   │   ├── WorkflowOwnershipGuard.cs
│   │   │   └── WorkflowExecutionOwnershipGuard.cs
│   │   ├── Validation/
│   │   │   └── WorkflowGraphValidator.cs      # FR-016 — disconnected nodes, missing Start/End, cycles, etc.; type-checking itself is IWorkflowExpressionEvaluator.ValidateTypes, not a separate class
│   │   ├── Expressions/
│   │   │   ├── WorkflowExpressionEvaluator.cs, WorkflowExpressionParser.cs, WorkflowExpressionAst.cs
│   │   ├── EventTriggers/
│   │   │   ├── DocumentUploadedNotification.cs, DocumentProcessedNotification.cs,
│   │   │   │   KnowledgeBaseUpdatedNotification.cs   # NEW — published from Documents/KnowledgeBases handlers
│   │   │   └── WorkflowEventTriggerHandler.cs         # INotificationHandler<T>, one per event type
│   │   └── Runtime/                            # node executors + orchestration (provider-agnostic, testable without Hangfire/SignalR)
│   │       ├── IWorkflowNodeExecutor.cs, WorkflowNodeExecutorRegistry.cs
│   │       ├── RagSearchNodeExecutor.cs, MemorySearchNodeExecutor.cs, DocumentProcessingNodeExecutor.cs,
│   │       │   FileOperationNodeExecutor.cs, McpToolNodeExecutor.cs, NativeToolNodeExecutor.cs,
│   │       │   PromptNodeExecutor.cs, AgentNodeExecutor.cs, ConditionNodeExecutor.cs,
│   │       │   TransformNodeExecutor.cs, ValidationNodeExecutor.cs, MergeNodeExecutor.cs
│   │       ├── WorkflowExecutionOrchestrator.cs  # the graph-walk loop; called by the Hangfire job, unit-testable standalone
│   │       ├── WorkflowBudgetGuard.cs            # FR-055 limits
│   │       └── WorkflowPolicyEvaluator.cs        # FR-035/FR-036, uses PolicyConditionMatcher
│   ├── Documents/                                # EXTENDED — publish DocumentUploadedNotification/DocumentProcessedNotification
│   │   └── (existing UploadDocumentCommandHandler, DocumentProcessingPipeline — one IPublisher.Publish call added post-commit)
│   └── KnowledgeBases/                           # EXTENDED — publish KnowledgeBaseUpdatedNotification
│       └── (existing UpdateKnowledgeBaseCommandHandler et al. — one IPublisher.Publish call added post-commit)
│
├── AskLucy.Infrastructure/
│   └── Workflows/                               # NEW
│       ├── WorkflowExecutionHub.cs               # SignalR hub (contracts/workflow-execution-events.md)
│       ├── WorkflowExecutionNotifier.cs          # IWorkflowExecutionNotifier, wraps IHubContext<WorkflowExecutionHub>
│       └── WorkflowExecutionRunnerJob.cs         # IWorkflowExecutionRunner, dispatched via Hangfire (research.md Decision 7)
│
├── AskLucy.Persistence/
│   ├── Configurations/Workflows/                 # NEW — one IEntityTypeConfiguration<T> per entity, EF Fluent API
│   └── Repositories/Workflows/                   # NEW — IWorkflowRepository/IWorkflowExecutionRepository/IWorkflowPolicyRepository implementations
│   # AskLucyDbContext gains new DbSets + one new EF Core migration
│
└── AskLucy.Web/
    ├── Controllers/v1/
    │   ├── WorkflowsController.cs                # NEW
    │   ├── WorkflowExecutionsController.cs        # NEW
    │   └── WorkflowPoliciesController.cs          # NEW
    └── ClientApp/
        ├── package.json                           # EXTENDED — adds @xyflow/react (research.md Decision 16)
        └── src/features/workflows/                # NEW — mirrors features/agents/ shape
            ├── api/workflowsApi.ts, workflowExecutionsApi.ts, workflowPoliciesApi.ts
            ├── hooks/useWorkflows.ts, useWorkflowMutations.ts, useWorkflowExecution.ts,
            │         useWorkflowVersions.ts, useWorkflowExecutionHub.ts
            ├── components/WorkflowCanvas.tsx, NodePalette.tsx, NodeConfigPanel.tsx,
            │              ValidationPanel.tsx, ExecutionMonitor.tsx, ApprovalDialog.tsx,
            │              VersionHistory.tsx, ExecutionHistoryList.tsx, StatisticsDashboard.tsx
            └── pages/WorkflowLibraryPage.tsx, WorkflowDesignerPage.tsx, WorkflowExecutionPage.tsx

tests/
├── AskLucy.Domain.Tests/Workflows/               # entity invariant tests
├── AskLucy.Application.Tests/Workflows/          # WorkflowGraphValidatorTests, WorkflowExpressionEngineSecurityTests,
│                                                  #   WorkflowExecutionOrchestratorTests, WorkflowBudgetGuardTests,
│                                                  #   WorkflowConcurrencyLimitTests, WorkflowApprovalPolicyTests,
│                                                  #   WorkflowEventTriggerHandlerTests, WorkflowCrossUserSecurityTests, ...
├── AskLucy.Persistence.Tests/Workflows/          # concurrency/RowVersion, query-filter/soft-delete tests
├── AskLucy.Infrastructure.Tests/Workflows/       # WorkflowExecutionRunnerJobTests (Hangfire job), WorkflowExecutionHubTests
├── AskLucy.Web.Tests/Workflows/                  # controller/authorization/Problem-Details tests
└── AskLucy.E2E.Tests/
    ├── WorkflowCreateAndRun.spec.ts
    ├── WorkflowDesignerCanvas.spec.ts
    ├── WorkflowApprovalGate.spec.ts
    ├── WorkflowFailureRecovery.spec.ts
    ├── WorkflowVersioning.spec.ts
    └── WorkflowEventTrigger.spec.ts
```

**Structure Decision**: Web application (existing modular monolith) — extends the existing five backend projects (`AskLucy.Domain/Application/Infrastructure/Persistence/Web`) and the existing frontend (`AskLucy.Web/ClientApp`) with a new `Workflows` feature area in each, following the identical folder shape the Agent Framework (specs/020) and MCP Integration (specs/021) features already established. No new project, no new deployable, no new repository-root folder. The only new external dependency across the whole feature is `@xyflow/react` on the frontend (research.md Decision 16).

## Complexity Tracking

> No entries — the Constitution Check above recorded zero violations. Two design choices might look non-obvious at a glance and are addressed here for completeness even though neither requires justification as a violation:
>
> - **A second `WorkflowApproval`/`WorkflowPolicy` pair instead of reusing `AgentApproval`/`AgentPolicy`** (research.md Decision 5) — reduces complexity relative to the alternative (a dual-purpose entity with two mutually exclusive optional FKs), it does not add it.
> - **Three new cross-module MediatR notifications touching Documents/KnowledgeBases** (research.md Decision 12) — the smallest possible mechanism for a feature requirement (Event-Driven workflows) that is structurally impossible to satisfy without *some* signal from those modules; the alternative (polling) would have been strictly more code and worse latency (SC-012), not less coupling.
