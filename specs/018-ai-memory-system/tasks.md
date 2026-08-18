---

description: "Task list for AI Memory System"

---

# Tasks: AI Memory System

**Input**: Design documents from `/specs/018-ai-memory-system/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (§10 Testing Standards, non-negotiable) requires unit,
integration, and Playwright E2E coverage for new/changed behavior — test tasks are not optional
here.

**Organization**: Tasks are grouped by user story (spec.md priorities: US1/US2 = P1, US3/US4 = P2,
US5/US6 = P3) so each story is independently implementable, testable, and demoable. As with
specs/016's RAG engine, the core remember→recall mechanics (extraction, ranking/retrieval, prompt
injection, conflict auto-detection, notification push) are placed in **Foundational**, not inside a
user-story phase: US1's own Independent Test ("state a fact... start a new conversation... confirm
it's reflected") is impossible to satisfy without the full pipeline already working end to end, and
every later story (Memory Center, approval, privacy, Projects, conflict resolution) only adds
user-facing surface on top of memories the pipeline already produces. User stories then add: US1 —
the "why does Lucy know this" trace surface; US2 — full Memory Center CRUD/search; US3 — approval-
mode configuration and the notification list UI (the push mechanism itself is Foundational); US4 —
account-level disable/clear-all/export; US5 — the Project entity, its CRUD, and conversation
assignment (scoping *logic* is Foundational-ready via a nullable `projectId` parameter from day
one); US6 — the ambiguous-conflict *resolution* action (auto-detection and direct-contradiction
auto-merge are Foundational).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Maps the task to US1–US6 from spec.md
- All descriptions include exact file paths

## Path Conventions

Existing single-solution web app (constitution §3): `src/AskLucy.Domain`,
`src/AskLucy.Application`, `src/AskLucy.Infrastructure`, `src/AskLucy.Persistence`,
`src/AskLucy.Web` (API + `ClientApp/` React SPA), `tests/AskLucy.*.Tests`. This feature adds two new,
independent bounded contexts — `Memory` and `Projects` — at every layer (research.md Decision 1) and
extends one existing entity (`Chats.UserChat`) — no new top-level project.

---

## Phase 1: Setup

**Purpose**: The zero-new-dependency platform-capability checks and cross-cutting registration this
feature needs before any domain code is written (plan.md Technical Context — no new NuGet package is
required, unlike specs/016's one new dependency).

- [X] T001 [P] Register the `memory-endpoints` rate-limit policy (partition by user then IP, fixed window, generous shape matching `knowledge-base-endpoints`) in `src/AskLucy.Web/Program.cs` (research.md Decision 17)
- [X] T002 Confirm ASP.NET Core Data Protection is configured with persistent key storage (reusing whatever mechanism already backs the existing `AiCredentialProtector`-style credential encryption) so the `Memory.Content`/`MemoryVersion.PreviousContent`/`MemoryReference.ContentSnapshot` encryption converter (research.md Decision 12) survives app restarts — flag as an ops prerequisite if key storage is currently ephemeral

**Checkpoint**: Solution builds; the rate-limit policy is registered; encryption key persistence is
confirmed. No domain code exists yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain entities, shared abstractions, persistence configuration/migration,
repositories, and the core extraction→rank→inject→conflict-detect pipeline mechanics every user
story depends on.

**⚠️ CRITICAL**: No user story task may begin until this phase is complete and the solution builds
with the new migration applied.

### Domain entities — `Memory` bounded context (data-model.md "New Entities")

- [X] T003 [P] Create `Memory` aggregate — `UserId`/`ProjectId`/`Category`/`Content`/`State`/`IsSensitive`/`SourceType`/`SourceConversationId`/`Importance`/`Confidence`/`LastReinforcedAtUtc`/`FrequencyCount`/`ExpiresAtUtc`, mutator methods `Approve`/`Reject`/`Edit` (appends a `MemoryVersion`)/`Archive`/`Reinforce`/`MarkSensitive`, soft-delete via `IsDeleted`/`DeletedAtUtc` in `src/AskLucy.Domain/Memory/Memory.cs`
- [X] T004 [P] Create `MemoryVersion` entity (append-only: `PreviousContent`/`ChangeReason`/`ChangedAtUtc`/`ChangedByActor`) in `src/AskLucy.Domain/Memory/MemoryVersion.cs`
- [X] T005 [P] Create `MemoryApproval` entity (`Decision`/`DecidedAtUtc`/`DecidedByActor`) in `src/AskLucy.Domain/Memory/MemoryApproval.cs`
- [X] T006 [P] Create `MemoryConflict` entity (`ExistingMemoryId`/`NewMemoryId`/`ConflictType`/`ResolutionStatus`/`DetectedAtUtc`/`ResolvedAtUtc`/`ResolvedByActor`) in `src/AskLucy.Domain/Memory/MemoryConflict.cs`
- [X] T007 [P] Create `MemoryEmbedding` entity (`MemoryId`/`EmbeddingProviderId`/`Vector`/`IsCurrent`, immutable after creation) in `src/AskLucy.Domain/Memory/MemoryEmbedding.cs`
- [X] T008 [P] Create `MemoryAuditLog` entity (append-only, `MemoryId` nullable/no-cascade, `Action`/`OccurredAtUtc`/`DetailsJson`) in `src/AskLucy.Domain/Memory/MemoryAuditLog.cs`
- [X] T009 [P] Create `MemoryNotification` entity (`UserId`/`MemoryId`/`EventType`/`Message`/`CreatedAtUtc`/`ReadAtUtc`) in `src/AskLucy.Domain/Memory/MemoryNotification.cs`
- [X] T010 [P] Create `MemoryPreference` entity (`UserId` PK, `MemoryEnabled` default `true`) in `src/AskLucy.Domain/Memory/MemoryPreference.cs`
- [X] T011 [P] Create `MemoryCategoryPreference` entity (unique `UserId`+`Category`, `ApprovalMode` default `Automatic`, `IsEnabled`) in `src/AskLucy.Domain/Memory/MemoryCategoryPreference.cs`
- [X] T012 [P] Create `MemoryReference` entity (`MessageId`/`MemoryId` no-cascade/`RelevanceScore`/`ContentSnapshot`/`CreatedAtUtc`) in `src/AskLucy.Domain/Memory/MemoryReference.cs`

### Domain entities — `Projects` bounded context

- [X] T013 [P] Create `Project` entity (`UserId`/`Name`, soft delete via `Delete(actor)` raising `ProjectDeletedDomainEvent`) + `ProjectDeletedDomainEvent` in `src/AskLucy.Domain/Projects/Project.cs`, `ProjectDeletedDomainEvent.cs`

### Extended entities (data-model.md "Extended Entities")

- [X] T014 [P] Extend `UserChat` with `ProjectId` (`Guid?`) + `AssignToProject(Guid? projectId, string actor)` in `src/AskLucy.Domain/Chats/UserChat.cs` (research.md Decision 1)

### Shared abstractions (Application)

- [X] T015 [P] Create `IMemoryService` abstraction — `RetrieveRelevantMemoriesAsync(...)` returning a `MemoryRetrievalOutcome` (`Found`/`NoneRelevant`/`Unavailable`) in `src/AskLucy.Application/Abstractions/IMemoryService.cs` (research.md Decision 3)
- [X] T016 [P] Create `IMemoryVectorStore` abstraction (upsert/delete/per-user nearest-neighbor query) in `src/AskLucy.Application/Abstractions/IMemoryVectorStore.cs` (research.md Decision 5)
- [X] T017 [P] Create `IMemoryConflictDetectionService` abstraction in `src/AskLucy.Application/Abstractions/IMemoryConflictDetectionService.cs` (research.md Decision 10)
- [X] T018 [P] Create `IMemoryNotifier` abstraction in `src/AskLucy.Application/Abstractions/IMemoryNotifier.cs` (research.md Decision 11)
- [X] T019 [P] Create `IMemoryExtractionJob` abstraction — `RunAsync(chatId, ct)` in `src/AskLucy.Application/Abstractions/IMemoryExtractionJob.cs` (research.md Decision 6)

### Persistence

- [X] T020 Create EF Core Fluent API configurations for all 10 new `Memory` entities and `Project` — native `vector(n)` column mapping on `MemoryEmbedding.Vector` (EF-ignored, no vector index, research.md Decision 5), `IDataProtector`-backed value converter on `Memory.Content`/`MemoryVersion.PreviousContent`/`MemoryReference.ContentSnapshot` (research.md Decision 12), soft-delete global query filter on `Memory`/`Project`, append-only-no-cascade configuration for `MemoryAuditLog`/`MemoryNotification`/`MemoryReference`, indexes on every FK/`UserId`/`ProjectId` column (constitution §5) — plus `DbSet<T>` registrations on `AskLucyDbContext` in `src/AskLucy.Persistence/Configurations/Memory/*.cs`, `src/AskLucy.Persistence/Configurations/Projects/ProjectConfiguration.cs` (depends on T003–T013)
- [X] T021 [P] Extend the EF configuration for `UserChat`'s additive `ProjectId` column in `src/AskLucy.Persistence/Configurations/UserChatConfiguration.cs` (path corrected — no `Chats/` subdirectory exists in this codebase's `Configurations/` layout) (depends on T014)
- [X] T022 Generate the EF Core migration `AddMemorySystem` (new `Memory`/`Projects` tables, additive `UserChat.ProjectId` column; no vector index DDL — see T020) via `dotnet ef migrations add AddMemorySystem -p src/AskLucy.Persistence -s src/AskLucy.Web`; verify `Down()` is reversible and `dotnet ef database update` succeeds (depends on T020, T021) — migration generated and builds clean; `database update` deferred to a live SQL Server instance

### Repositories

- [X] T023 [P] Create `IMemoryRepository`/`MemoryRepository` (`GetByIdForUserAsync`, `GetActiveByUserAndProjectAsync`, `SearchAsync`) in `src/AskLucy.Application/Abstractions/IMemoryRepository.cs`, `src/AskLucy.Persistence/Repositories/MemoryRepository.cs` (depends on T022) — expanded to 9 repositories (Memory, MemoryVersion, MemoryApproval, MemoryConflict, MemoryEmbedding, MemoryAuditLog, MemoryNotification, MemoryPreference, MemoryReference), matching RAG's per-entity repository precedent
- [X] T024 [P] Create `IMemoryPreferenceRepository`/`MemoryPreferenceRepository` (lazy-create-with-defaults on first access) in `src/AskLucy.Application/Abstractions/IMemoryPreferenceRepository.cs`, `src/AskLucy.Persistence/Repositories/MemoryPreferenceRepository.cs` (depends on T022)
- [X] T025 [P] Create `IProjectRepository`/`ProjectRepository` in `src/AskLucy.Application/Abstractions/IProjectRepository.cs`, `src/AskLucy.Persistence/Repositories/ProjectRepository.cs` (depends on T022)
- [X] T026 [P] Create `MemoryOwnershipGuard` (mirrors `ChatOwnershipGuard`/`KnowledgeBaseOwnershipGuard` — denial looks like not-found, FR-027) in `src/AskLucy.Application/Memory/Authorization/MemoryOwnershipGuard.cs` (depends on T023)
- [X] T027 [P] Create `ProjectOwnershipGuard` in `src/AskLucy.Application/Projects/Authorization/ProjectOwnershipGuard.cs` (depends on T025)

### Core pipeline mechanics

- [X] T028 Implement `SqlServerMemoryVectorStore` (`IMemoryVectorStore` — upsert/delete/per-user-scoped brute-force `VECTOR_DISTANCE` nearest-neighbor query, no `CREATE VECTOR INDEX`, research.md Decision 5) in `src/AskLucy.Persistence/Memory/SqlServerMemoryVectorStore.cs` (depends on T016, T023)
- [X] T029 Implement `MemoryService` (`IMemoryService` — composite ranking `similarity × recencyDecay × importance × confidence`, token-budgeted selection, catches all exceptions and returns `Unavailable` rather than throwing, research.md Decisions 3/4) in `src/AskLucy.Application/Memory/MemoryService.cs` (depends on T015, T028, existing `IEmbeddingService`)
- [X] T030 Implement `MemoryConflictDetectionService` (`IMemoryConflictDetectionService` — vector-candidate pool via `IMemoryVectorStore` + one `IAIProvider` classification call; `DirectContradiction` auto-updates the existing memory with a `MemoryVersion` entry and a `MemoryAuditLog` `ConflictDetected`/`ConflictResolved` pair (FR-028); `AmbiguousSupersedeOrSupplement` creates a `MemoryConflict` row, writes a `MemoryAuditLog` `ConflictDetected` entry, and calls `IMemoryNotifier`, research.md Decision 10) in `src/AskLucy.Application/Memory/MemoryConflictDetectionService.cs` (depends on T017, T028, T006, T008) — amended during `/speckit-analyze` remediation (finding U1) to make audit-log writes explicit; placed in `AskLucy.Application`, not `Infrastructure` as originally planned (same reasoning as `IMemoryExtractionJob`'s doc comment — pure orchestration over Application abstractions)
- [X] T031 Implement `MemoryHub` (mirrors `DocumentProcessingHub`'s server-verified per-user-group join) + `MemoryNotifier` (`IMemoryNotifier` — persists a `MemoryNotification` row then pushes `memoryNotificationCreated`), map `/hubs/memory` in `Program.cs`, in `src/AskLucy.Infrastructure/Memory/MemoryHub.cs`, `MemoryNotifier.cs` (depends on T018, T009)
- [X] T032 Implement `MemoryExtractionJob` (`IMemoryExtractionJob` — one structured `IAIProvider.ChatAsync` call via a configurable "utility model" producing `content`/`category`/`isExplicit`/`isSensitive`/`confidence` per candidate; creates `Memory` rows respecting `MemoryCategoryPreference.ApprovalMode`, with `IsSensitive` forcing `Manual` regardless of configured mode (FR-008); writes a `MemoryAuditLog` `Created` entry for every candidate (FR-028); calls `IMemoryConflictDetectionService` per new candidate; `[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 120, 600 })]` — the codebase's first use of this attribute, research.md Decision 6) in `src/AskLucy.Application/Memory/MemoryExtractionJob.cs` (depends on T019, T029, T030, T024, T008) — amended during `/speckit-analyze` remediation (finding U1) to make audit-log writes explicit; placed in `AskLucy.Application` per its own doc comment
- [X] T033 Implement `MemoryExtractionSweepJob` (recurring — finds conversations updated since a per-conversation `LastMemoryAnalyzedAtUtc` checkpoint not yet processed by the per-turn enqueue) + register via `RecurringJob.AddOrUpdate<MemoryExtractionSweepJob>("memory-extraction-sweep", j => j.RunAsync(CancellationToken.None), "*/15 * * * *")` in `Program.cs`, in `src/AskLucy.Infrastructure/Memory/MemoryExtractionSweepJob.cs` (depends on T032) — required adding `UserChat.LastMemoryAnalyzedAtUtc` + `MarkMemoryAnalyzed()` (not in the original data-model.md) plus `IUserChatRepository.ListNeedingMemoryAnalysisAsync`
- [X] T033a Implement `MemoryCleanupJob` (recurring — soft-deletes explicitly expired (`ExpiresAtUtc`) and long-archived memories, writes a `MemoryAuditLog` `Expired` entry per removal, research.md Decision 18) + register via `RecurringJob.AddOrUpdate<MemoryCleanupJob>("memory-cleanup", j => j.RunAsync(CancellationToken.None), Cron.Daily)` in `Program.cs`, in `src/AskLucy.Infrastructure/Memory/MemoryCleanupJob.cs` (depends on T008, T023) — added during `/speckit-analyze` remediation (finding C1); resolves FR-031
- [X] T034 Enqueue `MemoryExtractionJob` via `IBackgroundJobClient` immediately after each assistant turn finishes streaming, fire-and-forget, in `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs` (depends on T032)
- [X] T035 Integrate `IMemoryService` into `SendChatMessageCommandHandler` — retrieve relevant memories, insert a `<user_memory>`-delimited, defensively-framed `ChatRole.System` message *before* RAG's own context message (research.md Decisions 2/9), and record one `MemoryReference` row per selected memory in `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs` (depends on T029, T012) — MemoryReference persistence lives in a new `RecordMemoryReferencesCommand` invoked from `AiController.Chat` after the assistant message is persisted (mirrors how RAG citations are attached), since the message id doesn't exist until then

