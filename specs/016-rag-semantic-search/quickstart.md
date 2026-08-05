# Quickstart: Validating the RAG & Semantic Search Engine

**Feature**: [spec.md](./spec.md) | **Data model**: [data-model.md](./data-model.md) |
**Contracts**: [contracts/](./contracts/)

Manual/scripted validation scenarios proving the feature works end-to-end, mapped to the spec's
user stories and success criteria. Run after implementation, before marking the feature done
(constitution §19 Definition of Done).

## Prerequisites

- Solution built and running locally (`dotnet run` for `AskLucy.Web`), against a SQL Server
  instance that supports the native `vector` type and full-text search (SQL Server 2025+/Azure SQL
  — research.md Decision 3), with this feature's migration applied (new `Retrieval` tables plus
  additive columns on `KnowledgeBase`, `KnowledgeBaseDocument`, `UserChat`, `Chats.Citation`).
- A cloud embedding provider credential configured (default `EmbeddingProvider` row, OpenAI) and
  the local ONNX embedding model files present (research.md Decision 5) so both `HostingType`
  options are exercisable.
- A logged-in test user with: (a) an `Active` knowledge base containing at least two documents
  already processed by the Document Intelligence Pipeline (specs/015 — `Document.ProcessingStatus
  = Completed`), and (b) a second, separate user account to validate ownership scoping.
- At least one document with content spanning multiple pages/sections, to exercise page/section
  citation display.
- Ability to simulate the embedding provider being unreachable (e.g., toggle a feature flag or
  point configuration at an invalid endpoint) for the degraded-mode scenario.

## Scenario 1 — Chat with your documents and get cited answers (User Story 1 / SC-001, SC-003, SC-011)

1. Attach the prerequisite knowledge base to a new conversation
   (`PUT /api/v1/chats/{id}/knowledge-bases`); trigger its initial index and wait for
   `indexStatus: "Indexed"` (this exercises Scenario 5 as a precondition).
2. Ask a question whose answer exists in the indexed content. Confirm the response is grounded and
   displays a citation with document, version, knowledge base, page, and section (FR-030, US1 AC1).
   Time from send to first citation-bearing response — should be within 5 seconds of retrieval
   starting (SC-001).
3. Attach a second knowledge base and ask a question spanning both; confirm citations correctly
   attribute each passage to its source knowledge base (US1 AC2).
4. Start a new conversation with no knowledge base attached; ask a question; confirm the answer has
   no citations and no retrieval occurred (US1 AC3, FR-036).
5. In the first conversation, ask something with no relevant content in either attached knowledge
   base; confirm the system clearly states nothing relevant was found rather than presenting an
   ungrounded answer as if it were grounded (US1 AC4, FR-025).
6. Open a citation from step 2; confirm the source document opens at the cited page/section with
   the matched passage highlighted, in under 10 seconds (US1 AC5, SC-003).
7. **Degraded-mode check**: simulate the embedding provider being unreachable, then send a message
   in the attached-knowledge-base conversation. Confirm the AI still responds (from general
   knowledge), the response is visibly labeled as not grounded with no citations, and a separate
   non-silent retrieval-failure indicator appears (US1 AC6, FR-037a, research.md Decision 8).
   Restore the provider afterward.

**Pass condition**: matches spec.md User Story 1's six acceptance scenarios.

## Scenario 2 — Search a knowledge base directly (User Story 2 / SC-002, SC-008)

1. Run a semantic search using words that don't literally appear in the source text but describe
   the same concept; confirm relevant chunks return, ranked by conceptual similarity (US2 AC1).
2. Run a keyword search for an exact term known to appear verbatim; confirm matching chunks return
   (US2 AC2).
3. Run the same query in hybrid mode; confirm the result ordering reflects a blend of both signals,
   not identical to either pure-mode result set (US2 AC3).
4. Scope a search to a single knowledge base (with a second knowledge base also indexed); confirm
   only the scoped knowledge base's results appear (US2 AC4, SC-008). Then search across all
   knowledge bases while explicitly excluding one; confirm the excluded knowledge base contributes
   no results (US2 AC5).
5. Search for something with no relevant content anywhere; confirm a clear "no results" state, not
   an error or irrelevant filler (US2 AC6, FR-025).
6. Across at least 10 queries against content known to be relevant, confirm at least 90% return a
   relevant result in the top 5 (SC-002, representative spot-check, not exhaustive).

**Pass condition**: matches spec.md User Story 2's six acceptance scenarios.

## Scenario 3 — Control how retrieval works (User Story 3)

1. Lower `topK` for the conversation to 2 and ask a question with more than 2 plausibly relevant
   chunks available; confirm the answer/citations reflect only ~2 chunks considered (US3 AC1).
2. Raise `similarityThreshold`; confirm a subsequent search returns fewer, more strictly relevant
   results than before (US3 AC2).
3. Switch a conversation's search mode to `Keyword`; confirm subsequent questions use literal
   matching until changed again (US3 AC3).
4. Set a small `maxContextTokens` budget; confirm lower-ranked chunks are trimmed rather than the
   budget being exceeded (check the retrieval history's chunk count against what would otherwise
   have been returned) (US3 AC4, FR-024).
5. Change the conversation's knowledge base/retrieval settings, then scroll to an earlier message;
   confirm its citations/results are unchanged by the new settings (US3 AC5, FR-037).

