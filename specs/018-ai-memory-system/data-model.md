# Phase 1 Data Model: AI Memory System

New entities live in `AskLucy.Domain/Memory/` and `AskLucy.Domain/Projects/` (two bounded contexts,
research.md Decision 1), configured in `AskLucy.Persistence/Configurations/Memory/` and
`.../Projects/` per constitution §3 (Domain purity — no EF Core attributes on Domain types; all
mapping via Fluent API). Surrogate keys are `Guid` v7 (`Guid.CreateVersion7()`), matching every
existing entity. Audit columns (`CreatedAtUtc`/`CreatedBy`/`ModifiedAtUtc`/`ModifiedBy`) and the
`RowVersion` concurrency token come from `BaseEntity` + the existing `AuditSaveChangesInterceptor`,
exactly as on every existing entity, and are not repeated per-entity below. One existing entity
gains an additive field only (`Chats.UserChat`), documented under **Extended Entities**.

## New Entities — `Memory` bounded context

### Memory

The core aggregate — a single remembered fact or preference (spec.md FR-001–FR-014a, Key Entity
"Memory").

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserId` | `string` | Owner (FR-027). Indexed. |
| `ProjectId` | `Guid?` | FK `Projects.Project`, nullable = "general" scope (research.md Decision 1, FR-002). Indexed. |
| `Category` | `enum MemoryCategory` | `UserPreference, PersonalFact, ProjectContext, ConversationDerived` (FR-001). `KnowledgeDerived` intentionally not added — reserved for a future iteration per spec.md Assumptions. Closed, stable set → C# enum, not a lookup table (constitution §4 "Magic values, constants, enums"; see "Explicitly Not Modeled" below). |
| `Content` | `string` (`nvarchar(max)`) | The fact/preference text. **Encrypted at rest** via an `IDataProtector`-backed value converter (research.md Decision 12) — PII by construction. |
| `State` | `enum MemoryLifecycleState` | `Candidate, PendingApproval, Active, Archived` (FR-005). Soft-deleted rows (`IsDeleted`) are excluded via the standard global query filter rather than a `Deleted` enum value — matches the codebase's existing soft-delete convention (constitution §5) rather than inventing a parallel deletion channel. "Updated" (spec.md FR-005's fifth listed state) is modeled as an *event*, not a persisted state — an update keeps `State = Active` and appends a `MemoryVersion` row (below); there is no distinct "Updated" value to avoid an ambiguous state that duplicates what the version history already expresses unambiguously. |
| `IsSensitive` | `bool` | Set by the extraction classification (FR-008, research.md Decision 8). Forces `ApprovalMode = Manual` for this row regardless of the user's category-level setting (Decision below, `MemoryCategoryPreference`). |
| `SourceType` | `enum MemorySourceType` | `ExplicitUserStatement, PassiveConversationAnalysis` at launch; `ProjectConfiguration`, `Integration` reserved fields for future sources per spec.md's "Memory Creation" section, not yet emitted by any handler in this release. |
| `SourceConversationId` | `Guid?` | FK `Chats.UserChat` — which conversation produced this memory, when applicable. Nullable (a future non-conversation source could omit it). |
| `Importance` | `decimal(3,2)` | `0.00`–`1.00` (FR-010). Set by extraction classification; user-editable in the Memory Center. |
| `Confidence` | `decimal(3,2)` | `0.00`–`1.00` (FR-010). Set by extraction classification; not user-editable (reflects the system's own certainty, not a user preference). |
| `LastReinforcedAtUtc` | `DateTime` | FR-010 recency input; bumped whenever the same fact is restated (Edge Case: "same fact stated many times" → reinforce, don't duplicate). |
| `FrequencyCount` | `int` | FR-010; incremented alongside `LastReinforcedAtUtc`. |
| `ExpiresAtUtc` | `DateTime?` | FR-010, only set for explicitly time-bound memories (spec.md Assumptions — no default expiration otherwise). |

**Validation rules** (Domain):
- `Content` required, non-blank.
- `Importance`/`Confidence` clamped to `[0, 1]` in the factory/mutator, not left to the caller.
- A row can only be `Active` while `IsDeleted = false`; the standard global query filter
  additionally excludes soft-deleted rows from every read path (retrieval, Memory Center list,
  search) without a second manual check per query (constitution §5 convention).
- Mutation is via named methods only (`Approve(actor)`, `Reject(actor)`, `Edit(newContent, actor)`,
  `Archive(actor)`, `Reinforce(actor)`, `MarkSensitive(actor)`), never a public setter — mirrors
  `UserChat.UpdateRetrievalSettings(...)`'s intention-revealing-method convention (research finding
  #4).

**Relationships**: Optionally belongs to one `Project`. Optionally sourced from one `Chats.UserChat`.
Has one current + zero or more historical `MemoryEmbedding` rows. Has zero or more `MemoryVersion`
rows (its edit history). Has zero or one open `MemoryApproval` (while `Candidate`/`PendingApproval`).
Referenced by zero or more `MemoryReference` rows (usage trace) and `MemoryConflict` rows (as either
side of a detected conflict).

---

### MemoryVersion

An immutable snapshot of a memory's prior content, captured on every content change (FR-009, FR-019,
Key Entity "Memory Version / History Entry").

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `MemoryId` | `Guid` | FK `Memory`, indexed. Cascade delete (a version has no meaning independent of its memory — unlike audit/notification rows, which deliberately do *not* cascade; see `MemoryAuditLog` below). |
| `PreviousContent` | `string` | The content *before* this change. Encrypted at rest, same converter as `Memory.Content` (research.md Decision 12). |
| `ChangeReason` | `enum MemoryChangeReason` | `UserEdit, ConflictResolutionSupersede, SystemReinforcement` (FR-015, FR-019). |
| `ChangedAtUtc` | `DateTime` | |
| `ChangedByActor` | `string` | User id, or a system-actor identifier for automated changes (matches the existing `actor`-string convention passed through every domain mutation method in this codebase). |

**Validation rules**: Append-only — no update/delete methods; created only via `Memory.Edit(...)`'s
internal call to a `Memory.CreateVersionSnapshot()` helper, never constructed directly by
Application-layer code.

**Relationships**: Belongs to one `Memory`.

---

### MemoryApproval

The pending/approved/rejected decision for a candidate memory (FR-005, FR-007, FR-021, Key Entity
"Memory Approval").

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `MemoryId` | `Guid` | FK `Memory`, indexed, cascade delete (no meaning without its memory). |
| `Decision` | `enum MemoryApprovalDecision` | `Pending, Approved, Rejected`. |
| `DecidedAtUtc` | `DateTime?` | Null while `Pending`. |
| `DecidedByActor` | `string?` | User id for a manual decision; a system-actor identifier when `ApprovalMode = Automatic` auto-approves (FR-007's "still appears in the Memory Center with its source disclosed" — the actor field is how "disclosed" is satisfied at the data level). |

**Validation rules**: At most one `MemoryApproval` row per `Memory` at a time — created when a
candidate is detected, resolved (never re-created) when approved/rejected.

**Relationships**: Belongs to one `Memory`.

---

### MemoryConflict

Tracks a detected contradiction/ambiguity between a new candidate and an existing active memory
(FR-015, FR-016 — amended by the 2026-08-09 clarification for asynchronous resolution).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `ExistingMemoryId` | `Guid` | FK `Memory`, indexed. |
| `NewMemoryId` | `Guid?` | FK `Memory` — set when the new statement was itself persisted as a separate candidate pending resolution; null when the new statement was auto-merged directly into `ExistingMemoryId` as a `DirectContradiction` (in which case no separate row for the "new" side ever existed — see `MemoryVersion.ChangeReason = ConflictResolutionSupersede` instead). |
| `ConflictType` | `enum MemoryConflictType` | `DirectContradiction, AmbiguousSupersedeOrSupplement` (research.md Decision 10). |
| `ResolutionStatus` | `enum MemoryConflictResolutionStatus` | `AutoResolved, PendingUserConfirmation, ResolvedKeepExisting, ResolvedKeepNew, ResolvedKeepBoth`. |
| `DetectedAtUtc` | `DateTime` | |
| `ResolvedAtUtc` | `DateTime?` | Null while `PendingUserConfirmation`. |
| `ResolvedByActor` | `string?` | |

**Validation rules**: A `Memory` with an open (`PendingUserConfirmation`) conflict is excluded from
memory-selection ranking (research.md Decision 4/10) until resolved — enforced in the retrieval
query, not by mutating `Memory.State` (the memory stays `Active`/`Candidate` as appropriate; the
*open conflict* is what suppresses it from selection, keeping the two concerns — lifecycle state vs.
conflict status — independently queryable).

**Relationships**: References one or two `Memory` rows.

---

### MemoryEmbedding

A vector representation of a memory's content, produced by a specific embedding provider/model
(FR-010, FR-011; research.md Decision 5). Structurally parallel to `Retrieval.Embedding` but a
distinct table — not a reuse (research.md Decision 5 explains why reuse was rejected).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `MemoryId` | `Guid` | FK `Memory`, indexed. |
| `EmbeddingProviderId` | `Guid` | FK `Retrieval.EmbeddingProvider` — **reused from the RAG feature** (research.md Decision 5 confirms `IEmbeddingService`/`IEmbeddingServiceResolver` are content-agnostic and directly reusable; the existing `EmbeddingProvider` catalog entity is reused the same way, avoiding a duplicate provider-catalog table). |
| `Vector` | `vector(n)` | Same native SQL Server vector column technique as `Retrieval.Embedding.Vector` — EF-ignored (`builder.Ignore(e => e.Vector)`), managed via raw ADO.NET inside `SqlServerMemoryVectorStore` (research.md Decision 5), for the same documented EF Core 10.0.10 Fluent API limitation `Retrieval.Embedding` already works around. |
| `IsCurrent` | `bool` | Exactly one current row per `MemoryId` — same "collapses a conceptual separate embedding-linkage concept into a flag" reasoning specs/016 used for `Embedding.IsCurrent`. |

**Validation rules**: Immutable after creation (a content edit re-embeds and flips `IsCurrent`,
mirroring `Retrieval.Embedding`'s convention exactly, for the same "keep historical similarity
queries meaningful" reason).

**Relationships**: Belongs to one `Memory` and one `Retrieval.EmbeddingProvider`.

**Indexes**: No `CREATE VECTOR INDEX` (research.md Decision 5 — inherited platform constraint from
specs/016). Brute-force `VECTOR_DISTANCE` scan, scoped per-user via a standard `(UserId)`-partitioned
query (the `MemoryId → Memory.UserId` join, indexed) rather than an unscoped table scan.

---

### MemoryAuditLog

Security/compliance audit trail (FR-028, SC-008, Key Entity "Memory Access/Audit Record"). Follows
the established per-context audit-log convention (research finding #5) — its own class, no shared
base beyond `BaseEntity`.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `MemoryId` | `Guid?` | **Not a hard/cascade FK** — an audit row must survive even if the memory is later hard-purged (GDPR-style erasure via account deletion, FR-026), mirroring `DocumentAuditLog.DocumentId`'s identical nullable-no-cascade convention. |
| `UserId` | `string` | The memory's owner (for scoping "show me my own audit trail" — this row is never exposed to any other user, FR-028). |
| `ActorUserId` | `string` | Who/what performed the action — may differ from `UserId` for a system-actor automated action (auto-approval, auto-conflict-resolution). |
| `Action` | `enum MemoryAuditAction` | `Created, Approved, Rejected, Edited, Archived, Deleted, Expired, ConflictDetected, ConflictResolved` (FR-028's "creation, updates, approvals, rejections, and deletions" plus the two conflict events and `Expired`, since those are also memory-affecting changes worth an audit trail entry — `Expired` added for the background cleanup job, research.md Decision 18). |
| `OccurredAtUtc` | `DateTime` | |
| `DetailsJson` | `string?` | A short, sanitized summary — mirrors `KnowledgeBaseAuditLog.DetailsJson`'s documented convention exactly ("never raw content, never a secret"). For `Edited`, this references the associated `MemoryVersion.Id` rather than duplicating the (already-encrypted) content a second time. |

**Validation rules**: Append-only (no mutation methods, only a static `Create(...)` factory) —
matches `KnowledgeBaseAuditLog`/`DocumentAuditLog` exactly.

**Relationships**: Loosely references `Memory` (no enforced FK cascade).

---

### MemoryNotification

Backing row for the FR-006a / conflict-confirmation low-noise signal (research.md Decision 11),
mirroring the existing `ProcessingNotifier`/`DocumentNotification` persist-then-push idiom.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserId` | `string` | Indexed — recipient. |
| `MemoryId` | `Guid?` | The memory the notification concerns, when applicable. |
| `EventType` | `enum MemoryNotificationEventType` | `AutoCreated, AutoApproved, ConflictNeedsConfirmation` (FR-006a; the conflict case from the 2026-08-09 clarification, Q2). |
| `Message` | `string` | Short, user-facing text. |
| `CreatedAtUtc` | `DateTime` | |
| `ReadAtUtc` | `DateTime?` | Null until the user has seen it in the Memory Center. |

