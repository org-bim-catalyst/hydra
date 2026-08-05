# Phase 0 Research: RAG & Semantic Search Engine

All Technical Context unknowns are resolved below; no `NEEDS CLARIFICATION` markers remain.
Findings come from reading the existing codebase (`src/AskLucy.*`), `docs/ARCHITECTURE.md` §13
(RAG Engine — already documents the target pipeline/interface names), and the project
constitution — extending already-shipped, closely related patterns (`KnowledgeBases` specs/014,
`Documents` specs/015, `Chats` specs/002/005) rather than starting from a blank slate.

## Decision 1: A new `Retrieval` bounded context, not an extension of `KnowledgeBases` or `Documents`

**Decision**: Chunking, embeddings, vector storage, search, ranking, and indexing jobs live in a
new `Retrieval` namespace (`Domain/Retrieval`, `Application/Retrieval`, `Infrastructure/Retrieval`),
independent of `KnowledgeBases` and `Documents`. Citations extend the **existing**
`Domain/Chats/Citation` entity rather than introducing a new one (Decision 9). Conversation-level
retrieval settings and knowledge-base attachment extend the **existing** `Domain/Chats/UserChat`
aggregate (Decision 10).

**Rationale**: Constitution §2.I/§2.II (Dependency Rule, SRP) — "indexing/searching content" is a
different reason to change than "organizing documents in a knowledge base" (`KnowledgeBases`) or
"extracting/versioning document content" (`Documents`). This mirrors specs/015 research.md
Decision 1's own reasoning for keeping `Documents` independent of `KnowledgeBases`. Citations and
conversation settings are the two exceptions: those concepts *already exist* in `Chats`
(specs/002 FR-016/FR-017 added `Citation` specifically in anticipation of this feature — its
`SourceLabel`/`SourceReference` fields are otherwise unused today), and constitution §18 forbids
duplicating business logic/entities that already exist elsewhere.

**Alternatives considered**:
- *Fold `Retrieval` into `KnowledgeBases`* — rejected: would make `KnowledgeBases` responsible for
  both organizational lifecycle (specs/014) and the entire RAG pipeline, a materially different
  and much larger reason to change (violates SRP the same way Decision 1 of specs/015 already
  reasoned about).
- *New `RagCitation` entity instead of extending `Chats.Citation`* — rejected: `Citation` already
  exists, is already wired into `Message.AddCitation`/`Message.Citations`, and was explicitly
  created "for SPEC-002" ahead of this feature; a parallel entity would violate constitution §18
  and leave the pre-existing one dead code.

## Decision 2: `KnowledgeBaseDocument` gains an optional link into the `Documents` pipeline

**Decision**: Add a nullable `DocumentId` (FK to `Documents.Document`) to the existing
`KnowledgeBaseDocument` entity (specs/014). RAG chunking sources its text exclusively from
`Document` → `DocumentVersion.ExtractedText`/`OcrTextRaw`/`ExtractedStructureJson` — it does
**not** re-implement parsing/OCR. `KnowledgeBaseDocument.DocumentId` is populated the first time a
knowledge base document is indexed (initial index, incremental index, or a newly-uploaded document
once its knowledge base has indexing enabled): the indexing pipeline creates a `Document` +
`DocumentVersion` from the `KnowledgeBaseDocument`'s already-stored file (reusing
`IFileStorage`/`IDocumentContentValidator`/the Document Intelligence Pipeline's existing
extraction/OCR stages end-to-end) and stores the resulting id back onto `KnowledgeBaseDocument`.

