# Implementation Plan: Document Intelligence Pipeline

**Branch**: `015-document-intelligence-pipeline` | **Date**: 2026-08-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/015-document-intelligence-pipeline/spec.md`

## Summary

Introduce a Document Intelligence Engine: a new, independent `Documents` bounded context (not an
extension of the existing `KnowledgeBases` module — research.md Decision 1) that turns an uploaded
file into a durably-processed intelligent document — validated, stored, OCR'd where needed,
text/structure-extracted, auto-classified, language-detected, versioned, previewed, and tracked
through a fully durable, resumable background pipeline. Architecturally this reuses every existing
platform capability it can (`IFileStorage`/`ISignedUrlService` for storage and downloads, the
multi-provider `IAIProvider` abstraction for classification/language detection, the existing
admin-role authorization convention for the org-wide dashboard, the `BaseEntity`/`RowVersion`/soft-
delete conventions for every entity) and adds exactly four genuinely new pieces of infrastructure:
a durable job engine (Hangfire on SQL Server, for FR-030a's crash-resume requirement), a self-hosted
OCR engine (Tesseract, mirroring the existing self-hosted Whisper.net STT precedent), real structured
text-extraction libraries (`DocumentFormat.OpenXml`, `Docnet.Core` — superseding the prior BCL-only,
page-count-only approach for this materially larger requirement), and the platform's first concrete
SignalR usage (already named in the constitution's tech stack, never previously implemented) for
near-real-time processing status.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend, existing solution); TypeScript 5.x / React 19
(frontend, existing `ClientApp`). No new language.

**Primary Dependencies**: New backend packages, each tied to a specific FR (research.md has the
full rationale/alternatives per decision): `Hangfire.Core`/`Hangfire.SqlServer`/`Hangfire.AspNetCore`
(durable job engine, FR-030a, Decision 2); a Tesseract 5 .NET binding (OCR, FR-021, Decision 3);
`DocumentFormat.OpenXml` (DOCX/XLSX/PPTX structured extraction, FR-022, Decision 5); `Docnet.Core`
(PDF structured extraction AND page rasterization for previews — also FR-043, Decision 6 —
originally split across `Docnet.Core`/`PDFtoImage` until both libraries' bundled `pdfium.dll`
native binaries were found to collide; see Decision 6's correction note); `SixLabors.ImageSharp`
(image thumbnailing, FR-043, Decision 6);
`Microsoft.AspNetCore.SignalR` (server push, FR-027, Decision 7 — client side already ships
`@microsoft/signalr` or an equivalent small client package). Classification and language detection
add **no** new dependency — both reuse the existing `IAIProvider`/`IAIProviderResolver` abstraction
(Decision 4). Frontend: existing MUI, TanStack Query, Zustand, React Hook Form + Zod, plus a small
drag-and-drop library consistent with the one introduced for `KnowledgeBases` (specs/014 research.md
Decision 6) — reused, not reinvented, for the upload panel.

**Storage**: SQL Server via EF Core (existing `AskLucy.Persistence`) — fourteen new tables
(`Document`, `DocumentVersion`, `DocumentFolder`, `DocumentMetadata`, `DocumentLanguage`,
`DocumentCategory`, `DocumentClassification`, `DocumentPreview`, `DocumentProcessingJob`,
`DocumentProcessingStage`, `DocumentProcessingLog`, `DocumentTag` (+ join table), `DocumentAuditLog`,
`DocumentChecksum`) plus one denormalized `DocumentStatistics` table, plus Hangfire's own SQL Server
storage schema (same database — no new datastore introduced, per constitution §5's preference).
Document bytes go through the existing `IFileStorage`/`LocalFileStorage` server-filesystem
implementation exactly as knowledge-base documents, avatars, and chat attachments do today.

**Testing**: xUnit (backend) for Domain/Application unit tests (pipeline stage handlers tested with
faked `IOcrEngine`/`IDocumentTextExtractor`/`IAIProvider`, no real Tesseract/OpenXml/SQL Server
dependency in unit tests) and Infrastructure integration tests (real SQL Server test instance,
real sample files run through the real extractors/OCR engine, real Hangfire crash-resume scenario
per constitution §10); Vitest + React Testing Library + MSW + jest-axe (frontend) for the upload
panel, processing status view, dashboard, and version timeline; Playwright E2E
(`tests/AskLucy.E2E.Tests`) for the full upload→processing→review journey, mirroring the existing
`KnowledgeBase*.spec.ts` suite's shape.

**Target Platform**: ASP.NET Core 10 on the existing Windows/IIS (ANCM) deployment; React SPA
static build served the same way. Tesseract's native OCR engine component must be present on the
deployment host/container image (a new deployment-time dependency, called out for the ops runbook —
not a new hosting *capability*, since the existing deployment already runs native components like
Whisper.net's runtime).

**Project Type**: Web application — extends the existing layered .NET backend + React SPA. No new
top-level project.

**Performance Goals**: Directly from spec.md Success Criteria — document visible in list within 5s
of upload completion (SC-001); 95% of standard document types fully processed within 2 minutes
(SC-002); document search/filter results in <10s for 90% of attempts (SC-003); 1,000,000 documents/
organization without measurable list/search degradation (SC-004); resumed large-file uploads
(>100MB) continue without re-transferring data in ≥95% of retries (SC-005); version restore <30s
(SC-007); OCR ≥90% text-recognition accuracy on clean scans (SC-008); processing-history visibility
within 5s of a transition (SC-009); dashboard job counts accurate within 5s (SC-011).

**Constraints**: All list/search endpoints are cursor-paginated (constitution §6), matching the
`KnowledgeBases`/`Chats` shape. Every processing stage must run asynchronously and never block the
workspace UI (FR-030). Processing job/stage state must be durable enough to resume after a crash/
restart without duplicating completed work (FR-030a — the reason Hangfire, not a bespoke
`BackgroundService`, was chosen, research.md Decision 2). Concurrent metadata edits use
`RowVersion`-detected staleness with a merge-and-warn resolution, not a hard reject (FR-031a,
research.md Decision 9). Files are validated by content (magic-byte/structural sniffing extended to
the new file types), never by extension alone (FR-010, constitution §8). Downloads are signed,
time-limited URLs only, never physical paths (FR-015/FR-050).

**Scale/Scope**: All authenticated users at launch, with an additional organization-wide dashboard
view gated by the existing administrator role (FR-045a) — no separate tier gating, nothing in the
spec requires it. Scale target is SC-004's 1,000,000 documents per organization.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see "Post-Design
Re-check" below.*

| Principle / Gate | Status | Notes |
|---|---|---|
| §3 Clean Architecture & Dependency Rule | PASS | All 14 entities live in `Domain/Documents`; `IOcrEngine`, `IDocumentTextExtractor`, `IDocumentPreviewGenerator`, `IDocumentLanguageAndClassifier` (or equivalent) are Application-owned interfaces; Tesseract/OpenXml/Docnet.Core/ImageSharp/Hangfire specifics live only in `Infrastructure/Documents`. No Domain/Application code references these libraries or SignalR directly — the hub lives in `Web`, invoked from Application via a small `IProcessingNotifier` port. |
| §2.III Simplicity — DRY/KISS/YAGNI, avoid unnecessary dependencies | PASS (five new backend dependencies, each justified) | Hangfire, the Tesseract binding, `DocumentFormat.OpenXml`, `Docnet.Core`, and `SixLabors.ImageSharp` are all new — but each is tied to an explicit, approved FR (FR-030a, FR-021, FR-022 ×2, FR-043 ×2) that a BCL-only/no-dependency approach cannot satisfy (research.md Decisions 2/3/5/6 each record the rejected simpler alternative and why it falls short). `Docnet.Core` alone now covers both PDF text extraction and PDF page rasterization (Decision 6's correction note) — one less native dependency than originally planned. Classification and language detection (FR-024–FR-026) add **zero** new dependencies by reusing the existing `IAIProvider` abstraction (Decision 4) — the more expensive path (a dedicated NLP library) was explicitly rejected in favor of reuse. |
| §5 Database — entity design, soft delete, auditing | PASS | Every new aggregate extends `BaseEntity` (surrogate `Guid` v7, audit columns via the existing `AuditSaveChangesInterceptor`, `RowVersion`). `Document`/`DocumentFolder` use soft delete via `HasQueryFilter`, matching `KnowledgeBase`/`UserChat`. `DocumentProcessingLog`/`DocumentAuditLog` are deliberately append-only, no soft delete — same documented exception as `KnowledgeBaseAuditLog`/`ProviderHealthCheck`. |
| §5 Concurrency | PASS | `RowVersion` drives metadata-edit staleness detection (FR-031a); `DbUpdateConcurrencyException` is caught explicitly and resolved as merge-and-warn (`WasStale: true`), never left to bubble as a generic 500 — research.md Decision 9 records why this is a *resolution*, not a bypass, of the constitution's concurrency-handling requirement. |
| §3 CQRS/MediatR/Repository/FluentValidation | PASS | Every mutation is an `IRequest`/handler pair validated by the existing `ValidationBehavior` pipeline, mirroring `KnowledgeBases`/`Chats`. Repositories expose aggregate-oriented methods (e.g., `IDocumentRepository.GetOwnedByAsync`), not a leaky `IQueryable` escape hatch. |
| §6 REST conventions, pagination, Problem Details | PASS | `/api/v1/documents` (list/create via upload sessions), `{id}` (get/rename/delete), `{id}/actions/{verb}` for archive/restore/retry/duplicate, `{id}/versions/...` and `folders/...` sub-resources — matches the `KnowledgeBases` contract shape exactly (contracts/*.md). |
| §6 Streaming | N/A / PASS | Classification and language detection call `IAIProvider.ChatAsync` **non-streaming** — constitution §9 explicitly names "background batch classification" as the justified exception to the streaming-by-default rule; this is precisely that case. |
| §6 AuthN/AuthZ | PASS | `[Authorize]` by default; ownership enforced via a new `DocumentOwnershipGuard` (mirrors `KnowledgeBaseOwnershipGuard`); the organization-wide dashboard additionally requires the existing administrator role (research.md Decision 11) and never exposes individual document content through that path (FR-045a). |
| §6 Rate limiting | PASS | New `document-endpoints` policy, generous shape like `knowledge-base-endpoints` for CRUD/browse calls; a separate, tighter policy is applied to the upload-chunk endpoint given its higher per-call cost. |
| §8 Security — file validation, signed downloads, audit logging | PASS | `IDocumentContentValidator`'s existing magic-byte approach (specs/014) is extended to the new file types (RTF/HTML/JSON/XML/PNG/JPEG/TIFF/BMP/WEBP); downloads only via `ISignedUrlService`-issued URLs (FR-015/FR-050); `DocumentAuditLog` is explicitly distinct from `DocumentProcessingLog` (FR-051). |
| §9 AI Principles | PASS | Classification/language detection go through the existing provider-neutral `IAIProvider`/`IAIProviderResolver` abstraction with a versioned system prompt (constitution §9 "Prompt engineering") and inherit existing token/cost tracking (`CostEstimator`) automatically — no new, parallel AI integration is introduced. |
| §10 Testing | PASS (planned in tasks) | Domain/Application unit-tested with faked `IOcrEngine`/`IDocumentTextExtractor`/`IAIProvider`/`IFileStorage`; Infrastructure integration-tested against real sample files and a real Hangfire crash-resume scenario; new frontend hooks/components covered by Vitest+RTL+jest-axe; Playwright E2E covers the end-to-end upload→processing→review journey. |
| §14 Observability | PASS | `DocumentProcessingLog` (processing/lifecycle trail) and `DocumentAuditLog` (security trail) are kept distinct per FR-051; Serilog structured logging in every job handler; Hangfire's own dashboard is restricted to operators only, never exposed as a user-facing surface. |
| §15 Performance | PASS | All processing is asynchronous/background (FR-030); `DocumentStatistics` is a periodically-recomputed denormalized aggregate rather than a synchronous per-write counter, given the 1M-document scale target (SC-004) — see data-model.md's "Explicitly not modeled as real-time-updated counters." |
| §7 UI — accessibility, responsive, theming | PASS | FR-052 restates the constitution's WCAG 2.1 AA floor at the stricter 2.2 AA level this feature's original request asked for — same knowingly-stricter-than-baseline pattern already established for `KnowledgeBases` (specs/014). |

No Complexity Tracking entries — every gate above is a clean PASS; the new dependencies flagged
under §2.III are additions justified by explicit, approved functional requirements, not
unjustified complexity or a deviation from an existing architectural rule.

## Project Structure

### Documentation (this feature)

```text
specs/015-document-intelligence-pipeline/
├── spec.md                                  # Feature specification
├── plan.md                                  # This file
├── research.md                              # Phase 0 output
├── data-model.md                            # Phase 1 output
├── quickstart.md                            # Phase 1 output
├── contracts/                               # Phase 1 output
│   ├── documents-api.md                    # Upload, lifecycle, metadata, tags, organization, preview
│   ├── document-versions-folders-api.md    # Versioning + folders
│   └── document-processing-api.md          # Status, retry, SignalR hub, dashboards, notifications
├── checklists/
│   └── requirements.md
└── tasks.md                                 # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

