---

description: "Task list for MCP (Model Context Protocol) Integration"
---

# Tasks: MCP (Model Context Protocol) Integration

**Input**: Design documents from `/specs/021-mcp-integration/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: Included. The constitution (§10 Testing Standards, §16 Quality Gates, §19 Definition of Done) mandates tests for all new observable behavior in the same PR that introduces it, spec.md's own "Testing" section explicitly enumerates Unit/Integration/Security/E2E categories for this feature, and every prior feature in this repo (Prompts, Memory, Agents) ships with full-depth test coverage — this is a standing, repo-wide "explicit request," not an optional add-on.

**Organization**: Tasks are grouped by user story (spec.md's 7 prioritized stories) to enable independent implementation and testing of each.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps to spec.md's US1–US7; omitted for Setup/Foundational/Polish tasks
- Every task names its exact file path(s)

## Path Conventions

Existing modular monolith (plan.md Project Structure) — no new projects: `src/AskLucy.Domain/Mcp/`, `src/AskLucy.Application/Mcp/`, `src/AskLucy.Application/Abstractions/`, `src/AskLucy.Infrastructure/Mcp/`, `src/AskLucy.Persistence/{Configurations,Repositories}/Mcp/`, `src/AskLucy.Web/Controllers/v1/`, `src/AskLucy.Web/ClientApp/src/features/mcp/`, `tests/AskLucy.*.Tests/Mcp/`. Two existing spec-020 files are modified in place: `src/AskLucy.Application/Agents/Tools/IAgentTool.cs` (enum extension) and `src/AskLucy.Application/Agents/Tools/AgentToolCatalog.cs` (dynamic-source composition).

---

## Phase 1: Setup

**Purpose**: Minimal scaffolding and new dependencies — this extends an existing solution, so there is no project/toolchain initialization.

- [X] T001 [P] Create the empty `Mcp` folders this plan targets: `src/AskLucy.Domain/Mcp/`, `src/AskLucy.Application/Mcp/{Commands,Queries,Authorization,Tools,Validation,Resilience}/`, `src/AskLucy.Infrastructure/Mcp/`, `src/AskLucy.Persistence/Configurations/Mcp/`, `src/AskLucy.Persistence/Repositories/Mcp/`, `src/AskLucy.Web/ClientApp/src/features/mcp/{api,hooks,components,pages}/`
- [X] T002 [P] Add the official `ModelContextProtocol` NuGet package reference to `src/AskLucy.Infrastructure/AskLucy.Infrastructure.csproj` (research.md Decision 2)
- [X] T003 [P] Add the `JsonSchema.Net` NuGet package reference to `src/AskLucy.Application/AskLucy.Application.csproj` (research.md Decision 9 — no I/O dependency, safe to live in Application for testability)
- [X] T004 [P] Add `McpRuntimeOptions` (`IOptions<T>`, `ValidateOnStart`, per constitution §4) with `AllowLocalTransport`, `DefaultCapabilityRefreshIntervalMinutes`, `MaxResponseSizeBytes`, `MaxCallDurationSeconds`, `MaxRetries`, `CircuitBreakerFailureThreshold`, `HealthCheckIntervalMinutes` in `src/AskLucy.Application/Options/McpRuntimeOptions.cs`
- [X] T005 Add an `"Mcp"` configuration section with `McpRuntimeOptions` defaults to `src/AskLucy.Web/appsettings.json` and `appsettings.Development.json`, and bind/validate it in `src/AskLucy.Web/Program.cs` (depends on T004)
- [X] T006 [P] Register a new named `"Mcp"` `IHttpClientFactory` client (no `BaseAddress`, bounded timeout — research.md Decision 11) in `src/AskLucy.Infrastructure/DependencyInjection.cs`, alongside the existing `"OpenAI"`/`"Anthropic"`/`"GoogleGemini"`/`"OpenRouter"` clients

**Checkpoint**: Scaffolding ready.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain model, persistence, and shared runtime contracts every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain entities, value objects, enums (data-model.md)

- [X] T007 [P] `McpServer` aggregate root + `McpServerTransport`/`McpAuthenticationType` enums (`Name`, `Endpoint`, `Transport`, `AuthenticationType`, `RequiresUnauthenticatedConfirmation`, `AllowInsecureTransport`+`InsecureTransportJustification`, `EndpointValidationOverride`+`EndpointValidationJustification`, `IsEnabled`, `OwnerUserId`, `ConfigurationVersion`, `CapabilityRefreshIntervalMinutes`, `LastHealthCheckAtUtc`, `LastCapabilityDiscoveryAtUtc`) in `src/AskLucy.Domain/Mcp/McpServer.cs`
- [X] T008 [P] `McpServerCredential` entity (`McpServerId`, `CiphertextBlob`, `RotatedAtUtc`, `RotatedByUserId`) in `src/AskLucy.Domain/Mcp/McpServerCredential.cs`
- [X] T009 [P] `McpServerHealth` entity + `McpServerHealthStatus`/`McpFailureCategory` enums in `src/AskLucy.Domain/Mcp/McpServerHealth.cs`
- [X] T010 [P] `McpCapabilitySnapshot` entity (`SnapshotVersion`, `DeclaredCapabilitiesJson`, `ChangeSummaryJson`, `WasSuccessful`, `FailureCategory`) in `src/AskLucy.Domain/Mcp/McpCapabilitySnapshot.cs`
- [X] T011 [P] `McpTool` entity + `McpToolActivationStatus` enum (`NamespacedName`, `ToolName`, `DisplayName`, `Description`, `InputSchemaJson`/`OutputSchemaJson`, `ServerDeclaredRiskLevel`, `EffectiveRiskLevel` — defaults `Critical` per FR-022, `RequiredPermissionsJson`, `ActivationStatus`, `ActivatedByUserId`/`ActivatedAtUtc`, `Version`, `IsAvailable`) in `src/AskLucy.Domain/Mcp/McpTool.cs`
- [X] T012 [P] `McpResource` entity (`NamespacedName`, `Name`, `Description`, `ContentType`, `IsAvailable`) in `src/AskLucy.Domain/Mcp/McpResource.cs`
- [X] T013 [P] `McpPrompt` entity (`NamespacedName`, `Name`, `Description`, `ContentTemplate`, `IsAvailable` — no `Update` domain method, only `RefreshFromSnapshot`, per clarification) in `src/AskLucy.Domain/Mcp/McpPrompt.cs`
- [X] T014 [P] `McpAuditLog` entity + `McpAuditAction` enum (no hard FK to `McpServer`, per data-model.md) in `src/AskLucy.Domain/Mcp/McpAuditLog.cs`
- [X] T015 [P] Domain invariant tests for `McpServer` (enable/disable transitions, insecure-transport/endpoint-override require a justification string) in `tests/AskLucy.Domain.Tests/Mcp/McpServerTests.cs`
- [X] T016 [P] Domain invariant tests for `McpTool`'s `ActivationStatus` state machine (`PendingReview`→`Active`→`Deactivated`→`Active`, contracts/mcp-lifecycle-events.md) in `tests/AskLucy.Domain.Tests/Mcp/McpToolTests.cs`

### Persistence (EF Core)

- [X] T017 [P] EF configuration for `McpServer` (unique index on `(Endpoint, Transport)`, clarification) in `src/AskLucy.Persistence/Configurations/Mcp/McpServerConfiguration.cs`
- [X] T018 [P] EF configuration for `McpServerCredential` in `src/AskLucy.Persistence/Configurations/Mcp/McpServerCredentialConfiguration.cs`
- [X] T019 [P] EF configuration for `McpServerHealth` in `src/AskLucy.Persistence/Configurations/Mcp/McpServerHealthConfiguration.cs`
- [X] T020 [P] EF configuration for `McpCapabilitySnapshot` in `src/AskLucy.Persistence/Configurations/Mcp/McpCapabilitySnapshotConfiguration.cs`
- [X] T021 [P] EF configuration for `McpTool` (unique index on `NamespacedName`, FK index on `McpServerId`) in `src/AskLucy.Persistence/Configurations/Mcp/McpToolConfiguration.cs`
- [X] T022 [P] EF configuration for `McpResource` (unique index on `NamespacedName`) in `src/AskLucy.Persistence/Configurations/Mcp/McpResourceConfiguration.cs`
- [X] T023 [P] EF configuration for `McpPrompt` (unique index on `NamespacedName`) in `src/AskLucy.Persistence/Configurations/Mcp/McpPromptConfiguration.cs`
- [X] T024 [P] EF configuration for `McpAuditLog` (no FK constraint on `McpServerId`, per data-model.md) in `src/AskLucy.Persistence/Configurations/Mcp/McpAuditLogConfiguration.cs`
- [X] T025 Register all 8 new `DbSet<T>` properties on `src/AskLucy.Persistence/AskLucyDbContext.cs` (depends on T017–T024)
- [X] T026 Generate the `AddMcpModule` EF Core migration covering every entity from T017–T024 (depends on T025)

### Repositories

- [X] T027 [P] `IMcpServerRepository` (CRUD, `GetByEndpointAndTransportAsync` for the uniqueness check, `CountReferencingAgentToolsAsync` — joins `AgentTool.ToolName == McpTool.NamespacedName`, research.md Decision 15) in `src/AskLucy.Application/Abstractions/IMcpServerRepository.cs`
- [X] T028 [P] `IMcpToolRepository` (list by server, list `PendingReview`, find by `NamespacedName`, list `Active`+`IsAvailable` for the registry) in `src/AskLucy.Application/Abstractions/IMcpToolRepository.cs`
- [X] T029 [P] `IMcpResourceRepository` in `src/AskLucy.Application/Abstractions/IMcpResourceRepository.cs`
- [X] T030 [P] `IMcpPromptRepository` in `src/AskLucy.Application/Abstractions/IMcpPromptRepository.cs`
- [X] T031 [P] `IMcpAuditLogRepository` in `src/AskLucy.Application/Abstractions/IMcpAuditLogRepository.cs`
- [X] T032 [P] `McpServerRepository` implementation in `src/AskLucy.Persistence/Repositories/Mcp/McpServerRepository.cs` (depends on T027)
- [X] T033 [P] `McpToolRepository` implementation in `src/AskLucy.Persistence/Repositories/Mcp/McpToolRepository.cs` (depends on T028)
- [X] T034 [P] `McpResourceRepository` implementation in `src/AskLucy.Persistence/Repositories/Mcp/McpResourceRepository.cs` (depends on T029)
- [X] T035 [P] `McpPromptRepository` implementation in `src/AskLucy.Persistence/Repositories/Mcp/McpPromptRepository.cs` (depends on T030)
- [X] T036 [P] `McpAuditLogRepository` implementation in `src/AskLucy.Persistence/Repositories/Mcp/McpAuditLogRepository.cs` (depends on T031)
- [X] T037 Register all five repositories in `src/AskLucy.Persistence/DependencyInjection.cs` (depends on T032–T036)

### New protocol/security/validation infrastructure (research.md Decisions 2, 7, 8, 9, 11, 12)

- [X] T038 [P] `IMcpClient`/`IMcpClientFactory` interfaces (`ListToolsAsync`, `ListResourcesAsync`, `ListPromptsAsync`, `CallToolAsync`, `ReadResourceAsync`, `GetPromptAsync`, `PingAsync`, `GetOrCreateAsync`) in `src/AskLucy.Application/Abstractions/IMcpClient.cs`, `IMcpClientFactory.cs`
- [X] T039 `McpClient`/`McpClientFactory` implementation wrapping the `ModelContextProtocol` SDK — transport selection (`StreamableHttp`/`Stdio` gated by `McpRuntimeOptions.AllowLocalTransport`), connection reuse per server (contracts/mcp-tool-adapter.md's "avoid reconnecting for every tool invocation") in `src/AskLucy.Infrastructure/Mcp/McpClient.cs`, `McpClientFactory.cs` (depends on T002, T038)
- [X] T040 [P] `IMcpEndpointValidator` interface in `src/AskLucy.Application/Abstractions/IMcpEndpointValidator.cs`
- [X] T041 `McpEndpointValidator` implementation — DNS resolution + rejection of private/loopback/link-local/cloud-metadata ranges, `EndpointValidationOverride` bypass path (contracts/mcp-security-model.md) in `src/AskLucy.Infrastructure/Mcp/McpEndpointValidator.cs` (depends on T040)
- [X] T042 [P] `McpEndpointValidatorTests` (RFC1918 ranges, loopback, link-local, `169.254.169.254` cloud-metadata, override bypass with justification, DNS-rebinding re-check called again at connection time not just registration) in `tests/AskLucy.Infrastructure.Tests/Mcp/McpEndpointValidatorTests.cs` (depends on T041)
- [X] T043 [P] `IMcpCredentialProtector` interface in `src/AskLucy.Application/Abstractions/IMcpCredentialProtector.cs`
- [X] T044 `McpCredentialProtector` implementation (`IDataProtectionProvider.CreateProtector("AskLucy.McpServerCredentials")`, mirrors `AiCredentialProtector` exactly — research.md Decision 7) in `src/AskLucy.Infrastructure/Mcp/McpCredentialProtector.cs` (depends on T043)
- [X] T045 [P] `IJsonSchemaValidator` interface (`Validate(schemaJson, instanceJson, maxSizeBytes) : IReadOnlyList<string>`) in `src/AskLucy.Application/Abstractions/IJsonSchemaValidator.cs`
- [X] T046 `JsonSchemaValidator` implementation wrapping `JsonSchema.Net` (schema conformance + `McpRuntimeOptions.MaxResponseSizeBytes` independent size check, research.md Decision 9) in `src/AskLucy.Application/Mcp/Validation/JsonSchemaValidator.cs` (depends on T003, T045)
- [X] T047 [P] `JsonSchemaValidatorTests` (valid/invalid input, valid/invalid output, oversized-but-schema-valid payload rejection, malformed schema document handling) in `tests/AskLucy.Application.Tests/Mcp/JsonSchemaValidatorTests.cs` (depends on T046)
- [X] T048 [P] `IMcpRateLimiter`/`IMcpConcurrencyLimiter` interfaces (keyed `(serverId, toolName, userId, agentId)`) in `src/AskLucy.Application/Abstractions/IMcpRateLimiter.cs`
- [X] T049 `McpRateLimiter` implementation on `System.Threading.RateLimiting` primitives (`FixedWindowRateLimiter`/`ConcurrencyLimiter`, research.md Decision 12) in `src/AskLucy.Infrastructure/Mcp/McpRateLimiter.cs` (depends on T048)
- [X] T050 [P] `McpRateLimiterTests` (per-key isolation — one server/tool/user/agent's limit doesn't affect another's, rejection returns an actionable failure not a silent drop) in `tests/AskLucy.Infrastructure.Tests/Mcp/McpRateLimiterTests.cs` (depends on T049)
- [X] T051 `McpConnectionResiliencePolicy` (retry-with-backoff only for calls marked idempotent/safe-to-retry, per-server circuit breaker opening after `McpRuntimeOptions.CircuitBreakerFailureThreshold` consecutive failures, research.md Decision 11) in `src/AskLucy.Application/Mcp/Resilience/McpConnectionResiliencePolicy.cs` (depends on T038)
- [X] T051a [P] `McpConnectionResiliencePolicyTests` (an ambiguous-outcome failure — e.g. a dropped connection mid-call — is never retried or assumed successful; circuit breaker opens after `McpRuntimeOptions.CircuitBreakerFailureThreshold` consecutive failures; half-opens on the next health-check tick, FR-054) in `tests/AskLucy.Application.Tests/Mcp/McpConnectionResiliencePolicyTests.cs` (depends on T051)
- [X] T051b Instrument `McpClient`/`McpClientFactory`/`McpRateLimiter`/`McpConnectionResiliencePolicy` with structured Serilog logging (`ILogger<T>`, named properties tagged with `McpServerId`/`ToolName`) capturing connection latency, tool-call latency, request counts, failure counts, timeout counts, and rate-limit events (FR-057, constitution §14) in `src/AskLucy.Infrastructure/Mcp/McpClient.cs`, `McpClientFactory.cs`, `src/AskLucy.Infrastructure/Mcp/McpRateLimiter.cs`, `src/AskLucy.Application/Mcp/Resilience/McpConnectionResiliencePolicy.cs` (depends on T039, T049, T051)
- [X] T051c [P] `McpObservabilityTests` (a successful call, a failed call, a timeout, and a rate-limit rejection each emit a structured log event with the correct named properties) in `tests/AskLucy.Application.Tests/Mcp/McpObservabilityTests.cs` (depends on T051b)

### Tool-catalog composition, permission extension, admin guard

- [X] T052 Extend `AgentToolPermission` enum with `ReadExternalData`, `WriteExternalData`, `SendCommunication`, `ModifyExternalSystem`, `DeleteExternalData`, `ExecuteOperation` (additive, research.md Decision 5) in `src/AskLucy.Application/Agents/Tools/IAgentTool.cs`
- [X] T053 [P] `McpToolAdapter` class shell — `Name => tool.NamespacedName`, `Description`, `RiskLevel => tool.EffectiveRiskLevel` (never `ServerDeclaredRiskLevel` directly), `RequiredPermissions` (deserialized from `RequiredPermissionsJson`), `InputSchemaJson`/`OutputSchemaJson`; `ExecuteAsync` stub (completed in US2, T093) in `src/AskLucy.Application/Mcp/Tools/McpToolAdapter.cs` (depends on T011, T052)
- [X] T054 [P] `IMcpToolRegistry` interface + `McpToolRegistry` implementation — constructs `McpToolAdapter` instances from every `McpTool` row that is `ActivationStatus == Active`, `IsAvailable == true`, whose `McpServer.IsEnabled == true`; synchronous in-memory `ActiveTools`, `Invalidate()` rebuild trigger (research.md Decision 1/4) in `src/AskLucy.Application/Agents/Tools/IMcpToolRegistry.cs`, `src/AskLucy.Application/Mcp/Tools/McpToolRegistry.cs` (depends on T033, T053)
- [X] T055 Modify `AgentToolCatalog` constructor from `(IEnumerable<IAgentTool> tools)` to `(IEnumerable<IAgentTool> nativeTools, IMcpToolRegistry mcpToolRegistry)`, merging both sources into `_toolsByName` — `Find`/`All` unchanged (research.md Decision 1, contracts/mcp-tool-adapter.md) in `src/AskLucy.Application/Agents/Tools/AgentToolCatalog.cs` (depends on T054)
- [X] T056 [P] `AgentToolCatalogCompositionTests` (native+MCP merge, disjoint-name invariant since native names are unqualified and MCP names are always `mcp:...`-namespaced, catalog reflects a subsequent `Invalidate()`) in `tests/AskLucy.Application.Tests/Mcp/AgentToolCatalogCompositionTests.cs` (depends on T055)
- [X] T057 Register `IMcpToolRegistry`/`McpToolRegistry`, `IMcpClient`/`IMcpClientFactory`, `IMcpEndpointValidator`, `IMcpCredentialProtector`, `IJsonSchemaValidator`, `IMcpRateLimiter`, `McpConnectionResiliencePolicy` in the DI composition roots `src/AskLucy.Application/DependencyInjection.cs` and `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T039, T041, T044, T046, T049, T051, T054)
- [X] T058 ~~`McpServerAdministrationGuard`~~ — **resolved by reuse during implementation**: confirmed `AgentPolicy`'s admin gate is not a bespoke Application-layer guard class at all — it is purely the existing `AdministratorOrSuperUser` ASP.NET Core authorization policy (already registered in `Program.cs:131`, `RequireRole("Administrator", "Super User")`), applied declaratively via `[Authorize(Policy = "AdministratorOrSuperUser")]` at the controller level (confirmed against `CreateAgentPolicyCommand`'s own doc comment: "enforced by the controller's `AdministratorOrSuperUser` authorization policy"). Creating a redundant guard class would duplicate an existing, working mechanism (YAGNI, constitution §2.III) — `McpServersController` (US1, T081) applies the same attribute directly, no new Foundational-phase file needed.

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Register and Connect an MCP Server (Priority: P1) 🎯 MVP