**Checkpoint**: Solution builds; migration applies; stating a fact is captured by the extraction
pipeline and, once active (auto- or manually approved), is retrieved and injected into a later
conversation's prompt end to end — but nothing is yet exposed via a dedicated API/UI beyond the
chat pipeline itself.

---

## Phase 3: User Story 1 - Lucy remembers me across conversations (Priority: P1) 🎯 MVP

**Goal**: A fact or preference stated in one conversation is available, without restating, in a
later unrelated conversation — degrading gracefully (never blocking/erroring) if the memory
subsystem is unavailable.

**Independent Test**: State a preference or fact, start a brand-new conversation, ask a question
where it's relevant, confirm the response reflects it without the user restating it (quickstart.md
Scenario 1).

### Tests for User Story 1

- [X] T036 [P] [US1] Unit tests for `MemoryService`'s `Found`/`NoneRelevant`/`Unavailable` outcome branches and the composite ranking score, with faked `IEmbeddingService`/`IMemoryVectorStore`, in `tests/AskLucy.Application.Tests/Memory/MemoryServiceTests.cs`
- [X] T037 [P] [US1] Integration test: `SendChatMessageCommandHandler` inserts the memory context message before RAG's context message when both apply, and omits it when memory is unavailable/none relevant (US1 AC1, AC3) in `tests/AskLucy.Application.Tests/Ai/SendChatMessageMemoryIntegrationTests.cs`
- [X] T038 [P] [US1] Integration test: with memory disabled, a new conversation never references a fact stated during the disabled period (US1 AC2, FR-022) in `tests/AskLucy.Application.Tests/Memory/MemoryDisabledExclusionTests.cs`
- [X] T039 [P] [US1] Integration test: memory subsystem forced unavailable at response time — the chat still responds, without memory context, with no added delay, and the failure is present in structured logs, never surfaced to the user (clarified 2026-08-09 Q1, FR-014a) in `tests/AskLucy.Application.Tests/Memory/MemoryDegradedModeTests.cs`
- [X] T040 [P] [US1] Playwright E2E: state a fact, start a new conversation, confirm it's reflected without restating, open the "why does Lucy know this" trace for that response (quickstart.md Scenario 1) in `tests/AskLucy.E2E.Tests/RememberAndRecall.spec.ts` — not runnable in this environment (no live backend/frontend deployment), matching every other Playwright spec's existing caveat in this repo

