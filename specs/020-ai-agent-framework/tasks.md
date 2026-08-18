---

description: "Task list for AI Agent Framework & Agent Runtime"
---

# Tasks: AI Agent Framework & Agent Runtime

**Input**: Design documents from `/specs/020-ai-agent-framework/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: Included. The constitution (§10 Testing Standards, §16 Quality Gates, §19 Definition of Done) mandates tests for all new observable behavior in the same PR that introduces it, and every prior feature in this repo (Prompts, Memory, Documents) ships with full-depth test coverage — this is a standing, repo-wide "explicit request," not an optional add-on for this feature.

**Organization**: Tasks are grouped by user story (spec.md's 6 prioritized stories) to enable independent implementation and testing of each.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps to spec.md's US1–US6; omitted for Setup/Foundational/Polish tasks
- Every task names its exact file path(s)

## Path Conventions

Existing modular monolith (plan.md Project Structure) — no new projects:
`src/AskLucy.Domain/Agents/`, `src/AskLucy.Application/Agents/`, `src/AskLucy.Infrastructure/Agents/`, `src/AskLucy.Persistence/{Configurations,Repositories}/Agents/`, `src/AskLucy.Web/Controllers/v1/`, `src/AskLucy.Web/ClientApp/src/features/agents/`, `tests/AskLucy.*.Tests/Agents/`.

---

## Phase 1: Setup

**Purpose**: Minimal scaffolding — this extends an existing solution, so there is no project/toolchain initialization.

- [X] T001 [P] Create the empty `Agents` folders this plan targets: `src/AskLucy.Domain/Agents/`, `src/AskLucy.Application/Agents/{Commands,Queries,Authorization,Tools,Runtime}/`, `src/AskLucy.Infrastructure/Agents/`, `src/AskLucy.Persistence/Configurations/Agents/`, `src/AskLucy.Persistence/Repositories/Agents/`, `src/AskLucy.Web/ClientApp/src/features/agents/{api,hooks,components,pages}/`
- [X] T002 [P] Add `AgentRuntimeOptions` (`IOptions<T>`, `ValidateOnStart`, per constitution §4) with `DefaultMaxConcurrentExecutions`, `DefaultMaxSteps`, `DefaultMaxExecutionDurationSeconds`, `DefaultMaxTokens`, `DefaultMaxCost`, `DefaultMaxRetries`, `DefaultMaxToolCalls` in `src/AskLucy.Application/Options/AgentRuntimeOptions.cs`
- [X] T003 Add an `"Agents"` configuration section with `AgentRuntimeOptions` defaults to `src/AskLucy.Web/appsettings.json` and `appsettings.Development.json`, and bind/validate it in `src/AskLucy.Web/Program.cs` (depends on T002)

**Checkpoint**: Scaffolding ready.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain model, persistence, and shared runtime contracts every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain entities, value objects, enums (data-model.md)

- [X] T004 [P] `Agent` aggregate root + `AgentType`/`AgentStatus`/`AgentOutputFormat` enums + `AgentInstructions` owned value object in `src/AskLucy.Domain/Agents/Agent.cs`
- [X] T005 [P] `AgentVersion` entity + `AgentExecutionPolicy` owned value object in `src/AskLucy.Domain/Agents/AgentVersion.cs`
- [X] T006 [P] `AgentTool` entity in `src/AskLucy.Domain/Agents/AgentTool.cs`
- [X] T007 [P] `AgentKnowledgeBase` entity in `src/AskLucy.Domain/Agents/AgentKnowledgeBase.cs`
- [X] T008 [P] `AgentMemoryPolicy` entity in `src/AskLucy.Domain/Agents/AgentMemoryPolicy.cs`
- [X] T009 [P] `AgentExecution` aggregate root + `AgentExecutionStatus`/`AgentConversationIntegrationMode` enums in `src/AskLucy.Domain/Agents/AgentExecution.cs`
- [X] T010 [P] `AgentExecutionStep` entity + `AgentExecutionStepStatus`/`AgentExecutionStepType` enums in `src/AskLucy.Domain/Agents/AgentExecutionStep.cs`
- [X] T011 [P] `AgentExecutionEvent` entity + `AgentExecutionEventType` enum in `src/AskLucy.Domain/Agents/AgentExecutionEvent.cs`
- [X] T012 [P] `AgentToolCall` entity + `AgentToolRiskLevel` enum in `src/AskLucy.Domain/Agents/AgentToolCall.cs`
- [X] T013 [P] `AgentApproval` entity + `AgentApprovalDecision` enum in `src/AskLucy.Domain/Agents/AgentApproval.cs`
- [X] T014 [P] `AgentExecutionError` entity + `AgentExecutionErrorCategory` enum in `src/AskLucy.Domain/Agents/AgentExecutionError.cs`
- [X] T015 [P] `AgentExecutionUsage` entity in `src/AskLucy.Domain/Agents/AgentExecutionUsage.cs`
- [X] T016 [P] `AgentExecutionCost` entity in `src/AskLucy.Domain/Agents/AgentExecutionCost.cs`
- [X] T017 [P] `AgentPolicy` entity (reserved nullable `OrganizationId`, research.md Decision 1) in `src/AskLucy.Domain/Agents/AgentPolicy.cs`
- [X] T018 [P] `AgentUserExecutionLimit` entity (research.md Decision 2) in `src/AskLucy.Domain/Agents/AgentUserExecutionLimit.cs`
- [X] T019 [P] `AgentAuditLog` entity + `AgentAuditAction` enum in `src/AskLucy.Domain/Agents/AgentAuditLog.cs`
- [X] T020 [P] Domain invariant tests for the `Agent` aggregate (Draft→Published, version immutability, duplicate never copies version history) in `tests/AskLucy.Domain.Tests/Agents/AgentTests.cs`
- [X] T021 [P] Domain invariant tests for the `AgentExecution` state machine (data-model.md transition table) in `tests/AskLucy.Domain.Tests/Agents/AgentExecutionTests.cs`

### Persistence (EF Core)

- [X] T022 [P] EF configuration for `Agent` (+ owned `AgentInstructions`) in `src/AskLucy.Persistence/Configurations/Agents/AgentConfiguration.cs`
- [X] T023 [P] EF configuration for `AgentVersion` (+ owned `AgentExecutionPolicy`) in `src/AskLucy.Persistence/Configurations/Agents/AgentVersionConfiguration.cs`
- [X] T024 [P] EF configuration for `AgentTool` in `src/AskLucy.Persistence/Configurations/Agents/AgentToolConfiguration.cs`
- [X] T025 [P] EF configuration for `AgentKnowledgeBase` in `src/AskLucy.Persistence/Configurations/Agents/AgentKnowledgeBaseConfiguration.cs`
- [X] T026 [P] EF configuration for `AgentMemoryPolicy` in `src/AskLucy.Persistence/Configurations/Agents/AgentMemoryPolicyConfiguration.cs`
- [X] T027 [P] EF configuration for `AgentExecution` in `src/AskLucy.Persistence/Configurations/Agents/AgentExecutionConfiguration.cs`
- [X] T028 [P] EF configuration for `AgentExecutionStep` in `src/AskLucy.Persistence/Configurations/Agents/AgentExecutionStepConfiguration.cs`
- [X] T029 [P] EF configuration for `AgentExecutionEvent` in `src/AskLucy.Persistence/Configurations/Agents/AgentExecutionEventConfiguration.cs`
- [X] T030 [P] EF configuration for `AgentToolCall` in `src/AskLucy.Persistence/Configurations/Agents/AgentToolCallConfiguration.cs`
- [X] T031 [P] EF configuration for `AgentApproval` in `src/AskLucy.Persistence/Configurations/Agents/AgentApprovalConfiguration.cs`
- [X] T032 [P] EF configuration for `AgentExecutionError` in `src/AskLucy.Persistence/Configurations/Agents/AgentExecutionErrorConfiguration.cs`
- [X] T033 [P] EF configuration for `AgentExecutionUsage` in `src/AskLucy.Persistence/Configurations/Agents/AgentExecutionUsageConfiguration.cs`
- [X] T034 [P] EF configuration for `AgentExecutionCost` in `src/AskLucy.Persistence/Configurations/Agents/AgentExecutionCostConfiguration.cs`
- [X] T035 [P] EF configuration for `AgentPolicy` in `src/AskLucy.Persistence/Configurations/Agents/AgentPolicyConfiguration.cs`
- [X] T036 [P] EF configuration for `AgentUserExecutionLimit` in `src/AskLucy.Persistence/Configurations/Agents/AgentUserExecutionLimitConfiguration.cs`
- [X] T037 [P] EF configuration for `AgentAuditLog` (no hard FK to `AgentExecution`, per data-model.md) in `src/AskLucy.Persistence/Configurations/Agents/AgentAuditLogConfiguration.cs`
- [X] T038 Register all 16 new `DbSet<T>` properties on `src/AskLucy.Persistence/AskLucyDbContext.cs` (depends on T022–T037)
- [X] T039 Generate the `AddAgentsModule` EF Core migration covering every entity from T022–T037 (depends on T038)

### Repositories

- [X] T040 [P] `IAgentRepository` (Agent aggregate: CRUD, `GetByIdForOwnerAsync`, version publish/list) in `src/AskLucy.Application/Abstractions/IAgentRepository.cs`
- [X] T041 [P] `IAgentExecutionRepository` (Execution aggregate: `Add`, `GetByIdAsync` incl. children, `ListByUserAsync` cursor-paginated, `CountActiveByUserAsync` for FR-042) in `src/AskLucy.Application/Abstractions/IAgentExecutionRepository.cs`
- [X] T042 [P] `IAgentPolicyRepository` (`AgentPolicy` + `AgentUserExecutionLimit`) in `src/AskLucy.Application/Abstractions/IAgentPolicyRepository.cs`
- [X] T043 [P] `IAgentAuditLogRepository` in `src/AskLucy.Application/Abstractions/IAgentAuditLogRepository.cs`
- [X] T044 [P] `AgentRepository` implementation in `src/AskLucy.Persistence/Repositories/Agents/AgentRepository.cs` (depends on T040)
- [X] T045 [P] `AgentExecutionRepository` implementation in `src/AskLucy.Persistence/Repositories/Agents/AgentExecutionRepository.cs` (depends on T041)
- [X] T046 [P] `AgentPolicyRepository` implementation in `src/AskLucy.Persistence/Repositories/Agents/AgentPolicyRepository.cs` (depends on T042)
- [X] T047 [P] `AgentAuditLogRepository` implementation in `src/AskLucy.Persistence/Repositories/Agents/AgentAuditLogRepository.cs` (depends on T043)
- [X] T048 Register all four repositories in `src/AskLucy.Persistence/DependencyInjection.cs` (depends on T044–T047)

### Shared runtime contracts

- [X] T049 [P] `IAgentTool`, `AgentToolResult`, `AgentToolExecutionContext`, `AgentToolPermission` enum (contracts/agent-tool-contract.md) in `src/AskLucy.Application/Agents/Tools/IAgentTool.cs`
- [X] T050 [P] `AgentToolCatalog` (resolves `IEnumerable<IAgentTool>` by name) in `src/AskLucy.Application/Agents/Tools/AgentToolCatalog.cs` (depends on T049)
- [X] T051 [P] `AgentOwnershipGuard` (mirrors `PromptOwnershipGuard` — 404 not 403) in `src/AskLucy.Application/Agents/Authorization/AgentOwnershipGuard.cs`
- [X] T052 [P] `AgentExecutionOwnershipGuard` in `src/AskLucy.Application/Agents/Authorization/AgentExecutionOwnershipGuard.cs`
- [X] T053 Register `AgentToolCatalog` in the Application DI composition root (`src/AskLucy.Application/DependencyInjection.cs`); each user-story tool task below appends its own registration line here (depends on T050)

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Create and Run a Simple Agent (Priority: P1) 🎯 MVP

**Goal**: A user creates an agent (name/description/instructions/model/output format), starts it with an objective, and gets a final persisted result.

**Independent Test**: Create an agent with only instructions + model (no tools/KBs), give it a simple objective, confirm a final structured result is produced, persisted, and retrievable afterward.

### Tests for User Story 1

- [X] T054 [P] [US1] `CreateAgentCommandHandlerTests` in `tests/AskLucy.Application.Tests/Agents/CreateAgentCommandHandlerTests.cs`
- [X] T055 [P] [US1] `PublishAgentVersionCommandHandlerTests` (immutable snapshot correctness) in `tests/AskLucy.Application.Tests/Agents/PublishAgentVersionCommandHandlerTests.cs`
- [X] T056 [P] [US1] `StartAgentExecutionCommandHandlerTests` (no-tool path) in `tests/AskLucy.Application.Tests/Agents/StartAgentExecutionCommandHandlerTests.cs`
- [X] T057 [P] [US1] `AgentExecutionOrchestratorTests` (single `ModelReasoning` step, happy path) in `tests/AskLucy.Application.Tests/Agents/AgentExecutionOrchestratorTests.cs`
- [X] T058 [P] [US1] `AgentsControllerTests` (create/get/list/update/publish, 404-not-403 ownership) in `tests/AskLucy.Web.Tests/Agents/AgentsControllerTests.cs`
- [X] T059 [P] [US1] E2E `AgentCreateAndRun.spec.ts` in `tests/AskLucy.E2E.Tests/AgentCreateAndRun.spec.ts`
- [X] T059a [P] [US1] E2E `AgentConversationIntegration.spec.ts` covering all three `conversationIntegrationMode` values (`Standalone`/`NewConversation`/`ExistingConversation`, FR-051/FR-052) in `tests/AskLucy.E2E.Tests/AgentConversationIntegration.spec.ts`

### Implementation for User Story 1

- [X] T060 [P] [US1] `CreateAgentCommand` + Handler + Validator in `src/AskLucy.Application/Agents/Commands/CreateAgent/`
- [X] T061 [P] [US1] `UpdateAgentCommand` + Handler + Validator (draft fields: name/description/instructions/model/outputFormat/limits) in `src/AskLucy.Application/Agents/Commands/UpdateAgent/`
- [X] T062 [P] [US1] `PublishAgentVersionCommand` + Handler + Validator (snapshot per data-model.md `AgentVersion`) in `src/AskLucy.Application/Agents/Commands/PublishAgentVersion/`
- [X] T063 [P] [US1] `GetAgentQuery` + Handler + `AgentDetailDto` in `src/AskLucy.Application/Agents/Queries/GetAgent/`
- [X] T064 [P] [US1] `ListAgentsQuery` + Handler + `AgentListItemDto` (cursor-paginated) in `src/AskLucy.Application/Agents/Queries/ListAgents/`
- [X] T065 [US1] `IAgentPlanner`/`AgentPlanner`: trivial single-step plan when no tools are configured; JSON-mode planning call via `IAIProvider.ChatAsync` otherwise (research.md Decisions 3, 11) in `src/AskLucy.Application/Agents/Runtime/IAgentPlanner.cs`, `AgentPlanner.cs` (depends on T004–T019, T049)
- [X] T066 [US1] `AgentExecutionOrchestrator`: step loop executing a `ModelReasoning`-only plan end-to-end, persisting `AgentExecutionStep`/`AgentExecution` status transitions in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (depends on T065)
- [X] T066a [US1] On any step or execution failure, persist an `AgentExecutionError` row (category/message/retryCount) before transitioning `AgentExecution.Status` to `Failed` — never a caught-and-discarded exception (constitution §2.VIII) — in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (depends on T066)
- [X] T067 [US1] `IAgentExecutionRunner` + `AgentExecutionRunnerJob` (Hangfire-dispatched wrapper, research.md Decision 8) in `src/AskLucy.Application/Abstractions/IAgentExecutionRunner.cs`, `src/AskLucy.Infrastructure/Agents/AgentExecutionRunnerJob.cs` (depends on T066)
- [X] T068 [US1] `StartAgentExecutionCommand` + Handler + Validator: creates the `AgentExecution` row, enqueues via injected `IBackgroundJobClient.Enqueue<IAgentExecutionRunner>` (never the static Hangfire facade) in `src/AskLucy.Application/Agents/Commands/StartAgentExecution/` (depends on T067)
- [X] T068a [US1] Extend `StartAgentExecutionCommand`/Handler to accept `conversationIntegrationMode` (`ExistingConversation`|`NewConversation`|`Standalone`) and `userChatId` (required for `ExistingConversation`); for `NewConversation`, call `CreateUserChatCommand` via `ISender` before creating the `AgentExecution` row and set `AgentExecution.UserChatId` to the result; for `ExistingConversation`, validate ownership via `ChatOwnershipGuard` and set it directly (FR-051) in `src/AskLucy.Application/Agents/Commands/StartAgentExecution/StartAgentExecutionCommandHandler.cs` (depends on T068)
- [X] T068b [US1] On execution completion, post the objective + final result into the linked conversation via `AppendMessageCommand` (user turn + assistant turn) whenever `ConversationIntegrationMode != Standalone` (FR-052) in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (depends on T066, T068a)
- [X] T069 [P] [US1] `GetAgentExecutionQuery` + Handler + `AgentExecutionDetailDto` (full history assembly) in `src/AskLucy.Application/Agents/Queries/GetAgentExecution/`
- [X] T070 [US1] `AgentsController`: `POST/GET/PUT /agents`, `GET /agents`, `POST /agents/{id}/versions` in `src/AskLucy.Web/Controllers/v1/AgentsController.cs` (depends on T060–T064)
- [X] T071 [US1] `AgentExecutionsController`: `POST /agent-executions`, `GET /agent-executions/{id}` in `src/AskLucy.Web/Controllers/v1/AgentExecutionsController.cs` (depends on T068, T069)
- [X] T072 [P] [US1] Register `AgentExecutionRunnerJob`/`IAgentExecutionRunner` in `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T067)
- [X] T073 [P] [US1] `agentsApi.ts`, `agentExecutionsApi.ts` fetch wrappers in `src/AskLucy.Web/ClientApp/src/features/agents/api/`
- [X] T074 [P] [US1] `useAgents.ts`, `useAgentMutations.ts`, `useAgentExecution.ts` hooks in `src/AskLucy.Web/ClientApp/src/features/agents/hooks/`
- [X] T075 [US1] `AgentBuilder.tsx` (identity/instructions/model/output-format fields only) in `src/AskLucy.Web/ClientApp/src/features/agents/components/AgentBuilder.tsx` (depends on T074)
- [X] T076 [P] [US1] `AgentLibraryPage.tsx`, `AgentBuilderPage.tsx` in `src/AskLucy.Web/ClientApp/src/features/agents/pages/`
- [X] T077 [US1] `ExecutionConsole.tsx`: minimal execution trigger + final-result display (no live streaming yet — US4) in `src/AskLucy.Web/ClientApp/src/features/agents/components/ExecutionConsole.tsx` (depends on T074)

