# Feature Specification: Document Intelligence Pipeline

**Feature Branch**: `015-document-intelligence-pipeline`

**Created**: 2026-08-04

**Status**: Draft

**Input**: User description: "Build an enterprise-grade document ingestion and processing pipeline that transforms uploaded files into structured knowledge assets — covering the complete lifecycle of upload, validation, storage, parsing, OCR, metadata extraction, language detection, classification, versioning, and processing status tracking. Explicitly excludes embedding generation, vector storage, semantic search, RAG, AI chat over documents, and knowledge graph generation — those belong to future specifications that build on top of this one."

## Clarifications

### Session 2026-08-04

- Q: If the server/background worker restarts while documents are mid-processing (e.g., mid-OCR), should those jobs automatically resume/requeue, or rely on the existing manual "Retry" action? → A: Auto-resume on restart — processing jobs are durable and automatically requeue from where they left off without user action or duplicated work.
- Q: Should the processing dashboard (upload queue, jobs, stats) be scoped to each user's own documents only, or also offer an organization-wide admin view across all users? → A: Per-user + org-wide admin view — administrators get an additional cross-user dashboard view alongside each user's own scoped view.
- Q: When two edits to the same document's metadata happen concurrently, how should the system resolve the conflict? → A: Last-write-wins with a stale-data warning — the later save persists, but an editor whose copy was stale is warned their view was out of date.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Upload and manage documents (Priority: P1)

A user uploads one or more files — by browsing, dragging-and-dropping, or pasting — and can then see them in their document workspace, rename them, download the original, archive them, restore them from the archive, or delete them permanently.

**Why this priority**: This is the atomic capability the entire feature depends on. Without getting files in and being able to manage them, there is nothing for any other capability (processing, versioning, search, preview) to operate on. It is independently valuable and demoable on its own: a user can store and organize files even before automated processing exists.

**Independent Test**: Can be fully tested by uploading a file via drag-and-drop, confirming it appears in the document list with an "Uploaded" status, then renaming, downloading, archiving, restoring, and deleting it — each action's result immediately visible without depending on any other story in this spec.

**Acceptance Scenarios**:

1. **Given** a user is on the document workspace, **When** they drag a supported file onto the upload area, **Then** the file begins uploading with a visible progress indicator and appears in the document list once complete.
2. **Given** a user selects multiple files at once, **When** they confirm the upload, **Then** all files are queued and uploaded with per-file progress, and the user can cancel any file still in the queue.
3. **Given** a user owns a document, **When** they rename it, download it, archive it, or delete it, **Then** the action completes and is reflected immediately in the document list.
4. **Given** a user has archived a document, **When** they restore it, **Then** the document returns to its prior active state and location.
5. **Given** a large-file upload is interrupted (e.g., network drop), **When** the user resumes the upload, **Then** the upload continues from where it left off instead of restarting from zero.

---

### User Story 2 - Automatic document processing with visible status (Priority: P1)

After a document is uploaded, the system automatically runs it through processing — text extraction, metadata extraction, language detection, classification, and OCR when needed — while the user can watch its progress move through each stage, see when it completes, see a clear reason if it fails, and retry a failed stage.

**Why this priority**: This is what turns a stored file into an "intelligent document" — the core value proposition of the pipeline. It is independently testable once Story 1 exists: upload a document and observe it progress to "Completed" (or a diagnosable "Failed") without needing organization, versioning, or search features.

**Independent Test**: Upload a text-bearing PDF and a scanned image, and confirm both progress through visible processing stages to a "Completed" state with extracted text and metadata available, while a deliberately corrupted file lands in a "Failed" state with a specific, actionable error and a working retry action.

**Acceptance Scenarios**:

1. **Given** a document finishes uploading, **When** processing begins, **Then** the user sees the document's current stage (e.g., Parsing, OCR, Metadata Extraction) update in near real time without needing to refresh the page.
2. **Given** a scanned PDF or image with no embedded text layer, **When** it reaches the OCR stage, **Then** the system extracts recognizable text from it and that text becomes part of the document's extracted content.
3. **Given** a processing stage fails (e.g., an unreadable file), **When** the user views the document, **Then** they see a specific, actionable error message and a "Retry" action that re-attempts processing.
4. **Given** a document has completed processing, **When** the user opens it, **Then** they can see its extracted text, detected language(s) with a confidence indicator, and an assigned classification.
5. **Given** a user views a document's processing history, **When** they open the history panel, **Then** every state transition the document has gone through is listed with a timestamp.