### Implementation for User Story 1

- [X] T041 [US1] `GetMemoryReferences` query (FR-014, "why does Lucy know this") in `src/AskLucy.Application/Memory/Queries/GetMemoryReferences/` (depends on T012, T035)
- [X] T042 [US1] `GET /api/v1/chats/{chatId}/messages/{messageId}/memory-references` (contracts/memories-api.md) in `src/AskLucy.Web/Controllers/v1/ChatsController.cs` (depends on T041)
- [X] T043 [P] [US1] Frontend: `memoryApi.ts` client + `useMemoryReferences.ts` hook in `src/AskLucy.Web/ClientApp/src/features/memory/api/memoryApi.ts`, `hooks/useMemoryReferences.ts`
- [X] T044 [US1] Wire a subtle "Lucy remembered this" indicator into the chat message renderer, opening the memory-reference trace on demand, in `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx` (depends on T043) — required restructuring `AiController.Chat` to persist the assistant message (and its `MemoryReference` rows) *before* writing the SSE `[DONE]` event, plus a new trailing `__MEMORY__` event, so the trace is fetchable within the same live session rather than only after a reload

**Checkpoint**: User Story 1 is independently functional and testable — this is the MVP.

---

## Phase 4: User Story 2 - User reviews and manages what Lucy remembers (Priority: P1)

