# Implementation Plan: Retrieval-Augmented Generation (RAG) & Semantic Search Engine

**Branch**: `016-rag-semantic-search` | **Date**: 2026-08-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/016-rag-semantic-search/spec.md`

## Summary

Introduce a new `Retrieval` bounded context that turns already-processed document content into
searchable, cited knowledge: chunk `DocumentVersion.ExtractedText` (specs/015) using a configurable
strategy, embed each chunk through a provider-agnostic `IEmbeddingService` (a cloud default and an
in-process local/self-hosted option for data-residency-sensitive knowledge bases), store vectors in
SQL Server's native `vector` column via EF Core 10 behind an `IVectorStore` abstraction, and serve
semantic/keyword/hybrid search with ranking transparency, citations, and conversation-time
retrieval — all durably indexed via Hangfire background jobs with retry queues and near-real-time
status via SignalR, matching every established pattern already proven by specs/014 (`KnowledgeBases`)
and specs/015 (`Documents`). Two existing aggregates are extended rather than duplicated:
`Chats.Citation` (added ahead of this feature in specs/002 for exactly this purpose) gains
RAG-specific reference fields, and `Chats.UserChat` gains knowledge-base attachment and
conversation-level retrieval settings, alongside `KnowledgeBaseDocument` gaining an optional link
into the `Documents` pipeline (research.md Decision 2) so RAG never re-implements OCR/text
extraction. Three clarified decisions shape the design end to end: per-knowledge-base data
residency (local embedding option), opt-in-only backfill of pre-existing documents (no automatic
bulk indexing on rollout), and graceful degradation (never silent failure) when retrieval is
temporarily unavailable during a chat message.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend, existing solution); TypeScript 5.x / React 19
(frontend, existing `ClientApp`). No new language.

**Primary Dependencies**: Backend: `Microsoft.ML.OnnxRuntime` (in-process local embedding model,
research.md Decision 5 — the only genuinely new package; mirrors the existing self-hosted
Whisper.net/Tesseract precedent). Everything else reuses what the solution already references:
`Microsoft.EntityFrameworkCore.SqlServer` 10.0.10's native `vector` column support (Decision 3, no
new persistence package), SQL Server Full-Text Search (Decision 6, a DB feature, not a package),
Hangfire (Decision 7, already present since specs/015), `Microsoft.AspNetCore.SignalR` (Decision 7,
already present since specs/015), and the existing `OpenAIOptions`/`AiCredentialProtector`
credential pattern for the cloud embedding provider (Decision 5). Frontend: existing MUI, TanStack
Query, Zustand, React Hook Form + Zod — no new frontend dependency.

**Storage**: SQL Server via EF Core (existing `AskLucy.Persistence`) — nine new tables
(`DocumentChunk`, `Embedding`, `EmbeddingProvider`, `IndexingJob`, `IndexingLog`,
`RetrievalHistory`, `RetrievalResult`, `SearchHistory`, `ChunkStatistics`/`SearchAnalytics`
denormalized aggregates) plus `ConversationKnowledgeBase` (new join entity, research.md
Decision 10), plus additive columns on three existing entities: `KnowledgeBaseDocument.DocumentId`
(Decision 2), `KnowledgeBase.{ChunkingStrategy,EmbeddingProviderId,RequiresDataResidency,
IndexStatus,LastIndexedAtUtc}` (Decision 11), `UserChat.{RetrievalSearchMode,RetrievalTopK,
RetrievalSimilarityThreshold,RetrievalMaxContextTokens}` (Decision 10), and
`Chats.Citation.{DocumentChunkId,KnowledgeBaseId,DocumentId,DocumentVersionId,PageNumber,Section}`
(Decision 9). No `ChunkEmbedding`/`VectorIndex` tables (Decisions 5/11 — collapsed into
`Embedding.IsCurrent` and `KnowledgeBase.IndexStatus` respectively, avoiding tables with no
independent rows/behavior per constitution §2.III).

**Testing**: xUnit (backend) for Domain/Application unit tests (chunking strategies, ranking/
blending math, and `IRagService`'s `Grounded`/`NoRelevantContent`/`Unavailable` branches tested
with faked `IEmbeddingService`/`IVectorStore`, no real SQL Server/ONNX/OpenAI dependency) and
Infrastructure integration tests (real SQL Server test instance exercising the native `vector`
column and full-text search, the real ONNX local provider, and a recorded/replayed OpenAI
embedding call); Vitest + React Testing Library + MSW + jest-axe (frontend) for the search
interface, retrieval dashboard, and citation viewer; Playwright E2E
(`tests/AskLucy.E2E.Tests`) covering upload→index→search and chat-with-citations journeys,
mirroring the existing `KnowledgeBase*.spec.ts`/`Document*.spec.ts` suites' shape.

**Target Platform**: ASP.NET Core 10 on the existing Windows/IIS (ANCM) deployment; React SPA
static build served the same way. **New deployment-time prerequisite**: the target SQL Server
instance must support the native `vector` type and `VECTOR_DISTANCE` (SQL Server 2025+ or Azure
SQL) — flagged for the ops runbook exactly as specs/015 flagged Tesseract's native OCR component,
not a code-level gap. **`CREATE VECTOR INDEX` is deliberately NOT used** on this non-Azure SQL
Server target: verified directly against the real hosted SQL Server 2025 (RTM-CU3, Standard
Edition) Test instance that creating a vector index there produces the pre-Azure/Fabric index
format (`sys.vector_indexes.index_version` is `NULL`, not the "3"/latest format), which makes the
indexed table **read-only for all DML** (INSERT/UPDATE/DELETE/MERGE fail with error 42231) — a
direct conflict with FR-010/FR-011/US5's continuous incremental-indexing requirement. The
documented `ALLOW_STALE_VECTOR_INDEX` scoped-configuration workaround is also not recognized on
this build. See research.md Decision 3 for the full finding and the brute-force `VECTOR_DISTANCE`
scan this drives instead.

**Project Type**: Web application — extends the existing layered .NET backend + React SPA. No new
top-level project.

**Performance Goals**: Directly from spec.md Success Criteria — grounded, cited chat response
within 5s of retrieval starting for 95% of queries (SC-001); ≥90% of searches return a relevant
top-5 result (SC-002); citation traceable to source in <10s (SC-003); newly processed document
searchable within 5 minutes of extraction completing, once its knowledge base has indexing enabled
(SC-004); 5,000,000 indexed chunks/organization without measurable search-latency increase
(SC-005); dashboard statistics accurate within 5s (SC-010).

**Constraints**: All list/search endpoints are cursor-paginated (constitution §6, matching
`KnowledgeBases`/`Documents`/`Chats`). Chunking/embedding/indexing/cleanup/statistics all run as
asynchronous Hangfire background jobs, never blocking the workspace UI (FR-038). A knowledge base
must be `Active` (not `Draft`/`Archived`) before an initial index can be triggered (existing
`KnowledgeBase.Activate()` already documents this as "required before future RAG indexing
eligibility"). Existing (pre-feature) `KnowledgeBaseDocument` rows remain unindexed until their
owner explicitly triggers an initial index (FR-010a) — no automatic bulk backfill job runs on
rollout. Retrieval failures during a chat message degrade to an ungrounded, clearly-labeled
response plus a non-silent error surface — never a silently-ungrounded response and never a hard
failure of the whole message (FR-037a, constitution §2.VIII). Knowledge bases with
`RequiresDataResidency` set are restricted to the local/self-hosted embedding provider only
(FR-009a) — enforced in the Application layer, not merely a UI restriction.

**Scale/Scope**: All authenticated users at launch, scoped to knowledge bases they own (matching
specs/014's private-only-in-this-release model — spec.md Assumptions). Scale target is SC-005's 5
million indexed chunks per organization.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see "Post-Design
Re-check" below.*

| Principle / Gate | Status | Notes |
|---|---|---|
| §2.I / §3 Clean Architecture & Dependency Rule | PASS | All new entities live in `Domain/Retrieval` (plus the additive fields on existing `Chats`/`KnowledgeBases` entities, owned by their existing aggregates). `IChunkingService`, `IEmbeddingService`, `IVectorStore`, `IRetriever`/`IRagService` (naming per `docs/ARCHITECTURE.md` §13) are Application-owned interfaces; OpenAI/ONNX/SQL-Server-vector/full-text specifics live only in `Infrastructure/Retrieval`. No Domain/Application code references OpenAI's SDK, `Microsoft.ML.OnnxRuntime`, or SQL Server vector/full-text syntax directly. |
| §2.II SOLID (SRP/OCP/DIP) | PASS | `Retrieval` is its own bounded context (research.md Decision 1) rather than folded into `KnowledgeBases`/`Documents`, each with a distinct reason to change. Adding a new chunking strategy or embedding provider is a new class registered via DI (OCP/DIP), never an edit to existing strategy/provider code. |
| §2.III Simplicity — DRY/KISS/YAGNI, avoid unnecessary dependencies | PASS (one new backend dependency, justified) | `Microsoft.ML.OnnxRuntime` is the only new package, justified by FR-009a's data-residency requirement (research.md Decision 5) and mirroring an already-accepted precedent (Whisper.net/Tesseract). Chunking (Decision 4), keyword search (Decision 6), vector storage (Decision 3), and background jobs/SignalR (Decision 7) all deliberately add **zero** new dependencies by reusing existing platform capabilities. `ChunkEmbedding` and `VectorIndex` (spec.md's conceptual key entities) are explicitly *not* modeled as separate tables (Decisions 5/11) — no rows/behavior beyond what `Embedding.IsCurrent`/`KnowledgeBase.IndexStatus` already provide. |
| §2.VIII No Silent Failures (NON-NEGOTIABLE) | PASS | FR-009 (embedding failures), FR-013/FR-040 (indexing failures + bounded retry queues), and FR-037a (retrieval-unavailable-mid-chat) all route through the platform's existing non-silent-failure conventions — Problem Details at the API boundary, and the same `AiProviderUnavailableException`-style surfaced-not-swallowed pattern `IRagService` reuses (research.md Decision 8) rather than inventing a new error shape. |
| §3 CQRS/MediatR/Repository/FluentValidation | PASS | Every mutation (trigger reindex, attach knowledge base to conversation, update retrieval settings) is an `IRequest`/handler pair validated by the existing `ValidationBehavior` pipeline; queries (search, dashboard, history) are separate `IRequest` query handlers that never mutate state. Repositories expose aggregate-oriented methods (e.g., `IDocumentChunkRepository.GetByKnowledgeBaseAsync`), not a leaky `IQueryable` escape hatch. |
| §5 Database — entity design, soft delete, auditing | PASS | Every new aggregate extends `BaseEntity` (surrogate `Guid` v7, audit columns via the existing `AuditSaveChangesInterceptor`, `RowVersion`). `DocumentChunk`/`Embedding` follow their source `Document`'s soft-delete/exclusion behavior (FR-016) rather than inventing a separate deletion model. `IndexingLog`/`RetrievalHistory`/`SearchHistory` are deliberately append-only, no soft delete — same documented exception pattern as `DocumentProcessingLog`/`DocumentAuditLog`. |
| §5 RAG & vector storage (explicit, non-negotiable clause) | PASS | SQL Server native `vector` column type/vector search is the only vector storage this release ships (research.md Decision 3) — no ADR needed since no alternative datastore is introduced. Chunking strategy, embedding provider/model identifier, and embedding version are stored alongside each vector (`Embedding.EmbeddingProviderId`, `DocumentChunk.ChunkingStrategy`) exactly as required, so re-embedding on a model upgrade is a data migration, not a guess; `RetrievalHistory` is provider/model-tagged for cross-version quality comparison. |
| §5 Concurrency | PASS | `KnowledgeBase.IndexStatus`/`RowVersion` guards concurrent reindex triggers (Edge Case: two users concurrently starting a full reindex) — a second trigger while `Indexing`/`InitialIndexQueued` returns `409 Conflict`, mirroring `DocumentProcessingController`'s existing "not in Failed state" retry guard. |
| §6 REST conventions, pagination, Problem Details | PASS | `/api/v1/retrieval/search` (search actions), `/api/v1/knowledge-bases/{id}/index` sub-resource actions (`actions/reindex`, `actions/initial-index`), `/api/v1/chats/{id}/knowledge-bases` (attachment), `/api/v1/retrieval/dashboard`/`history` — matches the `KnowledgeBases`/`Documents` contract shape exactly (contracts/*.md). List endpoints are cursor-paginated. |
| §6 Streaming | PASS | Retrieval itself is non-streaming (a bounded, synchronous-from-the-caller's-view lookup before generation begins — constitution §9 "background batch classification"-style justified exception, same reasoning specs/015 used for classification calls); the chat *response* that follows continues to stream exactly as today, unaffected by this feature. |
| §6 AuthN/AuthZ | PASS | `[Authorize]` by default; ownership enforced via a new `KnowledgeBaseOwnershipGuard`-style check reused from specs/014 (FR-045) for every retrieval/search/indexing endpoint — a search that includes a non-owned knowledge base excludes it entirely rather than erroring (FR-045, FR-048). |
| §6 Rate limiting | PASS | New `retrieval-search-endpoints`/`retrieval-indexing-endpoints` policies (research.md Decision 12), matching the existing per-feature tiering convention. |
| §8 Security — data residency, least privilege | PASS | FR-009a's local/self-hosted embedding requirement is enforced in the Application layer (a knowledge base with `RequiresDataResidency` can only be assigned a `Local`-hosted `EmbeddingProvider` — validated, not just UI-hidden), directly serving constitution §8's "least privilege & secure defaults" and the Vision's "data custody are deliverables" core value. |
| §8 Security — audit logging | PASS | FR-047 (unauthorized retrieval/reindex attempts) writes to the existing audit-log convention (`KnowledgeBaseAuditLog`-style, distinct from `IndexingLog`'s operational trail), mirroring specs/015's `DocumentAuditLog`/`DocumentProcessingLog` split. |
| §8 Security — prompt injection | PASS | Constitution §8 already mandates retrieved RAG content be treated as untrusted data, never instructions, with system prompts structurally separated from retrieved content — `IRagService`'s context-assembly step (FR-033) follows this existing, already-mandated separation; no new prompt-injection surface is introduced beyond what §8 already governs. |
| §9 AI Principles — provider/model abstraction | PASS | `IEmbeddingService`/`IEmbeddingServiceResolver` mirror `IAIProvider`/`IAIProviderResolver` exactly (FR-006, FR-007); embedding generation is fully decoupled from the chat provider/model a user selects. |
| §9 AI Principles — RAG architecture | PASS | The pipeline is explicitly staged (ingest → chunk → embed → store → retrieve → rank → assemble context — constitution §9's own required shape), each stage independently observable (`IndexingLog`) and independently swappable (per-stage interfaces, Decisions 3–6). |
| §10 Testing | PASS (planned in tasks) | Domain/Application unit-tested with faked `IEmbeddingService`/`IVectorStore`/`IChunkingService`; Infrastructure integration-tested against a real SQL Server vector/full-text instance and the real ONNX provider; new frontend hooks/components covered by Vitest+RTL+jest-axe; Playwright E2E covers index→search and chat-with-citations journeys end to end. |
| §14 Observability | PASS | `IndexingLog` (operational trail) and the security audit trail (FR-047) are kept distinct, matching specs/015's `DocumentProcessingLog`/`DocumentAuditLog` split; Serilog structured logging in every job handler and `IRagService` outcome branch. |
| §15 Performance | PASS | All indexing is asynchronous/background (FR-038); embeddings are generated in batches, not one chunk at a time (FR-050); `ChunkStatistics`/`SearchAnalytics` are periodically-recomputed denormalized aggregates, not synchronous per-write counters, given the 5M-chunk scale target (SC-005) — same modeling choice specs/015 made for `DocumentStatistics`. |
| §7 UI — accessibility, responsive, theming | PASS | FR-051 restates the constitution's WCAG 2.1 AA floor at the stricter 2.2 AA level, matching the same knowingly-stricter-than-baseline pattern already established for `KnowledgeBases` (specs/014) and `Documents` (specs/015). |

No Complexity Tracking entries — every gate above is a clean PASS; the one new dependency
(`Microsoft.ML.OnnxRuntime`) is justified by an explicit, clarified functional requirement
(FR-009a), not unjustified complexity or a deviation from an established architectural rule.

**Post-Design Re-check** (after Phase 1 — data-model.md, contracts/, quickstart.md): No new gate
concerns emerged during data-model/contract design. Two design choices worth recording as having
been considered against constitution §2.III (Simplicity/YAGNI) during Phase 1 and resolved in
favor of *not* adding new tables: spec.md's conceptual `ChunkEmbedding` and `VectorIndex` entities
were both found, once the field-level design was worked out, to carry no independent rows/behavior
beyond `Embedding.IsCurrent` and `KnowledgeBase.IndexStatus`/the physical vector index respectively
(data-model.md "Explicitly Not Modeled") — confirmed still correct after writing out every other
entity's full field list. All gates remain PASS; no Complexity Tracking entries were added.

## Project Structure

### Documentation (this feature)

```text
specs/016-rag-semantic-search/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