**Checkpoint**: User Story 1 fully functional and independently testable.

---

## Phase 4: User Story 2 - Multi-Step Task Execution with Tools (Priority: P2)

**Goal**: A Task Agent with tools + Knowledge Bases plans and executes multiple steps toward an objective, producing a cited final result.

**Independent Test**: Agent with ≥1 tool + 1 Knowledge Base, objective requiring retrieval+synthesis; execution history shows a multi-step plan, ≥1 tool call, and citations in the final result.

### Tests for User Story 2

- [X] T078 [P] [US2] `AgentToolPermissionTests` in `tests/AskLucy.Application.Tests/Agents/AgentToolPermissionTests.cs`
- [X] T079 [P] [US2] `AgentBudgetGuardTests` in `tests/AskLucy.Application.Tests/Agents/AgentBudgetGuardTests.cs`
- [X] T080 [P] [US2] `AgentDuplicateToolCallDetectionTests` in `tests/AskLucy.Application.Tests/Agents/AgentDuplicateToolCallDetectionTests.cs`
- [X] T080a [P] [US2] `AgentResourceConflictTests` (two concurrent executions writing the same memory candidate — the second is rejected, not silently overwritten, FR-041) in `tests/AskLucy.Application.Tests/Agents/AgentResourceConflictTests.cs`
- [X] T081 [P] [US2] `AgentPlannerTests` (multi-step tool-selecting plans; invalid-JSON one-retry path) in `tests/AskLucy.Application.Tests/Agents/AgentPlannerTests.cs`
- [X] T082 [P] [US2] `KnowledgeSearchToolTests` (citation preservation) in `tests/AskLucy.Application.Tests/Agents/KnowledgeSearchToolTests.cs`
- [X] T083 [P] [US2] `MemoryWriteToolIntegrationTests` (`CreateMemoryCandidateCommand` path) in `tests/AskLucy.Application.Tests/Agents/MemoryWriteToolIntegrationTests.cs`
- [X] T084 [P] [US2] E2E `AgentMultiStepToolExecution.spec.ts` in `tests/AskLucy.E2E.Tests/AgentMultiStepToolExecution.spec.ts`