**Goal**: An administrator registers an MCP server, tests connectivity, discovers its capabilities, and activates the tools they intend to allow — entirely without any agent involved (spec.md's own Independent Test wording).

**Independent Test**: Register a server with valid connection details, run a connectivity test, confirm the system discovers and lists its tools/resources/prompts with a Healthy status, and activate one tool — all via the admin API, no agent required.

### Tests for User Story 1

- [X] T059 [P] [US1] `RegisterMcpServerCommandHandlerTests` (SSRF rejection via `IMcpEndpointValidator`, duplicate-`(Endpoint,Transport)` rejection, credential encrypted via `IMcpCredentialProtector` before persistence, unauthenticated-remote-server requires explicit confirmation) in `tests/AskLucy.Application.Tests/Mcp/RegisterMcpServerCommandHandlerTests.cs`
- [X] T060 [P] [US1] `TestMcpServerConnectionCommandHandlerTests` (success/failure paths, writes `McpServerHealth`, re-validates the endpoint at connection time not just registration time) in `tests/AskLucy.Application.Tests/Mcp/TestMcpServerConnectionCommandHandlerTests.cs`
- [X] T061 [P] [US1] `RefreshMcpCapabilitiesCommandHandlerTests` (new `McpCapabilitySnapshot` creation, `ChangeSummaryJson` computation, a failed refresh leaves the prior successful snapshot's tools/resources/prompts untouched — FR-016) in `tests/AskLucy.Application.Tests/Mcp/RefreshMcpCapabilitiesCommandHandlerTests.cs`
- [X] T062 [P] [US1] `ActivateMcpToolCommandHandlerTests` (`PendingReview`→`Active`, optional risk/permission override at activation time, a tool missing a server-declared risk level defaults to `Critical` until reviewed — FR-022) in `tests/AskLucy.Application.Tests/Mcp/ActivateMcpToolCommandHandlerTests.cs`
- [X] T063 [P] [US1] `McpServersControllerTests` (`AdministratorOrSuperUser` gate on every action, Problem Details shapes for `mcp-endpoint-not-allowed`/`mcp-server-endpoint-conflict`/`mcp-server-has-references`) in `tests/AskLucy.Web.Tests/Mcp/McpServersControllerTests.cs`
- [ ] T064 [P] [US1] E2E `McpServerRegisterAndDiscover.spec.ts` (quickstart.md Scenario 1) in `tests/AskLucy.E2E.Tests/McpServerRegisterAndDiscover.spec.ts`
- [X] T059a [P] [US1] `UpdateMcpServerCommandHandlerTests` (`ConfigurationVersion` increment, `InsecureTransportJustification`/`EndpointValidationJustification` required when their respective override flags are set, `409` on `RowVersion` mismatch — FR-007/FR-049) in `tests/AskLucy.Application.Tests/Mcp/UpdateMcpServerCommandHandlerTests.cs`
- [X] T059b [P] [US1] `EnableMcpServerCommandHandlerTests` in `tests/AskLucy.Application.Tests/Mcp/EnableMcpServerCommandHandlerTests.cs`
- [X] T059c [P] [US1] `DisableMcpServerCommandHandlerTests` (disabling calls `IMcpToolRegistry.Invalidate()`; every child tool/resource/prompt is immediately absent from `ActiveTools` — FR-004/SC-008) in `tests/AskLucy.Application.Tests/Mcp/DisableMcpServerCommandHandlerTests.cs`

### Implementation for User Story 1

- [X] T065 [P] [US1] `RegisterMcpServerCommand` + Handler + Validator — runs `IMcpEndpointValidator` and the `(Endpoint,Transport)` uniqueness check before insert, encrypts the supplied credential via `IMcpCredentialProtector`, server starts `IsEnabled: false` in `src/AskLucy.Application/Mcp/Commands/RegisterMcpServer/` (depends on T027, T041, T044)
- [X] T066 [P] [US1] `UpdateMcpServerCommand` + Handler + Validator — increments `ConfigurationVersion`, requires `InsecureTransportJustification`/`EndpointValidationJustification` when their respective override flags are set, `409` on `RowVersion` mismatch in `src/AskLucy.Application/Mcp/Commands/UpdateMcpServer/`
- [X] T067 [P] [US1] `EnableMcpServerCommand` + Handler in `src/AskLucy.Application/Mcp/Commands/EnableMcpServer/`
- [X] T068 [P] [US1] `DisableMcpServerCommand` + Handler — calls `IMcpToolRegistry.Invalidate()` so every child tool/resource/prompt is immediately excluded (FR-004) in `src/AskLucy.Application/Mcp/Commands/DisableMcpServer/` (depends on T054)
- [X] T069 [P] [US1] `DeleteMcpServerCommand` + Handler — `422` (`mcp-server-has-references`, listing referencing `agentId`/`toolName` pairs) when `IMcpServerRepository.CountReferencingAgentToolsAsync(serverId) > 0`, otherwise soft delete (research.md Decision 15) in `src/AskLucy.Application/Mcp/Commands/DeleteMcpServer/` (depends on T027)
- [X] T070 [US1] `TestMcpServerConnectionCommand` + Handler — re-runs `IMcpEndpointValidator` then `IMcpClientFactory.GetOrCreateAsync(...).PingAsync(...)`, writes an `McpServerHealth` row + `McpAuditLog(Action=HealthStateChanged)` in `src/AskLucy.Application/Mcp/Commands/TestMcpServerConnection/` (depends on T039, T041, T057)
- [X] T071 [US1] `RefreshMcpCapabilitiesCommand` + Handler — connects, calls `ListToolsAsync`/`ListResourcesAsync`/`ListPromptsAsync`, writes a new `McpCapabilitySnapshot` (`DeclaredCapabilitiesJson` set to whichever the server actually returned, FR-017), writes/carries-forward `McpTool`/`McpResource`/`McpPrompt` rows per contracts/mcp-lifecycle-events.md's carry-forward rule, writes `McpAuditLog(Action=CapabilityDiscoverySucceeded/Failed)` in `src/AskLucy.Application/Mcp/Commands/RefreshMcpCapabilities/` (depends on T070)
- [X] T072 [P] [US1] `ActivateMcpToolCommand` + Handler — `PendingReview`/`Deactivated`→`Active`, accepts an optional `EffectiveRiskLevel`/`RequiredPermissionsJson` override, calls `IMcpToolRegistry.Invalidate()` in `src/AskLucy.Application/Mcp/Commands/ActivateMcpTool/` (depends on T054)
- [X] T073 [P] [US1] `DeactivateMcpToolCommand` + Handler — calls `IMcpToolRegistry.Invalidate()` in `src/AskLucy.Application/Mcp/Commands/DeactivateMcpTool/`
- [X] T074 [US1] In `RefreshMcpCapabilitiesCommandHandler`, default every newly-created `McpTool.EffectiveRiskLevel` to `Critical` when `ServerDeclaredRiskLevel` is absent, and force `ActivationStatus = PendingReview` for every genuinely new/changed tool (FR-021/FR-022, contracts/mcp-lifecycle-events.md's re-review rule) in `src/AskLucy.Application/Mcp/Commands/RefreshMcpCapabilities/RefreshMcpCapabilitiesCommandHandler.cs` (depends on T071)
- [X] T075 [P] [US1] `GetMcpServerQuery` + Handler + `McpServerDetailDto` (never includes credential material, FR-045) in `src/AskLucy.Application/Mcp/Queries/GetMcpServer/`
- [X] T076 [P] [US1] `ListMcpServersQuery` + Handler (cursor-paginated, filters `status`/`transport`/`enabled`) in `src/AskLucy.Application/Mcp/Queries/ListMcpServers/`
- [X] T077 [P] [US1] `GetMcpServerHealthQuery` + Handler in `src/AskLucy.Application/Mcp/Queries/GetMcpServerHealth/`
- [X] T078 [P] [US1] `ListMcpServerReferencesQuery` + Handler (same join as `CountReferencingAgentToolsAsync`, FR-065) in `src/AskLucy.Application/Mcp/Queries/ListMcpServerReferences/`
- [X] T079 [P] [US1] `ListMcpServerToolsQuery` + Handler (admin view — includes `PendingReview`/`Deactivated` tools, unlike the user-facing catalog) in `src/AskLucy.Application/Mcp/Queries/ListMcpServerTools/`
- [X] T080 [P] [US1] `ListMcpAuditLogQuery` + Handler (cursor-paginated, per server, FR-058) in `src/AskLucy.Application/Mcp/Queries/ListMcpAuditLog/`
- [X] T081 [US1] `McpServersController` — register/list/get/update/enable/disable/delete/test-connection/refresh-capabilities/health/references/tools/audit-log/activate/deactivate actions, `[Authorize(Policy = "AdministratorOrSuperUser")]`, `[EnableRateLimiting("mcp-admin-endpoints")]` (contracts/mcp-api.md) in `src/AskLucy.Web/Controllers/v1/McpServersController.cs` (depends on T065–T080)
- [X] T082 [P] [US1] Register the `"mcp-admin-endpoints"` named rate-limit policy (fixed-window, partitioned by user, matching the existing 11-policy convention) in `src/AskLucy.Web/Program.cs` (depends on T006)
- [X] T083 [P] [US1] `mcpServersApi.ts` fetch wrappers in `src/AskLucy.Web/ClientApp/src/features/mcp/api/mcpServersApi.ts`
- [X] T084 [P] [US1] `useMcpServers.ts`, `useMcpServerMutations.ts` hooks in `src/AskLucy.Web/ClientApp/src/features/mcp/hooks/`
- [X] T085 [US1] `McpServerList.tsx`, `McpServerForm.tsx` (register/edit; credential input is write-only, never pre-filled or echoed) in `src/AskLucy.Web/ClientApp/src/features/mcp/components/` (depends on T084)
- [X] T086 [US1] `McpHealthBadge.tsx`, `McpToolActivationPanel.tsx` (admin review/activate/deactivate list, shows risk/permissions before activation) in `src/AskLucy.Web/ClientApp/src/features/mcp/components/` (depends on T084)
- [X] T087 [US1] `McpAuditLogTable.tsx` in `src/AskLucy.Web/ClientApp/src/features/mcp/components/McpAuditLogTable.tsx` (depends on T084)
- [X] T088 [US1] `McpAdministrationPage.tsx` (server list/form/health/tool-activation/audit-log, TanStack Query polling per research.md Decision 13 — no live push) in `src/AskLucy.Web/ClientApp/src/features/mcp/pages/McpAdministrationPage.tsx` (depends on T085–T087) — new route + admin-nav entry

**Checkpoint**: User Story 1 fully functional and independently testable.

---

## Phase 4: User Story 2 - Agent Executes an MCP Tool (Priority: P2)

**Goal**: An agent configured with an enabled, activated MCP tool calls it as part of a plan, through the exact same runtime path as a native tool.

**Independent Test**: Enable one Low-risk, Active MCP tool for an agent; give it an objective requiring that tool; confirm the execution's history shows the MCP tool call alongside any native tool calls, indistinguishable in shape from spec 020's own execution history.

### Tests for User Story 2

- [X] T089 [P] [US2] `McpToolAdapterTests` (input-schema validation before the call leaves the process, output-schema defense-in-depth re-check, rate-limit enforcement, `McpRuntimeOptions.MaxCallDurationSeconds` timeout) in `tests/AskLucy.Application.Tests/Mcp/McpToolAdapterTests.cs`
- [X] T090 [P] [US2] `McpToolExecutionOrchestratorIntegrationTests` (an agent execution calling an `Active` MCP tool end-to-end through the unmodified `AgentExecutionOrchestrator`, resulting `AgentToolCall` row shaped identically to a native tool's, FR-031) in `tests/AskLucy.Application.Tests/Mcp/McpToolExecutionOrchestratorIntegrationTests.cs`
- [X] T091 [P] [US2] `McpFailureCategorizationTests` (a failed MCP call resolves to `AgentExecutionErrorCategory.ToolFailure` at the execution-history level while the granular `McpFailureCategory` is embedded in `AgentToolResult.FailureReason`/`AgentToolCall.FailureReason` — corrected from the original "lands on `McpAuditLog`" wording, which data-model.md's `McpAuditLog` section explicitly rules out ("does not duplicate" `AgentToolCall`'s per-execution activity); research.md Decision 17) in `tests/AskLucy.Application.Tests/Mcp/McpFailureCategorizationTests.cs`
- [ ] T092 [P] [US2] E2E `McpToolActivationAndAgentUse.spec.ts` (quickstart.md Scenario 2) in `tests/AskLucy.E2E.Tests/McpToolActivationAndAgentUse.spec.ts`

### Implementation for User Story 2

- [X] T093 [US2] Complete `McpToolAdapter.ExecuteAsync`: `IMcpRateLimiter.AcquireAsync((serverId, toolName, userId, agentId))` → `IMcpClientFactory.GetOrCreateAsync` → `McpConnectionResiliencePolicy`-wrapped `IMcpClient.CallToolAsync` (bounded by `McpRuntimeOptions.MaxCallDurationSeconds`) → `IJsonSchemaValidator` output re-check → `AgentToolResult.Success`/`.Failure` (never throws for an ordinary MCP-side failure, FR-032) in `src/AskLucy.Application/Mcp/Tools/McpToolAdapter.cs` (depends on T053, T039, T046, T049, T051)
- [X] T094 [US2] Wire the two-level failure mapping: every MCP-side failure resolves to `AgentExecutionErrorCategory.ToolFailure` at the `AgentExecutionStep`/`AgentToolCall` level (automatic — the orchestrator already maps any `!AgentToolResult.Succeeded` to `ToolFailure`, zero code change) while the specific `McpFailureCategory` is embedded as a `[CategoryName]` prefix in `AgentToolResult.FailureReason`, never written to `McpAuditLog` (research.md Decision 17, data-model.md's non-duplication note) in `src/AskLucy.Application/Mcp/Tools/McpToolAdapter.cs` (depends on T093)
- [X] T095 [US2] Verify `AgentPolicyEvaluator`/`AgentApproval` creation resolve correctly against a `mcp:{serverId}:{toolName}` `NamespacedName` with zero orchestrator code change (research.md Decisions 3/6); add a regression test proving it in `tests/AskLucy.Application.Tests/Mcp/McpNamespacedToolPolicyMatchingTests.cs` (depends on T093, T055)
- [X] T096 [US2] Verify `UpdateAgentCommand`'s existing tool-name validation (spec 020) accepts any `AgentToolCatalog.Find`-resolvable name, including MCP's namespaced strings, with zero schema change; add a regression test in `tests/AskLucy.Application.Tests/Mcp/McpAgentToolConfigurationTests.cs` (depends on T055)
- [X] T097 [P] [US2] Register the `"mcp-endpoints"` named rate-limit policy for the user-facing catalog/enable-disable surface in `src/AskLucy.Web/Program.cs` (depends on T006)
- [X] T098 [US2] **Superseded by T111** — see T111 for the completed component and the second dependency correction discovered while building it.

**Checkpoint**: User Stories 1–2 both independently functional.

---

## Phase 5: User Story 3 - Approval for High-Risk MCP Actions (Priority: P3)

**Goal**: A High/Critical-risk MCP tool call pauses execution for explicit user approval (or auto-proceeds under an administrator policy), through the exact same approval mechanism spec 020 already built.

**Independent Test**: Enable a High- or Critical-risk MCP tool for an agent; trigger a plan that calls it; confirm execution pauses `WaitingForApproval`, shows the target server and parameters, and proceeds/ends per the user's (or a matching `AgentPolicy`'s) decision — recorded in the audit trail.

### Tests for User Story 3

- [X] T099 [P] [US3] `McpHighRiskApprovalTests` (pause/approve/reject/policy-auto-approve against an MCP tool call; a prompt-injection payload embedded in a prior tool's output never bypasses or weakens the approval requirement, FR-030/FR-035) in `tests/AskLucy.Application.Tests/Mcp/McpHighRiskApprovalTests.cs`
- [ ] T100 [P] [US3] E2E `McpHighRiskApproval.spec.ts` (quickstart.md Scenario 3, including the policy-auto-approve path and the prompt-injection check) in `tests/AskLucy.E2E.Tests/McpHighRiskApproval.spec.ts`

### Implementation for User Story 3

- [X] T099a [US3] **Design correction discovered at implementation time**: the task as originally written asked `AgentExecutionOrchestrator` itself to resolve `McpTool.McpServerId` → `McpServer.Name`, which would give the orchestrator a direct MCP-specific dependency — contradicting the feature's own non-negotiable constraint ("Do not allow the Agent Runtime to depend directly on a concrete MCP client. Use dependency inversion.") and every other US1–US3 task's "zero orchestrator code change" framing. Instead, `IMcpToolRepository.ListActiveAvailableAsync` now returns each tool's server name alongside it (was `IReadOnlyList<McpTool>`, now `IReadOnlyList<(McpTool Tool, string ServerName)>`), `McpToolRegistry.InvalidateAsync` passes it into a new `McpToolAdapter(McpTool tool, string serverName, ...)` constructor parameter, and `McpToolAdapter.Description` embeds it (`"{tool.Description} (MCP server: {serverName})"`). Since the orchestrator already uses `tool.Name`/an approval flow built from the `IAgentTool` interface for every tool, this satisfies FR-029 with the orchestrator unchanged. In `src/AskLucy.Application/Abstractions/IMcpToolRepository.cs`, `src/AskLucy.Persistence/Repositories/McpToolRepository.cs`, `src/AskLucy.Application/Mcp/Tools/McpToolRegistry.cs`, `src/AskLucy.Application/Mcp/Tools/McpToolAdapter.cs` (depends on T093)
- [X] T099b [P] [US3] `McpApprovalServerDisplayTests` (an `AgentApproval` created for an MCP tool call includes the target server's name in its intended-action description) in `tests/AskLucy.Application.Tests/Mcp/McpApprovalServerDisplayTests.cs` (depends on T099a)
- [X] T101 [US3] Add a High-risk test/dev-only `McpTool` seed fixture (mirroring spec 020's `FakeHighRiskTool` pattern) so the approval flow is exercisable without a real destructive external action, registered only in Testing/Development, in `tests/AskLucy.Application.Tests/Mcp/Fixtures/McpHighRiskToolFixture.cs` (depends on T093)
- [X] T102 [US3] **Done via reuse**: verify the existing `AgentPolicyEvaluator` (spec 020, unmodified) matches an MCP tool call's `ValidatedInputJson` against an `AgentPolicy.ConditionsJson` targeting a `mcp:...` `ToolName` correctly. This is the identical scenario T095's `McpNamespacedToolPolicyMatchingTests` (`FindMatchAsync_ShouldRespectConditions_AgainstANamespacedMcpToolName`) already covers — an unconditional-match case and a conditions-based match/mismatch case, both against a namespaced tool name — so no separate `McpPolicyEvaluatorRegressionTests.cs` file was added to avoid a literal duplicate of T095's coverage (depends on T093)
- [X] T103 [US3] Verify `McpToolAdapter`'s output reuses the existing `RetrievalPromptFraming`-style untrusted-content framing before re-entering any subsequent `IAIProvider.ChatAsync` call — no MCP-specific framing path (contracts/mcp-tool-adapter.md) — add a regression test in `tests/AskLucy.Application.Tests/Mcp/McpUntrustedContentFramingTests.cs` (depends on T093)

**Checkpoint**: User Stories 1–3 all independently functional.

---

## Phase 6: User Story 4 - Discover and Configure MCP Tools for an Agent (Priority: P4)

**Goal**: A user browses the MCP tools available to them (only `Active`+`Available`+enabled+healthy ones — exactly what an agent could actually call) and enables/disables specific ones for their own agent.

**Independent Test**: Open an agent's tool configuration; confirm every `Active` MCP tool from an enabled server is listed with description/risk/permissions; enable one; confirm it appears among the agent's tools without affecting any other user's agents.

### Tests for User Story 4

- [X] T104 [P] [US4] `ListAvailableMcpToolsQueryHandlerTests` (returns exactly the set `IMcpToolRegistry.ActiveTools` would resolve — same filter, no drift between "what's shown" and "what's callable") in `tests/AskLucy.Application.Tests/Mcp/ListAvailableMcpToolsQueryHandlerTests.cs`
- [X] T105 [P] [US4] `McpCatalogControllerTests` (any authenticated user, no admin gate, correct rate-limit policy applied) in `tests/AskLucy.Web.Tests/Mcp/McpCatalogControllerTests.cs`
- [ ] T106 [P] [US4] E2E `McpAgentToolEnableDisable.spec.ts` in `tests/AskLucy.E2E.Tests/McpAgentToolEnableDisable.spec.ts`

### Implementation for User Story 4

- [X] T107 [P] [US4] `ListAvailableMcpToolsQuery` + Handler (FR-062 — filters identically to `IMcpToolRegistry`) in `src/AskLucy.Application/Mcp/Queries/ListAvailableMcpTools/` (depends on T054)
- [X] T108 [P] [US4] `GetMcpToolQuery` + Handler + `McpToolDetailDto` (full input/output schema, capabilities, version, last-updated, FR-020) in `src/AskLucy.Application/Mcp/Queries/GetMcpTool/`
- [X] T109 [US4] `McpCatalogController` — `GET /mcp/catalog/tools`, `GET /mcp/catalog/tools/{namespacedName}`, `[EnableRateLimiting("mcp-endpoints")]` (contracts/mcp-api.md) in `src/AskLucy.Web/Controllers/v1/McpCatalogController.cs` (depends on T107, T108, T097)
- [X] T110 [P] [US4] `mcpCatalogApi.ts`, `useMcpCatalog.ts` in `src/AskLucy.Web/ClientApp/src/features/mcp/api/mcpCatalogApi.ts`, `src/AskLucy.Web/ClientApp/src/features/mcp/hooks/useMcpCatalog.ts`
- [X] T111 [US4] `McpToolPicker.tsx` — search/filter list of catalog tools plus a tool-detail dialog (description, risk, required permissions, input schema) shown before enabling (FR-062) in `src/AskLucy.Web/ClientApp/src/features/mcp/components/McpToolPicker.tsx`. **Second dependency correction discovered at implementation time**: T098's own wording ("integrated into spec 020's existing Agent Builder tool selector") assumes `AgentBuilder.tsx` already has a native-tool-selection UI to extend — it does not (only a read-only `toolNames` display; `SaveAgentInput`/`UpdateAgentCommand`'s `tools` field is never populated from any frontend form today). Building that selector is a spec 020 gap, out of this feature's scope. `McpToolPicker` is instead a standalone, fully-functional, controlled component (`selectedToolNames: string[]` / `onChange: (names: string[]) => void` props) — complete and correct on its own terms, ready to be wired in whenever Agent Builder gains a real tool selector, rather than force-integrating into a selector that doesn't exist (depends on T110)

**Checkpoint**: User Stories 1–4 all independently functional.

---

## Phase 7: User Story 5 - Agent Uses MCP Resources and Prompts (Priority: P5)

**Goal**: An agent retrieves MCP resource content on demand (through the same runtime path as a tool call) and uses MCP-sourced, read-only prompts; a user can duplicate a prompt into an independent, editable native copy.

**Independent Test**: Enable one MCP resource and one MCP prompt for an agent; run an objective using both; confirm both appear in execution history with correct source-server attribution, no automatic Knowledge Base ingestion occurred, and duplicating the prompt produces an independently editable native prompt.

### Tests for User Story 5

- [X] T112 [P] [US5] `McpResourceReadToolTests` (authorized fetch, size/time limits, execution-history recording, no automatic RAG ingestion — FR-037/FR-038/FR-039) in `tests/AskLucy.Application.Tests/Mcp/McpResourceReadToolTests.cs`
- [X] T113 [P] [US5] `DuplicateMcpPromptCommandHandlerTests` (creates an independent, user-owned native `Prompt`; the source `McpPrompt` is unaffected and has no direct-edit path) in `tests/AskLucy.Application.Tests/Mcp/DuplicateMcpPromptCommandHandlerTests.cs`
- [X] T114 [P] [US5] `McpPromptRefreshTests` (an `McpPrompt`'s `ContentTemplate` re-syncs on a successful capability refresh; a disabled/removed source server shows the prompt as unavailable, FR-044) in `tests/AskLucy.Application.Tests/Mcp/McpPromptRefreshTests.cs`
- [ ] T115 [P] [US5] E2E `McpResourcesAndPrompts.spec.ts` (quickstart.md Scenario 4) in `tests/AskLucy.E2E.Tests/McpResourcesAndPrompts.spec.ts`
- [X] T114a [P] [US5] `McpPromptDuplicateExecutionFramingTests` (a native `Prompt` created via `DuplicateMcpPromptCommand` is executed by `PromptExecutionTool`/`ExecutePromptCommand` with the exact same framing as any user-authored prompt — no special-cased "trusted because MCP-sourced" path, FR-043) in `tests/AskLucy.Application.Tests/Mcp/McpPromptDuplicateExecutionFramingTests.cs` (depends on T120)

### Implementation for User Story 5

- [X] T116 [US5] `McpResourceReadTool : IAgentTool` — single built-in adapter (not one class per resource) taking a `resourceUri` input, dispatching to `IMcpClient.ReadResourceAsync`, fixed `Low` risk, `[ReadExternalData]` permission (contracts/mcp-tool-adapter.md) in `src/AskLucy.Application/Mcp/Tools/McpResourceReadTool.cs` (depends on T038, T046, T049, T057)
- [X] T117 [US5] Register `McpResourceReadTool` with the native `IEnumerable<IAgentTool>` DI collection (a singular built-in tool, unlike the per-instance `McpToolAdapter`) in `src/AskLucy.Application/DependencyInjection.cs` (depends on T116)
- [X] T118 [P] [US5] `ListAvailableMcpResourcesQuery` + Handler (FR-036) in `src/AskLucy.Application/Mcp/Queries/ListAvailableMcpResources/`
- [X] T119 [P] [US5] `ListAvailableMcpPromptsQuery` + Handler (FR-042) in `src/AskLucy.Application/Mcp/Queries/ListAvailableMcpPrompts/`
- [X] T120 [US5] `DuplicateMcpPromptCommand` + Handler — creates an independent, user-owned native `Prompt` seeded from `McpPrompt.ContentTemplate`, mirroring spec 019's existing `DuplicatePromptCommand` (research.md Decision 16) in `src/AskLucy.Application/Mcp/Commands/DuplicateMcpPrompt/`
- [X] T121 [US5] **Done via reuse**: `RefreshMcpCapabilitiesCommandHandler` (T071, built in US1) already re-syncs every `McpPrompt.ContentTemplate` in place on each successful discovery (`existingPrompt.RefreshFromSnapshot(...)`) — no US5-time code change was needed; T114's `McpPromptRefreshTests` (this phase) is the first test to actually verify it in `src/AskLucy.Application/Mcp/Commands/RefreshMcpCapabilities/RefreshMcpCapabilitiesCommandHandler.cs` (depends on T071)
- [X] T122 [US5] Extend `McpCatalogController` with `GET /mcp/catalog/resources`, `GET /mcp/catalog/prompts`, `POST /mcp/catalog/prompts/{namespacedName}/actions/duplicate` (depends on T118–T120, T109)
- [X] T123 [P] [US5] Add resources/prompts sections to `mcpCatalogApi.ts`/`useMcpCatalog.ts` (depends on T110)
- [X] T124 [US5] **Third dependency correction discovered at implementation time** (same shape as T098/T111's Agent Builder gap): this task assumed an "existing spec-019 prompt-picker component" to extend — spec 019's Prompt Library (`src/AskLucy.Web/ClientApp/src/features/prompts/`) has no such reusable picker widget, only a full browse/edit workspace (`PromptLibraryPage.tsx`) and folder/version/testing components; nothing to "extend, not duplicate." Built `McpResourcesAndPromptsPanel.tsx` instead — a standalone panel listing MCP resources (read-only) and MCP prompts with a "Duplicate" action, wired into a new `McpCatalogPage.tsx` (`/mcp/catalog` route, any authenticated user) alongside `McpToolPicker` (T111). Duplicating invalidates `PROMPTS_QUERY_KEY`, so the new native prompt already appears in the existing Prompt Library exactly like any hand-authored one — satisfying the Independent Test's "duplicating the prompt produces an independently editable native prompt" without inventing scope spec 019 never built. In `src/AskLucy.Web/ClientApp/src/features/mcp/components/McpResourcesAndPromptsPanel.tsx`, `src/AskLucy.Web/ClientApp/src/features/mcp/pages/McpCatalogPage.tsx` (depends on T119, T123)

**Checkpoint**: User Stories 1–5 all independently functional.

---

## Phase 8: User Story 6 - Monitor Server Health and Refresh Capabilities (Priority: P6)

**Goal**: Server health degrades/recovers automatically and blocks new tool calls while unhealthy; capabilities refresh on a schedule and surface what changed.

**Independent Test**: Simulate a server becoming unreachable; confirm health status changes and new tool calls against it are blocked; restore it; confirm health recovers and calls resume — all independent of any specific agent execution.

### Tests for User Story 6

- [X] T125 [P] [US6] `McpServerHealthCheckJobTests` (`Healthy`/`Degraded`/`Unavailable`/`AuthenticationFailed`/`ConfigurationError` transitions, circuit-breaker `ConsecutiveFailureCount` interplay) in `tests/AskLucy.Infrastructure.Tests/Mcp/McpServerHealthCheckJobTests.cs`
- [X] T126 [P] [US6] `McpCapabilityRefreshJobTests` (only servers past their `CapabilityRefreshIntervalMinutes` are refreshed, a failed refresh preserves the prior working capability set — FR-016) in `tests/AskLucy.Infrastructure.Tests/Mcp/McpCapabilityRefreshJobTests.cs`
- [ ] T127 [P] [US6] E2E `McpServerHealthMonitoring.spec.ts` (quickstart.md Scenario 5, steps 3–5) in `tests/AskLucy.E2E.Tests/McpServerHealthMonitoring.spec.ts`
- [X] T126a [P] [US6] `McpToolRegistryHealthExclusionTests` (`ActiveTools` excludes a tool the moment its server's health leaves `Healthy`, re-includes it on recovery — FR-056) in `tests/AskLucy.Application.Tests/Mcp/McpToolRegistryHealthExclusionTests.cs` (depends on T131)

### Implementation for User Story 6

- [X] T128 [US6] `McpServerHealthCheckJob` — Hangfire recurring job calling the same Application service `TestMcpServerConnectionCommand`'s handler calls for every enabled server, no duplicate code path (research.md Decision 10) in `src/AskLucy.Infrastructure/Mcp/McpServerHealthCheckJob.cs` (depends on T070)
- [X] T129 [US6] `McpCapabilityRefreshJob` — Hangfire recurring job calling `RefreshMcpCapabilitiesCommand`'s handler for every server whose `LastCapabilityDiscoveryAtUtc` is older than its own `CapabilityRefreshIntervalMinutes` in `src/AskLucy.Infrastructure/Mcp/McpCapabilityRefreshJob.cs` (depends on T071)
- [X] T130 [P] [US6] Register both recurring jobs (`RecurringJob.AddOrUpdate<T>`, 5-minute default cadence) in `src/AskLucy.Web/Program.cs`, and `services.AddScoped<T>()` each concrete job class in `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T128, T129)
- [X] T131 [US6] **Partially done via reuse**: the `Unavailable`/`AuthenticationFailed` filter itself was already built in T054/US1 (`IMcpToolRepository.ListActiveAvailableAsync`'s join against `McpServerHealth.Status`) — no query-logic change was needed here. What US6 actually adds is the *immediacy* half of FR-056: `McpServerHealthCheckJob` (T128) now calls `mcpToolRegistry.InvalidateAsync()` once after every health-check sweep, so a health transition is reflected the moment it's detected rather than waiting for an unrelated activate/deactivate/capability-refresh to trigger the next rebuild — in `src/AskLucy.Infrastructure/Mcp/McpServerHealthCheckJob.cs` (depends on T054, T128)

**Checkpoint**: User Stories 1–6 all independently functional.

---

## Phase 9: User Story 7 - Rotate MCP Server Credentials (Priority: P7)

**Goal**: An administrator rotates a server's credentials in place, with no plaintext value ever exposed and no unnecessary interruption to in-flight calls.

**Independent Test**: Rotate a server's credential; confirm subsequent tool calls authenticate with the new value while the old one no longer works, with no credential value visible in any response, log, or audit record.

### Tests for User Story 7

- [X] T132 [P] [US7] `RotateMcpServerCredentialCommandHandlerTests` (in-place `CiphertextBlob` replacement — never delete+re-insert, no credential value in the response DTO, `RotatedAtUtc`/`RotatedByUserId` stamped, `McpAuditLog(Action=CredentialRotated)` written with no credential material) in `tests/AskLucy.Application.Tests/Mcp/RotateMcpServerCredentialCommandHandlerTests.cs`
- [ ] T133 [P] [US7] E2E `McpCredentialRotation.spec.ts` (quickstart.md Scenario 5, steps 1–2 + 6) in `tests/AskLucy.E2E.Tests/McpCredentialRotation.spec.ts`
- [X] T132a [US7] `McpCredentialRotationInFlightCallTests` (an in-flight, already-approved MCP tool call executing at the moment its server's credential is rotated completes or fails with a recorded, user-visible outcome — never silently disappears, US7 Acceptance Scenario 3/SC-009) in `tests/AskLucy.Application.Tests/Mcp/McpCredentialRotationInFlightCallTests.cs` (depends on T134, T093)

### Implementation for User Story 7

- [X] T134 [US7] `RotateMcpServerCredentialCommand` + Handler — replaces `McpServerCredential.CiphertextBlob` in place via `IMcpCredentialProtector`, stamps `RotatedAtUtc`/`RotatedByUserId`, writes `McpAuditLog(Action=CredentialRotated)` (FR-047) in `src/AskLucy.Application/Mcp/Commands/RotateMcpServerCredential/` (depends on T044). **Real bug found and fixed while implementing this**: `McpClientFactory`'s connection cache (T041) is keyed only on `McpServer.ConfigurationVersion`, which credential rotation never changes — without a fix, a rotated credential would have had zero effect on already-cached connections, silently contradicting FR-047 ("the old one no longer works"). Added `IMcpClientFactory.InvalidateConnectionAsync(serverId)` (discards/disposes the cached connection so the next call reconnects with the new credential; an already in-flight call on the old connection is unaffected) and call it at the end of this handler, in `src/AskLucy.Application/Abstractions/IMcpClient.cs`, `src/AskLucy.Infrastructure/Mcp/McpClientFactory.cs`
- [X] T135 [US7] Extend `McpServersController` with `POST /mcp/servers/{id}/actions/rotate-credential` (depends on T134, T081)
- [X] T136 [P] [US7] Add a "rotate credential" action to `McpServerForm.tsx`/`McpServerList.tsx` (write-only new-value input, never displays the existing value) in `src/AskLucy.Web/ClientApp/src/features/mcp/components/` (depends on T085)

**Checkpoint**: All 7 user stories independently functional.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Requirements that span every story (security review, indexes, accessibility, docs) plus final validation.

- [X] T137 [P] Security review pass — completed via T138–T140 plus existing coverage: SSRF at registration (`RegisterMcpServerCommandHandlerTests`) and update-time (`UpdateMcpServerCommandHandlerTests`, new this pass) confirmed; connection-time re-check newly proven directly (`McpClientFactoryTests`, no SDK mocking needed since the rejection throws before the SDK call); credential-exposure — grepped every `McpServerDto`/audit-record path, confirmed no field ever carries plaintext or ciphertext (`RotateMcpServerCredentialCommandHandlerTests`, `RegisterMcpServerCommandHandlerTests`); prompt-injection framing reuse confirmed (`McpUntrustedContentFramingTests`, `McpHighRiskApprovalTests`); `AdministratorOrSuperUser` gate confirmed present on all 16 `McpServersController` routes (`McpServersControllerTests`) vs. no gate on the 6 user-facing `McpCatalogController` routes (`McpCatalogControllerTests`) — per spec.md's Security Tests section
- [X] T138 [P] SSRF security tests: full range matrix (RFC1918, loopback, link-local, `169.254.169.254` cloud-metadata, DNS-rebinding — a hostname safe at registration but repointed before a later connection attempt) in `tests/AskLucy.Application.Tests/Mcp/McpSsrfSecurityTests.cs`
- [X] T139 [P] Malicious/oversized tool-output security tests (schema-violating payloads, oversized-but-schema-valid responses, malformed JSON, a tool result containing prompt-injection text) in `tests/AskLucy.Application.Tests/Mcp/McpMaliciousOutputSecurityTests.cs`
- [X] T140 [P] Authorization-bypass security tests (a non-administrator attempting server management, a user accessing another user's MCP-related audit/history, a deactivated/`PendingReview` tool rejected at execution time) in `tests/AskLucy.Application.Tests/Mcp/McpAuthorizationBypassSecurityTests.cs`
- [X] T141 [P] Performance tests: concurrent requests against one server respecting `McpRuntimeOptions`/`IMcpConcurrencyLimiter` limits, capability discovery across many servers, an oversized tool response rejected cleanly without blocking other calls in `tests/AskLucy.Infrastructure.Tests/Mcp/McpPerformanceTests.cs`
- [X] T142 [P] **Reviewed, no changes needed**: `(Endpoint, Transport)` unique on `McpServers` ✓; `NamespacedName` unique on `McpTools`/`McpResources`/`McpPrompts` ✓; every `McpServerId` FK indexed (directly, or as the leading column of a composite/unique index — `McpCapabilitySnapshots`' `(McpServerId, SnapshotVersion)` unique, `McpTools`' `(McpServerId, ActivationStatus, IsAvailable)`) ✓; `McpAuditLogs.UserId`/`.Action` indexed ✓. `McpCapabilitySnapshotId` FK on `McpTools`/`McpResources`/`McpPrompts` is deliberately *not* separately indexed — grepped the entire codebase for `McpCapabilitySnapshotId ==` and found zero query filters on it anywhere (write-only lineage data today); adding an index nothing queries would violate constitution §2.III (YAGNI), not satisfy §5
- [X] T143 [P] Accessibility pass (automated axe checks + manual review) on `McpAdministrationPage.tsx`, `McpServerForm.tsx`, `McpToolPicker.tsx` (constitution §7/§10)
- [X] T144 [P] Update `docs/ARCHITECTURE.md` §16 ("MCP Tool Engine") to match the shipped design, explicitly marking the prior aspirational sketch superseded (research.md's opening note)
- [X] T145 [P] Update `docs/ENTITY_MODEL.md` §11 ("MCP Aggregate") to replace the placeholder `McpServer`/`McpTool`/`McpExecution` sketch with the shipped 9-entity model (data-model.md) — the placeholder `McpExecution` is explicitly not built (research.md Decision 17)
- [X] T146 [P] Update `docs/DATABASE.md` §12 ("MCP Context") to match the shipped schema
- [X] T147 [P] Update `docs/DOMAIN_SERVICES.md` §23 ("MCP Service") to describe the actual `IMcpClient`/`IMcpClientFactory`/`IMcpToolRegistry` shape rather than a monolithic `IMcpService`
- [X] T148 [P] Update `docs/API_GUIDELINES.md` §27 ("MCP Endpoints") to match contracts/mcp-api.md's actual endpoint list
- [ ] T149 **Not runnable in this sandbox**: quickstart.md's 5 scenarios require a fully deployed local build — a live SQL Server instance (`PERSISTENCE_TESTS_CONNECTION_STRING` unset here, same limitation `AskLucy.Persistence.Tests` hits), a running `AskLucy.Web` host, and a real or reference MCP server to register against. Every scenario is exercised at the unit/integration-test level instead (1129 backend + 263 frontend tests passing) plus all 7 deferred E2E specs (T064/T092/T100/T106/T115/T127/T133) share this same blocker. Left for the user to run against a real deployment (depends on all prior tasks)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Stories (Phase 3–9)**: All depend on Foundational phase completion.
  - US1 (P1) has no dependency on any other story and delivers the MVP.
  - US2 (P2) depends on US1 having shipped a way to reach `McpTool.ActivationStatus == Active` (T072) — functionally needs at least one server registered/activated to be end-to-end testable, though its own code changes touch different files than US1.
  - US3 (P3) depends on US2's completed `McpToolAdapter.ExecuteAsync` (T093) to have a real tool call to gate with an approval.
  - US4 (P4) depends on US1's registry/activation existing but is otherwise independent of US2/US3's execution-path work — it only reads `IMcpToolRegistry`/`McpTool` state.
  - US5 (P5) depends on the same Foundational plumbing as US2 (`McpResourceReadTool` reuses `IMcpClient`/`IMcpRateLimiter`/`IJsonSchemaValidator` exactly as `McpToolAdapter` does) but is independently testable once US1 has an enabled server with discovered resources/prompts.
  - US6 (P6) depends on US1's `TestMcpServerConnectionCommand`/`RefreshMcpCapabilitiesCommand` (T070/T071) — the recurring jobs call the same handlers.
  - US7 (P7) depends only on Foundational (`IMcpCredentialProtector`, T044) and US1's `McpServersController` (T081) to attach its new endpoint to.
- **Polish (Phase 10)**: Depends on all desired user stories being complete.

### Within Each User Story

- Tests are written before implementation and must fail first.
- Domain/Application plumbing before controllers/UI.
- Story complete (checkpoint) before moving to the next priority, if working sequentially.

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel.
- Within Foundational, all Domain entity tasks (T007–T014), all EF configuration tasks (T017–T024), and all repository-interface tasks (T027–T031) are mutually parallel (different files); each repository *implementation* (T032–T036) can start as soon as its own interface task completes, in parallel with the others.
- Once Foundational completes, US1 and US4 can be staffed in parallel (US4 only reads state US1 writes); US2/US3/US5/US6/US7 are best sequenced after US1 delivers at least one activated tool, per the dependency notes above, even though their own file sets don't literally overlap with US1's.
- All test tasks within a story marked [P] can run in parallel with each other.

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "RegisterMcpServerCommandHandlerTests in tests/AskLucy.Application.Tests/Mcp/RegisterMcpServerCommandHandlerTests.cs"
Task: "TestMcpServerConnectionCommandHandlerTests in tests/AskLucy.Application.Tests/Mcp/TestMcpServerConnectionCommandHandlerTests.cs"
Task: "RefreshMcpCapabilitiesCommandHandlerTests in tests/AskLucy.Application.Tests/Mcp/RefreshMcpCapabilitiesCommandHandlerTests.cs"
Task: "ActivateMcpToolCommandHandlerTests in tests/AskLucy.Application.Tests/Mcp/ActivateMcpToolCommandHandlerTests.cs"
Task: "McpServersControllerTests in tests/AskLucy.Web.Tests/Mcp/McpServersControllerTests.cs"
Task: "E2E McpServerRegisterAndDiscover.spec.ts in tests/AskLucy.E2E.Tests/McpServerRegisterAndDiscover.spec.ts"

# Launch the independent command/query pairs for User Story 1 together:
Task: "RegisterMcpServerCommand + Handler + Validator in src/AskLucy.Application/Mcp/Commands/RegisterMcpServer/"
Task: "UpdateMcpServerCommand + Handler + Validator in src/AskLucy.Application/Mcp/Commands/UpdateMcpServer/"
Task: "EnableMcpServerCommand + Handler in src/AskLucy.Application/Mcp/Commands/EnableMcpServer/"
Task: "GetMcpServerQuery + Handler + McpServerDetailDto in src/AskLucy.Application/Mcp/Queries/GetMcpServer/"
Task: "ListMcpServersQuery + Handler in src/AskLucy.Application/Mcp/Queries/ListMcpServers/"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: run quickstart.md Scenario 1 against a real (or reference/sandbox) MCP server.
5. Deploy/demo if ready — administrators can register, connect, discover, and activate MCP servers/tools even before any agent can use one.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → test independently → deploy/demo (MVP — admin-only value).
3. US2 → test independently → deploy/demo (agents can now actually call an MCP tool).
4. US3 → test independently → deploy/demo (high-risk MCP actions are safe to enable).
5. US4 → test independently → deploy/demo (users get a real browsing/configuration experience, not just admin-side).
6. US5 → test independently → deploy/demo (resources and prompts extend capability breadth).
7. US6 → test independently → deploy/demo (ongoing operational trust).
8. US7 → test independently → deploy/demo (credential hygiene, production-readiness).
9. Polish → security/performance/accessibility/docs hardening, final quickstart validation.

### Parallel Team Strategy

With multiple developers, after Foundational is done:

- Developer A: US1 (registry/discovery/activation — the critical path everything else waits on).
- Developer B: starts US4's read-only queries/controller against Foundational's `IMcpToolRegistry` stub in parallel with US1 (low risk of rework — the filter shape is fixed by data-model.md, not by US1's command implementations).
- Developer C: builds out the security-review test suites (T138–T140) incrementally as each story's surface area lands, rather than only at the end.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- This feature makes zero schema changes to spec 020's existing tables — every cross-feature touch point (T052 enum extension, T055 catalog composition, T095/T096/T102 regression verifications) is additive.
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently.
- Avoid: vague tasks, same-file conflicts, cross-story dependencies that break independence beyond the sequencing already called out above.
