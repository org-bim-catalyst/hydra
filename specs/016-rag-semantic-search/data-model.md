# Phase 1 Data Model: RAG & Semantic Search Engine

New entities live in `AskLucy.Domain/Retrieval/`, configured in `AskLucy.Persistence/Configurations/
Retrieval/`, per constitution §3 (Domain purity — no EF Core attributes on Domain types; all
mapping via Fluent API). Surrogate keys are `Guid` v7 (`Guid.CreateVersion7()`), matching every
existing entity. Audit columns (`CreatedAtUtc`/`CreatedBy`/`ModifiedAtUtc`/`ModifiedBy`) and the
`RowVersion` concurrency token come from `BaseEntity` + the existing `AuditSaveChangesInterceptor`,
exactly as on every existing entity, and are not repeated per-entity below. This is a new,
independent bounded context from `KnowledgeBases`, `Documents`, and `Chats` (research.md
Decision 1) — four existing entities gain additive fields only (`KnowledgeBase`,
`KnowledgeBaseDocument`, `UserChat`, `Chats.Citation`), documented under **Extended Entities**.

## New Entities

### DocumentChunk

A segment of a document's extracted content produced by a chunking strategy (FR-001–FR-005).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `KnowledgeBaseId` | `Guid` | Denormalized from `KnowledgeBaseDocument.KnowledgeBaseId` at chunk-creation time — avoids a join on every search's authorization/scoping filter (FR-045, constitution §15 N+1 avoidance). Indexed. |
| `KnowledgeBaseDocumentId` | `Guid` | FK `KnowledgeBaseDocument`, indexed. |
| `DocumentId` | `Guid` | FK `Documents.Document` (research.md Decision 2) — the actual content source. |
| `DocumentVersionId` | `Guid` | FK `Documents.DocumentVersion` — which version this chunk was extracted from (FR-002). |
| `ChunkingStrategy` | `enum ChunkingStrategy` | `FixedSize, Recursive, Paragraph, Sentence, Markdown, Heading, Table, Semantic` (FR-001) — copied from the knowledge base's setting at chunk-creation time so a later strategy change doesn't retroactively relabel existing chunks. |
| `Content` | `string` (`nvarchar(max)`) | The chunk's text. Full-text indexed (research.md Decision 6) for keyword/hybrid search. |
| `ContentHash` | `string` (64 hex chars, SHA-256) | FR-003, FR-005 — reused to detect unchanged content and skip re-embedding. Indexed. |
| `TokenCount` | `int` | FR-003. |
| `CharacterCount` | `int` | FR-003. |
| `Language` | `string?` | BCP-47 code, inherited from `DocumentLanguage` where available. |
| `PageNumber` | `int?` | FR-002, where applicable to the source format. |
| `Section` | `string?` | FR-002, derived from `DocumentVersion.ExtractedStructureJson` where available. |
| `Heading` | `string?` | FR-002. |
| `Position` | `int` | Ordinal position within the document version (FR-002) — the chunk sequence order. |

**Validation rules** (Domain):
- `Content` required, non-blank.
- `ContentHash`/`TokenCount`/`CharacterCount` are computed at creation, never mutated afterward — a
  content change always produces a new `DocumentChunk` (immutable, mirrors `DocumentVersion`'s
  immutability convention) rather than an in-place edit, keeping `Embedding` history unambiguous.

**Relationships**: Belongs to one `KnowledgeBaseDocument`/`Document`/`DocumentVersion`. Has one or
more `Embedding`s (current + historical, Decision 5). Referenced by zero or more `RetrievalResult`/
`Citation` rows.

**Deletion behavior**: When the source `Document`/`KnowledgeBaseDocument` is soft-deleted or
archived, or an earlier `DocumentVersion` is restored, affected `DocumentChunk` rows are excluded
from search via the same soft-delete/query-filter convention (FR-016) rather than hard-deleted —
existing `Citation`/`RetrievalResult` rows referencing them keep resolving (their own snapshot
fields, Decision 9, remain intact regardless).

---

### Embedding