**Relationships**: Loosely references `Memory` (nullable, no cascade — a notification should remain
readable even if its memory is later deleted).

---

### MemoryPreference

Account-level memory settings (FR-022, FR-025, Key Entity "Memory Preference").

| Field | Type | Notes |
|---|---|---|
| `UserId` | `string` | PK (one row per user — not a surrogate `Guid`, since this is a 1:1 extension of the user, matching how account-level singleton settings are naturally keyed). |
| `MemoryEnabled` | `bool` | FR-022 — the account-level on/off switch. Defaults `true` (memory is opt-out, consistent with FR-007's "new accounts default to automatic mode," clarified 2026-08-09). |

**Relationships**: Has many `MemoryCategoryPreference` child rows (below).

---

### MemoryCategoryPreference

Per-category approval mode and enablement (FR-007, FR-025).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserId` | `string` | FK `MemoryPreference`, indexed. |
| `Category` | `enum MemoryCategory` | Same enum as `Memory.Category`. |
| `ApprovalMode` | `enum MemoryApprovalMode` | `Automatic, Manual, Disabled` (FR-007). Defaults `Automatic` per the clarified default. |
| `IsEnabled` | `bool` | FR-025 — per-category on/off, independent of `ApprovalMode` (a category can be `Disabled` at the approval-mode level, meaning "don't create new candidates," which is distinct from `IsEnabled = false`, meaning "don't use this category's existing memories at all" — both are needed because FR-025's per-category disable must also stop *existing* memories in that category from being used, not just stop new ones from being created). |

**Validation rules**: Unique `(UserId, Category)`. A row is created lazily on first access with the
defaults above rather than requiring a bulk-insert-all-categories step at account creation
(consistent with "new accounts default to automatic mode for all categories" being a *default*, not
a value that must be materialized row-by-row up front).

**Relationships**: Belongs to one `MemoryPreference` (by `UserId`).

---

### MemoryReference

Usage trace — which memories were used to produce a given assistant response (FR-014; research.md
Decision 16). Structurally parallel to `Chats.Citation`'s role for RAG.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `MessageId` | `Guid` | FK `Chats.Message`, indexed. |
| `MemoryId` | `Guid` | FK `Memory` — **no cascade delete** (the trace must remain resolvable even if the memory is later edited/archived/deleted, same reasoning as `Citation`'s snapshot fields). |
| `RelevanceScore` | `decimal(5,4)` | The composite `finalScore` (research.md Decision 4) at selection time. |
| `ContentSnapshot` | `string` | The memory's content *as it was when used* — encrypted at rest, same converter as `Memory.Content`. |
| `CreatedAtUtc` | `DateTime` | |

**Relationships**: Belongs to one `Chats.Message`; loosely references one `Memory`.

---

## New Entities — `Projects` bounded context

### Project

A user-created workspace grouping related conversations, used to scope Project Memory (FR-002a,
FR-002b, User Story 5, Key Entity "Project"). Deliberately minimal per FR-002b's explicit scope
limit.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserId` | `string` | Owner. Indexed. |
| `Name` | `string` (`nvarchar(200)`) | Required, non-blank. |

**Validation rules**: Soft-deleted (`IsDeleted`/`DeletedAtUtc`) via the standard global query filter
convention, not hard-deleted — required so `ProjectDeletedDomainEvent` (research.md Decision 15) has
a committed row to reference and so historical `UserChat.ProjectId`/`Memory.ProjectId` values remain
resolvable for display ("this conversation belonged to a since-deleted project named X") rather than
becoming orphaned foreign keys.

**Relationships**: Has many `Chats.UserChat` (via `UserChat.ProjectId`) and many `Memory` (via
`Memory.ProjectId`). A conversation belongs to at most one `Project` at a time (FR-002a).

---

## Extended Entities

### `Chats.UserChat` (additive field only)

| Field | Type | Notes |
|---|---|---|
| `ProjectId` | `Guid?` | FK `Projects.Project`. Nullable = no project (general scope). Mutated only via a new `AssignToProject(Guid? projectId, string actor)` method — mirrors the existing nullable-override-block convention `UserChat.UpdateRetrievalSettings(...)` already established for specs/016's additive fields (research finding #4), not a public setter. |

No other existing entity requires a field change — the RAG feature's `IEmbeddingService`/
`IEmbeddingServiceResolver`/`EmbeddingProvider` are reused as-is (interfaces and a catalog entity,
not schema changes), and `IAIProvider`/`IAIProviderResolver` are called, not modified.

---

## Explicitly Not Modeled

- **Memory Category** (spec.md Key Entity) — modeled as `enum MemoryCategory`, not a lookup table.
  It is a closed, stable set (constitution §4's "C# `enum` is used for closed, stable sets") — the
  four launch categories are fixed by FR-001, and adding a fifth (e.g., the deferred
  `KnowledgeDerived`) is a code change with migration implications either way, so a table buys no
  runtime configurability this feature needs.
- **Memory Source** (spec.md Key Entity) — modeled as `Memory.SourceType` (enum) +
  `Memory.SourceConversationId` (nullable FK), not a separate `MemorySource` table. It has no
  independent rows or behavior beyond identifying where a memory came from — the same reasoning
  specs/016 used to fold its conceptual `ChunkEmbedding`/`VectorIndex` entities into existing
  fields/flags rather than standalone tables (constitution §2.III).
