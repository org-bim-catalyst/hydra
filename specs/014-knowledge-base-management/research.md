# Phase 0 Research: Knowledge Base Management

All Technical Context unknowns are resolved below; no `NEEDS CLARIFICATION` markers remain.
Findings come from reading the existing codebase (`src/AskLucy.*`), not external sources —
this feature's technical shape is almost entirely determined by matching an already-shipped
pattern (`Chats`/`UserChat`, specs/002-chat-history-management) rather than by new external
research.

## Decision 1: Draft → Active is an explicit owner action, not automatic

**Decision**: A knowledge base is created in `Draft` status (FR-002) and moves to `Active`
only via an explicit `POST /api/v1/knowledge-bases/{id}/actions/activate` call, mirroring the
existing archive/restore action shape. `Active` is required before the future RAG pipeline
spec can index a knowledge base (FR-006).

**Rationale**: spec.md's Functional Requirements establish that Draft and Active are distinct
states and that only Active participates in future indexing eligibility, but no acceptance
scenario or FR specifies *what* triggers the Draft → Active transition. Rather than route this
back through `/speckit-clarify` (it does not change scope, security posture, or user-facing
behavior in a materially divergent way — it only decides which of two very similar UI actions
fires), this is treated as a plan-level technical decision per the constitution's normal
decision-making process (§17): an explicit action gives the owner a deliberate "this is ready"
moment (consistent with Draft existing as a real, meaningful state at all — if it auto-activated
immediately, Draft would never be observable and the whole status would be dead code, which
constitution §2.III (YAGNI) would flag as pointless).

**Alternatives considered**:
- *Auto-activate on creation* — rejected: makes the Draft status unreachable/meaningless,
  contradicting FR-002's explicit requirement that Draft is a real initial state.
- *Auto-activate on first document upload* — rejected: implicit triggers are harder to reason
  about and explain to a user ("why did my knowledge base activate?"), and nothing in the spec
  requires documents to gate activation; an empty, still-Draft knowledge base with a fully
  filled-in description is a completely reasonable thing for a user to want to keep as Draft.

## Decision 2: Deleted is not a fourth `Status` enum value

**Decision**: `KnowledgeBase.Status` is a 3-value enum (`Draft`, `Active`, `Archived`).
"Deleted" is represented entirely by the existing `BaseEntity.DeletedAtUtc`/`IsDeleted`
soft-delete mechanism (already present on every entity, already filtered by a per-entity
`HasQueryFilter`), exactly as `UserChat` already does — `UserChat` has no "Deleted" value in
any status field; a soft-deleted chat is simply a chat with `DeletedAtUtc` set, independent of
its `ArchivedAtUtc`/`PinnedAtUtc`/`IsFavorite` flags.

**Rationale**: spec.md's Lifecycle section lists "Deleted (Soft Delete)" as one of four states,
but the existing, already-proven `UserChat` convention treats soft-delete as an orthogonal flag
rather than a status value — this avoids a knowledge base needing to remember "what status was
I in before I was deleted" (a real problem the enum-only model would create: restoring a
deleted knowledge base needs to know whether to return it to Draft, Active, or Archived,
which the orthogonal-flag model gets for free by simply clearing `DeletedAtUtc` and leaving
`Status` untouched). Constitution §7 (Convention Over Configuration) favors this proven
in-codebase pattern over a fresh design.

**Alternatives considered**:
- *4-value enum with `Deleted`* — rejected: loses the prior status on delete unless a second
  "PreviousStatus" field is added, which is exactly the complexity the flag-based model avoids
  for free.

## Decision 3: Extend `IFileStorage` with `DeleteAsync`, don't introduce a parallel abstraction

**Decision**: Add `Task DeleteAsync(string storedFileName, CancellationToken)` to the existing
`IFileStorage` interface (`Application/Abstractions/IFileStorage.cs`), implemented in
`LocalFileStorage`. Both the immediate owner-triggered purge (FR-036) and the 30-day automatic
purge sweep call this to physically delete a `KnowledgeBaseDocument`'s underlying file when its
owning knowledge base is permanently purged.

