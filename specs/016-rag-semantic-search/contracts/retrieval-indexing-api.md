# API Contract: Indexing

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `RetrievalIndexingController`, nested under the existing `/api/v1/knowledge-bases/{id}`
resource (constitution §6 sub-resource action convention, mirrors `DocumentProcessingController`).
Rate-limited via `retrieval-indexing-endpoints` (research.md Decision 12, tighter than search given
reindex cost). Ownership enforced identically to every other `KnowledgeBases` endpoint (FR-045).

## Update retrieval settings

`PUT /api/v1/knowledge-bases/{id}/retrieval-settings`

```json
{
  "chunkingStrategy": "Recursive",
  "embeddingProviderId": "...",
  "requiresDataResidency": false
}
```

(FR-001, FR-004, FR-009a — added during `/speckit-analyze` remediation, finding G1: this was the
missing mutation entry point for the `ChunkingStrategy`/`EmbeddingProviderId`/
`RequiresDataResidency` fields data-model.md already defined on `KnowledgeBase`). Returns `400 Bad
Request` if `requiresDataResidency: true` and `embeddingProviderId` resolves to a `Cloud`-hosted
provider (FR-009a). If `chunkingStrategy` or `embeddingProviderId` actually changes from its prior
value, this call automatically enqueues a `FullReindex` `IndexingJob` — no separate manual reindex
trigger is required, satisfying FR-004's "without requiring a separate manual trigger beyond...
the strategy change itself" (resolves the earlier quickstart.md/FR-004 inconsistency, finding I1).

`GET /api/v1/knowledge-bases/{id}/retrieval-settings` → the current values, each resolved
provider's `hostingType` included so the UI can show which embedding provider is active.

## Trigger an initial index

`POST /api/v1/knowledge-bases/{id}/index/actions/initial-index` (FR-010a, FR-011, US6 AC1).
Requires `KnowledgeBase.Status = Active` — `409 Conflict` with `{ "reason": "NotActive" }`
otherwise (existing `KnowledgeBase.Activate()` precondition). Requires `IndexStatus = NotIndexed`
or `Failed` — `409 Conflict` with `{ "reason": "AlreadyIndexing" }` if a job is already
`InitialIndexQueued`/`Indexing` (§5 Concurrency, Edge Case: concurrent triggers). Creates a
`Document`/`DocumentVersion` for every `KnowledgeBaseDocument` in this knowledge base lacking one
(research.md Decision 2), then chunks and embeds every resulting document.

## Trigger a reindex

`POST /api/v1/knowledge-bases/{id}/index/actions/reindex`

```json
{ "mode": "Incremental" }
```

`mode` is `Full` or `Incremental` (FR-011, US6 AC1–AC2). Same `409 Conflict` concurrency guard as
above. A `Full` reindex re-chunks/re-embeds every document regardless of content-hash match; an
`Incremental` reindex only processes new/changed content (FR-005, FR-049).

## Reindex a single document version

`POST /api/v1/knowledge-bases/{id}/documents/{documentId}/versions/{versionId}/actions/reindex`
(FR-012, US6 AC4). Independent of the knowledge base's own `IndexStatus` — does not require or
change the knowledge-base-level concurrency guard above.

## Index status

`GET /api/v1/knowledge-bases/{id}/index` → `KnowledgeBaseIndexStatusDto`:

```json
{
  "knowledgeBaseId": "...",
  "indexStatus": "Indexing",
  "lastIndexedAtUtc": "2026-08-05T10:14:00Z",
  "currentJob": {
    "jobId": "...",
    "jobType": "IncrementalReindex",
    "status": "InProgress",
    "startedAtUtc": "...",
    "stages": [
      { "stage": "Chunking", "status": "Completed" },
      { "stage": "EmbeddingGeneration", "status": "InProgress" },
      { "stage": "VectorWrite", "status": "Pending" }
    ]
  },
  "failureReason": null
}
```

(FR-014, US5 AC4). `failureReason` is populated (and `indexStatus: "Failed"`) with a specific,
actionable message when the current/last job failed (FR-013).

## Retry a failed indexing job

`POST /api/v1/knowledge-bases/{id}/index/actions/retry` (FR-013, FR-040, US6 AC3). `409 Conflict`
with `{ "reason": "NotInFailedState" }` if the knowledge base's current job is not `Failed`,
mirroring `DocumentProcessingController`'s identical retry guard.

## Indexing history

`GET /api/v1/knowledge-bases/{id}/index/history?cursor=...&pageSize=50` → paginated
`IndexingLogDto[]` (`id, jobId, stage, status, message, occurredAtUtc`), newest-first (FR-039).

## Real-time push (SignalR)

Hub route: `/hubs/retrieval-indexing` (research.md Decision 7, mirrors `DocumentProcessingHub`).
Clients join a group keyed by their own server-assigned user id.

| Event | Payload | Fires when |
|---|---|---|
| `knowledgeBaseIndexStatusChanged` | `{ knowledgeBaseId, indexStatus }` | `KnowledgeBase.IndexStatus` transitions (US5 AC4). |
| `indexingStageChanged` | `{ knowledgeBaseId, jobId, stage, status }` | An `IndexingLog` stage transition (US6 AC1–AC2). |
| `indexingJobFailed` | `{ knowledgeBaseId, jobId, failureReason }` | An `IndexingJob` becomes `Failed`. |

The client also polls `GET /api/v1/knowledge-bases/{id}/index` on a 5-second interval via TanStack
Query as a reconciliation fallback (same principle as `document-processing-api.md`) — a missed
push event is never the sole source of truth.