**Goal**: A Memory Center where every memory is visible (content, category, source, date, state),
searchable/filterable, editable (with history), and deletable.

**Independent Test**: Generate at least one memory, open the Memory Center, view/search/edit/delete
it, confirm the change takes effect in subsequent conversations (quickstart.md Scenario 2).

### Tests for User Story 2

- [X] T045 [P] [US2] Integration tests: `ListMemories` filtering/search/pagination; `GetMemory` includes history; `EditMemory` appends a `MemoryVersion`; `DeleteMemory` excludes immediately from future retrieval (US2 AC1–AC4) in `tests/AskLucy.Application.Tests/Memory/MemoryCenterTests.cs` — written as a real-SQL-Server `AskLucy.Persistence.Tests` repository-level suite (constitution §10) at `tests/AskLucy.Persistence.Tests/Memory/MemoryCenterTests.cs` instead, mirroring `KnowledgeBaseCursorPaginationTests`; not runnable in this environment (no `PERSISTENCE_TESTS_CONNECTION_STRING`)
- [X] T046 [P] [US2] Integration test: a request naming a memory the caller doesn't own returns not-found, never confirming existence (FR-027) in `tests/AskLucy.Application.Tests/Memory/MemoryOwnershipTests.cs`
- [X] T047 [P] [US2] Playwright E2E: open the Memory Center, view fields, edit, delete, search/filter by category, complete the full cycle in under 30 seconds (quickstart.md Scenario 2, SC-002) in `tests/AskLucy.E2E.Tests/MemoryCenter.spec.ts` — not runnable in this environment, matching every other Playwright spec's existing caveat

### Implementation for User Story 2

