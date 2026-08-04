# Phase 1 Data Model: Knowledge Base Management

New entities live in `AskLucy.Domain/KnowledgeBases/`, configured in
`AskLucy.Persistence/Configurations/`, per constitution §3 (Domain purity — no EF Core
attributes on Domain types; all mapping via Fluent API). Surrogate keys are `Guid` v7
(`Guid.CreateVersion7()`), matching every existing entity. Audit columns (`CreatedAtUtc`/
`CreatedBy`/`ModifiedAtUtc`/`ModifiedBy`) and the `RowVersion` concurrency token come from
`BaseEntity` + `AuditSaveChangesInterceptor`, exactly as on every existing entity, and are not
repeated per-entity below. `DeletedAtUtc`/`DeletedBy`/`IsDeleted` are called out explicitly per
entity only where soft delete is (or is deliberately not) used.

## New Entities

### KnowledgeBase

The aggregate root (FR-001–FR-011). Mirrors `UserChat`'s lifecycle shape (research.md
Decision 2).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `OwnerId` | `string` | Required. FK to `ApplicationUser`. Every query/mutation is scoped to this (FR-009/FR-010). |
| `Name` | `string` (≤200) | Required (FR-001). Not unique — disambiguated by `CreatedAtUtc` in the UI (spec.md Assumptions). |
| `Description` | `string?` (≤2000) | FR-003. |
| `Status` | `enum` (`Draft`, `Active`, `Archived`) | FR-002. Defaults `Draft` on creation. "Deleted" is **not** a value here — see research.md Decision 2. |
| `Color` | `string?` (≤7, hex) | FR-003. |
| `Icon` | `string?` (≤50) | FR-003, an icon-key string (e.g., `"folder-open"`), not binary data. |
| `CategoryId` | `Guid?` | FK `KnowledgeBaseCategory`. FR-019 — exactly one category, optional (an uncategorized KB is valid). |
| `Notes` | `string?` (≤4000) | FR-003. |
| `IsFavorite` | `bool` | FR-028. Defaults `false`. |
| `PinnedAtUtc` | `DateTime?` | FR-028. Non-null = pinned; also the sort key for pinned-first ordering, mirroring `UserChat.PinnedAtUtc`. |
| `DocumentCount` | `int` | Denormalized cached counter (FR-030/FR-031/FR-035) — see "Explicitly Not Modeled" below for why this is not a separate `KnowledgeBaseStatistics` table. Updated transactionally alongside document add/remove/move. |
| `TotalPageCount` | `int` | Same rationale; sum of child documents' non-null `PageCount`. |
| `StorageSizeBytes` | `long` | Same rationale; sum of child documents' `SizeBytes`. |
| `PurgeScheduledAtUtc` | `DateTime?` | Set to `DeletedAtUtc + 30 days` on soft delete (FR-036); cleared on `Restore`. Read by `KnowledgeBasePurgeHostedService` (indexed). |
| `DeletedAtUtc` / `DeletedBy` | `DateTime?` / `string?` | `BaseEntity` soft-delete fields, filtered via `HasQueryFilter(kb => kb.DeletedAtUtc == null)`. |

