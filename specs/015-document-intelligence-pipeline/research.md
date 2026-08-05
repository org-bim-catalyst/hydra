# Phase 0 Research: Document Intelligence Pipeline

All Technical Context unknowns are resolved below; no `NEEDS CLARIFICATION` markers remain.
Findings come from reading the existing codebase (`src/AskLucy.*`) and the project constitution,
extending an already-shipped, closely related pattern (`KnowledgeBases`, specs/014-knowledge-base-
management) rather than starting from a blank slate. Where this feature's requirements exceed what
the existing `KnowledgeBases`/`Files` code already provides, new decisions are made explicitly below.

## Decision 1: New `Documents` bounded context, not an extension of `KnowledgeBases`

**Decision**: This feature introduces its own `Document`/`DocumentVersion`/`DocumentFolder`/etc.
aggregate family under a new `Documents` namespace in each layer (`Domain/Documents`,
`Application/Documents`, `Infrastructure/Documents`), independent of the existing
`KnowledgeBases` module and its `KnowledgeBaseDocument`.

**Rationale**: `KnowledgeBaseDocument` (specs/014) is scoped narrowly to "a file living inside a
knowledge base" with a document-type enum (`KnowledgeBaseDocumentType`) limited to the RAG-eligible
formats a knowledge base ingests (PDF, Word, Excel, PowerPoint, Markdown, CSV, Text). This spec's
scope is a general-purpose intelligent-document pipeline supporting a strictly larger format set
(adds RTF, HTML, JSON, XML, and raster images) and a materially different lifecycle (OCR, language
detection, classification, versioning, a processing dashboard) that a knowledge base document does
not have today. Forcing the two into one aggregate would couple two independently-evolving bounded
contexts and violate constitution §2.I (Dependency Rule) / §2.II (SRP) — "a file inside a knowledge
base" and "an intelligent document with its own processing pipeline" are different reasons to
change. A later specification can define how a `Document` gets attached to a `KnowledgeBase` (this
spec's Assumptions explicitly defer RAG/embedding); that link is out of scope here.

**Alternatives considered**:
- *Extend `KnowledgeBaseDocument` with processing/versioning fields* — rejected: would force every
  knowledge-base document to carry OCR/classification/versioning concerns it doesn't need, and
  would force this pipeline's documents to always belong to a knowledge base, contradicting the
  spec's own scope (documents exist and are useful before/without RAG).
- *Rename/repurpose `KnowledgeBaseDocumentType`* — rejected: the enum's five extra formats used
  here (images, RTF, HTML, JSON, XML) are meaningless in a RAG-ingestion context; a new
  `DocumentFileType` enum in `Domain/Documents` keeps each context's type set honest.

## Decision 2: Durable background processing via Hangfire on SQL Server

**Decision**: Introduce Hangfire (`Hangfire.Core`, `Hangfire.SqlServer`, `Hangfire.AspNetCore`) as
the job execution engine for the processing pipeline (FR-020, FR-027, FR-029, FR-030, FR-030a).
Each pipeline stage (Validation → OCR → Text Extraction → Metadata Extraction → Classification →
Language Detection → Preview Generation) is a Hangfire background job chained via
`BackgroundJob.ContinueJobWith`, with the job id stored on `DocumentProcessingJob`. Hangfire's own
SQL Server storage schema lives in the same database, so no new datastore is introduced
(constitution §5 spirit: SQL Server remains the single store).

**Rationale**: Clarification Q1 requires jobs to automatically resume/requeue after a server/worker
restart without duplicating completed work (FR-030a). The codebase's existing background-processing
precedent (`KnowledgeBasePurgeHostedService`, `ProviderHealthCheckHostedService`,
`WhisperWarmupHostedService`) is a simple timer-driven `BackgroundService` with no per-item durable
state — adequate for a periodic sweep, not for a multi-stage job that must survive a crash mid-OCR
and resume exactly where it left off. Reimplementing crash-safe locking, retry/backoff, and
recovery from scratch would be substantial custom infrastructure duplicating a mature, widely used
library — the wrong side of constitution §2.III's simplicity trade-off (avoiding a dependency here
means building and maintaining the hard part of Hangfire ourselves). Hangfire's SQL Server storage
also keeps deployment topology unchanged, consistent with the constitution's general preference for
SQL Server as the platform's one datastore before introducing new infrastructure (§5 RAG note).

**Alternatives considered**:
- *Custom EF-Core-backed job/queue table + polling `BackgroundService`* — rejected: durable
  locking, retry/backoff, and multi-worker safety are exactly the hard, well-solved part of a job
  queue; building it bespoke is the larger, riskier amount of new code, not the simpler one.