**Pass condition**: matches spec.md User Story 3's five acceptance scenarios.

## Scenario 4 — See why a result was selected (User Story 4)

1. Inspect a hybrid search result; confirm both a semantic-score and keyword-score contribution
   are visible (US4 AC1).
2. Configure a metadata boost (e.g., recency) on the knowledge base's ranking, run a search, and
   confirm a boosted result discloses the boost as a contributing factor (US4 AC2).
3. Confirm displayed results are ordered consistently with their disclosed scores (US4 AC3).

**Pass condition**: matches spec.md User Story 4's three acceptance scenarios.

## Scenario 5 — Keep knowledge bases automatically searchable (User Story 5 / SC-004)

1. Upload a new document to a knowledge base whose initial index has already run; confirm it
   becomes searchable without any manual reindex action, and time it — under 5 minutes of
   Document-Intelligence-Pipeline extraction completing (US5 AC1, SC-004).
2. Replace that document with a new version; confirm the new version's content becomes searchable
   and the prior version's chunks no longer appear in default search results (US5 AC2).
3. Delete or archive a document; confirm its chunks stop appearing in search results (US5 AC3,
   FR-016).
4. Check the knowledge base's index status at each step above; confirm it accurately reflects
   `Indexing`/`Indexed`/etc. throughout (US5 AC4, FR-014).

**Pass condition**: matches spec.md User Story 5's four acceptance scenarios.

## Scenario 6 — Manually manage indexing (User Story 6)

1. On a knowledge base with `IndexStatus: NotIndexed` (a fresh one, or a pre-existing one from
   before this feature shipped), trigger the initial index and confirm existing documents were
   NOT already searchable beforehand (FR-010a) but become so only after this explicit trigger
   (US6 AC1, confirms the opt-in-backfill clarification).
2. Change the knowledge base's chunking strategy (`PUT /api/v1/knowledge-bases/{id}/retrieval-settings`);
   confirm a full reindex is triggered automatically, with no separate manual reindex action
   required, and all documents are re-chunked and re-embedded (US6 AC1, FR-004).
3. Add one new document and trigger an incremental reindex; confirm only the new content is
   processed (check `IndexingLog` timestamps show no re-processing of unchanged chunks) (US6 AC2,
   FR-049).
4. Force an indexing failure (e.g., temporarily break the embedding provider mid-job); confirm the
   processing queue shows the failure with an actionable reason and a working retry action (US6
   AC3, FR-013).
5. Reindex a single document version independently; confirm other versions'/documents' index state
   is unaffected (US6 AC4, FR-012).
6. **Concurrency check**: trigger a full reindex, then immediately attempt a second reindex trigger
   on the same knowledge base from another session; confirm the second is rejected with `409
   Conflict` rather than starting a concurrent duplicate job (Edge Cases, §5 Concurrency).

**Pass condition**: matches spec.md User Story 6's four acceptance scenarios plus the concurrency
edge case.

## Scenario 7 — Monitor retrieval and search activity (User Story 7 / SC-010)

1. Perform a mix of successful, empty, and (simulated) failed searches over a knowledge base, then
   open the retrieval dashboard; confirm search count, average retrieval time, average similarity
   score, and failed/empty counts are all present and accurate (US7 AC1, FR-042).
2. Confirm the dashboard's chunk count, embedding count, and storage usage for the knowledge base
   match what indexing actually produced (US7 AC2, FR-041).
3. Search for specific documents' content repeatedly, then confirm those documents appear ranked
   in "most-queried documents" (US7 AC3, FR-044).
4. Open search history; confirm prior queries, mode, knowledge bases searched, and result counts
   are all listed (US7 AC4, FR-043).
5. Trigger a state change (e.g., a new search) and confirm the dashboard reflects it within 5
   seconds (SC-010).

**Pass condition**: matches spec.md User Story 7's four acceptance scenarios.

## Cross-cutting checks

- **Data residency**: mark a knowledge base `RequiresDataResidency: true`; confirm only
  `Local`-hosted embedding providers can be assigned to it (attempting to assign a `Cloud`
  provider is rejected server-side, not just hidden in the UI) (FR-009a, research.md Decision 5).
- **Security**: confirm a search scoped to include a knowledge base the caller does not own
  excludes that knowledge base's content entirely rather than erroring or leaking partial results
  (FR-045, FR-048); confirm the same for a direct citation lookup by id against another user's
  citation.
- **No silent failures**: confirm every failure path exercised above (indexing failure, retrieval
  outage, embedding generation failure) surfaces a visible, actionable message — none are silent,
  console-only, or generic (constitution §2.VIII, SC-007).
- **Accessibility**: run automated a11y checks (axe or equivalent) against the search interface,
  retrieval dashboard, and citation viewer; verify keyboard-only operation of search, filters, and
  citation navigation (FR-051).
- **Scale spot-check**: seed a representative sample of chunks (not the full 5M of SC-005) and
  confirm search response time doesn't visibly degrade versus a small dataset; confirm the vector
  index (research.md Decision 3) is actually being used (check the query plan) rather than a
  full scan.
- **Vector store abstraction (SC-006)**: confirm no `Application`/`Domain` code references SQL
  Server-specific vector syntax directly (only `Infrastructure/Retrieval/SqlServerVectorStore`
  does) — a static/architecture-test check, not a runtime scenario, verifying FR-015's swap-without-
  behavior-change requirement is structurally true, not just asserted.
