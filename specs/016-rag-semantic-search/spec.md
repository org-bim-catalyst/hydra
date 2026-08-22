# Feature Specification: Retrieval-Augmented Generation (RAG) & Semantic Search Engine

**Feature Branch**: `016-rag-semantic-search`

**Created**: 2026-08-05

**Status**: Draft

**Input**: User description: "Build an enterprise-grade Retrieval-Augmented Generation (RAG) engine that transforms processed documents into searchable knowledge and augments AI conversations with highly relevant context. The engine must be provider-agnostic and vector-store-agnostic, initially using SQL Server as the vector storage backend while allowing future migration to dedicated vector databases without changing business logic. It builds upon Knowledge Base Management and the Document Intelligence Pipeline. Responsibilities: chunk documents using multiple configurable strategies; generate embeddings through a provider abstraction; index chunks; run semantic, keyword, and hybrid search across one, many, or excluded knowledge bases with configurable retrieval depth, filters, and ranking; inject retrieved context into AI prompts within model context limits; return citations (document, version, knowledge base, page, section, chunk) with every knowledge-grounded answer; support automatic and manual (full/incremental/version) reindexing via background jobs with retry queues; provide a retrieval dashboard with knowledge base, embedding, chunk, storage, and search analytics; and lay groundwork for future knowledge graphs, agentic retrieval, and multi-modal (CAD/BIM/GIS/image) embeddings without requiring architectural rework."

## Clarifications

### Session 2026-08-05

- Q: Should embedding generation for confidential document content be restricted from leaving the platform's environment, or is sending chunk text to a cloud embedding provider acceptable for all knowledge bases in this release? → A: Per-knowledge-base choice — owners can designate a knowledge base as requiring a local/self-hosted embedding provider so its content never leaves the platform's environment, alongside a cloud default for knowledge bases without that requirement.
- Q: When this feature ships, should already-processed documents from the existing Document Intelligence Pipeline be automatically bulk-indexed for RAG right away, or does indexing wait for an explicit trigger per knowledge base? → A: Opt-in per knowledge base — existing knowledge bases remain unindexed until their owner explicitly triggers a first index; only newly processed documents going forward are indexed automatically.
- Q: If a conversation has a knowledge base attached but retrieval is temporarily unavailable when the user sends a message, what should happen? → A: Degrade with a clear warning — the AI still answers from general knowledge, the response is visibly labeled as ungrounded with no citations, and the retrieval failure is separately surfaced as a non-silent error.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Chat with your documents and get cited answers (Priority: P1)

A user is in a conversation with one or more knowledge bases attached. They ask a question in plain language. The system finds the most relevant passages across the attached knowledge bases, uses them to ground the AI's answer, and shows the user exactly which documents, pages, and passages the answer came from.

**Why this priority**: This is the entire reason the RAG engine exists — turning a static document collection into something a user can actually converse with, and doing so in a way the user can trust and verify. Without cited, grounded answers, every other capability in this spec is just plumbing with no visible payoff.

**Independent Test**: Can be fully tested by attaching a knowledge base containing a known document to a conversation, asking a question whose answer exists in that document, and confirming the response is relevant, and includes a citation pointing to the correct document and location.

**Acceptance Scenarios**:

1. **Given** a conversation has one knowledge base attached with relevant content, **When** the user asks a question answerable from that content, **Then** the AI's response is grounded in the retrieved passages and displays a citation identifying the source document, version, knowledge base, page (if applicable), and section.
2. **Given** a conversation has multiple knowledge bases attached, **When** the user asks a question, **Then** relevant passages are drawn from across all attached knowledge bases and each citation identifies which knowledge base it came from.
3. **Given** a conversation has no knowledge base attached, **When** the user asks a question, **Then** the AI answers from its general knowledge without performing retrieval and without displaying fabricated citations.
4. **Given** a conversation has a knowledge base attached, **When** the user asks a question with no relevant content in any attached knowledge base, **Then** the system clearly indicates no relevant knowledge was found rather than presenting an unsupported answer as if it were grounded.
5. **Given** an AI response includes a citation, **When** the user opens it, **Then** they see the source document at the cited page/section with the matched passage visually highlighted.
6. **Given** a conversation has a knowledge base attached, **When** the user sends a message while retrieval is temporarily unavailable, **Then** the AI still responds from general knowledge, the response is visibly labeled as not grounded in the knowledge base with no citations attached, and the retrieval failure is separately surfaced as a non-silent error.