- *Cloud-managed queue (Azure Service Bus / Storage Queues)* — rejected: introduces a new external
  managed-service dependency and cost/ops surface no other feature in this codebase currently uses,
  when a self-hosted, SQL-Server-backed option satisfies FR-030a today.

## Decision 3: OCR via a self-hosted Tesseract engine behind `IOcrEngine`

**Decision**: Add `IOcrEngine` to `Application/Abstractions`, implemented in
`Infrastructure/Documents/Ocr/TesseractOcrEngine.cs` using a Tesseract 5 .NET binding (multilingual
trained-data packs bundled per the OCR/language assumption in spec.md). Strategy-pattern swappable
per FR-021/"future providers should be replaceable."

**Rationale**: Mirrors the codebase's existing self-hosted-model precedent for offline AI
capability — `Whisper.net`/`Whisper.net.Runtime` already ship a self-hosted, offline ML model for
speech-to-text rather than calling an external STT API. Tesseract is the equivalent choice for OCR:
self-hosted, no per-page vendor billing to model in the Billing Engine yet, and it ships trained
data for dozens of languages, satisfying "OCR should support multilingual recognition" out of the
box. The abstraction (not the specific engine) is what FR-021's replaceability requirement actually
demands, so a future cloud-OCR `Infrastructure` implementation is a drop-in swap with zero
Application/Domain changes.

**Alternatives considered**:
- *Cloud OCR (Azure AI Vision Read API, AWS Textract)* — rejected for v1: introduces a new external
  vendor credential and a per-page cost the Billing Engine doesn't yet track; revisit via ADR if
  self-hosted OCR's accuracy (SC-008: ≥90% on clean scans) proves insufficient in practice.

## Decision 4: Language detection and classification both reuse the existing AI Provider Engine

**Decision**: Both language detection (FR-024) and document classification (FR-025/FR-026) are
performed by a single call to the existing `IAIProvider`/`IAIProviderResolver` abstraction
(`Application/Ai`, specs/005-multi-provider-ai-engine), using one versioned system prompt
(constitution §9) that returns a structured result: primary language, secondary languages with
confidence, and a category from the taxonomy in spec.md's Assumptions. No new NLP/ML library is
introduced for this purpose.

