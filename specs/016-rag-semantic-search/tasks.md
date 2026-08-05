---

description: "Task list for RAG & Semantic Search Engine"

---

# Tasks: RAG & Semantic Search Engine

**Input**: Design documents from `/specs/016-rag-semantic-search/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (§10 Testing Standards, non-negotiable) requires unit,
integration, and Playwright E2E coverage for new/changed behavior — test tasks are not optional
here.

**Organization**: Tasks are grouped by user story (spec.md priorities: US1/US2 = P1, US3/US4 = P2,
US5/US6 = P3, US7 = P4) so each story is independently implementable, testable, and demoable.
Unlike a typical feature, the core chunk→embed→store→search pipeline mechanics are placed in
**Foundational**, not inside US5/US6 ("Indexing") — US1 (chat) and US2 (search) cannot be
meaningfully tested at all without content already being searchable (spec.md's own US2
"Independent Test" assumes "indexing a knowledge base with known content" as a precondition), so
the pipeline's *mechanics* are shared infrastructure every story depends on, while US5 adds the
*automatic trigger* and US6 adds the *manual trigger, concurrency guard, and retry UX* around that
already-working core (mirrors constitution §9's own required pipeline staging).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Maps the task to US1–US7 from spec.md
- All descriptions include exact file paths

## Path Conventions

Existing single-solution web app (constitution §3): `src/AskLucy.Domain`,
`src/AskLucy.Application`, `src/AskLucy.Infrastructure`, `src/AskLucy.Persistence`,
`src/AskLucy.Web` (API + `ClientApp/` React SPA), `tests/AskLucy.*.Tests`. This feature adds a new,
independent `Retrieval` module at every layer (research.md Decision 1) and extends four existing
entities (`KnowledgeBase`, `KnowledgeBaseDocument`, `UserChat`, `Chats.Citation`) — no new
top-level project.

---

## Phase 1: Setup

**Purpose**: The one genuinely new dependency and the database/platform capabilities this feature
needs before any domain code is written (plan.md Technical Context; research.md Decisions 3, 5, 6,
12).

- [X] T001 [P] Add `Microsoft.ML.OnnxRuntime` package reference to `src/AskLucy.Infrastructure/AskLucy.Infrastructure.csproj`; document where the local embedding model's `.onnx`/tokenizer assets are staged for the runtime to load (research.md Decision 5)
- [X] T002 [P] Add a migration/setup script enabling a SQL Server full-text catalog on the target database and confirming the native `vector` type / `VECTOR_DISTANCE` function are available (research.md Decisions 3, 6) — flag as an ops prerequisite (SQL Server 2025+/Azure SQL) if the local/target instance predates it
- [X] T003 Register `retrieval-search-endpoints` (generous, matches `knowledge-base-endpoints`) and `retrieval-indexing-endpoints` (tighter, matches `document-upload-chunk-endpoints`) rate-limit policies in `src/AskLucy.Web/Program.cs` (research.md Decision 12)

**Checkpoint**: Solution builds with the new dependency restored; full-text catalog and native
vector type confirmed available. No domain code exists yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain entities, shared abstractions, persistence configuration/migration,
repositories, and the core chunk→embed→store→search pipeline mechanics every user story depends
on.

**⚠️ CRITICAL**: No user story task may begin until this phase is complete and the solution builds
with the new migration applied.

### Domain entities (data-model.md "New Entities")

- [X] T004 [P] Create `DocumentChunk` entity — `KnowledgeBaseId`/`KnowledgeBaseDocumentId`/`DocumentId`/`DocumentVersionId`/`ChunkingStrategy`/`Content`/`ContentHash`/`TokenCount`/`CharacterCount`/`Language`/`PageNumber`/`Section`/`Heading`/`Position` in `src/AskLucy.Domain/Retrieval/DocumentChunk.cs`
- [X] T005 [P] Create `Embedding` entity — `DocumentChunkId`/`EmbeddingProviderId`/`Vector`/`IsCurrent`, immutable-after-create in `src/AskLucy.Domain/Retrieval/Embedding.cs`
- [X] T006 [P] Create `EmbeddingProvider` entity — `Vendor`/`ModelKey`/`Dimensionality`/`HostingType`/`IsDefault`/`IsActive` in `src/AskLucy.Domain/Retrieval/EmbeddingProvider.cs`
- [X] T007 [P] Create `IndexingJob` entity + lifecycle methods (`Queued`→`InProgress`→`Completed`/`Failed`→retry) in `src/AskLucy.Domain/Retrieval/IndexingJob.cs`
- [X] T008 [P] Create `IndexingLog` entity (append-only) in `src/AskLucy.Domain/Retrieval/IndexingLog.cs`
- [X] T009 [P] Create `RetrievalHistory` entity (append-only) in `src/AskLucy.Domain/Retrieval/RetrievalHistory.cs`
- [X] T010 [P] Create `RetrievalResult` entity (append-only) in `src/AskLucy.Domain/Retrieval/RetrievalResult.cs`
- [X] T011 [P] Create `SearchHistory` entity (append-only) in `src/AskLucy.Domain/Retrieval/SearchHistory.cs`
- [X] T012 [P] Create `ChunkStatistics` entity in `src/AskLucy.Domain/Retrieval/ChunkStatistics.cs`
- [X] T013 [P] Create `SearchAnalytics` entity in `src/AskLucy.Domain/Retrieval/SearchAnalytics.cs`
- [X] T014 [P] Create `ConversationKnowledgeBase` join entity — unique on (`UserChatId`,`KnowledgeBaseId`) in `src/AskLucy.Domain/Retrieval/ConversationKnowledgeBase.cs`

### Extended entities (data-model.md "Extended Entities")

- [X] T015 [P] Extend `KnowledgeBase` with `ChunkingStrategy`/`EmbeddingProviderId`/`RequiresDataResidency`/`IndexStatus`/`LastIndexedAtUtc` fields and index-status transition methods in `src/AskLucy.Domain/KnowledgeBases/KnowledgeBase.cs` (research.md Decision 11)
- [X] T016 [P] Extend `KnowledgeBaseDocument` with a nullable `DocumentId` link field + setter in `src/AskLucy.Domain/KnowledgeBases/KnowledgeBaseDocument.cs` (research.md Decision 2)
- [X] T017 [P] Extend `UserChat` with `RetrievalSearchMode`/`RetrievalTopK`/`RetrievalSimilarityThreshold`/`RetrievalMaxContextTokens` nullable fields + an update method in `src/AskLucy.Domain/Chats/UserChat.cs` (research.md Decision 10)
- [X] T018 [P] Extend `Citation` with `DocumentChunkId`/`KnowledgeBaseId`/`DocumentId`/`DocumentVersionId`/`PageNumber`/`Section` fields and a RAG-specific `CreateFromChunk` factory overload in `src/AskLucy.Domain/Chats/Citation.cs` (research.md Decision 9)

### Shared abstractions (Application)

- [X] T019 [P] Create `IEmbeddingService`/`IEmbeddingServiceResolver` abstractions in `src/AskLucy.Application/Abstractions/IEmbeddingService.cs` (research.md Decision 5)
- [X] T020 [P] Create `IVectorStore` abstraction (upsert, delete, cosine nearest-neighbor query) in `src/AskLucy.Application/Abstractions/IVectorStore.cs` (research.md Decision 3)
- [X] T021 [P] Create `IChunkingService`/`IChunkingStrategy` abstractions in `src/AskLucy.Application/Abstractions/IChunkingService.cs` (research.md Decision 4)
- [X] T022 [P] Create `IRagService` abstraction — `RetrieveContextAsync` returning a `RagRetrievalOutcome` (`Grounded`/`NoRelevantContent`/`Unavailable`) in `src/AskLucy.Application/Abstractions/IRagService.cs` (research.md Decision 8)
- [X] T023 [P] Create `IIndexingOrchestrator` abstraction — indexes one `KnowledgeBaseDocument` end-to-end in `src/AskLucy.Application/Abstractions/IIndexingOrchestrator.cs` (research.md Decision 2)
- [X] T024 [P] Create `IRetrievalIndexingNotifier` abstraction (mirrors `IProcessingNotifier`) in `src/AskLucy.Application/Abstractions/IRetrievalIndexingNotifier.cs` (research.md Decision 7)

### Persistence

- [X] T025 Create EF Core Fluent API configurations for all 11 new `Retrieval` entities — native `vector(n)` column mapping (no vector index — see research.md Decision 3: on this non-Azure SQL Server target, `CREATE VECTOR INDEX` produces the pre-Azure/Fabric format, which makes the table read-only for DML, incompatible with FR-010/FR-011's incremental-indexing requirement; searches scan via `VECTOR_DISTANCE` directly), full-text index on `DocumentChunk.Content`, append-only entities with no soft-delete filter, indexes on every FK/filter/sort column (constitution §5) — plus `DbSet<T>` registrations on `AskLucyDbContext` in `src/AskLucy.Persistence/Configurations/Retrieval/*.cs` (depends on T004–T014)
- [X] T026 [P] Extend EF configurations for the `KnowledgeBase`/`KnowledgeBaseDocument`/`Chats.UserChat`/`Chats.Citation` additive columns in `src/AskLucy.Persistence/Configurations/KnowledgeBases/*.cs`, `src/AskLucy.Persistence/Configurations/Chats/*.cs` (depends on T015–T018)
- [X] T027 Generate the EF Core migration `AddRetrievalEngine` (new tables, additive columns, full-text catalog/index; no vector index DDL — see T025) via `dotnet ef migrations add AddRetrievalEngine -p src/AskLucy.Persistence -s src/AskLucy.Web`; verify `Down()` is reversible and `dotnet ef database update` succeeds against a SQL Server instance supporting native vector/full-text (depends on T025, T026, T002)
- [X] T028 [P] Seed default `EmbeddingProvider` rows — OpenAI `text-embedding-3-small` (`Cloud`, `IsDefault: true`) and the local ONNX model (`Local`, `IsDefault: true`) — via `migrationBuilder.InsertData` (depends on T027)

### Repositories

- [X] T029 [P] Create `IDocumentChunkRepository`/`DocumentChunkRepository` (KB-scoped queries, content-hash lookup for FR-005) in `src/AskLucy.Application/Abstractions/IDocumentChunkRepository.cs`, `src/AskLucy.Persistence/Repositories/DocumentChunkRepository.cs` (depends on T027)
- [X] T030 [P] Create `IEmbeddingRepository`/`EmbeddingRepository` (current-embedding lookup, mark-superseded on re-embed) in `src/AskLucy.Application/Abstractions/IEmbeddingRepository.cs`, `src/AskLucy.Persistence/Repositories/EmbeddingRepository.cs` (depends on T027)
- [X] T031 [P] Create `IEmbeddingProviderRepository`/`EmbeddingProviderRepository` in `src/AskLucy.Application/Abstractions/IEmbeddingProviderRepository.cs`, `src/AskLucy.Persistence/Repositories/EmbeddingProviderRepository.cs` (depends on T027)
- [X] T032 [P] Create `IIndexingJobRepository`/`IndexingJobRepository` (concurrency-guarded create — no second `Queued`/`InProgress` job per knowledge base, §5 Concurrency) in `src/AskLucy.Application/Abstractions/IIndexingJobRepository.cs`, `src/AskLucy.Persistence/Repositories/IndexingJobRepository.cs` (depends on T027)
- [X] T033 [P] Create `IConversationKnowledgeBaseRepository`/`ConversationKnowledgeBaseRepository` in `src/AskLucy.Application/Abstractions/IConversationKnowledgeBaseRepository.cs`, `src/AskLucy.Persistence/Repositories/ConversationKnowledgeBaseRepository.cs` (depends on T027)
- [X] T033a [P] Create `RetrievalOwnershipGuard` (mirrors `KnowledgeBaseOwnershipGuard` — throws `KeyNotFoundException` when the caller doesn't own the target knowledge base, so denial is indistinguishable from not-found, FR-048) in `src/AskLucy.Application/Retrieval/Authorization/RetrievalOwnershipGuard.cs` (depends on T015) — added during `/speckit-analyze` remediation (finding I2): every Retrieval mutation command below must apply this guard, not defer enforcement to Polish

### Core pipeline mechanics (Infrastructure)

- [X] T034 [P] Implement the seven non-semantic `IChunkingStrategy` strategies (`FixedSize`, `Recursive`, `Paragraph`, `Sentence`, `Markdown`, `Heading`, `Table`) reading `DocumentVersion.ExtractedText`/`ExtractedStructureJson` in `src/AskLucy.Infrastructure/Retrieval/Chunking/*.cs` (depends on T021)
- [X] T035 Implement `SemanticChunkingStrategy` (embedding-similarity sentence-boundary grouping, calls `IEmbeddingService`) in `src/AskLucy.Infrastructure/Retrieval/Chunking/SemanticChunkingStrategy.cs` (depends on T021, T037)
- [X] T036 [P] Implement `OpenAiEmbeddingProvider` (`IEmbeddingService`, `Cloud`) plus `OpenAiEmbeddingOptions`, reusing the existing `AiCredentialProtector` pattern in `src/AskLucy.Infrastructure/Retrieval/Embeddings/OpenAiEmbeddingProvider.cs`, `OpenAiEmbeddingOptions.cs` (depends on T019, T001)
- [X] T037 [P] Implement `OnnxLocalEmbeddingProvider` (`IEmbeddingService`, `Local`, in-process, mirrors `WhisperLocalTranscriptionProvider`'s shape) in `src/AskLucy.Infrastructure/Retrieval/Embeddings/OnnxLocalEmbeddingProvider.cs` (depends on T019, T001)
- [X] T038 Implement `EmbeddingServiceResolver` (`IEmbeddingServiceResolver`, keyed by `EmbeddingProvider.HostingType`) in `src/AskLucy.Infrastructure/Retrieval/Embeddings/EmbeddingServiceResolver.cs` (depends on T036, T037, T031)
- [X] T039 Implement `SqlServerVectorStore` (`IVectorStore` — upsert/delete/cosine nearest-neighbor via `EF.Functions.VectorDistance` against the native vector column, research.md Decision 3) in `src/AskLucy.Infrastructure/Retrieval/Vector/SqlServerVectorStore.cs` (depends on T020, T029, T030)
- [X] T040 Implement keyword relevance search (`CONTAINSTABLE`/`FREETEXTTABLE` over `DocumentChunk.Content`'s full-text index, research.md Decision 6) in `src/AskLucy.Infrastructure/Retrieval/Search/FullTextKeywordSearch.cs` (depends on T029, T002)

### Core search + indexing orchestration (Application)

- [X] T041 Create `SemanticSearchQuery`/handler (calls `IVectorStore`; applies knowledge-base/document/language/date/version/metadata filters, FR-017, FR-022, FR-026) in `src/AskLucy.Application/Retrieval/Queries/SemanticSearch/` (depends on T039)
- [X] T042 Create `KeywordSearchQuery`/handler (calls `FullTextKeywordSearch`, FR-018, FR-027) in `src/AskLucy.Application/Retrieval/Queries/KeywordSearch/` (depends on T040)
- [X] T043 Create `HybridSearchQuery`/handler (blends semantic+keyword scores in application code, so the blend formula is unit-testable independent of the database — research.md Decision 6; FR-019, FR-027, FR-029 ranking-factor disclosure) in `src/AskLucy.Application/Retrieval/Queries/HybridSearch/` (depends on T041, T042)
- [X] T044 Implement `IndexingOrchestrator` (`IIndexingOrchestrator` — for a `KnowledgeBaseDocument`: create `Document`/`DocumentVersion` via the Documents pipeline if `DocumentId` is null, run the knowledge base's chunking strategy, generate embeddings in batches (FR-050), write to `IVectorStore`, skip unchanged content by `ContentHash`, FR-005) in `src/AskLucy.Application/Retrieval/Indexing/IndexingOrchestrator.cs` (depends on T023, T034–T040, T016)

### Real-time hub

- [X] T045 Create `RetrievalIndexingHub` (mirrors `DocumentProcessingHub`'s per-user-group join), map `/hubs/retrieval-indexing` in `Program.cs`, and implement `RetrievalIndexingNotifier` (`IRetrievalIndexingNotifier`) in `src/AskLucy.Infrastructure/Retrieval/RetrievalIndexingHub.cs`, `RetrievalIndexingNotifier.cs` (depends on T024, T032)

**Checkpoint**: Solution builds; migration applies; the core pipeline (chunk → embed → store →
search) is callable end-to-end programmatically, but nothing is yet exposed to a real user or
triggered automatically — user story work begins next.

---

## Phase 3: User Story 1 - Chat with your documents and get cited answers (Priority: P1) 🎯 MVP

**Goal**: A conversation with one or more knowledge bases attached gets grounded, cited answers;
one with none attached is unaffected; a retrieval outage degrades gracefully with a visible,
non-silent error instead of a silent or blocked failure.

**Independent Test**: Attach a knowledge base to a conversation, ask a question answerable from
its content, confirm a grounded response with a citation to the correct document/page/section
(quickstart.md Scenario 1).

### Tests for User Story 1

- [X] T046 [P] [US1] Unit tests for `IRagService`'s `Grounded`/`NoRelevantContent`/`Unavailable` outcome branches with faked search/embedding/vector-store dependencies in `tests/AskLucy.Application.Tests/Retrieval/RagServiceTests.cs`
- [X] T047 [P] [US1] Integration test: `SendChatMessageCommandHandler` augments the prompt and attaches citations when an attached knowledge base has relevant content; no citations and no retrieval when none is attached (US1 AC1–AC3) in `tests/AskLucy.Application.Tests/Ai/SendChatMessageRagIntegrationTests.cs`
- [X] T048 [P] [US1] Integration test: no relevant content found states so explicitly rather than an unsupported grounded-looking answer (US1 AC4, FR-025) in `tests/AskLucy.Application.Tests/Ai/SendChatMessageNoRelevantContentTests.cs`
- [X] T049 [P] [US1] Integration test: retrieval forced unavailable mid-message still returns a response (ungrounded, no citations) plus a separate non-silent retrieval error — the message is never blocked (US1 AC6, FR-037a, research.md Decision 8) in `tests/AskLucy.Application.Tests/Ai/SendChatMessageRetrievalOutageTests.cs`
- [X] T050 [P] [US1] Playwright E2E: attach a knowledge base, ask a question, see a cited response, open the citation to the source page/section with the passage highlighted (quickstart.md Scenario 1) in `tests/AskLucy.E2E.Tests/ChatWithCitations.spec.ts`

### Implementation for User Story 1

- [X] T051 [US1] Implement `RagService` (`IRagService`) — runs `HybridSearchQuery` (or the conversation's configured mode once US3 exists; hybrid/system-default until then), applies top-K/threshold/max-context-token limits, returns the `RagRetrievalOutcome` in `src/AskLucy.Application/Retrieval/RagService.cs` (depends on T022, T043)
- [X] T052 [US1] Full-replace attach/detach of a conversation's knowledge bases — implemented as one `UpdateConversationKnowledgeBasesCommand` (not two separate Attach/Detach commands: the actual documented contract, `PUT /api/v1/chats/{id}/knowledge-bases`, and `IConversationKnowledgeBaseRepository`'s `Add`/`RemoveExceptAsync` shape are both a single full-replace operation), ownership-guarded via `ChatOwnershipGuard` + `IKnowledgeBaseRepository.ResolveOwnedIdsAsync` (a `DomainRuleViolationException`/400 for an unowned id, not `RetrievalOwnershipGuard`'s 404-shaped "look like not found" — the caller is explicitly naming these ids in their own request) in `src/AskLucy.Application/Retrieval/Commands/UpdateConversationKnowledgeBases/`, plus `GetConversationKnowledgeBasesQuery` in `src/AskLucy.Application/Retrieval/Queries/GetConversationKnowledgeBases/` (depends on T033, T033a)
- [X] T053 [US1] Integrate `IRagService` into `SendChatMessageCommandHandler` — retrieve before building the message list (only when the conversation has attached knowledge bases); citations ride the new `ChatStreamChunk.RetrievalOutcome` on the stream's final chunk (added `Guid ChatId` to `SendChatMessageCommand`/`StreamVoiceReplyCommand`) rather than being attached directly here, since persistence is a controller-composed concern (`AppendMessageCommand`, extended with RAG citation fields) — `AiController.Chat` attaches them via `Citation.AddCitationFromChunk`; surfaces `Unavailable` as a non-silent warning (`retrievalError`) alongside the still-generated response (research.md Decision 8) in `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs`, `src/AskLucy.Web/Controllers/v1/AiController.cs` (depends on T051, T018)
- [X] T054 [US1] `ConversationKnowledgeBasesController` — attach/detach (full-replace) + get endpoints (contracts/conversation-retrieval-api.md) in `src/AskLucy.Web/Controllers/v1/ConversationKnowledgeBasesController.cs` (depends on T052)
- [X] T055 [P] [US1] Frontend: `KnowledgeBaseAttachmentPicker.tsx` (attach/detach knowledge bases on a conversation) in `src/AskLucy.Web/ClientApp/src/features/retrieval/components/KnowledgeBaseAttachmentPicker.tsx`
- [X] T056 [P] [US1] Frontend: `CitationBadge.tsx` + `CitationViewer.tsx` (source document at cited page/section, highlighted passage) in `src/AskLucy.Web/ClientApp/src/features/retrieval/components/CitationBadge.tsx`, `CitationViewer.tsx`
- [X] T057 [US1] Wire citation display and a "not grounded"/retrieval-error indicator into the chat message renderer in `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx` (depends on T056)
- [X] T058 [US1] Frontend: `retrievalApi.ts` client + `useConversationKnowledgeBases.ts` hook in `src/AskLucy.Web/ClientApp/src/features/retrieval/api/retrievalApi.ts`, `hooks/useConversationKnowledgeBases.ts` (depends on T054)

**Checkpoint**: User Story 1 is independently functional — chat with cited, grounded answers works
against an already-indexed knowledge base (indexed via the foundational orchestrator directly for
test/demo purposes ahead of US5/US6's user-facing triggers).

---

## Phase 4: User Story 2 - Search a knowledge base directly (Priority: P1)

**Goal**: Semantic, keyword, and hybrid search across one, many, or explicitly excluded knowledge
bases, with correct scoping and a clear empty-results state.

**Independent Test**: Run a semantic, keyword, and hybrid search against an indexed knowledge
base; confirm each mode returns correctly ranked, attributed results (quickstart.md Scenario 2).

### Tests for User Story 2

- [ ] T059 [P] [US2] Integration tests: `SemanticSearchQuery`/`KeywordSearchQuery`/`HybridSearchQuery` ranking correctness against seeded chunks/embeddings in `tests/AskLucy.Application.Tests/Retrieval/SearchQueryTests.cs`
- [ ] T060 [P] [US2] Integration test: scoping to a single knowledge base, excluding a knowledge base, and an empty-results-below-threshold response (FR-021, FR-025, US2 AC4–AC6) in `tests/AskLucy.Application.Tests/Retrieval/SearchScopingTests.cs`
- [ ] T061 [P] [US2] Integration test: a search naming a knowledge base the caller does not own excludes it entirely rather than erroring or leaking (FR-045, FR-048) in `tests/AskLucy.Application.Tests/Retrieval/SearchAuthorizationTests.cs`
- [ ] T062 [P] [US2] Playwright E2E: run semantic/keyword/hybrid search, scope/exclude knowledge bases, open a citation from a result (quickstart.md Scenario 2) in `tests/AskLucy.E2E.Tests/DirectSearch.spec.ts`

### Implementation for User Story 2

- [ ] T063 [US2] `RetrievalSearchController` — `POST /api/v1/retrieval/search`, citation lookup, search history (contracts/retrieval-search-api.md) in `src/AskLucy.Web/Controllers/v1/RetrievalSearchController.cs` (depends on T041–T043)
- [ ] T064 [US2] Record a `SearchHistory` row on every direct search request (FR-043) in `src/AskLucy.Application/Retrieval/Queries/SemanticSearch/`, `KeywordSearch/`, `HybridSearch/` handlers (depends on T011, T063)
- [ ] T065 [US2] `GetCitation` query (contracts/retrieval-search-api.md) in `src/AskLucy.Application/Retrieval/Queries/GetCitation/` (depends on T018)
- [ ] T066 [US2] `GetSearchHistory` query, cursor-paginated (FR-043, US7 AC4 reuse) in `src/AskLucy.Application/Retrieval/Queries/GetSearchHistory/` (depends on T011)
- [ ] T067 [P] [US2] Frontend: `SearchInterface.tsx` (mode selector, knowledge-base selector/exclude, filters) + `SearchResultsList.tsx` (score/rank display, highlighted excerpt) in `src/AskLucy.Web/ClientApp/src/features/retrieval/components/SearchInterface.tsx`, `SearchResultsList.tsx`
- [ ] T068 [US2] `RetrievalSearchPage.tsx`; wire the `/retrieval/search` route and a navigation entry in `src/AskLucy.Web/ClientApp/src/features/retrieval/pages/RetrievalSearchPage.tsx`, `src/AskLucy.Web/ClientApp/src/routes/router.tsx` (depends on T067)

**Checkpoint**: User Stories 1 + 2 together form a real MVP — chat-with-documents and standalone
search both work end to end.

---

## Phase 5: User Story 3 - Control how retrieval works (Priority: P2)

**Goal**: Per-conversation retrieval depth, similarity threshold, context-token budget, and
search-mode overrides that apply to future messages only.

**Independent Test**: Change a conversation's retrieval depth/threshold/mode/token-budget and
confirm the next question reflects the change while earlier messages remain unaffected
(quickstart.md Scenario 3).

### Tests for User Story 3

- [ ] T069 [P] [US3] Integration tests: `UpdateConversationRetrievalSettings` persists overrides; `null` reverts to the system default; a later settings change never alters prior messages' `RetrievalHistory`/citations (FR-037, US3 AC5) in `tests/AskLucy.Application.Tests/Retrieval/RetrievalSettingsTests.cs`
- [ ] T070 [P] [US3] Playwright E2E: change top-K/threshold/mode/token-budget for a conversation and confirm only subsequent messages reflect it (quickstart.md Scenario 3) in `tests/AskLucy.E2E.Tests/RetrievalSettings.spec.ts`

### Implementation for User Story 3

- [ ] T071 [US3] `UpdateConversationRetrievalSettings` command + `GetConversationRetrievalSettings` query (each field flagged `isSystemDefault`), ownership-guarded via `RetrievalOwnershipGuard` in `src/AskLucy.Application/Retrieval/Commands/UpdateConversationRetrievalSettings/`, `Queries/GetConversationRetrievalSettings/` (depends on T017, T033a)
- [ ] T072 [US3] Extend `ConversationKnowledgeBasesController` with retrieval-settings endpoints (contracts/conversation-retrieval-api.md) in `src/AskLucy.Web/Controllers/v1/ConversationKnowledgeBasesController.cs` (depends on T071)
- [ ] T073 [US3] Wire `RagService` to read the conversation's effective retrieval settings, falling back to system defaults when null (FR-020, FR-023, FR-024) in `src/AskLucy.Application/Retrieval/RagService.cs` (depends on T051, T071)
- [ ] T074 [P] [US3] Frontend: `RetrievalSettingsPanel.tsx` (mode/top-K/threshold/token-budget controls with `isSystemDefault` indicators) in `src/AskLucy.Web/ClientApp/src/features/retrieval/components/RetrievalSettingsPanel.tsx`
- [ ] T075 [US3] Wire `RetrievalSettingsPanel` into the conversation settings UI in `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatSettingsDrawer.tsx` (depends on T074)

**Checkpoint**: Retrieval is fully configurable per conversation without affecting search history.

---

## Phase 6: User Story 4 - See why a result was selected (Priority: P2)

**Goal**: Every search result and citation discloses its similarity score, keyword-match
contribution, and any applied metadata boosts.

**Independent Test**: Inspect a hybrid result's score breakdown and a boosted result's disclosed
boost factor (quickstart.md Scenario 4).

### Tests for User Story 4

- [ ] T076 [P] [US4] Unit test: `HybridSearchQuery`'s blended-score/boost-factor output is internally consistent with the displayed rank ordering (FR-029) in `tests/AskLucy.Application.Tests/Retrieval/RankingTransparencyTests.cs`
- [ ] T077 [P] [US4] Playwright E2E: inspect a result's score breakdown and a boosted result's disclosed boost factor (quickstart.md Scenario 4) in `tests/AskLucy.E2E.Tests/RankingTransparency.spec.ts`

### Implementation for User Story 4

- [ ] T078 [US4] `UpdateKnowledgeBaseRankingBoosts` command (recency/category metadata boost configuration, FR-028) in `src/AskLucy.Application/Retrieval/Commands/UpdateKnowledgeBaseRankingBoosts/` (depends on T015)
- [ ] T079 [US4] Apply configured boosts in `HybridSearchQuery`/`SemanticSearchQuery` scoring in `src/AskLucy.Application/Retrieval/Queries/HybridSearch/`, `SemanticSearch/` (depends on T043, T078)
- [ ] T080 [P] [US4] Frontend: `ResultScoreBreakdown.tsx` (semantic/keyword/boost contribution display) in `src/AskLucy.Web/ClientApp/src/features/retrieval/components/ResultScoreBreakdown.tsx`
- [ ] T081 [US4] Wire `ResultScoreBreakdown` into `SearchResultsList.tsx` and `CitationBadge.tsx` (depends on T080, T067, T056)

**Checkpoint**: Every result's ranking rationale is visible and consistent.

---

## Phase 7: User Story 5 - Keep knowledge bases automatically searchable (Priority: P3)

**Goal**: Once a knowledge base's initial index has run, new/replaced/deleted documents keep the
index current automatically, with accurate, visible index status throughout.

**Independent Test**: Upload a new document to an already-indexed knowledge base and confirm it
becomes searchable without any manual action; replace and delete a document and confirm the index
updates accordingly (quickstart.md Scenario 5).

### Tests for User Story 5

- [ ] T082 [P] [US5] Integration test: a document reaching `Completed` in an already-first-indexed knowledge base automatically enqueues an `IndexingJob` and becomes searchable (FR-010, US5 AC1) in `tests/AskLucy.Application.Tests/Retrieval/AutomaticIndexingTests.cs`
- [ ] T083 [P] [US5] Integration test: a new document version supersedes the prior version's chunks in default search (US5 AC2, FR-016) in `tests/AskLucy.Application.Tests/Retrieval/VersionSupersessionTests.cs`
- [ ] T084 [P] [US5] Integration test: deleting/archiving a document excludes its chunks from search (US5 AC3) in `tests/AskLucy.Application.Tests/Retrieval/DeletionExclusionTests.cs`
- [ ] T085 [P] [US5] Playwright E2E: upload to an indexed knowledge base and confirm automatic searchability; replace/delete and confirm exclusion; check index status throughout (quickstart.md Scenario 5) in `tests/AskLucy.E2E.Tests/AutomaticIndexing.spec.ts`

### Implementation for User Story 5

- [ ] T086 [US5] Hook the Document Intelligence Pipeline's completion event (`Document.ProcessingStatus` → `Completed`) to enqueue an `IncrementalReindex`-scoped `IndexingJob` when the owning knowledge base's `IndexStatus` is not `NotIndexed` (FR-010, FR-010a's "only after first index" boundary) in `src/AskLucy.Application/Documents/Processing/DocumentProcessingPipeline.cs`, `src/AskLucy.Application/Retrieval/Events/` (depends on T044)
- [ ] T087 [US5] Hangfire job wiring: `IndexingJob` → `IndexingOrchestrator`, with `Chunking`→`EmbeddingGeneration`→`VectorWrite`→`Cleanup` stages persisted as `IndexingLog` rows and pushed via `RetrievalIndexingNotifier` in `src/AskLucy.Infrastructure/Retrieval/Jobs/IndexingJobRunner.cs` (depends on T044, T045, T032)
- [ ] T088 [US5] Wire `KnowledgeBase.IndexStatus` transitions to `IndexingJob` lifecycle (`NotIndexed`→`Indexing`→`Indexed`/`PartiallyIndexed`/`Failed`) in `src/AskLucy.Application/Retrieval/Indexing/IndexingOrchestrator.cs`, `src/AskLucy.Domain/KnowledgeBases/KnowledgeBase.cs` (depends on T087, T015)
- [ ] T089 [US5] `GetIndexStatus` query (contracts/retrieval-indexing-api.md) in `src/AskLucy.Application/Retrieval/Queries/GetIndexStatus/` (depends on T088)
- [ ] T090 [P] [US5] Frontend: `IndexStatusBadge.tsx` + `useRetrievalIndexingHub.ts` (SignalR + 5s poll fallback, mirrors `useDocumentProcessingHub`) in `src/AskLucy.Web/ClientApp/src/features/retrieval/components/IndexStatusBadge.tsx`, `hooks/useRetrievalIndexingHub.ts`
- [ ] T091 [US5] Wire `IndexStatusBadge` into the knowledge base workspace's card/detail view in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/components/KnowledgeBaseCard.tsx` (depends on T090)

**Checkpoint**: Knowledge bases stay searchable automatically once their initial index has run.

---

## Phase 8: User Story 6 - Manually manage indexing (Priority: P3)

**Goal**: Owners trigger a knowledge base's initial index, full/incremental reindex, or a single
document-version reindex, and can retry a failed indexing job.

**Independent Test**: Trigger an initial index on a never-indexed knowledge base (confirming its
existing documents were NOT already searchable — the opt-in backfill clarification), then a full
reindex, an incremental reindex, and a forced-failure retry (quickstart.md Scenario 6).

### Tests for User Story 6

- [ ] T092 [P] [US6] Integration test: initial-index requires `KnowledgeBase.Status = Active` (`409 NotActive`) and `IndexStatus` of `NotIndexed`/`Failed` (`409 AlreadyIndexing` on a concurrent trigger, §5 Concurrency) in `tests/AskLucy.Application.Tests/Retrieval/InitialIndexGuardTests.cs`
- [ ] T093 [P] [US6] Integration test: `Full` reindex re-processes all chunks; `Incremental` reindex only processes new/changed content by `ContentHash` (FR-049) in `tests/AskLucy.Application.Tests/Retrieval/ReindexModeTests.cs`
- [ ] T094 [P] [US6] Integration test: a single document-version reindex is independent of the knowledge-base-level concurrency guard (FR-012) in `tests/AskLucy.Application.Tests/Retrieval/VersionReindexTests.cs`
- [ ] T095 [P] [US6] Integration test: retry returns `409` unless the current job is `Failed`, mirroring `DocumentProcessingController`'s retry guard (FR-013, FR-040) in `tests/AskLucy.Application.Tests/Retrieval/IndexingRetryTests.cs`
- [ ] T096 [P] [US6] Playwright E2E: trigger initial index (confirms opt-in backfill), full reindex after a chunking-strategy change, incremental reindex, and a forced-failure retry (quickstart.md Scenario 6) in `tests/AskLucy.E2E.Tests/ManualIndexing.spec.ts`
- [ ] T092a [P] [US6] Integration test: `UpdateKnowledgeBaseRetrievalSettings` persists `ChunkingStrategy`/`EmbeddingProviderId`/`RequiresDataResidency`; rejects assigning a `Cloud` provider when `RequiresDataResidency=true`; changing `ChunkingStrategy` or `EmbeddingProviderId` auto-enqueues a `FullReindex` job (FR-001, FR-004, FR-009a) in `tests/AskLucy.Application.Tests/Retrieval/UpdateKnowledgeBaseRetrievalSettingsTests.cs` — added during `/speckit-analyze` remediation (finding G1)

### Implementation for User Story 6

- [ ] T097 [US6] `TriggerInitialIndex`/`TriggerReindex` (`Full`/`Incremental`) commands with the concurrency and `Active`-status guards, ownership-guarded via `RetrievalOwnershipGuard` in `src/AskLucy.Application/Retrieval/Commands/TriggerInitialIndex/`, `TriggerReindex/` (depends on T032, T088, T033a)
- [ ] T097a [US6] `UpdateKnowledgeBaseRetrievalSettings` command — validates the FR-009a residency constraint (rejects a `Cloud` `EmbeddingProvider` when `RequiresDataResidency=true`); auto-enqueues a `FullReindex` `IndexingJob` when `ChunkingStrategy` or `EmbeddingProviderId` actually changes (FR-004 — "without requiring a separate manual trigger beyond... the strategy change itself"), ownership-guarded via `RetrievalOwnershipGuard` in `src/AskLucy.Application/Retrieval/Commands/UpdateKnowledgeBaseRetrievalSettings/` (depends on T015, T032, T033a, T097) — added during `/speckit-analyze` remediation (finding G1); resolves finding I1 by making the strategy/provider change itself trigger reindexing, matching FR-004's literal wording
- [ ] T098 [US6] `ReindexDocumentVersion` command, ownership-guarded via `RetrievalOwnershipGuard` in `src/AskLucy.Application/Retrieval/Commands/ReindexDocumentVersion/` (depends on T044, T033a)
- [ ] T099 [US6] `RetryIndexingJob` command (`409 NotInFailedState`), ownership-guarded via `RetrievalOwnershipGuard` in `src/AskLucy.Application/Retrieval/Commands/RetryIndexingJob/` (depends on T032, T033a)
- [ ] T100 [US6] `GetIndexingHistory` query, cursor-paginated (FR-039) in `src/AskLucy.Application/Retrieval/Queries/GetIndexingHistory/` (depends on T008)
- [ ] T101 [US6] `RetrievalIndexingController` — initial-index/reindex/version-reindex/retry/history endpoints, plus `PUT /api/v1/knowledge-bases/{id}/retrieval-settings` (contracts/retrieval-indexing-api.md) in `src/AskLucy.Web/Controllers/v1/RetrievalIndexingController.cs` (depends on T097–T100, T097a)
- [ ] T102 [P] [US6] Frontend: `IndexingControls.tsx` (trigger initial index/full/incremental reindex, retry) + `IndexingHistoryPanel.tsx` in `src/AskLucy.Web/ClientApp/src/features/retrieval/components/IndexingControls.tsx`, `IndexingHistoryPanel.tsx`
- [ ] T103 [US6] Wire `IndexingControls`/`IndexingHistoryPanel` into the knowledge base detail view in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/pages/KnowledgeBaseDetailPage.tsx` (depends on T102)

**Checkpoint**: Owners have full manual control over indexing, including safe concurrent-trigger
handling and recovery from failure.

---

## Phase 9: User Story 7 - Monitor retrieval and search activity (Priority: P4)

**Goal**: A retrieval dashboard shows knowledge-base/embedding/chunk/storage statistics, search
analytics, most-queried documents, and search history.

**Independent Test**: Perform a mix of searches and reindexes, then confirm the dashboard's
statistics, top-documents, and search history accurately reflect that activity (quickstart.md
Scenario 7).

### Tests for User Story 7

- [ ] T104 [P] [US7] Integration test: `ChunkStatistics`/`SearchAnalytics` periodic recompute reflects actual indexing/search activity in `tests/AskLucy.Application.Tests/Retrieval/StatisticsRecomputeTests.cs`
- [ ] T105 [P] [US7] Integration test: `GetRetrievalDashboard`/`GetTopDocuments` scoped to the caller's own knowledge bases only (FR-041) in `tests/AskLucy.Application.Tests/Retrieval/DashboardScopingTests.cs`
- [ ] T106 [P] [US7] Playwright E2E: perform searches/reindexes, open the dashboard, confirm statistics/top-documents/search-history accuracy (quickstart.md Scenario 7) in `tests/AskLucy.E2E.Tests/RetrievalDashboard.spec.ts`

### Implementation for User Story 7

- [ ] T107 [US7] Periodic Hangfire recurring job recomputing `ChunkStatistics`/`SearchAnalytics` per knowledge base (mirrors `DocumentStatistics`'s recompute cadence) in `src/AskLucy.Infrastructure/Retrieval/Jobs/RetrievalStatisticsRecomputeJob.cs` (depends on T012, T013)
- [ ] T108 [US7] `GetRetrievalDashboard`, `GetTopDocuments`, `GetEmbeddingStatus` queries (contracts/retrieval-dashboard-api.md) in `src/AskLucy.Application/Retrieval/Queries/GetRetrievalDashboard/`, `GetTopDocuments/`, `GetEmbeddingStatus/` (depends on T107)
- [ ] T109 [US7] `RetrievalDashboardController` (contracts/retrieval-dashboard-api.md) in `src/AskLucy.Web/Controllers/v1/RetrievalDashboardController.cs` (depends on T108)
- [ ] T110 [P] [US7] Frontend: `RetrievalDashboardPage.tsx`, `KnowledgeBaseStatsCard.tsx`, `TopDocumentsList.tsx` in `src/AskLucy.Web/ClientApp/src/features/retrieval/pages/RetrievalDashboardPage.tsx`, `components/KnowledgeBaseStatsCard.tsx`, `TopDocumentsList.tsx`
- [ ] T111 [US7] Wire the `/retrieval/dashboard` route and a navigation entry in `src/AskLucy.Web/ClientApp/src/routes/router.tsx` (depends on T110)

**Checkpoint**: All seven user stories are independently functional.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories.

- [ ] T112 [P] Accessibility pass (WCAG 2.2 AA, FR-051) on `SearchInterface`, `RetrievalDashboardPage`, `CitationViewer`, `IndexingControls` — keyboard operability, ARIA roles, contrast; automated axe checks in `tests/AskLucy.Web.Tests/Retrieval/RetrievalAccessibilityTests.cs`
- [ ] T113 [P] Reflection-based test confirming no `AskLucy.Domain`/`AskLucy.Application` type references SQL Server vector syntax, the OpenAI SDK, or `Microsoft.ML.OnnxRuntime` directly (FR-015/SC-006 structural check) in `tests/AskLucy.Application.Tests/Architecture/RetrievalLayeringTests.cs`
- [ ] T114 [P] Security audit-log entries for unauthorized retrieval/reindex attempts (FR-047), writing to the existing audit-log convention whenever `RetrievalOwnershipGuard` (T033a) denies access — in `src/AskLucy.Application/Retrieval/Authorization/RetrievalOwnershipGuard.cs` (depends on T033a) — narrowed during `/speckit-analyze` remediation (finding I2): this task is the audit-logging side effect only, not the guard's creation, which moved to Foundational (T033a)
- [ ] T115 [P] Update `docs/ARCHITECTURE.md` §13 (RAG Engine) to reflect the shipped `IEmbeddingService`/`IVectorStore`/`IChunkingService`/`IRagService` implementation choices
- [ ] T115a [P] Performance test: seed a representative chunk/embedding volume, assert grounded-chat-response retrieval latency stays within SC-001's 5s budget for 95% of runs, and assert search response time shows no measurable regression as volume scales toward SC-005's 5M-chunk target — wired to fail the build on regression (constitution §10/§15) in `tests/AskLucy.Infrastructure.Tests/Retrieval/RetrievalPerformanceTests.cs` — added during `/speckit-analyze` remediation (finding C1)
- [ ] T116 Run quickstart.md validation end-to-end (all 7 scenarios plus cross-cutting checks)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories. This phase is
  larger than usual for this codebase's precedent (specs/014/015) because the core RAG pipeline
  mechanics (chunking, embeddings, vector store, search ranking) are genuine shared prerequisites
  for both P1 stories, not story-specific work.
- **User Stories (Phase 3–9)**: All depend on Foundational completion.
  - US1 and US2 (P1) have no dependency on each other and can proceed in parallel.
  - US3 and US4 (P2) depend on US1/US2's search/chat plumbing existing but not on each other.
  - US5 and US6 (P3) depend on the Foundational `IndexingOrchestrator` (already built) but not on
    US1–US4.
  - US7 (P4) depends on US5/US6 having produced `IndexingJob`/search activity to report on, but
    could be stubbed against Foundational-seeded data if built earlier.
- **Polish (Phase 10)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Foundational only. Independently testable against a knowledge base indexed via the
  Foundational orchestrator directly (ahead of US5/US6's user-facing triggers).
- **US2 (P1)**: Foundational only. No dependency on US1.
- **US3 (P2)**: Builds on US1's `RagService` (T051) — extends it to read conversation-level
  settings; not independently meaningful without US1.
- **US4 (P2)**: Builds on US2's search result display (T067) and US1's citation display (T056);
  extends both with score/boost disclosure.
- **US5 (P3)**: Foundational only (`IndexingOrchestrator`, T044). Independently testable.
- **US6 (P3)**: Foundational only (`IndexingOrchestrator`, T044). Independently testable; shares no
  code with US5 beyond the orchestrator itself.
- **US7 (P4)**: Reads data produced by US5/US6 (`IndexingJob`) and US1/US2 (`RetrievalHistory`/
  `SearchHistory`) for its statistics to be meaningful, though its own code has no hard dependency
  beyond Foundational.

### Within Each User Story

- Tests MUST be written and FAIL before implementation.
- Domain/entities before commands/queries; commands/queries before controllers; controllers before
  frontend wiring.
- Story complete before moving to the next priority (or proceed in parallel per the Parallel Team
  Strategy below).

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel.
- Within Foundational: all entity-creation tasks (T004–T018) are [P]; all abstraction tasks
  (T019–T024) are [P]; all repository tasks (T029–T033, T033a) are [P]; the two embedding-provider
  implementations (T036, T037) are [P] of each other.
- Once Foundational completes, US1 and US2 can proceed fully in parallel; US5 and US6 can proceed
  fully in parallel with each other and with US1/US2.
- All tests for a user story marked [P] can run in parallel.

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Unit tests for IRagService outcome branches in tests/AskLucy.Application.Tests/Retrieval/RagServiceTests.cs"
Task: "Integration test: SendChatMessageCommandHandler grounded/no-KB paths in tests/AskLucy.Application.Tests/Ai/SendChatMessageRagIntegrationTests.cs"
Task: "Integration test: no relevant content found in tests/AskLucy.Application.Tests/Ai/SendChatMessageNoRelevantContentTests.cs"
Task: "Integration test: retrieval outage degraded mode in tests/AskLucy.Application.Tests/Ai/SendChatMessageRetrievalOutageTests.cs"

# Launch frontend component tasks for User Story 1 together:
Task: "KnowledgeBaseAttachmentPicker.tsx in src/AskLucy.Web/ClientApp/src/features/retrieval/components/KnowledgeBaseAttachmentPicker.tsx"
Task: "CitationBadge.tsx + CitationViewer.tsx in src/AskLucy.Web/ClientApp/src/features/retrieval/components/"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — the largest phase in this feature, blocks
   everything; includes the full working chunk→embed→store→search pipeline).
3. Complete Phase 3: User Story 1 (chat with citations).
4. Complete Phase 4: User Story 2 (direct search).
5. **STOP and VALIDATE**: run quickstart.md Scenarios 1–2 independently.
6. Deploy/demo if ready — this is the smallest slice that demonstrates the RAG engine's actual
   value proposition (unlike most features, US1 alone isn't a meaningful MVP here since nothing is
   indexed yet without also exercising the Foundational orchestrator or waiting for US5/US6).

### Incremental Delivery

1. Complete Setup + Foundational → pipeline mechanics ready.
2. Add US1 + US2 → test independently → deploy/demo (MVP!).
3. Add US3 + US4 → richer control and transparency → deploy/demo.
4. Add US5 + US6 → indexing becomes a real, user-facing, self-service capability (until this
   point, indexing content requires a direct/internal call to the Foundational orchestrator) →
   deploy/demo.
5. Add US7 → operational visibility → deploy/demo.

### Parallel Team Strategy

With multiple developers, after Foundational completes:

- Developer A: US1 → US3
- Developer B: US2 → US4
- Developer C: US5 → US6
- Developer D: US7 (can start once US5/US6's `IndexingJob` entity is in place, even before their
  full implementation lands, by working against Foundational-seeded test data)

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- The Foundational phase intentionally includes the full pipeline *mechanics* (chunking,
  embeddings, vector store, search ranking) — see the note under "Organization" above for why this
  deviates from the usual "interfaces only in Foundational" pattern.
- Verify tests fail before implementing.
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently.
- Avoid: vague tasks, same-file conflicts, cross-story dependencies that break independence.
