# Implementation Plan: MCP (Model Context Protocol) Integration

**Branch**: `021-mcp-integration` | **Date**: 2026-08-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/021-mcp-integration/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Add a new `Mcp` module that lets administrators register, connect to, and monitor external MCP servers, and lets any authorized user enable administrator-activated MCP tools/resources/prompts for their own agents — implemented as a second, dynamic source composed into the *existing* compile-time `AgentToolCatalog` (research.md Decision 1) via a namespaced tool identifier (`mcp:{serverId}:{toolName}`, Decision 3) that requires zero schema change to spec 020's `AgentTool`/`AgentToolCall`/`AgentPolicy` tables — never a second, parallel tool execution framework (FR-019). Technical approach (research.md): wrap the official MCP C# SDK behind Application-owned `IMcpClient`/`IMcpClientFactory` interfaces (Decision 2) so Domain/Application never reference protocol/transport code directly; gate every newly discovered tool behind mandatory administrator activation regardless of the risk level the server itself claims (Decision 4, clarification); extend the `AgentToolPermission` enum with new external-data-oriented values (Decision 5); reuse the existing `AgentApproval`/`AgentPolicy` approval-gate and audit machinery verbatim (Decision 6); encrypt credentials with the same ASP.NET Core Data Protection pattern `AiCredentialProtector` already established (Decision 7); build this codebase's first SSRF/endpoint-validation utility, since none exists today, re-checked at registration *and* at every connection (Decision 8); adopt `JsonSchema.Net` as a new dependency to validate arbitrary externally-supplied tool schemas, since every existing native tool instead hand-validates its own known shape (Decision 9); run health checks and capability refresh as new Hangfire recurring jobs mirroring `MemoryExtractionSweepJob` (Decision 10); make outbound calls through a new named `IHttpClientFactory` client with hand-rolled retry/circuit-breaker, since no Polly dependency exists anywhere in this codebase (Decision 11); enforce per-server/tool/user/agent concurrency and rate limits in-process via `System.Threading.RateLimiting` primitives at the point of each call, distinct from the ASP.NET Core inbound-request rate-limiting middleware that only governs the new admin/browsing REST endpoints (Decision 12); skip a fifth SignalR hub since this spec sets no sub-second live-update requirement, unlike spec 020's Agent Execution Hub (Decision 13); and keep MCP prompts as their own read-only, admin-registry-scoped entity rather than forcing them into the user-owned `Prompt` table, reusing the existing `DuplicatePromptCommand` pattern as the "customize" escape hatch (Decision 16, clarification).

## Technical Context

**Language/Version**: C# on .NET 10 (backend, all five existing `AskLucy.*` projects); TypeScript on React 19 + Vite (frontend, `src/AskLucy.Web/ClientApp`) — no new language/runtime, matches spec 020 and every prior feature in this repo.