**Rationale**: specs/015 research.md Decision 1 explicitly flagged this as deferred: *"A later
specification can define how a `Document` gets attached to a `KnowledgeBase`... that link is out
of scope here."* This is that specification. Reusing `Documents`' existing OCR/extraction pipeline
(rather than re-parsing files inside `Retrieval`) is mandated by constitution §18 ("never duplicate
business logic that already exists") and §2.III (DRY). Making the link nullable and
lazily-populated — rather than retrofitting every existing `KnowledgeBaseDocument` row immediately —
is exactly what the opt-in backfill clarification (spec.md Clarifications, Q2 / FR-010a) requires:
existing documents get a `Document` created for them only when their knowledge base's owner
explicitly triggers an initial index.

**Alternatives considered**:
- *Merge `KnowledgeBaseDocument` into `Document`, dropping the separate entity* — rejected: too
  large and risky a refactor of an already-shipped, tested feature for what this spec needs; also
  re-litigates specs/015 Decision 1's SRP reasoning, which still holds (a document usable
  before/without any knowledge base is still a real scenario).
  Reimplementing extraction here (already done in specs/015 with justified new dependencies
  Tesseract/OpenXml/Docnet.Core) would violate DRY and double the ongoing maintenance surface for
  an identical concern.

## Decision 3: Vector storage — SQL Server native `vector` column via EF Core 10, no new persistence dependency

**Decision**: `Embedding.Vector` is mapped to SQL Server's native `vector(n)` column type using EF
Core 10's built-in vector support (`Microsoft.EntityFrameworkCore.SqlServer` 10.0.10, already
referenced in `AskLucy.Persistence`) — no new NuGet package. Cosine similarity ranking (FR-026) is
expressed via `EF.Functions.VectorDistance("cosine", ...)`, translated to SQL Server's native
`VECTOR_DISTANCE` T-SQL function, scanning `Embedding.Vector` directly with **no vector index**
(see "Vector index — deliberately not used" below for why). `IVectorStore` (named per
`docs/ARCHITECTURE.md` §13) is the Application-owned abstraction; `SqlServerVectorStore` in
`AskLucy.Persistence/Retrieval` (Infrastructure has no reference to Persistence — constitution §3
Dependency Rule — so this lives in Persistence, not Infrastructure) is its only implementation for
this release, satisfying constitution §5's "no separate vector database MAY be introduced without
an ADR" and FR-015's "switching backends is a configuration change" requirement structurally (a
future `QdrantVectorStore`, etc. is an additive class plus a DI registration change, never a change
to `Application`/`Domain`).

**Rationale**: Constitution §5 (RAG & vector storage) is explicit and non-negotiable on this point.
EF Core 10 is already the pinned ORM version, and its native vector mapping means zero new
persistence-layer dependencies — the simplest option satisfying both the constitution and FR-015.

**Operational prerequisite** (not code): the target SQL Server instance must support the native
`vector` type and `VECTOR_DISTANCE` (SQL Server 2025+ or Azure SQL). This is an infrastructure/ops
readiness item for deployment, not a specification gap — flagged in plan.md Technical Context
"Constraints" for the ops runbook, mirroring how specs/015 flagged Tesseract's native OCR component
as a deployment-host prerequisite rather than a code concern.

