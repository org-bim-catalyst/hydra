# API Contract: Documents (Upload, Lifecycle, Metadata, Organization)

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `DocumentsController`, `[Authorize]`, `[EnableRateLimiting("document-endpoints")]` (new
policy). Route base: `/api/v1/documents`. Every response is scoped to the caller's own documents
(FR-048); a document the caller doesn't own returns `404`, identical to a nonexistent one
(mirrors `KnowledgeBaseOwnershipGuard`'s pattern — a new `DocumentOwnershipGuard`).

## Upload a document

`POST /api/v1/documents/uploads` — starts a resumable upload session (FR-005).

Request: `multipart/form-data` with `fileName`, `sizeBytes`, `contentType` metadata only (no
content yet). Response (`201 Created`):

```json
{ "uploadSessionId": "...", "chunkSizeBytes": 5242880, "expiresAtUtc": "..." }
```

`PUT /api/v1/documents/uploads/{uploadSessionId}/chunks/{chunkIndex}` — uploads one chunk
(research.md Decision 6). Request body: raw chunk bytes. Response: `202 Accepted` with
`{ "receivedChunkIndex": N, "nextExpectedChunkIndex": N+1 }` — the client uses this to resume
after an interruption (FR-005) without re-sending already-received chunks.

`POST /api/v1/documents/uploads/{uploadSessionId}/complete` — finalizes the upload once all
chunks are received. Server runs content validation (FR-010, reusing the existing
`IDocumentContentValidator` pattern extended for the new file types), computes the SHA-256
checksum (research.md Decision 8), and either:
- Returns `409 Conflict` with `{ "duplicateOfDocumentId": "...", "checksumMatch": true }` if a
  checksum match is found (FR-009) — the client then calls one of the two follow-up endpoints
  below to resolve it.
- Returns `201 Created` with the new `Document` (status `Uploaded`, immediately transitioning to
  `Queued` as processing is enqueued, FR-020).

`POST /api/v1/documents/uploads/{uploadSessionId}/complete-as-version?existingDocumentId={id}` —
resolves a duplicate by creating a new `DocumentVersion` on the existing document instead
(FR-009, see also `document-versions-folders-api.md`).

`POST /api/v1/documents/uploads/{uploadSessionId}/complete-as-new` — resolves a duplicate by
proceeding as a separate new `Document` anyway (FR-009).

`DELETE /api/v1/documents/uploads/{uploadSessionId}` — cancels an in-progress upload (FR-007);
any already-received chunks are deleted, never left orphaned (Edge Cases).

Single-file uploads under a small size threshold MAY skip the chunk dance and call
`POST /api/v1/documents/uploads/simple` with the full file in one request — same validation/
duplicate-detection path, fewer round trips (FR-001/FR-002/FR-003/FR-004 all funnel through
either the simple or chunked path; drag-and-drop/paste/multi-select are client-side concerns that
call one of these two per file).

## List / search the caller's documents

`GET /api/v1/documents`

| Param | Type | Notes |
|---|---|---|
| `view` | `Active` \| `Archived` \| `Deleted` (default `Active`) | Mirrors the KnowledgeBases contract's `view` semantics. |
| `folderId` | `guid?` | FR-033 — filter to one folder; omitted means "all folders." |
| `q` | `string?` | FR-035 — matches filename and metadata content. |
| `author` | `string?` | FR-036. |
| `language` | `string?` (ISO 639-1) | FR-036. |
| `tag` | `string?` (repeatable) | FR-036/FR-037. |
| `categoryId` | `guid?` | FR-036/FR-037. |
| `dateFrom` / `dateTo` | `date?` | FR-036. |
| `status` | `ProcessingStatus?` | FR-036. |
| `sort` | `Name` \| `RecentlyUpdated` \| `Created` \| `Size` (default `RecentlyUpdated`) | |
| `cursor` / `pageSize` | as KnowledgeBases contract | Constitution §6 pagination. |

Response (`200 OK`): `PagedResult<DocumentSummaryDto>` with `id, fileName, fileType, sizeBytes,
processingStatus, folderId, categoryName, languagePrimary, tags[], isArchived, createdAtUtc,
lastUpdatedAtUtc`. An empty `items` array is the normal "no results" contract, not an error.

## Get one document (detail view)

`GET /api/v1/documents/{id}` → `DocumentDetailDto` — everything in the summary plus
`extractedText`, `extractedStructure` (from `DocumentVersion.ExtractedStructureJson`),
`metadata` (`DocumentMetadataDto`, including `isAutoExtracted` per field group), `languages[]`
(primary + secondary with confidence), `classification` (`categoryName`, `source`,
`confidenceScore`), `currentVersion` summary, and `rowVersion` (opaque token the client echoes
back on metadata edits — research.md Decision 9).

## Edit metadata

`PATCH /api/v1/documents/{id}/metadata`

Request: `{ "rowVersion": "...", "title": "...", "author": "...", "creationDate": "...", ... }`
(only supplied fields are changed). Response (`200 OK`):
`{ "metadata": { ... }, "rowVersion": "...", "wasStale": false }`. `wasStale: true` (FR-031a,
research.md Decision 9) means the save succeeded but the caller's view was out of date when they
submitted — the client shows the "your view was out of date" warning and refreshes.

## Classification override

`PUT /api/v1/documents/{id}/classification` — Request: `{ "categoryId": "..." }`. Response
(`200 OK`): the updated `DocumentClassification` with `source: "UserOverride"` (FR-026).

## Tags

`POST /api/v1/documents/{id}/tags` — `{ "name": "..." }` (creates the tag for this user if it
doesn't already exist, then attaches it). `DELETE /api/v1/documents/{id}/tags/{tagName}`.
(FR-032). `GET /api/v1/documents/tags` lists the caller's own tags for filter/autocomplete UI.

## Rename / archive / restore / delete / duplicate / move

- `PATCH /api/v1/documents/{id}` — `{ "fileName": "..." }` (FR-019).
- `POST /api/v1/documents/{id}/actions/archive` / `.../restore` (FR-016).
- `DELETE /api/v1/documents/{id}` — soft delete (FR-017).
- `POST /api/v1/documents/{id}/actions/duplicate` → `201 Created` with the new `Document`
  (independent file copy, metadata, tags; processing history starts fresh — FR-034).
- `PATCH /api/v1/documents/{id}/folder` — `{ "folderId": "..." }` (null = move to root) (FR-033).

## Download

`GET /api/v1/documents/{id}/download?versionId={optional}` → `200 OK` with
`{ "url": "...", "fileName": "..." }` — a signed, time-limited URL from the existing
`ISignedUrlService` (FR-018, FR-050), never a physical path (FR-015). **Not a redirect**: this
endpoint requires `[Authorize]`, but a browser's plain navigation to a redirect target never
attaches a Bearer token — found during implementation. The client calls this over an
authenticated request, then separately navigates to the returned URL, which points at
`GET /api/v1/documents/versions/{versionId}/download-content?exp=...&sig=...` — an
`[AllowAnonymous]` endpoint whose signature check is itself the authorization (mirrors
`UsersController`'s avatar download pattern). Omitting `versionId` downloads the current
version; passing it downloads a specific prior version (FR-040).

## Preview

`GET /api/v1/documents/{id}/preview` → `DocumentPreviewDto`:
`{ "previewType": "PageImage" | "Thumbnail" | "StructuredContent" | "Unavailable", "url": "...",
"structuredContent": null }`. `Unavailable` (FR-044) means the client shows "no preview
available" and offers the download action instead — never an error state.