---

### User Story 3 - Review and correct extracted content (Priority: P2)

A user views the text, metadata, detected language, and classification the system extracted for a document, and can edit metadata fields, add or remove tags, and override an incorrect classification.

**Why this priority**: Automated extraction will not always be perfect; letting users see and correct it is what makes the resulting knowledge asset trustworthy enough to build later capabilities (RAG, search) on top of. It depends on Story 2 having produced something to review.

**Independent Test**: Open a completed document, edit its title/author metadata fields, add a custom tag, override its auto-assigned classification to a different category, and confirm all changes persist and are reflected in the document's detail view.

**Acceptance Scenarios**:

1. **Given** a completed document, **When** the user opens its metadata panel, **Then** they see auto-extracted fields (title, author, dates, page count, language, file size, type, keywords, category) pre-populated.
2. **Given** a user edits an auto-extracted metadata field, **When** they save the change, **Then** the corrected value is stored and displayed thereafter instead of the original auto-extracted value.
3. **Given** a document has an automatically assigned classification, **When** the user selects a different category, **Then** the override is saved and marked as user-assigned rather than automatic.
4. **Given** a user adds a tag to a document, **When** they view the document list, **Then** the tag is visible and can be used as a filter.

---

### User Story 4 - Organize documents into folders and find them again (Priority: P2)

A user organizes documents into folders, moves and duplicates documents between folders, and finds documents later by searching or filtering on filename, metadata, author, language, tags, category, date, or status.

**Why this priority**: As the number of stored documents grows, organization and retrieval become necessary for the workspace to stay usable. It builds on Stories 1–3 (there must be documents, with metadata, to organize and filter).

**Independent Test**: Create a folder, move two documents into it, duplicate one of them, then use search and at least two filter combinations (e.g., category + date range) to locate a specific document without browsing the full list.

**Acceptance Scenarios**:

1. **Given** a user creates a folder, **When** they move documents into it, **Then** those documents appear under that folder and no longer at the prior location.
2. **Given** a user duplicates a document, **When** the duplication completes, **Then** a second, independent document record exists with its own copy of the file and metadata.
3. **Given** a user searches by filename or metadata keyword, **When** results are returned, **Then** only documents matching the search term are shown.
4. **Given** a user applies filters (e.g., category = "Contract", language = "English"), **When** the filters are active, **Then** the document list shows only documents matching all active filters.

---

### User Story 5 - Version documents over time (Priority: P3)

A user replaces a document with an updated file, sees a timeline of all prior versions, compares versions, and restores an earlier version if needed — with every version's original file preserved.

**Why this priority**: Documents change over time in real organizations (revised contracts, updated drawings); losing history on replace would be a regression from basic file storage. It depends on Story 1 (a document must exist to version) and benefits from Story 2 (each version can be independently processed).

**Independent Test**: Upload a document, replace it with a new file to create version 2, confirm version 1 remains downloadable from the version timeline, and restore version 1 as the current version.

**Acceptance Scenarios**:

1. **Given** an existing document, **When** the user uploads a replacement file, **Then** a new version is created, the document's current content points to the new version, and the previous version's original file remains intact and retrievable.
2. **Given** a document with multiple versions, **When** the user opens its version timeline, **Then** they see every version listed in order with its creation date and who created it.
3. **Given** a document with multiple versions, **When** the user chooses to restore an earlier version, **Then** that version becomes the current version without deleting any version from the history.

---

### User Story 6 - Monitor processing activity and get notified (Priority: P3)

A user views a processing dashboard scoped to their own documents, showing the upload queue, in-progress jobs, completed jobs, failed jobs, and a retry queue, along with statistics on processing duration, storage usage, file-type distribution, and language distribution — and receives notifications when uploads finish, processing completes or fails, a new version is created, or a storage limit is reached. A workspace administrator additionally has an organization-wide dashboard view covering processing activity and statistics across all users.

**Why this priority**: At scale, users and administrators need visibility into the health of the pipeline rather than checking individual documents one at a time. It depends on Stories 1–2 producing jobs and state transitions to display.