A vector representation of a chunk's content produced by a specific embedding provider/model
(FR-006–FR-009a).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `DocumentChunkId` | `Guid` | FK `DocumentChunk`, indexed. |
| `EmbeddingProviderId` | `Guid` | FK `EmbeddingProvider` — which vendor/model/version produced this vector (constitution §5's "provider/model-tagged" requirement). |
| `Vector` | `vector(n)` (native SQL Server vector column, research.md Decision 3) | `n` = the provider's dimensionality. Mapped via EF Core 10's native vector support — no serialized `varbinary`/JSON fallback. |
| `IsCurrent` | `bool` | Exactly one `Embedding` per `DocumentChunk` is current at a time — the one used for retrieval (collapses spec.md's conceptual `ChunkEmbedding` into this flag, see "Explicitly Not Modeled" below). |

**Validation rules**:
- Immutable after creation (a re-embed always inserts a new row and flips `IsCurrent`, never
  updates a vector in place — needed so `RetrievalHistory`/`Citation` rows created against an
  older embedding remain meaningful for quality comparison, constitution §5).
- Exactly one `IsCurrent = true` row per `DocumentChunkId` enforced in the Application layer
  (mirrors `Document.CurrentVersionId`'s equivalent "app-enforced, not DB-constrained" pattern).
- FR-008: a search query never mixes `Embedding` rows whose `EmbeddingProviderId` differs from the
  knowledge base's *currently configured* provider — non-current-provider rows are simply not
  `IsCurrent` and excluded by the query itself, not a separate runtime check.

**Relationships**: Belongs to one `DocumentChunk` and one `EmbeddingProvider`.

**Indexes**: A SQL Server vector index (`CREATE VECTOR INDEX`, research.md Decision 3) on `Vector`,
scoped for approximate nearest-neighbor search at the SC-005 (5M chunks/org) scale target.

---

### EmbeddingProvider

A configured embedding source available to knowledge bases (FR-006, FR-009a).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `Vendor` | `string` | e.g., `"OpenAI"`, `"Local"`. |
| `ModelKey` | `string` | e.g., `"text-embedding-3-small"`, `"onnx-minilm-l6-v2"`. |
| `Dimensionality` | `int` | Must match the `Vector` column width of every `Embedding` it produces. |
| `HostingType` | `enum HostingType` | `Cloud`, `Local` (research.md Decision 5) — `Local` providers run in-process (`Microsoft.ML.OnnxRuntime`), never transmitting content externally (FR-009a). |
| `IsDefault` | `bool` | Exactly one `Cloud` row and, once configured, at most one `Local` row may be marked default. |
| `IsActive` | `bool` | Deactivated providers are excluded from new-embedding generation but existing `Embedding` rows referencing them remain valid history. |

**Validation rules**:
- A `KnowledgeBase` with `RequiresDataResidency = true` may only reference an `EmbeddingProvider`
  with `HostingType = Local` (FR-009a) — enforced in the `AttachEmbeddingProvider`/knowledge-base
  Application handler, not merely hidden in the UI.

**Relationships**: Referenced by zero or more `KnowledgeBase`s and zero or more `Embedding`s.

---

### IndexingJob

A unit of background work indexing a knowledge base, document, or document version
(FR-010–FR-013, FR-038).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `KnowledgeBaseId` | `Guid` | FK, indexed. |
| `KnowledgeBaseDocumentId` | `Guid?` | Set for a version-scoped or single-document job (FR-012); null for a full/incremental knowledge-base-wide job. |
| `JobType` | `enum IndexingJobType` | `InitialIndex, FullReindex, IncrementalReindex, VersionReindex, SingleDocumentIndex` (FR-010a, FR-011, FR-012). |
| `Status` | `enum IndexingJobStatus` | `Queued, InProgress, Completed, Failed` (mirrors `DocumentProcessingJob`). |
| `RetryCount` | `int` | FR-013. |
| `MaxRetries` | `int` | Bounded automatic retry count (FR-013, FR-040). |
| `HangfireJobId` | `string?` | The underlying Hangfire job id (research.md Decision 7, mirrors `DocumentProcessingJob.HangfireJobId`). |
| `StartedAtUtc` | `DateTime?` | |
| `CompletedAtUtc` | `DateTime?` | |
| `FailureReason` | `string?` | Populated on `Failed` — specific and actionable (FR-013), never generic. |

**Validation rules**:
- A new `InitialIndex`/`FullReindex`/`IncrementalReindex` job cannot be created for a knowledge
  base whose `IndexStatus` is already `Indexing`/`InitialIndexQueued` — returns `409 Conflict`
  instead (§5 Concurrency, Edge Case: two users concurrently triggering a full reindex).
- An `InitialIndex`/`FullReindex`/`IncrementalReindex` job requires `KnowledgeBase.Status =
  Active` (existing `KnowledgeBase.Activate()` XML doc: "required before future RAG indexing
  eligibility").

**Relationships**: Belongs to one `KnowledgeBase`, optionally scoped to one `KnowledgeBaseDocument`.
Has one or more `IndexingLog` entries.

**Lifecycle**:

```text
Queued ──automatic──> InProgress ──automatic──> Completed
                            │
                            └──any stage fails──> Failed ──user retry (bounded)──> Queued
```

---

### IndexingLog

A timestamped record of a stage transition or event within an `IndexingJob` (FR-039, mirrors
`DocumentProcessingLog`).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `IndexingJobId` | `Guid` | FK, indexed. |
| `Stage` | `enum IndexingStage` | `Chunking, EmbeddingGeneration, VectorWrite, Cleanup`. |
| `Status` | `enum IndexingStageStatus` | `Started, Completed, Failed`. |
| `Message` | `string?` | Human-readable detail, populated especially on `Failed`. |
| `OccurredAtUtc` | `DateTime` | |

**Relationships**: Belongs to one `IndexingJob`. Append-only — no soft delete, mirroring
`DocumentProcessingLog`'s documented exception.

---

### RetrievalHistory

A record of a retrieval performed on behalf of a conversation message (FR-030–FR-037a).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserChatId` | `Guid` | FK `Chats.UserChat`, indexed. |
| `MessageId` | `Guid?` | FK `Chats.Message` — the assistant message this retrieval served, once created. |
| `UserId` | `string` | Indexed. |
| `Query` | `string` | The effective retrieval query (typically the user's message text). |
| `SearchMode` | `enum SearchMode` | `Semantic, Keyword, Hybrid` (FR-020). |
| `KnowledgeBaseIdsSearchedJson` | `string` (JSON array) | Which knowledge bases were in scope for this retrieval — a small, write-once list never queried per-id, so a join table is unwarranted (same reasoning as `DocumentStatistics.FileTypeDistributionJson`). |
| `TopK` | `int` | Effective retrieval depth used (FR-023). |
| `SimilarityThreshold` | `decimal` | Effective threshold used (FR-023). |
| `MaxContextTokens` | `int` | Effective token budget used (FR-024). |
| `Outcome` | `enum RetrievalOutcome` | `Grounded, NoRelevantContent, Unavailable` (research.md Decision 8). |
| `DurationMs` | `int` | For `SearchAnalytics`' average-retrieval-time metric. |
| `ResultCount` | `int` | |

**Relationships**: Belongs to one `UserChat`, optionally one `Message`. Has zero or more
`RetrievalResult` rows. Append-only.

---

### RetrievalResult

A single ranked chunk returned by a retrieval (FR-026–FR-029).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `RetrievalHistoryId` | `Guid` | FK, indexed. |
| `DocumentChunkId` | `Guid` | FK. |
| `Rank` | `int` | Position in the returned, ranked list. |
| `RelevanceScore` | `decimal` | The final blended/ranked score (FR-029). |
| `SemanticScore` | `decimal?` | Cosine-similarity component, where applicable (FR-026). |
| `KeywordScore` | `decimal?` | Full-text relevance component, where applicable (FR-027). |
| `BoostFactorsJson` | `string?` (JSON) | Applied metadata boosts and their contribution (FR-028, FR-029) — small, display-only, never queried per-field. |

**Relationships**: Belongs to one `RetrievalHistory` and references one `DocumentChunk`.
Append-only.

---

### SearchHistory

A record of a direct (non-conversation) search a user performed (FR-043).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserId` | `string` | Indexed. |
| `Query` | `string` | |
| `SearchMode` | `enum SearchMode` | Same enum as `RetrievalHistory`. |
| `KnowledgeBaseIdsSearchedJson` | `string` (JSON array) | Same rationale as `RetrievalHistory`. |
| `FiltersJson` | `string?` (JSON) | Document/language/date/version/metadata filters applied (FR-022). |
| `ResultCount` | `int` | |
| `CreatedAtUtc` | `DateTime` | |

**Relationships**: Belongs to one user. Distinct from `RetrievalHistory` (conversation-scoped
retrieval) per spec.md's own separation of the two concepts. Append-only.

---

### ChunkStatistics / SearchAnalytics

Periodically computed, denormalized aggregate metrics per knowledge base (FR-041, FR-042, FR-044),
mirroring `DocumentStatistics`'s "explicitly not real-time-updated" precedent (specs/015).

| Field (`ChunkStatistics`) | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `KnowledgeBaseId` | `Guid` | Indexed. |
| `TotalChunks` | `int` | |
| `TotalEmbeddings` | `int` | |
| `StorageBytes` | `long` | Vector + text storage estimate. |
| `ComputedAtUtc` | `DateTime` | Recomputed on a periodic Hangfire recurring job, same cadence pattern as `DocumentStatistics`. |

| Field (`SearchAnalytics`) | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `KnowledgeBaseId` | `Guid?` | Null for a user-wide rollup row. |
| `UserId` | `string` | Indexed. |
| `SearchCount` | `int` | |
| `AverageRetrievalTimeMs` | `int?` | |
| `AverageSimilarityScore` | `decimal?` | |
| `FailedSearchCount` | `int` | Searches whose `Outcome = Unavailable`. |
| `EmptySearchCount` | `int` | Searches whose `Outcome = NoRelevantContent`. |
| `TopDocumentsJson` | `string?` (JSON) | Most-queried documents (FR-044) — small, display-only ranked list. |
| `ComputedAtUtc` | `DateTime` | |

**Relationships**: `ChunkStatistics` belongs to one `KnowledgeBase`; `SearchAnalytics` belongs to
one user, optionally scoped to one `KnowledgeBase`. Both are periodically recomputed, satisfying
SC-010's 5-second accuracy budget the same way `DocumentStatistics` satisfies specs/015's SC-011.

---

### ConversationKnowledgeBase

The many-to-many attachment between a conversation and the knowledge base(s) it draws context from
(FR-035, research.md Decision 10).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserChatId` | `Guid` | FK `Chats.UserChat`, indexed. |
| `KnowledgeBaseId` | `Guid` | FK, indexed. |
| `AttachedAtUtc` | `DateTime` | |

**Validation rules**: Unique on (`UserChatId`, `KnowledgeBaseId`) — attaching an already-attached
knowledge base is a no-op, not a duplicate row.

**Relationships**: Join entity between `UserChat` and `KnowledgeBase`. A `UserChat` with zero rows
here has no knowledge base attached (FR-036 — no retrieval performed).

---

## Extended Entities

### KnowledgeBase (specs/014) — additive fields

| New Field | Type | Notes |
|---|---|---|
| `ChunkingStrategy` | `enum ChunkingStrategy` | FR-001. Defaults to `Recursive` (a reasonable general-purpose default) at knowledge-base creation. |
| `EmbeddingProviderId` | `Guid?` | FK `EmbeddingProvider`. Null = use the platform's default `Cloud` provider (research.md Decision 5). |
| `RequiresDataResidency` | `bool` | FR-009a (spec.md Clarifications Q1). Defaults `false`. When `true`, `EmbeddingProviderId` must reference a `Local`-hosted provider. |
| `IndexStatus` | `enum KnowledgeBaseIndexStatus` | `NotIndexed, InitialIndexQueued, Indexing, PartiallyIndexed, Indexed, Failed` (FR-014). Defaults `NotIndexed`. |
| `LastIndexedAtUtc` | `DateTime?` | |

**New lifecycle** (independent axis from `Status`/`ArchivedAtUtc`/`DeletedAtUtc`, same "orthogonal
flag" convention specs/014 already established):

```text
NotIndexed ──owner triggers initial index (requires Status = Active)──> InitialIndexQueued
    ──automatic──> Indexing ──automatic──> Indexed
                        │
                        ├──partial completion / some documents failed──> PartiallyIndexed
                        └──job exhausts retries──> Failed ──owner retry──> InitialIndexQueued
Indexed/PartiallyIndexed ──owner triggers reindex──> Indexing (loops back above)
```

---

### KnowledgeBaseDocument (specs/014) — additive field

| New Field | Type | Notes |
|---|---|---|
| `DocumentId` | `Guid?` | FK `Documents.Document` (research.md Decision 2). Null until this document is first indexed (either as part of an `InitialIndex`/`IncrementalReindex` job, or automatically for a document uploaded after its knowledge base's first index has run — FR-010/FR-010a). Populated by the indexing pipeline creating a `Document`/`DocumentVersion` from the already-stored file, reusing the Document Intelligence Pipeline's existing extraction/OCR — never re-implemented here. |

---

### UserChat (specs/002/005) — additive fields

| New Field | Type | Notes |
|---|---|---|
| `RetrievalSearchMode` | `enum SearchMode?` | FR-020. Null = use the system default (Hybrid). |
| `RetrievalTopK` | `int?` | FR-023. Null = system default. |
| `RetrievalSimilarityThreshold` | `decimal?` | FR-023. Null = system default. |
| `RetrievalMaxContextTokens` | `int?` | FR-024. Null = system default. |

Same "conversation-level override, inherited by new messages, null means default" convention
already established by `ProviderId`/`ModelId`/`GenerationParametersJson` (specs/005).

---

### Chats.Citation (specs/002) — additive fields

| New Field | Type | Notes |
|---|---|---|
| `DocumentChunkId` | `Guid?` | Soft-reference FK — populated for RAG-sourced citations only (FR-030). May resolve to a since-deleted/inaccessible chunk; render-time accessibility is checked independently of this FK's mere presence (FR-034). |
| `KnowledgeBaseId` | `Guid?` | Snapshot at creation time — display value, not re-derived from `DocumentChunkId` at render time, so the citation survives the source knowledge base's own deletion (FR-034). |
| `DocumentId` | `Guid?` | Same snapshot rationale. |
| `DocumentVersionId` | `Guid?` | Same snapshot rationale — which version was cited, even if a newer version later supersedes it (FR-030). |
| `PageNumber` | `int?` | Snapshot. |
| `Section` | `string?` | Snapshot. |

**Validation rules**: `SourceLabel`/`SourceReference` (existing fields) remain populated for every
citation regardless of source — for RAG citations, derived from the chunk's document title/section
at creation time (Decision 9), so a citation's basic display text never depends on a live join.

---

## Explicitly Not Modeled

- **`ChunkEmbedding`** (spec.md's conceptual key entity): collapsed into `Embedding.IsCurrent`
  (research.md Decision 5). Because a `DocumentChunk`↔`Embedding` relationship is one-to-many (not
  many-to-many — a chunk accumulates embedding history as providers/models change, it never shares
  one embedding with another chunk), a separate join table would carry no fields or behavior
  `Embedding` itself doesn't already provide (constitution §2.III YAGNI).
- **`VectorIndex`** (spec.md's conceptual key entity): realized as `KnowledgeBase.IndexStatus` +
  `KnowledgeBase.LastIndexedAtUtc` plus the physical SQL Server vector index on `Embedding.Vector`
  (research.md Decisions 3/11) — not a separate EF-mapped table, for the same reason.
- **A separate `RetrievalConfiguration` entity**: retrieval settings are scalar columns directly on
  `UserChat` (Decision 10), consistent with how generation parameters are already modeled there —
  not a satellite one-row-per-conversation table.
