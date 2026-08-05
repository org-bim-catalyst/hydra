# Quickstart: Validating the Document Intelligence Pipeline

**Feature**: [spec.md](./spec.md) | **Data model**: [data-model.md](./data-model.md) |
**Contracts**: [contracts/](./contracts/)

Manual/scripted validation scenarios proving the feature works end-to-end, mapped to the spec's
user stories and success criteria. Run after implementation, before marking the feature done
(constitution §19 Definition of Done).

## Prerequisites

- Solution built and running locally (`dotnet run` for `AskLucy.Web`), against a local SQL Server
  instance with this feature's migration applied (Hangfire's own SQL Server schema, plus the new
  `Documents` tables) and the seeded `DocumentCategory` taxonomy.
- A logged-in test user, and a second user account to validate ownership scoping; a third
  administrator-role account for the organization-wide dashboard scenario.
- Sample files covering: a text-layer PDF, a scanned/image-only PDF (for OCR), a `.docx`, `.xlsx`,
  `.pptx`, `.png`, `.md`, plus one deliberately mislabeled file (e.g., a `.txt` renamed to
  `.pdf`) and one password-protected PDF.
- Ability to simulate a server/worker restart mid-processing (e.g., stop and restart the
  `AskLucy.Web` process, or the Hangfire server component, while a job is `InProgress`) for the
  durability scenario.

## Scenario 1 — Upload and manage documents (User Story 1 / SC-001, SC-005)

1. Drag-and-drop a small PDF onto the upload area. Confirm a progress indicator appears and the
   document shows up in the list within 5 seconds of upload completion (SC-001).
2. Select 3 files at once; confirm all queue and upload with independent progress, and cancelling
   one mid-upload leaves the others unaffected (FR-002, FR-007).
3. Rename, download, archive, restore, then soft-delete the document; confirm each action is
   reflected immediately and the deleted document is recoverable, not gone (FR-016, FR-017,
   FR-019).
4. Upload a large (>100MB) file and interrupt the connection partway through; resume it and
   confirm it continues from the last received chunk rather than restarting (FR-005, SC-005 —
   time the retry to confirm no re-transfer of already-sent data).

**Pass condition**: matches spec.md User Story 1's five acceptance scenarios.

## Scenario 2 — Automatic processing with visible status (User Story 2 / SC-002, SC-006, SC-008, SC-009)

1. Upload the text-layer PDF and the scanned/image-only PDF. Watch the processing dashboard/
   detail view move through stages (Validation → Ocr/skip → TextExtraction → MetadataExtraction
   → Classification → LanguageDetection → PreviewGeneration → Completed) without a page refresh
   (FR-027, contracts/document-processing-api.md SignalR events).
2. Confirm the scanned PDF's `ocrTextRaw`/`extractedText` contains recognizable text (FR-021,
   SC-008) and the text-layer PDF's `extractedText` is populated without an OCR stage running
   (`Skipped`).
3. Upload the mislabeled file (`.txt` renamed `.pdf`) and the password-protected PDF; confirm
   both land in `Failed` with a specific, actionable `failureReason` (not "an error occurred")
   and a working retry action (FR-010, FR-028, SC-006).
4. Time a standard document's upload-to-`Completed` transition — should be under 2 minutes
   (SC-002).
5. **Durability check**: upload a document, and while a stage is `InProgress`, restart the
   `AskLucy.Web`/Hangfire process. Confirm the job resumes automatically to `Completed` without
   any manual retry and without re-running the already-`Completed` stages (FR-030a, research.md
   Decision 10) — check `DocumentProcessingStage` timestamps to confirm no stage re-executed.
6. Open the processing history panel; confirm every state transition is listed with a timestamp
   within 5 seconds of occurring (FR-013, SC-009).

**Pass condition**: matches spec.md User Story 2's five acceptance scenarios plus the restart
edge case.

## Scenario 3 — Review and correct extracted content (User Story 3)

1. Open a completed document's metadata panel; confirm auto-extracted fields are pre-populated
   and `isAutoExtracted: true` (FR-023).