**Independent Test**: Upload several documents of different types (including one designed to fail), then confirm the dashboard shows accurate counts across queue/current/completed/failed, and that a notification is received for the completed upload, the completed processing, and the failure. Separately, confirm an administrator's dashboard shows aggregate activity across multiple users while a non-administrator's dashboard shows only their own.

**Acceptance Scenarios**:

1. **Given** documents are actively being processed, **When** a user opens the processing dashboard, **Then** they see accurate, near-real-time counts of queued, in-progress, completed, and failed jobs limited to their own documents.
2. **Given** a processing job fails, **When** the user views the retry queue, **Then** the failed job is listed with the ability to retry it individually.
3. **Given** an upload finishes, processing completes, processing fails, or a new version is created, **When** that event occurs, **Then** the initiating user receives an in-app notification describing what happened.
4. **Given** a user's storage usage reaches their plan's limit, **When** the limit is reached, **Then** the user is notified and further uploads are blocked until space is freed or the plan changes.
5. **Given** a user holds the administrator role, **When** they open the organization-wide dashboard view, **Then** they see queue, job, and statistics data aggregated across all users' documents, without gaining the ability to open or download an individual user's document content from that view alone.
6. **Given** a user does not hold the administrator role, **When** they attempt to access the organization-wide dashboard view, **Then** access is denied.

---

### User Story 7 - Preview documents without downloading (Priority: P4)

A user opens a visual preview of a document — PDF, Office file, image, or Markdown — directly in the workspace without needing to download and open it in a separate application.

**Why this priority**: This is a convenience/efficiency layer on top of an already-functional document workspace; the platform is fully usable via download for supported types without it, so it is lowest priority even though it is high-value for everyday use.

**Independent Test**: Open a completed PDF, an Office document, an image, and a Markdown file, and confirm each renders a visual preview in the workspace without triggering a file download.

**Acceptance Scenarios**:

1. **Given** a completed PDF, Office document, image, or Markdown document, **When** the user selects "Preview," **Then** a rendered preview displays inline without downloading the original file.
2. **Given** a document type without preview support (e.g., a future CAD format), **When** the user selects it, **Then** the workspace clearly indicates no preview is available and offers download instead.

---

### Edge Cases

- What happens when a user uploads a file whose extension is allowed but whose actual content doesn't match (e.g., a renamed executable disguised as a `.pdf`)? The system MUST reject it based on actual content inspection, not just the filename/extension.
- What happens when a user uploads a file larger than the platform's configured size limit? The upload MUST be rejected before it consumes processing resources, with a clear message stating the limit.
- What happens when a user uploads a file whose checksum exactly matches a document already in their workspace? The system MUST detect the duplicate and let the user choose to link it as a new version of the existing document or proceed as a separate new document, rather than silently doing either.
- What happens when OCR cannot recognize any text in a scanned document (e.g., a blank or extremely low-quality scan)? The document MUST still reach a terminal state (e.g., "Completed" with empty extracted text, or "Failed" with a specific reason) — it must never remain stuck in an OCR state indefinitely.
- What happens when a document is password-protected or encrypted? Processing MUST fail with an error identifying the reason (protected content) rather than an unexplained generic failure.
- What happens when a user tries to delete a folder that still contains documents? The system MUST either block the deletion with a clear message or require the user to explicitly confirm what happens to the contained documents (move to parent, or delete/archive along with the folder).
- What happens when two users (or two browser tabs of the same user) edit a document's metadata at the same time? The later save wins (last-write-wins), but if the saving user's copy was stale (loaded before the other edit was saved), the system MUST warn them that their view was out of date rather than silently applying the save with no indication a conflict occurred.
- What happens when a user tries to restore a version while a new version upload is simultaneously in progress? The system MUST resolve this deterministically (e.g., reject the restore until the in-progress upload finishes) rather than corrupting version history.
- What happens when a user cancels an upload that is already partway through processing? The system MUST stop further processing and MUST NOT leave an orphaned partial file consuming storage.
- What happens when a user's storage quota is exceeded mid-upload of a large file? The upload MUST fail cleanly with a clear "storage limit reached" message, and any partial data already transferred MUST be cleaned up.
- What happens when a retry is requested for a document that is currently mid-processing (not yet failed)? The system MUST ignore or queue the retry rather than running two processing attempts on the same document concurrently.
- What happens if the server or background worker restarts while a document is mid-processing (e.g., mid-OCR)? The job MUST automatically resume/requeue from a durable, persisted state after the restart, without requiring manual user retry and without duplicating already-completed work.

