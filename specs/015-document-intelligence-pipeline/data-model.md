# Phase 1 Data Model: Document Intelligence Pipeline

New entities live in `AskLucy.Domain/Documents/`, configured in `AskLucy.Persistence/Configurations/
Documents/`, per constitution §3 (Domain purity — no EF Core attributes on Domain types; all
mapping via Fluent API). Surrogate keys are `Guid` v7 (`Guid.CreateVersion7()`), matching every
existing entity. Audit columns (`CreatedAtUtc`/`CreatedBy`/`ModifiedAtUtc`/`ModifiedBy`) and the
`RowVersion` concurrency token come from `BaseEntity` + the existing `AuditSaveChangesInterceptor`,
exactly as on every existing entity, and are not repeated per-entity below. `DeletedAtUtc`/
`DeletedBy`/`IsDeleted` are called out explicitly only where soft delete is (or is deliberately not)
used. This is a new, independent bounded context from `KnowledgeBases` (research.md Decision 1).

**Modeling note — three orthogonal status axes**: A `Document`'s automated-processing outcome
(`ProcessingStatus`), the owner's archive action (`ArchivedAtUtc`), and soft delete
(`BaseEntity.IsDeleted`) are three independent axes, not one combined enum — mirroring the same
"orthogonal flag, not enum value" convention `KnowledgeBase` already established for Archived/
Favorite/Pinned/Deleted (specs/014 research.md Decision 2, constitution §7 Convention Over
Configuration). This lets a `Completed` document be archived without losing its processing outcome,
and a `Failed` document be deleted without needing to first "cancel" a status that doesn't apply to
delete.

## New Entities

### Document

The aggregate root (FR-001, FR-012–FR-019).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `OwnerId` | `string` | Required. FK to `ApplicationUser`. Every query/mutation is scoped to this (FR-048), except the administrator dashboard aggregate view (FR-045a), which never exposes `Document` rows themselves — only counts/statistics. |
| `FolderId` | `Guid?` | FK `DocumentFolder`. Null = root level (FR-033). |
| `FileName` | `string` (≤260) | User-editable display name (FR-019); independent of the physical stored file name. |
| `FileType` | `enum DocumentFileType` | `Pdf, Word, Excel, PowerPoint, Rtf, Markdown, Html, Csv, Json, Xml, Text, Png, Jpeg, Tiff, Bmp, Webp` — a new enum scoped to this context (research.md Decision 1), not `KnowledgeBaseDocumentType`. |
| `SizeBytes` | `long` | Of the current version's original file. |
| `CurrentVersionId` | `Guid` | FK `DocumentVersion` — the version currently considered "current" (FR-038, FR-041). |
| `ProcessingStatus` | `enum ProcessingStatus` | `Uploaded, Queued, Processing, Completed, Failed` (FR-012). Sub-stage detail lives on `DocumentProcessingStage`, not here. |
| `ArchivedAtUtc` | `DateTime?` | FR-016. Non-null = archived; orthogonal to `ProcessingStatus` (see modeling note above). |
| `DeletedAtUtc` / `DeletedBy` | `DateTime?` / `string?` | `BaseEntity` soft-delete fields (FR-017), filtered via `HasQueryFilter(d => d.DeletedAtUtc == null)`. |