**Primary Dependencies**: Existing (reused, no new usage pattern): MediatR (CQRS), FluentValidation, AutoMapper, Entity Framework Core (SQL Server), Hangfire (already registered — two new recurring jobs, research.md Decision 10), SignalR (existing hubs untouched — no new hub, Decision 13), ASP.NET Core Identity, ASP.NET Core `RateLimiting` middleware (two new named policies), ASP.NET Core Data Protection (`AiCredentialProtector`'s pattern reused with a distinct purpose string, Decision 7). **New**: the official MCP C# SDK (`ModelContextProtocol` NuGet package, Decision 2) and `JsonSchema.Net` (Decision 9) — research.md's Technology Summary table.

**Storage**: SQL Server via EF Core Code-First migrations against the existing `AskLucyDbContext`; every new entity inherits `BaseEntity` (Guid v7 keys, soft delete via query filter + `AuditSaveChangesInterceptor`, `RowVersion` optimistic concurrency) — see data-model.md. Zero schema changes to any spec 020 table.

**Testing**: xUnit for `AskLucy.Domain.Tests` / `AskLucy.Application.Tests` / `AskLucy.Persistence.Tests` / `AskLucy.Infrastructure.Tests` / `AskLucy.Web.Tests`; Playwright (`*.spec.ts`) for `AskLucy.E2E.Tests` — matches every existing feature's test-folder convention exactly, extended with `Mcp`-named subfolders alongside the existing `Agents` ones.

**Target Platform**: ASP.NET Core Web API (`AskLucy.Web`) hosting the embedded React SPA (`ClientApp`) — single existing deployable, no new platform or service introduced.

**Project Type**: Web application — extends the existing modular monolith's five backend projects plus the existing frontend SPA; no new project/solution entry.

**Performance Goals**: SC-001 (register → verified connectivity → discoverable tools within 2 minutes, achieved synchronously via the on-demand test-connection/refresh-capabilities actions, not by waiting on the recurring-job cadence). SC-006 (a health transition reflected within one health-check cycle — Decision 10's 5-minute default cadence, tunable via `McpRuntimeOptions`). FR-051 (per-call response-size and duration caps enforced by `McpToolAdapter`/`IJsonSchemaValidator`, Decision 9/contracts/mcp-security-model.md).

**Constraints**: MCP tool/resource/prompt output is never elevated to a system/developer instruction (FR-030/FR-035) — reuses spec 020's existing `RetrievalPromptFraming`-style structural separation, no new framing mechanism. Every newly discovered tool is unusable until an administrator activates it, independent of server-declared risk (FR-022, Decision 4). Credentials never leave the server process and are never logged/audited in plaintext (FR-045/FR-046, Decision 7). Every remote connection is SSRF-validated at registration *and* at every connection, not once (FR-050, Decision 8). No blind retry of a non-idempotent tool call with an ambiguous outcome (FR-054, Decision 11).

**Scale/Scope**: Platform-wide, administrator-managed registry — not per-organization/tenant (Assumptions, consistent with spec 020's `AgentPolicy.OrganizationId` precedent: reserved-but-unused there, not modeled at all here since there is no forward-looking spec requirement to reserve a column for). New surface: 1 new aggregate cluster spanning 8 entities (data-model.md) — `McpServer`, `McpServerCredential`, `McpServerHealth`, `McpCapabilitySnapshot`, `McpTool`, `McpResource`, `McpPrompt`, `McpAuditLog` — plus an additive extension to the existing `AgentToolPermission` enum — 66 functional requirements, 2 new REST controllers (`McpServersController`, `McpCatalogController` — no new controller for "enable an MCP tool for an agent," which reuses spec 020's existing `PUT /agents/{id}`), 2 new Hangfire recurring jobs, 0 new SignalR hubs, 3 new NuGet dependencies (`ModelContextProtocol`, `JsonSchema.Net`, and `System.Threading.RateLimiting` — the third discovered during implementation to be a discrete package rather than an automatic transitive dependency, research.md Decision 12's implementation-correction note).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Section | Status | Notes |
|---|---|---|
| I. Clean Architecture & Dependency Rule | **PASS** | New `Domain/Mcp`, `Application/Mcp`, `Infrastructure/Mcp`, `Persistence/Configurations/Mcp` follow the exact existing layer boundaries; no new project. `IMcpClient`/`IMcpClientFactory` are Application-owned interfaces — the `ModelContextProtocol` SDK is referenced only from `Infrastructure` (research.md Decision 2), never from `Application`/`Domain`. |
| II. SOLID | **PASS** | `McpToolAdapter`/`McpResourceReadTool` satisfy OCP exactly as spec 020's built-in tools do — a new MCP capability is new data (an `McpTool` row) plus, at most, one small adapter class, never an edit to `AgentExecutionOrchestrator`/`AgentPlanner` (contracts/mcp-tool-adapter.md). Narrow interfaces (`IMcpClient`, `IMcpClientFactory`, `IMcpEndpointValidator`, `IMcpCredentialProtector`, `IMcpToolRegistry`, `IJsonSchemaValidator`, `IMcpRateLimiter`), no god interface. |
| III. Simplicity First (DRY/KISS/YAGNI) | **PASS** | research.md Decisions 13 (no fifth SignalR hub) and 16 (no `Prompt`-table ownership hack) explicitly choose the smaller design where the spec's literal wording could have justified a larger one — matching spec 020's own precedent (its Decisions 1/2) of preferring the restrictive, simpler option. |
| IV. Composition over Inheritance | **PASS** | `McpToolAdapter` composes `IMcpClientFactory`/`IMcpRateLimiter`/`IJsonSchemaValidator` via constructor injection, no inheritance hierarchy; one adapter class per tool instance (data-driven), not a class hierarchy per tool type. |
| V. Dependency Inversion & Testability | **PASS** | Every external capability (`IMcpClient`, `IMcpClientFactory`, `IMcpEndpointValidator`, `IMcpCredentialProtector`, `IMcpRateLimiter`, `IJsonSchemaValidator`) is an Application-defined interface; `McpToolAdapter`, the orchestrator's extended `AgentToolCatalog`, and every command handler are unit-testable with all dependencies faked — no database/network/filesystem required. |
| VI. Separation of Concerns | **PASS** | `McpServersController`/`McpCatalogController` stay thin (every action delegates to a Command/Query, contracts/mcp-api.md); SSRF/credential/rate-limit logic lives in dedicated Application-owned services, not scattered controller `if` checks. |
| VII. Convention over Configuration | **PASS, with two deliberate, justified new-dependency exceptions** | Repository shape mirrors `IAgentPolicyRepository` exactly (Decision 14); background jobs reuse Hangfire recurring-job convention (Decision 10); credential encryption reuses the Data Protection pattern (Decision 7); rate limiting reuses `System.Threading.RateLimiting` primitives already transitively present (Decision 12); no new SignalR mechanism (Decision 13). The two genuinely new dependencies (MCP SDK, JSON Schema validator) are justified in Complexity Tracking below — both are protocol/security necessities with no existing in-repo equivalent, not convenience additions. |
| VIII. No Silent Failures (NON-NEGOTIABLE) | **PASS, enforced by design** | Every MCP-side failure path writes an `McpAuditLog`/`McpServerHealth` row *and* resolves to a normal, visible `AgentToolCall` failure at the execution-history level (research.md Decision 17) — never a caught-and-discarded exception. `IJsonSchemaValidator` rejections are always a recorded, user-visible tool-call failure, never a silently-passed-through malformed payload. |
| §3 Architecture Rules | **PASS** | No `Domain`→`Application`/`Infrastructure` reference; `Application/Mcp` depends only on `Domain` and `Application/Abstractions`; `Infrastructure/Mcp` (SDK wrapper, credential protector, endpoint validator, Hangfire jobs) depends on `Application`/`Domain` only, implementing interfaces Application defines. |
| §3 CQRS rules | **PASS** | Every mutation (`RegisterMcpServerCommand`, `ActivateMcpToolCommand`, etc.) is a MediatR command with one handler; queries never mutate; cross-cutting concerns via existing `IPipelineBehavior`s. |
| §3 Domain events | **PASS** | `McpServerRegistered`, `McpServerEnabled/Disabled`, `McpServerRemovalBlocked/Removed` raised from the `McpServer` aggregate, dispatched post-commit — matches the existing event-raising convention (data-model.md). |
| §5 Database Principles | **PASS** | `BaseEntity` inheritance, explicit indexes on `(Endpoint, Transport)` unique constraint and every FK/status column on a query path (data-model.md), `RowVersion` concurrency, soft delete via query filter — no new pattern. Zero changes to spec 020's schema. |
| §6 API Standards | **PASS** | Nouns/plural/kebab, `/api/v1/...`, action sub-resources (`.../actions/{verb}`), Problem Details errors, cursor pagination, `[Authorize]` by default, new named rate-limit policies following the existing 11-policy convention (contracts/mcp-api.md). |
| §7 UI Principles | **PASS** | New `ClientApp/src/features/mcp` (admin) follows the exact existing feature-folder shape; MUI theme, no bespoke styling. Frontend detail is a task-phase concern — this plan's contracts define the API surface the UI consumes. |
| §8 Security | **PASS, this feature's central concern** | Every Security Requirements item in spec.md maps to a specific research.md decision and contracts/mcp-security-model.md control: SSRF (Decision 8), credentials (Decision 7), TLS (contracts/mcp-security-model.md), untrusted content/prompt injection (reuses spec 020's existing `RetrievalPromptFraming` framing, no new mechanism), rate limiting (Decision 12), audit (data-model.md `McpAuditLog`). |
| §9 AI Principles | **PASS** | Agent tool set remains explicit/scoped/bounded (constitution's own words) — MCP only widens *which* tools can be in that set, never how the boundary itself works; every MCP call is still authorized, logged, and bounded exactly like a native tool call. |
| §10 Testing Standards | **PASS (planned)** | Test-folder plan mirrors the `Agents` feature exactly (`Mcp`-named subfolders per project); unit/integration/security categories mapped directly onto spec.md's own Testing section (SSRF tests, prompt-injection tests, credential-exposure tests, schema-validation tests all named explicitly there). |
| §11–§19 (Git/CI/CD/Docs/Observability/Performance/Quality Gates/Decision Making/AI Agent Rules/DoD) | **PASS** | No deviation requested; `docs/ARCHITECTURE.md` §16, `docs/ENTITY_MODEL.md` §11, `docs/DATABASE.md` §12, `docs/DOMAIN_SERVICES.md` §23, `docs/API_GUIDELINES.md` §27 will be rewritten to match this plan during implementation (documentation-is-part-of-implementation, constitution §13), not before — research.md's opening note lists exactly which pre-existing placeholder sections these supersede. |

Both new-dependency additions (MCP SDK, JSON Schema validator) are recorded in Complexity Tracking below per constitution §17's ADR trigger ("introduces a new... cross-cutting infrastructure dependency") — not because either is a violation, but because a new NuGet dependency is exactly the class of decision constitution §17 asks to be recorded with alternatives considered, which research.md Decisions 2 and 9 already do.

## Post-Design Constitution Check

*Re-checked after Phase 1 (data-model.md, contracts/, quickstart.md).* No new violation introduced by the detailed design. The one design element most worth re-confirming post-design — `McpToolAdapter` living entirely inside `Application` (research.md Decision 1/contracts/mcp-tool-adapter.md) while depending only on `IMcpClientFactory`/`IMcpRateLimiter`/`IJsonSchemaValidator` interfaces — still holds: nothing in `Infrastructure/Mcp`'s concrete SDK usage leaks into `Application`, matching §3's Dependency Rule exactly as strictly as spec 020's `AgentExecutionHub`/Hangfire split did for its own Infrastructure boundary. The two-level failure categorization (research.md Decision 17) was added during design specifically to resolve an apparent conflict between FR-032 and FR-033 without weakening either — recorded here because it's the one place Phase 1 design work changed a Phase 0 assumption (initially, one `McpFailureCategory` enum was assumed to replace `AgentExecutionErrorCategory.ToolFailure` outright at the execution-history level; data-model.md instead keeps them separate, by audience). Gate: **PASS**.

## Project Structure

### Documentation (this feature)

```text
specs/021-mcp-integration/
├── plan.md                       # This file (/speckit-plan command output)
├── research.md                   # Phase 0 output (/speckit-plan command)
├── data-model.md                 # Phase 1 output (/speckit-plan command)
├── quickstart.md                 # Phase 1 output (/speckit-plan command)
├── contracts/                    # Phase 1 output (/speckit-plan command)
│   ├── mcp-api.md
│   ├── mcp-tool-adapter.md
│   ├── mcp-security-model.md
│   └── mcp-lifecycle-events.md
├── checklists/
│   └── requirements.md
└── tasks.md                      # Phase 2 output (/speckit-tasks command — NOT created by /speckit-plan)
```

### Source Code (repository root)

This is the existing Ask Lucy Clean Architecture modular monolith (`docs/ARCHITECTURE.md` §4/§7) — the feature adds new folders inside the five existing backend projects plus the existing frontend, introducing zero new projects. It extends, but does not modify the schema of, spec 020's `Agents` feature area:

```text
src/
├── AskLucy.Domain/
│   └── Mcp/                                  # NEW — entities + enums from data-model.md
│       ├── McpServer.cs, McpServerCredential.cs, McpServerHealth.cs,
│       │   McpCapabilitySnapshot.cs, McpTool.cs, McpResource.cs, McpPrompt.cs,
│       │   McpAuditLog.cs
│       └── (enums: McpServerTransport, McpAuthenticationType, McpServerHealthStatus,
│                   McpToolActivationStatus, McpFailureCategory, McpAuditAction)
│
├── AskLucy.Application/
│   ├── Abstractions/                         # EXTENDED — new interfaces added here (existing flat convention)
│   │   ├── IMcpServerRepository.cs, IMcpToolRepository.cs, IMcpResourceRepository.cs,
│   │   │   IMcpPromptRepository.cs, IMcpAuditLogRepository.cs
│   │   ├── IMcpClient.cs, IMcpClientFactory.cs                  # research.md Decision 2
│   │   ├── IMcpEndpointValidator.cs                              # research.md Decision 8
│   │   ├── IMcpCredentialProtector.cs                            # research.md Decision 7
│   │   ├── IJsonSchemaValidator.cs                               # research.md Decision 9
│   │   └── IMcpRateLimiter.cs, IMcpConcurrencyLimiter.cs         # research.md Decision 12
│   ├── Agents/
│   │   └── Tools/                            # EXTENDED (spec 020's existing folder — not moved)
│   │       ├── AgentToolCatalog.cs            # MODIFIED — merges native + IMcpToolRegistry.ActiveTools (Decision 1)
│   │       └── IAgentTool.cs                  # MODIFIED — AgentToolPermission gains 6 new values (Decision 5)
│   └── Mcp/                                   # NEW feature folder — mirrors Agents/ shape
│       ├── Commands/
│       │   ├── RegisterMcpServer/ UpdateMcpServer/ EnableMcpServer/ DisableMcpServer/
│       │   │   DeleteMcpServer/ TestMcpServerConnection/ RefreshMcpCapabilities/
│       │   │   RotateMcpServerCredential/ ActivateMcpTool/ DeactivateMcpTool/
│       │   └── DuplicateMcpPrompt/            # extends Prompts, research.md Decision 16
│       ├── Queries/
│       │   ├── GetMcpServer/ ListMcpServers/ GetMcpServerHealth/ ListMcpServerReferences/
│       │   │   ListMcpServerTools/ ListMcpAuditLog/
│       │   └── ListAvailableMcpTools/ GetMcpTool/ ListAvailableMcpResources/
│       │       ListAvailableMcpPrompts/
│       ├── Authorization/
│       │   └── McpServerAdministrationGuard.cs  # AdministratorOrSuperUser, mirrors AgentPolicy's guard
│       ├── Tools/                             # IAgentTool adapters (contracts/mcp-tool-adapter.md)
│       │   ├── McpToolAdapter.cs, McpResourceReadTool.cs
│       │   └── McpToolRegistry.cs             # IMcpToolRegistry implementation
│       └── Resilience/
│           └── McpConnectionResiliencePolicy.cs   # research.md Decision 11
│
├── AskLucy.Infrastructure/
│   └── Mcp/                                   # NEW
│       ├── McpClient.cs, McpClientFactory.cs   # wraps the ModelContextProtocol SDK (Decision 2)
│       ├── McpEndpointValidator.cs             # SSRF checks (Decision 8)
│       ├── McpCredentialProtector.cs           # Data Protection (Decision 7)
│       ├── JsonSchemaValidator.cs              # wraps JsonSchema.Net (Decision 9)
│       ├── McpRateLimiter.cs                   # System.Threading.RateLimiting (Decision 12)
│       ├── McpServerHealthCheckJob.cs          # Hangfire recurring (Decision 10)
│       └── McpCapabilityRefreshJob.cs          # Hangfire recurring (Decision 10)
│
├── AskLucy.Persistence/
│   ├── Configurations/Mcp/                    # NEW — one IEntityTypeConfiguration<T> per entity
│   └── Repositories/Mcp/                      # NEW — repository implementations (research.md Decision 14)
│   # AskLucyDbContext gains new DbSets + one new EF Core migration; zero changes to existing DbSets
│
└── AskLucy.Web/
    ├── Controllers/v1/
    │   ├── McpServersController.cs             # NEW (admin)
    │   └── McpCatalogController.cs             # NEW (any user)
    ├── Program.cs                              # MODIFIED — 2 new recurring-job registrations, 2 new rate-limit policies, new HttpClient "Mcp"
    └── ClientApp/src/features/mcp/             # NEW — mirrors features/agents/ shape exactly
        ├── api/mcpServersApi.ts, mcpCatalogApi.ts
        ├── hooks/useMcpServers.ts, useMcpServerMutations.ts, useMcpCatalog.ts
        ├── components/McpServerList.tsx, McpServerForm.tsx, McpToolActivationPanel.tsx,
        │              McpHealthBadge.tsx, McpAuditLogTable.tsx, McpToolPicker.tsx (used by AgentBuilder)
        └── pages/McpAdministrationPage.tsx

tests/
├── AskLucy.Domain.Tests/Mcp/                  # entity invariant tests
├── AskLucy.Application.Tests/Mcp/             # McpEndpointValidatorTests (SSRF), McpToolAdapterTests,
│                                               #   McpToolActivationTests, McpServerRemovalBlockingTests,
│                                               #   McpCredentialRotationTests, McpRateLimiterTests,
│                                               #   McpAgentToolCatalogCompositionTests (Decision 1 merge)
├── AskLucy.Persistence.Tests/Mcp/             # concurrency/RowVersion, endpoint-uniqueness constraint tests
├── AskLucy.Infrastructure.Tests/Mcp/          # McpClientFactoryTests, JsonSchemaValidatorTests,
│                                               #   McpServerHealthCheckJobTests, McpCredentialProtectorTests
├── AskLucy.Web.Tests/Mcp/                     # controller/authorization/Problem-Details tests
└── AskLucy.E2E.Tests/
    ├── McpServerRegisterAndDiscover.spec.ts
    ├── McpToolActivationAndAgentUse.spec.ts
    ├── McpHighRiskApproval.spec.ts
    └── McpCredentialRotation.spec.ts
```

**Structure Decision**: Web application (existing modular monolith) — extends the same five backend projects (`AskLucy.Domain/Application/Infrastructure/Persistence/Web`) and the existing frontend (`AskLucy.Web/ClientApp`) with a new `Mcp` feature area in each, following the identical folder shape spec 020's `Agents` feature already established, plus one modification each to `Agents/Tools/AgentToolCatalog.cs` and `Agents/Tools/IAgentTool.cs` (the two integration seams, research.md Decisions 1 and 5). No new project, no new deployable, no new repository-root folder.

## Complexity Tracking

> Three entries, all new NuGet dependencies — recorded per constitution §17 ("a decision requires an ADR when it introduces a new... cross-cutting infrastructure dependency"), not because any is a Constitution Check violation (the Constitution Check above is a clean PASS). The third (`System.Threading.RateLimiting`) was discovered during implementation to be a genuine new package, not the transitive dependency research.md's Decision 12 originally assumed — corrected there and recorded here for completeness.

| New Dependency | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| `ModelContextProtocol` (official MCP C# SDK) | The Agent Runtime must speak a real, versioned wire protocol (JSON-RPC 2.0 over Streamable HTTP/stdio, with capability negotiation) to external, third-party MCP servers this codebase doesn't control — no in-repo equivalent exists (research.md Decision 2) | Hand-rolling JSON-RPC/Streamable-HTTP framing was rejected: it reimplements a spec-compliant protocol this codebase has zero prior experience shipping correctly, at a security-sensitive boundary, for no benefit over a maintained SDK — the opposite of what YAGNI/KISS argue for |
| `JsonSchema.Net` | MCP tool schemas are arbitrary and supplied by an untrusted external party at runtime (FR-025/FR-026); every existing `IAgentTool` instead hand-validates its own fixed, known shape in C# — that approach is infeasible for schemas the codebase has never seen before deployment (research.md Decision 9) | A hand-rolled minimal validator (`type`/`required`/`properties` only) was rejected: this is a security boundary ("reject malformed parameters" against an untrusted server), and a partial implementation would silently under-validate constructs (`additionalProperties`, `enum`, numeric bounds) a malicious or buggy server could exploit |
| `System.Threading.RateLimiting` | FR-052/FR-053's per-server/tool/user/agent limits on outbound MCP calls (from the Hangfire runner, never the HTTP pipeline) need the same rate/concurrency primitives ASP.NET Core's middleware uses, but that middleware only ships inside the shared framework for HTTP requests — the underlying primitives require this separate package to use programmatically (research.md Decision 12) | Hand-rolling a token-bucket/semaphore limiter was rejected: this is a first-party Microsoft package, version-locked with every other `Microsoft.Extensions.*` reference already in the project, and is the exact mechanism the framework's own middleware uses — reimplementing it would be duplicating a solved problem for no benefit |