## Requirements *(mandatory)*

### Functional Requirements

**Upload**

- **FR-001**: System MUST allow users to upload a single file via file browser selection.
- **FR-002**: System MUST allow users to upload multiple files in one action.
- **FR-003**: System MUST support drag-and-drop upload.
- **FR-004**: System MUST support paste-to-upload (e.g., pasting an image from the clipboard).
- **FR-005**: System MUST support resumable upload for large files, continuing from the last successfully transferred point after an interruption rather than restarting.
- **FR-006**: System MUST show upload progress per file and an overall queue view when multiple files are uploading.
- **FR-007**: Users MUST be able to cancel an upload that is queued or in progress.
- **FR-008**: Users MUST be able to retry an upload that failed.
- **FR-009**: System MUST detect duplicate uploads via content checksum and prompt the user to choose between creating a new version of the existing document or proceeding as a separate document.
- **FR-010**: System MUST validate every uploaded file's actual content type (not solely its filename extension) before accepting it.
- **FR-011**: System MUST reject uploads that exceed the user's configured file-size or storage-quota limits, before consuming processing resources, with a clear explanation.

**Document Lifecycle & Storage**

- **FR-012**: System MUST track every document through the following lifecycle states: Uploaded, Queued, Processing (with sub-stages OCR, Parsing, Metadata Extraction, Language Detection), Completed, Failed, Archived, Deleted.
- **FR-013**: System MUST log every state transition a document undergoes, including timestamp and triggering actor, and MUST make this history viewable by the document's owner.
- **FR-014**: System MUST retain the original uploaded file unmodified for the life of the document (and every version — see Versioning).
- **FR-015**: System MUST NOT expose physical file storage paths to clients; files MUST be served via time-limited signed URLs.
- **FR-016**: Users MUST be able to archive a document and later restore it to its prior active state.
- **FR-017**: Users MUST be able to delete a document; deletion MUST be recoverable (soft delete) rather than an immediate, irreversible removal, consistent with the platform's data-retention conventions.
- **FR-018**: Users MUST be able to download the original file of any document they own.
- **FR-019**: Users MUST be able to rename a document without affecting its stored content or history.

**Automated Processing**

- **FR-020**: System MUST automatically initiate processing (parsing, metadata extraction, language detection, classification) upon successful upload without requiring manual triggering by the user.
- **FR-021**: System MUST perform OCR on scanned PDFs and images that lack an extractable text layer, supporting recognition across multiple languages.
- **FR-022**: System MUST extract plain text, headings, paragraphs, tables, lists, captions, footnotes, hyperlinks, and page numbers from supported document types where present.
- **FR-023**: System MUST automatically extract metadata including filename, title, author, creation date, modification date, page count, detected language, file size, file type, and keywords.
- **FR-024**: System MUST automatically detect a document's primary language and any secondary languages present, each with a confidence score.
- **FR-025**: System MUST automatically assign a document to a category (e.g., Technical, Legal, Financial, Contract, Drawing, Report) based on its content.
- **FR-026**: Users MUST be able to override an automatically assigned classification, and the system MUST retain the distinction between an automatic and a user-confirmed classification.
- **FR-027**: System MUST make processing progress visible to the user at the stage level (e.g., "OCR in progress," "Metadata extraction complete") while processing is underway.
- **FR-028**: System MUST surface a specific, actionable error when any processing stage fails, rather than a generic or silent failure.
- **FR-029**: Users MUST be able to retry processing for a document in a Failed state, and the system MUST prevent concurrent duplicate processing attempts on the same document.
- **FR-030**: Processing MUST run asynchronously in the background; the workspace UI MUST remain responsive and usable while processing is underway.
- **FR-030a**: Processing job state MUST be persisted durably such that an in-flight job automatically resumes/requeues after a service restart or crash, without requiring manual user retry and without duplicating already-completed work.

**Metadata, Tagging & Organization**

