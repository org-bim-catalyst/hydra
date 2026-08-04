# API Contract: Folders & Documents

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Same `KnowledgeBasesController`, same auth/rate-limit policy as
[knowledge-bases-api.md](./knowledge-bases-api.md), nested under
`/api/v1/knowledge-bases/{knowledgeBaseId}/...`. Every route below 404s if the parent
knowledge base doesn't exist or isn't owned by the caller (`KnowledgeBaseOwnershipGuard`
applied first, before any folder/document lookup).

## Folder tree

`GET /api/v1/knowledge-bases/{knowledgeBaseId}/folders`

Returns the full folder tree plus root-level documents in one response (small trees — no
pagination; a knowledge base with an unusually large flat folder count is out of this
endpoint's stated scale target, which is documents-per-knowledge-base, not folders):

```json
{
  "folders": [
    { "id": "...", "parentFolderId": null, "name": "2026 Contracts", "depth": 0,
      "documentCount": 3, "childFolderCount": 1 }
  ],
  "rootDocuments": [ { "id": "...", "fileName": "policy.pdf", "sizeBytes": 204800 } ]
}
```

## Create / rename / delete a folder

- `POST /api/v1/knowledge-bases/{knowledgeBaseId}/folders` — body
  `{ "name": "Client A", "parentFolderId": null }`. `201 Created`. `400` if the resulting
  `Depth` would exceed `MaxNestingDepth` (FR-012, spec.md Edge Cases — "block the action and
  explain the limit").
- `PATCH /api/v1/knowledge-bases/{knowledgeBaseId}/folders/{folderId}` — body
  `{ "name": "..." }`. `200`.
- `DELETE /api/v1/knowledge-bases/{knowledgeBaseId}/folders/{folderId}` — body
  `{ "confirm": false }` by default. `409` (not `400` — the request is well-formed, but the
  precondition "folder is empty, or caller has confirmed" isn't met) if the folder is
  non-empty and `confirm` is not `true` (FR-015); the `409` body's `detail` states what the
  folder contains so the client can render the confirmation prompt without a second round
  trip. `204` on success.

## Move a folder

`POST /api/v1/knowledge-bases/{knowledgeBaseId}/folders/{folderId}/actions/move`

Body: `{ "newParentFolderId": null }` (null = move to root). `200` with the updated folder.
`409` if the target is the folder itself or one of its own descendants (FR-013) — the
response `detail` explains why, matching spec.md's edge case requirement to explain the
rejection, not just reject silently.

## List documents in a folder (or at root)

`GET /api/v1/knowledge-bases/{knowledgeBaseId}/documents?folderId={folderId?}&cursor=&pageSize=`

`folderId` omitted/null = root-level documents only (not the whole tree — the frontend calls
this once per expanded folder, consistent with the tree view being lazily loaded).
`PagedResult<KnowledgeBaseDocumentDto>`.

## Upload a document

`POST /api/v1/knowledge-bases/{knowledgeBaseId}/documents`

`multipart/form-data`: `file` (binary), `folderId` (optional form field, null = root).

Validation (research.md Decision 8, constitution §8):
- `413` if the file exceeds `KnowledgeBaseDocumentOptions.MaxFileSizeBytes`.
- `400` if the file's magic-byte signature doesn't match one of the supported types (PDF,
  Word, Excel, PowerPoint, Markdown, CSV, Text) — rejected with which type was detected vs.
  expected, not a generic "invalid file" message (constitution §2.VIII — actionable, not
  silent).

`201 Created`:

```json
{ "id": "...", "fileName": "standards.pdf", "sizeBytes": 204800, "contentType": "application/pdf",
  "pageCount": 24, "processingStatus": "Ready", "folderId": null, "uploadedAtUtc": "..." }
```

`pageCount` may be `null` and `processingStatus` may be `"Failed"` on a page-count extraction
failure (research.md Decision 5) — the upload itself still succeeds (`201`); this is not an
error response.

## Move / delete a document

- `POST /api/v1/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/actions/move` — body
  `{ "newFolderId": null }`. `200`.
- `DELETE /api/v1/knowledge-bases/{knowledgeBaseId}/documents/{documentId}` — soft delete
  (recoverable the same way the knowledge base itself is, per data-model.md). `204`.

## Frontend usage note

The folder tree (`KnowledgeBaseFolderTree.tsx`) drives both mouse drag-and-drop and the
`@dnd-kit` keyboard sensor (research.md Decision 6, FR-040) against the same `actions/move`
endpoints above — there is no separate "keyboard move" API; the accessibility requirement is
satisfied entirely by the frontend interaction layer offering an equivalent (e.g., a "Move
to…" menu action) that calls the identical endpoint a mouse drag would.