**Rationale**: The platform already has a first-class, provider-neutral abstraction for "ask a
model to reason about text content" with cost/usage tracking (`CostEstimator`) built in — using it
here is the direct application of constitution §7 (Convention Over Configuration) and §9 (Provider
abstraction), and gets token/cost observability (constitution §9's "every AI call records...cost")
for free instead of building a second, parallel accounting path for a dedicated NLP library. The
2-minute per-document processing budget (SC-002) comfortably accommodates one additional model call
per document as a background job (not a live chat turn).

**Alternatives considered**:
- *Dedicated local statistical language-ID library (e.g., an n-gram classifier)* — rejected: a
  second "intelligence" pathway alongside the AI Provider Engine duplicates a capability the
  platform already has multi-provider access to, for marginal latency benefit in a background job
  with a generous time budget.
- *Cloud Language Detection API (e.g., Azure AI Language)* — rejected: a third external vendor
  credential/billing surface when the existing AI Provider Engine already covers the need.

## Decision 5: Structured text extraction via `DocumentFormat.OpenXml` and `Docnet.Core`

**Decision**: Add `IDocumentTextExtractor` in `Application/Abstractions`, implemented per format in
`Infrastructure/Documents/Extraction/`: `DocumentFormat.OpenXml` (Microsoft's own OOXML SDK) for
DOCX/XLSX/PPTX, and `Docnet.Core` (a PDFium-backed .NET PDF library) for PDF, extracting plain
text, headings, paragraphs, tables, lists, captions, footnotes, hyperlinks, and page numbers
(FR-022).

**Rationale**: specs/014-knowledge-base-management's research.md Decision 5 deliberately avoided a
PDF/Office parsing library dependency — but that decision was scoped to a *page-count-only* need
solvable by a regex scan of well-known XML/PDF structure markers. This spec's FR-022 is a materially
larger, newly-approved requirement (recovering headings, tables, lists, footnotes) that a regex scan
cannot reliably provide; per constitution §17, introducing complexity here is justified because the
requirement is explicit and approved, not speculative (YAGNI does not block an already-specified
capability). `DocumentFormat.OpenXml` is the lowest-risk choice for Office formats (same vendor as
the runtime itself, MIT-licensed, actively maintained).

**Correction during implementation**: this decision originally named `UglyToad.PdfPig`
(`PdfPig`, pure-.NET, no native binary) for PDF extraction. While adding the package during
`/speckit-implement`, its NuGet listing turned out to be untrustworthy — a single published
version (`1.7.0-custom-5`) with a placeholder `"Package Description"`, an `authors` field equal
to the package id rather than a real name, an owner (`grinay`) not matching the project's actual
maintainers, and an empty `repository` field — inconsistent with the genuine, actively-maintained
PdfPig project. It was never installed. `Docnet.Core` (owner `modestas` on NuGet, real
description, 5M+ downloads, consistent version history) is used instead — a PDFium-backed native
binary dependency, the same category of dependency this spec already accepts for Tesseract OCR
(Decision 3), so it does not introduce a new *kind* of operational cost, only another instance of
one already accepted.

**Alternatives considered**:
- *Continue BCL-only regex parsing* — rejected: cannot recover document structure (tables, lists,
  headings), only crude text/counts.
- *Apache Tika (via a JVM sidecar)* — rejected: introduces a JVM runtime dependency into an
  otherwise pure .NET deployment, a disproportionate operational cost for this need.
- *iText7* — rejected: mature and reputable, but AGPL/commercial dual-licensed, a copyleft/cost
  obligation this project has no established precedent for taking on.

## Decision 6: Preview generation — rasterize PDF/images; text-based preview for Office in v1

**Decision**: `IDocumentPreviewGenerator` (`Application/Abstractions`) renders PDF previews/
thumbnails via `Docnet.Core` (the same PDFium wrapper Decision 5/11 uses for text extraction —
originally `PDFtoImage`, corrected during US2 implementation, see below), resizes image uploads
via `SixLabors.ImageSharp` (MIT, no native GDI dependency), and previews Office documents (FR-043)
by rendering the already-extracted structured content (Decision 5) read-only in the workspace
rather than a pixel-perfect rasterized image. Markdown previews render the extracted text directly
client-side; no server-side preview artifact is generated for Markdown.

**Correction (US2 implementation)**: `PDFtoImage` was the original choice here, but it and
`Docnet.Core` (Decision 5/11) each bundle a native binary literally named `pdfium.dll` — from
*different, incompatible* PDFium builds. MSBuild's file copy let one silently overwrite the other
in the output directory, so `Docnet.Core` ended up calling into a PDFium build it was never
compiled against; running both libraries' code paths in the same process (exactly what
`OcrStageHandler` does for every scanned PDF) crashed the process outright. Fixed by rasterizing
via `Docnet.Core`'s own `IPageReader.GetImage()` instead, and removing `PDFtoImage` entirely — one
native PDFium build, no possible collision. See tasks.md T070's correction note for detail.

**Rationale**: FR-043 requires an inline preview, not pixel-perfect fidelity to the original
application's rendering. Achieving true pixel-perfect Office rendering requires a document-
conversion engine (e.g., a headless LibreOffice installation) that is a materially heavier
operational dependency than this spec's bar calls for. Reusing Decision 5's already-extracted
structure for the Office preview avoids a second extraction pathway (DRY) and ships a working
preview now; a future spec can add pixel-perfect conversion via an ADR if users need it.

**Alternatives considered**:
- *Headless LibreOffice conversion for all formats* — rejected for v1: heavier deployment/operational
  dependency than FR-043 requires; the door remains open behind `IDocumentPreviewGenerator`.

## Decision 7: Real-time processing status via SignalR, with polling fallback

**Decision**: Introduce a `DocumentProcessingHub` (SignalR) that pushes stage-transition events
(FR-027, US2 AC1) to the uploading user's connection group. The frontend also polls the processing-
status query via TanStack Query (e.g., every 5s) as a reconciliation fallback, so a missed/dropped
SignalR event never leaves the UI stale beyond SC-009's 5-second budget.

**Rationale**: SignalR is already named in the constitution's Backend technology list but has no
concrete usage yet anywhere in the codebase — this is the first feature to actually need
server-push. Building a bespoke polling-only mechanism instead would introduce a parallel, bespoke
real-time mechanism where the constitution has already committed to one (§7 Convention Over
Configuration), even though no prior feature happened to need it yet. Keeping a TanStack Query poll
as a fallback matches the existing frontend convention (server state lives in TanStack Query) and
protects SC-009 against a dropped WebSocket/SSE connection.

**Alternatives considered**:
- *Polling only, no SignalR* — rejected: leaves the constitution's committed real-time mechanism
  unused and requires a tighter poll interval to hit the same 5-second SC-009 budget, at higher
  server load for no benefit over push.

## Decision 8: Duplicate detection via streaming SHA-256 checksum

**Decision**: Compute a SHA-256 hash of the file content while it is being written via `IFileStorage`
(no separate full read pass), store it as `DocumentChecksum`, and scope duplicate lookups (FR-009) to
the uploading user's own documents (consistent with the spec's single-owner assumption).