- **FR-031**: Users MUST be able to view and edit a document's extracted metadata fields.
- **FR-031a**: When two metadata edits to the same document are saved concurrently, the system MUST apply last-write-wins (the later save persists) and MUST warn the saving user if their loaded copy was stale at save time, rather than silently discarding either edit without indication.
- **FR-032**: Users MUST be able to add and remove free-form tags on a document.
- **FR-033**: Users MUST be able to create folders, move documents between folders, and delete folders, with explicit, non-silent handling of any documents contained in a folder being deleted.
- **FR-034**: Users MUST be able to duplicate a document, producing an independent copy with its own file, metadata, and processing history.

**Search & Discovery**

- **FR-035**: Users MUST be able to search documents by filename and metadata content.
- **FR-036**: Users MUST be able to filter documents by author, language, tags, category, date, and processing/lifecycle status.
- **FR-037**: Users MUST be able to combine multiple filters at once, with results reflecting the intersection of all active filters.

**Versioning**

- **FR-038**: Users MUST be able to replace a document with an updated file, creating a new version while preserving every prior version's original file unchanged.
- **FR-039**: System MUST distinguish major and minor version increments and let the user indicate which applies when replacing a document.
- **FR-040**: Users MUST be able to view a chronological version timeline for any document, showing who created each version and when.
- **FR-041**: Users MUST be able to restore an earlier version as the document's current version without deleting any version from history.
- **FR-042**: Users MUST be able to compare two versions of a document (at minimum, their extracted text and metadata).

**Preview**

- **FR-043**: System MUST generate an inline preview for PDF, supported Office document types, images, and Markdown documents.
- **FR-044**: System MUST clearly indicate when no preview is available for a given document type and offer download as the fallback.

**Dashboard & Notifications**

- **FR-045**: System MUST provide a processing dashboard, scoped to the requesting user's own documents by default, showing the upload queue, currently processing jobs, completed jobs, failed jobs, and a retry queue.
- **FR-045a**: System MUST provide an organization-wide administrative dashboard view, restricted to users holding an administrator role, aggregating the upload queue, jobs, and retry queue across all users' documents; this view MUST NOT itself grant the administrator the ability to open, download, or edit an individual user's document content.
- **FR-046**: System MUST provide processing statistics including average processing duration, total storage usage, file-type distribution, and language distribution, both scoped to the requesting user and, for administrators, aggregated organization-wide.
- **FR-047**: System MUST notify a user when their upload completes, when processing completes, when processing fails, when OCR fails, when a new version is created, and when their storage limit is reached.

**Security & Access Control**

- **FR-048**: System MUST enforce that only a document's owner (or an explicitly authorized party, once sharing exists in a future spec) can view, modify, or delete it.
- **FR-049**: System MUST validate uploaded file extension, actual content type, and size before storage on every upload, including version-replacement uploads.
- **FR-050**: System MUST provide downloads only via signed, time-limited URLs rather than direct, guessable file paths.
- **FR-051**: System MUST log security-relevant events (e.g., rejected uploads, unauthorized access attempts) to an audit trail distinct from general processing logs.

**Accessibility**

- **FR-052**: The document workspace MUST conform to WCAG 2.2 AA, including full keyboard operability, visible focus states, correct ARIA roles/labels, and sufficient color contrast in both light and dark themes.

### Key Entities

