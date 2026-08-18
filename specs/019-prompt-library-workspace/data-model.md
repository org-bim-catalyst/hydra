# Phase 1 Data Model: Prompt Library & Prompt Engineering Workspace

New entities live in `AskLucy.Domain/Prompts/` (one bounded context, research.md Decision 1),
configured in `AskLucy.Persistence/Configurations/Prompts/` per constitution §3 (Domain purity — no
EF Core attributes on Domain types; all mapping via Fluent API). Surrogate keys are `Guid` v7
(`Guid.CreateVersion7()`), matching every existing entity. Audit columns
(`CreatedAtUtc`/`CreatedBy`/`ModifiedAtUtc`/`ModifiedBy`), soft delete
(`DeletedAtUtc`/`DeletedBy`/`IsDeleted`), and the `RowVersion` concurrency token come from
`BaseEntity` + the existing `AuditSaveChangesInterceptor`, exactly as on every existing entity, and
are not repeated per-entity below. `AuthorId`/`CreatedBy` on a version or audit row is `BaseEntity`'s
own `CreatedBy`, not a duplicate field. No existing entity is extended by this feature — `Prompts` is
purely additive (`PromptExecution.ResultMessageId` *references* `Chats.Message` but adds no column to
it).

## New Entities

### Prompt

The aggregate root — the reusable asset itself (spec.md FR-001–FR-007, Key Entity "Prompt").

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `OwnerId` | `string` | Owner (FR-060, FR-090). Indexed. |
| `Name` | `string` (`nvarchar(200)`) | Unique per `OwnerId`, case-insensitive (FR-006, research.md Decision 7). |
| `Description` | `string?` (`nvarchar(1000)`) | |
| `PromptType` | `enum PromptType` | `Chat, System, Instruction, Summarization, Translation, Extraction, Classification, Rag, StructuredOutput` (FR-003). Closed, stable set → C# enum (constitution §4), not a lookup table. |
| `Status` | `enum PromptStatus` | `Draft, Active, Archived` (FR-001). Soft-deleted rows excluded via the standard global query filter rather than a `Deleted` status value, matching this codebase's soft-delete convention (constitution §5) — same reasoning `Memory.State` used (specs/018). |
| `SystemInstructions` | `string?` (`nvarchar(max)`) | Structural component (FR-002). |
| `DeveloperInstructions` | `string?` (`nvarchar(max)`) | Structural component (FR-002). |
| `UserInstructions` | `string` (`nvarchar(max)`) | Structural component (FR-002); the one required content field — a prompt with no user-facing instruction has nothing to execute. |
| `ContextText` | `string?` (`nvarchar(max)`) | Structural component "Context" (FR-002). |
| `ExamplesText` | `string?` (`nvarchar(max)`) | Structural component "Examples" (FR-002) — free-text/JSON blob, not a normalized child table (see "Explicitly Not Modeled" below). |
| `OutputInstructions` | `string?` (`nvarchar(max)`) | Structural component (FR-002). |
| `Constraints` | `string?` (`nvarchar(max)`) | Structural component (FR-002). |
| `FolderId` | `Guid?` | FK `PromptFolder`, nullable = unfiled (FR-050). Indexed. |
| `CategoryId` | `Guid?` | FK `PromptCategory` (FR-050). Indexed. |
| `CurrentVersionId` | `Guid` | FK `PromptVersion` — the active/current version (FR-030–FR-033). |
| `IsFavorite` | `bool` | FR-050. |
| `IsPinned` | `bool` | FR-050. |
| `RequiresStreaming` / `RequiresVision` / `RequiresFunctionCalling` / `RequiresJsonMode` / `RequiresReasoning` / `RequiresEmbeddings` / `RequiresImageInput` / `RequiresImageOutput` / `RequiresAudio` | `bool` (×9) | Same flat-column shape `AIModel` itself uses for its own capabilities (`src/AskLucy.Persistence/Configurations/AIModelConfiguration.cs:22-30` — `SupportsStreaming`, `SupportsVision`, etc. are plain scalar columns, not an owned type), so a required-vs-supported comparison is a straight per-flag AND, not a cross-shape mapping (FR-004). Both sides are assembled into the existing `AskLucy.Domain.Ai.AIModelCapabilities` record (`src/AskLucy.Domain/Ai/AIModel.cs:13`) in Application-layer code for the compatibility check — that record is reused as the in-memory comparison shape, not persisted directly (mirrors how `AIModel` itself only ever constructs it on read). |
| `PreferredModelKey` | `string?` | Optional soft preference — does not block execution against a different, capability-compatible model (FR-004). |

