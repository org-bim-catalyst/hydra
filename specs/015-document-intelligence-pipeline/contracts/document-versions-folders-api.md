# API Contract: Document Versions & Folders

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Part of `DocumentsController` (folders) and a new `DocumentVersionsController` nested under
`/api/v1/documents/{documentId}/versions` — same `[Authorize]`/ownership-guard/rate-limit
pattern as `documents-api.md`.

## Replace a document (create a new version)

`POST /api/v1/documents/{documentId}/versions` — same upload session flow as
`documents-api.md`'s upload endpoints (`uploads` → chunks → `complete`), but targeted at an
existing document via `?documentId={id}`. Request additionally carries
`{ "versionIncrement": "Major" | "Minor" }` (FR-039). Response (`201 Created`): the new
`DocumentVersion`, with `Document.CurrentVersionId` repointed to it; the prior version's file and
extracted content remain retrievable, untouched (FR-038). Processing (FR-020) is enqueued for
the new version independently of the prior version's already-completed job.

If a new-version upload targeting this document is already in progress, a version-restore
request (below) against the same document returns `409 Conflict` with
`{ "reason": "VersionUploadInProgress" }` until that upload finishes (Edge Cases — deterministic
resolution, never a corrupted version history).

## Version timeline

`GET /api/v1/documents/{documentId}/versions` → array of `DocumentVersionSummaryDto`
(`id, versionLabel ("2.1"), sizeBytes, createdAtUtc, createdByUserId, isCurrent`), ordered
newest-first (FR-040).

## Compare two versions

`GET /api/v1/documents/{documentId}/versions/compare?fromVersionId={a}&toVersionId={b}` →
`{ "extractedTextDiff": "...", "metadataDiff": { "title": { "from": "...", "to": "..." }, ... } }`
(FR-042 — at minimum extracted text and metadata, per spec.md).

## Restore a prior version

`POST /api/v1/documents/{documentId}/versions/{versionId}/actions/restore` — repoints
`Document.CurrentVersionId` to `versionId`; no version row is deleted (FR-041). Returns `409
Conflict` (`VersionUploadInProgress`) if a replacement upload is currently in flight for this
document, per the Edge Case above.

---

## Folders

`POST /api/v1/documents/folders` — `{ "name": "...", "parentFolderId": "..." }` (FR-033).

`PATCH /api/v1/documents/folders/{id}` — rename.

`PATCH /api/v1/documents/folders/{id}/parent` — `{ "parentFolderId": "..." }` (move; rejects
moving a folder into itself or its own descendant).

`DELETE /api/v1/documents/folders/{id}?onContainedDocuments={MoveToParent|ArchiveAll|DeleteAll}`
— the `onContainedDocuments` parameter is required whenever the folder is non-empty; omitting it
for a non-empty folder returns `400 Bad Request` rather than silently choosing a default,
satisfying the Edge Case's "explicit, non-silent handling" requirement (FR-033).

`GET /api/v1/documents/folders/tree` → the caller's full folder hierarchy (id, name,
parentFolderId, depth, documentCount), used to render folder navigation.