2. Edit the title; confirm it persists and `isAutoExtracted` becomes `false` (FR-023, FR-031).
3. Override the auto-assigned classification to a different category; confirm `source` becomes
   `UserOverride` (FR-026).
4. Add a tag and confirm it's usable as a filter in Scenario 4 below (FR-032).
5. **Conflict check**: open the same document's metadata in two sessions, edit different fields
   in each with the first-loaded `rowVersion`, save the first (succeeds, not stale), then save
   the second (succeeds with `wasStale: true` — confirm the UI shows the stale-data warning
   rather than silently overwriting, FR-031a, research.md Decision 9).

**Pass condition**: matches spec.md User Story 3's four acceptance scenarios plus the conflict
edge case.

## Scenario 4 — Organize and find documents (User Story 4)

1. Create a folder, move two documents into it, confirm they no longer appear at the prior
   location (FR-033).
2. Duplicate one document; confirm an independent copy exists with its own file/metadata/
   processing history (FR-034).
3. Search by filename/metadata keyword; then combine `categoryId` + `language` + `tag` filters
   and confirm only the intersection is returned (FR-035–FR-037).
4. Time a specific-document search — should resolve in under 10 seconds (SC-003).

**Pass condition**: matches spec.md User Story 4's four acceptance scenarios.

## Scenario 5 — Versioning (User Story 5 / SC-007)

1. Replace a document with an updated file, marking it a minor version; confirm version 2 is
   current and version 1's file remains downloadable (FR-038).
2. Open the version timeline; confirm both versions are listed with creator and date (FR-040).
3. Restore version 1 as current; confirm no version is deleted from history, and time the
   restore — should be under 30 seconds (FR-041, SC-007).
4. Compare version 1 and version 2's extracted text/metadata diff (FR-042).
5. Start a new version upload, and while it's in flight, attempt a version restore on the same
   document; confirm it's rejected deterministically (`409 VersionUploadInProgress`) rather than
   corrupting version history (Edge Cases).

**Pass condition**: matches spec.md User Story 5's three acceptance scenarios plus the
concurrent-version-operation edge case.

## Scenario 6 — Dashboard, statistics & notifications (User Story 6 / SC-011)

1. As a regular user, upload several documents (including one engineered to fail) and confirm
   the per-user dashboard's queue/in-progress/completed/failed counts are accurate within 5
   seconds of each state change (FR-045, SC-011).
2. Confirm in-app notifications arrive for upload-completed, processing-completed, and
   processing-failed (FR-047).
3. Reduce a test account's storage quota (or upload until the limit is reached); confirm further
   uploads are blocked with a clear message and a notification fires (FR-011, US6 AC4).
4. As the administrator account, open the organization-wide dashboard and confirm it reflects
   activity across multiple users' documents, while a non-administrator's attempt to access the
   same endpoint returns `403` (FR-045a, US6 AC5–AC6).

**Pass condition**: matches spec.md User Story 6's six acceptance scenarios.

## Scenario 7 — Preview (User Story 7)

1. Preview a completed PDF, DOCX, PNG, and Markdown document; confirm each renders inline without
   triggering a file download (FR-043).
2. Preview a document type with no preview support; confirm the workspace clearly offers download
   instead of erroring (FR-044).

**Pass condition**: matches spec.md User Story 7's two acceptance scenarios.

## Cross-cutting checks

- **Accessibility**: run automated a11y checks (axe or equivalent) against the upload panel,
  document list, metadata panel, and processing dashboard; verify keyboard-only operation of
  upload, rename, archive, delete, and filter controls (FR-052).
- **Security**: confirm `GET /api/v1/documents/{id}/download` never returns a physical path,
  only a signed URL that expires (FR-015, FR-050); confirm a second user cannot fetch the first
  user's document by id (404, not 403, per the ownership-guard convention) (FR-048).
- **Scale spot-check**: seed a large document count (representative sample, not the full 1M of
  SC-004) and confirm list/search response time doesn't visibly degrade versus a small dataset.