**Rationale**: SHA-256 is already the platform's implicit integrity/hashing baseline (Identity's
credential hashing, general "encrypt sensitive data" posture in constitution §8); reusing the same
strength avoids introducing a second, weaker hash algorithm into the codebase for a security-adjacent
purpose (deduplication is not a security control per se, but a hash collision here would silently
misidentify two different files as one).

**Alternatives considered**: *MD5* — rejected: no benefit over SHA-256 at this data volume, and a
weaker algorithm for no reason invites an unnecessary review question later.

## Decision 9: Metadata conflict resolution — `RowVersion`-detected staleness, not a hard reject

**Decision**: Metadata edit commands (FR-031a) carry the `RowVersion` the client last read
(`BaseEntity.RowVersion`, already present on every entity per constitution §5). On
`DbUpdateConcurrencyException`, the handler does **not** reject the write: it reloads the current
row, re-applies the incoming field changes on top of the latest state, retries the save, and returns
`WasStale: true` in the response so the client shows a "your view was out of date" warning —
implementing the clarified "last-write-wins with a stale-data warning" behavior using the row-version
token the constitution already requires for concurrency detection, rather than the token's more
common use (hard 409 reject), which was the explicitly declined alternative.

**Rationale**: Directly implements the clarification answer while still satisfying constitution §5's
requirement that `DbUpdateConcurrencyException` be handled explicitly at the Application layer (it
is handled — just resolved as a merge-and-warn rather than a reject).

**Alternatives considered**: *Reject with 409 Conflict* — this is the "optimistic concurrency
rejection" option explicitly declined during `/speckit-clarify` in favor of last-write-wins.

## Decision 10: Job/stage state is the source of truth for resume, not Hangfire alone

**Decision**: `DocumentProcessingJob` and `DocumentProcessingStage` rows are written to SQL Server
(via the same `IUnitOfWork`/`AskLucyDbContext` as everything else) *before* each stage begins and
immediately on completion/failure. On restart, Hangfire's own crash recovery re-invokes the
interrupted job, but the job handler's first action is to check `DocumentProcessingStage` rows and
skip any stage already marked `Completed`, resuming from the first non-completed stage (FR-030a's
"without duplicating already-completed work").

**Rationale**: Hangfire guarantees a crashed job gets re-attempted, but not that a multi-stage
pipeline resumes mid-sequence without redoing finished stages — that granularity is this
application's own state, not Hangfire's concern. Recording it in the same relational store as every
other entity avoids a second source of truth for "what's done."

## Decision 11: A new `IDocumentFileValidator`, not an extension of `IDocumentContentValidator`

**Decision**: Content validation for this feature (FR-010, FR-049) is a new
`IDocumentFileValidator`/`DocumentFileValidator` (`Application/Abstractions`,
`Infrastructure/Documents`), covering the full `DocumentFileType` set (including RTF, HTML, JSON,
XML, PNG, JPEG, TIFF, BMP, WEBP) — not an in-place extension of the existing
`IDocumentContentValidator`/`DocumentValidationResult` from specs/014-knowledge-base-management.

**Correction during implementation**: tasks.md originally described this as extending the
existing validator/entity in place. On inspection, `DocumentValidationResult` is hard-typed to
`KnowledgeBaseDocumentType` (`specs/014`'s RAG-ingestible format set) — extending it to also
recognize raster images and markup formats that are meaningless in a knowledge-base-ingestion
context would either pollute that enum with concepts it doesn't need or force an awkward
second/nullable output shape. This is exactly the bounded-context separation Decision 1 already
established for the entities themselves; the validator should follow the same rule. The new
implementation is free to reuse identical byte-signature logic (PDF `%PDF-`, OOXML ZIP + entry-name
disambiguation, UTF-8 plain-text sniffing) where formats overlap — that's the DRY-safe kind of
duplication (two small, independently-evolving call sites reading the same well-known magic
bytes), not the kind of business-logic duplication constitution §2.III forbids.

## Decision 12: Administrator dashboard reuses the existing role/authorization system

**Decision**: The organization-wide dashboard view (FR-045a) is gated by the same ASP.NET Identity
administrator role already used by `AdminDashboardController`/the frontend's `useIsAdmin` hook — no
new document-specific permission entity is introduced for this.

**Rationale**: Constitution §7 (Convention Over Configuration): a working admin-role convention
already exists and is exactly what FR-045a needs; introducing a parallel, document-scoped role
model would duplicate it for no reason.