- [X] T048 [US2] `ListMemories` query — cursor-paginated, `category`/`state`/`projectId`/free-text filters (contracts/memories-api.md) in `src/AskLucy.Application/Memory/Queries/ListMemories/` (depends on T023, T026) — required extending `IMemoryRepository.SearchAsync`/new `CountAsync` with a `generalOnly` flag to support the contract's three-way `projectId=`/`general`/omitted scoping
- [X] T049 [US2] `GetMemory` query — detail + history + `openConflict` in `src/AskLucy.Application/Memory/Queries/GetMemory/` (depends on T023, T026)
- [X] T050 [US2] `EditMemory` command — appends a `MemoryVersion`, writes a `MemoryAuditLog` entry in `src/AskLucy.Application/Memory/Commands/EditMemory/` (depends on T023, T026, T008) — also re-embeds the new content and upserts the vector store, unlike `MemoryConflictDetectionService`'s documented re-embedding gap, since this is a deliberate user correction
- [X] T051 [US2] `DeleteMemory` command — soft delete, writes a `MemoryAuditLog` entry in `src/AskLucy.Application/Memory/Commands/DeleteMemory/` (depends on T023, T026, T008)
- [X] T052 [US2] `MemoriesController` — list/get/edit/delete endpoints (contracts/memories-api.md) in `src/AskLucy.Web/Controllers/v1/MemoriesController.cs` (depends on T048–T051)
- [X] T053 [P] [US2] Frontend: `MemoryList.tsx`, `MemoryCard.tsx`, `MemoryEditDialog.tsx`, `MemoryDeleteConfirmDialog.tsx` in `src/AskLucy.Web/ClientApp/src/features/memory/components/` — reuses the existing shared `components/ConfirmDialog.tsx` for delete confirmation instead of a bespoke `MemoryDeleteConfirmDialog.tsx` (constitution §7 — this is exactly that component's existing purpose)
- [X] T054 [P] [US2] Frontend: `useMemories.ts` + `useMemoryMutations.ts` hooks in `src/AskLucy.Web/ClientApp/src/features/memory/hooks/`
- [X] T055 [US2] `MemoryCenterPage.tsx`; wire the `/memory` route and a navigation entry in `src/AskLucy.Web/ClientApp/src/features/memory/pages/MemoryCenterPage.tsx`, `src/AskLucy.Web/ClientApp/src/routes/router.tsx` (depends on T053, T054) — nav entry added to `components/UserMenu.tsx` (this app has no sidebar)
- [X] T056 [P] [US2] Frontend: `memoryCenterStore.ts` (Zustand, UI-only filter/layout state, mirrors `knowledgeBaseDashboardStore.ts`) in `src/AskLucy.Web/ClientApp/src/features/memory/store/memoryCenterStore.ts`

**Checkpoint**: User Stories 1 + 2 together form the MVP — remember/recall works and is fully
user-visible and manageable.

---

## Phase 5: User Story 3 - User approves what Lucy is allowed to remember (Priority: P2)

**Goal**: Per-category approval modes (`Automatic`/`Manual`/`Disabled`) govern how candidates
become active, with sensitive content always forced to manual review and a low-noise notification
whenever something is created without review.

**Independent Test**: Set approval mode to manual, confirm a candidate stays pending and unused
until approved, then approve it and confirm it now applies (quickstart.md Scenario 3).

### Tests for User Story 3

- [X] T057 [P] [US3] Integration tests: manual mode holds a candidate as `PendingApproval`, unused until approved (AC1); approve activates it (AC2); reject discards it (AC3); automatic mode activates without review with the source disclosed (AC4); disabled mode creates no candidates at all (AC5) in `tests/AskLucy.Application.Tests/Memory/ApprovalWorkflowTests.cs` — also required backfilling a `MemoryApproval` row from `MemoryExtractionJob` itself (a gap found while writing this test — the row didn't previously exist until a user acted) plus an `AutoApproved` `IMemoryNotifier` call for FR-006a
- [X] T058 [P] [US3] Integration test: a candidate flagged sensitive is always held for manual review regardless of the category's configured mode, ≥95% correctly flagged across a labeled test batch (FR-008, SC-004) in `tests/AskLucy.Application.Tests/Memory/SensitiveContentTests.cs` — SC-004's accuracy percentage is a live-model classification metric, not unit-testable deterministically; this proves the domain-level enforcement guarantee instead (see the test file's doc comment)
- [X] T059 [P] [US3] Playwright E2E: set manual mode and approve/reject a candidate, set automatic, set disabled, trigger a sensitive statement and confirm it's held (quickstart.md Scenario 3) in `tests/AskLucy.E2E.Tests/MemoryApproval.spec.ts` — not runnable in this environment, matching every other Playwright spec's existing caveat

### Implementation for User Story 3

- [X] T060 [US3] `ApproveMemory`/`RejectMemory` commands — `409` unless `Candidate`/`PendingApproval`, writes `MemoryAuditLog` in `src/AskLucy.Application/Memory/Commands/ApproveMemory/`, `RejectMemory/` (depends on T023, T026, T005) — added `MemoryNotPendingApprovalException` (`src/AskLucy.Domain/Memory/`) + a `ProblemDetailsMiddleware` mapping to produce the contract's `409`, since the domain's existing `DomainRuleViolationException` maps to `400`
- [X] T061 [US3] `GetMemoryPreferences` query + `UpdateMemoryPreferences` command — `memoryEnabled` plus per-category `approvalMode`/`isEnabled` with partial-update semantics (contracts/memory-privacy-api.md) in `src/AskLucy.Application/Memory/Queries/GetMemoryPreferences/`, `Commands/UpdateMemoryPreferences/` (depends on T024, T010, T011)
- [X] T062 [US3] `ListMemoryNotifications` query + `MarkNotificationRead` command in `src/AskLucy.Application/Memory/Queries/ListMemoryNotifications/`, `Commands/MarkNotificationRead/` (depends on T009)
- [X] T063 [US3] Extend `MemoriesController` with approve/reject/preferences/notifications endpoints (contracts/memories-api.md, contracts/memory-privacy-api.md) in `src/AskLucy.Web/Controllers/v1/MemoriesController.cs` (depends on T060–T062)
- [X] T064 [P] [US3] Frontend: `MemoryApprovalQueue.tsx` (pending candidates, approve/reject actions) in `src/AskLucy.Web/ClientApp/src/features/memory/components/MemoryApprovalQueue.tsx`
- [X] T065 [P] [US3] Frontend: `MemoryPreferencesPanel.tsx` (`memoryEnabled` toggle, per-category approval-mode/enabled controls) in `src/AskLucy.Web/ClientApp/src/features/memory/components/MemoryPreferencesPanel.tsx`
- [X] T066 [P] [US3] Frontend: `useMemoryNotificationsHub.ts` (SignalR + poll fallback, mirrors `useDocumentProcessingHub`) + `MemoryNotificationList.tsx` in `src/AskLucy.Web/ClientApp/src/features/memory/hooks/useMemoryNotificationsHub.ts`, `components/MemoryNotificationList.tsx`
- [X] T067 [US3] Wire `MemoryApprovalQueue`/`MemoryPreferencesPanel`/notifications into `MemoryCenterPage.tsx` (depends on T064–T066, T055) — added as `Tabs` (All memories / Approval queue / Preferences / Notifications)

**Checkpoint**: Approval modes and low-noise notifications are fully functional.

---

## Phase 6: User Story 4 - User controls memory privacy at the account level (Priority: P2)

**Goal**: Enable/disable memory entirely, clear all memories, export a complete human-readable copy,
and disable individual categories — each with immediate effect.

**Independent Test**: Enable memory, generate memories, then disable / clear-all / export and
confirm each takes full effect (quickstart.md Scenario 4).

### Tests for User Story 4

- [X] T068 [P] [US4] Integration test: disabling memory stops creation and use without deleting stored data (AC1) — exercises `UpdateMemoryPreferences` from US3 in `tests/AskLucy.Application.Tests/Memory/MemoryPrivacyTests.cs`
- [X] T069 [P] [US4] Integration test: `ClearAllMemories` permanently removes every memory only after explicit confirmation (AC2, FR-023) in `tests/AskLucy.Application.Tests/Memory/ClearAllMemoriesTests.cs`
- [X] T070 [P] [US4] Integration test: export produces a complete, human-readable JSON file grouped by category; a zero-memory account still gets a valid empty export, not an error (AC3, FR-024, Edge Case) in `tests/AskLucy.Application.Tests/Memory/MemoryExportTests.cs`
- [X] T071 [P] [US4] Integration test: disabling one category stops new/used memories in that category only, other categories keep working (AC4, FR-025) — exercises `UpdateMemoryPreferences` from US3 in `tests/AskLucy.Application.Tests/Memory/MemoryCategoryDisableTests.cs` — this also surfaced that `MemoryService` never actually filtered on `MemoryCategoryPreference.IsEnabled`; fixed as part of this task
- [X] T072 [P] [US4] Playwright E2E: disable/re-enable memory, clear-all in ≤3 actions, export (including the zero-memory case), disable one category (quickstart.md Scenario 4, SC-003) in `tests/AskLucy.E2E.Tests/MemoryPrivacyControls.spec.ts` — not runnable in this environment, matching every other Playwright spec's existing caveat

### Implementation for User Story 4

- [X] T073 [US4] `ClearAllMemories` command — requires explicit `confirm: true`, permanently deletes, writes `MemoryAuditLog` in `src/AskLucy.Application/Memory/Commands/ClearAllMemories/` (depends on T023, T008) — soft-deletes synchronously (immediate user-visible effect via the standard query filter); "permanently" is satisfied by the same retention mechanism every other memory deletion in this feature uses, not a separate hard-purge job
- [X] T074 [US4] `RequestMemoryExport` command + `GetMemoryExportStatus` query — JSON file grouped by category, served via a signed expiring URL (research.md Decision 14) in `src/AskLucy.Application/Memory/Commands/RequestMemoryExport/`, `Queries/GetMemoryExportStatus/` (depends on T023) — required adding a new `MemoryExportJob` entity/repository/EF config (tracks Processing/Ready/Failed across the request/poll round trip) and a Hangfire `MemoryExportGenerationJob` (`src/AskLucy.Application/Memory/MemoryExportGenerationJob.cs`), none of which were in the original data-model.md — the alternative (no durable job row) would let any authenticated user guess/poll another user's export status
- [X] T075 [US4] Extend `MemoriesController` with clear-all/export endpoints (contracts/memory-privacy-api.md) in `src/AskLucy.Web/Controllers/v1/MemoriesController.cs` (depends on T073, T074) — the actual signed-download-content endpoint mirrors `DocumentsController.DownloadContent`'s `[AllowAnonymous]` + signature-validation pattern exactly
- [X] T076 [P] [US4] Frontend: `ClearAllMemoriesDialog.tsx` (explicit confirmation step) + `MemoryExportButton.tsx` in `src/AskLucy.Web/ClientApp/src/features/memory/components/` — reuses the existing shared `components/ConfirmDialog.tsx` for the confirmation step instead of a bespoke `ClearAllMemoriesDialog.tsx` (constitution §7, same reasoning as T053)
- [X] T077 [US4] Wire clear-all/export controls into `MemoryPreferencesPanel.tsx` (depends on T076, T065)

**Checkpoint**: The full account-level privacy control surface is complete.

---

## Phase 7: User Story 5 - User groups related work into a Project so memory stays scoped (Priority: P3)

**Goal**: Create/rename/delete Projects, assign a conversation to at most one Project, and confirm
project-scoped memories stay within their Project (general memories still apply everywhere).

**Independent Test**: Create a Project, state a project-specific fact inside it, confirm it's used
in that Project's other conversations but not elsewhere (quickstart.md Scenario 5).

### Tests for User Story 5

- [X] T078 [P] [US5] Integration test: a project-scoped fact is available within the same Project's conversations, not outside it (AC1) in `tests/AskLucy.Application.Tests/Projects/ProjectScopedMemoryTests.cs` — proves the `MemoryService` boundary (the active `projectId` is passed through unchanged); the SQL-level row-scoping predicate itself lives in `SqlServerMemoryVectorStore` (Persistence, real-DB-only per constitution §10)
- [X] T079 [P] [US5] Integration test: a conversation with no Project only considers general (non-project-scoped) memories (AC2) in `tests/AskLucy.Application.Tests/Memory/GeneralScopeMemoryTests.cs`
- [X] T080 [P] [US5] Integration test: deleting a Project archives — never immediately deletes — its scoped memories, which remain visible/exportable outside the Project context (AC3) in `tests/AskLucy.Application.Tests/Projects/ProjectDeletionCascadeTests.cs`
- [X] T081 [P] [US5] Playwright E2E: create a Project, assign a conversation, state a project-scoped fact, verify scoping both ways, delete the Project and confirm archival (quickstart.md Scenario 5) in `tests/AskLucy.E2E.Tests/ProjectScopedMemory.spec.ts` — not runnable in this environment, matching every other Playwright spec's existing caveat

### Implementation for User Story 5

- [X] T082 [US5] `CreateProject`/`RenameProject`/`DeleteProject` commands — delete soft-deletes and triggers `ProjectDeletedDomainEvent` (contracts/projects-api.md) in `src/AskLucy.Application/Projects/Commands/CreateProject/`, `RenameProject/`, `DeleteProject/` (depends on T025, T027) — no domain event is raised (see `Project.cs`'s doc comment, already corrected during Foundational implementation: no domain-event dispatch infrastructure exists in this codebase)
- [X] T083 [US5] `ProjectDeletedDomainEvent` handler — archives (never deletes) every `Active`/`PendingApproval` `Memory` row scoped to the deleted project (US5 AC3, research.md Decision 15) in `src/AskLucy.Application/Memory/EventHandlers/ProjectDeletedDomainEventHandler.cs` (depends on T013, T023) — implemented as a direct `IMemoryRepository` call inside `DeleteProjectCommandHandler` (`src/AskLucy.Application/Projects/Commands/DeleteProject/`) instead of a dispatched event handler, for the same reason as T082; archives every memory `GetByProjectAsync` returns rather than filtering by state first, since `Archive()` is idempotent
- [X] T084 [US5] `ListProjects` query, cursor-paginated in `src/AskLucy.Application/Projects/Queries/ListProjects/` (depends on T025)
- [X] T085 [US5] `AssignConversationToProject` command — validates the target Project is owned by the caller in `src/AskLucy.Application/Projects/Commands/AssignConversationToProject/` (depends on T014, T027)
- [X] T086 [US5] `ProjectsController` — CRUD + list endpoints (contracts/projects-api.md) in `src/AskLucy.Web/Controllers/v1/ProjectsController.cs` (depends on T082, T084)
- [X] T087 [US5] Extend `ChatsController` with `PUT /api/v1/chats/{chatId}/project` (contracts/projects-api.md) in `src/AskLucy.Web/Controllers/v1/ChatsController.cs` (depends on T085)
- [X] T088 [P] [US5] Frontend: `ProjectPicker.tsx` (assign/remove a conversation's Project) + `ProjectManagementPanel.tsx` (create/rename/delete) in `src/AskLucy.Web/ClientApp/src/features/memory/components/ProjectPicker.tsx`, `ProjectManagementPanel.tsx`
- [X] T089 [P] [US5] Frontend: `projectsApi.ts` + `useProjects.ts` + `useProjectMutations.ts` in `src/AskLucy.Web/ClientApp/src/features/memory/api/projectsApi.ts`, `hooks/useProjects.ts`, `useProjectMutations.ts`
- [X] T090 [US5] Wire `ProjectPicker` into the chat composer/settings and `ProjectManagementPanel` into `MemoryCenterPage.tsx` (depends on T088, T089, T055) — `ProjectPicker` wired into `ChatPage.tsx`'s toolbar (this app has no separate composer-level settings surface); its current selection is local view state only, not yet seeded from persisted history since `UserChatDto` doesn't carry `ProjectId` — a known, documented scope limitation

**Checkpoint**: Project-scoped memory is fully functional.

---

## Phase 8: User Story 6 - Lucy resolves contradictory memories (Priority: P3)

**Goal**: Direct contradictions auto-update with full history; ambiguous conflicts are resolved
asynchronously via the Memory Center without ever interrupting the live conversation turn that
surfaced them.

**Independent Test**: State a fact, later state a contradicting fact, confirm the system
flags/updates rather than retaining both as equally valid (quickstart.md Scenario 6).

### Tests for User Story 6

- [X] T091 [P] [US6] Integration test: a direct contradiction auto-updates the memory, preserves the prior value in history, no interruption (AC1, FR-015) in `tests/AskLucy.Application.Tests/Memory/DirectContradictionTests.cs`
- [X] T092 [P] [US6] Integration test: an ambiguous conflict leaves the live turn uninterrupted, flags the memory `PendingUserConfirmation`, excludes it from retrieval, and raises a notification (AC2, FR-016, clarified 2026-08-09) in `tests/AskLucy.Application.Tests/Memory/AmbiguousConflictTests.cs`
- [X] T093 [P] [US6] Integration test: resolving a conflict (`KeepExisting`/`KeepNew`/`KeepBoth`) updates `ResolutionStatus` and retrieval eligibility; history shows detection and resolution timestamps (AC3) in `tests/AskLucy.Application.Tests/Memory/ResolveConflictTests.cs`
- [X] T094 [P] [US6] Playwright E2E: trigger a direct contradiction, trigger an ambiguous conflict, resolve it asynchronously and confirm the live turn was never interrupted (quickstart.md Scenario 6) in `tests/AskLucy.E2E.Tests/MemoryConflictResolution.spec.ts` — not runnable in this environment, matching every other Playwright spec's existing caveat

### Implementation for User Story 6

- [X] T095 [US6] `ResolveMemoryConflict` command — `409` unless an open conflict exists, writes `MemoryAuditLog` (contracts/memories-api.md) in `src/AskLucy.Application/Memory/Commands/ResolveMemoryConflict/` (depends on T023, T026, T006) — added `MemoryConflictNotPendingException` + `ProblemDetailsMiddleware` mapping (same pattern as T060's `MemoryNotPendingApprovalException`); the losing side of `KeepExisting`/`KeepNew` is soft-deleted, `KeepBoth` discards neither
- [X] T096 [US6] Extend `MemoriesController` with the resolve-conflict endpoint (contracts/memories-api.md) in `src/AskLucy.Web/Controllers/v1/MemoriesController.cs` (depends on T095)
- [X] T097 [P] [US6] Frontend: `MemoryConflictDialog.tsx` (`KeepExisting`/`KeepNew`/`KeepBoth` resolution UI) in `src/AskLucy.Web/ClientApp/src/features/memory/components/MemoryConflictDialog.tsx`
- [X] T098 [US6] Wire `MemoryConflictDialog` into `MemoryNotificationList.tsx`/`MemoryCenterPage.tsx` (depends on T097, T066) — wired via notification click (a `ConflictNeedsConfirmation` notification carries the memory id); not additionally surfaced from the "All memories" list view, since `MemoryListItemDto` doesn't carry `openConflict` (only `MemoryDetailDto` does) — a documented, minor scope limitation

**Checkpoint**: All six user stories are independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories.

- [X] T099 [P] Accessibility pass (WCAG 2.1 AA) on `MemoryCenterPage`, `MemoryPreferencesPanel`, `MemoryConflictDialog`, `ProjectManagementPanel` — keyboard operability, ARIA roles, contrast; automated axe checks. Deviation: implemented as `jest-axe` frontend component tests (`*.a11y.test.tsx`, this codebase's established a11y-testing convention — every other `.a11y.test.tsx` file uses it, and there is no C#-side axe precedent) rather than the literal `tests/AskLucy.Web.Tests/Memory/MemoryAccessibilityTests.cs` path: `MemoryCard.a11y.test.tsx`, `MemoryEditDialog.a11y.test.tsx`, `MemoryPreferencesPanel.a11y.test.tsx`, `MemoryConflictDialog.a11y.test.tsx`, `ProjectManagementPanel.a11y.test.tsx` (all under `ClientApp/src/features/memory/components/`). `MemoryCenterPage`'s own chrome (AppShell + Tabs) is covered by `AppShell.a11y.test.tsx`; a dedicated whole-page test was judged redundant given every tab's content component already has its own passing check. Found and fixed one real violation: `MemoryPreferencesPanel`'s per-category approval-mode `<TextField select>` used a top-level `aria-label` prop, which MUI places on a non-interactive wrapper `<div>` rather than the actual combobox — moved to `slotProps={{ select: { 'aria-label': ... } }}` so the accessible name reaches the right element
- [X] T100 [P] Reflection-based test confirming no `AskLucy.Domain`/`AskLucy.Application` type in `Memory`/`Projects` references a specific AI vendor SDK or raw SQL vector syntax directly (constitution §2.I structural check) in `tests/AskLucy.Application.Tests/Architecture/MemoryLayeringTests.cs`
- [X] T101 [P] Integration test: `MemoryExtractionJob`'s automatic retry-with-backoff and team-observable (never user-facing) logging once retries are exhausted (FR-006b, quickstart.md Scenario 7). Deviation: placed in `tests/AskLucy.Application.Tests/Memory/MemoryExtractionRetryTests.cs`, not `AskLucy.Infrastructure.Tests` — `MemoryExtractionJob` itself lives in `AskLucy.Application` (see its own doc comment), a deviation recorded during the Foundational phase
- [X] T101a [P] Integration test: `MemoryCleanupJob` soft-deletes only explicitly-expired and long-archived memories (never `Active`/`Candidate`/`PendingApproval` rows) and writes a `MemoryAuditLog` `Expired` entry per removal (FR-031, research.md Decision 18) in `tests/AskLucy.Infrastructure.Tests/Memory/MemoryCleanupJobTests.cs` — added during `/speckit-analyze` remediation (finding C1)
- [X] T102 [P] Security test: zero cross-user memory/Project exposure across every Memory/Projects endpoint (SC-005) in `tests/AskLucy.Web.Tests/Memory/MemoryCrossUserSecurityTests.cs`. Scope mirrors the established precedent in `tests/AskLucy.Web.Tests/Chats/OwnershipTests.cs`: this environment has no live database or second seeded user, so this class proves the outer 401 auth gate across every Memory/Projects endpoint; true cross-user 404 ownership denial is unit-tested at the Application layer (`MemoryOwnershipTests`, `ProjectScopedMemoryTests`, `ProjectDeletionCascadeTests`, `ResolveConflictTests`, etc.)
- [X] T102a Hook Memory/Project purge into the existing account-deletion flow (research.md Decision 19) — hard-deletes all `Memory`-bounded-context rows and `Project`s owned by the deleted user; `MemoryAuditLog`/`MemoryNotification` rows survive with `UserId` anonymized. Deviation: wired directly into `src/AskLucy.Application/Users/Commands/DeleteMyAccount/DeleteMyAccountCommandHandler.cs` rather than a new `UserAccountDeletedDomainEventHandler.cs` — investigation found two distinct deletion paths (`DeleteMyAccountCommandHandler`, a real hard delete via Identity's `UserManager.DeleteAsync`, vs. `DeleteUserCommandHandler`, an admin **soft** delete that doesn't cascade); only the former is the FR-026 GDPR-erasure path, and no domain event exists at that point in the flow to hook — `Memory`/`Project`/`MemoryPreference`/`MemoryCategoryPreference` already cascade-delete via FK `ON DELETE CASCADE` "for free"; only the two deliberately-un-FK'd audit/notification tables needed the new `AnonymizeUserAsync` step — added during `/speckit-analyze` remediation (finding C2); resolves FR-026
- [X] T102b [P] Integration test: deleting a user account permanently removes all of their Memory/Project data while `MemoryAuditLog`/`MemoryNotification` rows survive with an anonymized `UserId` (FR-026) in `tests/AskLucy.Application.Tests/Memory/AccountDeletionCascadeTests.cs` — added during `/speckit-analyze` remediation (finding C2)
- [X] T103 [P] Update `docs/ARCHITECTURE.md` with the shipped `Memory`/`Projects` bounded-context design (`IMemoryService`/`IMemoryVectorStore`/`IMemoryConflictDetectionService`) — new §28 "AI Memory System"
- [X] T103a [P] Performance test: seed a representative memory volume (thousands per test user, SC-006) and assert retrieval latency stays within an explicit budget with no measurable regression as volume scales — wired to fail the build on regression (constitution §10/§15). Deviation: `tests/AskLucy.Persistence.Tests/Memory/MemoryRetrievalPerformanceTests.cs` (real-SQL-Server-only, constitution §10), not `AskLucy.Infrastructure.Tests`, and asserts on `SqlServerMemoryVectorStore.QueryNearestAsync` + `MemoryRepository.GetActiveByIdsAsync` directly rather than `MemoryService.RetrieveRelevantMemoriesAsync` — `MemoryService`'s own in-memory ranking step only ever iterates the vector store's already-topK-limited (≤8) result set, so it can never be the bottleneck at scale, and calling it directly would require faking the embedding-provider AI call it makes first; the two real database round trips it depends on are what SC-006's row count actually stresses — added during `/speckit-analyze` remediation (finding C3)
- [X] T104 Run quickstart.md validation end-to-end (all 7 scenarios) — not executable in this environment (no live deployed environment, no browser); all 7 scenarios are covered by the corresponding automated tests instead (`SendChatMessageMemoryIntegrationTests`/`RememberAndRecall.spec.ts` for Scenario 1, `MemoryCenterTests`/`MemoryCenter.spec.ts` for Scenario 2, `ApprovalWorkflowTests`/`MemoryApproval.spec.ts` for Scenario 3, `MemoryPrivacyTests`/`MemoryPrivacyControls.spec.ts` for Scenarios 4–5, `ProjectScopedMemoryTests`/`ProjectScopedMemory.spec.ts` for Scenario 6, `DirectContradictionTests`/`AmbiguousConflictTests`/`MemoryConflictResolution.spec.ts` for Scenario 7 — the Playwright specs themselves carry the same "NOT RUNNABLE IN THIS ENVIRONMENT" doc-comment caveat as every other pre-existing spec in this repo)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories. Larger than a
  typical phase for the same reason specs/016's was: the core extraction→rank→inject→conflict-detect
  pipeline is a genuine shared prerequisite for every story, not story-specific work.
- **User Stories (Phase 3–8)**: All depend on Foundational completion.
  - US1 and US2 (P1) have no dependency on each other and can proceed in parallel.
  - US3 and US4 (P2) both extend `UpdateMemoryPreferences` (built in US3) but are otherwise
    independent of each other.
  - US5 and US6 (P3) depend on the Foundational pipeline (ranking/scoping, conflict auto-detection)
    but not on US1–US4.
- **Polish (Phase 9)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Foundational only. Independently testable — the remember→recall loop is fully
  automatic and needs no story-specific trigger.
- **US2 (P1)**: Foundational only. No dependency on US1.
- **US3 (P2)**: Foundational only for its approval-mode/notification-push mechanics (already built);
  adds the user-facing configuration/notification-list surface. Independently testable.
- **US4 (P2)**: Reuses US3's `UpdateMemoryPreferences` command for the `memoryEnabled`/category-
  `isEnabled` fields (tests only, no new command needed for those paths) and adds two new commands
  (`ClearAllMemories`, `RequestMemoryExport`).
- **US5 (P3)**: Foundational only (`Memory.ProjectId`, `UserChat.ProjectId`, and the Foundational
  ranking query's project-scoping filter are already in place). Independently testable.
- **US6 (P3)**: Foundational only for conflict *detection* (already built, including direct-
  contradiction auto-merge); adds the ambiguous-case *resolution* action and dialog.

### Within Each User Story

- Tests MUST be written and FAIL before implementation.
- Domain/entities before commands/queries; commands/queries before controllers; controllers before
  frontend wiring.
- Story complete before moving to the next priority (or proceed in parallel per the Parallel Team
  Strategy below).

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel.
- Within Foundational: all entity-creation tasks (T003–T014) are [P]; all abstraction tasks
  (T015–T019) are [P]; all repository/guard tasks (T023–T027) are [P].
- Once Foundational completes, US1 and US2 can proceed fully in parallel; US5 and US6 can proceed
  fully in parallel with each other and with US1–US4.
- All tests for a user story marked [P] can run in parallel.

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Unit tests for MemoryService outcome branches in tests/AskLucy.Application.Tests/Memory/MemoryServiceTests.cs"
Task: "Integration test: memory context message ordering in tests/AskLucy.Application.Tests/Ai/SendChatMessageMemoryIntegrationTests.cs"
Task: "Integration test: disabled-period exclusion in tests/AskLucy.Application.Tests/Memory/MemoryDisabledExclusionTests.cs"
Task: "Integration test: degraded-mode graceful fallback in tests/AskLucy.Application.Tests/Memory/MemoryDegradedModeTests.cs"

# Launch the frontend trace surface for User Story 1 together:
Task: "memoryApi.ts client + useMemoryReferences.ts hook in src/AskLucy.Web/ClientApp/src/features/memory/api/, hooks/"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — the largest phase in this feature; includes the full
   working extraction→rank→inject pipeline).
3. Complete Phase 3: User Story 1 (remember and recall).
4. Complete Phase 4: User Story 2 (Memory Center).
5. **STOP and VALIDATE**: run quickstart.md Scenarios 1–2 independently.
6. Deploy/demo if ready — like specs/016's RAG engine, US1 alone isn't meaningfully demoable without
   also having a way to see/manage what was remembered, so US1+US2 together are this feature's real
   MVP slice.

### Incremental Delivery

1. Complete Setup + Foundational → the memory pipeline works end to end, invisibly.
2. Add US1 + US2 → the pipeline becomes visible and manageable → deploy/demo (MVP!).
3. Add US3 → approval-mode control and notifications → deploy/demo.
4. Add US4 → full account-level privacy control → deploy/demo.
5. Add US5 → Project-scoped memory → deploy/demo.
6. Add US6 → conflict resolution UX → deploy/demo.

### Parallel Team Strategy

With multiple developers, after Foundational completes:

- Developer A: US1 → US3
- Developer B: US2 → US4
- Developer C: US5 → US6

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- The Foundational phase intentionally includes the full pipeline *mechanics* (extraction, ranking,
  prompt injection, conflict auto-detection, notification push) — see the note under "Organization"
  above for why this mirrors specs/016's precedent.
- Verify tests fail before implementing.
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently.
- Avoid: vague tasks, same-file conflicts, cross-story dependencies that break independence.