**Validation rules** (Domain):
- `Name` required, non-blank, unique per `OwnerId` (case-insensitive; enforced in the Application
  handler per research.md Decision 7, with a filtered unique index as defense-in-depth).
- `UserInstructions` required, non-blank.
- Mutation is via named methods only (`ApplyEdit(...)`, `Archive(actor)`, `Restore(actor)`,
  `Rename(name, actor)`, `SetFolder(folderId, actor)`, `SetFavorite(bool, actor)`,
  `SetPinned(bool, actor)`), never a public setter — mirrors `UserChat`/`Memory`'s
  intention-revealing-method convention.
- `ApplyEdit(...)` internally calls `CreateVersionSnapshot()` (research.md Decision 9) and updates
  `CurrentVersionId` — a `Prompt` is never left pointing at a version that doesn't exist.
- Before `ApplyEdit(...)` commits, content is checked via `PromptContentAnalyzer`
  (research.md Decision 10) for undeclared placeholders / unreferenced variable definitions
  (FR-014); violations throw `DomainRuleViolationException` (→ 400).

**Relationships**: Belongs to one owner. Optionally belongs to one `PromptFolder` and one
`PromptCategory`. Has one current + many historical `PromptVersion` rows. Has zero or more
`PromptTag`, `PromptTestCase`, `PromptExecution` rows. Has exactly one `PromptUsageStatistics` row.

---

### PromptVersion

An immutable snapshot of a `Prompt`'s content, variables, and model settings at a point in time
(FR-030–FR-033, Key Entity "PromptVersion").

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `PromptId` | `Guid` | FK `Prompt`, indexed. Cascade delete (no meaning independent of its prompt). |
| `VersionNumber` | `int` | Monotonically increasing per `PromptId` (unique index `(PromptId, VersionNumber)`). |
| `SystemInstructions` / `DeveloperInstructions` / `UserInstructions` / `ContextText` / `ExamplesText` / `OutputInstructions` / `Constraints` | as `Prompt` | Snapshot copies at save time. |
| `ProviderKey` | `string?` | Model-settings snapshot (FR-031). |
| `ModelKey` | `string?` | Model-settings snapshot (FR-031). |
| `Temperature` | `decimal?` | Model-settings snapshot (FR-031, FR-040). |
| `MaxOutputTokens` | `int?` | Model-settings snapshot (FR-031, FR-040). |
| `StructuredOutputRequested` | `bool` | Model-settings snapshot (FR-040). |
| `ChangeDescription` | `string?` (`nvarchar(500)`) | Optional, user-supplied (FR-031). |

**Validation rules**: Append-only — no update/delete methods; created only via
`Prompt.CreateVersionSnapshot()` (research.md Decision 9), never constructed directly by
Application-layer code. `RestoreVersionCommand` calls `Prompt.RestoreFrom(version, actor)`, which
creates a **new** `PromptVersion` copying the restored content rather than mutating or deleting any
existing row (FR-033).

**Relationships**: Belongs to one `Prompt`. Has many `PromptVariable` rows (this version's variable
definitions). Referenced by zero or more `PromptExecution` rows (which version was run).

---

### PromptVariable

A named placeholder definition, scoped to one `PromptVersion` (FR-010–FR-014, Key Entity
"PromptVariable").

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `PromptVersionId` | `Guid` | FK `PromptVersion`, indexed. Cascade delete (no meaning without its version). |
| `Name` | `string` (`nvarchar(100)`) | Matches a `{{name}}` placeholder (research.md Decision 10). Unique within a `PromptVersionId`. |
| `Description` | `string?` (`nvarchar(500)`) | |
| `VariableType` | `enum PromptVariableType` | `String, Number, Boolean, Date, Json, Text, File, Conversation, KnowledgeBase` (FR-012). |
| `IsRequired` | `bool` | FR-011, FR-013. |
| `DefaultValue` | `string?` | Stored as text, interpreted per `VariableType` at resolution time (FR-011). |
| `ExampleValue` | `string?` | FR-011, used by preview (FR-005). |
| `ValidationRulesJson` | `string?` (`nvarchar(1000)`) | Length/format/allowed-values rules (FR-011, FR-013) — stored as JSON rather than one column per rule kind, since the applicable rule set varies by `VariableType` (a fixed wide column set would be mostly-null for every type); validated against a documented per-type JSON shape in the Application-layer validator, not left schema-less. |
| `OrderIndex` | `int` | Stable display/insertion order in the editor. |

