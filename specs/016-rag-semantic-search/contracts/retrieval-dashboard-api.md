# API Contract: Retrieval Dashboard & Analytics

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `RetrievalDashboardController` (`/api/v1/retrieval/dashboard`). Rate-limited via
`retrieval-search-endpoints` (read-only, low-cost, same tier as search). Scoped to the requesting
user's own knowledge bases throughout (FR-041) — no organization-wide variant in this release
(unlike `document-processing-api.md`'s administrator view; nothing in spec.md requests one here).

## Dashboard summary

`GET /api/v1/retrieval/dashboard` → `RetrievalDashboardSummaryDto`:

```json
{
  "knowledgeBases": [
    {
      "knowledgeBaseId": "...",
      "name": "BIM Standards",
      "indexStatus": "Indexed",
      "totalChunks": 4820,
      "totalEmbeddings": 4820,
      "storageBytes": 62914560,
      "lastIndexedAtUtc": "2026-08-05T10:14:00Z"
    }
  ],
  "processingQueue": [
    { "knowledgeBaseId": "...", "jobId": "...", "jobType": "IncrementalReindex", "status": "InProgress" }
  ],
  "searchAnalytics": {
    "searchCount": 342,
    "averageRetrievalTimeMs": 410,
    "averageSimilarityScore": 0.81,
    "failedSearchCount": 2,
    "emptySearchCount": 15
  }
}
```

(FR-041, FR-042, US7 AC1–AC2). Backed by `ChunkStatistics`/`SearchAnalytics`
(data-model.md), refreshed on the same periodic-recompute cadence as `DocumentStatistics`
(specs/015 precedent), satisfying SC-010's 5-second accuracy budget.

## Most-queried documents

`GET /api/v1/retrieval/dashboard/top-documents?knowledgeBaseId=...&limit=10` →

```json
{
  "documents": [
    { "documentId": "...", "title": "Structural Spec Section 03 30 00.pdf", "queryCount": 58 }
  ]
}
```

(FR-044, US7 AC3). `knowledgeBaseId` optional — omitted aggregates across all of the caller's
knowledge bases.

## Embedding status

`GET /api/v1/retrieval/dashboard/embedding-status?knowledgeBaseId=...` →

```json
{
  "totalChunks": 4820,
  "chunksWithCurrentEmbedding": 4790,
  "chunksPendingEmbedding": 30,
  "embeddingProvider": { "vendor": "OpenAI", "modelKey": "text-embedding-3-small", "hostingType": "Cloud" }
}
```

(FR-041, FR-008 visibility — lets an owner see when a provider/model change has left chunks
pending re-embedding).