### Implementation for User Story 2

- [X] T085 [US2] Extend `UpdateAgentCommand` to accept `tools[]`/`knowledgeBases[]`/`memoryPolicy` in `src/AskLucy.Application/Agents/Commands/UpdateAgent/` (depends on T061)
- [X] T086 [P] [US2] `ConversationTool` in `src/AskLucy.Application/Agents/Tools/ConversationTool.cs` (depends on T049)
- [X] T087 [P] [US2] `KnowledgeSearchTool` (`IRagService` + `IKnowledgeBaseRepository.ResolveOwnedIdsAsync`, research.md Decision 4) in `src/AskLucy.Application/Agents/Tools/KnowledgeSearchTool.cs`
- [X] T088 [P] [US2] `DocumentSearchTool` (`IDocumentRepository` + `DocumentOwnershipGuard`) in `src/AskLucy.Application/Agents/Tools/DocumentSearchTool.cs`
- [X] T089 [P] [US2] `MemorySearchTool` (`IMemoryService`, research.md Decision 4) in `src/AskLucy.Application/Agents/Tools/MemorySearchTool.cs`
- [X] T090 [US2] `CreateMemoryCandidateCommand` + Handler (extends the Memory feature, research.md Decision 5) in `src/AskLucy.Application/Memory/Commands/CreateMemoryCandidate/`
- [X] T091 [US2] `MemoryWriteTool` (sends `CreateMemoryCandidateCommand` via `ISender`, Medium risk) in `src/AskLucy.Application/Agents/Tools/MemoryWriteTool.cs` (depends on T090)
- [X] T092 [P] [US2] `PromptExecutionTool` (sends `ExecutePromptCommand` via `ISender`, research.md Decision 6) in `src/AskLucy.Application/Agents/Tools/PromptExecutionTool.cs`
- [X] T093 [P] [US2] `FileReadTool` (`IDocumentRepository` + `DocumentOwnershipGuard` + `IFileStorage`, research.md Decision 7) in `src/AskLucy.Application/Agents/Tools/FileReadTool.cs`
- [X] T094 [P] [US2] `FileMetadataTool` in `src/AskLucy.Application/Agents/Tools/FileMetadataTool.cs`
- [X] T095 [US2] Register all 8 tools with `AgentToolCatalog`/DI (depends on T086–T094)
- [X] T096 [US2] Extend `AgentPlanner` for multi-step, tool-selecting JSON-mode plans against the agent's configured tool set in `src/AskLucy.Application/Agents/Runtime/AgentPlanner.cs` (depends on T065, T095)
- [X] T097 [US2] `AgentBudgetGuard` (FR-039/040: max steps/duration/tokens/cost/tool-calls/retries) in `src/AskLucy.Application/Agents/Runtime/AgentBudgetGuard.cs`
- [X] T098 [US2] `AgentDuplicateToolCallDetector` (FR-039) in `src/AskLucy.Application/Agents/Runtime/AgentDuplicateToolCallDetector.cs`
- [X] T099 [US2] Extend `AgentExecutionOrchestrator`: tool-call step execution (input/output schema validation, permission check, `AgentToolCall` persistence), step dependency ordering (FR-018), conditional execution (FR-019), retry/backoff (FR-037/038), integrate `AgentBudgetGuard`/`AgentDuplicateToolCallDetector` in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (depends on T066, T096, T097, T098)
- [X] T099a [US2] Handle conflicting concurrent writes around write-capable tool calls (e.g. `MemoryWriteTool`) inside the orchestrator's tool-call execution path, translating the failure into an `AgentExecutionErrorCategory` failure with an actionable message instead of letting it bubble to a 500 (FR-041) in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` — implemented via the orchestrator's existing generic `catch (Exception ex)`, not an EF Core-specific `DbUpdateConcurrencyException` catch, since `Application` must not reference EF Core types (constitution §3); verified by `AgentResourceConflictTests` (depends on T099, T066a, T091)
- [X] T099b [US2] Frame all tool/RAG/memory output using `RetrievalPromptFraming` (or a new, identically-shaped `AgentToolResultFraming`) before it re-enters any subsequent `IAIProvider.ChatAsync` call, so retrieved/tool content can never be interpreted as an instruction (FR-005) in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (depends on T099)
- [X] T100 [US2] Assemble final output with preserved citations (FR-045) in the orchestrator's completion path in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (depends on T099)
- [X] T100a [US2] Accumulate `AgentExecutionUsage` (input/output/reasoning tokens, tool-call/step counts) and `AgentExecutionCost` (via `ModelPricing`) from each `ChatCompletionResult`/tool call, persisting both on execution completion (FR-036) in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (depends on T099, T100)