- **Document**: The core intelligent-document record a user interacts with — current lifecycle state, owning user, current version pointer, folder location, and links to its metadata, extracted content, tags, and processing history.
- **DocumentVersion**: A single immutable snapshot of a document's file at a point in time, including its own original file, extracted content, and major/minor version identifier; a Document has one or more Versions.
- **DocumentFolder**: A user-organized container for grouping Documents hierarchically.
- **DocumentMetadata**: The structured, editable fields describing a Document/Version (title, author, dates, page count, language, file size, type, keywords, category), distinguishing auto-extracted values from user-edited overrides.
- **DocumentLanguage**: A detected language for a Document/Version, with a role (primary/secondary) and a confidence score.
- **DocumentClassification**: The category assigned to a Document, distinguishing an automatic (system-assigned) classification from a user override.
- **DocumentPreview**: A generated, renderable preview artifact (e.g., page image, thumbnail) associated with a Document/Version.
- **DocumentProcessingJob**: A unit of work representing one document's journey through the processing pipeline, with an overall status (queued, in progress, completed, failed).
- **DocumentProcessingStage**: A single step within a Processing Job (e.g., OCR, Parsing, Metadata Extraction), independently trackable and retryable.
- **DocumentProcessingLog**: A timestamped record of a state transition or event within a Processing Job/Stage, forming the visible processing history.
- **DocumentTag**: A user-defined label that can be attached to one or more Documents for organization and filtering.
- **DocumentAuditLog**: An immutable record of security- and lifecycle-relevant actions taken on a Document (upload, delete, restore, permission-relevant events), distinct from processing logs.
- **DocumentChecksum**: The content hash of a Document/Version used for duplicate detection and integrity verification.
- **DocumentStatistics**: Aggregated, periodically computed metrics (storage usage, processing duration, file-type and language distribution) used to power the processing dashboard, computed both per-user and, for the administrator view, organization-wide.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can upload a typical office document (under 25MB) and see it appear in their document list within 5 seconds of the upload finishing.
- **SC-002**: At least 95% of standard document types (PDF, DOCX, XLSX, PPTX, TXT) complete automated processing — extraction, metadata, language detection, and classification — within 2 minutes of upload, without any manual intervention.
- **SC-003**: Users can locate a specific, previously uploaded document via search or filters in under 10 seconds in at least 90% of attempts.
- **SC-004**: The system sustains at least 1 million stored documents for a single organization without a measurable increase in document list or search response time.
- **SC-005**: A resumable upload of a large file (over 100MB) that is interrupted successfully continues without re-transferring already-uploaded data in at least 95% of retry attempts.
- **SC-006**: 100% of processing failures result in a visible, actionable error with a working retry path — zero failures are silent or unexplained.
- **SC-007**: A user can restore any prior document version as the current version in under 30 seconds from opening the version timeline.
- **SC-008**: OCR processing achieves at least 90% text-recognition accuracy on clean, legible scanned documents in a supported language.
- **SC-009**: Every document state transition appears in the document's processing history within 5 seconds of occurring.
- **SC-010**: A first-time user can successfully upload a document and view its extracted content and metadata without external help in under 3 minutes.
- **SC-011**: The processing dashboard's job counts (queued/in-progress/completed/failed) remain accurate to within 5 seconds of the underlying job state changing.

## Assumptions

- **Sharing & permissions**: Documents are private to the uploading user in this spec; multi-user sharing and granular permissions are out of scope here and deferred to a future specification, matching the source request's own treatment of `DocumentPermission` as future work. The one exception is the administrator dashboard (see Clarifications): administrators see aggregate cross-user queue/job/statistics data, but that view does not itself grant access to open, download, or edit another user's document content — document-content privacy is otherwise unchanged.
- **Administrator role**: "Administrator" reuses the platform's existing role/authorization system (ASP.NET Identity roles, as established elsewhere in the platform's Administration Engine) rather than introducing a document-specific role model.
- **Deletion & retention**: "Deleted" is a recoverable soft-delete state, not an immediate permanent purge; permanent erasure happens only through an explicit, audited action, consistent with the platform's existing soft-delete/audit conventions.
- **Duplicate handling**: A checksum match on upload prompts the user to choose between "new version of existing document" or "separate new document" — it is never silently blocked or silently merged.
- **Storage & size limits**: Per-file size caps and total storage quotas are enforced according to the user's existing subscription tier; the specific numeric limits are a configuration concern, not fixed by this specification.
- **OCR/language coverage**: Initial OCR and language-detection coverage matches the platform's currently supported languages; additional languages are added incrementally without requiring pipeline redesign.
- **Folder scope**: Folders form a single-owner hierarchical tree in this spec; cross-user or shared folder structures are future work, consistent with the sharing assumption above.
- **Versioning scheme**: Versions follow a major/minor numbering scheme; the user replacing a document indicates whether the change is major or minor.
- **Malware scanning**: Virus/malware scanning is explicitly out of scope for this spec (as stated in the source request); the Validation pipeline stage is designed with a slot for it to be added later without redesign.
- **Notification channels**: Notifications in this spec are in-app only; email and push notification channels are future work.
- **Search scope**: Search and filtering in this spec cover filename, metadata, author, language, tags, category, date, and status — full-text search over extracted content and semantic search are explicitly excluded here and belong to a future RAG-related specification.
- **Classification taxonomy**: The initial set of classification categories (Technical, Legal, Financial, Research, Contract, Specification, Manual, Drawing, Presentation, Report, Meeting Notes) is a starting taxonomy; administrators can extend it without a pipeline redesign.