Existing layered layout (`Domain` → `Application` → `Infrastructure`/`Persistence` → `Web`) plus
the React SPA under `Web/ClientApp` — extended, not restructured. New backend code follows the
`KnowledgeBases` module's shape at every layer; new frontend code mirrors
`features/knowledge-base/`.

```text
src/AskLucy.Domain/Documents/
├── Document.cs                              # NEW — aggregate root
├── DocumentVersion.cs                       # NEW
├── DocumentFolder.cs                        # NEW
├── DocumentMetadata.cs                      # NEW
├── DocumentLanguage.cs                      # NEW
├── DocumentCategory.cs                      # NEW
├── DocumentClassification.cs                # NEW
├── DocumentPreview.cs                       # NEW
├── DocumentProcessingJob.cs                 # NEW
├── DocumentProcessingStage.cs               # NEW
├── DocumentProcessingLog.cs                 # NEW — append-only
├── DocumentTag.cs                           # NEW
├── DocumentAuditLog.cs                      # NEW — append-only, distinct from ProcessingLog
├── DocumentChecksum.cs                      # NEW
└── DocumentStatistics.cs                    # NEW

src/AskLucy.Application/
├── Abstractions/
│   ├── IOcrEngine.cs                        # NEW — research.md Decision 3
│   ├── IDocumentTextExtractor.cs            # NEW — research.md Decision 5
│   ├── IDocumentPreviewGenerator.cs         # NEW — research.md Decision 6
│   ├── IDocumentLanguageAndClassifier.cs    # NEW — wraps IAIProvider per research.md Decision 4
│   ├── IProcessingNotifier.cs               # NEW — port for the SignalR hub (research.md Decision 7)
│   └── IDocumentContentValidator.cs         # EXTENDED — new file types
└── Documents/
    ├── Authorization/
    │   └── DocumentOwnershipGuard.cs        # NEW — mirrors KnowledgeBaseOwnershipGuard.cs
    ├── Commands/
    │   ├── StartUpload/ UploadChunk/ CompleteUpload/ CancelUpload/   # NEW — chunked upload (Decision 6)
    │   ├── ReplaceDocument/                 # NEW — new version (FR-038/FR-039)
    │   ├── RestoreDocumentVersion/          # NEW
    │   ├── RenameDocument/ ArchiveDocument/ RestoreDocument/ DeleteDocument/ DuplicateDocument/  # NEW
    │   ├── MoveDocument/ CreateFolder/ RenameFolder/ MoveFolder/ DeleteFolder/  # NEW
    │   ├── UpdateDocumentMetadata/          # NEW — RowVersion staleness handling (Decision 9)
    │   ├── OverrideClassification/ AddTag/ RemoveTag/  # NEW
    │   └── RetryProcessing/                 # NEW — rejects if not currently Failed
    ├── Queries/
    │   ├── SearchDocuments/ GetDocument/ GetDocumentProcessingStatus/ GetProcessingHistory/  # NEW
    │   ├── GetDocumentDashboardSummary/ GetOrganizationDashboardSummary/  # NEW (latter admin-only)
    │   ├── GetVersionTimeline/ CompareVersions/  # NEW
    │   └── GetFolderTree/ ListTags/          # NEW
    └── Processing/
        ├── DocumentProcessingPipeline.cs     # NEW — Hangfire job chain orchestrator (research.md Decision 2/10)
        └── Stages/                           # NEW — one handler per DocumentProcessingStage.StageType

src/AskLucy.Infrastructure/
├── Documents/
│   ├── Ocr/TesseractOcrEngine.cs             # NEW
│   ├── Extraction/OpenXmlTextExtractor.cs    # NEW
│   ├── Extraction/DocnetPdfTextExtractor.cs # NEW
│   ├── Preview/PdfPreviewGenerator.cs        # NEW
│   ├── Preview/ImageThumbnailGenerator.cs    # NEW
│   ├── AiDocumentLanguageAndClassifier.cs    # NEW — wraps IAIProvider (Decision 4)
│   ├── ProcessingNotifier.cs                 # NEW — publishes into the SignalR hub context
│   └── DocumentStatisticsRecomputeJob.cs     # NEW — Hangfire recurring job (data-model.md)
└── Files/
    └── DocumentContentValidator.cs           # EXTENDED — new file types

src/AskLucy.Persistence/
├── Configurations/Documents/*.cs             # NEW — one per entity, Fluent API only
├── Repositories/DocumentRepository.cs (+ folder/version/tag-focused methods)  # NEW
├── Seed/DocumentCategorySeed.cs              # NEW — starting taxonomy
└── Migrations/<timestamp>_AddDocumentIntelligencePipeline.cs  # NEW (includes Hangfire's own schema)

src/AskLucy.Web/
├── Controllers/v1/
│   ├── DocumentsController.cs                # NEW
│   ├── DocumentVersionsController.cs         # NEW
│   └── DocumentProcessingController.cs       # NEW
├── Hubs/DocumentProcessingHub.cs             # NEW — research.md Decision 7
└── ClientApp/src/features/documents/
    ├── pages/DocumentWorkspacePage.tsx        # NEW — grid/list toggle, folder nav, search/filters
    ├── components/
    │   ├── UploadPanel.tsx                    # NEW — drag-and-drop, chunked resumable upload
    │   ├── DocumentCard.tsx / DocumentDetailPanel.tsx / MetadataPanel.tsx  # NEW
    │   ├── ProcessingStatusBadge.tsx / ProcessingHistoryPanel.tsx  # NEW
    │   ├── VersionTimeline.tsx / VersionCompareDialog.tsx  # NEW
    │   ├── DocumentPreviewPane.tsx            # NEW
    │   └── ProcessingDashboard.tsx / OrganizationDashboard.tsx (admin-only)  # NEW
    ├── hooks/
    │   ├── useDocuments.ts / useDocumentMutations.ts  # NEW — TanStack Query
    │   ├── useResumableUpload.ts              # NEW
    │   └── useDocumentProcessingHub.ts        # NEW — SignalR client + polling fallback (Decision 7)
    └── store/documentWorkspaceStore.ts        # NEW — Zustand: view mode, active filters (UI-only)
```

**Structure Decision**: Extends the existing layered backend and `src/features/<domain>` frontend
convention with a new, independent `Documents` module at every layer (research.md Decision 1) — no
new top-level project. Modeled directly on the already-shipped `KnowledgeBases` feature's shape,
diverging only where this feature's own requirements (durable background processing, OCR,
versioning, a processing dashboard) genuinely differ.

## Post-Design Re-check

Re-evaluated after Phase 1 (data-model.md, contracts/, quickstart.md): no new violations were
introduced. `DocumentCategory` (a supporting lookup entity, data-model.md) is not named in spec.md's
Key Entities list, but it is required to make FR-025/FR-026's "administrators can extend the
taxonomy without a pipeline redesign" assumption concrete — mirroring `KnowledgeBaseCategory`'s
already-approved precedent for the same need (specs/014). `DocumentStatistics`'s periodic-recompute
design (rather than synchronous per-write counters) was confirmed against SC-004's 1M-document scale
target and SC-011's 5-second accuracy bar — both are satisfied by a short recompute interval, so no
gate changes. All Constitution Check gates above remain PASS.

## Complexity Tracking

*No entries — see the Constitution Check table above; every new dependency is tied to an explicit,
approved functional requirement, and research.md records the simpler alternative rejected in each
case.*