---

### User Story 2 - Search a knowledge base directly (Priority: P1)

A user wants to find information without starting a conversation. They open the search interface, choose semantic, keyword, or hybrid search, and search across one knowledge base, several, or all of them (optionally excluding specific ones).

**Why this priority**: Direct search is independently valuable — many users want to locate a fact or passage quickly without a conversational back-and-forth — and it is the simplest possible slice that proves chunking, embeddings, indexing, and ranking work end to end, ahead of layering chat integration on top.

**Independent Test**: Can be fully tested by indexing a knowledge base with known content, issuing a search query in each mode (semantic, keyword, hybrid), and confirming relevant chunks are returned, ranked, and correctly attributed — independent of any conversation or chat flow.

**Acceptance Scenarios**:

1. **Given** an indexed knowledge base, **When** a user runs a semantic search for a concept described in different words than the source text, **Then** relevant chunks are returned ranked by conceptual similarity.
2. **Given** an indexed knowledge base, **When** a user runs a keyword search for an exact term, **Then** chunks containing that literal term are returned.
3. **Given** an indexed knowledge base, **When** a user runs a hybrid search, **Then** results reflect a combined ranking of semantic and keyword relevance.
4. **Given** a user has access to multiple knowledge bases, **When** they scope a search to one specific knowledge base, **Then** only results from that knowledge base appear.
5. **Given** a user is searching across all their knowledge bases, **When** they explicitly exclude one, **Then** no results from the excluded knowledge base appear.
6. **Given** a search matches nothing above the configured relevance threshold, **When** the search completes, **Then** the system shows a clear "no results" state rather than an error or irrelevant results.

---

### User Story 3 - Control how retrieval works (Priority: P2)