**Validation rules** (Domain):
- `FileName` required, non-blank after trim, ≤260 chars.
- `OwnerId` required.
- `Archive()`/`Restore()` are idempotent no-ops guarded by current state.
- Soft `Delete()` sets `DeletedAtUtc`/`DeletedBy`; recoverable per spec.md's retention assumption —
  permanent purge is a separate, explicit, audited command, not modeled by this entity's own state
  (mirrors `KnowledgeBase`'s soft-delete/purge split).
- `Rename()` only updates `FileName`; never touches stored content or `DocumentVersion` history
  (FR-019).

**Relationships**: Has one current `DocumentVersion` plus zero or more prior `DocumentVersion`s
(one-to-many, ordered). Has zero or one `DocumentMetadata`, `DocumentLanguage` set, and
`DocumentClassification`. Has zero or more `DocumentPreview`, `DocumentTag` links, and
`DocumentProcessingJob`s (one active/most-recent, plus history). Belongs to zero or one
`DocumentFolder`.

**Lifecycle**:

```text
Uploaded ──automatic──> Queued ──automatic──> Processing ──automatic──> Completed
                                                    │
                                                    └──any stage fails──> Failed ──user retry──> Queued

ArchivedAtUtc: null ──archive──> set ──restore──> null            (orthogonal to ProcessingStatus)
DeletedAtUtc:  null ──delete (soft)──> set ──restore──> null       (orthogonal to both above)
```

---

### DocumentVersion

An immutable snapshot of a document's file at a point in time (FR-038–FR-042).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `DocumentId` | `Guid` | Required FK, indexed. |
| `VersionMajor` | `int` | FR-039. |
| `VersionMinor` | `int` | FR-039. Together, `VersionMajor.VersionMinor` is the displayed version label (e.g., "2.1"). |
| `StoredFileName` | `string` | The `IFileStorage`-minted name (never the original file name — constitution §8). |
| `OriginalFileName` | `string` | The name the file had at upload time, retained for download (FR-014, distinct from `Document.FileName`, which can be renamed independently). |
| `SizeBytes` | `long` | |
| `ChecksumId` | `Guid` | FK `DocumentChecksum` (1:1 per version). |
| `ExtractedText` | `string?` (`nvarchar(max)`) | FR-022 plain-text result, populated once Text Extraction completes. |
| `ExtractedStructureJson` | `string?` (`nvarchar(max)`) | FR-022 headings/paragraphs/tables/lists/captions/footnotes/hyperlinks/page-number structure, stored as JSON (no separate structured-content table — this is write-once, read-whole, never queried by sub-field). |
| `OcrTextRaw` | `string?` (`nvarchar(max)`) | FR-021 OCR output, kept distinct from `ExtractedText` so a document that had both an existing text layer and an OCR pass never conflates the two sources. |
| `PageCount` | `int?` | Reuses the existing `IDocumentPageCountExtractor` abstraction/extraction approach (specs/014 research.md Decision 5) where the format overlaps; new formats use the new `IDocumentTextExtractor` (research.md Decision 5) page count instead. |
| `CreatedByUserId` | `string` | Who created this version — shown on the version timeline (FR-040). |

**Validation rules**:
- Immutable after creation: no command updates a `DocumentVersion`'s file/extracted-content fields
  in place; a "replace" always creates a new `DocumentVersion` row (FR-038's "every version keeps
  its original file").
- Exactly one `DocumentVersion` per `Document` is referenced by `Document.CurrentVersionId` at any
  time (enforced in the Application layer, not a DB constraint, since restoring an old version just
  repoints `CurrentVersionId` — no row is ever deleted or recreated, FR-041).

**Relationships**: Belongs to one `Document`. Has one `DocumentChecksum`. Has zero or more
`DocumentPreview`s and one `DocumentProcessingJob` (the job that processed this specific version).

---

### DocumentFolder

A user-organized hierarchical container (FR-033).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `OwnerId` | `string` | Required, indexed — folders are single-owner (spec.md Assumptions). |
| `ParentFolderId` | `Guid?` | FK to another `DocumentFolder`. Null = root-level. Indexed. |
| `Name` | `string` (≤200) | Required. |
| `Depth` | `int` | Computed at create/move time, stored (same rationale as `KnowledgeBaseFolder.Depth`, specs/014 data-model.md). |
| `DeletedAtUtc` / `DeletedBy` | `DateTime?` / `string?` | Soft delete. |

**Validation rules**:
- `Name` required, non-blank.
- A folder MUST belong to the same `OwnerId` as its `ParentFolderId`'s folder, when set.
- Deleting a folder that still contains `Document`s requires the caller to explicitly choose
  "move contained documents to parent" or "archive/delete contained documents along with the
  folder" (Edge Cases) — enforced in `DeleteFolderCommandHandler`, not a DB cascade.

**Relationships**: Owns zero or more child `DocumentFolder`s and zero or more `Document`s.

---

### DocumentMetadata

Structured, editable descriptive fields for a `Document` (FR-023, FR-031, FR-031a).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `DocumentId` | `Guid` | Required FK, unique (1:1 with `Document`). |
| `Title` | `string?` | Auto-extracted, user-editable. |
| `Author` | `string?` | Auto-extracted, user-editable. |
| `CreationDate` | `DateTime?` | Auto-extracted from file metadata, user-editable. |
| `ModificationDate` | `DateTime?` | Auto-extracted from file metadata, user-editable. |
| `Keywords` | `string?` (delimited or JSON array) | Auto-extracted, user-editable. |
| `Encoding` | `string?` | Auto-extracted (text-based formats only). |
| `IsAutoExtracted` | `bool` | `true` until the user edits any field, then permanently `false` for this record — distinguishes an auto-extracted value from a user override (FR-023's "distinguishing auto-extracted from user-edited overrides"). |

**Validation rules**:
- Concurrent edits use `RowVersion`-based staleness detection, not a hard reject (research.md
  Decision 9): the handler catches `DbUpdateConcurrencyException`, reloads, re-applies the incoming
  changes, retries, and returns `WasStale: true` (FR-031a).

**Relationships**: Belongs to exactly one `Document`.

---

### DocumentLanguage

A detected language for a `Document` (FR-024).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `DocumentId` | `Guid` | Required FK, indexed. |
| `LanguageCode` | `string` (ISO 639-1, ≤10) | e.g., `"en"`, `"ar"`. |
| `Role` | `enum` (`Primary`, `Secondary`) | FR-024. Exactly one `Primary` row per document; zero or more `Secondary`. |
| `ConfidenceScore` | `decimal(5,4)` | 0.0000–1.0000, from the AI Provider Engine call (research.md Decision 4). |

**Relationships**: Belongs to one `Document`. Populated by the Language Detection processing stage.

---

### DocumentCategory

A supporting lookup entity realizing the classification taxonomy's extensibility (spec.md
Assumptions: "administrators can extend it without a pipeline redesign"), mirroring
`KnowledgeBaseCategory`'s existing convention (specs/014).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `Name` | `string` (≤100) | Unique. Seeded with the starting taxonomy (Technical, Legal, Financial, Research, Contract, Specification, Manual, Drawing, Presentation, Report, Meeting Notes). |
| `IsSystemDefined` | `bool` | `true` for the seeded set (not user-deletable); `false` for administrator-added categories. |

---

### DocumentClassification

The category assigned to a `Document` (FR-025, FR-026).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `DocumentId` | `Guid` | Required FK, unique (1:1 with `Document` — a document has exactly one current classification). |
| `CategoryId` | `Guid` | FK `DocumentCategory`. |
| `Source` | `enum` (`Automatic`, `UserOverride`) | FR-026 — retains the distinction even after an override. |
| `ConfidenceScore` | `decimal(5,4)?` | Only populated when `Source == Automatic`. |

**Relationships**: Belongs to one `Document`. References one `DocumentCategory`.

---

### DocumentPreview

A generated, renderable preview artifact (FR-043, FR-044).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `DocumentVersionId` | `Guid` | Required FK, indexed. |
| `PreviewType` | `enum` (`PageImage`, `Thumbnail`, `StructuredContent`) | `StructuredContent` covers the Office-preview approach from research.md Decision 6. |
| `StoredFileName` | `string?` | `IFileStorage`-minted name for `PageImage`/`Thumbnail`; null for `StructuredContent` (which reuses `DocumentVersion.ExtractedStructureJson` instead of a separate file). |
| `PageNumber` | `int?` | For multi-page `PageImage` previews. |

**Relationships**: Belongs to one `DocumentVersion`.

---

### DocumentProcessingJob

One document version's journey through the pipeline (FR-020, FR-027–FR-030a).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `DocumentId` | `Guid` | Required FK, indexed. |
| `DocumentVersionId` | `Guid` | Required FK — a job processes one specific version. |
| `Status` | `enum` (`Queued`, `InProgress`, `Completed`, `Failed`) | Mirrors `Document.ProcessingStatus` at the job level; a document's displayed status is its most recent job's status. |
| `HangfireJobId` | `string?` | The underlying Hangfire job id (research.md Decision 2), for correlation/diagnostics only — never exposed to clients. |
| `StartedAtUtc` | `DateTime?` | |
| `CompletedAtUtc` | `DateTime?` | Used for the average-processing-duration statistic (FR-046). |
| `FailureReason` | `string?` | Specific, actionable message (FR-028), not a raw exception dump. |
| `RetryCount` | `int` | Incremented on each user- or system-triggered retry (FR-029). |

**Relationships**: Belongs to one `Document`/`DocumentVersion`. Has one or more
`DocumentProcessingStage` children and `DocumentProcessingLog` entries.

---

### DocumentProcessingStage

One step within a `DocumentProcessingJob` (FR-027, FR-029, FR-030a).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `DocumentProcessingJobId` | `Guid` | Required FK, indexed. |
| `StageType` | `enum` (`Validation`, `Ocr`, `TextExtraction`, `MetadataExtraction`, `Classification`, `LanguageDetection`, `PreviewGeneration`) | Matches spec.md's Processing Pipeline order (Virus Scan is explicitly future — not modeled as a stage yet, per Assumptions). |
| `Status` | `enum` (`Pending`, `InProgress`, `Completed`, `Failed`, `Skipped`) | `Skipped` covers e.g. OCR being unnecessary for a document with an existing text layer. |
| `StartedAtUtc` | `DateTime?` | |
| `CompletedAtUtc` | `DateTime?` | |
| `FailureReason` | `string?` | |

**Validation rules**: On job resume after a restart (research.md Decision 10), any stage already
`Completed` is never re-executed; the first `Pending`/`InProgress` stage is where execution resumes.

**Relationships**: Belongs to one `DocumentProcessingJob`.

---

### DocumentProcessingLog

A timestamped record of a state transition or event (FR-013, FR-027).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `DocumentId` | `Guid` | Required FK, indexed. |
| `DocumentProcessingJobId` | `Guid?` | Set when the event is processing-related; null for lifecycle events not tied to a job (e.g., archive/restore/rename). |
| `EventType` | `string` | e.g., `"StatusChanged"`, `"StageStarted"`, `"StageCompleted"`, `"StageFailed"`, `"Archived"`, `"Restored"`, `"Renamed"`. |
| `Detail` | `string?` | Human-readable detail shown in the processing-history panel (FR-013, US2 AC5). |
| `OccurredAtUtc` | `DateTime` | |

**Relationships**: Belongs to one `Document`; optionally references one `DocumentProcessingJob`.
This is append-only — never updated or deleted, forming the visible processing history.

---

### DocumentTag

A user-defined label (FR-032).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `OwnerId` | `string` | Required, indexed — tags are private to the creating user (mirrors the KnowledgeBaseCategory-vs-custom-category precedent, spec.md scope). |
| `Name` | `string` (≤50) | Unique per `OwnerId`. |

A many-to-many `DocumentTagAssignment(DocumentId, DocumentTagId)` join table links tags to
documents; not user-facing as its own entity.

---

### DocumentAuditLog

An immutable record of security- and lifecycle-relevant actions (FR-051).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `DocumentId` | `Guid?` | Null for events not tied to a specific document (e.g., a rejected upload that never became a `Document` row). |
| `ActorUserId` | `string` | |
| `EventType` | `string` | e.g., `"UploadRejected"`, `"UnauthorizedAccessAttempt"`, `"Deleted"`, `"Restored"`, `"Downloaded"`. |
| `Detail` | `string?` | |
| `OccurredAtUtc` | `DateTime` | |

**Relationships**: Distinct from `DocumentProcessingLog` (FR-051's "distinct from general
processing logs") — this table is the security/audit trail; the other is the processing/lifecycle
trail. Append-only.

---

### DocumentChecksum

The content hash used for duplicate detection and integrity (FR-009, research.md Decision 8).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `Algorithm` | `string` | Fixed to `"SHA-256"` for now (research.md Decision 8). |
| `Hash` | `string` (64 hex chars) | Indexed together with the owning `Document`'s `OwnerId` for fast per-user duplicate lookup on upload. |

**Relationships**: Referenced 1:1 by exactly one `DocumentVersion`.

---

### DocumentStatistics

Periodically computed, denormalized aggregate metrics powering the processing dashboard
(FR-046), computed both per-user and organization-wide (FR-045a).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `Scope` | `enum` (`User`, `Organization`) | `User` rows are keyed additionally by `OwnerId`; `Organization` rows aggregate across all owners (visible only via the administrator dashboard, FR-045a). |
| `OwnerId` | `string?` | Null for `Organization`-scoped rows. |
| `TotalDocuments` | `int` | |
| `TotalStorageBytes` | `long` | |
| `AverageProcessingDurationMs` | `long?` | Derived from completed `DocumentProcessingJob.CompletedAtUtc - StartedAtUtc`. |
| `FileTypeDistributionJson` | `string` (JSON) | `{ "Pdf": 120, "Docx": 45, ... }` — a small, dashboard-only breakdown; not modeled as a normalized table since it is never queried per-file-type in isolation. |
| `LanguageDistributionJson` | `string` (JSON) | Same rationale as above. |
| `ComputedAtUtc` | `DateTime` | Recomputed on a periodic Hangfire recurring job (not on every mutation — SC-011's 5-second accuracy budget is met by a short recompute interval, not synchronous updates on every write, avoiding a hot-path write amplification). |

**Explicitly not modeled as real-time-updated counters**: unlike `KnowledgeBase.DocumentCount`
(specs/014, updated transactionally), dashboard statistics here are refreshed on a short interval
(research.md-adjacent operational decision) because SC-011 only requires 5-second accuracy, not
synchronous consistency, and several of these aggregates (file-type/language distribution) are
expensive to maintain transactionally at 1M-document scale (SC-004).

---

### DocumentNotification

A supporting entity realizing FR-047's in-app notifications. Not in spec.md's Key Entities list
(the spec describes the *behavior*, not a backing entity) and not derivable from any existing
platform capability: a repository-wide search found **no existing in-app notification mechanism
anywhere in this codebase** (CLAUDE.md's "Notification Engine" is an aspirational future module,
not yet built) — this feature is the first to need one. Scope is deliberately narrow (this
feature's six event types only), not a general-purpose platform notification engine; building the
latter now would be exactly the kind of speculative, unrequested generalization constitution
§2.III (YAGNI) forbids.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserId` | `string` | Required, indexed — the recipient. |
| `DocumentId` | `Guid?` | The related document, where applicable. |
| `EventType` | `enum` (`UploadCompleted`, `ProcessingCompleted`, `ProcessingFailed`, `OcrFailed`, `VersionCreated`, `StorageLimitReached`) | FR-047. |
| `Message` | `string` | Human-readable, ready to render. |
| `IsRead` | `bool` | Defaults `false`. |
| `CreatedAtUtc` | `DateTime` | |

**Relationships**: Belongs to one user; optionally references one `Document`. Delivered to the
client both by push (over the same `DocumentProcessingHub` SignalR connection, research.md
Decision 7) and by a simple paginated inbox query, so a notification is never lost solely because
the user wasn't connected when it fired.

---

### DocumentUploadSession

Added during US1 implementation — not in the original entity list. Tracks a resumable chunked
upload in progress (FR-005) between `StartUpload`/`UploadChunk`/`CompleteUpload` calls. The
accumulated bytes live in a new `IResumableUploadStorage` temp-storage abstraction (distinct from
`IFileStorage`'s permanent store, so an abandoned session never pollutes permanent storage) —
this row tracks only metadata, plus (once a duplicate is detected at completion) the already-
finalized permanent file/hash pending the caller's version-vs-new-document choice (FR-009).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK — also the `IResumableUploadStorage` session key. |
| `OwnerId` | `string` | Required, indexed. |
| `FileName` | `string` | The original file name declared at `StartUpload`. |
| `DeclaredSizeBytes` | `long` | What the client said it would upload; checked against what's actually received at completion. |
| `ChunkSizeBytes` | `long` | Echoed from `DocumentUploadOptions` at session-start time. |
| `Status` | `enum` (`InProgress`, `PendingDuplicateResolution`, `Completed`, `Cancelled`) | |
| `PendingStoredFileName` | `string?` | Set only in `PendingDuplicateResolution` — the permanent `IFileStorage` name of the already-saved final file. |
| `PendingChecksumHash` | `string?` | Set alongside `PendingStoredFileName`. |
| `TargetDocumentId` | `Guid?` | Added during US5 implementation. Non-null only for a `ReplaceDocument` upload (FR-038) — set at `StartUpload` time (not just at finalize) so `RestoreDocumentVersion`'s in-flight-upload conflict check (FR-041, Edge Cases) has something to query before the replace finishes. Null for a plain new-document upload. Indexed alongside `Status`. |
| `ExpiresAtUtc` | `DateTime` | An abandoned session past this point is eligible for cleanup. |

**Note**: `NextExpectedChunkIndex` (contracts/documents-api.md) is deliberately **not** a stored
field — it's derived at request time as `(await resumableStorage.GetSizeAsync(sessionId)) /
ChunkSizeBytes`, so the actual accumulated bytes are always the single source of truth, never a
counter that could drift from them.