**Validation rules** (Domain):
- `Name` required, non-blank after trim.
- `OwnerId` required.
- Archive/Restore/Favorite/Unfavorite/Pin/Unpin are idempotent no-ops guarded by current state
  (mirrors `UserChat`'s equivalent methods).
- `Activate()` throws `DomainRuleViolationException` if not currently `Draft`.
- `Archive()` throws if not currently `Active` (Archived/Draft cannot be archived directly —
  matches spec.md User Story 3's scenarios, which only describe archiving an Active knowledge
  base).
- Soft `Delete()` sets `DeletedAtUtc`/`DeletedBy` and computes `PurgeScheduledAtUtc`; `Restore()`
  clears both `DeletedAtUtc`/`DeletedBy` and `PurgeScheduledAtUtc`, leaving `Status` untouched
  (research.md Decision 2 — this is exactly why Deleted is a flag, not an enum value: restore
  needs no "what was I before" lookup).

**Relationships**: Owns `KnowledgeBaseFolder`s and (indirectly, via folders)
`KnowledgeBaseDocument`s (cascade on hard delete only — see Document below). References zero
or one `KnowledgeBaseCategory`. Referenced by zero or more `KnowledgeBaseTag` rows.

**Lifecycle**:

```text
Draft (default) ──activate──> Active ──archive──> Archived ──restore──> Active
Draft/Active/Archived ──delete (soft)──> soft-deleted, PurgeScheduledAtUtc = +30d
soft-deleted ──restore──> prior Status (Draft/Active/Archived, unchanged — never touched by delete)
soft-deleted ──owner purge (confirmed) OR PurgeScheduledAtUtc elapses──> hard-deleted,
  cascades to permanently delete every child KnowledgeBaseDocument's file (FR-036)
Favorite / Pinned are independent flags orthogonal to the above at every state (edge case:
  archiving a favorited/pinned KB keeps both flags — spec.md Edge Cases).
```

---

### KnowledgeBaseFolder

A node in a knowledge base's hierarchy (FR-012–FR-016).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `KnowledgeBaseId` | `Guid` | Required FK, indexed. |
| `ParentFolderId` | `Guid?` | FK to another `KnowledgeBaseFolder`. Null = root-level folder. Indexed. |
| `Name` | `string` (≤200) | Required. |
| `Depth` | `int` | Computed at create/move time (`ParentFolderId is null ? 0 : parent.Depth + 1`); stored (not recomputed per-read) so the nesting-depth check (FR-012) is a cheap comparison, not a recursive query, on every create/move. |
| `DeletedAtUtc` / `DeletedBy` | `DateTime?` / `string?` | Soft delete, same filter pattern. |

**Validation rules**:
- `Name` required, non-blank.
- `Depth` MUST NOT exceed the configured maximum (default 10, spec.md Assumptions;
  `KnowledgeBaseFolderOptions.MaxNestingDepth`, bound via `IOptions<T>`).
- A folder MUST belong to the same `KnowledgeBaseId` as its `ParentFolderId`'s folder, when set
  (cross-knowledge-base nesting is never valid).
- Moving a folder into itself or any of its own descendants is rejected (FR-013) — enforced in
  the `MoveFolderCommandHandler` via a repository-provided descendant check (an
  aggregate-oriented repository method, not a leaky `IQueryable` per constitution §3), since
  this is a cross-aggregate-instance check the Domain model itself cannot perform without a
  database round trip.
- Deleting a folder that still contains documents or subfolders requires an explicit `Confirm`
  flag on the command (FR-015), same pattern as `DeleteFolderCommand(Guid Id, bool Confirm)`
  mirroring `ClearUserChatMessagesCommand`'s confirm-flag shape.

**Relationships**: Belongs to exactly one `KnowledgeBase`; optionally one parent
`KnowledgeBaseFolder`. Owns zero or more child folders and zero or more
`KnowledgeBaseDocument`s.

---

### KnowledgeBaseDocument

Associates an uploaded file (via the existing `IFileStorage`) with exactly one knowledge
base and at most one folder (FR-016, FR-030, FR-036, FR-037). **New concept** — nothing like
this exists in the codebase today; see plan.md Summary and research.md for why it's needed.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `KnowledgeBaseId` | `Guid` | Required FK, indexed. |
| `FolderId` | `Guid?` | FK `KnowledgeBaseFolder`. Null = lives at the knowledge base's root (not inside any folder). |
| `FileName` | `string` (≤260) | Original, user-facing file name (display only — never used as the storage path, per constitution §8). |
| `StoredFileName` | `string` (≤300) | The opaque key returned by `IFileStorage.SaveAsync` (research.md Decision 3). |
| `ContentType` | `string` (≤200) | Server-validated MIME type (research.md Decision 8) — not trusted from the client alone. |
| `SizeBytes` | `long` | Required. |
| `PageCount` | `int?` | Null = not applicable/not determined (research.md Decision 5). Meaningful only for PDF/Word/PowerPoint per spec.md Assumptions. |
| `ProcessingStatus` | `enum` (`Uploaded`, `Processing`, `Ready`, `Failed`) | Reflects this spec's own lightweight post-upload work (page-count extraction, content validation) — **not** a RAG-ingestion status; that pipeline is a future spec and out of scope here. |
| `UploadedAtUtc` | `DateTime` | Required. |
| `DeletedAtUtc` / `DeletedBy` | `DateTime?` / `string?` | Soft delete, same filter pattern — a document removed from a knowledge base is recoverable the same way the knowledge base itself is, until its owning knowledge base is purged (cascade hard-delete, FR-036). |

**Validation rules**:
- `FolderId`, when set, MUST belong to the same `KnowledgeBaseId`.
- `ContentType` MUST be one of the supported types (PDF, Word, Excel, PowerPoint, Markdown,
  CSV, Text) and MUST match the file's actual byte signature (research.md Decision 8) — a
  mismatch is rejected with a specific 400, never silently reclassified.
- `SizeBytes` MUST NOT exceed `KnowledgeBaseDocumentOptions.MaxFileSizeBytes`.

**Relationships**: Belongs to exactly one `KnowledgeBase` and at most one `KnowledgeBaseFolder`
(FR-016's "at most one folder within it" — never simultaneously owned by multiple knowledge
bases).

**Lifecycle note (cascade delete, FR-036)**: When a `KnowledgeBase` is hard-deleted (owner
purge or the 30-day sweep), every one of its documents — including previously soft-deleted
ones — is hard-deleted, and `IFileStorage.DeleteAsync(StoredFileName)` is called for each
before the database rows are removed, in the same unit of work; the audit log entry
(`KnowledgeBaseAuditLog`, "PermanentDelete") is written before the file deletions begin, per
spec.md's edge case ("the deletion MUST be logged in the audit trail before the files are
removed").

---

### KnowledgeBaseTag

A free-form label assignable to a knowledge base (FR-017–FR-021).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `KnowledgeBaseId` | `Guid` | Required FK, indexed. |
| `OwnerId` | `string` | Required, indexed together with `Value` — supports "list this user's distinct tags for the filter dropdown" (`ListTagsQuery`) without scanning other users' data. |
| `Value` | `string` (≤50) | Required, trimmed, case-preserved but compared case-insensitively at the database level (a case-insensitive collation index, matching SQL Server's default collation already in use). |

**Validation rules**: `Value` required, non-blank, ≤50 chars. A given `(KnowledgeBaseId,
Value)` pair is unique (no duplicate tag on the same knowledge base).

**Relationships**: Many-to-one to `KnowledgeBase` (a knowledge base has zero or more tags);
logically many-to-many across a user's knowledge bases (the same `Value` string can appear on
multiple knowledge bases owned by the same user) without a separate master "tag catalog" row —
see "Explicitly Not Modeled" below.

---

### KnowledgeBaseCategory

A classification value, predefined-and-shared or custom-and-private (FR-017–FR-019, FR-038).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `OwnerId` | `string?` | **Null = predefined, shared platform-wide** (the 8 seeded categories). **Non-null = custom, private to that owner** (FR-038). This nullability is the sole discriminator — no separate `IsPredefined` flag, avoiding a redundant signal. |
| `Name` | `string` (≤100) | Required. |

**Validation rules**: `Name` required, non-blank. For custom categories, `(OwnerId, Name)` is
unique (case-insensitive) — a user cannot create two categories with the same name; no such
constraint across different owners (two users may each have their own "Vendor Docs" category).
Predefined categories (`OwnerId == null`) are seeded once via migration/seed data and are not
user-editable or user-deletable.

**Relationships**: Referenced by zero or more `KnowledgeBase` rows via `KnowledgeBase
.CategoryId`.

**Lifecycle note (FR-021)**: Deleting a custom category (only the owner's own, never a
predefined one) sets every `KnowledgeBase.CategoryId` that referenced it to `null`
("Uncategorized" is simply the absence of a category, not a sentinel row) within the same
transaction as the category's own hard delete — categories carry no independent history worth
soft-deleting (unlike knowledge bases/documents, there is no "undo" concept requested for
category deletion in spec.md).

---

### KnowledgeBaseAuditLog

Immutable record of a lifecycle-relevant action (FR-011). Append-only, mirrors
`ProviderHealthCheck`/`VoiceProviderFailoverEvent`'s documented "log, not user-editable data"
exception to soft delete.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `KnowledgeBaseId` | `Guid` | Indexed together with `OccurredAtUtc` — supports "history for this knowledge base" queries. |
| `UserId` | `string` | The acting user (required — every audited action has an authenticated actor; there is no anonymous/system-initiated action in this list except the 30-day sweep, which records `"system:knowledge-base-purge"`, mirroring `"system:health-check"`'s existing convention). |
| `Action` | `enum` (`Created`, `Edited`, `Archived`, `Restored`, `Deleted`, `PermanentlyDeleted`, `Duplicated`) | Exactly FR-011's list — scoped narrowly; folder/document-level events are not separately audited because no FR requires it (constitution §2.III YAGNI). |
| `OccurredAtUtc` | `DateTime` | Required, indexed. |
| `DetailsJson` | `string?` (≤2000) | A short, sanitized summary of what changed (e.g., which fields were edited) — never raw content, never a secret (constitution §14, same rule already documented on `VoiceProviderFailoverEvent.Reason`). |

**Validation rules**: None beyond required fields — this is a log, not a user-editable entity.

**Relationships**: References a `KnowledgeBaseId` by value, not a navigation property/FK
constraint that would force cascade behavior — an audit entry for a knowledge base that has
since been permanently purged is deliberately retained (it *is* the record that the purge
happened), so it cannot have a hard FK to a row that may no longer exist.

**Lifecycle note**: No soft delete — append-only operational/compliance log, same documented
exception as `ProviderHealthCheck`. A retention/pruning policy is out of scope for this spec
(no FR requires it).

---

## Explicitly Not Modeled

The spec's Key Entities/Database sections describe several concepts that are **not**
implemented as new database tables — recorded here so a future reader doesn't wonder why
they're "missing":

- **KnowledgeBasePermission**: Not created in this release. Every knowledge base's
  authorization is `OwnerId`-only (FR-009/FR-010), with zero FRs in the finalized spec
  exercising sharing. Constitution §2.III (YAGNI) forbids tables built for hypothetical future
  requirements absent from the approved spec; see plan.md's Constitution Check for the full
  justification. Adding this table later (when a sharing spec ships) is additive, not
  breaking.
- **KnowledgeBaseStatistics**: Not a separate table. `DocumentCount`/`TotalPageCount`/
  `StorageSizeBytes` are denormalized cached counters directly on `KnowledgeBase` (see that
  entity's table above), updated transactionally alongside document mutations, rather than
  requiring a join on every dashboard list row — the dashboard's primary query (list N
  knowledge bases with their stats) is exactly the kind of hot, row-per-item read this
  optimizes for, and a separate 1:1 table would only add a join with no independent lifecycle
  of its own (constitution §III DRY/simplicity — a 1:1 "statistics" table with no distinct
  ownership or query pattern is redundant with columns on the owning row).
- **Separate "tag catalog" master table**: `KnowledgeBaseTag` rows are per-knowledge-base
  values, not references into a deduplicated master list — a tag has no attributes beyond its
  text (unlike categories, which need `OwnerId`-scoped identity for FR-038's private/shared
  split), so a master table would add a join with no behavior it enables that the indexed
  `(OwnerId, Value)` shape above doesn't already provide equally well.

## Modified Entities

**`IFileStorage`** (Application/Abstractions) — extended with `DeleteAsync` (research.md
Decision 3). No existing entity's schema changes; this is an interface change, not a data
model change, called out here because it's the one piece of "modification to something that
already exists" in this feature.

**None of `UserChat`, `Message`, `Attachment`, or any other existing entity changes.** This
feature is fully additive at the schema level beyond the `IFileStorage` interface extension
above.