**Validation rules** (Domain/Application): `Name` required, matches placeholder-name grammar
(`[A-Za-z_][A-Za-z0-9_]*`). `DefaultValue`/`ExampleValue`, when present, must themselves satisfy
`VariableType` + `ValidationRulesJson` (a required-but-invalid default is rejected at save time, not
deferred to first execution). Execution-time resolution re-validates every supplied value against
`IsRequired`/`VariableType`/`ValidationRulesJson` and fails per-variable with a specific message
(FR-013) before any AI provider call.

**Relationships**: Belongs to one `PromptVersion`.

---

### PromptCategory

A classification value, predefined-and-shared or custom-and-private (FR-050, research.md
Decision 6 — reuses `KnowledgeBaseCategory`'s exact shape).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `OwnerId` | `string?` | `null` = predefined/shared platform-wide (a small seeded set); non-null = custom, private to that owner. |
| `Name` | `string` (`nvarchar(100)`) | |

**Relationships**: Referenced by zero or more `Prompt` rows.

---

### PromptTag

A free-form label assignable to a prompt (FR-050, FR-052, research.md Decision 6 — reuses
`KnowledgeBaseTag`'s exact shape: a per-prompt value row, not a deduplicated master tag catalog).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `PromptId` | `Guid` | FK `Prompt`, indexed. Cascade delete. Child of `Prompt`'s aggregate, created only via `Prompt.AddTag(...)`. |
| `OwnerId` | `string` | Denormalized owner, matching `KnowledgeBaseTag` (enables an owner-scoped distinct-tag-list query without a join through `Prompt`). |
| `Value` | `string` (`nvarchar(50)`) | |

**Relationships**: Belongs to one `Prompt`.

---

### PromptFolder

A user-defined, nested grouping (FR-050, FR-054, research.md Decision 5 — reuses
`KnowledgeBaseFolder`'s exact shape).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `OwnerId` | `string` | Indexed. |
| `ParentFolderId` | `Guid?` | Self-FK. Nullable = top-level. |
| `Name` | `string` (`nvarchar(100)`) | |
| `Depth` | `int` | Computed at create/move time (not recomputed per-read); a cheap comparison against `MaxNestingDepth`, mirroring `KnowledgeBaseFolder.Depth` exactly. |

**Validation rules**: `Create`/`MoveTo` reject a depth beyond `MaxNestingDepth`
(`DomainRuleViolationException`) and reject a move that would make a folder its own descendant (cycle
prevention, spec.md Edge Cases) — identical algorithm to `KnowledgeBaseFolder`/`MoveFolderCommandHandler`.

**Relationships**: Optionally has one parent `PromptFolder`. Has zero or more child `PromptFolder`
rows and zero or more `Prompt` rows.

---

### PromptTestCase

A saved, reusable test scenario (FR-043, Key Entity "PromptTestCase").

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `PromptId` | `Guid` | FK `Prompt`, indexed. Cascade delete. |
| `Name` | `string` (`nvarchar(200)`) | |
| `VariableValuesJson` | `string` (`nvarchar(max)`) | Input variable values (FR-043). |
| `ExpectedOutput` | `string?` (`nvarchar(max)`) | Optional (FR-043). |
| `EvaluationCriteria` | `string?` (`nvarchar(1000)`) | Optional (FR-043). |
| `ProviderKey` | `string` | The model/provider it was defined against (FR-043). |
| `ModelKey` | `string` | |
| `SourceExecutionId` | `Guid?` | FK `PromptExecution` — the execution it was saved from, when applicable (FR-043's "save an executed test... as a reusable test case"). No cascade (a test case outlives the specific execution it was captured from). |

**Relationships**: Belongs to one `Prompt`. Optionally references one `PromptExecution`.

---

### PromptExecution

One run of a `Prompt`/`PromptVersion` (FR-040–FR-046, FR-051, Key Entity "PromptExecution").

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `PromptId` | `Guid` | FK `Prompt`, indexed. |
| `PromptVersionId` | `Guid` | FK `PromptVersion` — which version was executed. |
| `Origin` | `enum PromptExecutionOrigin` | `TestingWorkspace, ConversationInsertion` (FR-040, FR-080, spec.md Clarifications — only a *successful* execution of either origin counts toward FR-051's usage/recency signal). |
| `ProviderKey` | `string` | |
| `ModelKey` | `string` | |
| `Temperature` | `decimal?` | Parameters actually used (may differ from the version's saved defaults — FR-040 allows per-execution overrides). |
| `MaxOutputTokens` | `int?` | |
| `StructuredOutputRequested` | `bool` | |
| `ResolvedVariableValuesJson` | `string` (`nvarchar(max)`) | The actual values supplied for this run (FR-100 observability; access restricted to the owner exactly like the prompt itself, FR-090/FR-091). |
| `RequestedRagContext` | `bool` | Whether RAG context was requested for this run (FR-081). |
| `RequestedMemoryContext` | `bool` | Whether memory context was requested for this run (FR-082). |
| `Outcome` | `enum PromptExecutionOutcome` | `Success, Failed` (FR-100, FR-101). |
| `ErrorDetail` | `string?` (`nvarchar(1000)`) | Sanitized, user-surfaceable failure reason when `Outcome = Failed` (FR-101) — never a raw exception message/stack trace. |
| `LatencyMs` | `int?` | FR-042, FR-100. |
| `ResultMessageId` | `Guid?` | FK `Chats.Message` — set only when `Origin = ConversationInsertion` and the send succeeded; the actual AI output/usage already lives on that `Message` via the existing chat pipeline (research.md Decision 4), so it is referenced, not duplicated. |

**Validation rules**: Immutable after creation (an execution is a historical fact); `Outcome`/
`ErrorDetail`/`LatencyMs`/`ResultMessageId` are set once, at completion, by the handler — never
mutated afterward. Only `Outcome = Success` rows increment `PromptUsageStatistics`
(spec.md Clarifications — successful executions only).

**Relationships**: Belongs to one `Prompt` and one `PromptVersion`. Has zero or one
`PromptExecutionResult` (only for `Origin = TestingWorkspace`; see below). Has zero or one
`PromptRating`. Referenced by zero or more `PromptTestCase` rows.

---

### PromptExecutionResult

The AI output and usage data for a `TestingWorkspace`-origin execution (FR-042, Key Entity
"PromptExecutionResult"). **Not created for `Origin = ConversationInsertion`** — that origin's output
already lives on the referenced `Chats.Message` (`PromptExecution.ResultMessageId`); duplicating it
here would violate DRY (constitution §2.III) and create two divergent copies of the same AI output.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `PromptExecutionId` | `Guid` | FK `PromptExecution`, unique (1:1), cascade delete. |
| `OutputText` | `string` (`nvarchar(max)`) | The AI's response (FR-040–FR-042). |
| `InputTokenCount` | `int?` | FR-042. |
| `OutputTokenCount` | `int?` | FR-042. |
| `EstimatedCostUsd` | `decimal?` | Computed via `CostEstimator.Estimate(...)` (research.md Decision 11) — `null`, never a fabricated zero, when pricing is unavailable. |
| `RagCitationsJson` | `string?` (`nvarchar(max)`) | Captured `RagCitationContext` list when RAG context was used (FR-081) — this execution's own observability copy, distinct from `Retrieval.Citation` (which is FK'd to a real `Chats.Message`, not applicable to a standalone test run). |
| `MemoryReferencesJson` | `string?` (`nvarchar(max)`) | Captured `MemoryReferenceContext` list when memory context was used (FR-082), same reasoning as above relative to `Memory.MemoryReference`. |

**Relationships**: Belongs to one `PromptExecution`.

---

### PromptRating

A manual evaluation of a `PromptExecution`'s result (FR-044, Key Entity "PromptRating").

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `PromptExecutionId` | `Guid` | FK `PromptExecution`, unique (1:1), cascade delete. |
| `RatingValue` | `enum PromptRatingValue` | `Good, NeedsImprovement, Failed` (FR-044). |
| `RatedByActor` | `string` | |

**Relationships**: Belongs to one `PromptExecution`.

---

### PromptUsageStatistics

Aggregated, successful-execution-only usage data for a `Prompt` (FR-051, spec.md Clarifications, Key
Entity "PromptUsageStatistics").

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `PromptId` | `Guid` | FK `Prompt`, unique (1:1). |
| `SuccessfulExecutionCount` | `int` | Incremented only when a `PromptExecution.Outcome = Success` is recorded (either origin). |
| `LastSuccessfulUseAtUtc` | `DateTime?` | Drives the "recently used" view ordering (FR-051). Null until the first successful execution. |

**Relationships**: Belongs to one `Prompt`.

---

### PromptAuditLog

An immutable record of security- and lifecycle-relevant actions on a `Prompt` (FR-090, Key Entity
"PromptAuditLog") — mirrors `KnowledgeBaseAuditLog`/`MemoryAuditLog`'s established convention.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `PromptId` | `Guid` | Indexed. **No cascade FK** — must survive a hard-purged prompt, matching `DocumentAuditLog`/`MemoryAuditLog`'s established pattern. |
| `Action` | `enum PromptAuditAction` | `Created, Updated, Deleted, Archived, Restored, Duplicated, VersionRestored, Exported, Imported` (FR-090). |
| `ActorId` | `string` | |
| `DetailsJson` | `string?` (`nvarchar(2000)`) | Sanitized — never raw prompt content (FR-091), matching `MemoryAuditLog`'s "sanitized `DetailsJson`, never raw/decrypted content" convention. |

**Relationships**: References a `Prompt` by id only (no navigation, no cascade).

---

## Explicitly Not Modeled (this release)

- **`PromptPermission` / `PromptShare` / `PromptEvaluation`** — spec.md's own Key Entities section
  marks these "data model only, not enforced/implemented" (FR-061). No table is created for any of
  them in this release: `Prompt.OwnerId` (a single string column) is the entire access-control model
  today, and adding `PromptPermission`(`PromptId`, `GranteeId`, `Role`) or
  `PromptShare`(`PromptId`, `ScopeType`, `ScopeId`) later is a purely additive migration — it does not
  require restructuring `Prompt` or any entity above. An empty table with no code path ever writing to
  it is dead weight, not genuine future-readiness (constitution §2.III YAGNI) — FR-061 is satisfied by
  the schema *not precluding* the addition, not by pre-building unused tables.
- **A separate `PromptExample` entity** — `Prompt.ExamplesText`/`PromptVersion.ExamplesText` store the
  "Examples" structural component (FR-002) as a single text/JSON blob. Spec.md never requires querying,
  filtering, or independently versioning individual examples — a normalized child table would add
  relational overhead with no corresponding requirement.
- **A deduplicated master `Tag` catalog** — see `PromptTag` above / research.md Decision 6; a tag
  carries no attributes beyond its text.

## Indexes (beyond the ones noted per-entity above)

- `Prompt`: filtered unique index `(OwnerId, Name)` `WHERE IsDeleted = 0` (research.md Decision 7).
  `FULLTEXT INDEX` on `(Name, Description, SystemInstructions, UserInstructions)` (research.md
  Decision 12, FR-052). Non-unique indexes on `FolderId`, `CategoryId`, `Status`, `IsFavorite`,
  `IsPinned` (list/filter query paths, FR-052/FR-053).
- `PromptVersion`: unique index `(PromptId, VersionNumber)`.
- `PromptVariable`: unique index `(PromptVersionId, Name)`.
- `PromptTag`: index `(OwnerId, Value)` (owner-scoped distinct-tag query, mirrors
  `KnowledgeBaseTagConfiguration`), index `(PromptId)`.
- `PromptExecution`: index `(PromptId, CreatedAtUtc)` (execution history, "recently used" support).
- `PromptUsageStatistics`: unique index `(PromptId)`.