**Vector index — deliberately not used (confirmed 2026-08-05 against the real hosted SQL Server
2025 instances)**: `CREATE VECTOR INDEX` was initially assumed unavailable during Foundational
implementation (it returned "Unknown object type 'VECTOR'" against a local SQL Server 2025 LocalDB
instance with default settings). Direct verification against the real Test database
(`SQL8012.site4now.net`, SQL Server 2025 RTM-CU3, Standard Edition — recently upgraded from SQL
Server 2022 specifically for `vector` support) showed the earlier conclusion was only partially
right: `CREATE VECTOR INDEX` *does* work once `ALTER DATABASE SCOPED CONFIGURATION SET
PREVIEW_FEATURES = ON;` is set and the batch has `SET QUOTED_IDENTIFIER ON` — LocalDB's failure was
a missing preview-feature flag, not a missing feature. However, the index it creates is confirmed
to be the **pre-Azure/Fabric ("earlier") index format**: `sys.vector_indexes.index_version` is
`NULL`, not `"3"` (the "latest" format). Per Microsoft's own docs, the latest/DML-compatible index
format is available "only in Azure SQL Database and SQL database in Microsoft Fabric currently" —
not on this on-prem/hosted, non-Azure instance. The earlier format makes the indexed table
**permanently read-only for DML** — confirmed directly: `INSERT` into an indexed test table failed
with `Msg 42231: Data modification statement failed because table '...' has a vector index on it.`
The documented `ALLOW_STALE_VECTOR_INDEX` scoped-configuration workaround, which the docs suggest
for exactly this situation, is **not recognized** on this SQL Server build either ("Incorrect
syntax near 'ALLOW_STALE_VECTOR_INDEX'"), and the modern `VECTOR_SEARCH(...) ... WITH APPROXIMATE`
query syntax is likewise not recognized ("Incorrect syntax near 'APPROXIMATE'") — both confirming
those capabilities are Azure/Fabric-only, not a matter of enabling a flag on this build.

A vector index is therefore incompatible with FR-010/FR-011/US5's continuous incremental-indexing
requirement on this real deployment target, so **no vector index is created on SQL Server**;
`SqlServerVectorStore` performs an exact (brute-force) nearest-neighbor scan via `VECTOR_DISTANCE`
in `ORDER BY`. **Update (ADR-0007, 2026-08-05)**: rather than wait for a future SQL Server release
or migrate the whole platform to Azure SQL/Fabric, the platform now also supports Pinecone as a
second, genuinely indexed `IVectorStore` implementation, selectable per knowledge base
(`KnowledgeBase.VectorStoreProvider`) and defaulted for new knowledge bases — see ADR-0007 for the
full decision, alternatives, and consequences. `SqlServerVectorStore`'s brute-force scan remains
correct and unchanged for any knowledge base that stays on `SqlServer` (the default for knowledge
bases that existed before ADR-0007, and mandatory for `RequiresDataResidency` ones).

**Alternatives considered** (at the time this Decision was written, before ADR-0007):
- *A dedicated vector database (Qdrant/Pinecone/pgvector/etc.) now* — rejected at the time by the
  spec itself (FR-015/SC-006 explicitly target SQL Server first) and by constitution §5, which
  requires an ADR to introduce one. **Superseded by ADR-0007**, which is that ADR: Pinecone was
  subsequently added as a second implementation once the read-only-after-vector-index finding above
  demonstrated SQL Server's native vector search insufficient for FR-010/FR-011/US5 on this real,
  non-Azure deployment target.
- *Store vectors as `varbinary(max)`/JSON and compute cosine similarity in application code* —
  rejected: defeats indexed, in-database nearest-neighbor search entirely, cannot meet SC-005's
  5M-chunk scale target or SC-001's 5-second latency target, and duplicates what the native column
  type already provides for free.

## Decision 4: Chunking — hand-rolled strategies, no new dependency

**Decision**: Each chunking strategy from FR-001 (fixed-size, recursive, paragraph, sentence,
markdown-aware, heading-aware, table-aware, semantic) is a small `IChunkingStrategy`
implementation in `Application/Retrieval` (per `docs/ARCHITECTURE.md` §13's `IChunkingService`),
operating on `DocumentVersion.ExtractedText`/`ExtractedStructureJson` (the latter already carries
headings/paragraphs/tables from specs/015 FR-022, so heading/table-aware chunking reads structure
that already exists rather than re-parsing it). Semantic chunking groups adjacent sentences by
embedding-similarity boundary (a cheap, incremental use of the already-required embedding
provider) rather than a separate ML dependency. Selected via the Strategy pattern
(constitution §3 CQRS/architecture rules), keyed by `KnowledgeBase.ChunkingStrategy`.

**Rationale**: None of the eight strategies require an external NLP library — they are
well-understood text-splitting algorithms over content the platform already extracts. Adding a
chunking library (e.g., a LangChain-style package) would be an unjustified new dependency per
constitution §2.III ("no unnecessary dependencies") for logic this small.

**Alternatives considered**:
- *Third-party chunking library* — rejected: no such dependency exists in the .NET ecosystem
  mature enough to justify displacing a straightforward, testable, in-house Strategy
  implementation; would also make swapping/tuning a strategy an external-library upgrade instead
  of a code change.

## Decision 5: Embeddings — cloud default (OpenAI) + local in-process option (data residency)

**Decision**: Two `EmbeddingProvider` implementations of a new `IEmbeddingService` (per
`docs/ARCHITECTURE.md` §13), registered in `Infrastructure/Retrieval`:
- **Cloud default**: `OpenAiEmbeddingProvider`, calling OpenAI's embeddings endpoint
  (`text-embedding-3-small`), reusing the existing `OpenAIOptions`/credential-protection pattern
  already established in `Infrastructure/Ai` (`AiCredentialProtector`) rather than a parallel
  credential mechanism.
- **Local/self-hosted (FR-009a)**: `OnnxLocalEmbeddingProvider`, running a compact sentence-
  embedding ONNX model in-process via `Microsoft.ML.OnnxRuntime`, mirroring the exact precedent
  specs/015's plan.md already cites for OCR: *"a self-hosted OCR engine (Tesseract, mirroring the
  existing self-hosted Whisper.net STT precedent)"* — `WhisperLocalTranscriptionProvider`
  (`Infrastructure/Ai`) is the same in-process, no-network-call shape this provider follows. This
  literally satisfies "content never leaves the platform's environment" (FR-009a), not merely
  "hosted on infrastructure we control."

`EmbeddingProvider` (the entity, distinct from the interface) records vendor, model, dimensionality,
and a `HostingType` (`Cloud`/`Local`); `KnowledgeBase.EmbeddingProviderId` selects one, defaulting
to the platform's default cloud provider row and restricted to `Local`-hosted rows when
`KnowledgeBase.RequiresDataResidency` is set (spec.md Clarifications Q1). Both providers are
resolved through the same `IEmbeddingService`/`IEmbeddingServiceResolver` seam `IAIProvider`/
`IAIProviderResolver` already establishes for chat (FR-006, FR-007 — decoupled from the chat
provider a user selects).

**Rationale**: OpenAI is the only one of the platform's existing chat providers
(OpenAI/Anthropic/Google Gemini/OpenRouter) that offers a first-class embeddings API, making it the
natural default requiring no new vendor integration pattern. The local provider reuses a pattern
this codebase has already proven out for exactly this "must not leave the box" requirement class.

**Alternatives considered**:
- *Local provider calls an external self-hosted server (e.g., a separately-deployed Ollama
  instance) instead of running in-process* — rejected: adds a new network hop and deployment
  topology component for no benefit over the in-process ONNX approach, and is a weaker
  interpretation of "never transmitted" than running inside the same process.
- *Cohere/Mistral/Azure OpenAI as the initial cloud default* — deferred (spec.md Assumptions: the
  specific initial cloud vendor is a configuration detail); OpenAI chosen only because it requires
  zero new provider-integration pattern given the credential/options plumbing already exists.

## Decision 6: Search — SQL Server Full-Text Search for keyword relevance, blended with vector distance for hybrid

**Decision**: `DocumentChunk.Content` gets a SQL Server full-text index; keyword search (FR-018)
uses `CONTAINSTABLE`/`FREETEXTTABLE` for BM25-style relevance ranking; hybrid search (FR-019,
FR-027) blends the full-text rank and the vector cosine-distance rank via a configurable weighted
score computed in the `Application/Retrieval` query handler (not in SQL), keeping the blending
formula unit-testable independent of the database.

**Rationale**: SQL Server Full-Text Search is a built-in engine capability (enabling it is a DDL/
feature-flag concern, not a new dependency), consistent with constitution §7 (Convention over
Configuration — reuse the platform's existing datastore's own capability) and §2.III (no
unnecessary dependencies). Introducing a separate search engine (e.g., Elasticsearch, Lucene.NET)
for keyword relevance would duplicate what SQL Server already does and contradict the
single-datastore posture constitution §5 already commits to for vectors.

**Alternatives considered**:
- *Elasticsearch/OpenSearch for keyword search* — rejected: a second datastore for a capability
  SQL Server already has, disproportionate to this spec's needs and against constitution §5's
  spirit (no new datastore without an ADR justifying insufficiency).
- *`LIKE '%term%'` substring matching instead of full-text search* — rejected: no relevance
  ranking, terrible performance at the 5M-chunk scale target (SC-005), and does not satisfy
  FR-018's "returning chunks containing literal query terms" with usable ranking for FR-027's
  hybrid blend.

## Decision 7: Background processing reuses Hangfire; indexing status reuses the SignalR pattern

**Decision**: `IndexingJob` is processed via Hangfire (already referenced by
`AskLucy.Persistence`/`AskLucy.Infrastructure.Documents` since specs/015 — no new dependency),
chained per stage (Chunking → Embedding Generation → Vector Write → Cleanup) exactly like
`DocumentProcessingJob`'s stage chain. A new SignalR hub, `/hubs/retrieval-indexing`, mirrors
`DocumentProcessingHub`'s per-user-group join pattern and reconciliation-poll fallback
(specs/015 research.md Decision 7) for pushing `KnowledgeBase.IndexStatus` changes (FR-014) and
job/stage progress (FR-039) to the owner in near-real-time.

**Rationale**: Both patterns are already fully proven in this exact codebase for the structurally
identical problem (durable, resumable, retryable background work with live status push). Constitution
§7 (Convention over Configuration) requires following the established convention rather than a
parallel mechanism.

**Alternatives considered**: None seriously considered — introducing a second job engine or a
polling-only status mechanism would both regress below an already-adopted, working precedent for no
benefit.

## Decision 8: Retrieval-time outage handling lives in a new `IRagService`, invoked from `SendChatMessageCommandHandler`

**Decision**: A new `IRagService.RetrieveContextAsync(...)` (per `docs/ARCHITECTURE.md` §13) is
called from `SendChatMessageCommandHandler` before building the message list, only when the
conversation (`UserChat`) has one or more attached knowledge bases (Decision 10). `IRagService`
catches embedding-provider/vector-store failures internally and returns a result type
(`RagRetrievalOutcome`: `Grounded` with chunks, `NoRelevantContent`, or `Unavailable` with a
failure reason) rather than throwing — `SendChatMessageCommandHandler` then either augments the
prompt and attaches citations (`Grounded`), sends the user's message ungrounded with no citations
attached (`NoRelevantContent`/`Unavailable`), and additionally raises the non-silent, actionable
error surface for `Unavailable` (FR-037a) via the same `IAIProvider`-failure-surfacing convention
`AiProviderUnavailableException`/`Problem Details` already uses for chat-provider outages —
satisfying constitution §2.VIII (No Silent Failures) with the platform's existing failure-surfacing
shape rather than a bespoke one.

**Rationale**: Directly implements spec.md's Q3 clarification (degrade with a clear warning,
FR-037a) using the exact non-silent-failure mechanism the constitution and the rest of the AI
integration surface already use, rather than inventing a new error-reporting convention.

