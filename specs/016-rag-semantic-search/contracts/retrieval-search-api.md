# API Contract: Search

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `RetrievalSearchController` (`/api/v1/retrieval/search`). Rate-limited via
`retrieval-search-endpoints` (research.md Decision 12). Every response is scoped to knowledge
bases the caller owns — a request that names an unowned/inaccessible knowledge base excludes it
from the result set silently rather than returning `403`/`404` for that portion (FR-045, FR-048).

## Search

`POST /api/v1/retrieval/search`

```json
{
  "query": "what is the minimum rebar cover for exterior slabs?",
  "mode": "Hybrid",
  "knowledgeBaseIds": ["kb-1", "kb-2"],
  "excludeKnowledgeBaseIds": [],
  "topK": 10,
  "similarityThreshold": 0.72,
  "maxContextTokens": 4000,
  "filters": {
    "documentIds": null,
    "language": null,
    "dateFrom": null,
    "dateTo": null,
    "documentVersionId": null,
    "section": null
  },
  "cursor": null,
  "pageSize": 20
}
```

(FR-017–FR-025, US2/US3). `mode` is one of `Semantic`, `Keyword`, `Hybrid`; omitted defaults to the
caller's `UserChat.RetrievalSearchMode` or, absent that, the system default `Hybrid` (FR-020).
`knowledgeBaseIds` empty/omitted means "all knowledge bases the caller owns"; entries in
`excludeKnowledgeBaseIds` are removed from that set (FR-021). Cursor-paginated (constitution §6).

Response → `SearchResultPageDto`:

```json
{
  "results": [
    {
      "chunkId": "...",
      "documentId": "...",
      "documentVersionId": "...",
      "knowledgeBaseId": "...",
      "documentTitle": "Structural Spec Section 03 30 00.pdf",
      "knowledgeBaseName": "BIM Standards",
      "pageNumber": 42,
      "section": "3.2 Cover Requirements",
      "excerpt": "...minimum concrete cover for reinforcement in exterior slabs is 50mm...",
      "highlightRanges": [[38, 61]],
      "relevanceScore": 0.91,
      "semanticScore": 0.88,
      "keywordScore": 0.94,
      "boostFactors": { "recency": 0.02 },
      "rank": 1
    }
  ],
  "nextCursor": null,
  "totalCount": 1
}
```

(FR-026–FR-029, US4 — every result carries its own ranking rationale, satisfying "see why results
were selected" without a separate call). When no chunk meets `similarityThreshold`, `results` is an
empty array with `totalCount: 0`, not an error (FR-025, US2 AC6).

## Citation lookup

`GET /api/v1/retrieval/citations/{citationId}` → `CitationDetailDto`:

```json
{
  "citationId": "...",
  "documentTitle": "Structural Spec Section 03 30 00.pdf",
  "knowledgeBaseName": "BIM Standards",
  "pageNumber": 42,
  "section": "3.2 Cover Requirements",
  "excerpt": "...",
  "sourceAvailable": true,
  "sourceDocumentUrl": "https://.../signed-preview-url?...",
  "highlightRanges": [[38, 61]]
}
```

(FR-030–FR-034, US1 AC5, US4). `sourceAvailable: false` when the underlying `DocumentChunk`/
`Document` has since become deleted/inaccessible to the caller — the snapshot fields
(`documentTitle`, `knowledgeBaseName`, `pageNumber`, `section`, `excerpt`) still render from
`Citation`'s own stored values (data-model.md "Extended Entities — Citation"); `sourceDocumentUrl`
is omitted in that case rather than pointing at a broken link.

## Search history

`GET /api/v1/retrieval/search/history?cursor=...&pageSize=50` → paginated `SearchHistoryDto[]`
(`id, query, mode, knowledgeBaseIds, resultCount, createdAtUtc`), newest-first, scoped to the
caller (FR-043, US7 AC4).