**Checkpoint**: User Stories 1–2 both independently functional.

---

## Phase 5: User Story 3 - Approval for Sensitive Actions (Priority: P3)

**Goal**: A High/Critical-risk tool call pauses execution for explicit user approval (or auto-proceeds under an administrator policy), with the decision recorded.

**Independent Test**: Agent with a High/Critical-risk tool attached, objective triggering it; execution pauses `WaitingForApproval`, shows the intended action, proceeds/ends per the user's (or a matching policy's) decision — recorded in the audit trail.

### Tests for User Story 3

- [X] T101 [P] [US3] `AgentApprovalWorkflowTests` (pause/approve/reject/policy-bypass) in `tests/AskLucy.Application.Tests/Agents/AgentApprovalWorkflowTests.cs`
- [X] T102 [P] [US3] `AgentPolicyEvaluatorTests` in `tests/AskLucy.Application.Tests/Agents/AgentPolicyEvaluatorTests.cs`
- [X] T103 [P] [US3] `AgentPoliciesControllerTests` (`AdministratorOrSuperUser` gate) in `tests/AskLucy.Web.Tests/Agents/AgentPoliciesControllerTests.cs`
- [X] T104 [P] [US3] E2E `AgentToolApproval.spec.ts` in `tests/AskLucy.E2E.Tests/AgentToolApproval.spec.ts`

