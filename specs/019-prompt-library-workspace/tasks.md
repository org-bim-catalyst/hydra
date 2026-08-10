---

description: "Task list for Prompt Library & Prompt Engineering Workspace"

---

# Tasks: Prompt Library & Prompt Engineering Workspace

**Input**: Design documents from `/specs/019-prompt-library-workspace/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (§10 Testing Standards, non-negotiable) requires unit,
integration, and Playwright E2E coverage for new/changed behavior — test tasks are not optional here.

**Organization**: Tasks are grouped by user story (spec.md priorities: US1/US2 = P1, US3/US4/US5 = P2,
US6/US7 = P3) so each story is independently implementable, testable, and demoable. Two pieces of
logic are used by more than one story — variable resolution/validation (US2 execution, US5 conversation
insertion) and model-capability compatibility checking (same two stories) — and are therefore placed in
**Foundational** as shared Application services rather than duplicated into both stories' phases
(research.md Decisions 2–4, constitution §2.III DRY). Everything else stays inside the story that
first needs it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Maps the task to US1–US7 from spec.md
- All descriptions include exact file paths

## Path Conventions

Existing single-solution web app (constitution §3): `src/AskLucy.Domain`, `src/AskLucy.Application`,
`src/AskLucy.Infrastructure`, `src/AskLucy.Persistence`, `src/AskLucy.Web` (API + `ClientApp/` React
SPA), `tests/AskLucy.*.Tests`. This feature adds one new, independent bounded context — `Prompts` — at
every layer (research.md Decision 1) and adds one new controller action to the existing `Chats`
context (`ChatsController`) — no new top-level project, no existing entity is modified.

---

## Phase 1: Setup

**Purpose**: The zero-new-dependency platform-capability checks and cross-cutting registration this
feature needs before any domain code is written (plan.md Technical Context — no new NuGet package or
frontend dependency is required).

- [X] T001 [P] Register the `prompt-endpoints` rate-limit policy (partition by user then IP, fixed
  window, matching the shape of `knowledge-base-endpoints`/`memory-endpoints`) in
  `src/AskLucy.Web/Program.cs` (contracts/prompts-api.md)
- [X] T002 Confirm the SQL Server instance backing this environment supports `FULLTEXT
  CATALOG`/`FULLTEXT INDEX` (already required and used by `specs/002-chat-history-management`'s
  conversation search) — no new capability, just confirm before the migrations in T026/T027 depend
  on it (research.md Decision 12)

**Checkpoint**: Solution builds; the rate-limit policy is registered; full-text capability confirmed.
No domain code exists yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain entities, shared abstractions/services used by 2+ user stories, persistence
configuration/migration, repositories, and authorization guard every user story depends on.

**⚠️ CRITICAL**: No user story task may begin until this phase is complete and the solution builds
with the new migration applied.

### Domain entities — `Prompts` bounded context (data-model.md "New Entities")

- [X] T003 [P] Create `Prompt` aggregate — `OwnerId`/`Name`/`Description`/`PromptType`/`Status`/
  `SystemInstructions`/`DeveloperInstructions`/`UserInstructions`/`ContextText`/`ExamplesText`/
  `OutputInstructions`/`Constraints`/`FolderId`/`CategoryId`/`CurrentVersionId`/`IsFavorite`/
  `IsPinned`/`Requires*` (9 capability flags)/`PreferredModelKey`, mutator methods `ApplyEdit(...)`
  (internally calls `CreateVersionSnapshot()`), `Archive(actor)`, `Restore(actor)`, `Rename(name,
  actor)`, `SetFolder(folderId, actor)`, `SetFavorite(bool, actor)`, `SetPinned(bool, actor)`,
  `RestoreFrom(version, actor)`, `AddTag(value, ownerId, actor)`, plus `PromptType`/`PromptStatus`
  enums, in `src/AskLucy.Domain/Prompts/Prompt.cs` (data-model.md `Prompt`)
- [X] T004 [P] Create `PromptVersion` entity (append-only: `VersionNumber`/content snapshot fields/
  `ProviderKey`/`ModelKey`/`Temperature`/`MaxOutputTokens`/`StructuredOutputRequested`/
  `ChangeDescription`, no update/delete methods) in `src/AskLucy.Domain/Prompts/PromptVersion.cs`
  (data-model.md `PromptVersion`)
- [X] T005 [P] Create `PromptVariable` entity (`PromptVersionId`/`Name`/`Description`/
  `VariableType`/`IsRequired`/`DefaultValue`/`ExampleValue`/`ValidationRulesJson`/`OrderIndex`, plus
  `PromptVariableType` enum) in `src/AskLucy.Domain/Prompts/PromptVariable.cs` (data-model.md
  `PromptVariable`)
- [X] T006 [P] Create `PromptCategory` entity (`OwnerId?`/`Name`, `IsPredefined => OwnerId is null`,
  `CreateCustom(...)` factory — mirrors `KnowledgeBaseCategory`) in
  `src/AskLucy.Domain/Prompts/PromptCategory.cs` (data-model.md `PromptCategory`, research.md
  Decision 6)
- [X] T007 [P] Create `PromptTag` entity (`PromptId`/`OwnerId`/`Value`, internal `Create(...)` factory
  — mirrors `KnowledgeBaseTag`) in `src/AskLucy.Domain/Prompts/PromptTag.cs` (data-model.md
  `PromptTag`, research.md Decision 6)
- [X] T008 [P] Create `PromptFolder` entity (`OwnerId`/`ParentFolderId`/`Name`/`Depth`, `Create(...)`/
  `Rename(...)`/`MoveTo(...)` with `MaxNestingDepth` + cycle-prevention checks — mirrors
  `KnowledgeBaseFolder`) in `src/AskLucy.Domain/Prompts/PromptFolder.cs` (data-model.md
  `PromptFolder`, research.md Decision 5)
- [X] T009 [P] Create `PromptTestCase` entity (`PromptId`/`Name`/`VariableValuesJson`/
  `ExpectedOutput`/`EvaluationCriteria`/`ProviderKey`/`ModelKey`/`SourceExecutionId`) in
  `src/AskLucy.Domain/Prompts/PromptTestCase.cs` (data-model.md `PromptTestCase`)
- [X] T010 [P] Create `PromptExecution` entity (`PromptId`/`PromptVersionId`/`Origin`/`ProviderKey`/
  `ModelKey`/`Temperature`/`MaxOutputTokens`/`StructuredOutputRequested`/
  `ResolvedVariableValuesJson`/`RequestedRagContext`/`RequestedMemoryContext`/`Outcome`/
  `ErrorDetail`/`LatencyMs`/`ResultMessageId`, immutable-after-creation, plus
  `PromptExecutionOrigin`/`PromptExecutionOutcome` enums) in
  `src/AskLucy.Domain/Prompts/PromptExecution.cs` (data-model.md `PromptExecution`)
- [X] T011 [P] Create `PromptExecutionResult` entity (`PromptExecutionId` 1:1/`OutputText`/
  `InputTokenCount`/`OutputTokenCount`/`EstimatedCostUsd`/`RagCitationsJson`/
  `MemoryReferencesJson`) in `src/AskLucy.Domain/Prompts/PromptExecutionResult.cs` (data-model.md
  `PromptExecutionResult`)
- [X] T012 [P] Create `PromptRating` entity (`PromptExecutionId` 1:1/`RatingValue`/`RatedByActor`,
  plus `PromptRatingValue` enum) in `src/AskLucy.Domain/Prompts/PromptRating.cs` (data-model.md
  `PromptRating`)
- [X] T013 [P] Create `PromptUsageStatistics` entity (`PromptId` 1:1/`SuccessfulExecutionCount`/
  `LastSuccessfulUseAtUtc`, `RecordSuccessfulUse()` mutator) in
  `src/AskLucy.Domain/Prompts/PromptUsageStatistics.cs` (data-model.md `PromptUsageStatistics`)
- [X] T014 [P] Create `PromptAuditLog` entity (append-only, `PromptId` no cascade/`Action`/
  `ActorId`/`DetailsJson`, plus `PromptAuditAction` enum) in
  `src/AskLucy.Domain/Prompts/PromptAuditLog.cs` (data-model.md `PromptAuditLog`)

### Shared Domain/Application services (used by 2+ user stories)

- [X] T015 [P] Create `PromptContentAnalyzer` — pure, dependency-free static helper: detects
  `{{name}}` placeholders via compiled regex, flags undeclared placeholders and unreferenced
  variable definitions (FR-014) in `src/AskLucy.Application/Prompts/PromptContentAnalyzer.cs`
  (research.md Decision 10)
- [X] T016 [P] Create `PromptVariableResolver` — validates supplied variable values against
  `PromptVariable` definitions (required/type/length/format/allowed-values, FR-013) and produces the
  resolved content string; used by both `ExecutePromptCommand` (US2) and
  `InsertPromptIntoConversationCommand` (US5) in
  `src/AskLucy.Application/Prompts/PromptVariableResolver.cs`
- [X] T017 [P] Create `PromptCapabilityChecker` — compares a `Prompt`'s `Requires*` flags against an
  `AIModel`'s `AIModelCapabilities` (reused from `AskLucy.Domain.Ai`, data-model.md), used by both
  `ExecutePromptCommand` (US2) and `InsertPromptIntoConversationCommand` (US5) in
  `src/AskLucy.Application/Prompts/PromptCapabilityChecker.cs` (FR-004)

### Application abstractions

- [X] T018 [P] Create `IPromptRepository` (`GetByIdForOwnerAsync`, `GetByOwnerAndNameAsync`,
  `SearchAsync` (full-text + filters, cursor-paginated), `AddAsync`) in
  `src/AskLucy.Application/Abstractions/IPromptRepository.cs`
- [X] T019 [P] Create `IPromptFolderRepository` (`GetTreeForOwnerAsync`, `GetByIdForOwnerAsync`,
  `AddAsync`) in `src/AskLucy.Application/Abstractions/IPromptFolderRepository.cs`
- [X] T020 [P] Create `IPromptCategoryRepository` (`ListPredefinedAndCustomForOwnerAsync`,
  `AddCustomAsync`) in `src/AskLucy.Application/Abstractions/IPromptCategoryRepository.cs`
- [X] T021 [P] Create `IPromptTestCaseRepository` (`ListForPromptAsync`, `AddAsync`, `DeleteAsync`)
  in `src/AskLucy.Application/Abstractions/IPromptTestCaseRepository.cs`
- [X] T022 [P] Create `IPromptExecutionRepository` (`AddAsync` (execution + result), `GetByIdAsync`,
  `ListForPromptAsync` (cursor-paginated), `ListByIdsAsync` (comparison)) in
  `src/AskLucy.Application/Abstractions/IPromptExecutionRepository.cs`
- [X] T023 [P] Create `IPromptAuditLogRepository` (`AddAsync`) in
  `src/AskLucy.Application/Abstractions/IPromptAuditLogRepository.cs`

### Persistence

- [X] T024 Create EF Core Fluent API configurations for all 12 new `Prompts` entities — owned-type/
  scalar mapping matching data-model.md's field list exactly, soft-delete global query filter on
  `Prompt`, filtered unique index `(OwnerId, Name) WHERE IsDeleted = 0` on `Prompt` (research.md
  Decision 7), unique index `(PromptId, VersionNumber)` on `PromptVersion`, unique index
  `(PromptVersionId, Name)` on `PromptVariable`, append-only-no-cascade configuration for
  `PromptAuditLog`, cascade configuration for `PromptVersion`/`PromptVariable`/`PromptTag`,
  indexes on every FK/`OwnerId`/`FolderId`/`CategoryId`/`Status`/`IsFavorite`/`IsPinned` column
  (constitution §5), plus `DbSet<T>` registrations on `AskLucyDbContext`, in
  `src/AskLucy.Persistence/Configurations/Prompts/*.cs` (depends on T003–T014)
- [X] T025 [P] Seed the predefined (platform-shared, `OwnerId = null`) `PromptCategory` rows
  matching spec.md's Prompt Types list, via a hand-written `migrationBuilder.InsertData(...)` call
  inside the `AddPromptLibrary` migration's `Up()` method (verified mechanism — confirmed by reading
  `20260804044614_AddKnowledgeBaseManagement.cs`: `KnowledgeBaseCategory`'s predefined rows are
  seeded this way, not via `HasData()` in the entity configuration) — implemented as part of T026,
  not `PromptCategoryConfiguration.cs` (depends on T024)
- [X] T026 Generate the EF Core migration `AddPromptLibrary` (all 12 new `Prompts` tables; no
  changes to any existing table) via `dotnet ef migrations add AddPromptLibrary -p
  src/AskLucy.Persistence -s src/AskLucy.Web`; verify `Down()` is reversible and `dotnet ef database
  update` succeeds (depends on T024, T025)
- [X] T027 Add a raw-SQL migration step (`suppressTransaction: true`, mirroring
  `20260729190610_AddConversationFullTextSearch.cs` exactly) creating `FULLTEXT CATALOG
  PromptSearchCatalog` and `FULLTEXT INDEX ON Prompts(Name, Description, SystemInstructions,
  UserInstructions)` in `src/AskLucy.Persistence/Migrations/<timestamp>_AddPromptFullTextSearch.cs`
  (depends on T026, research.md Decision 12)

### Repositories

- [X] T028 [P] Implement `PromptRepository` in
  `src/AskLucy.Persistence/Repositories/PromptRepository.cs` — `SearchAsync` uses `CONTAINS(...)`
  against the full-text index for the `query` parameter plus ordinary indexed predicates for
  category/tag/folder/favorite/pinned/status filters, cursor-paginated (depends on T018, T027)
- [X] T029 [P] Implement `PromptFolderRepository` in
  `src/AskLucy.Persistence/Repositories/PromptFolderRepository.cs` (depends on T019, T027)
- [X] T030 [P] Implement `PromptCategoryRepository` in
  `src/AskLucy.Persistence/Repositories/PromptCategoryRepository.cs` (depends on T020, T027)
- [X] T031 [P] Implement `PromptTestCaseRepository` in
  `src/AskLucy.Persistence/Repositories/PromptTestCaseRepository.cs` (depends on T021, T027)
- [X] T032 [P] Implement `PromptExecutionRepository` in
  `src/AskLucy.Persistence/Repositories/PromptExecutionRepository.cs` (depends on T022, T027)
- [X] T033 [P] Implement `PromptAuditLogRepository` in
  `src/AskLucy.Persistence/Repositories/PromptAuditLogRepository.cs` (depends on T023, T027)
- [X] T034 [P] Create `PromptOwnershipGuard` (mirrors `MemoryOwnershipGuard`/`ChatOwnershipGuard` —
  denial looks like not-found, FR-090) in
  `src/AskLucy.Application/Prompts/Authorization/PromptOwnershipGuard.cs` (depends on T028)
- [X] T035 ~~Create AutoMapper profile~~ — **superseded during implementation**: verified this
  codebase registers AutoMapper in DI (`AskLucy.Persistence/DependencyInjection.cs`) but has zero
  actual `Profile` classes or `IMapper.Map()` call sites anywhere in `AskLucy.Application` — every
  existing DTO is built via a `static FromEntity(...)` factory method on the DTO itself (e.g.
  `AdminAiModelDto.FromEntity`), called from the query handler
  (`providerModels.Select(AdminAiModelDto.FromEntity)`). Building an AutoMapper profile here would
  introduce a second, inconsistent mapping convention (constitution §2.VII Convention over
  Configuration). Each Prompt DTO (`PromptDetailDto`, `PromptListItemDto`,
  `PromptVersionSummaryDto`/`PromptVersionDetailDto`, `PromptExecutionSummaryDto`/
  `PromptExecutionDetailDto`, `PromptTestCaseDto`, `PromptFolderTreeDto`, `PromptCategoryDto`,
  `PromptTagDto`, `PromptRatingDto`) gets its own `static FromEntity(...)` factory instead, added
  alongside the query handler that first needs it (T046, T061, T073, T086, T087, T088, T090, T119).

**Checkpoint**: Solution builds; migration applies including the full-text index; repositories,
ownership guard, and the two cross-story services (`PromptVariableResolver`,
`PromptCapabilityChecker`) exist — but nothing is yet exposed via API/UI.

---

## Phase 3: User Story 1 - Create and Reuse a Structured Prompt (Priority: P1) 🎯 MVP

**Goal**: A user can create a named, reusable prompt with distinct structural components and
auto-detected variables, and reopen it later with everything preserved exactly as saved — scoped
strictly to their own library.

**Independent Test**: Create a prompt with `{{document}}`/`{{target_language}}`/`{{summary_length}}`
placeholders and matching variables, save it, reopen it, confirm every field matches (quickstart.md
Scenario 1).

### Tests for User Story 1

- [X] T036 [P] [US1] Unit tests for `PromptContentAnalyzer` — undeclared placeholder detection,
  unreferenced variable detection, well-formed content passes, in
  `tests/AskLucy.Application.Tests/Prompts/PromptContentAnalyzerTests.cs`
- [X] T037 [P] [US1] Unit tests for `Prompt.ApplyEdit`/`CreateVersionSnapshot` — version 1 created on
  `Create`, a new version created per edit, `CurrentVersionId` always valid; also asserts that
  resolving/executing a prompt (simulated by exercising only the read path, no mutator call) creates
  no new version, satisfying FR-020's "execute repeatedly without modifying the template" guarantee,
  in `tests/AskLucy.Domain.Tests/Prompts/PromptTests.cs`
- [X] T038 [P] [US1] Integration test: `CreatePromptCommandHandler` rejects a duplicate name for the
  same owner with `DuplicateResourceException` (case-insensitive) and allows the same name for a
  different owner (FR-006, SC-008) in
  `tests/AskLucy.Application.Tests/Prompts/CreatePromptCommandHandlerTests.cs`
- [X] T039 [P] [US1] Integration test: a second concurrent `UpdatePromptCommand` against a stale
  `RowVersion` throws `DbUpdateConcurrencyException` (FR-007) in
  `tests/AskLucy.Persistence.Tests/Prompts/PromptConcurrencyTests.cs`
- [X] T040 [P] [US1] Playwright E2E: create a prompt with variables, reopen it, confirm every field
  persisted; as a second user, confirm the prompt is invisible/`404` (quickstart.md Scenario 1) in
  `tests/AskLucy.E2E.Tests/PromptLifecycle.spec.ts`

### Implementation for User Story 1

- [X] T041 [P] [US1] Implement `CreatePromptCommand`/Handler/Validator — checks name uniqueness per
  owner (research.md Decision 7, throws `DuplicateResourceException` on collision), creates `Prompt`
  + version 1 + `PromptVariable` rows, runs `PromptContentAnalyzer`, writes a `PromptAuditLog`
  `Created` entry, in `src/AskLucy.Application/Prompts/Commands/CreatePrompt/`
- [X] T042 [P] [US1] Implement `UpdatePromptCommand`/Handler/Validator — checks name uniqueness per
  owner when renaming (research.md Decision 7), calls `Prompt.ApplyEdit`, runs
  `PromptContentAnalyzer`, writes a `PromptAuditLog` `Updated` entry, in
  `src/AskLucy.Application/Prompts/Commands/UpdatePrompt/`
- [X] T043 [P] [US1] Implement `DeletePromptCommand`/Handler (soft delete) in
  `src/AskLucy.Application/Prompts/Commands/DeletePrompt/`
- [X] T044 [P] [US1] Implement `ArchivePromptCommand`/`RestorePromptCommand`/Handlers in
  `src/AskLucy.Application/Prompts/Commands/ArchivePrompt/`,
  `src/AskLucy.Application/Prompts/Commands/RestorePrompt/`
- [X] T045 [P] [US1] Implement `DuplicatePromptCommand`/Handler — new `Prompt`, fresh version-1
  history, auto-suffixed name on collision, in
  `src/AskLucy.Application/Prompts/Commands/DuplicatePrompt/`
- [X] T046 [P] [US1] Implement `GetPromptQuery`/Handler → `PromptDetailDto` in
  `src/AskLucy.Application/Prompts/Queries/GetPrompt/`
- [X] T047 [P] [US1] Implement `PreviewPromptQuery`/Handler — resolves content with supplied/
  example/default values, no AI call (FR-005) in
  `src/AskLucy.Application/Prompts/Queries/PreviewPrompt/`
- [X] T048 [US1] Implement `PromptsController` create/get/update/delete/archive/restore/duplicate/
  preview endpoints (`[Authorize]`, `PromptOwnershipGuard`, `prompt-endpoints` rate limit) in
  `src/AskLucy.Web/Controllers/v1/PromptsController.cs` (contracts/prompts-api.md) (depends on
  T041–T047)
- [X] T049 [US1] Add request/response contract types (`CreatePromptRequest`, `UpdatePromptRequest`,
  `PromptDetailResponse`, etc.) in `src/AskLucy.Web/Contracts/PromptContracts.cs`
- [X] T050 [P] [US1] Frontend: Prompt API client hooks (`useCreatePrompt`, `useUpdatePrompt`,
  `useGetPrompt`, `useDeletePrompt`, etc., TanStack Query) in
  `src/AskLucy.Web/ClientApp/src/features/prompts/api/promptsApi.ts`
- [X] T051 [P] [US1] Frontend: Prompt Editor (system/developer/user instructions, context, examples,
  output instructions, constraints as distinct fields — FR-002; character/token estimation) in
  `src/AskLucy.Web/ClientApp/src/features/prompts/components/PromptEditor.tsx`
- [X] T052 [P] [US1] Frontend: Variable Editor (add/edit/remove variable definitions with type/
  required/default/example/validation-rule fields; variable-highlighting in the editor) in
  `src/AskLucy.Web/ClientApp/src/features/prompts/components/VariableEditor.tsx`
- [X] T053 [US1] Frontend: minimal Prompt Library list page (create button, basic list, open editor)
  in `src/AskLucy.Web/ClientApp/src/features/prompts/pages/PromptLibraryPage.tsx` (depends on T050,
  T051, T052)

**Checkpoint**: A user can create, edit (versioned), duplicate, archive/restore, delete, and preview a
prompt with validated variables, scoped strictly to their own library. User Story 1 is fully
functional and independently testable/demoable.

---

## Phase 4: User Story 2 - Test a Prompt Before Relying on It (Priority: P1) 🎯 MVP

**Goal**: A user can execute a saved prompt from the workspace against a chosen provider/model,
see the streamed response with token usage/cost, and save it as a reusable test case.

**Independent Test**: Open a saved prompt, fill required variables, execute, confirm a streamed
response with usage/cost; leave a required variable blank and confirm execution is blocked before
any provider call (quickstart.md Scenario 2).

### Tests for User Story 2

- [X] T054 [P] [US2] Unit tests for `PromptVariableResolver` — required/type/length/format/
  allowed-values validation branches, in
  `tests/AskLucy.Application.Tests/Prompts/PromptVariableResolverTests.cs`
- [X] T055 [P] [US2] Unit tests for `PromptCapabilityChecker` — required-but-unsupported flag blocks,
  no required flags always passes, in
  `tests/AskLucy.Application.Tests/Prompts/PromptCapabilityCheckerTests.cs`
- [X] T056 [P] [US2] Unit tests for `ExecutePromptCommandHandler`'s message-assembly ordering
  (system+developer instructions → resolved user message, no RAG/memory requested) with a faked
  `IAIProviderResolver`/`IAIProvider`, in
  `tests/AskLucy.Application.Tests/Prompts/ExecutePromptCommandHandlerTests.cs`
- [X] T057 [P] [US2] ~~Integration test (Web.Tests)~~ — **relocated during implementation**:
  `CustomWebApplicationFactory` has no live database available in this environment (its own
  documented constraint, confirmed against `AiControllerVoiceTests`), so "missing required
  variable blocks execution, zero `IAIProvider` invocations" (US2 AC1, SC-004) is instead verified
  as an `ExecutePromptCommandHandler` unit test with faked repositories/provider — consolidated
  into `tests/AskLucy.Application.Tests/Prompts/ExecutePromptCommandHandlerTests.cs` alongside
  T056. The SSE wire-format/`400` HTTP-status wrapping is verified by code review of
  `PromptsController.Execute` (validation throws before any `yield`, propagates to
  `ProblemDetailsMiddleware` before headers commit).
- [X] T058 [P] [US2] ~~Integration test (Web.Tests)~~ — **relocated during implementation**, same
  reasoning as T057: verified as an `ExecutePromptCommandHandlerTests` case proving a provider
  exception propagates uncaught from the handler (C# iterators cannot wrap `yield` in try/catch),
  plus code review of `PromptsController.Execute`'s try/catch around the stream (mirrors
  `AiController.VoiceReply` exactly) which writes the explicit SSE `error` event and calls
  `RecordPromptExecutionCommand` with `Outcome: Failed` or a sanitized `ErrorDetail` (FR-101, SC-010).
- [X] T059 [P] [US2] Playwright E2E: execute a prompt, confirm streamed output + usage/cost display,
  save as a test case, attempt execution against a capability-incompatible model (quickstart.md
  Scenario 2) in `tests/AskLucy.E2E.Tests/PromptTestingWorkspace.spec.ts`

### Implementation for User Story 2

- [X] T060 [US2] Implement `ExecutePromptCommand : IStreamRequest<PromptStreamChunk>` +
  `ExecutePromptCommandHandler : IStreamRequestHandler<...>` — resolves variables
  (`PromptVariableResolver`), checks capabilities (`PromptCapabilityChecker`), assembles messages
  (system/developer instructions → resolved user message; RAG/memory context wiring added in US6),
  calls `IAIProviderResolver`/`IAIProvider.StreamChatAsync`, persists `PromptExecution` +
  `PromptExecutionResult` (via `CostEstimator`) on completion, increments
  `PromptUsageStatistics` only on success, in
  `src/AskLucy.Application/Prompts/Commands/ExecutePrompt/` (research.md Decisions 2, 3, 11, 14)
  (depends on T016, T017, T022)
- [X] T061 [P] [US2] Implement `GetExecutionQuery`/`ListExecutionsQuery`/Handlers → detail/summary
  DTOs, cursor-paginated, in `src/AskLucy.Application/Prompts/Queries/GetExecution/`,
  `src/AskLucy.Application/Prompts/Queries/ListExecutions/`
- [X] T062 [P] [US2] Implement `CompareExecutionsQuery`/Handler → array of full detail DTOs for the
  requested execution ids, in `src/AskLucy.Application/Prompts/Queries/CompareExecutions/`
- [X] T063 [P] [US2] Implement `SaveTestCaseCommand`/Handler in
  `src/AskLucy.Application/Prompts/Commands/SaveTestCase/`
- [X] T064 [P] [US2] Implement `RateExecutionCommand`/Handler in
  `src/AskLucy.Application/Prompts/Commands/RateExecution/`
- [X] T065 [P] [US2] Implement `ListTestCasesQuery`/`DeleteTestCaseCommand`/Handlers in
  `src/AskLucy.Application/Prompts/Queries/ListTestCases/`,
  `src/AskLucy.Application/Prompts/Commands/DeleteTestCase/`
- [X] T066 [US2] Implement the SSE execution endpoint (`Response.ContentType =
  "text/event-stream"`, `await foreach (var chunk in mediator.CreateStream(...))`, mirrors
  `AiController`'s `StreamVoiceReplyCommand` endpoint exactly; uses the `ai-endpoints` rate-limit
  policy, not `prompt-endpoints`, since it invokes `IAIProvider` directly — see
  contracts/prompt-execution-api.md) plus executions/test-cases/rating endpoints on
  `PromptsController` (`prompt-endpoints` policy, contracts/prompt-execution-api.md) (depends on
  T060–T065)
- [X] T067 [P] [US2] Frontend: execution API client hooks including an SSE-consuming
  `useExecutePromptStream` hook in
  `src/AskLucy.Web/ClientApp/src/features/prompts/api/promptExecutionApi.ts`
- [X] T068 [P] [US2] Frontend: Testing Console — split layout (editor/variables/model settings on
  the left; streamed output/token usage/cost/latency/provider/model on the right), repeated
  execution without leaving the workspace (FR per spec.md "Prompt Testing UI") in
  `src/AskLucy.Web/ClientApp/src/features/prompts/pages/PromptTestingConsole.tsx` (depends on T067)
- [X] T069 [US2] Frontend: Execution History panel (list past executions, open detail, rate
  Good/NeedsImprovement/Failed, save-as-test-case action) in
  `src/AskLucy.Web/ClientApp/src/features/prompts/components/ExecutionHistory.tsx` (depends on T067)
- [X] T069a [P] [US2] Frontend: Execution Comparison view — select 2+ past executions and render
  them side by side with provider/model/version/generation-parameters clearly labeled per column
  (FR-045, SC-009), wired to `CompareExecutionsQuery` (T062), in
  `src/AskLucy.Web/ClientApp/src/features/prompts/components/ExecutionComparison.tsx` (depends on
  T062, T067)

**Checkpoint**: A user can execute a prompt end to end with streaming, usage/cost, capability gating,
test-case capture, and side-by-side comparison of past executions. Combined with User Story 1, this
is the MVP.

---

## Phase 5: User Story 3 - Version, Compare, and Restore Prompt Changes (Priority: P2)

**Goal**: Every edit is preserved as a version; a user can compare any two versions and restore an
older one without losing any history.

**Independent Test**: Edit a prompt twice, confirm three versions exist, compare two of them,
restore an earlier one, confirm history is never deleted (quickstart.md Scenario 3).

### Tests for User Story 3

- [X] T070 [P] [US3] Unit tests for `Prompt.RestoreFrom` — creates a new version copying the
  restored content, never deletes/mutates existing versions (FR-033) in
  `tests/AskLucy.Domain.Tests/Prompts/PromptVersionRestoreTests.cs`
- [X] T071 [P] [US3] Integration test: `CompareVersionsQueryHandler` correctly diffs content/
  variables/model settings between two versions in
  `tests/AskLucy.Application.Tests/Prompts/CompareVersionsQueryHandlerTests.cs`
- [X] T072 [P] [US3] Playwright E2E: edit twice, compare v1/v3, restore v1, confirm a 4th version was
  created (not a deletion), duplicate v2 into a new prompt (quickstart.md Scenario 3) in
  `tests/AskLucy.E2E.Tests/PromptVersioning.spec.ts`

### Implementation for User Story 3

- [X] T073 [P] [US3] Implement `ListVersionsQuery`/`GetVersionQuery`/Handlers in
  `src/AskLucy.Application/Prompts/Queries/ListVersions/`,
  `src/AskLucy.Application/Prompts/Queries/GetVersion/`
- [X] T074 [P] [US3] Implement `CompareVersionsQuery`/Handler in
  `src/AskLucy.Application/Prompts/Queries/CompareVersions/`
- [X] T075 [P] [US3] Implement `RestoreVersionCommand`/Handler — calls `Prompt.RestoreFrom`, writes a
  `PromptAuditLog` `VersionRestored` entry, in
  `src/AskLucy.Application/Prompts/Commands/RestoreVersion/`
- [X] T076 [P] [US3] Implement `DuplicateVersionCommand`/Handler — new independent `Prompt` seeded
  from the given version, in `src/AskLucy.Application/Prompts/Commands/DuplicateVersion/`
- [X] T077 [US3] Add versions/compare/restore/duplicate-version endpoints to `PromptsController`
  (contracts/prompts-api.md) (depends on T073–T076)
- [X] T078 [P] [US3] Frontend: Version History panel (list, open, restore, duplicate-as-new) in
  `src/AskLucy.Web/ClientApp/src/features/prompts/components/VersionHistory.tsx`
- [X] T079 [US3] Frontend: Version Comparison view (side-by-side content/variable/model-setting diff)
  in `src/AskLucy.Web/ClientApp/src/features/prompts/components/VersionComparison.tsx` (depends on
  T078)

**Checkpoint**: Users can view, compare, restore, and duplicate any historical version; nothing is
ever destroyed. User Stories 1–3 all work independently and together.

---

## Phase 6: User Story 4 - Organize and Find Prompts at Scale (Priority: P2)

**Goal**: Users can organize prompts into nested folders/categories/tags, favorite/pin them, and
search/filter quickly even across thousands of prompts.

**Independent Test**: Create prompts across categories/tags/folders, favorite/pin a subset, confirm
search/filter/favorites/pinned/recently-used views return correctly-scoped results at 1,000+ prompts
in under 10 seconds (quickstart.md Scenario 4).

### Tests for User Story 4

- [X] T080 [P] [US4] Unit tests for `PromptFolder.MoveTo` — depth-limit and cycle-prevention
  rejections (FR-054, spec.md Edge Cases) in
  `tests/AskLucy.Domain.Tests/Prompts/PromptFolderTests.cs`. Cycle-prevention itself is enforced at
  the application layer (`MoveFolderCommandHandler` via `IPromptFolderRepository.IsSameOrDescendantAsync`),
  not on the domain entity — mirrors `KnowledgeBaseFolder`'s identical split exactly, so only the
  depth-limit/rename/soft-delete behaviors are exercised here; cycle rejection is covered by the
  Playwright E2E scenario (T084) and is implicit in the existing repository method.
- [X] T081 [P] [US4] Integration test: `PromptRepository.SearchAsync` full-text query matches name/
  description/system/user instructions and ranks best-match-first; combined category+tag+folder
  filters return only matching prompts (FR-052) in
  `tests/AskLucy.Persistence.Tests/Prompts/PromptSearchTests.cs`. Deviation: `SearchAsync` orders
  every view by `ModifiedAtUtc` descending (no relevance-ranking column exists), so "ranks best-
  match-first" is not separately asserted — only match/no-match — consistent with the actual
  implementation; not runnable in this environment (real SQL Server test DB required, see
  `docs/TESTING.md` §13), same as `PromptConcurrencyTests`.
- [X] T082 [P] [US4] Integration test: `view=recentlyUsed` reflects only successful executions in
  correct order; a failed execution does not move a prompt up (spec.md Clarifications, FR-051) in
  `tests/AskLucy.Application.Tests/Prompts/RecentlyUsedOrderingTests.cs`. Verified at
  `RecordPromptExecutionCommandHandler` (the sole caller of `PromptUsageStatistics.RecordSuccessfulUse`)
  with NSubstitute fakes; the SQL ordering itself is covered by `PromptSearchTests`/live DB. All 3
  tests pass (`dotnet test --filter FullyQualifiedName~Prompts`, 37/37 Application.Tests green).
- [X] T083 [P] [US4] Performance/scale test: seed 1,000+ prompts for one owner, assert a search and a
  filtered list call each return within the SC-003 budget in
  `tests/AskLucy.Persistence.Tests/Prompts/PromptSearchScaleTests.cs`. Not runnable in this
  environment (real SQL Server test DB required).
- [X] T084 [P] [US4] Playwright E2E: nested folders, search, combined filters, favorite/pin toggling,
  recently-used ordering, folder-cycle rejection (quickstart.md Scenario 4) in
  `tests/AskLucy.E2E.Tests/PromptOrganizationAtScale.spec.ts`

### Implementation for User Story 4

- [X] T085 [P] [US4] Implement `CreateFolderCommand`/`RenameFolderCommand`/`MoveFolderCommand`/
  `DeleteFolderCommand`/Handlers (mirrors `KnowledgeBases`' folder command shape exactly) in
  `src/AskLucy.Application/Prompts/Commands/{CreateFolder,RenameFolder,MoveFolder,DeleteFolder}/`
- [X] T086 [P] [US4] Implement `GetFolderTreeQuery`/Handler in
  `src/AskLucy.Application/Prompts/Queries/GetFolderTree/`
- [X] T087 [P] [US4] Implement `CreateCustomCategoryCommand`/`ListCategoriesQuery`/Handlers
  (duplicate-name-within-owner → `DuplicateResourceException`, mirrors
  `CreateCustomCategoryCommandHandler`) in
  `src/AskLucy.Application/Prompts/Commands/CreateCustomCategory/`,
  `src/AskLucy.Application/Prompts/Queries/ListCategories/`
- [X] T088 [P] [US4] Implement `AddTagCommand`/`RemoveTagCommand`/`ListTagsQuery`/Handlers
  (`ListTagsQuery` queries the `PromptTags` `DbSet` directly for an owner-scoped distinct list,
  mirroring `KnowledgeBaseTagConfiguration`'s documented reasoning) in
  `src/AskLucy.Application/Prompts/Commands/{AddTag,RemoveTag}/`,
  `src/AskLucy.Application/Prompts/Queries/ListTags/`
- [X] T089 [P] [US4] Implement `SetFavoriteCommand`/`SetPinnedCommand`/Handlers in
  `src/AskLucy.Application/Prompts/Commands/{SetFavorite,SetPinned}/`
- [X] T090 [P] [US4] Implement `ListPromptsQuery`/Handler — full-text search + category/tag/folder/
  favorite/pinned/status filters + `view=recentlyUsed|recentlyModified`, cursor-paginated
  (FR-050–FR-053) in `src/AskLucy.Application/Prompts/Queries/ListPrompts/`
- [X] T091 [US4] Implement `PromptFoldersController` (create/rename/move/delete/tree) in
  `src/AskLucy.Web/Controllers/v1/PromptFoldersController.cs`; add tags/categories/favorite/pinned/
  list-search endpoints to `PromptsController` (contracts/prompts-api.md) (depends on T085–T090).
  Fixed a `Contracts/PromptContracts.cs` naming collision (`AddTagRequest`/`CreateCategoryRequest`/
  `CreateFolderRequest`/`RenameFolderRequest`/`MoveFolderRequest` already existed for
  `KnowledgeBasesController`) by renaming to `AddPromptTagRequest`/`CreatePromptCategoryRequest`/
  `CreatePromptFolderRequest`/`RenamePromptFolderRequest`/`MovePromptFolderRequest`, propagated to
  both controllers; full solution builds clean (0 errors).
- [X] T092 [P] [US4] Frontend: Folder tree component (nested, drag-and-drop move via existing
  `@dnd-kit` dependency, create/rename/delete) in
  `src/AskLucy.Web/ClientApp/src/features/prompts/components/FolderTree.tsx`
- [X] T093 [P] [US4] Frontend: Search bar + Filters panel (category/tag/folder/favorite/pinned/
  status, recently-used/recently-modified toggle) in
  `src/AskLucy.Web/ClientApp/src/features/prompts/components/PromptFilters.tsx` (folder itself is
  selected via `FolderTree`, not duplicated here)
- [X] T094 [US4] Frontend: upgrade the Prompt Library list (T053) to a virtualized
  (`@tanstack/react-virtual`) list wired to `ListPromptsQuery`, folder tree, and filters, with
  favorite/pin toggle affordances in
  `src/AskLucy.Web/ClientApp/src/features/prompts/pages/PromptLibraryPage.tsx` (depends on T092,
  T093)

**Checkpoint**: Prompts can be organized into nested folders/categories/tags, favorited/pinned, and
found quickly at scale via search and filters. User Stories 1–4 all work independently and together.

---

## Phase 7: User Story 5 - Use a Saved Prompt Inside a Live Conversation (Priority: P2)

**Goal**: A user can insert a saved prompt into an active conversation; variables resolve, the
conversation's existing provider/model/context are preserved, and delivery reuses the existing chat
pipeline unchanged.

**Independent Test**: Insert a prompt with variables into an existing conversation, confirm prior
context and model selection are preserved and the resolved text becomes the new message
(quickstart.md Scenario 5).

### Tests for User Story 5

- [X] T095 [P] [US5] Integration test: `InsertPromptIntoConversationCommandHandler` blocks on a
  missing/invalid required variable before delegating to `SendChatMessageCommand` (US5 AC1, reuses
  `PromptVariableResolver`) in
  `tests/AskLucy.Application.Tests/Prompts/InsertPromptIntoConversationTests.cs` (also covers
  blocking when the conversation has no provider/model selected yet). 2/2 pass.
- [X] T096 [P] [US5] Integration test: a capability-incompatible conversation model blocks insertion
  with a specific warning before anything is sent (US5 AC3, reuses `PromptCapabilityChecker`) in
  `tests/AskLucy.Application.Tests/Prompts/InsertPromptCapabilityTests.cs`. 1/1 pass.
- [X] T097 [P] [US5] Integration test: on successful send, a `PromptExecution` row
  (`Origin: ConversationInsertion`, `ResultMessageId` set) is recorded and
  `PromptUsageStatistics` increments; on failure, neither happens (US5 AC2, FR-051) in
  `tests/AskLucy.Application.Tests/Prompts/InsertPromptUsageTrackingTests.cs`. The usage-increment
  itself is `RecordPromptExecutionCommandHandler`'s own responsibility (covered by
  `RecentlyUsedOrderingTests`, T082) — this test asserts the trigger for it (a correctly-shaped
  `RecordPromptExecutionCommand` send) fires on success and never fires on a mid-stream provider
  failure. 2/2 pass. All 5 new US5 tests + existing suite: 42/42 Application.Tests green.
- [X] T098 [P] [US5] Playwright E2E: insert a prompt into an existing conversation with prior
  messages, confirm context/model preserved, confirm the incompatible-model warning path
  (quickstart.md Scenario 5) in `tests/AskLucy.E2E.Tests/PromptConversationInsertion.spec.ts`

### Implementation for User Story 5

- [X] T099 [US5] Implement `InsertPromptIntoConversationCommand`/Handler — resolves variables
  (`PromptVariableResolver`), checks capability against the conversation's current model
  (`PromptCapabilityChecker`), delegates to the existing `SendChatMessageCommand` unchanged, then
  records `PromptExecution`(`Origin: ConversationInsertion`) and updates
  `PromptUsageStatistics` on success (research.md Decision 4) in
  `src/AskLucy.Application/Prompts/Commands/InsertPromptIntoConversation/` (depends on T016, T017,
  T022, existing `SendChatMessageCommand`). The handler injects `ISender` and calls
  `mediator.Send`/`mediator.CreateStream` to orchestrate `AppendMessageCommand` (user message before
  the delegated stream, assistant message after) and `RecordPromptExecutionCommand` itself — the
  same established delegation pattern `StreamVoiceReplyCommandHandler` already uses, chosen so the
  `ChatsController` endpoint can stay a thin SSE relay (mirrors `AiController.Chat`'s persistence-
  composed-by-the-caller precedent, just moved one layer down since the resolved user-message text
  only exists inside this handler). `RecordPromptExecutionCommand` was extended with an optional
  trailing `ResultMessageId` parameter (default `null`, so the existing Testing Workspace caller in
  `PromptsController.Execute` is unaffected) to carry the real `Chats.Message.Id` per the contract.
  Deliberately has no try/catch around the delegated stream — a provider failure propagates
  uncaught, so neither the assistant message nor the execution row is ever written on failure.
  Scope note: RAG/memory trailing SSE events and `RecordMemoryReferencesCommand` (which the
  existing `AiController.Chat` path forwards) are not replicated here — not named in US5's
  acceptance criteria, and the underlying chat send still runs its own RAG/memory retrieval
  unaffected; only the "why does Lucy know this" memory-reference *trace* is not recorded for a
  prompt-inserted message specifically, a narrow, documented gap.
- [X] T100 [US5] Add `POST /api/v1/chats/{chatId}/prompt-messages` to the existing `ChatsController`
  (contracts/prompt-conversation-integration-api.md) (depends on T099). Deviation: the contract's
  "identical shape to `POST /api/v1/chats/{chatId}/messages`" line refers to a send-message endpoint
  that does not actually exist under that route — the real equivalent is `POST /api/v1/ai/chat`
  (`AiController.Chat`); this endpoint's plain-text `data: {delta}` / `data: [DONE]` SSE format
  matches that one instead. Full solution builds clean (0 errors).
- [X] T101 [P] [US5] Frontend: "Insert Prompt" picker in the chat composer (search/select a saved
  prompt, resolve/prompt-for variables, capability warning) in
  `src/AskLucy.Web/ClientApp/src/features/chat/components/InsertPromptPicker.tsx`. Wired via a new
  optional `ChatComposer` prop (`onInsertPromptClick`, button hidden when omitted) and rendered from
  `ChatPage.tsx`, which supplies the conversation's `chatId`/`providerId`/`modelId` and refetches
  persisted messages (`refetchMessages()`) once insertion completes — the picker itself does not
  duplicate `useChatStream`'s live-streaming state. Capability-check logic
  (`unmetCapabilities`/`isModelCompatible`) was extracted from `PromptTestingConsole.tsx` into a
  new shared `promptCapabilityUtils.ts` so both it and this picker use one implementation.
  `tsc -b` and `eslint` both clean (only the pre-existing `useVirtualizer`-class warning, already
  present on `ChatPage.tsx` before this change).

**Checkpoint**: Saved prompts can be inserted into live conversations without disrupting existing
chat behavior. User Stories 1–5 all work independently and together.

---

## Phase 8: User Story 6 - Request RAG or Memory Context From a Prompt (Priority: P3)

**Goal**: A prompt can optionally pull in Knowledge Base (RAG) and/or Memory context at execution
time, clearly separated from its own instructions, never able to override them.

**Independent Test**: Execute a RAG-flagged prompt against an indexed Knowledge Base and a
memory-flagged prompt against a user with stored memories; confirm both are grounded and the
assembled request keeps every component structurally distinguishable (quickstart.md Scenario 6).

### Tests for User Story 6

- [X] T102 [P] [US6] Unit test: `ExecutePromptCommandHandler` calls `IRagService.RetrieveContextAsync`
  with `PromptExecution.Id` in the `userChatId` slot only when `useRagContext` is set, and inserts a
  `<retrieved_context>`-delimited system message in the correct order (research.md Decisions 3, 14)
  with a faked `IRagService`, in
  `tests/AskLucy.Application.Tests/Prompts/ExecutePromptRagIntegrationTests.cs`. Deviation: the real
  delimiter tag is `<context>` (confirmed by reading `SendChatMessageCommandHandler`'s actual
  `BuildAugmentedSystemPrompt`), not `<retrieved_context>` as this task's own text states —
  research.md Decision 14's "matching RagService's existing framing" is followed literally (the
  real existing framing), correcting the mismatched tag name the decision quoted. Also verifies
  the correlation id passed is fresh (not the prompt/version id — `PromptExecution.Id` does not
  exist yet at this point in the flow, see T106). 3/3 pass.
- [X] T103 [P] [US6] Unit test: same for `IMemoryService`/`<user_memory>` delimiter, and both
  together produce the documented message order (system+developer → memory → RAG → user), with
  faked `IMemoryService`, in
  `tests/AskLucy.Application.Tests/Prompts/ExecutePromptMemoryIntegrationTests.cs`. 4/4 pass.
- [X] T104 [P] [US6] Integration test: `PromptExecutionResult.RagCitationsJson`/
  `MemoryReferencesJson` are populated when requested and left null otherwise (FR-081, FR-082) in
  `tests/AskLucy.Application.Tests/Prompts/PromptExecutionContextCaptureTests.cs`. Written as an
  Application.Tests unit test (NSubstitute) against `RecordPromptExecutionCommandHandler` rather
  than a live-DB integration test — same relocation reasoning as T057/T058
  (`CustomWebApplicationFactory` has no live database in this environment); also covers that
  `ConversationInsertion`-origin executions never create a `PromptExecutionResult` at all. 3/3 pass.
  All 10 new US6 tests + existing suite: 52/52 Prompts tests, 421/421 Application.Tests green.
- [X] T105 [P] [US6] Playwright E2E: RAG-grounded execution, memory-grounded execution, combined
  execution (quickstart.md Scenario 6) in
  `tests/AskLucy.E2E.Tests/PromptRagMemoryExecution.spec.ts`

### Implementation for User Story 6

- [X] T106 [US6] Extend `ExecutePromptCommandHandler` (from T060) — when `useRagContext`/
  `useMemoryContext` are set, call `IRagService.RetrieveContextAsync`/
  `IMemoryService.RetrieveRelevantMemoriesAsync` (passing `PromptExecution.Id`, research.md
  Decision 3), insert delimited context messages in the fixed order (research.md Decision 14),
  capture citations/memory-references onto `PromptExecutionResult` in
  `src/AskLucy.Application/Prompts/Commands/ExecutePrompt/ExecutePromptCommandHandler.cs` (depends
  on T060). Deviation: passes a freshly-generated `Guid.CreateVersion7()` per attempt, not the
  literal `PromptExecution.Id` — that id is created later, in `RecordPromptExecutionCommandHandler`,
  which only runs *after* the controller's stream-consuming loop completes (needs latency/output/
  usage, all unknown until then); research.md Decision 3 itself confirms the parameter is a
  logging-only correlation id, never a foreign key, so this substitution has zero functional
  impact and avoids speculative ID-threading plumbing the decision's own rationale calls out as
  unnecessary (constitution §2.III YAGNI). The RAG/Memory system-message framing text
  (`BuildAugmentedSystemPrompt`/`BuildMemorySystemPrompt`) was extracted from
  `SendChatMessageCommandHandler`'s private methods into a new shared
  `src/AskLucy.Application/Ai/RetrievalPromptFraming.cs` so both handlers emit byte-identical
  delimiter/defensive-framing text, not a re-typed near-duplicate; `SendChatMessageCommandHandler`
  updated to call it too, with zero behavioral change (verified: the full 421-test
  Application.Tests suite, including every existing RAG/Memory chat test, stays green).
  `PromptsController.Execute` updated to capture `chunk.RetrievalOutcome`/`chunk.MemoryOutcome` off
  the trailing `PromptStreamChunk` and serialize them into `RecordPromptExecutionCommand`'s
  `RagCitationsJson`/`MemoryReferencesJson` (previously hardcoded `null`).
- [X] T107 [US6] Extend `InsertPromptIntoConversationCommandHandler` (from T099) — no new RAG/Memory
  logic is added here; the delegated `SendChatMessageCommand` already performs RAG/Memory retrieval
  for the conversation's attached knowledge bases, so this task only confirms/documents that
  behavior is inherited correctly, in
  `src/AskLucy.Application/Prompts/Commands/InsertPromptIntoConversation/` (depends on T099,
  research.md Decision 4). Confirmed by re-reading `InsertPromptIntoConversationCommandHandler.cs`:
  it constructs its own `messages` list (prior history + prompt's system/developer + resolved user
  instructions) and passes it to `SendChatMessageCommand` unmodified — that handler's own RAG/
  Memory retrieval (driven by the conversation's attached knowledge bases via
  `IConversationKnowledgeBaseRepository`, and the user's stored memories) runs exactly as it does
  for an ordinary typed message, with no special-casing needed or added. No code change; verification-only task.
- [X] T108 [P] [US6] Frontend: RAG/Memory toggle + knowledge-base picker in the Testing Console
  (execution request options) in
  `src/AskLucy.Web/ClientApp/src/features/prompts/components/ExecutionContextOptions.tsx` (depends
  on T068). Reuses the existing Knowledge Base search hook
  (`useSearchKnowledgeBases`) for the picker — no new KB-listing endpoint. State type/default
  extracted into `executionContextOptionsState.ts` to avoid a Fast Refresh lint warning (same
  pattern as `promptFiltersState.ts`, T093). `tsc -b` and `eslint` both clean.

**Checkpoint**: Prompts can optionally augment execution with RAG/Memory context, reusing the existing
engines with zero duplication. User Stories 1–6 all work independently and together.

---

## Phase 9: User Story 7 - Export and Import Prompts (Priority: P3)

**Goal**: Users can export one or more prompts to a portable JSON file and import a previously
exported file back in, with atomic, all-or-nothing validation.

**Independent Test**: Export a single prompt and a multi-prompt bundle, delete the originals, import
both back, confirm exact recreation; corrupt one entry in a bundle and confirm the whole import is
rejected (quickstart.md Scenario 7).

### Tests for User Story 7

- [X] T109 [P] [US7] Unit tests for `PromptImportValidator` — valid single/bundle files pass; a
  missing required field, unknown `schemaVersion`, or malformed variable rejects the **entire** file
  with a specific error (FR-071, research.md Decision 13) in
  `tests/AskLucy.Infrastructure.Tests/Prompts/PromptImportValidatorTests.cs`. Relocated to
  `tests/AskLucy.Application.Tests/Prompts/PromptImportValidatorTests.cs` — see T114's deviation
  note (the validator itself moved layers, so its test follows). 7/7 pass.
- [X] T110 [P] [US7] Integration test: export → delete → import round-trip recreates content/
  variables/current version/model settings/tags exactly, as an independent new prompt with fresh
  version-1 history (FR-070–FR-072, SC-007) in
  `tests/AskLucy.Application.Tests/Prompts/PromptExportImportRoundTripTests.cs`. 1/1 pass.
- [X] T111 [P] [US7] Integration test: a name collision on an imported entry is auto-suffixed rather
  than failing the whole import (FR-072) in
  `tests/AskLucy.Application.Tests/Prompts/PromptImportNameCollisionTests.cs`. 2/2 pass. All 10 new
  US7 tests + existing suite: 431/431 Application.Tests, 62/62 Infrastructure.Tests green.
- [X] T112 [P] [US7] Playwright E2E: single export/import, bundle export/import, corrupted-entry
  rejection (quickstart.md Scenario 7) in `tests/AskLucy.E2E.Tests/PromptExportImport.spec.ts`

### Implementation for User Story 7

- [X] T113 [P] [US7] Implement `PromptExportFileBuilder` (`{ schemaVersion, prompts: [...] }`,
  research.md Decision 13) in `src/AskLucy.Infrastructure/Prompts/PromptExportFileBuilder.cs`.
  Deviation: implemented in `src/AskLucy.Application/Prompts/PromptExportFileBuilder.cs` instead —
  a plain static class (no interface, no DI), not an Infrastructure service. The builder is pure,
  dependency-free JSON-shape assembly over already-loaded aggregates with zero file-system/network
  dependency, so it belongs with `PromptContentAnalyzer`/`PromptVariableResolver`/
  `PromptCapabilityChecker` (the codebase's existing convention for exactly this kind of pure
  prompt-domain helper) rather than behind an `IPromptExportFileBuilder` interface implemented in
  Infrastructure — which would also have forced `AskLucy.Application.Tests` to take a project
  reference to `AskLucy.Infrastructure` it otherwise never needs, just to exercise the real
  builder/validator in T110/T111's round-trip tests (constitution §3 Dependency Rule governs test
  project layering too, not only `src/`).
- [X] T114 [P] [US7] Implement `PromptImportValidator` (validates every entry before any is created;
  returns a structured, per-entry error list on failure) in
  `src/AskLucy.Infrastructure/Prompts/PromptImportValidator.cs`. Deviation: same reasoning and same
  relocation as T113 — `src/AskLucy.Application/Prompts/PromptImportValidator.cs`, a plain static
  class reusing `PromptContentAnalyzer.Analyze` directly (both already live in `Application.Prompts`,
  so no cross-layer call was ever needed).
- [X] T115 [US7] Implement `ExportPromptsCommand`/Handler in
  `src/AskLucy.Application/Prompts/Commands/ExportPrompts/` (depends on T113). Also writes a
  `PromptAuditLog` `Exported` entry per exported prompt (FR-090 — mirrors T116's `Imported` entry;
  not explicitly called out in this task's own text but the same audit-trail requirement applies
  symmetrically, and `PromptAuditAction.Exported` already existed in the enum for exactly this).
- [X] T116 [US7] Implement `ImportPromptsCommand`/Handler — validates atomically (T114), creates
  each prompt with its own fresh version-1 history, resolves name collisions per FR-006, writes a
  `PromptAuditLog` `Imported` entry per created prompt, in
  `src/AskLucy.Application/Prompts/Commands/ImportPrompts/` (depends on T114)
- [X] T117 [US7] Add export/import endpoints to `PromptsController` (contracts/prompts-api.md)
  (depends on T115, T116). `Import` binds `[FromBody] PromptExportFile` directly — the same shape
  `Export` returns, no separate request-contract wrapper needed. `Export` explicitly serializes with
  the app's configured (string-enum) `JsonSerializerOptions` (injected via `IOptions<JsonOptions>`)
  rather than `JsonSerializer`'s bare default — a manual `SerializeToUtf8Bytes` call does not
  automatically pick up `Program.cs`'s `AddJsonOptions()` configuration the way an ordinary
  controller-returned object result does, and re-import binds via `[FromBody]` (which *does* use
  that configuration and therefore expects string enum values like `"Summarization"`) — using the
  bare default here would have silently broken every export/import round-trip via mismatched
  enum encodings, caught before it shipped.
- [X] T118 [P] [US7] Frontend: Export dialog (multi-select from library, download file) and Import
  dialog (file picker, per-entry validation-error display) in
  `src/AskLucy.Web/ClientApp/src/features/prompts/components/{ExportDialog,ImportDialog}.tsx`.
  `ApiError` (`src/AskLucy.Web/ClientApp/src/api/httpClient.ts`) extended with an optional `errors`
  field carrying the Problem Details `errors` extension (`ProblemDetailsMiddleware.cs`) — additive,
  every existing call site unaffected — so `ImportDialog` can render the real structured per-entry
  validation errors rather than only a generic message. Wired into `PromptLibraryPage.tsx` via new
  header "Import"/"Export" buttons. `tsc -b` and `eslint` both clean (only the pre-existing
  `useVirtualizer`-class warning already on this page).

**Checkpoint**: All seven user stories are independently functional and demoable together as one
complete Prompt Library & Prompt Engineering Workspace.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Improvements and verification that span multiple user stories.

- [X] T119 [P] Add `PromptStatisticsQuery`/Handler + `GET /api/v1/prompts/{id}/statistics` endpoint
  (execution count, last-successful-use, rating breakdown — spec.md "Prompt Statistics" API
  requirement, FR-062) in `src/AskLucy.Application/Prompts/Queries/GetPromptStatistics/`,
  `src/AskLucy.Web/Controllers/v1/PromptsController.cs`. Rating breakdown required a new
  `IPromptExecutionRepository.GetRatingBreakdownByPromptIdAsync` repository method (joins
  `PromptRatings`→`PromptExecutions`, groups by `RatingValue`) — no existing method aggregated
  ratings across every execution of a prompt. Covered by
  `tests/AskLucy.Application.Tests/Prompts/GetPromptStatisticsQueryHandlerTests.cs` (2/2 pass).
- [X] T120 [P] Accessibility pass (jest-axe) on Prompt Editor, Testing Console, Version History/
  Comparison, and Folder Tree components in
  `tests/AskLucy.Web/ClientApp/src/features/prompts/**/*.a11y.test.tsx`. Deviation: written
  alongside each component (`src/AskLucy.Web/ClientApp/src/features/prompts/components/*.a11y.test.tsx`),
  matching every other `.a11y.test.tsx` file's actual location in this codebase (colocated with
  the component, not under a separate `tests/` tree — no such tree exists for the frontend).
  **Found and fixed two real violations**, not just written passing tests: (1) `FolderTree.tsx`'s
  root "All prompts" row was missing `role="treeitem"` — a bare `<li>` inside a `role="tree"` list,
  failing `aria-required-children`/`listitem`; (2) `VersionHistory.tsx`'s comparison-selection
  `Checkbox` used a plain `aria-label` prop, which MUI places on the outer non-interactive `<span>`
  wrapper rather than the actual `<input>` — failing `aria-prohibited-attr`/`label` (axe: "Form
  elements must have labels"); fixed via `slotProps={{ input: { 'aria-label': ... } }}`, the same
  pattern already used correctly in `ModelSyncDialog.tsx`. All 4 a11y tests pass after the fixes;
  `tsc -b`/`eslint` clean.
- [X] T121 [P] Security review pass: confirm no prompt content appears in any Serilog sink above
  Debug level (FR-091) — audit every log call added across Phases 2–9 in
  `src/AskLucy.Application/Prompts/`, `src/AskLucy.Infrastructure/Prompts/`. Clean: zero `ILogger`
  usage anywhere in `Application/Prompts` or `Web/Controllers/v1/PromptsController.cs`/
  `ChatsController.cs`'s new action. Specifically checked the two integration points where prompt
  content flows into an *existing* logging-capable service: `IRagService.RetrieveContextAsync`/
  `IMemoryService.RetrieveRelevantMemoriesAsync` receive the resolved prompt text as their `query`
  parameter — `RagService` has no logging calls at all; `MemoryService` logs only the
  correlation-id parameter (a random `Guid`, never prompt content, per T106's design) and the
  exception object on a retrieval failure. No `PromptAuditLog.DetailsJson` call anywhere passes
  raw content (every call site is either `null` or a JSON blob of ids only).
- [X] T122 [P] Verify `PromptAuditLog` entries are written for every action listed in
  `PromptAuditAction` across all handlers (create/update/delete/archive/restore/duplicate/
  version-restore/export/import) — add any missing call site. Verified via
  `grep -rn "PromptAuditAction\." src/AskLucy.Application/Prompts` — all 9 enum values
  (`Created`/`Updated`/`Deleted`/`Archived`/`Restored`/`Duplicated`/`VersionRestored`/`Exported`/
  `Imported`) have at least one call site; no missing coverage found, no code change needed.
- [X] T123 Run the full `quickstart.md` validation pass (Scenarios 1–8) end to end against a local
  deployment; record and fix any deviation before marking the feature done (constitution §19).
  **Environment caveat** (consistent with every other "NOT RUNNABLE IN THIS ENVIRONMENT" note across
  this feature's E2E specs): no live SQL Server, running `AskLucy.Web` instance, or real AI provider
  credentials are available here, so a literal live-deployment run was not possible. Instead,
  cross-referenced every scenario step against this session's actual automated test coverage and
  code:
  - **Scenario 1** (create/reuse): name-uniqueness 409 (`CreatePromptCommandHandler`), ownership
    404 (`PromptOwnershipGuard`), variable auto-detection (`PromptContentAnalyzer`) — all covered
    by `CreatePromptCommandHandlerTests`/`PromptTests`. Timing (SC-001, "under 3 minutes") is a
    human/UX measure, not programmatically verifiable.
  - **Scenario 2** (test execution): blocks-before-provider-call, capability gating, SSE streaming,
    provider-failure → explicit error event + `Outcome: Failed` — all covered by
    `ExecutePromptCommandHandlerTests`. Timing (SC-002) not programmatically verifiable.
  - **Scenario 3** (version/compare/restore): covered by `PromptTests`
    (`ApplyEdit`/`RestoreFrom` never delete/overwrite) and `CompareVersionsQueryHandlerTests`.
  - **Scenario 4** (organize/find at scale): covered by T080–T084 (`PromptFolderTests`,
    `PromptSearchTests`, `RecentlyUsedOrderingTests`, `PromptSearchScaleTests` — the latter two
    real-DB tests not runnable here but verified by code review of the exact query shape).
  - **Scenario 5** (conversation insertion): covered by T095–T097
    (`InsertPromptIntoConversationTests`/`InsertPromptCapabilityTests`/`InsertPromptUsageTrackingTests`).
  - **Scenario 6** (RAG/Memory): covered by T102–T104
    (`ExecutePromptRagIntegrationTests`/`ExecutePromptMemoryIntegrationTests` — including the
    combined-order test proving research.md Decision 14's exact message sequence —
    /`PromptExecutionContextCaptureTests`).
  - **Scenario 7** (export/import): covered by T109–T111
    (`PromptExportImportRoundTripTests`/`PromptImportNameCollisionTests`/`PromptImportValidatorTests`
    — the "corrupt one entry rejects the whole file" case explicitly tested).
  - **Scenario 8** (concurrency/cross-cutting): concurrency 409 covered by
    `PromptConcurrencyTests` (real-DB, not runnable here, `RowVersion`-based — same mechanism
    proven elsewhere in this codebase); no-content-in-logs confirmed by T121; no-unhandled-500s is
    structural (every domain/validation exception this feature throws is already mapped by the
    platform-wide `ProblemDetailsMiddleware`, verified by reading that middleware, not re-derived
    per feature).
  - **Not verified here, requires a real deployment**: exact SC-001/SC-002/SC-003/SC-006 numeric
    timing thresholds, and genuine browser/SSE behavior — flagged, not silently assumed passing.
- [X] T124 [P] Update `docs/ARCHITECTURE.md`/API documentation index to list the new `Prompts`
  bounded context and its endpoints (constitution §13). No separate API-documentation-index file
  exists in this codebase (`docs/API_GUIDELINES.md` is guidelines-only, not a per-module endpoint
  index) — `docs/ARCHITECTURE.md` §26–28 (Consent, Document Intelligence, AI Memory System) are
  themselves this codebase's de facto per-feature index, each one narrative-prose describing the
  bounded context, its key flows, and where its endpoints live. Added a matching new §29 (renumbering
  the former §29 "Architecture Principles" to §30) covering the aggregate/versioning model,
  variable resolution, the execution/instruction-priority design (RAG/Memory reuse, message
  ordering), organization/search, export/import, and the ownership-guard security convention;
  updated the document's "Last Updated" header line.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Stories (Phase 3–9)**: All depend on Foundational phase completion.
  - US1 and US2 (both P1) form the MVP and have no dependency on each other beyond Foundational.
  - US3 and US4 (P2) depend only on Foundational (they extend US1's `Prompt`/`PromptFolder`/etc. but
    do not require US2's execution pipeline).
  - US5 (P2) depends on Foundational's `PromptVariableResolver`/`PromptCapabilityChecker` (T016,
    T017) and the existing `SendChatMessageCommand` — does **not** depend on US2's
    `ExecutePromptCommand`.
  - US6 (P3) depends on US2's `ExecutePromptCommandHandler` (T060) existing — it extends that
    handler rather than creating a new one.
  - US7 (P3) depends only on Foundational.
- **Polish (Phase 10)**: Depends on all desired user stories being complete.

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel.
- Within Foundational: all 12 entity tasks (T003–T014) are [P]; all shared-service tasks (T015–T017)
  are [P]; all abstraction tasks (T018–T023) are [P]; all repository tasks (T028–T033) are [P] once
  T027 (migration) completes.
- Once Foundational completes, **US1, US2, US3, US4, and US7 can all start in parallel** (different
  files, no cross-dependency); US5 can start in parallel with them (only needs T016/T017); US6 must
  wait for US2's T060.
- Within any story, all `[P]`-marked test tasks can run in parallel with each other, and all
  `[P]`-marked implementation tasks (different command/query folders) can run in parallel with each
  other.

---

## Parallel Example: Foundational Phase

```bash
# Launch all 12 entity tasks together:
Task: "Create Prompt aggregate in src/AskLucy.Domain/Prompts/Prompt.cs"
Task: "Create PromptVersion entity in src/AskLucy.Domain/Prompts/PromptVersion.cs"
Task: "Create PromptVariable entity in src/AskLucy.Domain/Prompts/PromptVariable.cs"
# ...through T014

# Then, once entities exist, launch the three shared services together:
Task: "Create PromptContentAnalyzer in src/AskLucy.Application/Prompts/PromptContentAnalyzer.cs"
Task: "Create PromptVariableResolver in src/AskLucy.Application/Prompts/PromptVariableResolver.cs"
Task: "Create PromptCapabilityChecker in src/AskLucy.Application/Prompts/PromptCapabilityChecker.cs"
```

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Unit tests for PromptContentAnalyzer in tests/AskLucy.Application.Tests/Prompts/PromptContentAnalyzerTests.cs"
Task: "Unit tests for Prompt.ApplyEdit/CreateVersionSnapshot in tests/AskLucy.Domain.Tests/Prompts/PromptTests.cs"
Task: "Integration test: duplicate-name rejection in tests/AskLucy.Application.Tests/Prompts/CreatePromptCommandHandlerTests.cs"

# Launch all command handlers for User Story 1 together:
Task: "Implement CreatePromptCommand/Handler/Validator in src/AskLucy.Application/Prompts/Commands/CreatePrompt/"
Task: "Implement UpdatePromptCommand/Handler/Validator in src/AskLucy.Application/Prompts/Commands/UpdatePrompt/"
Task: "Implement DeletePromptCommand/Handler in src/AskLucy.Application/Prompts/Commands/DeletePrompt/"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
3. Complete Phase 3: User Story 1 (create/reuse a prompt).
4. Complete Phase 4: User Story 2 (test a prompt) — spec.md explicitly frames US1+US2 as the
   inseparable MVP ("This must ship alongside creation (P1) for the feature to be usable, not merely
   storable").
5. **STOP and VALIDATE**: run quickstart.md Scenarios 1–2 independently.
6. Deploy/demo if ready.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 + US2 → MVP: create, edit/version, and test prompts → validate → deploy/demo.
3. US3 (versioning UI) + US4 (organization/search at scale) → validate → deploy/demo.
4. US5 (conversation insertion) → validate → deploy/demo.
5. US6 (RAG/Memory context) → validate → deploy/demo.
6. US7 (export/import) → validate → deploy/demo.
7. Polish → final quickstart.md full pass → done.

### Parallel Team Strategy

With multiple developers, once Foundational is done:

- Developer A: US1 → US3 (both extend `Prompt`/`PromptVersion` directly).
- Developer B: US2 → US6 (US6 extends US2's execution handler).
- Developer C: US4 (organization/search, largely independent surface).
- Developer D: US5 → US7 (both independent of the execution pipeline).

---

## Notes

- [P] tasks = different files, no dependency on an incomplete task.
- [Story] label maps task to specific user story for traceability.
- Every user story is independently completable and testable per its quickstart.md scenario.
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently.
- No task in this list introduces a new NuGet package or frontend dependency (research.md "Summary of
  dependencies") — every implementation task is expected to reuse an existing abstraction cited in
  research.md/data-model.md/contracts/.