**Alternatives considered**:
- *Throw and fail the whole chat message on retrieval outage* — this was the explicit alternative
  presented during clarification and rejected by the user in favor of graceful degradation.
- *Silently drop retrieval and say nothing* — rejected outright: violates constitution §2.VIII
  (No Silent Failures), which is non-negotiable.

## Decision 9: Extend `Chats.Citation`, not a new `Citation` entity

**Decision**: Add nullable fields to the existing `Domain/Chats/Citation` entity:
`DocumentChunkId` (FK, soft-reference), `KnowledgeBaseId`, `DocumentId`, `DocumentVersionId`,
`PageNumber`, `Section`. Existing `SourceLabel`/`SourceReference` remain the generic
always-populated display fields (now populated from the chunk's document title/section for
RAG-sourced citations); the new fields are RAG-specific and null for any future non-RAG citation
source. Because `Citation` rows already store their own denormalized `SourceLabel`/
`SourceReference` at creation time, FR-034's "retain the citation on the original historical
response... indicating the source is no longer available" is satisfied by checking
`DocumentChunkId`'s live accessibility at render time while the label/reference text itself never
disappears (it was captured at creation, not looked up live).

**Rationale**: Decision 1's constitution §18 reasoning. This also means `Citation` needs no new
migration-breaking change to `Message.AddCitation`'s existing signature — a new overload/factory
handles the RAG-specific fields.