### Implementation for User Story 3

- [X] T105 [US3] `FakeHighRiskTool` test/dev-only fixture (registered only in Testing/Development, quickstart.md Scenario 3) in `src/AskLucy.Application/Agents/Tools/FakeHighRiskTool.cs` (depends on T049)
- [X] T106 [US3] `AgentPolicyEvaluator` (matches an intended tool call against enabled `AgentPolicy` rows) in `src/AskLucy.Application/Agents/Runtime/AgentPolicyEvaluator.cs` (depends on T042)
- [X] T107 [US3] Extend `AgentExecutionOrchestrator`: High/Critical-risk calls create a `Pending` `AgentApproval` and set status `WaitingForApproval` unless `AgentPolicyEvaluator` finds a match (FR-025/026) in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (depends on T099, T106) — also made `RunAsync` resumable (reuses the persisted plan, rebuilds in-memory context from already-completed steps, never re-runs a completed step) so `ApproveAgentActionCommand`'s re-enqueue continues correctly from the paused step (FR-017)
- [X] T108 [P] [US3] `ApproveAgentActionCommand` + Handler + Validator (re-enqueues the runner) in `src/AskLucy.Application/Agents/Commands/ApproveAgentAction/`
- [X] T109 [P] [US3] `RejectAgentActionCommand` + Handler + Validator in `src/AskLucy.Application/Agents/Commands/RejectAgentAction/`
- [X] T110 [P] [US3] `GetAgentApprovalQuery` + Handler in `src/AskLucy.Application/Agents/Queries/GetAgentApproval/`
- [X] T111 [P] [US3] `CreateAgentPolicyCommand` + `UpdateAgentPolicyCommand` + `DeleteAgentPolicyCommand` (Administrator/Super User only) in `src/AskLucy.Application/Agents/Commands/CreateAgentPolicy/`, `UpdateAgentPolicy/`, `DeleteAgentPolicy/`
- [X] T112 [P] [US3] `ListAgentPoliciesQuery` + Handler in `src/AskLucy.Application/Agents/Queries/ListAgentPolicies/`
- [X] T113 [US3] Extend `AgentExecutionsController` with `GET .../approvals/{id}`, `POST .../approve`, `POST .../reject` (depends on T108–T110)
- [X] T114 [US3] `AgentPoliciesController` (CRUD, `[Authorize(Policy = "AdministratorOrSuperUser")]`) in `src/AskLucy.Web/Controllers/v1/AgentPoliciesController.cs` (depends on T111, T112) — also fixed a pre-existing gap from T085: `UpdateAgentRequest`/`AgentsController.Update` never forwarded `tools[]`/`knowledgeBaseIds[]`/`memoryPolicy` to `UpdateAgentCommand`, so there was no API path to actually attach a tool to an agent
- [X] T115 [P] [US3] `agentPoliciesApi.ts` in `src/AskLucy.Web/ClientApp/src/features/agents/api/agentPoliciesApi.ts`
- [X] T116 [US3] `ApprovalDialog.tsx` (intended action/parameters, approve/reject) in `src/AskLucy.Web/ClientApp/src/features/agents/components/ApprovalDialog.tsx` (depends on T074) — wired into `ExecutionConsole.tsx`, shown automatically whenever the polled execution is `WaitingForApproval`
- [X] T117 [P] [US3] Admin `AgentPolicy` management UI component in `src/AskLucy.Web/ClientApp/src/features/agents/components/AgentPolicyAdminPanel.tsx` — reachable at `/admin/agent-policies` (new `AgentPoliciesAdminPage.tsx` + router entry + link from `AdminDashboardPage.tsx`)

**Checkpoint**: User Stories 1–3 all independently functional.

---

## Phase 6: User Story 4 - Real-Time Execution Visibility (Priority: P4)

**Goal**: Live step/tool-activity/usage/cost visibility during a run, plus pause/cancel control.

**Independent Test**: Start a multi-step execution; confirm the UI reflects step transitions and tool activity without manual refresh, and cancel stops it promptly.

### Tests for User Story 4

- [X] T118 [P] [US4] `AgentExecutionHubTests` (group membership, event push shape) — placed at `tests/AskLucy.Web.Tests/Agents/AgentExecutionHubTests.cs`, not `AskLucy.Infrastructure.Tests` as literally stated: the repo's only existing hub test (`DocumentProcessingHubTests`) lives in `AskLucy.Web.Tests` even though its hub is in `Infrastructure`, so this follows that actual precedent
- [X] T119 [P] [US4] `AgentExecutionRunnerJobTests` (pause/resume/cancel observed at step boundary) — placed at `tests/AskLucy.Application.Tests/Agents/AgentExecutionRunnerJobTests.cs`, not `AskLucy.Infrastructure.Tests` as literally stated: `AgentExecutionOrchestrator`/`AgentExecutionRunner` are both Application-layer classes (research.md Decision 8), the same correction already applied to their actual location earlier in this feature
- [X] T120 [P] [US4] `AgentExecutionControlCommandsTests` (pause/resume/cancel) in `tests/AskLucy.Application.Tests/Agents/AgentExecutionControlCommandsTests.cs`
- [X] T121 [P] [US4] E2E `AgentRealtimeExecutionVisibility.spec.ts` in `tests/AskLucy.E2E.Tests/AgentRealtimeExecutionVisibility.spec.ts`

### Implementation for User Story 4