This is the existing Web application (Option 2 shape), not a new project — it extends the
already-established `src/AskLucy.*` layered backend and `src/AskLucy.Web/ClientApp` React SPA
exactly as specs/014 and specs/015 did.

```text
src/
├── AskLucy.Domain/
│   ├── Retrieval/                     # NEW — DocumentChunk, Embedding, EmbeddingProvider,
│   │                                   #   IndexingJob, IndexingLog, RetrievalHistory,
│   │                                   #   RetrievalResult, SearchHistory, ChunkStatistics,
│   │                                   #   SearchAnalytics, ConversationKnowledgeBase
│   ├── Chats/                         # EXTENDED — Citation gains RAG reference fields
│   │                                   #   (Decision 9); UserChat gains retrieval settings
│   │                                   #   (Decision 10)
│   ├── KnowledgeBases/                # EXTENDED — KnowledgeBase gains ChunkingStrategy/
│   │                                   #   EmbeddingProviderId/RequiresDataResidency/
│   │                                   #   IndexStatus/LastIndexedAtUtc (Decision 11);
│   │                                   #   KnowledgeBaseDocument gains DocumentId (Decision 2)
│   └── Documents/                     # UNCHANGED — DocumentVersion.ExtractedText is read,
│                                       #   never modified, by Retrieval
│
├── AskLucy.Application/
│   ├── Abstractions/                  # EXTENDED — IEmbeddingService, IEmbeddingServiceResolver,
│   │                                   #   IVectorStore, IChunkingService, IRagService
│   └── Retrieval/
│       ├── Commands/                  # TriggerInitialIndex, TriggerReindex, ReindexDocumentVersion,
│       │                              #   RetryIndexingJob, AttachKnowledgeBaseToConversation,
│       │                              #   UpdateConversationRetrievalSettings
│       └── Queries/                   # SemanticSearch, KeywordSearch, HybridSearch,
│                                       #   GetIndexStatus, GetRetrievalDashboard,
│                                       #   GetSearchHistory, GetSearchAnalytics, GetCitation
│
├── AskLucy.Infrastructure/
│   └── Retrieval/                     # NEW — SqlServerVectorStore, chunking strategy
│       ├── Chunking/                  #   implementations (Decision 4), OpenAiEmbeddingProvider
│       ├── Embeddings/                #   + OnnxLocalEmbeddingProvider (Decision 5),
│       └── Jobs/                      #   Hangfire job/stage handlers (Decision 7)
│
├── AskLucy.Persistence/
│   └── Configurations/Retrieval/      # NEW — EF Fluent API configs for every new entity;
│                                       #   migrations for new tables + additive columns
│
└── AskLucy.Web/
    ├── Controllers/v1/
    │   ├── RetrievalSearchController.cs        # NEW
    │   ├── RetrievalIndexingController.cs      # NEW
    │   ├── RetrievalDashboardController.cs     # NEW
    │   └── ConversationKnowledgeBasesController.cs  # NEW
    ├── Hubs/RetrievalIndexingHub.cs   # NEW — mirrors DocumentProcessingHub (Decision 7)
    └── ClientApp/src/features/
        └── retrieval/                 # NEW — search interface, retrieval dashboard,
                                        #   citation viewer, conversation KB attachment UI

tests/
├── AskLucy.Domain.Tests/Retrieval/
├── AskLucy.Application.Tests/Retrieval/
├── AskLucy.Infrastructure.Tests/Retrieval/
├── AskLucy.Persistence.Tests/Retrieval/
├── AskLucy.Web.Tests/Retrieval/
└── AskLucy.E2E.Tests/                 # NEW specs: index-and-search, chat-with-citations
```

**Structure Decision**: Extends the existing single-solution layered backend
(`Domain`→`Application`→`Infrastructure`/`Persistence`→`Web`) plus the existing React SPA under
`AskLucy.Web/ClientApp`, per constitution §3. `Retrieval` is a new bounded-context folder at each
layer (research.md Decision 1); `Chats` and `KnowledgeBases` receive additive changes only to
entities that already exist, never new parallel entities for concepts those modules already own
(Decisions 2/9/10/11).

## Complexity Tracking

*No entries — the Constitution Check above has no violations requiring justification.*