**Alternatives considered**: See Decision 1.

## Decision 10: Conversation knowledge-base attachment and retrieval settings extend `UserChat`

**Decision**: A new join entity `ConversationKnowledgeBase` (`UserChatId`, `KnowledgeBaseId`)
models the many-to-many attachment (FR-035). Retrieval configuration (FR-020, FR-023, FR-024)
lives as nullable scalar columns directly on `UserChat` — `RetrievalSearchMode`, `RetrievalTopK`,
`RetrievalSimilarityThreshold`, `RetrievalMaxContextTokens` — null meaning "use the system
default," mirroring the existing `GenerationParametersJson`/`ProviderId`/`ModelId` "conversation-
level override, inherited by new messages" pattern already on `UserChat` (specs/005).

**Rationale**: `UserChat` is already the aggregate that owns conversation-level AI configuration
(provider, model, generation parameters); retrieval configuration is the same category of concern,
and constitution §7 favors following that convention over introducing a parallel
"ConversationSettings" aggregate for one more configuration axis. A join table (not a JSON array
of ids on `UserChat`) is used for the KB attachment specifically because it must be efficiently
queryable from the `KnowledgeBase` side too (e.g., "how many conversations reference this KB").

**Alternatives considered**:
- *Store attached knowledge base ids as a JSON array on `UserChat`* — rejected: not indexable/
  queryable from the `KnowledgeBase` side, and inconsistent with how every other many-to-many
  relationship in this codebase (e.g., `Document`↔`DocumentTag`) is modeled as a real join.

