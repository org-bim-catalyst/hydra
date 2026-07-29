# Data Model: Chat History & Conversation Management

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

This extends the existing `UserChat`/`Message` aggregate (`src/AskLucy.Domain/Chats/`,
introduced in SPEC-000) rather than introducing a parallel model — see research.md Topic
1. All entities inherit `BaseEntity` (`Id: Guid` v7, `CreatedAtUtc/CreatedBy`,
`ModifiedAtUtc/ModifiedBy`, `DeletedAtUtc/DeletedBy`, `RowVersion`), per constitution §5.

## Conversation (extends existing `UserChat`)

The spec's "Conversation" business concept is this entity; the type name `UserChat`
persists to avoid a disruptive rename of already-shipped code (research.md Topic 1).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (v7) | Surrogate key (existing) |
| `Title` | `string` (≤200) | Manual or auto-generated (FR-013/FR-014); existing field |
| `UserId` | `string` | Owner FK to `ApplicationUser`; existing field |
| `SessionId` | `string?` | Existing field, unrelated to this feature |
| `IsTitleManuallySet` | `bool` | **New.** Set `true` on first manual rename; auto-title generation (FR-013) only fires while `false` (FR-014). |
| `ArchivedAtUtc` | `DateTime?` | **New.** Non-null = archived (FR-006/FR-007). |
| `PinnedAtUtc` | `DateTime?` | **New.** Non-null = pinned; sort key for pinned-first ordering (FR-008). |
| `IsFavorite` | `bool` | **New.** Favorite flag, independent of archive/pin (FR-009). |
| `DeletedAtUtc` / `DeletedBy` | `DateTime?` / `string?` | Existing `BaseEntity` soft-delete fields, reused as the "Recently Deleted" / Trash state (FR-003, research.md Topic 2). Global EF query filter (`DeletedAtUtc == null`) already hides these from default queries; a Trash-scoped query explicitly bypasses the filter. |
| `CreatedAtUtc` / `ModifiedAtUtc` | `DateTime` / `DateTime?` | Existing audit fields; surfaced to the user per FR-012. |
| `RowVersion` | `byte[]` | Existing concurrency token (research.md Topic 10). |

**Validation rules** (Domain, unchanged/extended):
- `Title` required, non-blank after trim (existing rule, FR-002).
- Archive/Restore/Pin/Unpin/Favorite/Unfavorite are no-ops guarded by current state (e.g.,
  archiving an already-archived conversation is idempotent, not an error).
- Permanent Delete and Clear-Messages both require an explicit confirmation flag to have
  been acknowledged by the caller (enforced at the Application command boundary — see
  contracts) before the handler proceeds (FR-005, FR-011).

**Lifecycle** (state is the cross-product of three independent flags plus the shared
soft-delete field — not a single enum, per research.md Topic 2):

```text
Active (default) ──archive──> Archived ──restore──> Active
Active/Archived ──delete (soft)──> Recently Deleted ──restore──> prior Archived/Active state
Recently Deleted ──leave untouched──> stays in Recently Deleted (no auto-purge, FR-005b)
Any state ──permanent delete (hard, confirmed)──> gone (irreversible, FR-004/FR-005)
Pinned / Favorite are independent booleans orthogonal to the above at every state.
```

## Message (extends existing `Message`)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (v7) | Existing |
| `UserChatId` | `Guid` | FK to Conversation; existing |
| `Role` | `enum` (`User`, `Assistant`) | Existing |
| `Kind` | `enum` (`Text`, `Image`, `Translation`) | Existing |
| `Content` | `string` | Existing; full-text-indexed for search (research.md Topic 5) |
| `SourceText` | `string?` | Existing |
| `Provider` | `string?` | **New.** AI provider name for assistant messages (FR-016); null for user messages. |
| `Model` | `string?` | **New.** Model identifier for assistant messages (FR-016). |
| `GenerationParametersJson` | `string?` | **New.** Serialized generation parameters (temperature, etc.) in effect for this message (FR-016); stored as JSON since parameter shape varies by provider/model. |
| `InputTokenCount` | `int?` | **New.** (FR-016) |
| `OutputTokenCount` | `int?` | **New.** (FR-016) |
| `CreatedAtUtc` | `DateTime` | Existing; the message timestamp (FR-016). |

**Validation rules**: unchanged — `Content` required (existing). Messages remain
immutable/append-only (FR-018); no new mutation methods are added to `Message`.

**Full-text index**: `Content` (Message) and `Title` (UserChat) participate in a SQL
Server full-text catalog (research.md Topic 5), added via an EF Core migration.

## Attachment (new entity)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (v7) | Surrogate key |
| `MessageId` | `Guid` | FK to `Message`, cascade delete with parent |
| `FileName` | `string` | Original/display filename (FR-017) |
| `ContentType` | `string` | MIME type, for rendering/export (FR-017) |
| `AccessLocation` | `string` | Existing signed-URL/storage reference the file is already served from (per CLAUDE.md File Management — never a raw physical path) |
| Audit fields | — | `BaseEntity` |

**Relationship**: One `Message` → many `Attachment`. Attachments reference files already
produced by existing upload/generation capabilities (research context, spec.md
Assumptions) — this feature persists the reference, it does not add new upload/storage
capability.

## Citation (new entity)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (v7) | Surrogate key |
| `MessageId` | `Guid` | FK to `Message` (assistant messages only), cascade delete with parent |
| `SourceLabel` | `string` | Human-readable source description (FR-017) |
| `SourceReference` | `string?` | URL or identifier of the source, if any |
| Audit fields | — | `BaseEntity` |

**Relationship**: One `Message` → many `Citation`.

## Conversation Export (not a persisted entity)

Per spec.md's Key Entities, this is a point-in-time projection, not a stored row: an
on-demand serialization of a Conversation plus its Messages (with Attachment/Citation
references, per the export clarification) into the JSON structure chosen in research.md
Topic 7. No new table is required.

## Entity Relationship Summary

```text
ApplicationUser 1───* UserChat (Conversation)
UserChat        1───* Message
Message         1───* Attachment
Message         1───* Citation
```

No changes to `ApplicationUser` or any other existing aggregate are required.