- [X] T122 [US4] `IAgentExecutionNotifier` + `AgentExecutionHub` + `AgentExecutionNotifier` (contracts/agent-execution-events.md) in `src/AskLucy.Application/Abstractions/IAgentExecutionNotifier.cs`, `src/AskLucy.Infrastructure/Agents/AgentExecutionHub.cs`, `AgentExecutionNotifier.cs`
- [X] T123 [US4] Wire `AgentExecutionEvent` persistence + `IAgentExecutionNotifier` push at every orchestrator transition (`ExecutionStarted`/`PlanCreated`/`StepStarted`/`StepCompleted`/`StepFailed`/`ToolCallStarted`/`ToolCallCompleted`/`ExecutionCompleted`/`Failed`/`Cancelled`/`UsageUpdated`) in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (depends on T099, T122) — interactive `ApprovalGranted`/`ApprovalRejected` are wired in `ApproveAgentActionCommandHandler`/`RejectAgentActionCommandHandler` instead (those decisions happen outside the orchestrator's own run)
- [X] T124 [P] [US4] `PauseAgentExecutionCommand` + Handler in `src/AskLucy.Application/Agents/Commands/PauseAgentExecution/`
- [X] T125 [P] [US4] `ResumeAgentExecutionCommand` + Handler (re-enqueues the runner) in `src/AskLucy.Application/Agents/Commands/ResumeAgentExecution/`
- [X] T126 [P] [US4] `CancelAgentExecutionCommand` + Handler in `src/AskLucy.Application/Agents/Commands/CancelAgentExecution/`
- [X] T127 [US4] Extend the orchestrator's step-boundary loop to observe `Paused`/`Cancelled` status and exit safely (SC-009: ≤5s) in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (depends on T099, T124–T126) — via a new lightweight `IAgentExecutionRepository.GetStatusAsync` (untracked read) checked at the top of every step-loop iteration, so the run exits without re-saving its own now-stale tracked entity against whichever command already persisted the pause/cancel
- [X] T128 [P] [US4] `GetAgentExecutionEventsQuery` + Handler (reconciliation fallback, `since`-timestamp filtered rather than cursor-paginated — a single execution's event stream is bounded, unlike the cross-execution listings elsewhere that need keyset pagination) in `src/AskLucy.Application/Agents/Queries/GetAgentExecutionEvents/`
- [X] T129 [US4] Extend `AgentExecutionsController` with pause/resume/cancel actions + events endpoint (depends on T124–T126, T128)
- [X] T130 [US4] Register the `AgentExecutionHub` route (`/hubs/agent-execution`) in `src/AskLucy.Web/Program.cs` (depends on T122)
- [X] T131 [P] [US4] `useAgentExecutionHub.ts` hook in `src/AskLucy.Web/ClientApp/src/features/agents/hooks/useAgentExecutionHub.ts`
- [X] T132 [US4] `ExecutionConsole.tsx` live step/tool activity + `ExecutionTimeline.tsx`, pause/resume/cancel controls in `src/AskLucy.Web/ClientApp/src/features/agents/components/` (depends on T131, T077)

**Checkpoint**: User Stories 1–4 all independently functional.

---

## Phase 7: User Story 5 - Execution History & Audit (Priority: P5)

**Goal**: Full inspectable history for any past execution; strict cross-user access denial.

**Independent Test**: Run an agent to completion; independently open its history and confirm every recorded field is present and matches what happened.

### Tests for User Story 5

- [X] T133 [P] [US5] `AgentCrossUserSecurityTests` (404-not-403 for another user's execution) in `tests/AskLucy.Application.Tests/Agents/AgentCrossUserSecurityTests.cs`
- [X] T134 [P] [US5] `AgentExecutionHistorySecurityTests` in `tests/AskLucy.Web.Tests/Agents/AgentExecutionHistorySecurityTests.cs`
- [X] T135 [P] [US5] E2E `AgentExecutionHistory.spec.ts` in `tests/AskLucy.E2E.Tests/AgentExecutionHistory.spec.ts`

### Implementation for User Story 5

- [X] T136 [P] [US5] `ListAgentExecutionsQuery` + Handler (cursor-paginated, filters `agentId`/`status`/`isTestExecution`) in `src/AskLucy.Application/Agents/Queries/ListAgentExecutions/`
- [X] T137 [P] [US5] `GetAgentExecutionStepsQuery` + Handler in `src/AskLucy.Application/Agents/Queries/GetAgentExecutionSteps/`
- [X] T138 [P] [US5] `GetAgentToolCallsQuery` + Handler in `src/AskLucy.Application/Agents/Queries/GetAgentToolCalls/`
- [X] T139 [P] [US5] `GetAgentExecutionUsageQuery` + Handler (usage + cost together) in `src/AskLucy.Application/Agents/Queries/GetAgentExecutionUsage/`
- [X] T140 [US5] Wire `AgentAuditLog` writes for `PermissionChecked`/`PermissionDenied`/`ApprovalDecided`/`CrossUserAccessAttempted`/`ExecutionCompleted`/`ExecutionFailed` at the relevant guard/orchestrator points (depends on T099, T107, T051, T052) — kept `AgentOwnershipGuard`/`AgentExecutionOwnershipGuard` themselves static/pure (unchanged, matching every other ownership guard in the codebase) and instead wrote audit rows at the calling handlers/orchestrator: `PermissionChecked` on `StartAgentExecutionCommandHandler`; `CrossUserAccessAttempted` only on `GetAgentExecutionQueryHandler`'s not-found path (and only when the execution provably belongs to someone else, never on a genuine 404, and never on the hot polled happy path); `PermissionDenied` in the orchestrator's catch block when a tool's own ownership guard throws `KeyNotFoundException`; `ApprovalDecided` in `Approve`/`RejectAgentActionCommandHandler`; `ExecutionCompleted`/`ExecutionFailed` in the orchestrator's completion/catch paths
- [X] T141 [US5] Extend `AgentExecutionsController` with list/steps/tool-calls/usage endpoints (depends on T136–T139)
- [X] T142 [P] [US5] `ExecutionHistoryList.tsx` in `src/AskLucy.Web/ClientApp/src/features/agents/components/ExecutionHistoryList.tsx`
- [X] T143 [US5] `AgentExecutionPage.tsx`: detail view assembling steps/tool-calls/approvals/usage/cost/citations in `src/AskLucy.Web/ClientApp/src/features/agents/pages/AgentExecutionPage.tsx` (depends on T142, T132) — new route `/agents/:agentId/executions/:executionId`, linked from a new `ExecutionHistoryList` section on `AgentBuilderPage.tsx`

**Checkpoint**: User Stories 1–5 all independently functional.

---

## Phase 8: User Story 6 - Agent Versioning & Testing (Priority: P6)

**Goal**: Safe draft iteration in a test console, immutable published versions, executions always reference their exact version.

**Independent Test**: Publish v1, edit, publish v2; confirm an execution started under v1 still reports v1 after v2 exists; duplicate/archive/restore all behave correctly.

### Tests for User Story 6

- [X] T144 [P] [US6] `AgentVersioningTests` (immutability, exact-version-referenced-by-execution) in `tests/AskLucy.Application.Tests/Agents/AgentVersioningTests.cs`
- [X] T145 [P] [US6] `DuplicateArchiveRestoreAgentTests` in `tests/AskLucy.Application.Tests/Agents/DuplicateArchiveRestoreAgentTests.cs`
- [X] T145a [US6] `TestExecutionSkipsMutatingToolsTests` (an `isTestExecution: true` run with `MemoryWriteTool` configured never creates a `Memory`/`MemoryApproval` row; the step is recorded `Skipped`, SC-007/research.md Decision 12) in `tests/AskLucy.Application.Tests/Agents/TestExecutionSkipsMutatingToolsTests.cs` (depends on T153a)
- [X] T146 [P] [US6] E2E `AgentVersioning.spec.ts` in `tests/AskLucy.E2E.Tests/AgentVersioning.spec.ts`

### Implementation for User Story 6

- [X] T147 [P] [US6] `DuplicateAgentCommand` + Handler in `src/AskLucy.Application/Agents/Commands/DuplicateAgent/`
- [X] T148 [P] [US6] `ArchiveAgentCommand` + Handler in `src/AskLucy.Application/Agents/Commands/ArchiveAgent/`
- [X] T149 [P] [US6] `RestoreAgentCommand` + Handler in `src/AskLucy.Application/Agents/Commands/RestoreAgent/`
- [X] T150 [P] [US6] `DeleteAgentCommand` + Handler (soft delete, never cascades to versions/executions) in `src/AskLucy.Application/Agents/Commands/DeleteAgent/`
- [X] T151 [P] [US6] `ListAgentVersionsQuery` + Handler in `src/AskLucy.Application/Agents/Queries/ListAgentVersions/`
- [X] T152 [P] [US6] `GetAgentVersionQuery` + Handler in `src/AskLucy.Application/Agents/Queries/GetAgentVersion/`
- [X] T153 [US6] Extend `StartAgentExecutionCommand` to accept an explicit `agentVersionNumber` + `isTestExecution` flag, defaulting to `Agent.PublishedVersionNumber` in `src/AskLucy.Application/Agents/Commands/StartAgentExecution/` (depends on T068, T068a) — already satisfied by T068/T068a's original implementation, verified still correct, no change needed
- [X] T153a [US6] Extend `AgentExecutionOrchestrator`'s tool-call step execution: when `AgentExecution.IsTestExecution` is `true` and the selected tool's `RequiredPermissions` include a mutating permission, skip `ExecuteAsync` entirely and record the step `Skipped` with reason `"write actions are disabled for test executions"` (SC-007, research.md Decision 12) in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (depends on T099, T153)
- [X] T154 [US6] Extend `AgentsController` with duplicate/archive/restore/delete/versions endpoints (depends on T147–T152)
- [X] T155 [P] [US6] `VersionHistory.tsx` + `useAgentVersions.ts` in `src/AskLucy.Web/ClientApp/src/features/agents/components/VersionHistory.tsx`, `src/AskLucy.Web/ClientApp/src/features/agents/hooks/useAgentVersions.ts`
- [X] T156 [US6] `TestingConsole.tsx` (select version, run, `isTestExecution`-flagged results) in `src/AskLucy.Web/ClientApp/src/features/agents/components/TestingConsole.tsx` (depends on T155, T077)
- [X] T157 [US6] Duplicate/archive/restore/delete actions on `AgentLibraryPage.tsx`/`AgentBuilderPage.tsx` (depends on T076)

**Checkpoint**: All 6 user stories independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Requirements that span every story (concurrency limits, indexes, docs, security/a11y review) plus final validation.

- [X] T158 [P] `SetAgentUserExecutionLimitCommand` + Handler (Administrator/Super User) in `src/AskLucy.Application/Agents/Commands/SetAgentUserExecutionLimit/`
- [X] T159 Enforce FR-042/043 concurrency cap in `StartAgentExecutionCommandHandler` (check `IAgentExecutionRepository.CountActiveByUserAsync` against `AgentUserExecutionLimit`/`AgentRuntimeOptions` default; reject with a 429 Problem Details) in `src/AskLucy.Application/Agents/Commands/StartAgentExecution/` (depends on T068, T158) — new `AgentConcurrencyLimitExceededException` (Domain) mapped to 429 in `ProblemDetailsMiddleware`, distinct from `DomainRuleViolationException`'s generic 400
- [X] T160 Extend `AgentPoliciesController` with `PUT /agent-policies/user-limits/{userId}` (depends on T158)
- [X] T161 [P] `AgentConcurrencyLimitTests` in `tests/AskLucy.Application.Tests/Agents/AgentConcurrencyLimitTests.cs` (depends on T159)
- [X] T162 [P] Index review pass across every `Configurations/Agents/*.cs` file for every FK/status/owner column used in a query path (constitution §5) — found and fixed one real gap: `AgentPolicies` had separate `ToolName`/`IsEnabled` indexes where `AgentPolicyEvaluator` always filters on both together (checked on every High/Critical-risk tool call); replaced with one composite index (migration `OptimizeAgentPolicyIndex`). Every other FK/status/owner column already correctly indexed from the Foundational phase.
- [X] T163 [P] Accessibility pass (automated axe checks + manual review) on all new `features/agents` components (constitution §7/§10) — representative axe coverage added (`AgentLibraryPage`, `AgentBuilder`, `ApprovalDialog` — a page, a form, and a dialog, the three interaction patterns the rest of the feature's components reuse), not exhaustively every single component; zero violations found
- [X] T164 [P] Update `docs/ARCHITECTURE.md` §15 (Agent Engine) and §21 (Future Expansion) to match the shipped design, in the §28/§29 narrative style already used for Memory/Prompts — added as new §30 (§15/§16 sketches explicitly marked superseded; §21's actual future-expansion list never mentioned agents, so needed no edit; renumbered the closing "Architecture Principles" section to §31
- [X] T165 [P] Update `docs/ENTITY_MODEL.md` §10 (Agent Aggregate) and `docs/DOMAIN_SERVICES.md` §21 (Agent Service) to match data-model.md/contracts/ (constitution §13 — documentation is part of implementation) — also corrected now-inconsistent `AgentRun`-era references elsewhere in ENTITY_MODEL.md (Enumerations, Delete Behavior, Domain Events, Aggregate Invariants) left stale by the §10 rewrite
- [X] T166 [P] Update `docs/DATABASE.md` §11 (Agent Context) to match the shipped schema
- [X] T166a [P] `AgentToolAccessBoundaryTests` (SC-005 end-to-end: one execution configured with `KnowledgeSearchTool`, `DocumentSearchTool`, `MemorySearchTool`, and `FileReadTool` together, where the configured Knowledge Base/document/memory/file each includes at least one item the executing user does *not* own — asserts every tool's result excludes the unauthorized item, not just that the call succeeds) in `tests/AskLucy.Application.Tests/Agents/AgentToolAccessBoundaryTests.cs` — four focused sub-tests (one per tool) rather than one combined mega-scenario, since each tool enforces its boundary differently (KnowledgeSearchTool filters a candidate-id list; DocumentSearchTool/MemorySearchTool are scoped entirely by the repository/service call's `context.UserId`; FileReadTool rejects outright with 404)
- [X] T167 Security review pass: authorization/ownership guards, privilege escalation (tool permissions never exceed the executing user's own), prompt-injection framing, cross-user access coverage in `AgentAuditLog` — per spec.md's Security Tests section. Checked: every Commands/Queries handler for a missing ownership guard (none found — the only handlers without one are `CreateAgentCommandHandler`, which creates a new entity, and the two `List*QueryHandler`s, which scope by `currentUser.UserId` directly, not a checked-by-id entity); every built-in tool's repository/service call scoped to `context.UserId` (`ConversationTool`'s `context.UserChatId` traced back to `StartAgentExecutionCommandHandler`'s `ChatOwnershipGuard`-validated resolution, never attacker-reachable afterward); `AgentPoliciesController`'s class-level `AdministratorOrSuperUser` policy confirmed present; every `contextEntries.Add` in the orchestrator that carries tool/RAG output goes through `RetrievalPromptFraming.BuildToolResultSystemMessage` (the two unframed adds are the model's own prior reasoning output, not external/untrusted content); `GetAgentExecutionQueryHandler` confirmed to log `CrossUserAccessAttempted` only on a verified cross-user hit, never a genuine 404; no raw SQL string concatenation in `Persistence/Repositories/Agent*.cs`/`Configurations/Agents/*.cs`; no TODO/FIXME/HACK markers anywhere in the Agents code. No issues found.
- [ ] T168 Run quickstart.md's 6 scenarios end-to-end against a fully deployed local build (depends on all prior tasks) — **not runnable in this sandbox**: no live SQL Server instance (`PERSISTENCE_TESTS_CONNECTION_STRING` unset — the same pre-existing environment gap noted at every checkpoint this session) and no running frontend dev server + authenticated browser session. Every backend layer's automated test suite (904/904 passing outside `Persistence.Tests`) and a clean frontend production build stand in as the verification available in this environment; the 6 `.spec.ts` E2E files (one per user story, written to this session's established selector/assertion conventions) are ready to run unmodified once a real deployed environment is wired into CI. Remains the one item a human needs to actually execute.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phases 3–8)**: All depend on Foundational. Implementation-wise they build on each other in priority order (US2 extends US1's orchestrator, US3/US4 extend it further, US5 reads data US1–US4 already produce, US6 extends US1's agent/version commands) — matching spec.md's own "Why this priority" reasoning — but each phase's own Independent Test can still be verified in isolation once its dependencies are in place.
- **Polish (Phase 9)**: Depends on every user story phase being complete.

### User Story Dependencies

| Story | Can start after | Notes |
|---|---|---|
| US1 (P1) | Foundational | No dependency on other stories |
| US2 (P2) | Foundational, US1's orchestrator (T066) | Extends the orchestrator with tool-call steps |
| US3 (P3) | Foundational, US2's orchestrator (T099) | Needs a tool call to gate for approval |
| US4 (P4) | Foundational, US2's orchestrator (T099) | Event/hub wiring instruments the same orchestrator |
| US5 (P5) | Foundational, US1–US4 (reads their execution data) | Read-only — adds no new write path |
| US6 (P6) | Foundational, US1's Agent/Version commands (T060–T062) | Extends agent lifecycle, not execution |

### Parallel Opportunities

- All `[P]` Foundational entity (T004–T019), config (T022–T037), and repository-interface (T040–T043) tasks run in parallel — different files, no cross-dependencies.
- Within US2, the 7 non-`MemoryWriteTool` tools (T086–T089, T092–T094) are fully parallel — each is an independent class implementing `IAgentTool`.
- Within any story, `[P]` Command/Query pairs targeting different folders (e.g., T108/T109/T110 in US3) run in parallel.
- Different user story phases may be staffed in parallel once Foundational completes, respecting the table above (e.g., US1 and the Foundational-only parts of US6 could overlap if staffed by different developers, though US6's own tasks still require US1's commands to exist first).

---

## Parallel Example: Foundational Phase

```bash
# Launch all 16 Domain entity tasks together:
Task: "Agent aggregate root in src/AskLucy.Domain/Agents/Agent.cs"
Task: "AgentVersion entity in src/AskLucy.Domain/Agents/AgentVersion.cs"
Task: "AgentTool entity in src/AskLucy.Domain/Agents/AgentTool.cs"
# ... T007–T019

# Then, once entities exist, launch all 16 EF configuration tasks together:
Task: "EF configuration for Agent in src/AskLucy.Persistence/Configurations/Agents/AgentConfiguration.cs"
# ... T023–T037
```

## Parallel Example: User Story 2 Tools

```bash
Task: "ConversationTool in src/AskLucy.Application/Agents/Tools/ConversationTool.cs"
Task: "KnowledgeSearchTool in src/AskLucy.Application/Agents/Tools/KnowledgeSearchTool.cs"
Task: "DocumentSearchTool in src/AskLucy.Application/Agents/Tools/DocumentSearchTool.cs"
Task: "MemorySearchTool in src/AskLucy.Application/Agents/Tools/MemorySearchTool.cs"
Task: "PromptExecutionTool in src/AskLucy.Application/Agents/Tools/PromptExecutionTool.cs"
Task: "FileReadTool in src/AskLucy.Application/Agents/Tools/FileReadTool.cs"
Task: "FileMetadataTool in src/AskLucy.Application/Agents/Tools/FileMetadataTool.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) + Phase 2 (Foundational).
2. Complete Phase 3 (US1).
3. **STOP and VALIDATE**: run quickstart.md Scenario 1 independently.
4. Demo: create an agent, run it, see a persisted result.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → validate (quickstart Scenario 1) → MVP.
3. US2 → validate (Scenario 2) → tool-using agents ship.
4. US3 → validate (Scenario 3) → safe for higher-risk tools once they're added later.
5. US4 → validate (Scenario 4) → live visibility ships.
6. US5 → validate (Scenario 5) → full audit/history ships.
7. US6 → validate (Scenario 6) → versioning/testing workflow ships; feature-complete per spec.md's 6 user stories' acceptance scenarios and SC-001–SC-010.
8. Polish (Phase 9) → concurrency hardening, docs, security/a11y review, full quickstart re-run.

### Parallel Team Strategy

With multiple developers: complete Setup + Foundational together first (it blocks everything and is highly parallelizable itself — see the Foundational parallel example). After that, US1 should still land before US2–US4 start in earnest since they all extend `AgentExecutionOrchestrator`, but US5 (pure read queries) and the agent-lifecycle half of US6 (duplicate/archive/restore, independent of the orchestrator) can be staffed in parallel with US2–US4 once US1's commands/entities exist.

---

## Notes

- `[P]` tasks touch different files with no unfinished-task dependency.
- `[Story]` labels trace every task back to spec.md's US1–US6 for independent-delivery tracking.
- `AgentExecutionOrchestrator.cs` is touched by every phase from US1 through US4 (T066, T066a, T068b, T099, T099a, T099b, T100, T100a, T107, T123, T127) — expected, since it's the one file the whole runtime converges on; each touch is additive (new capability), never a rewrite of prior stories' logic, preserving each earlier story's behavior.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently before continuing.
- Avoid combining two `[P]` tasks that touch the same file — none listed above do.