## Decision 11: `KnowledgeBase` gains RAG-configuration and index-status fields; `VectorIndex` is not a separate table

**Decision**: Add to the existing `KnowledgeBase` aggregate: `ChunkingStrategy` (enum, defaults to
a sensible platform default), `EmbeddingProviderId` (FK, nullable → platform default cloud
provider), `RequiresDataResidency` (bool, Decision 5/FR-009a), `IndexStatus` (enum: `NotIndexed`,
`InitialIndexQueued`, `Indexing`, `PartiallyIndexed`, `Indexed`, `Failed` — FR-014), and
`LastIndexedAtUtc`. The spec's "VectorIndex" key entity is realized as this status tracking plus
the physical SQL Server vector index from Decision 3 — not a separate EF-mapped table, since it
would have no independent rows/behavior beyond what `KnowledgeBase.IndexStatus` and the DB-level
index already provide (constitution §2.III YAGNI).

**Rationale**: Consistent with `KnowledgeBase`'s existing pattern of owning its own denormalized
status/statistics fields (`DocumentCount`, `TotalPageCount`, `StorageSizeBytes` — specs/014
Decision-equivalent) rather than a satellite one-row-per-KB table. A knowledge base's `Status`
must additionally be `Active` (not `Draft`/`Archived`) before an initial index can be triggered —
the existing `KnowledgeBase.Activate()` XML doc already anticipates this: *"Required before future
RAG indexing eligibility (FR-006)."*

**Alternatives considered**:
- *A literal `VectorIndex` table with one row per knowledge base* — rejected: would carry no
  fields beyond what's proposed for `KnowledgeBase` directly; an unjustified extra join for every
  status read.

## Decision 12: New rate-limiting policies, matching existing tiering

**Decision**: Three new `AddRateLimiter` policies in `Program.cs`, matching the existing
per-user/per-tenant fixed-window shape: `retrieval-search-endpoints` (semantic/keyword/hybrid
search — generous, like `knowledge-base-endpoints`/`document-endpoints`, 120/min), `retrieval-
indexing-endpoints` (manual reindex triggers — tighter, like `document-upload-chunk-endpoints`,
given the cost of a full reindex, 10/min), and reuse of the existing `ai-endpoints` cost-tiered
policy is **not** used for search itself (search doesn't call the chat AI provider) but chat
messages that trigger retrieval continue through the existing `ai-endpoints` policy unchanged —
retrieval adds no new AI-provider-invoking endpoint of its own.

**Rationale**: Constitution §6 requires every public endpoint to be rate-limited; this follows the
exact precedent already established per-feature (specs/001/002/004/005/014/015 each added their
own named policy) rather than reusing an unrelated one.