A user (or a conversation's owner) adjusts how many results come back, how strict the relevance threshold is, how much context budget retrieval may consume, and which search mode is used — either for a single search or as a conversation-level default.

**Why this priority**: Once basic search and chat retrieval work (P1s), the next most valuable thing is giving users control over precision vs. recall and cost/latency trade-offs, since a one-size-fits-all retrieval configuration will under-serve some knowledge bases (large, noisy) and over-serve others (small, precise).

**Independent Test**: Can be fully tested by changing retrieval depth, similarity threshold, and search mode for a conversation or search, and confirming results returned reflect the new configuration on the next query, independent of the dashboard or analytics features.

**Acceptance Scenarios**:

1. **Given** a user changes the retrieval depth (max results) for a conversation, **When** they next ask a question, **Then** the number of chunks considered for the answer reflects the new setting.
2. **Given** a user raises the similarity threshold, **When** they search again, **Then** fewer, more strictly relevant results are returned than before the change.
3. **Given** a user selects a specific search mode (semantic, keyword, or hybrid) for a conversation, **Then** subsequent questions in that conversation use that mode until changed.
4. **Given** a user sets a maximum context token budget, **When** retrieval would otherwise exceed it, **Then** lower-ranked chunks are trimmed so the budget is respected.
5. **Given** a user changes a conversation's knowledge base or retrieval settings, **When** they view earlier messages in the same conversation, **Then** those earlier messages' citations and results are unchanged by the new settings.

---

### User Story 4 - See why a result was selected (Priority: P2)

A user reviewing search results or a cited AI answer wants to understand why a particular passage was chosen — its relevance score, whether it matched on keywords, semantics, or both, and any boosts applied.

**Why this priority**: Trust in a RAG system depends on transparency; without visible ranking rationale, users cannot tell a well-grounded answer from a lucky guess, and knowledge base owners cannot tune their content or configuration effectively.

**Independent Test**: Can be fully tested by running a search and inspecting each result's displayed relevance information (score, match type, boosts applied) without needing chat, analytics, or indexing features.

**Acceptance Scenarios**:

1. **Given** a set of search results, **When** the user inspects a result, **Then** they can see its similarity score and, for hybrid results, the contribution of keyword vs. semantic relevance.
2. **Given** a result received a metadata boost (e.g., recency, category match), **When** the user inspects it, **Then** the boost is disclosed as a contributing factor.
3. **Given** two results with different ranking factors, **When** displayed together, **Then** their relative order is consistent with their disclosed scores.

---

### User Story 5 - Keep knowledge bases automatically searchable (Priority: P3)

As documents finish processing, get updated to a new version, or are added to a knowledge base, they become searchable without the user having to do anything, and the user can see the current index status of their knowledge base at any time.

**Why this priority**: Automatic indexing is what makes the system feel alive rather than requiring manual upkeep, but it is priority 3 because search and chat (P1) can be demonstrated against a knowledge base that was indexed by a one-time or manual action first.

**Independent Test**: Can be fully tested by completing document processing (from the Document Intelligence Pipeline) and confirming the resulting content becomes searchable and reflected in the knowledge base's index status without any manual reindex action.

**Acceptance Scenarios**:

1. **Given** a document finishes processing in its knowledge base, **When** indexing completes, **Then** its content is searchable and the knowledge base's index status reflects the update.
2. **Given** a document is replaced with a new version, **When** the new version finishes processing, **Then** the new version's content becomes searchable and the prior version's chunks are no longer returned in default searches.
3. **Given** a document is deleted or archived, **When** the change is applied, **Then** its chunks stop appearing in search results.
4. **Given** a user opens their knowledge base, **When** they check its status, **Then** they see whether it is fully indexed, partially indexed, indexing in progress, or failed.

---

### User Story 6 - Manually manage indexing (Priority: P3)

A knowledge base owner triggers a full or incremental reindex, reindexes a single document version, and reviews/retries any indexing operations that failed.

**Why this priority**: Manual control is a safety net for edge cases (chunking strategy changes, embedding provider changes, recovering from failures) rather than everyday behavior, so it can follow automatic indexing.

**Independent Test**: Can be fully tested by triggering a manual full reindex and an incremental reindex on a knowledge base, and by retrying a deliberately failed indexing job, confirming each action's outcome independent of automatic indexing behavior.

**Acceptance Scenarios**:

1. **Given** a knowledge base owner wants to reprocess everything (e.g., after changing its chunking strategy), **When** they trigger a full reindex, **Then** all documents in the knowledge base are re-chunked and re-embedded.
2. **Given** a knowledge base has some unindexed or changed content, **When** the owner triggers an incremental reindex, **Then** only that content is processed, leaving already-indexed, unchanged chunks untouched.
3. **Given** an indexing operation fails, **When** the owner views the processing queue, **Then** they see the failure with an actionable reason and a retry action.
4. **Given** a document has multiple versions, **When** the owner reindexes one specific version, **Then** other versions' index state is unaffected.

---

### User Story 7 - Monitor retrieval and search activity (Priority: P4)

A knowledge base owner opens a retrieval dashboard to see knowledge base, embedding, chunk, and storage statistics, plus search analytics like search volume, average retrieval time, top and most-queried documents, and failed/empty searches.

**Why this priority**: Analytics help owners optimize their content and configuration over time but are not required for the core retrieval experience to deliver value, making this the natural capstone once search, chat, control, transparency, and indexing all work.

**Independent Test**: Can be fully tested by performing a series of searches and reindex operations, then confirming the dashboard's statistics and search history reflect that activity accurately, independent of any other in-progress feature.

**Acceptance Scenarios**:

1. **Given** a user has performed searches over time, **When** they open the retrieval dashboard, **Then** they see search count, average retrieval time, average similarity score, and failed/empty search counts.
2. **Given** a knowledge base has indexed content, **When** the owner views its statistics, **Then** they see chunk count, embedding count, and storage usage.
3. **Given** searches have targeted specific documents more than others, **When** the owner views the dashboard, **Then** the most-queried documents are visibly ranked.
4. **Given** a user opens their search history, **When** they review it, **Then** they see prior queries, the mode used, knowledge bases searched, and result counts.

---

### Edge Cases

- What happens when a user's query returns zero chunks above the configured similarity threshold — does the AI answer unaided, or state that nothing relevant was found?
- How does the system handle a knowledge base that has content but has never been indexed (e.g., indexing job never ran or was interrupted)?
- How does the system handle a chunk whose source document is deleted, archived, or has its access permission changed after the chunk was already cited in a past conversation message?
- What happens when a single document's extracted content, after chunking, would exceed the selected model's usable context window on its own?
- How does the system handle a document version change while a search or an in-flight AI response is actively citing the prior version's chunks?
- What happens when two users concurrently trigger a full reindex on the same knowledge base?
- How does the system handle a query in a language different from the language of the indexed content?
- What happens when the configured embedding provider is unavailable or returns an error mid-indexing-batch?
- How does the system handle a user searching a knowledge base they do not own or are not authorized to access, including indirectly through a multi-knowledge-base search that includes it?
- What happens when a chunking strategy is changed on a knowledge base that already has indexed content — are prior chunks invalidated automatically or only on next reindex?
- How does the system handle near-duplicate chunks across different documents (e.g., a boilerplate clause repeated in many contracts) so results aren't dominated by repetition?
- What happens when the maximum context token budget is too small to fit even the single highest-ranked chunk?

## Requirements *(mandatory)*

### Functional Requirements

**Chunking**

- **FR-001**: System MUST automatically split a document's extracted content into chunks using a configurable chunking strategy (fixed-size, recursive, paragraph, sentence, markdown-aware, heading-aware, table-aware, or semantic), selectable per knowledge base.
- **FR-002**: System MUST preserve, for every chunk, a link back to its source document, document version, knowledge base, page number (where applicable), section/heading, and position within the document.
- **FR-003**: System MUST record token count, character count, a content hash, language, and creation date for every chunk.
- **FR-004**: System MUST automatically re-chunk a document when a new version is created or when the knowledge base's assigned chunking strategy changes, without requiring a separate manual trigger beyond the version upload or strategy change itself.
- **FR-005**: System MUST NOT regenerate a chunk's embedding when the chunk's content hash is identical to its previously indexed content.

**Embeddings**

- **FR-006**: System MUST generate a vector embedding for every indexed chunk using a configurable embedding provider, decoupled from the AI provider/model the user has selected for chat.
- **FR-007**: System MUST support introducing additional embedding providers as an incremental extension, without requiring changes to how chunking, storage, retrieval, ranking, or citations behave.
- **FR-008**: System MUST detect when a chunk's stored embedding was produced by a different provider or model than the knowledge base's currently configured embedding provider, and MUST exclude mismatched embeddings from a single ranked result set rather than silently mixing incompatible vectors.
- **FR-009**: System MUST surface embedding generation failures as an actionable, non-silent error tied to the specific document and chunk, with a retry path — never a silent skip.
- **FR-009a**: Users MUST be able to designate a knowledge base as requiring a local/self-hosted embedding provider, guaranteeing its chunk content is never transmitted to an external cloud embedding service; knowledge bases without this designation use the platform's default (cloud-capable) embedding provider.

**Indexing & Vector Storage**

- **FR-010**: System MUST automatically index a document's chunks and embeddings once the Document Intelligence Pipeline marks the document's content extraction complete, without requiring a manual trigger, for any document processed after this capability is enabled for its knowledge base.
- **FR-010a**: A knowledge base's existing (already-processed) documents MUST remain unindexed for RAG until the knowledge base owner explicitly triggers an initial index; the system MUST NOT automatically bulk-index pre-existing content across knowledge bases on rollout.
- **FR-011**: Users MUST be able to manually trigger a full reindex (all content) or an incremental reindex (only new or changed content) of a knowledge base, including a first-time initial index for a knowledge base that has never been indexed.
- **FR-012**: Users MUST be able to reindex a single document version independently of reindexing the rest of its knowledge base.
- **FR-013**: System MUST automatically retry indexing operations that fail due to transient errors, up to a bounded retry count, and MUST surface operations that exhaust retries as a visible failure requiring explicit user action.
- **FR-014**: System MUST make a knowledge base's current index status (not started, indexing in progress, partially indexed, fully indexed, failed) visible to its owner at all times.
- **FR-015**: The system's search, ranking, and citation behavior MUST remain unchanged regardless of which vector storage backend is in use, such that switching backends is a configuration change rather than a behavior change visible to users.
- **FR-016**: When a document is deleted, archived, or has an earlier version restored, its affected chunks MUST be excluded from search results and citations from that point forward.

**Search & Retrieval**

- **FR-017**: Users MUST be able to perform a semantic search against one or more knowledge bases, returning chunks ranked by conceptual similarity to the query.
- **FR-018**: Users MUST be able to perform a keyword search against one or more knowledge bases, returning chunks containing literal query terms.
- **FR-019**: Users MUST be able to perform a hybrid search that combines semantic and keyword relevance into a single ranked result set.
- **FR-020**: Users MUST be able to select which search mode (semantic, keyword, or hybrid) applies to a given search or conversation; hybrid MUST be the default when unset.
- **FR-021**: Users MUST be able to scope a search to a single knowledge base, to multiple selected knowledge bases, or to explicitly exclude specific knowledge bases from an otherwise multi-knowledge-base search.
- **FR-022**: Users MUST be able to filter search results by document, language, date range, document version, and chunk-level metadata (e.g., section, heading).
- **FR-023**: Users MUST be able to configure retrieval depth (maximum results returned) and a minimum similarity threshold, per search or as a saved conversation-level default.
- **FR-024**: Users MUST be able to configure the maximum number of tokens retrieved context may consume; when retrieval would exceed it, the system MUST trim or omit lower-ranked chunks rather than exceeding the budget.
- **FR-025**: System MUST return a clear "no matching content" result, rather than an error or irrelevant filler, when no chunk meets the configured similarity threshold.

**Ranking**

- **FR-026**: System MUST rank semantic search results by cosine similarity between the query embedding and each chunk's embedding.
- **FR-027**: System MUST rank hybrid search results using a combined score that blends semantic similarity with keyword relevance.
- **FR-028**: System MUST support boosting ranked results using chunk or document metadata (e.g., recency, document category) in addition to base relevance score.
- **FR-029**: For every search result, the system MUST make available the factors that contributed to its ranking (e.g., similarity score, keyword match, applied boosts) so a user can see why a result was selected.

**Citations & Prompt Augmentation**

- **FR-030**: Every retrieved chunk used to ground an answer MUST carry a citation identifying its source document, document version, knowledge base, page number (if applicable), section, and chunk.
- **FR-031**: Users MUST be able to open a citation and view the source document at the cited page/section, with the matched passage visually highlighted.
- **FR-032**: Every AI response that used retrieved knowledge MUST display its citations alongside the response; responses that used no retrieved knowledge MUST NOT display citations.
- **FR-033**: System MUST assemble retrieved chunks, their source metadata, and knowledge base context into the prompt sent to the AI provider, trimming content as needed to respect the selected model's context window.
- **FR-034**: If a cited chunk's source document later becomes deleted or inaccessible to the user, the system MUST retain the citation on the original historical response while indicating the source is no longer available, rather than silently removing or breaking the citation.

**Conversation Integration**

- **FR-035**: Users MUST be able to attach zero, one, or multiple knowledge bases to a conversation, and this selection MUST determine whether and from where retrieval draws context for that conversation.
- **FR-036**: A conversation with no knowledge base attached MUST NOT perform retrieval or inject retrieved context into the prompt.
- **FR-037**: Users MUST be able to change a conversation's attached knowledge base(s) and retrieval settings at any time, with changes applying to subsequent messages only, leaving prior messages' citations and results unaffected.
- **FR-037a**: If retrieval is unavailable when a message is sent in a conversation with an attached knowledge base (e.g., the embedding provider or vector index cannot be reached), the system MUST still generate a response from the AI's general knowledge, MUST visibly label that response as not grounded in the knowledge base (with no citations attached), and MUST separately surface the retrieval failure as a non-silent, actionable error rather than a response that appears grounded when it is not.

**Background Processing**

- **FR-038**: Chunk generation, embedding generation, reindexing, cleanup of orphaned chunks/embeddings, and statistics computation MUST run as asynchronous background jobs that do not block the user's active workspace.
- **FR-039**: System MUST log every indexing job's stage transitions, including timestamp, triggering actor, and outcome, viewable by the knowledge base owner.
- **FR-040**: Failed background jobs MUST enter a retry queue with a bounded number of automatic retries, after which they surface as a failure requiring explicit user action.

**Dashboard & Analytics**

- **FR-041**: System MUST provide a retrieval dashboard, scoped to the requesting user's own knowledge bases, showing knowledge base, embedding, chunk, and storage statistics, current index status, and the processing queue.
- **FR-042**: System MUST track and display search analytics including search count, average retrieval time, average similarity score, per-knowledge-base and per-document usage, and counts of failed and empty searches.
- **FR-043**: Users MUST be able to view their own search history, including the query, mode used, knowledge bases searched, and result count.
- **FR-044**: System MUST identify and display the most-queried documents within a knowledge base.

**Security & Access Control**

- **FR-045**: System MUST return search and retrieval results only from knowledge bases the requesting user owns; if a search is scoped to include a knowledge base the user does not own, that knowledge base's content MUST be excluded entirely rather than causing an error or partial leak.
- **FR-046**: A chunk and its embedding MUST inherit the access permissions of their source document and knowledge base at query time, so a permission change takes effect on the next search without requiring reindexing.
- **FR-047**: System MUST log security-relevant retrieval events (e.g., an attempt to search or reindex a knowledge base the requester does not own) to an audit trail.
- **FR-048**: Search and citation responses MUST NOT include chunk content, embeddings, or metadata belonging to a knowledge base or document the requesting user is not authorized to access, including indirectly through a multi-knowledge-base search or a citation lookup.

**Performance**

- **FR-049**: System MUST index only new or changed content during an incremental reindex, leaving previously indexed, unchanged chunks untouched.
- **FR-050**: System MUST generate embeddings in batches rather than one chunk at a time, to support indexing large volumes of content efficiently.

**Accessibility**

- **FR-051**: The retrieval dashboard and search interface MUST conform to WCAG 2.2 AA, including full keyboard operability, visible focus states, correct ARIA roles/labels, and sufficient color contrast in both light and dark themes.

### Key Entities

- **DocumentChunk**: A segment of a document's extracted content produced by a chunking strategy, carrying its source document/version/knowledge base link, page/section/position, token/character counts, content hash, language, and creation date.
- **Embedding**: A vector representation of a chunk's content produced by a specific embedding provider/model, versioned so mismatched provider/model embeddings are never mixed in one ranked result set.
- **ChunkEmbedding**: The association between a DocumentChunk and its current (and historical) Embedding(s), supporting re-embedding without losing prior history until superseded.
- **VectorIndex**: The searchable index structure over Embeddings for a knowledge base, abstracted from its underlying storage backend so that backend can change without altering retrieval behavior.
- **EmbeddingProvider**: A configured embedding source (vendor, model, dimensionality, and whether it is cloud-hosted or local/self-hosted) available for a knowledge base to use when generating Embeddings; knowledge bases requiring data residency are restricted to local/self-hosted providers only.
- **IndexingJob**: A unit of background work indexing a knowledge base, document, or document version — tracked through queued, in-progress, completed, and failed states, supporting full, incremental, and version-scoped reindexing.
- **IndexingLog**: A timestamped record of a stage transition or event within an IndexingJob, forming the visible indexing history and retry queue.
- **RetrievalHistory**: A record of a retrieval performed on behalf of a conversation message, capturing the query, knowledge bases searched, configuration used (mode, depth, threshold, token budget), and timing.
- **RetrievalResult**: A single ranked chunk returned by a retrieval, including its relevance score, ranking factors (similarity, keyword match, boosts), and the RetrievalHistory it belongs to.
- **Citation**: A durable reference from an AI response or search result to a specific DocumentChunk's source document, version, knowledge base, page, and section — retained even if the underlying content later becomes inaccessible.
- **SearchHistory**: A record of a direct (non-conversation) search a user performed, including query, mode, scope, filters, and result count.
- **SearchAnalytics**: Aggregated, periodically computed metrics (search volume, average retrieval time, average similarity score, usage by knowledge base/document, failed/empty search counts) powering the retrieval dashboard.
- **ChunkStatistics**: Aggregated, periodically computed counts and storage metrics for chunks and embeddings within a knowledge base.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user asking a question in a conversation with an attached knowledge base that has relevant content receives a grounded, cited response within 5 seconds of the retrieval step starting, in at least 95% of queries.
- **SC-002**: At least 90% of search queries against a knowledge base containing relevant content return at least one relevant result within the top 5 results.
- **SC-003**: A user can trace any citation in an AI response or search result back to its exact source document and page/section in under 10 seconds.
- **SC-004**: A newly processed document becomes searchable within 5 minutes of the Document Intelligence Pipeline completing extraction, without any manual reindexing, in at least 95% of cases.
- **SC-005**: The system sustains at least 5 million indexed chunks for a single organization without a measurable increase in search response time.
- **SC-006**: Switching the underlying vector storage backend produces zero user-visible differences in how search, ranking, or citations behave.
- **SC-007**: 100% of retrieval and indexing failures produce a visible, actionable error with a working retry path — zero silent or unexplained failures.
- **SC-008**: Users can scope a search to a single knowledge base, multiple knowledge bases, or an explicit exclusion, and see correctly scoped results in 100% of attempts.
- **SC-009**: A knowledge base owner can identify why a given search result ranked where it did, using only the information displayed with the result, without external help.
- **SC-010**: The retrieval dashboard's statistics (chunk/embedding counts, index status, processing queue) remain accurate to within 5 seconds of the underlying state changing.
- **SC-011**: A first-time user can attach a knowledge base to a conversation and receive their first cited answer without external help in under 3 minutes.

## Assumptions

- **Initial embedding provider**: The specific initial cloud embedding vendor/model is a configuration detail rather than a fixed choice in this specification — the provider abstraction (FR-006, FR-007) is what this spec requires, consistent with the platform's existing multi-vendor AI provider strategy. Embedding generation is decoupled from the chat AI provider a user selects. A local/self-hosted embedding provider option must also exist at launch (not deferred to a future release) so knowledge bases with data-residency requirements can be designated accordingly (FR-009a); the specific self-hosted model is likewise a configuration detail.
- **Knowledge base access model**: Matches the existing Knowledge Base Management specification — knowledge bases are private to their owner in this release with no team/organization sharing yet, so "authorized to access" (Security) currently means "is the owner." The permission-inheritance requirement (FR-046) is written to extend automatically once knowledge base sharing ships.
- **Backfill of existing documents**: Documents already processed by the Document Intelligence Pipeline before this feature ships are NOT automatically indexed on rollout; each knowledge base owner must explicitly trigger an initial index (FR-010a, FR-011) before its existing content becomes searchable, spreading embedding cost/load over time and keeping the decision in the owner's control. Only documents processed after a knowledge base's first index has been triggered are indexed automatically going forward (FR-010).
- **Default retrieval configuration**: Hybrid search, a system-defined default retrieval depth and similarity threshold, and a system-defined default context token budget apply unless a user overrides them at the search or conversation level.
- **Cross-lingual search**: Query language and indexed content language may differ; semantic search relies on the embedding model's own cross-lingual capability. No separate machine-translation step is introduced by this specification.
- **Out of scope for this release**: Knowledge graph search, entity/relationship extraction, GraphRAG, agentic retrieval, cross-encoder/LLM re-ranking, personalized ranking, multi-modal (image/CAD/BIM/GIS) embeddings, and federated (cross-organization) search are explicitly deferred to future specifications; this specification's abstractions (chunk metadata fields, provider pattern, vector store abstraction) are designed not to block adding them later.
- **Click-through and explicit user feedback on search results** are deferred to a future specification, consistent with the source request's own "Future" designation for click analytics and user feedback.
- **Vector storage backend (supersedes this spec's original "initially SQL Server" input)**: Per [ADR-0007](../../docs/adr/0007-pinecone-vector-store-per-knowledge-base.md), Pinecone is the default vector store for newly created knowledge bases, selected per knowledge base via `KnowledgeBase.VectorStoreProvider` behind the `IVectorStore`/`IVectorStoreResolver` abstraction required by FR-015/SC-006. SQL Server remains a selectable per-knowledge-base alternative — used for existing (pre-ADR-0007) knowledge bases and for knowledge bases requiring data residency — and is expected to become the default again once the platform runs on Azure SQL Database or Microsoft Fabric, where the DML-compatible native vector index is supported.