**Rationale**: `IFileStorage` today only has `SaveAsync`/`OpenReadAsync` — no caller has ever
needed to delete a stored file (avatars are replaced, not deleted; chat attachments have no
delete path either). This feature is the first to need deletion. Extending the existing
interface (rather than adding a second, competing file-storage abstraction) is required by
constitution §7 (Convention Over Configuration) and keeps the "swap storage backend via one
Infrastructure implementation change" guarantee (§3) intact — `LocalFileStorage` gets the new
method; a future S3/Blob implementation would too, with zero Application/Domain changes.

**Alternatives considered**:
- *New `IFileDeletionService` interface* — rejected: needless proliferation of a
  single-responsibility interface for what is naturally one more method on the file storage
  contract; ISP (constitution §2.II) is about not forcing unrelated clients to depend on
  methods they don't use, not about splitting every verb into its own interface.

## Decision 4: Duplication (deep copy) is synchronous for the request, not backgrounded

**Decision**: `DuplicateKnowledgeBaseCommand` performs the folder-tree copy and per-document
independent physical file copy (per spec.md's Clarifications) synchronously within the request,
returning the new knowledge base once every document has been copied. SC-006 ("begin working
in the new copy in under 10 seconds ... for knowledge bases with up to 1,000 documents") is
the explicit performance budget this must meet.

**Rationale**: A synchronous copy keeps the operation simple and immediately consistent (no
"copy in progress" status to model, no background-job infrastructure to introduce for a single
feature). 1,000 file copies through `IFileStorage.SaveAsync`/`OpenReadAsync` (local filesystem
`Stream.CopyToAsync`) is well within a 10-second budget on local disk I/O; this is validated at
implementation time with a performance test (constitution §10) against exactly that document
count, per SC-006's own stated ceiling.

**Alternatives considered**:
- *Background job with a "Copying" status* — rejected for this release: introduces new
  infrastructure (a job queue) and a new transient status not in spec.md's Lifecycle section,
  to solve a performance problem not yet demonstrated to exist. If a future measurement shows
  1,000-document duplication exceeding budget in production, that becomes its own
  small, focused follow-up change — not a reason to add speculative infrastructure now
  (constitution §2.III YAGNI).

## Decision 5: Page-count extraction uses only the .NET BCL — no new NuGet dependency

**Decision**: `IDocumentPageCountExtractor` (Application abstraction) is implemented using
only `System.IO.Compression` (already part of the BCL) for the OOXML zip-based formats and a
small, purpose-built PDF trailer reader:
- **PPTX**: page count = count of `ppt/slides/slideN.xml` entries in the zip archive.
- **DOCX**: page count = the `<Pages>` value in `docProps/app.xml` if present (Word populates
  this on save); `null` (N/A) if absent, per spec.md's Assumption that page count is
  best-effort for these types.
- **PDF**: page count parsed from the document's `/Type /Pages` `/Count` entry via a minimal
  trailer/xref reader.
- Any parse failure (malformed, encrypted, or unexpected-structure file) logs at `Warning` and
  leaves `PageCount` `null` — it does **not** fail the upload. This is a deliberate, narrow
  exception to constitution §2.VIII (no silent failures): the user-facing action here is
  "upload a document," which still succeeds; a missing derived statistic is not the outcome
  the user asked for and is visibly represented as "—"/"N/A" in the UI, not hidden.

**Rationale**: CLAUDE.md explicitly instructs "Avoid unnecessary dependencies," and the
project's stated tech stack does not currently include a PDF/OOXML parsing library. Page count
is a nice-to-have statistic (FR-030), not a correctness-critical feature — introducing a
heavier dependency (e.g., a commercial PDF SDK) is disproportionate. The chosen approach covers
the common, well-formed-file case cheaply and fails soft (null) rather than blocking uploads on
the uncommon case.

**Alternatives considered**:
- *A full PDF/Office parsing library* (e.g., an OpenXML SDK or a commercial PDF library) —
  rejected for now as heavier than justified by a single derived statistic; swappable later
  behind the same `IDocumentPageCountExtractor` interface with zero Application/Domain changes
  if accuracy requirements grow (e.g., once the future RAG spec needs real text extraction
  anyway, at which point page count likely comes for free from that pipeline instead).

## Decision 6: Folder/document drag-and-drop uses `@dnd-kit`

**Decision**: The folder tree and document grid/list drag-and-drop interactions (FR-014) use
`@dnd-kit/core` — a small, actively maintained, accessibility-first React DnD library with
built-in keyboard sensor support, which directly satisfies FR-040's requirement that
drag-and-drop have a keyboard-accessible equivalent without hand-rolling one.

**Rationale**: No existing feature in this codebase implements drag-and-drop, so there is no
established in-repo convention to follow (constitution §7 only applies where a convention
already exists). `@dnd-kit` was selected over the alternatives specifically because keyboard
operability is a hard *requirement* here (FR-040/FR-042), not a nice-to-have, and it is the
option that provides that natively rather than requiring a bespoke keyboard-interaction layer
to be built and separately accessibility-tested.

**Alternatives considered**:
- *react-dnd* — rejected: HTML5-backend drag-and-drop by default has materially weaker
  built-in keyboard/touch support, requiring extra work to meet FR-040/FR-042.
- *Hand-rolled pointer-event drag-and-drop* — rejected: reinvents what `@dnd-kit` already
  solves, with a much larger accessibility-testing burden to reach the same bar.

## Decision 7: Dashboard summary statistics are cached in-memory with a short TTL, not distributed cache

**Decision**: `GetKnowledgeBaseDashboardSummaryQuery` results are cached via
`IMemoryCache` with a short (60-second) TTL, keyed per user, and invalidated eagerly on any
mutation that changes counts (create/delete/purge/document add/remove) for that user.

**Rationale**: FR-035 requires caching so dashboard loads don't recompute full aggregates every
time (constitution §15 caching guidance). The existing codebase has no distributed cache
(Redis or similar) configured anywhere yet — introducing one for a single feature's summary
card would be a new cross-cutting infrastructure dependency requiring an ADR per constitution
§17, disproportionate to what a per-instance, short-TTL `IMemoryCache` already solves for the
stated performance goal (SC-003's 2-second budget at 1,000 knowledge bases). If the platform
later needs multi-instance cache coherency for other reasons, that ADR can cover this cache
too at that time.

**Alternatives considered**:
- *Distributed cache (Redis)* — rejected for this feature alone: disproportionate new
  infrastructure for one summary-card query; not precedented anywhere else in the codebase.
- *No caching, always compute live* — rejected: explicitly contradicts FR-035.

## Decision 8: Document uploads get magic-byte content validation and a size cap; this closes a pre-existing gap

**Decision**: A new `IDocumentContentValidator` (Application abstraction, Infrastructure
implementation) checks each uploaded knowledge-base document's actual byte signature against
its claimed type (PDF, Word, Excel, PowerPoint, Markdown, CSV, Text — the file types this
spec's objective lists as supported, per CLAUDE.md's RAG section) before persisting, and
rejects files over a configurable size limit (`KnowledgeBaseDocumentOptions.MaxFileSizeBytes`,
bound via `IOptions<T>` per constitution §4). Rejections return a specific, actionable 400, not
a silent drop.

**Rationale**: Constitution §8 requires "Uploaded files are validated by content (magic-byte
sniffing), not by extension/MIME header alone" for every file-handling surface — a rule that
predates this feature but has no existing implementation anywhere in the codebase (avatars and
chat attachments currently only trust `IFormFile.ContentType`, an unvalidated client-supplied
header). This feature is the first to close that gap for its own upload path;
retrofitting avatars/attachments is out of this spec's scope (no FR references them) and is
flagged here as a pre-existing gap elsewhere in the codebase, not a new one introduced by this
feature — recorded for awareness, not silently left unaddressed.

**Residual, explicitly out of scope**: constitution §8 also says uploads are "scanned" —
no anti-malware/AV scanning integration exists anywhere in this codebase today, and no FR in
spec.md requires one. This is called out as a known limitation, matching how
specs/012-elevenlabs-voice-engine flagged its own out-of-reach constraint rather than silently
claiming full compliance.

**Alternatives considered**:
- *Trust `IFormFile.ContentType`/extension alone* — rejected outright: this is the exact
  practice constitution §8 forbids.
