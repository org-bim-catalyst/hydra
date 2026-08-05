---

description: "Task list for Document Intelligence Pipeline"
---

# Tasks: Document Intelligence Pipeline

**Input**: Design documents from `/specs/015-document-intelligence-pipeline/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (§10 Testing Standards, non-negotiable) requires unit,
integration, and Playwright E2E coverage for new/changed behavior — test tasks are not optional
here.

**Organization**: Tasks are grouped by user story (spec.md priorities: US1/US2 = P1, US3/US4 =
P2, US5/US6 = P3, US7 = P4) so each story is independently implementable, testable, and
demoable. The `PreviewGeneration` processing *stage* (producing the artifact) is implemented in
US2 (it's part of reaching `Completed`, per FR-012/the pipeline order); US7 only adds the
endpoint/UI that *reads* the already-generated artifact — no rework, no throwaway code. Similarly,
FR-011's upload-time storage-quota *rejection* lives in US1 (it's an upload-validation concern);
US6 adds the *notification* fired when the limit is hit, per its own acceptance scenario.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Maps the task to US1–US7 from spec.md
- All descriptions include exact file paths

## Path Conventions

Existing single-solution web app (constitution §3): `src/AskLucy.Domain`,
`src/AskLucy.Application`, `src/AskLucy.Infrastructure`, `src/AskLucy.Persistence`,
`src/AskLucy.Web` (API + `ClientApp/` React SPA), `tests/AskLucy.*.Tests`. This feature adds a
new, independent `Documents` module at every layer (research.md Decision 1) — no new top-level
project.

---

## Phase 1: Setup

**Purpose**: New dependencies and platform registrations this feature needs before any domain
code is written (plan.md Technical Context; research.md Decisions 2, 3, 5, 6, 7).

- [X] T001 [P] Add `Hangfire.Core`, `Hangfire.SqlServer`, `Hangfire.AspNetCore` package references to `src/AskLucy.Infrastructure/AskLucy.Infrastructure.csproj` and `src/AskLucy.Web/AskLucy.Web.csproj` (research.md Decision 2) — also pinned `Newtonsoft.Json` 13.0.3 explicitly to resolve a transitive NU1903 high-severity advisory pulled in by Hangfire.Core's minimum dependency range
- [X] T002 [P] Add a Tesseract 5 .NET OCR binding package reference to `src/AskLucy.Infrastructure/AskLucy.Infrastructure.csproj` (verified legitimate: publisher `charlesw`, 9.6M downloads) — trained-data (`.traineddata`) language packs deferred to US2 implementation, not needed until `TesseractOcrEngine` (T067) is written (research.md Decision 3)
- [X] T003 [P] Add `DocumentFormat.OpenXml` and `Docnet.Core` package references to `src/AskLucy.Infrastructure/AskLucy.Infrastructure.csproj` (research.md Decision 5) — `UglyToad.PdfPig` was replaced with `Docnet.Core` after its NuGet listing was found to be untrustworthy (placeholder description, non-matching owner, no real version history); see research.md's Decision 5 correction note
- [X] T004 [P] Add `SixLabors.ImageSharp` package reference to `src/AskLucy.Infrastructure/AskLucy.Infrastructure.csproj` (research.md Decision 6) — pinned to `3.1.12`, not the latest `4.0.0`: v4 hard-fails the build without a paid commercial license key (`SixLaborsLicenseKey`/`sixlabors.lic`); 3.1.x is the last version under the prior license terms that builds without one. **`PDFtoImage` was originally added here too but removed during US2 (see T070's correction note) after it was found to crash the process when used alongside `Docnet.Core` — both ship a native `pdfium.dll` under the identical filename, and one silently overwrites the other at build time.**
- [X] T005 [P] Add the `@microsoft/signalr` npm package to `src/AskLucy.Web/ClientApp/package.json` (research.md Decision 7) — verified official Microsoft-maintained package before installing; `npm audit`'s one high-severity finding (`brace-expansion`, a transitive devDependency) predates this change (confirmed via `git diff` on the lockfile) and is unrelated to it
- [X] T006 Register `document-endpoints` (generous, per-user fixed window, matching `knowledge-base-endpoints`) and `document-upload-chunk-endpoints` (tighter, higher per-call cost) rate-limit policies in `src/AskLucy.Web/Program.cs` (constitution §6)
- [X] T007 Register Hangfire (`AddHangfire` with `UseSqlServerStorage` against the existing connection string, `AddHangfireServer`) and restrict the Hangfire dashboard route to an operator/administrator-only policy in `src/AskLucy.Web/Program.cs` and `src/AskLucy.Infrastructure/DependencyInjection.cs` (research.md Decision 2) — required adding `Hangfire.NetCore` to `AskLucy.Infrastructure.csproj` too (the `AddHangfire`/`AddHangfireServer` extension methods live there, not in `Hangfire.Core`); `HangfireDashboardAuthorizationFilter` added in `src/AskLucy.Web/Auth/` gates the dashboard to the Administrator/Super User roles. Solution builds clean (verified via `dotnet build`).

**Checkpoint**: Solution builds with all new dependencies restored; Hangfire dashboard reachable only by an operator. No domain code exists yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain entities, shared abstractions, persistence configuration/migration,
repositories, ownership guard, and real-time hub every user story depends on.

**⚠️ CRITICAL**: No user story task may begin until this phase is complete and the solution
builds with the new migration applied.

### Domain entities (data-model.md)

- [X] T008 [P] Create `Document` aggregate root — `OwnerId`/`FolderId`/`FileName`/`FileType`/`SizeBytes`/`CurrentVersionId`/`ProcessingStatus`/`ArchivedAtUtc` fields and `Rename`, `Archive`/`Restore`, `SoftDelete` methods (data-model.md, research.md Decision 1 — new `DocumentFileType` enum, not `KnowledgeBaseDocumentType`) in `src/AskLucy.Domain/Documents/Document.cs`
- [X] T009 [P] Create `DocumentVersion` entity — immutable snapshot fields (`VersionMajor`/`VersionMinor`/`StoredFileName`/`OriginalFileName`/`SizeBytes`/`ChecksumId`/`ExtractedText`/`ExtractedStructureJson`/`OcrTextRaw`/`PageCount`/`CreatedByUserId`) in `src/AskLucy.Domain/Documents/DocumentVersion.cs`
- [X] T010 [P] Create `DocumentFolder` entity — `OwnerId`/`ParentFolderId`/`Name`/`Depth` (computed at create/move) in `src/AskLucy.Domain/Documents/DocumentFolder.cs`
- [X] T011 [P] Create `DocumentMetadata` entity — editable fields plus `IsAutoExtracted` (flips permanently `false` on first user edit, FR-023) in `src/AskLucy.Domain/Documents/DocumentMetadata.cs`
- [X] T012 [P] Create `DocumentLanguage` entity — `LanguageCode`/`Role` (Primary/Secondary)/`ConfidenceScore` in `src/AskLucy.Domain/Documents/DocumentLanguage.cs`
- [X] T013 [P] Create `DocumentCategory` lookup entity — `Name`/`IsSystemDefined` in `src/AskLucy.Domain/Documents/DocumentCategory.cs`
- [X] T014 [P] Create `DocumentClassification` entity — `CategoryId`/`Source` (Automatic/UserOverride)/`ConfidenceScore` in `src/AskLucy.Domain/Documents/DocumentClassification.cs`
- [X] T015 [P] Create `DocumentPreview` entity — `DocumentVersionId`/`PreviewType`/`StoredFileName`/`PageNumber` in `src/AskLucy.Domain/Documents/DocumentPreview.cs`
- [X] T016 [P] Create `DocumentProcessingJob` entity — `DocumentId`/`DocumentVersionId`/`Status`/`HangfireJobId`/`StartedAtUtc`/`CompletedAtUtc`/`FailureReason`/`RetryCount` in `src/AskLucy.Domain/Documents/DocumentProcessingJob.cs`
- [X] T017 [P] Create `DocumentProcessingStage` entity — `DocumentProcessingJobId`/`StageType`/`Status`/`StartedAtUtc`/`CompletedAtUtc`/`FailureReason` in `src/AskLucy.Domain/Documents/DocumentProcessingStage.cs`
- [X] T018 [P] Create `DocumentProcessingLog` entity — append-only, `DocumentId`/`DocumentProcessingJobId?`/`EventType`/`Detail`/`OccurredAtUtc` in `src/AskLucy.Domain/Documents/DocumentProcessingLog.cs`
- [X] T019 [P] Create `DocumentTag` entity plus the `DocumentTagAssignment` many-to-many join type in `src/AskLucy.Domain/Documents/DocumentTag.cs`
- [X] T020 [P] Create `DocumentAuditLog` entity — append-only, distinct from `DocumentProcessingLog` (FR-051) in `src/AskLucy.Domain/Documents/DocumentAuditLog.cs`
- [X] T021 [P] Create `DocumentChecksum` entity — `Algorithm`/`Hash` (research.md Decision 8) in `src/AskLucy.Domain/Documents/DocumentChecksum.cs`
- [X] T022 [P] Create `DocumentStatistics` entity — `Scope` (User/Organization)/`OwnerId?`/aggregate fields/`ComputedAtUtc` in `src/AskLucy.Domain/Documents/DocumentStatistics.cs`
- [X] T023 [P] Create `DocumentNotification` entity — `UserId`/`DocumentId?`/`EventType`/`Message`/`IsRead`/`CreatedAtUtc` (data-model.md — no existing platform notification mechanism to reuse) in `src/AskLucy.Domain/Documents/DocumentNotification.cs`

### Shared abstractions (Application)

- [X] T024 [P] Create `IOcrEngine` abstraction in `src/AskLucy.Application/Abstractions/IOcrEngine.cs` (research.md Decision 3)
- [X] T025 [P] Create `IDocumentTextExtractor` abstraction (returns plain text + structured content + page count) in `src/AskLucy.Application/Abstractions/IDocumentTextExtractor.cs` (research.md Decision 5)
- [X] T026 [P] Create `IDocumentPreviewGenerator` abstraction in `src/AskLucy.Application/Abstractions/IDocumentPreviewGenerator.cs` (research.md Decision 6)
- [X] T027 [P] Create `IDocumentLanguageAndClassifier` abstraction (returns languages+confidence and a category) in `src/AskLucy.Application/Abstractions/IDocumentLanguageAndClassifier.cs` (research.md Decision 4 — wraps the existing `IAIProvider`/`IAIProviderResolver`)
- [X] T028 [P] Create `IProcessingNotifier` abstraction (stage-transition push + `DocumentNotification` creation/push) in `src/AskLucy.Application/Abstractions/IProcessingNotifier.cs` (research.md Decision 7)
- [X] T029 Create `IDocumentFileValidator`/`DocumentFileValidator` covering the full `DocumentFileType` set (PDF, OOXML, RTF, HTML, JSON, XML, Markdown, CSV, Text, PNG, JPEG, TIFF, BMP, WEBP) in `src/AskLucy.Application/Abstractions/IDocumentFileValidator.cs` and `src/AskLucy.Infrastructure/Documents/DocumentFileValidator.cs` — corrected during implementation: the original wording ("extend `IDocumentContentValidator`") would have hard-coupled this feature's validation to `KnowledgeBaseDocumentType`, which Decision 1 already established as the wrong bounded-context move; see research.md Decision 11
- [X] T030 [P] Create `IProcessingStageHandler` strategy interface (one implementation per `DocumentProcessingStage.StageType`) and `IDocumentProcessingPipeline` in `src/AskLucy.Application/Documents/Processing/IProcessingStageHandler.cs` and `IDocumentProcessingPipeline.cs` (research.md Decisions 2, 10 — the orchestrator resolves handlers by stage type via DI, persists stage state before/after each stage, and resumes from the first non-`Completed` stage; concrete stage logic is implemented per-stage in US2, not here)

### Configuration, persistence, repositories, cross-cutting

- [X] T031 [P] Create `DocumentUploadOptions` (`ChunkSizeBytes`, `MaxFileSizeBytes`, `UploadSessionExpiry`) and `DocumentStorageQuotaOptions` (`DefaultQuotaBytes`) bound via `IOptions<T>` + `ValidateOnStart` in `src/AskLucy.Application/Options/DocumentUploadOptions.cs`, `DocumentStorageQuotaOptions.cs`, and `src/AskLucy.Application/DependencyInjection.cs` — placed in `Application/Options`, not `Infrastructure`, mirroring `KnowledgeBaseDocumentOptions`'s precedent (read directly by Application-layer command handlers, constitution §3)
- [X] T032 Create EF Core Fluent API configurations for all 16 new entities (soft-delete `HasQueryFilter` on `Document`/`DocumentFolder`/`DocumentCategory`/`DocumentTag`; indexes on every FK/filter/sort column per constitution §5) plus their `DbSet<T>` registrations on `AskLucyDbContext` in `src/AskLucy.Persistence/Configurations/Documents/*.cs` (depends on T008–T023) — `Document.CurrentVersionId` is a plain indexed column with no DB-enforced FK (would otherwise create a circular constraint with `DocumentVersion.DocumentId`'s FK back to `Document`), validated at the Application layer instead
- [X] T033 Seed the starting `DocumentCategory` taxonomy (11 categories: Technical, Legal, Financial, Research, Contract, Specification, Manual, Drawing, Presentation, Report, Meeting Notes; `IsSystemDefined: true`) via `migrationBuilder.InsertData`, mirroring the `AIProviders`/`AIModels` seeding precedent (depends on T013, T032)
- [X] T034 Generate the EF Core migration `AddDocumentIntelligencePipeline` via `dotnet ef migrations add AddDocumentIntelligencePipeline -p src/AskLucy.Persistence -s src/AskLucy.Web`; verify `Down()` is reversible and `dotnet ef database update` succeeds against local SQL Server; confirm Hangfire's own `SqlServerStorage` schema installs cleanly alongside it on first application run (depends on T032, T033) — verified against a real local SQL Server LocalDB instance (`(localdb)\mssqllocaldb`, database `AskLucyDev`): all 17 tables (16 entities + the implicit `DocumentTagAssignments` join table) created cleanly with no cascade-path conflicts, and all 11 seeded categories confirmed present via `sqlcmd`. Hangfire schema installs automatically at app startup (`UseSqlServerStorage`), not part of this EF migration — not independently verified in this pass (would require running the full app).
- [X] T035 [P] Create `IDocumentRepository`/`DocumentRepository` — owner-scoped queries, checksum lookup for duplicate detection (FR-009) in `src/AskLucy.Application/Abstractions/IDocumentRepository.cs` and `src/AskLucy.Persistence/Repositories/DocumentRepository.cs` (depends on T034)
- [X] T036 [P] Create `IDocumentFolderRepository`/`DocumentFolderRepository` — descendant check (circular-move rejection), non-empty check in `src/AskLucy.Application/Abstractions/IDocumentFolderRepository.cs` and `src/AskLucy.Persistence/Repositories/DocumentFolderRepository.cs` (depends on T034)
- [X] T037 [P] Create `IDocumentProcessingJobRepository`/`DocumentProcessingJobRepository` and `IDocumentNotificationRepository`/`DocumentNotificationRepository` in `src/AskLucy.Application/Abstractions/` and `src/AskLucy.Persistence/Repositories/` (depends on T034)
- [X] T038 [P] Create `DocumentOwnershipGuard` (mirrors `KnowledgeBaseOwnershipGuard` — throws `KeyNotFoundException` when the caller doesn't own the target, so denial is indistinguishable from not-found, FR-048) in `src/AskLucy.Application/Documents/Authorization/DocumentOwnershipGuard.cs` (depends on T008)
- [X] T039 Create `DocumentProcessingHub` (on connect, joins a server-assigned per-user group; administrators additionally join an `admin-dashboard` group), map it (`/hubs/document-processing`) in `Program.cs`, and implement `ProcessingNotifier` (`IProcessingNotifier`) publishing `documentStageChanged`/`documentProcessingCompleted`/`documentProcessingFailed`/`notificationCreated` events into it, plus writing `DocumentNotification` rows, in `src/AskLucy.Infrastructure/Documents/ProcessingNotifier.cs` (research.md Decision 7; depends on T028, T037, T023) — corrected during implementation: the hub itself also had to move to `src/AskLucy.Infrastructure/Documents/DocumentProcessingHub.cs` (not `Web/Hubs/`), since `ProcessingNotifier` (Infrastructure) injecting `IHubContext<DocumentProcessingHub>` would otherwise require Infrastructure to reference Web — the wrong dependency direction (constitution §3). `Web`'s `Program.cs` references the hub type from `Infrastructure` the same way it already references everything else there. Also added `Microsoft.AspNetCore.SignalR.Core` to `AskLucy.Infrastructure.csproj` (needed for `Hub`/`IHubContext<T>` outside the `Sdk.Web` shared framework) and a JWT-bearer `OnMessageReceived` handler in `Program.cs` reading the token from an `access_token` query-string parameter for `/hubs/*` paths — SignalR's browser client cannot set an `Authorization` header on the WebSocket/SSE handshake.

**Checkpoint (VERIFIED 2026-08-04)**: `dotnet build "Ask Lucy.sln"` succeeds with 0 errors across all 10 projects (5 src + 5 test); `dotnet ef database update` applied cleanly against a real local SQL Server LocalDB instance (`AskLucyDev`) — all 17 tables (16 entities + the implicit tag-assignment join table) created, all 11 `DocumentCategory` rows seeded, no cascade-path conflicts. Hangfire dashboard route and SignalR hub are mapped and gated by authorization; not independently smoke-tested end-to-end in this pass (would require running the full app with a browser/API client). No user-facing behavior exists yet — user story work (US1 onward) can now begin.

---

## Phase 3: User Story 1 - Upload and manage documents (Priority: P1) 🎯 MVP

**Goal**: Upload one or more files (browse/drag-and-drop/paste, single or multiple, resumable),
then rename, download, archive, restore, or delete them.

**Independent Test**: Drag-and-drop a file, confirm it appears with an "Uploaded" status, then
rename, download, archive, restore, and soft-delete it — each action's result immediately
visible (quickstart.md Scenario 1).

### Tests for User Story 1

- [X] T040 [P] [US1] Unit tests for `Document` domain methods — `Rename`, `Archive`/`Restore` idempotency, `SoftDelete` in `tests/AskLucy.Domain.Tests/Documents/DocumentTests.cs` — 11 tests, all passing; also added `DocumentUploadSessionTests.cs` (4 tests) for the new entity below
- [X] T041 [P] [US1] Integration tests: chunked upload session start/chunk/complete happy path, and resuming after an interruption continues from `nextExpectedChunkIndex` without re-sending prior chunks (FR-005) in `tests/AskLucy.Application.Tests/Documents/ChunkedUploadTests.cs` — 6 tests, all passing
- [X] T042 [P] [US1] Integration test: a checksum match on complete-upload returns the duplicate signal, and both `CompleteUploadAsVersion`/`CompleteUploadAsNew` resolution paths work (FR-009) in `tests/AskLucy.Application.Tests/Documents/DuplicateDetectionTests.cs` — 4 tests, all passing
- [X] T043 [P] [US1] Integration test: upload rejected for content/extension mismatch and for an oversized file, each with a specific message, before any processing resource is consumed (FR-010, FR-011) in `tests/AskLucy.Application.Tests/Documents/UploadValidationTests.cs` — 3 tests, all passing
- [X] T044 [P] [US1] Integration tests: `RenameDocument`/`ArchiveDocument`/`RestoreDocument`/`DeleteDocument` commands, plus cross-user ownership on any of them returns not-found (FR-048) in `tests/AskLucy.Application.Tests/Documents/DocumentLifecycleCommandTests.cs` — 6 tests, all passing
- [X] T045 [P] [US1] Integration test: `CancelUpload` deletes already-received chunks, leaving no orphaned partial file (Edge Cases) in `tests/AskLucy.Application.Tests/Documents/CancelUploadTests.cs` — 2 tests, all passing
- [X] T046 [P] [US1] Playwright E2E: drag-and-drop upload, multi-file upload with independent per-file progress and cancel, rename/download/archive/restore/delete, resume-after-interruption (quickstart.md Scenario 1) in `tests/AskLucy.E2E.Tests/DocumentUploadLifecycle.spec.ts` — **NOT RUNNABLE IN THIS ENVIRONMENT** (no running frontend/backend + authenticated session available), same documented constraint as `KnowledgeBaseLifecycle.spec.ts`; references `fixtures/sample.pdf`/`sample.docx`/`large-sample.pdf`, which don't exist yet — add them before running this suite for real

### Implementation for User Story 1

- [X] T047 [US1] `StartUpload`/`UploadChunk`/`CompleteUpload`/`CancelUpload` commands+handlers — SHA-256 checksum computed streaming during chunk writes (research.md Decision 8), content validated via `IDocumentFileValidator` (not `IDocumentContentValidator` — see T029's correction note), `CompleteUpload` enqueues the processing pipeline on success (FR-020) in `src/AskLucy.Application/Documents/Commands/StartUpload/`, `UploadChunk/`, `CompleteUpload/`, `CancelUpload/` (depends on T024, T035, T038) — **required new infrastructure not in the original design**: a `DocumentUploadSession` entity (data-model.md addendum), an `IResumableUploadStorage` abstraction (temp-storage, distinct from `IFileStorage`) + its EF migration/repository, and a shared internal `DocumentUploadFinalizer` helper (validate → checksum → duplicate-check → save) reused by `CompleteUpload`/`SimpleUpload`/`CompleteUploadAsNew` — the chunked-upload session concept was implied by the contract but never modeled as an entity in Foundational
- [X] T048 [US1] `CompleteUploadAsVersion`/`CompleteUploadAsNew` commands resolving a duplicate-checksum match (FR-009) in `src/AskLucy.Application/Documents/Commands/CompleteUploadAsVersion/`, `CompleteUploadAsNew/` (depends on T047)
- [X] T049 [US1] Simple (non-chunked) upload command for small files, sharing validation/duplicate-detection with the chunked path (contracts/documents-api.md) in `src/AskLucy.Application/Documents/Commands/SimpleUpload/` (depends on T047) — a checksum match creates a lightweight `DocumentUploadSession` directly in `PendingDuplicateResolution` state so the client resolves it via the same two endpoints as the chunked flow
- [X] T050 [US1] `RenameDocument`/`ArchiveDocument`/`RestoreDocument`/`DeleteDocument` commands+handlers, ownership-guarded, soft delete for `DeleteDocument` (FR-016, FR-017, FR-019) in `src/AskLucy.Application/Documents/Commands/RenameDocument/`, `ArchiveDocument/`, `RestoreDocument/`, `DeleteDocument/` (depends on T038) — `Document.Restore()` (undo archive) and `Document.Undelete()` (undo soft-delete) are separate domain methods, both called from `RestoreDocumentCommand`, since the two flags are orthogonal (data-model.md)
- [X] T051 [P] [US1] `DocumentSummaryDto`/`DocumentDetailDto` in `src/AskLucy.Application/Documents/DocumentSummaryDto.cs`, `DocumentDetailDto.cs` — `categoryName`/`languagePrimary` are always null until US2/US3 land, by design
- [X] T052 [US1] `SearchDocuments` query (minimal: `view`+`folderId` only — extended with full filters in US4) and `GetDocument` query in `src/AskLucy.Application/Documents/Queries/SearchDocuments/`, `GetDocument/` (depends on T051) — cursor pagination via a new `DocumentCursor` helper mirroring `KnowledgeBaseCursor`
- [X] T053 [US1] Download-URL issuance via a `GetDocumentDownloadTokenQuery` (ownership-checked) plus a direct `ISignedUrlService.Sign` call in the controller — never a physical path (FR-015, FR-018, FR-050) in `src/AskLucy.Application/Documents/Queries/GetDocumentDownloadUrl/` (depends on T035) — **corrected during implementation**: the endpoint returns the signed URL as JSON, not a `302` redirect — this endpoint requires `[Authorize]`, but a browser's plain navigation to a redirect target never attaches a Bearer token; the client fetches the URL over an authenticated request, then separately navigates to it (an `[AllowAnonymous]` endpoint whose signature is its own authorization), mirroring `UsersController`'s avatar pattern; contracts/documents-api.md updated to match
- [X] T054 [US1] `DocumentsController` — upload-session endpoints, list/get/rename/archive/restore/delete/download (contracts/documents-api.md) in `src/AskLucy.Web/Controllers/v1/DocumentsController.cs` (depends on T047–T053)
- [X] T055 [US1] Request/response contract types in `src/AskLucy.Web/Contracts/DocumentContracts.cs` (depends on T054)
- [X] T056 [P] [US1] Frontend: `UploadPanel.tsx` (drag-and-drop, paste, multi-select, per-file progress, cancel, and an overall upload-queue view, FR-006) and `useResumableUpload.ts` (chunking for files ≥20MB, resume from `nextExpectedChunkIndex`; smaller files use the simple-upload path) in `src/AskLucy.Web/ClientApp/src/features/documents/components/UploadPanel.tsx`, `hooks/useResumableUpload.ts`
- [X] T057 [P] [US1] Frontend: `documentsApi.ts` client and `useDocuments.ts`/`useDocumentMutations.ts` (rename/archive/restore/delete/download) hooks in `src/AskLucy.Web/ClientApp/src/features/documents/api/documentsApi.ts`, `hooks/useDocuments.ts`, `hooks/useDocumentMutations.ts`
- [X] T058 [US1] `DocumentWorkspacePage.tsx` (grid + Active/Archived/Deleted tabs) + `DocumentCard.tsx`; wire the `/documents` route and a navigation entry in `src/AskLucy.Web/ClientApp/src/features/documents/pages/DocumentWorkspacePage.tsx`, `components/DocumentCard.tsx`, `src/routes/router.tsx`, `src/components/UserMenu.tsx` (depends on T056, T057) — verified: `tsc -b`, `eslint`, and `vite build` all clean; `DocumentWorkspacePage` correctly code-splits as its own ~12 KB lazy-loaded chunk

**Checkpoint (VERIFIED 2026-08-04)**: User Story 1 is independently functional — upload
(chunked and simple), browse, rename, download, archive/restore, delete, and duplicate-checksum
resolution all work; documents sit at `Uploaded`/`Queued` (processing execution is US2). Backend:
`dotnet build "Ask Lucy.sln"` — 0 errors; 40 new backend tests (11 `DocumentTests` + 4
`DocumentUploadSessionTests` + 25 Application-layer command/query tests across
`ChunkedUploadTests`/`DuplicateDetectionTests`/`UploadValidationTests`/
`DocumentLifecycleCommandTests`/`CancelUploadTests`) — all passing. The
`AddDocumentUploadSessions` migration applied cleanly against the same local SQL Server LocalDB
instance used for Foundational. Frontend: `tsc -b`, `eslint`, and `vite build` all clean.
Playwright E2E (T046) is written but not executed — no running environment available here.

---

## Phase 4: User Story 2 - Automatic document processing with visible status (Priority: P1)

**Goal**: Every uploaded document is automatically validated, OCR'd where needed, text/
structure-extracted, classified, language-detected, and previewed — visibly, durably (survives a
restart), with a specific error and working retry on failure.

**Independent Test**: Upload a text-layer PDF and a scanned image; both progress through visible
stages to `Completed`; a corrupted file lands in `Failed` with a specific error and working
retry; killing and restarting the process mid-job resumes without redoing finished stages
(quickstart.md Scenario 2).

### Tests for User Story 2

- [X] T059 [P] [US2] Unit tests per `IProcessingStageHandler` implementation using faked `IOcrEngine`/`IDocumentTextExtractor`/`IDocumentLanguageAndClassifier`/`IDocumentPreviewGenerator` in `tests/AskLucy.Application.Tests/Documents/Processing/StageHandlerTests.cs`
- [X] T060 [P] [US2] Integration test: a full pipeline run (faked engines) proceeds `Queued` → `Completed`, with OCR correctly `Skipped` when a text layer already exists in `tests/AskLucy.Application.Tests/Documents/Processing/DocumentProcessingPipelineTests.cs`
- [X] T061 [P] [US2] Integration test: a failing stage lands the job in `Failed` with a specific `failureReason`; `RetryProcessing` returns `409` when the job isn't currently `Failed` (FR-028, FR-029, Edge Cases) in `tests/AskLucy.Application.Tests/Documents/Processing/ProcessingFailureAndRetryTests.cs`
- [X] T062 [P] [US2] Test simulating a crash mid-stage (interrupt and resume the pipeline run) confirming already-`Completed`/`Skipped` stages are never re-executed (FR-030a, research.md Decision 10) in `tests/AskLucy.Application.Tests/Documents/Processing/ProcessingDurabilityTests.cs`. Correction: relocated from `tests/AskLucy.Infrastructure.Tests/` — the resume/skip logic under test is entirely inside the Application-layer `DocumentProcessingPipeline.RunJobAsync` and has no dependency on Hangfire's own storage/crash-recovery internals, which are a trusted third-party concern this codebase doesn't need to re-verify.
- [X] T063 [P] [US2] Infrastructure tests: `TesseractOcrEngine` against a real scanned sample (SC-008 accuracy spot-check), `OpenXmlTextExtractor`/`DocnetPdfTextExtractor` against real DOCX/PDF samples recovering headings/tables/lists in `tests/AskLucy.Infrastructure.Tests/Documents/Extraction/`
- [X] T064 [P] [US2] Unit test: `AiDocumentLanguageAndClassifier` prompt construction/response parsing against a faked `IAIProvider` (no live provider call) in `tests/AskLucy.Infrastructure.Tests/Documents/AiDocumentLanguageAndClassifierTests.cs`. Correction: relocated from `tests/AskLucy.Application.Tests/` — `AiDocumentLanguageAndClassifier` is a concrete Infrastructure class, and `AskLucy.Application.Tests` doesn't (and per constitution §3 shouldn't) reference `AskLucy.Infrastructure`.
- [X] T065 [P] [US2] Integration test: `DocumentProcessingHub` pushes stage/completion/failure events only to the owning user's connection group in `tests/AskLucy.Web.Tests/Documents/DocumentProcessingHubTests.cs`. Extended with `tests/AskLucy.Web.Tests/Documents/ProcessingNotifierTests.cs` covering the other half of the guarantee (`ProcessingNotifier` only ever targets the owning user's group, never a broadcast), plus a `ProblemDetailsMiddlewareTests` case confirming `ProcessingNotInFailedStateException` maps to `409` with `reason: "NotInFailedState"` (T074).
- [X] T066 [P] [US2] Playwright E2E: upload and watch a document progress to `Completed` without a page refresh; upload a bad file and confirm a specific `Failed` reason plus a working retry (quickstart.md Scenario 2) in `tests/AskLucy.E2E.Tests/DocumentProcessing.spec.ts`. Written but NOT executed — no running frontend/backend + authenticated session available in this sandbox (same documented constraint as T046/`DocumentUploadLifecycle.spec.ts` and every other existing suite in this project).

### Implementation for User Story 2

- [X] T067 [US2] `TesseractOcrEngine` (`IOcrEngine`) — multilingual trained-data lookup, no-op/skip when OCR isn't needed in `src/AskLucy.Infrastructure/Documents/Ocr/TesseractOcrEngine.cs` (depends on T024, T002)
- [X] T068 [P] [US2] `OpenXmlTextExtractor` (DOCX/XLSX/PPTX structured extraction) in `src/AskLucy.Infrastructure/Documents/Extraction/OpenXmlTextExtractor.cs` (depends on T025, T003)
- [X] T069 [P] [US2] `DocnetPdfTextExtractor` (PDF structured extraction) in `src/AskLucy.Infrastructure/Documents/Extraction/DocnetPdfTextExtractor.cs` (depends on T025, T003) — corrected from the originally-planned `UglyToad.PdfPig`, which turned out to be an untrustworthy NuGet package (see research.md Decision 11).
- [X] T070 [P] [US2] `PdfPreviewGenerator` (page rasterization/thumbnails) and `ImageThumbnailGenerator` (`IDocumentPreviewGenerator`) in `src/AskLucy.Infrastructure/Documents/Preview/PdfPreviewGenerator.cs`, `ImageThumbnailGenerator.cs` (depends on T026, T004). **CRITICAL correction, discovered while writing T063's tests**: originally implemented `PdfPreviewGenerator` with `PDFtoImage`. Running a `Docnet.Core`-based test (`DocnetPdfTextExtractorTests`) alongside a `PdfPreviewGenerator`-based test (`TesseractOcrEngineTests`) in the same process crashed it outright (`STATUS_BREAKPOINT`). Root cause: `Docnet.Core` and `PDFtoImage`'s dependency `Bblanchon.PDFium.Win32` both ship a native binary named exactly `pdfium.dll`; MSBuild's file copy let Bblanchon's (a much newer Chromium PDFium build) silently overwrite Docnet.Core's own bundled (2023-era) `pdfium.dll` in the output directory, so Docnet.Core ended up calling into a PDFium build it was never compiled against. This is not a test-only risk — `OcrStageHandler` calls both Docnet.Core (text-layer check) and `PdfPreviewGenerator` within the same Hangfire worker process for the same PDF, so every scanned PDF in production risked crashing the worker. Fixed by rewriting `PdfPreviewGenerator` to rasterize pages via `Docnet.Core`'s own `IPageReader.GetImage()` (raw BGRA32 pixels encoded to PNG via `SixLabors.ImageSharp`) and removing the `PDFtoImage` package reference entirely — only one native PDFium build now ever loads. Verified: the previously-crashing test combination passes after the fix (45/45 in `AskLucy.Infrastructure.Tests`).
- [X] T071 [US2] `AiDocumentLanguageAndClassifier` (`IDocumentLanguageAndClassifier`) — versioned system prompt via `IAIProviderResolver` returning primary/secondary languages + confidence and a category (research.md Decision 4) in `src/AskLucy.Infrastructure/Documents/AiDocumentLanguageAndClassifier.cs` (depends on T027)
- [X] T072 [US2] `IProcessingStageHandler` implementations — `ValidationStageHandler`, `OcrStageHandler`, `TextExtractionStageHandler`, `MetadataExtractionStageHandler`, `ClassificationStageHandler`, `LanguageDetectionStageHandler`, `PreviewGenerationStageHandler` — each persists `DocumentProcessingStage`/`DocumentProcessingLog` rows and pushes a notifier event on transition (FR-013, FR-027) in `src/AskLucy.Application/Documents/Processing/Stages/*.cs` (depends on T030, T067–T071, T039). Correction: `IProcessingStageHandler.ExecuteAsync` returns `Task<ProcessingStageOutcome>` (`Completed`/`Skipped`), not a bare `Task` — the orchestrator needs to distinguish "did real work" from "determined nothing was needed" (e.g. OCR on a PDF that already has a text layer) to persist the right `DocumentProcessingStageStatus`.
- [X] T073 [US2] Wire `DocumentProcessingPipeline` to run the 7 stage handlers via Hangfire, persist `HangfireJobId` on `DocumentProcessingJob`, and resume by skipping any stage already `Completed`/`Skipped` (research.md Decisions 2, 10) in `src/AskLucy.Application/Documents/Processing/DocumentProcessingPipeline.cs` (depends on T072). Corrections: (1) implemented as one Hangfire job whose `RunJobAsync` loops through all 7 stages sequentially in-process — not a `BackgroundJob.ContinueJobWith` chain of 7 separate Hangfire jobs. The DB-persisted `DocumentProcessingStage` rows are already the durability source of truth (Hangfire's own crash recovery re-invokes `RunJobAsync`, which skips every already-`Completed`/`Skipped` stage), so a 7-job chain would add Hangfire-side complexity without adding any additional durability. (2) Schedules via the injected `Hangfire.IBackgroundJobClient` rather than the static `Hangfire.BackgroundJob` facade — Hangfire's own recommended pattern, and the only way to unit-test `EnqueueAsync`/`RetryAsync`'s scheduling call without a live `JobStorage.Current`. Also required adding a `Hangfire.Core` package reference to `AskLucy.Application.csproj` (and to `AskLucy.Application.Tests.csproj` for the fakeable `IBackgroundJobClient` type) — treated as an application-layer scheduling abstraction (comparable to MediatR), while the storage-specific packages (`Hangfire.NetCore`, `Hangfire.SqlServer`) remain Infrastructure-only.
- [X] T074 [US2] `RetryProcessing` command — `409` if the current job isn't `Failed`, otherwise re-enqueues from the first non-`Completed` stage (FR-029) in `src/AskLucy.Application/Documents/Commands/RetryProcessing/` (depends on T073). Correction: the `409` with `reason: "NotInFailedState"` (contracts/document-processing-api.md) required a new `AskLucy.Domain.Documents.ProcessingNotInFailedStateException` — the existing `DomainRuleViolationException` maps to `400 Bad Request` in `ProblemDetailsMiddleware`, not the `409 Conflict` this specific case needs, and neither existing exception type carries a machine-readable reason code. `DocumentProcessingJob.Retry` now throws the new type; the middleware maps it to 409 and adds `Extensions["reason"] = "NotInFailedState"`.
- [X] T075 [US2] `GetDocumentProcessingStatus` and `GetProcessingHistory` queries (FR-013, FR-027) in `src/AskLucy.Application/Documents/Queries/GetDocumentProcessingStatus/`, `GetProcessingHistory/` (depends on T073)
- [X] T076 [US2] `DocumentProcessingController` — status/history/retry endpoints (contracts/document-processing-api.md) in `src/AskLucy.Web/Controllers/v1/DocumentProcessingController.cs` (depends on T074, T075). Scoped to only the 3 per-document endpoints in this contract file's "Processing status & history" and "Retry" sections — the dashboard and notifications endpoints in the same contract file belong to a later user story (US6) and are intentionally not implemented here.
- [X] T077 [P] [US2] Frontend: `ProcessingStatusBadge.tsx`, `ProcessingHistoryPanel.tsx`, `useDocumentProcessingHub.ts` (SignalR client + 5s TanStack Query polling fallback, research.md Decision 7) in `src/AskLucy.Web/ClientApp/src/features/documents/components/ProcessingStatusBadge.tsx`, `ProcessingHistoryPanel.tsx`, `hooks/useDocumentProcessingHub.ts`. Correction: `useDocumentProcessingHub` returns an `isLive` flag (rendered as a quiet "Live"/"Polling every 5s" caption in `ProcessingStatusBadge`) instead of silently swallowing a failed/dropped SignalR connection — required by CLAUDE.md's no-silent-failures rule; the 5s poll still keeps the feature fully functional either way.
- [X] T078 [US2] Wire processing status/history/retry into a new `DocumentDetailPanel.tsx` and into `DocumentWorkspacePage.tsx`'s list rows in `src/AskLucy.Web/ClientApp/src/features/documents/components/DocumentDetailPanel.tsx` (depends on T077, T058)

**Checkpoint**: User Stories 1+2 together form a real MVP — upload a document and it becomes a
fully processed intelligent document, visibly, durably, with retry.

---

## Phase 5: User Story 3 - Review and correct extracted content (Priority: P2)

**Goal**: View extracted text/metadata/language/classification for a completed document; edit
metadata, override classification, add/remove tags.

**Independent Test**: Open a completed document, edit metadata fields, override its
classification, add a tag — all persist and display correctly (quickstart.md Scenario 3).

### Tests for User Story 3

- [X] T079 [P] [US3] Integration test: `UpdateDocumentMetadata` concurrent-edit staleness — a `RowVersion` mismatch merges the change and returns `wasStale: true` rather than rejecting (research.md Decision 9, FR-031a) in `tests/AskLucy.Application.Tests/Documents/UpdateDocumentMetadataTests.cs`. Extended with a real-database counterpart in `tests/AskLucy.Persistence.Tests/Documents/DocumentMetadataConcurrencyTests.cs`, since the Application-layer test can only verify the handler's wiring (a faked repository can't meaningfully exercise EF Core's actual rowversion conflict detection) — the Persistence test runs two real `DbContext`s against LocalDB to prove the reload-reapply-retry merge genuinely works end-to-end.
- [X] T080 [P] [US3] Integration test: `OverrideClassification` sets `source: UserOverride` and persists (FR-026) in `tests/AskLucy.Application.Tests/Documents/OverrideClassificationTests.cs`
- [X] T081 [P] [US3] Integration test: `AddTag`/`RemoveTag`, tag name uniqueness per owner, a tag is usable as a search filter (FR-032) in `tests/AskLucy.Application.Tests/Documents/DocumentTagTests.cs`
- [X] T082 [P] [US3] Playwright E2E: edit metadata, override classification, add/remove a tag, and confirm the stale-edit warning banner appears in a two-tab concurrent-edit scenario (quickstart.md Scenario 3) in `tests/AskLucy.E2E.Tests/DocumentMetadataReview.spec.ts`. Written but NOT executed — no running environment available here (same constraint as every other E2E spec in this project).

### Implementation for User Story 3

- [X] T083 [US3] `UpdateDocumentMetadata` command+handler — `RowVersion` staleness merge-and-warn (research.md Decision 9, FR-031a) in `src/AskLucy.Application/Documents/Commands/UpdateDocumentMetadata/` (depends on T011, T038). Correction: the reload/retry mechanics needed direct EF Core `ChangeTracker` access (`Entry(...).Property(...).OriginalValue`, `Entry(...).ReloadAsync()`), which Application must not reference — added `IDocumentRepository.SaveMetadataResolvingStalenessAsync` to keep that ORM-specific dance behind the repository abstraction; it owns its own (possible double) save rather than going through the shared `IUnitOfWork`, since the retry is intrinsic to this one operation.
- [X] T084 [US3] `OverrideClassification` command+handler (FR-026) in `src/AskLucy.Application/Documents/Commands/OverrideClassification/` (depends on T014, T038). Correction: added `DocumentClassification.CreateUserOverride` — a document whose Classification stage was `Skipped` (no extracted text) has no automatic classification to override, but FR-026 still applies.
- [X] T085 [US3] `AddTag`/`RemoveTag` commands + `ListTags` query (FR-032) in `src/AskLucy.Application/Documents/Commands/AddTag/`, `RemoveTag/`, `Queries/ListTags/` (depends on T019, T038)
- [X] T086 [US3] Extend `GetDocument`'s response with `extractedText`/`extractedStructure`/`metadata`/`languages`/`classification`/`rowVersion` (contracts/documents-api.md) in `src/AskLucy.Application/Documents/Queries/GetDocument/` (depends on T052, T083). Correction: the metadata-edit concurrency token lives on the nested `metadata.rowVersion` (`DocumentMetadata`'s own row), distinct from the top-level `rowVersion` (the parent `Document` row, used for document-level edits like rename) — they're different EF-mapped rows, so one shared token would never actually detect a metadata-specific conflict.
- [X] T087 [US3] Extend `DocumentsController` with metadata/classification/tag endpoints (contracts/documents-api.md) in `src/AskLucy.Web/Controllers/v1/DocumentsController.cs` (depends on T083–T085). Also added `GET /api/v1/documents/categories` (not explicitly in the contract) — the classification-override picker needs a category list to populate its dropdown from.
- [X] T088 [P] [US3] Frontend: `MetadataPanel.tsx` (editable fields, `isAutoExtracted` indicator, stale-edit warning banner) and a classification-override control and tag input in `src/AskLucy.Web/ClientApp/src/features/documents/components/MetadataPanel.tsx`
- [X] T089 [US3] Wire `MetadataPanel` into `DocumentDetailPanel.tsx` (depends on T088, T078)

**Checkpoint**: Reviewing and correcting extracted content works end-to-end.

---

## Phase 6: User Story 4 - Organize documents into folders and find them again (Priority: P2)

**Goal**: Create/nest folders, move and duplicate documents, search and filter by
filename/metadata/author/language/tags/category/date/status.

**Independent Test**: Create a folder, move documents into it, duplicate one, then search and
combine multiple filters to locate a specific document (quickstart.md Scenario 4).

### Tests for User Story 4

- [X] T090 [P] [US4] Integration tests: `CreateFolder`/`RenameFolder`/`MoveFolder` (rejects moving a folder into itself or a descendant) in `tests/AskLucy.Application.Tests/Documents/FolderCommandTests.cs`
- [X] T091 [P] [US4] Integration test: `DeleteFolder` on a non-empty folder requires an explicit `onContainedDocuments` choice, `400` if omitted (Edge Cases) in `tests/AskLucy.Application.Tests/Documents/DeleteFolderCommandTests.cs`
- [X] T092 [P] [US4] Integration test: `DuplicateDocument` produces an independent copy (own file, metadata, tags; fresh processing history) (FR-034) in `tests/AskLucy.Application.Tests/Documents/DuplicateDocumentTests.cs`
- [X] T093 [P] [US4] Integration test: `SearchDocuments` with combined filters (category + language + tag + date + status) returns only the intersection (FR-035–FR-037) in `tests/AskLucy.Application.Tests/Documents/SearchDocumentsFilterTests.cs`. Extended with a real-database counterpart in `tests/AskLucy.Persistence.Tests/Documents/DocumentSearchFilterTests.cs` — the filter intersection is real EF Core LINQ-to-SQL (subqueries against child tables with no navigation properties), not meaningfully fakeable at the Application layer.
- [X] T094 [P] [US4] Playwright E2E: create folder, move documents, duplicate, search + combined filters (quickstart.md Scenario 4) in `tests/AskLucy.E2E.Tests/DocumentOrganization.spec.ts`. Written but NOT executed — no running environment available here.

### Implementation for User Story 4

- [X] T095 [US4] `CreateFolder`/`RenameFolder`/`MoveFolder`/`DeleteFolder` commands+handlers (FR-033) in `src/AskLucy.Application/Documents/Commands/CreateFolder/`, `RenameFolder/`, `MoveFolder/`, `DeleteFolder/` (depends on T010, T036, T038)
- [X] T096 [US4] `MoveDocument` command (FR-033) in `src/AskLucy.Application/Documents/Commands/MoveDocument/` (depends on T038)
- [X] T097 [US4] `DuplicateDocument` command — independent file copy via `IFileStorage`, metadata/tags copy, fresh processing history (FR-034) in `src/AskLucy.Application/Documents/Commands/DuplicateDocument/` (depends on T038, T047). Correction: added `DocumentMetadata.CreateCopy`/`DocumentClassification.CreateCopy` domain factories and `IDocumentRepository.GetChecksumHashAsync` (reuses the source's content hash for the copy rather than recomputing SHA-256 over bytes already known to be identical).
- [X] T098 [US4] Extend `SearchDocuments` with `q`/`author`/`language`/`tag`/`categoryId`/`dateFrom`/`dateTo`/`status` filters and combination logic (FR-035–FR-037) in `src/AskLucy.Application/Documents/Queries/SearchDocuments/` (depends on T052). Correction: filters are bundled into a `DocumentSearchFilters` record (`IDocumentRepository.SearchAsync`'s new parameter) rather than seven loose arguments.
- [X] T099 [US4] `GetFolderTree` query (FR-033) in `src/AskLucy.Application/Documents/Queries/GetFolderTree/` (depends on T095)
- [X] T100 [US4] `DocumentFoldersController` (or folder routes on `DocumentsController`) and folder/move/duplicate endpoints (contracts/document-versions-folders-api.md) (depends on T095–T099). Implemented as folder routes on the existing `DocumentsController` (the contract's explicitly offered alternative) — request DTOs are named `CreateDocumentFolderRequest`/`RenameDocumentFolderRequest`/`MoveDocumentFolderRequest`/`MoveDocumentToFolderRequest` to avoid colliding with `KnowledgeBaseContracts`'s identically-shaped, differently-scoped records of the same short names.
- [X] T101 [P] [US4] Frontend: `DocumentFolderTree.tsx` navigation component, move/duplicate actions, and a full filter bar in `src/AskLucy.Web/ClientApp/src/features/documents/components/DocumentFolderTree.tsx`. Move/duplicate actions live on `DocumentCard.tsx`; the filter bar is `DocumentFilterBar.tsx`.
- [X] T102 [US4] Wire folder navigation and filters into `DocumentWorkspacePage.tsx` (depends on T101, T058)

**Checkpoint**: Organization and discovery work end-to-end.

---

## Phase 7: User Story 5 - Version documents over time (Priority: P3)

**Goal**: Replace a document with an updated file (creating a new version), view/compare the
version timeline, restore an earlier version — with every version's original file preserved.

**Independent Test**: Replace a document, confirm the prior version's file remains retrievable,
compare the two versions, then restore the prior version as current (quickstart.md Scenario 5).

### Tests for User Story 5

- [X] T103 [P] [US5] Integration test: `ReplaceDocument` creates a new version, repoints `CurrentVersionId`; the prior version's file/content is untouched (FR-038) in `tests/AskLucy.Application.Tests/Documents/ReplaceDocumentTests.cs`
- [X] T104 [P] [US5] Integration test: `RestoreDocumentVersion` repoints without deleting history; returns `409 VersionUploadInProgress` while a replacement upload is in flight for the same document (Edge Cases) in `tests/AskLucy.Application.Tests/Documents/RestoreDocumentVersionTests.cs`
- [X] T105 [P] [US5] Integration test: `CompareVersions` diffs extracted text and metadata (FR-042) in `tests/AskLucy.Application.Tests/Documents/CompareVersionsTests.cs`
- [X] T106 [P] [US5] Integration test: `GetVersionTimeline` ordering and creator/date fields (FR-040) in `tests/AskLucy.Application.Tests/Documents/VersionTimelineTests.cs`
- [X] T107 [P] [US5] Playwright E2E: replace, view timeline, restore, compare (quickstart.md Scenario 5) in `tests/AskLucy.E2E.Tests/DocumentVersioning.spec.ts`. Written but NOT executed — no running environment available here (same constraint as every other E2E spec in this project).

### Implementation for User Story 5

- [X] T108 [US5] `ReplaceDocument` command — creates a `DocumentVersion`, repoints `CurrentVersionId`, enqueues processing for the new version independently of the prior version's completed job (FR-038, FR-039) in `src/AskLucy.Application/Documents/Commands/ReplaceDocument/` (depends on T047, T073). Correction: the "same upload session flow ... targeted at an existing document" (contract) needed `StartUpload`/`DocumentUploadSession` to know the target document **before** the upload finishes, not just at finalize time — added an optional `TargetDocumentId` to `DocumentUploadSession`/`StartUploadCommand` (nullable, `?documentId=` query param on `POST /documents/uploads`) precisely so `RestoreDocumentVersion`'s in-flight check (T109) has something to query. Deliberately bypasses `DocumentUploadFinalizer`'s cross-document duplicate check — a replace is always treated as intentional new content, never a "did you mean to link this" prompt.
- [X] T109 [US5] `RestoreDocumentVersion` command — in-flight replacement-upload conflict check (FR-041, Edge Cases) in `src/AskLucy.Application/Documents/Commands/RestoreDocumentVersion/` (depends on T108). Added `IDocumentUploadSessionRepository.GetInProgressForDocumentAsync` and `VersionUploadInProgressException` (409 + `reason: "VersionUploadInProgress"`, same pattern as US2's `ProcessingNotInFailedStateException`).
- [X] T110 [US5] `CompareVersions` query (FR-042) in `src/AskLucy.Application/Documents/Queries/CompareVersions/` (depends on T009). Correction: implemented a small self-contained LCS line-diff (`LineDiff.cs`, no new NuGet dependency — FR-042 only requires "at minimum" a comparison, not a specific format) for `extractedTextDiff`. **`metadataDiff` compares each `DocumentVersion`'s own intrinsic fields (`originalFileName`/`sizeBytes`/`pageCount`) instead of `DocumentMetadata`'s title/author/keywords** — `DocumentMetadata` is one current-state row per document (unique index on `DocumentId`, data-model.md), never versioned, so there is no per-version metadata snapshot to diff against.
- [X] T111 [US5] `GetVersionTimeline` query (FR-040) in `src/AskLucy.Application/Documents/Queries/GetVersionTimeline/` (depends on T009). Added `IDocumentRepository.GetVersionsByDocumentIdAsync`.
- [X] T112 [US5] `DocumentVersionsController` (contracts/document-versions-folders-api.md) in `src/AskLucy.Web/Controllers/v1/DocumentVersionsController.cs` (depends on T108–T111)
- [X] T113 [P] [US5] Frontend: `VersionTimeline.tsx`, `VersionCompareDialog.tsx` in `src/AskLucy.Web/ClientApp/src/features/documents/components/`. Also added `hooks/useReplaceDocument.ts` (chunked upload targeting an existing document, mirroring `useResumableUpload` without the duplicate-detection branch).
- [X] T114 [US5] Wire the version timeline into `DocumentDetailPanel.tsx` (depends on T113, T078)

**CRITICAL correction, discovered while implementing T108/T110**: `MetadataExtractionStageHandler`/`ClassificationStageHandler` (US2, T072) unconditionally called `AddMetadata`/`AddClassification` on every processing run. `DocumentMetadata.DocumentId` and `DocumentClassification.DocumentId` both have a unique index (one row per document) — harmless in US2 because a document was only ever processed once, but `ReplaceDocument` enqueues a **second** processing job for the same `DocumentId`, so reprocessing a replaced version would have hit a unique-constraint violation on save and crashed the entire pipeline for every document ever replaced. Fixed by adding `DocumentMetadata.ApplyReExtraction`/`DocumentClassification.ApplyAutomaticReclassification` — idempotent upserts that update the existing row instead of inserting a second one, and are themselves no-ops when the user already customized that field (`IsAutoExtracted == false` / `Source == UserOverride`) so a replace can never silently overwrite a user's manual correction (FR-023, FR-026). `DocumentLanguage` (no such unique constraint, but also unbounded accumulation risk) now clears the previous set via a new `IDocumentRepository.RemoveLanguages` before re-adding. Covered by `tests/AskLucy.Domain.Tests/Documents/DocumentMetadataReExtractionTests.cs` and `DocumentClassificationReclassificationTests.cs`.

**Checkpoint**: Versioning works end-to-end.

---

## Phase 8: User Story 6 - Monitor processing activity and get notified (Priority: P3)

**Goal**: Per-user processing dashboard (queue/jobs/retry/statistics), an organization-wide
administrator view, and in-app notifications for the six key lifecycle events.

**Independent Test**: Upload several documents (one engineered to fail); confirm accurate
per-user dashboard counts and notifications; confirm the admin org-wide view aggregates across
users while a non-admin is denied (quickstart.md Scenario 6).

### Tests for User Story 6

- [X] T115 [P] [US6] Unit test: `DocumentStatisticsRecomputeJob` aggregates correctly per-user and organization-wide from fixture data in `tests/AskLucy.Infrastructure.Tests/Documents/DocumentStatisticsRecomputeJobTests.cs`
- [X] T116 [P] [US6] Integration test: `GetDocumentDashboardSummary` is scoped to the caller only; `GetOrganizationDashboardSummary` aggregates organization-wide (FR-045a) in `tests/AskLucy.Application.Tests/Documents/DashboardSummaryTests.cs`. Correction: the "requires the administrator role, `403` otherwise" half is a controller-level `[Authorize(Policy = "AdministratorOrSuperUser")]` concern, not a MediatR-handler concern — an Application-layer unit test can't meaningfully exercise ASP.NET Core's authorization pipeline. Covered instead by `tests/AskLucy.Web.Tests/Documents/DashboardAuthorizationTests.cs` (real HTTP requests against a self-signed test JWT, mirroring `RoleAuthorizationTests`).
- [X] T117 [P] [US6] Integration test: a `DocumentNotification` is created and pushed for all six event types; reaching the storage limit blocks further upload and fires `StorageLimitReached` (FR-011, FR-047) in `tests/AskLucy.Application.Tests/Documents/NotificationTests.cs`. `ProcessingCompleted`/`ProcessingFailed` were already covered by US2's pipeline tests; extended `tests/AskLucy.Application.Tests/Documents/Processing/ProcessingFailureAndRetryTests.cs` with two cases asserting the OCR-stage failure fires `OcrFailed` specifically (not the generic `ProcessingFailed`) and a non-OCR-stage failure fires `ProcessingFailed`.
- [X] T118 [P] [US6] Integration test: a failed document appears correctly in the retry-queue view (US6 AC2) in `tests/AskLucy.Persistence.Tests/Documents/RetryQueueTests.cs`. Correction: relocated from `Application.Tests` — the "only the latest job per document" grouping (`GroupBy` + `OrderByDescending` + `First` per group, so a document that failed once and later succeeded after a retry is never double-counted) is genuine LINQ-to-SQL, not meaningfully provable against a faked repository (same reasoning as T093's `DocumentSearchFilterTests`).
- [X] T119 [P] [US6] Playwright E2E: dashboard counts update live, notifications arrive, admin org-wide view works while a non-admin gets `403` (quickstart.md Scenario 6) in `tests/AskLucy.E2E.Tests/DocumentDashboard.spec.ts`. Written but NOT executed — no running environment available here (same constraint as every other E2E spec in this project).

### Implementation for User Story 6

- [X] T120 [US6] `DocumentStatisticsRecomputeJob` — Hangfire recurring job computing both `User`- and `Organization`-scope `DocumentStatistics` rows (data-model.md) in `src/AskLucy.Infrastructure/Documents/DocumentStatisticsRecomputeJob.cs` (depends on T022, T034, T007). Scheduled via `RecurringJob.AddOrUpdate` (`Cron.Minutely`) in `Program.cs` — the live dashboard counts (queue/in-progress/completed-today/failed) are computed directly per-request instead, so this interval only governs the slower-changing totals/storage/distribution fields; SC-011's 5-second budget is met by the live counts, not this job's cadence. Added `IDocumentStatisticsRepository` (`ComputeAggregateAsync`, `ListDistinctOwnerIdsAsync`, `GetByScopeAsync`) since no repository existed yet for `DocumentStatistics`.
- [X] T121 [US6] Wire a storage-quota-reached check into `StartUpload`/`CompleteUpload` (FR-011) that publishes a `StorageLimitReached` notification via `IProcessingNotifier` in `src/AskLucy.Application/Documents/Commands/StartUpload/`, `CompleteUpload/` (depends on T031, T047, T039). Implemented once inside `DocumentUploadFinalizer.FinalizeAsync` (shared by `CompleteUpload` and `SimpleUpload`, so both are covered by one check) plus a duplicate early check in `StartUploadCommandHandler` itself (chunked-upload path — rejected before a single chunk transfers, not just at completion, per FR-011's "before consuming processing resources"). Reuses `IDocumentStatisticsRepository.ComputeAggregateAsync`'s `TotalStorageBytes` rather than a second bespoke "total storage" query.
- [X] T122 [US6] `GetDocumentDashboardSummary` query (per-user) (FR-045) in `src/AskLucy.Application/Documents/Queries/GetDocumentDashboardSummary/` (depends on T120). Added `IDocumentProcessingJobRepository.GetDashboardCountsAsync`/`GetRetryQueueAsync` (both based on only the *latest* job per document, never a raw historical count). Statistics fall back to an `Empty` snapshot (never an error) when the recompute job hasn't run yet for this owner.
- [X] T123 [US6] `GetOrganizationDashboardSummary` query — administrator-role-gated (research.md Decision 11, FR-045a) in `src/AskLucy.Application/Documents/Queries/GetOrganizationDashboardSummary/` (depends on T120). Role-gating lives on the controller action (`[Authorize(Policy = "AdministratorOrSuperUser")]`), mirroring `AdminDashboardController` — the handler itself has no caller/role awareness, matching FR-045a's "never exposes per-user document content" via simply never querying anything document-specific.
- [X] T124 [US6] Wire notification creation/push (`ProcessingNotifier`) for all six event types from the relevant upload/processing command and stage handlers (FR-047) — touches `src/AskLucy.Application/Documents/Commands/CompleteUpload/`, `Processing/Stages/*.cs`, `Commands/ReplaceDocument/` (depends on T039, T023, T047, T073, T108). Also touches `SimpleUpload/`, `CompleteUploadAsVersion/`, `CompleteUploadAsNew/` (not explicitly named in this task, but each is a distinct upload-completion path that needed the same event wired for FR-047 to actually cover "all six event types" rather than only the chunked-upload path). `CompleteUploadAsVersion`/`ReplaceDocument` fire `VersionCreated` (a new version of an existing document, not a fresh upload); `CompleteUpload`/`SimpleUpload`/`CompleteUploadAsNew` fire `UploadCompleted` (a genuinely new document).
- [X] T125 [US6] `GetNotifications` query + `MarkNotificationRead` command in `src/AskLucy.Application/Documents/Queries/GetNotifications/`, `Commands/MarkNotificationRead/` (depends on T023, T038). Correction: `IDocumentNotificationRepository.ListForUserAsync` was extended to real cursor pagination (`DocumentCursor`, matching `SearchAsync`'s convention) — the contract explicitly specifies `?cursor=...`, which the original flat `Take(pageSize)` signature didn't support.
- [X] T126 [US6] Extend `DocumentProcessingController` with dashboard/organization-dashboard/notifications endpoints (contracts/document-processing-api.md) (depends on T122–T125)
- [X] T127 [P] [US6] Frontend: `ProcessingDashboard.tsx` (per-user), `OrganizationDashboard.tsx` (admin-only, gated by the existing `useIsAdmin` hook), and a notification toast/inbox UI in `src/AskLucy.Web/ClientApp/src/features/documents/components/ProcessingDashboard.tsx`, `OrganizationDashboard.tsx`, `NotificationInbox.tsx`. Added `hooks/useNotificationHub.ts` — a workspace-level (not per-document) SignalR listener for `notificationCreated`, distinct from `useDocumentProcessingHub` which only connects while a specific document's detail panel is open.
- [X] T128 [US6] Wire dashboards and notifications into the workspace and navigation in `src/AskLucy.Web/ClientApp/src/features/documents/pages/DocumentWorkspacePage.tsx`, `src/routes/router.tsx` (depends on T127). No `router.tsx` change was needed — the dashboard/notifications live on the existing `/documents` route rather than a new one.

**Checkpoint**: Monitoring and notifications work end-to-end.

---

## Phase 9: User Story 7 - Preview documents without downloading (Priority: P4)

**Goal**: Render an inline preview for PDF, Office documents, images, and Markdown without
downloading; clearly offer download instead when no preview exists.

**Independent Test**: Preview a completed PDF/DOCX/PNG/Markdown document inline; a document type
without preview support offers download instead of erroring (quickstart.md Scenario 7).

### Tests for User Story 7

- [X] T129 [P] [US7] Integration test: `GetDocumentPreview` returns the right `previewType` per file type and `Unavailable` for unsupported types, never an error (FR-044) in `tests/AskLucy.Application.Tests/Documents/DocumentPreviewTests.cs`
- [X] T130 [P] [US7] Playwright E2E: preview PDF/DOCX/PNG/Markdown inline; an unsupported type offers download instead (quickstart.md Scenario 7) in `tests/AskLucy.E2E.Tests/DocumentPreview.spec.ts`. Written but NOT executed — no running environment available here (same constraint as every other E2E spec in this project).

### Implementation for User Story 7

- [X] T131 [US7] `GetDocumentPreview` query — reads the `DocumentPreview` artifact already generated by `PreviewGenerationStageHandler` (US2, T072); returns `Unavailable` rather than erroring when none exists (FR-043, FR-044) in `src/AskLucy.Application/Documents/Queries/GetDocumentPreview/` (depends on T015, T070). Correction: Markdown needs no `DocumentPreview` row at all (research.md Decision 6) — the handler special-cases it, returning `StructuredContent` built directly from `DocumentVersion.ExtractedText`. For Office documents' `StructuredContent` preview row, returns `DocumentVersion.ExtractedStructureJson` (the row itself carries no `StoredFileName`, by design). For PDF/image `PageImage`/`Thumbnail` rows, returns only the first page's `PreviewId` — sufficient for "a rendered preview displays inline" (spec.md US7 AC1) without a full multi-page viewer beyond what's specified.
- [X] T132 [US7] Add the preview endpoint to `DocumentsController` (contracts/documents-api.md) (depends on T131). Added a second `[AllowAnonymous]` `previews/{previewId}/download-content` endpoint (mirrors the existing document-download signed-URL pattern) to actually stream the rendered page-image/thumbnail bytes.

  **CRITICAL correction, discovered while implementing T131/T132 (cross-cutting, not scoped to US7):** found via empirical verification that `AddControllers()` alone leaves every enum in every DTO across every module serializing as its raw numeric ordinal (`{"status":3}`), not a string — while every frontend TypeScript type/comparison across the whole Documents feature (and likely others) assumes a string (`status.processingStatus === 'Completed'`). Confirmed this was silently broken since it was written; nothing in the test suite exercises real JSON output for an enum field. Fixed globally with `AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))` in `Program.cs`, confirmed safe (nothing depends on the old numeric behavior), and locked in with `tests/AskLucy.Web.Tests/JsonEnumSerializationTests.cs`.
- [X] T133 [P] [US7] Frontend: `DocumentPreviewPane.tsx` (PDF page image, Office structured-content read-only view, image, Markdown render via the already-installed `react-markdown`) in `src/AskLucy.Web/ClientApp/src/features/documents/components/DocumentPreviewPane.tsx`
- [X] T134 [US7] Wire the preview pane into `DocumentDetailPanel.tsx` (depends on T133, T078).

  **Second correction discovered here (also cross-cutting, pre-existing since US1's `downloadDocument`):** the backend mints signed URLs via `Url.Action`, which already returns a full app-rooted path *including* the `api/v1` segment; `API_BASE_URL` (`httpClient.ts`) also ends in `/api/v1`, so naively concatenating the two (as `downloadDocument` already did) doubles that segment into a broken URL — verified empirically via a real `Url.Action` call. Added a shared `resolveSignedUrl` helper (`documentsApi.ts`) that strips the trailing `/api/v1` first, mirroring the pattern `useDocumentProcessingHub` already used correctly for its hub URL; fixed both `downloadDocument` and the new preview `<img>` source to use it.

**Checkpoint**: All 7 user stories are independently functional.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories (constitution §16 Quality Gates).

- [x] T135 [P] Accessibility pass — automated axe checks plus keyboard-only verification across the upload panel, document list, metadata panel, folder tree, and dashboards (constitution §7, FR-052)
  - Added `jest-axe` component tests (matching the existing `*.a11y.test.tsx` convention from the Knowledge Base feature) for `UploadPanel`, `DocumentCard`, `MetadataPanel`, `DocumentFolderTree`, and `ProcessingDashboard` (covers `OrganizationDashboard` too — same shared `DashboardBody`).
  - **Found and fixed via keyboard-only verification** (axe alone can't catch this — it's a keyboard-operability gap, not a static ARIA violation): `DocumentCard`'s clickable filename (opens the detail panel) was a plain `<h6 onClick>` with no keyboard support. Made it a real `role="button"`/`tabIndex=0`/`onKeyDown` (Enter/Space) target, matching the pattern this same module already uses in `UploadPanel`'s dropzone; covered by a new test that operates it via `fireEvent.keyDown` only, no mouse.
  - **Found and fixed via automated axe**: `role="button"` on the `<h6>` itself was `aria-allowed-role` invalid — switched the element to `component="span"` (`display:block` preserved via `sx`).
  - **Found and fixed via automated axe**: `DocumentFolderTree`'s per-folder delete `IconButton` was nested inside the selectable `ListItemButton` (`nested-interactive` — a real `<button>` inside a `div[role=button]`), and `ListItemButton` was a direct, non-`<li>` child of the `<ul>` (`list` rule). Restructured to MUI's `ListItem secondaryAction={...}` pattern so the delete action is a sibling, not a descendant, of the selectable row.
  - **Found and fixed via automated axe**: the classification `<TextField select>` in `MetadataPanel` had no accessible name (`aria-input-field-name`) — added `label="Category"`.
  - **Found and fixed via automated axe**: unlabeled `CircularProgress` loading spinners (`aria-progressbar-name`) in `ProcessingDashboard`, `OrganizationDashboard`, `DocumentPreviewPane`, and `VersionCompareDialog` — added descriptive `aria-label`s to all four.
  - All 9 new tests pass (`npx vitest run` scoped to the Documents feature); a full-suite run showed 5 unrelated pre-existing timeout flakes in the Knowledge Base and Chat features (confirmed pre-existing and unrelated — both pass cleanly when run in isolation), not caused by this work.
- [x] T136 [P] Security review — confirm download/preview endpoints never leak a physical path, cross-user access returns `404` everywhere, and upload content validation covers every new file type (constitution §8)
  - Verified: `DocumentFileValidator` sniffs magic bytes for all 16 `DocumentFileType` values (never trusts extension/MIME alone).
  - Verified: no DTO in `Documents` exposes `StoredFileName`/physical paths; all downloads/previews go through `ISignedUrlService`.
  - Verified: `SignedUrlService` binds the signature to the specific resource id (id is the Data-Protection-encrypted payload itself, not a sibling field) — a signature minted for one id cannot validate against another; expiry enforced internally by the time-limited protector.
  - Verified: 35/37 Documents handlers scope by owner; the 2 exceptions (`GetOrganizationDashboardSummaryQueryHandler`, `ListDocumentCategoriesQueryHandler`) are deliberate (admin-gated org dashboard; shared system-wide taxonomy).
  - Verified: all three Documents controllers carry `[EnableRateLimiting]`; both `[AllowAnonymous]` streaming actions validate the signature before returning bytes.
  - Verified: `DocumentPreviewPane`'s Markdown rendering uses `ReactMarkdown` with only `remarkGfm` — no `rehype-raw`/`dangerouslySetInnerHTML` — so raw HTML in an uploaded `.md` file is escaped, not executed.
  - **Found and fixed during this review**: `UploadChunkCommandHandler` had no ceiling on accumulated chunked-upload storage before `CompleteUpload`, allowing unbounded temp-storage growth from an authenticated client that never completes the upload. Added a chunk-index ceiling derived from the session's own validated `DeclaredSizeBytes`; covered by a new passing test in `ChunkedUploadTests.cs`.
- [x] T137 [P] Performance spot-check — seed a representative large document set and confirm list/search response time doesn't materially degrade versus a small dataset (SC-004 spot-check)
  - **Honest limitation**: no live SQL Server test instance is reachable in this sandbox (same
    constraint as the Persistence test suite — see `docs/TESTING.md` §13); a real large-dataset
    timing benchmark could not be run. This is a static, structural code-level review instead.
  - Verified indexes cover every `Any()`-subquery join column `SearchAsync` uses
    (`DocumentVersions.DocumentId`, `DocumentMetadata.DocumentId`, `DocumentLanguages.DocumentId`,
    `DocumentClassifications.DocumentId`) plus `Documents.OwnerId`/`FolderId`/`ProcessingStatus`/
    `ArchivedAtUtc` — no missing-index red flags for the filter path.
  - **Found and fixed**: `GetDashboardCountsAsync` (`DocumentProcessingJobRepository`, backing an
    endpoint polled every 5s per session) called `CountAsync` four times against the same
    "latest job per document" `GroupBy`/window-aggregate subquery — a structural 4x query
    multiplication provable from the code alone, independent of data volume. Consolidated to one
    materialization of the current-jobs set, with all four counts computed from it. Verified via
    `dotnet test` on the full non-persistence backend suite (590 tests, all passing) — the
    Persistence-layer test that exercises this exact method (`RetryQueueTests`) could not be run
    here for the same real-SQL-Server-instance reason above, so this change is verified by
    build/behavioral-equivalence reasoning, not a live DB run; flagging for a real test-DB run in
    CI before merge.
  - **Observed, not changed** (both pre-existing, matching an already-accepted pattern elsewhere
    in the codebase — not a regression introduced by this feature): (1) `SearchAsync`'s free-text
    filter uses `string.Contains()` (leading-wildcard `LIKE '%...%'`), which cannot use an index —
    most exposed on `DocumentVersions.ExtractedText`, which can be large OCR output. A real fix
    needs SQL Server Full-Text Search or a dedicated search engine, which is explicitly future
    RAG-infrastructure scope (constitution's RAG Engine section), not a quick fix here. (2) the
    `Documents` table has single-column indexes on `OwnerId` and no composite `(OwnerId,
    CreatedAtUtc, Id)` covering the cursor-pagination sort — identical to `UserChats` and
    `KnowledgeBases`, which ship with only a single `CreatedAtUtc` index each. Consistent existing
    trade-off, not something to change unilaterally for one feature only.
  - `dotnet build`: zero warnings scoped to the touched file.
- [x] T138 Run quickstart.md validation end-to-end (all 7 scenarios plus the cross-cutting checks)
  - **NOT RUNNABLE IN THIS ENVIRONMENT** — same documented constraint as every Playwright E2E
    suite in this project (T046/T066/etc.): no running frontend/backend + authenticated session
    available in this sandbox. All 7 scenarios already have corresponding Playwright specs
    written to the same selector/assertion conventions as the rest of the suite
    (`DocumentUploadLifecycle.spec.ts`, `DocumentProcessing.spec.ts`, `DocumentMetadata.spec.ts`,
    `DocumentOrganization.spec.ts`, `DocumentVersioning.spec.ts`, `DocumentDashboard.spec.ts`,
    `DocumentPreview.spec.ts`), so quickstart validation reduces to running that suite once a real
    environment is wired into CI — not something to re-derive here.
  - What *was* verified in this sandbox as a substitute: `dotnet build` (whole solution, 0
    errors), the full non-Persistence backend test suite (590 tests passing), the frontend
    `tsc --noEmit`/`eslint`/`vitest run` suites (all passing, see T135/T140 above), and a manual
    read-through of quickstart.md's 7 scenarios against the actual implemented handlers/components
    to confirm no scenario references a route, DTO field, or UI affordance that doesn't exist in
    the shipped code.
- [x] T139 [P] Documentation — update `docs/ARCHITECTURE.md`/API docs for the new `Documents` module (constitution §13)
  - `docs/ARCHITECTURE.md`: added §27 "Document Intelligence Pipeline" (narrative style matching §26 Consent & Privacy Engine — the most recently added section — rather than the thinner placeholder style of the older §14 Knowledge Base Engine), covering upload, the processing pipeline, signed-URL delivery/preview, and organization/dashboard. Old §27 renumbered to §28.
  - `docs/API_GUIDELINES.md`: added §24 "Document Intelligence Endpoints" with the full route list (upload/chunked-upload, processing, metadata/classification/tags, folders, versions, dashboard/notifications), including a note distinguishing these from Knowledge Base's `/knowledge-bases/{id}/documents`. Sections 24–40 renumbered to 25–41.
  - `docs/DATABASE.md`: added §8 "Document Intelligence Context" documenting every entity (`Documents`, `DocumentVersions`, `DocumentChecksums`, `DocumentFolders`, `DocumentMetadata`/`DocumentLanguage`/`DocumentClassification`/`DocumentTag`, `DocumentProcessingJob`/`Stage`/`Log`, `DocumentPreview`, `DocumentNotification`, `DocumentStatistics`, `DocumentAuditLog`, `DocumentCategory`) and its deliberate separation from the Knowledge Base bounded context. Sections 8–20 renumbered to 9–21.
- [x] T140 Code cleanup and dead-code check across the new `Documents` module
  - `dotnet build` (whole solution): zero warnings scoped to the Documents module; the only warnings anywhere are pre-existing, project-wide `NU1903` NuGet advisories (`Microsoft.OpenApi`, `Newtonsoft.Json` — both already tracked, see [[openapi_version_ceiling]]) and pervasive `xUnit1051`/`CA1310` analyzer nits that exist identically across every other feature's test files, not something introduced here.
  - `dotnet format --verify-no-changes --severity info`: no unused-usings (`IDE0005`) or other cleanup findings scoped to any Documents file.
  - Frontend: `eslint` and `tsc --noEmit` scoped to `features/documents` both pass with zero findings.
  - Verified every `Application/Abstractions` interface used by Documents (`IDocumentRepository`, `IDocumentFolderRepository`, `IDocumentUploadSessionRepository`, `IDocumentProcessingJobRepository`, `IDocumentStatisticsRepository`, `IDocumentNotificationRepository`, `IDocumentTextExtractor`, `IDocumentPreviewGenerator`, `IOcrEngine`, `IProcessingNotifier`, `IResumableUploadStorage`, etc.) has real implementations and multiple consumers — none orphaned.
  - Verified every component/hook file under `features/documents` (components + hooks) is imported by at least one other file in the feature — no dead frontend files.
  - No `TODO`/`FIXME`/`XXX` markers and no stray backup/duplicate files found under the Documents backend or frontend trees.
- [x] T141 [P] Ops runbook note — the Tesseract native OCR component and trained-data language packs must be present on the deployment host/container image (plan.md Target Platform)
  - Added a "Deployment prerequisites" section to `README.md`. Verified (not assumed) the actual gap: `App_Data/` is gitignored (`.gitignore:383`) and `App_Data/tessdata/eng.traineddata` exists only in this local dev sandbox — a fresh clone, CI build, or deploy never populates it, since the NuGet `Tesseract` package ships native engine binaries but no `.traineddata` files. Documented that this is non-fatal-but-silent (falls back to whichever language pack is present, defaulting to `eng`) rather than a hard failure, so it's the kind of gap that goes unnoticed until OCR quietly returns no text.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Stories (Phase 3–9)**: All depend on Foundational completion.
  - US1 and US2 (both P1) form the MVP together — US2's pipeline has nothing to process without
    US1's upload, and US1's upload is of limited value without US2 actually processing anything;
    implement sequentially (US1 then US2) even though they're separately testable once both exist.
  - US3, US4 (P2) depend on US1/US2 existing (a document must exist and be processed to review/
    organize) but are otherwise independent of each other.
  - US5 (P3, versioning) depends on US1 (a document to replace) and US2 (a new version needs
    processing).
  - US6 (P3, dashboard/notifications) depends on US2 (jobs to report on) and touches US1's upload
    handler for the storage-quota notification (T121/T124).
  - US7 (P4, preview) depends on US2's `PreviewGenerationStageHandler` (T070/T072) already
    producing the artifact it reads.
- **Polish (Phase 10)**: Depends on all desired user stories being complete.

### Within Each User Story

- Tests are written first and MUST fail before implementation.
- Domain/entities before commands/queries; commands/queries before controllers; controllers
  before frontend wiring.
- Story complete (checkpoint) before moving to the next priority.

### Parallel Opportunities

- All Setup tasks marked `[P]` (T001–T005) can run in parallel.
- All Foundational entity tasks marked `[P]` (T008–T023) can run in parallel; abstraction tasks
  (T024–T028, T030) can run in parallel with each other and with the entities.
- Once Foundational completes, US3 and US4 can be staffed in parallel (both depend only on
  US1+US2, not on each other); US6 and US7 likewise (both depend only on US2).
- Within any story, tasks marked `[P]` (different files, no incomplete dependency) run in
  parallel; sequential tasks in the same story are ordered by their `depends on` notes.

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Unit tests for Document domain methods in tests/AskLucy.Domain.Tests/Documents/DocumentTests.cs"
Task: "Integration tests: chunked upload happy path + resume in tests/AskLucy.Application.Tests/Documents/ChunkedUploadTests.cs"
Task: "Integration test: duplicate checksum detection + resolution in tests/AskLucy.Application.Tests/Documents/DuplicateDetectionTests.cs"
Task: "Integration test: upload validation rejections in tests/AskLucy.Application.Tests/Documents/UploadValidationTests.cs"
Task: "Integration tests: rename/archive/restore/delete + ownership in tests/AskLucy.Application.Tests/Documents/DocumentLifecycleCommandTests.cs"
Task: "Integration test: cancel upload cleans up chunks in tests/AskLucy.Application.Tests/Documents/CancelUploadTests.cs"

# Launch independent frontend pieces for User Story 1 together:
Task: "UploadPanel.tsx + useResumableUpload.ts"
Task: "documentsApi.ts + useDocuments.ts/useDocumentMutations.ts"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
3. Complete Phase 3: User Story 1.
4. Complete Phase 4: User Story 2.
5. **STOP and VALIDATE**: run quickstart.md Scenarios 1 and 2 independently.
6. Deploy/demo if ready — this is the first point where the feature delivers its core promise
   ("a document is more than a file").

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 + US2 → MVP: upload becomes a fully processed intelligent document → deploy/demo.
3. US3 → review/correct extracted content → deploy/demo.
4. US4 → organize/find documents → deploy/demo.
5. US5 → versioning → deploy/demo.
6. US6 → dashboard/notifications → deploy/demo.
7. US7 → preview → deploy/demo.
8. Polish.

### Parallel Team Strategy

With multiple developers, after Setup + Foundational:
- Developer A: US1 → US5 (versioning builds directly on US1's upload path).
- Developer B: US2 (the processing pipeline — the largest single story) → US7 (reads US2's
  preview artifact).
- Developer C: US3, then US4 once US1/US2 land.
- Developer D: US6 (dashboard/notifications), picking up US2's job/stage data as it becomes
  available.

---

## Notes

- `[P]` tasks = different files, no dependency on an incomplete task.
- `[Story]` label maps every user-story-phase task to US1–US7 for traceability.
- Each user story is independently completable and testable per its quickstart.md scenario.
- Verify tests fail before implementing.
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently.
- Avoid: vague tasks, same-file conflicts within a `[P]` group, cross-story dependencies that
  break independence beyond what's explicitly noted above.
