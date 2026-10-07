# DATABASE.md

> **Project:** Ask Lucy AI Workspace
>
> **Database Engine:** Microsoft SQL Server
>
> **ORM:** Entity Framework Core (Code First)
>
> **Architecture:** Clean Architecture + Modular Monolith
>
> **Version:** 2.0
>
> **Last Updated:** October 2026 (added the notification hub tables, specs/067)

---

# 1. Database Philosophy

The database is designed to support an enterprise AI platform rather than a single chatbot.

Goals:

* Highly normalized
* Provider-independent
* Future-proof
* Multi-tenant ready
* Optimized for AI workloads
* SQL Server vector support
* Easy migration to microservices

Use Entity Framework Core Code-First Migrations as the single source of truth.

---

# 2. Database Contexts

The database is organized into logical bounded contexts.

```text
Identity

AI

Conversations

Knowledge

Memory

Agents

Files

Payments

Administration

Audit

Configuration
```

Schemas may be introduced later (Identity, AI, Billing, etc.), but initially a single schema (`dbo`) keeps deployment simple.

---

# 3. Key Conventions

Primary Key

```text
Id UNIQUEIDENTIFIER
```

Generated using sequential GUIDs.

Every business table includes:

```text
Id

CreatedAtUtc

CreatedBy

ModifiedAtUtc

ModifiedBy

DeletedAtUtc

DeletedBy

RowVersion
```

Soft delete is used unless permanent deletion is legally required.

---

# 4. Identity Context

Uses ASP.NET Identity.

Additional profile information extends the Identity user.

## Users

Stores:

* Profile
* Display name
* Avatar
* Preferred language
* Theme
* Time zone
* Default AI provider
* Default AI model

---

## RefreshTokens

Stores:

* User
* JWT family
* Expiration
* Revocation
* Rotation history

---

## PasswordResetTokens

specs/058-password-recovery. Stores:

* User
* SHA-256 hash of the token (never the token itself)
* The email address the link was issued to
* Issue, expiry, consumption and supersession timestamps
* Requesting IP

The plaintext token exists only in the email that carries it, so reading this table yields nothing
redeemable. A token is single-use, expires in one hour, and is superseded by a newer request, by a
password change or by an email change. Spent rows are deleted after 90 days by the
`password-reset-token-cleanup` recurring job.

---

## ExternalLogins

Google

Microsoft

Facebook

GitHub

---

## TwoFactorDevices

Authenticator App

Recovery Codes

Trusted Devices

---

## UserSessions

Tracks:

* Browser
* Device
* IP
* Last Activity
* Refresh Token

---

# 5. AI Context

## AIProviders

Examples:

OpenAI

Anthropic

Gemini

OpenRouter

Azure OpenAI

Ollama

Contains:

* Name
* Display Name
* Status
* Capabilities
* Configuration

---

## AIModels

Stores every supported model.

Fields:

* ProviderId
* ModelName
* DisplayName
* SupportsVision
* SupportsImages
* SupportsStreaming
* SupportsReasoning
* SupportsFunctions
* ContextWindow
* MaxOutputTokens

Models are data—not hardcoded.

---

## VoiceProviders

The text-to-speech engines Lucy's voice runs on, in failover order (specs/070). Separate from
`AIProviders`: a voice engine has no model catalog or health history, and is ordered rather
than enabled/disabled.

Fields:

* ProviderKey (unique; matches a registered `ITextToSpeechEngine`, e.g. `ElevenLabs`, `Supertonic`)
* DisplayName
* Priority (0 = Lucy's voice; the rest are tried in ascending order; non-unique index)
* DefaultVoiceId (max 100)
* CredentialCiphertext / CredentialHint / CredentialLastRotatedAtUtc (same Data Protection scheme as `AIProviders`; null for an on-server engine)

The `AddVoiceProviders` migration seeds one row — ElevenLabs at priority 0 with no stored
credential, so the configured `ElevenLabs:ApiKey` keeps working unchanged until an
administrator adds another provider and makes it primary.

---

## CustomModels

Hugging Face repositories deployed to the production host from Admin → Custom models (specs/072).
Soft-deleted (`DeletedAtUtc`, global query filter); `RowVersion` guards concurrent changes.

Fields:

* Name (max 100, case-insensitive collation), RepositoryId (case-insensitive), Revision, ResolvedCommitSha (fixed 40), SourceUrl
* Destination (relative to the target's root path, case-insensitive; the root path itself is never stored)
* DeploymentState / Availability / FailureKind (stored as **strings**), FailureReason
* IsInProgress (true while Queued, Listing or Transferring)
* TotalBytes, TransferredBytes, file counts, CurrentFilePath — the last persisted progress
* SubmittedByUserId, CancelledByUserId, BackgroundJobId

Indexes:

* `Name` — unique, filtered `[DeletedAtUtc] IS NULL`, so a removed model frees its name
* `RepositoryId` — unique, filtered `[Availability] = 'Available' AND [DeletedAtUtc] IS NULL`: at most one Available model per repository
* `Destination` — unique, filtered `[IsInProgress] = 1`: two deployments can't write the same folder at once
* `(RepositoryId, DeploymentState)` and `CreatedAtUtc` — lookups and list ordering

No credential, host or root path is stored in this table.

---

## CustomModelOverwrittenFiles

One row per file a deployment replaced on the target (specs/072 FR-041): CustomModelId (FK,
cascade delete, indexed), RelativePath, PreviousSizeBytes, OverwrittenAtUtc. Identity key.

Both tables are created by the `AddCustomModels` migration.

---

## AiCapabilityAssignments

Which provider — and optionally which exact model — serves each `AiCapability` (chat, embeddings,
transcription, image generation, and so on). `Capability` is unique, filtered on `DeletedAtUtc IS NULL`
so a soft-deleted row never blocks a fresh assignment. Stored as a **string**, never an ordinal: an
ordinal silently remaps if the enum is ever reordered, and this column decides which provider serves
a capability.

`ModelId` (nullable, added 2026-09-18) pins one specific model. Null means "follow the provider's
default", which is what every capability except image generation does. `ImageGeneration` requires it
and requires that model to have `SupportsImageOutput` — there is deliberately **no** fallback, because
asking a chat model to draw is a broken request, not a degraded one; an unassigned capability raises
`AiCapabilityNotConfiguredException` (HTTP 503) rather than quietly picking something.

Both the provider and model FKs are `Restrict`, not `Cascade`: retiring a provider or model that a
capability still points at must be a deliberate administrator decision, never a silent unassignment.

---

## SiteAnalyses / SiteAnalysisResults

`SiteAnalyses` is one run of the Site Analysis Agent (specs/057): `UserId`, `UserChatId`, `SiteName`,
`Latitude`/`Longitude`, optional `BoundaryGeoJson`, `Status`, `WorkflowExecutionId`,
`ExpectedResultCount`, `ClosingOutcomeReportedAtUtc`, `StartedAtUtc`/`CompletedAtUtc`. Indexed on
`(UserId, UserChatId, StartedAtUtc)` — the shape the chat rehydration query uses. Cascade-deletes with
its user.

`SiteAnalysisResults` is one specialist's finding, cascade-deleted with its analysis and **unique on
`(SiteAnalysisId, AnalysisType)`** — a specialist reports exactly once per analysis. Holds
`ContentJson` (the panel content blocks), `DataSource`, `ConfidenceLevel`, an optional `DocumentId`
for image-bearing results, and `FailureReason`.

Results exist as rows, rather than only as live SignalR pushes, so that navigating away mid-analysis
or reloading does not lose findings that arrived while the user was elsewhere. Failed and rejected
specialists are persisted too, with their `FailureReason` — the user is not shown a message for them,
but the failure is recorded and retrievable rather than discarded.

`Status` and every enum column here are stored as strings, for the same reason as above.

---

## UserAISettings

Stores per-user preferences.

Fields:

* Provider
* Model
* Temperature
* TopP
* FrequencyPenalty
* PresencePenalty
* MaxTokens
* StreamingEnabled
* SystemPrompt

---

## AIUsage

Tracks every AI request.

Stores:

* User
* Conversation
* Provider
* Model
* Input Tokens
* Output Tokens
* Cached Tokens
* Processing Time
* Estimated Cost
* Success
* Error

---

# 6. Conversation Context

> **Shipped in SPEC-002** (`specs/002-chat-history-management`), extending the `UserChats`/
> `Messages` tables SPEC-000 migrated onto the standard entity conventions. The entity type
> names remain `UserChat`/`Message` in code (research.md Topic 1 — extending the existing
> aggregate rather than introducing a parallel `Conversation` rename); this section uses
> "Conversation" only as the business-facing term. Fields below are what actually shipped —
> narrower than this document's original pre-implementation sketch (System Prompt/Temperature
> at the conversation level, System/Tool message roles, and `ConversationTags` were **not**
> built; see Assumptions/Out-of-scope in `specs/002-chat-history-management/spec.md`).

## Conversations (`UserChats` table)

Stores:

* Owner (`UserId`)
* Title, plus `IsTitleManuallySet` (freezes auto-title generation once a user renames it)
* `ArchivedAtUtc` (nullable — archived state)
* `PinnedAtUtc` (nullable — pinned state; also the pin-first sort key)
* `IsFavorite`
* Standard audit columns (`CreatedAtUtc`/`CreatedBy`, `ModifiedAtUtc`/`ModifiedBy`,
  `DeletedAtUtc`/`DeletedBy` — soft delete doubles as the "Recently Deleted"/Trash state),
  `RowVersion` (optimistic concurrency)

Provider/model/system-prompt/temperature are **not** stored at the conversation level —
each message records the provider/model/parameters that actually produced it (below),
since a single conversation is not pinned to one model choice.

---

## Messages

Stores:

* Conversation (`UserChatId`)
* Role — **User** or **Assistant** only (System/Tool roles are not used by this feature)
* Kind — Text, Image, or Translation (determines how `Content` is rendered)
* Content, `SourceText` (the original prompt behind an Image/Translation-kind reply)
* `Provider`, `Model` — the AI provider/model that produced this message (assistant messages only)
* `GenerationParametersJson` — opaque JSON (shape varies by provider/model), not fixed columns
* `InputTokenCount`, `OutputTokenCount` — null until the AI provider abstraction surfaces
  real usage stats (not fabricated in the meantime)
* `TurnOutcomeJson` — what the turn that produced this message actually *did* (specs/068):
  a verdict plus one entry per attempted action, each with its own success flag and failure
  reason. Nullable; JSON rather than columns because the attempt list is variable-length and
  read as a whole. Server-resolved capability arguments are deliberately **not** stored here —
  they are already in the agent execution records, and this payload is returned to the client.
* Standard audit columns, `RowVersion`

Messages are immutable/append-only once created — no update path exists.

**Migration note — `20260924115936_AddMessageRecordedTurnOutcome`.** Additive and reversible:
one nullable `NVARCHAR(MAX)` column on `Messages`, no index, no backfill. Existing messages keep
`NULL`, which the application reads as *outcome unknown* — never as success — so no historical
message gains an implied verdict it never had. Deploy order does not matter: an older binary
ignores the column, and a newer one treats every pre-existing row as unknown.

---

## Attachments

A file reference (not the file's bytes) associated with a message — `FileName`,
`ContentType`, `AccessLocation` (the existing signed-URL/storage reference the file is
already served from). Persists references produced by existing capabilities (uploads,
generated images); does not introduce new upload/storage capability. Child of `Message`'s
aggregate — no top-level `DbSet`, reachable only via `Message.Attachments`.

## Citations

A source reference associated with an assistant message — `SourceLabel`,
`SourceReference` (nullable URL/identifier). Same aggregate-child shape as Attachments.

---

## Full-text search

`UserChats.Title` and `Messages.Content` participate in a SQL Server full-text catalog
(`ConversationSearchCatalog`), populated asynchronously (`CHANGE_TRACKING AUTO`) — this is
what backs conversation search (title + message content) without a separate search engine.

## Not implemented (reserved for a future spec)

`ConversationTags` (chat categorization) and `ConversationParticipant`/sharing were
explicitly out of scope for SPEC-002 — see that spec's Assumptions section.

---

# 7. Knowledge Context

> **Shipped in SPEC-014** (`specs/014-knowledge-base-management`) — organization/lifecycle
> only, narrower than this section's original pre-implementation sketch. Embedding generation,
> vector storage, and RAG retrieval (`DocumentChunks`, `Embeddings`, semantic search) are
> explicitly **out of scope** and reserved for a future spec (data-model.md's "Explicitly Not
> Modeled" section); this feature stores and organizes documents, it does not index their
> content. `KnowledgeBaseMembers`/team-sharing is likewise **not** built — every knowledge
> base is private to its owner in this release (`Visibility` is a single fixed value, not yet
> a real access-control dimension).

## KnowledgeBases

Stores:

* `OwnerId` — sole owner; no sharing in this release
* `Name`, `Description`, `Color`, `Icon` — display/branding
* `Status` — `Draft` / `Active` / `Archived` (no `Deleted` status value; soft delete is a
  separate `DeletedAtUtc` flag, orthogonal to `Status`, so a knowledge base can be
  soft-deleted from any status)
* `CategoryId` (nullable FK to `KnowledgeBaseCategories`) — `null` renders as "Uncategorized"
* `Notes` — free-form owner notes
* `IsFavorite` (bool), `PinnedAtUtc` (nullable — pinned state; also the pin-first sort key,
  same shape as `UserChats.PinnedAtUtc`)
* Cached statistics, updated incrementally on document add/remove (not recomputed per-read):
  `DocumentCount`, `TotalPageCount`, `StorageSizeBytes`
* `PurgeScheduledAtUtc` (nullable) — set to +30 days on soft delete (FR-036); cleared on
  restore; read by `KnowledgeBasePurgeHostedService`'s periodic sweep
* Standard audit columns (`CreatedAtUtc`/`CreatedBy`, `ModifiedAtUtc`/`ModifiedBy`,
  `DeletedAtUtc`/`DeletedBy`), `RowVersion` (optimistic concurrency)

---

## KnowledgeBaseFolders

A node in one knowledge base's folder hierarchy. Stores `KnowledgeBaseId`,
`ParentFolderId` (nullable — null means root), `Name`, `Depth` (computed and stored at
create/move time, not recomputed per-read, so the max-nesting-depth check — 10 by default,
configurable — is a cheap comparison). Standard audit columns.

## KnowledgeBaseDocuments

Associates one uploaded file (via the existing `IFileStorage` abstraction) with exactly one
knowledge base and at most one folder. Stores `KnowledgeBaseId`, `FolderId` (nullable — null
means the knowledge base's root), `FileName` (original/display name), `StoredFileName` (the
opaque storage reference — never exposed to clients), `ContentType`, `SizeBytes`, `PageCount`
(nullable — null for non-paginated types like `.csv`/`.md`/`.txt`, or when extraction failed
for a paginated type), `ProcessingStatus` (`Uploaded`/`Ready`/`Failed` — this feature's own
lightweight post-upload work, not a RAG-ingestion status), `UploadedAtUtc`. Standard audit
columns.

## KnowledgeBaseTags

A free-form, reusable, owner-scoped label. Has its own `DbSet`/query filter (unlike
`Attachments`/`Citations`, which are pure aggregate children) because tag-filter/autocomplete
queries need to search across a user's knowledge bases, not just within one — the same reason
`Messages` has its own repository separate from `UserChats`. Stores `KnowledgeBaseId`,
`OwnerId`, `Value`.

## KnowledgeBaseCategories

A classification value, predefined-and-shared or custom-and-private. `OwnerId` (nullable) is
the sole discriminator: `null` means predefined and shared platform-wide (8 categories seeded
by migration `AddKnowledgeBaseManagement`); non-null means custom and private to that owner,
never visible to another user. Deleting a custom category clears `CategoryId` to `null`
(Uncategorized) on every knowledge base that referenced it, in the same transaction — never
leaves a dangling reference. Stores `OwnerId` (nullable), `Name`. Standard audit columns
(soft-deletable, though only ever hard-removed via the owner-triggered delete flow above).

## KnowledgeBaseAuditLogs

Append-only, immutable record of lifecycle-relevant actions (`Created`/`Edited`/`Archived`/
`Restored`/`Deleted`/`PermanentlyDeleted`/`Duplicated`) — folder/document-level events are
deliberately not separately audited (YAGNI; no requirement calls for it). Not FK'd to
`KnowledgeBaseId` with a hard/cascading foreign key — an audit entry for a permanently purged
knowledge base is deliberately retained. Stores `KnowledgeBaseId`, `UserId`, `Action`,
`OccurredAtUtc`, `DetailsJson` (a short, sanitized summary — never raw content or a secret).

## Not implemented (reserved for a future RAG spec)

`DocumentChunks`/`Embeddings`/vector storage, and `KnowledgeBaseMembers`/team-sharing — see
this section's intro note above and `specs/014-knowledge-base-management/data-model.md`'s
"Explicitly Not Modeled" section for the full rationale.

---

# 8. Document Intelligence Context

> **Shipped in SPEC-015** (`specs/015-document-intelligence-pipeline`) — a user's general
> document library and its automated processing pipeline. A deliberately separate bounded
> context from §7's `KnowledgeBaseDocuments` (research.md Decision 1): this document's
> lifecycle (OCR, versioning, classification) has no relationship to knowledge-base
> membership, and `DocumentFileType` here is its own enum, not `KnowledgeBaseDocumentType`.

## Documents

The aggregate root. Stores `OwnerId` (sole owner, no sharing), `FolderId` (nullable FK to
`DocumentFolders`), `FileName`, `FileType`, `SizeBytes`, `CurrentVersionId` (no DB-enforced
FK — the first `DocumentVersion` must reference this row's id before it exists, so this is
an application-level ordering concern only, resolved by the caller generating the id
upfront), `ProcessingStatus` (`Uploaded`/`Queued`/`Processing`/`Completed`/`Failed`),
`ArchivedAtUtc` (nullable — independent of `ProcessingStatus` and of the standard
`DeletedAtUtc` soft-delete column; a document can be archived without losing its processing
outcome). Standard audit columns, `RowVersion` (optimistic concurrency).

## DocumentVersions

One immutable content revision per row (US5). Holds the version label, size, file type, and
the storage reference for that specific revision; restoring an old version repoints
`Documents.CurrentVersionId` without deleting any version row, so history is never lost.

## DocumentChecksums

SHA-256 content hash per version, driving duplicate-upload detection — a matching checksum
lets the upload flow offer "save as a new version of this existing document" instead of
silently creating a duplicate.

## DocumentFolders

A node in one owner's folder hierarchy. Self-referencing `ParentFolderId` (nullable — null
means root), `Name`, `Depth` (computed and stored at create/move time, same pattern as
`KnowledgeBaseFolders.Depth`).

## DocumentMetadata / DocumentLanguage / DocumentClassification / DocumentTag

Extracted or user-edited descriptive data. `DocumentMetadata` (title/author/dates/keywords)
and `DocumentClassification` (category) both carry an "auto-extracted vs. user-edited" flag —
automatic reprocessing after a new version is upserted idempotently and never overwrites a
field the user already edited manually. `DocumentLanguage` supports multiple detected
languages per document with a Primary/Secondary role. `DocumentTag` is free-form and
reusable across a user's documents, same shape as `KnowledgeBaseTags`.

## DocumentProcessingJob / DocumentProcessingStage / DocumentProcessingLog

`DocumentProcessingJob` is one Hangfire-durable pipeline run; `DocumentProcessingStage`
records each stage's (`Validation`/`Ocr`/`TextExtraction`/`MetadataExtraction`/
`Classification`/`LanguageDetection`/`PreviewGeneration`) individual outcome
(`Completed`/`Failed`/`Skipped` — skipped is not a failure); `DocumentProcessingLog` is an
append-only event trail surfaced as the document's processing history. Live dashboard counts
are computed from only the latest job per document, so a document that failed and later
succeeded on retry is never double-counted.

## DocumentPreview

The generated preview artifact reference (page image, thumbnail, or extracted structured-
content JSON) for a version, keyed by `DocumentPreviewType`. Never stores a physical path
directly reachable by a client — delivery goes through `ISignedUrlService`.

## DocumentNotification

An in-app notification (`UploadCompleted`/`ProcessingCompleted`/`ProcessingFailed`/
`OcrFailed`/`VersionCreated`/`StorageLimitReached`), cursor-paginated like `Documents` itself,
pushed live over SignalR with REST as the reconciliation fallback.

## DocumentStatistics

Per-owner (and organization-wide, admin-only) aggregate counters — total documents, storage
bytes, average processing duration, file-type/language distributions — recomputed
incrementally by a Hangfire recurring job rather than aggregated per-read.

## DocumentAuditLog

Append-only lifecycle audit trail, same rationale as `KnowledgeBaseAuditLogs` (§7).

## DocumentCategory

Classification value, predefined-and-shared or custom-and-private — same `OwnerId`-nullable
discriminator pattern as `KnowledgeBaseCategories` (§7).

## Not implemented (reserved for a future RAG spec)

Embedding generation and vector storage for this document library — this feature stores,
organizes, and extracts text from documents, it does not index their content for semantic
search (that remains scoped to the Knowledge Base Engine's own future RAG spec, §7).

---

# 9. Memory Context

## UserMemories

Long-term AI memory.

Examples:

Preferred writing style

Favorite programming language

Company name

Frequently used prompts

---

## ConversationMemory

Stores temporary summarized context for long conversations.

Reduces token usage.

---

# 10. Prompt Library

## PromptCategories

Examples:

Writing

Coding

Translation

Marketing

Research

---

## PromptTemplates

Stores reusable prompts.

Supports:

Variables

Markdown

Versioning

Favorites

---

# 11. Agent Context

Shipped in specs/020-ai-agent-framework as 16 tables (`AskLucyDbContext`,
`Configurations/Agents/*.cs`) across four aggregates.

## Agents / AgentVersions / AgentTools / AgentKnowledgeBases / AgentMemoryPolicies

`Agents` is the mutable draft (Name, Description, Instructions, ModelProviderId/ModelId,
ExecutionPolicy, Status, PublishedVersionNumber). `AgentVersions` is an immutable snapshot
created on publish (`AgentId`+`VersionNumber` unique) — the draft-configuration tables
(`AgentTools`, `AgentKnowledgeBases`, `AgentMemoryPolicies`) are serialized into it at publish
time, never referenced live by an execution.

---

## AgentExecutions / AgentExecutionSteps / AgentToolCalls / AgentApprovals /
## AgentExecutionErrors / AgentExecutionEvents / AgentExecutionUsage / AgentExecutionCost

`AgentExecutions` is one run, always referencing the exact `AgentVersionId` it started under
(never the agent's current draft). `AgentExecutionSteps` (one plan step) owns `AgentToolCalls`
(one tool invocation) and can carry an `AgentApprovals` row when a High/Critical-risk tool call
paused for a decision. `AgentExecutionEvents` is the append-only, safe-metadata-only push/replay
log (`AgentExecutionId`+`OccurredAtUtc` indexed). `AgentExecutionUsage`/`AgentExecutionCost` are
1:1 accumulator rows (tokens, tool-call/step counts, estimated cost).

---

## AgentPolicies / AgentUserExecutionLimits

Administrator-managed: `AgentPolicies` auto-approves a High/Critical-risk tool call matching
`ToolName`+`ConditionsJson` (composite-indexed together, since they're always filtered
together). `AgentUserExecutionLimits` (`UserId` unique) overrides the system-wide concurrent-
execution cap per user.

---

## AgentAuditLogs

Tamper-resistant security record — deliberately **not** a hard FK to `AgentExecutions` (so an
entry for a later-purged execution survives), mirroring `KnowledgeBaseAuditLogs`/
`MemoryAuditLog`. Distinct from `AgentExecutionEvents`: this is the security-audit trail
(`PermissionChecked`/`PermissionDenied`/`ApprovalDecided`/`CrossUserAccessAttempted`/
`ExecutionCompleted`/`ExecutionFailed`), that's the operational one.

---

# 12. MCP Context

Shipped in specs/021-mcp-integration as 8 tables (`AskLucyDbContext`, `Configurations/Mcp/*.cs`).
No `MCPExecutions` table was built — an MCP tool call reuses spec 020's existing `AgentToolCalls`/
`AgentExecutionSteps` unmodified; a 4-column `ToolName` width increase (`nvarchar(100)` →
`nvarchar(400)`) across `AgentTools`/`AgentToolCalls`/`AgentPolicies`/`AgentExecutionSteps` is the
only touch to any spec 020 table, needed for MCP's longer `mcp:{serverId}:{toolName}` identifiers.

## McpServers

`(Endpoint, Transport)` unique platform-wide. Stores `Name`, `Description`, `Endpoint`,
`Transport`, `AuthenticationType`, `IsEnabled`, `OwnerUserId`, `ConfigurationVersion`,
`CapabilityRefreshIntervalMinutes`, `LastHealthCheckAtUtc`, `LastCapabilityDiscoveryAtUtc`, plus
the SSRF-override/insecure-transport justification fields. `Endpoint` capped at `nvarchar(400)` to
keep the unique composite index under SQL Server's 900-byte nonclustered key limit.

---

## McpServerCredentials / McpServerHealths / McpCapabilitySnapshots

`McpServerCredentials.McpServerId` unique (1:1, cascade-deletes with its server) — stores only
`CiphertextBlob` (Data Protection-encrypted), never plaintext. `McpServerHealths.McpServerId`
unique — one current row per server, overwritten on every check, not an unbounded history table.
`McpCapabilitySnapshots` is append-only, `(McpServerId, SnapshotVersion)` unique, restricted
(not cascade) delete against its server.

---

## McpTools / McpResources / McpPrompts

Every discovered tool/resource/prompt, each `NamespacedName`-unique (`nvarchar(400)`, capped for
the same 900-byte index-key reason as `McpServers.Endpoint`). `McpTools` additionally indexes
`(McpServerId, ActivationStatus, IsAvailable)` — the exact filter `IMcpToolRegistry.ActiveTools`
queries on every rebuild. `McpPrompts` rows are mutated in place on refresh (never a new row per
snapshot, unlike `McpTools`/`McpResources`) — the read-only-mirror design a duplicated native
`Prompt` diffs against.

---

## McpAuditLogs

Administrative/security events only (server lifecycle, credential rotation, health transitions,
capability-discovery runs, tool activation) — not a hard FK to `McpServers` (mirrors
`AgentAuditLogs`' pattern), and deliberately never duplicates `AgentToolCalls`' per-execution
tool-call activity.

---

# 13. File Context

## Files

Stores:

* Owner
* Original Name
* Stored Name
* Content Type
* Size
* SHA256 Hash
* Storage Provider

---

## SignedDownloads

Stores temporary signed URLs.

---

# 14. Payment Context

## SubscriptionPlans

Examples:

Free

Professional

Enterprise

---

## UserSubscriptions

Stores:

* Current Plan
* Renewal Date
* Status

---

## PaymentTransactions

Supports:

PayPal

Future:

Stripe

---

## UsageLimits

Tracks:

Tokens

Storage

Knowledge Bases

Agents

Uploads

---

# 15. Administration Context

## FeatureFlags

Enable features without redeployment.

---

## SystemSettings

Global configuration.

Examples:

Maximum Upload Size

Allowed File Types

SMTP Configuration

Maintenance Mode

---

## Announcements

Platform messages.

---

## PresenceSphereSettings

specs/080 - the workspace-wide look of the presence sphere shown in the chat. Exactly one row, keyed by a fixed id; with no row, the defaults apply, so a fresh deployment looks as it always did. The first save creates the row.

Fields:

* DotSizeMultiplier - `decimal(4,2)`, 0.25 to 2.00, default 1.00 (1.00 is the size the dots had before this was adjustable)
* CardFillPercent - `int`, 40 to 95, default 75 (the sphere's diameter as a percentage of its card's height)
* ZoomEnabled - `bit`, default off (when on, users can zoom between a quarter of the sphere's size and twice it)
* Standard audit columns - `ModifiedBy` and `ModifiedAtUtc` are shown on the Admin Appearance page as "last changed by/at"

The limits live once, on the `PresenceSphereSettings` entity, and are enforced by its `Update` method and by the command validator. Last write wins (no concurrency check): a decorative setting does not justify a retry flow. Migration `AddPresenceSphereSettings` creates the table and seeds nothing; apply it by hand to the shared test databases (CI fails without it on the dedicated persistence database).

---

## Notification hub (specs/067)

Nine tables and one column. Every table inherits `BaseEntity` (Guid v7 key, audit columns, `RowVersion`). Enums are stored as `nvarchar` strings, every foreign key is indexed, and all mapping is Fluent API in `AskLucy.Persistence/Configurations/Notifications/` (plus `Configurations/Localization/`). The reasoning is in [ADR 0018](adr/0018-transactional-notification-outbox.md); the full field lists are in `specs/067-notifications-communication-hub/data-model.md`.

### Notifications

One message for one recipient about one event. `RecipientUserId` is a nullable FK to `AspNetUsers` (null only for the support mailbox and address-only account emails). Holds the rendered plain-text `Title` and `Message`, `Category`, `Type`, `Priority`, the aggregated `Status`, the `Language` it was produced in, the related item (`RelatedItemType`/`RelatedItemId`), an app-relative `ActionRoute` and `ActionLabel`, `MetadataJson` (non-sensitive, 4 KB at most), `CorrelationId`, `EventKey`, `ShowInCenter`, `ReadAtUtc`, `ExpiresAtUtc`, and `TemplateVersionId` and `SourceEventId` (both nullable FKs; `SourceEventId` is `ON DELETE SET NULL`). Owner deletion is a soft delete with a global query filter; retention does the hard delete.

Indexes:

* `IX_Notifications_Recipient_Center` on `(RecipientUserId, CreatedAtUtc DESC, Id DESC)`, filtered `DeletedAtUtc IS NULL AND ShowInCenter = 1`, covering the category, read time, priority and status - keyset paging of the notification center.
* `IX_Notifications_Recipient_Unread` on `(RecipientUserId)`, filtered to unread, undeleted, in-center rows - the unread badge count.
* `UX_Notifications_Recipient_EventKey` (unique, `EventKey IS NOT NULL AND RecipientUserId IS NOT NULL`) and `UX_Notifications_Address_EventKey` (unique, `EventKey IS NOT NULL AND RecipientUserId IS NULL`) - de-duplication, so replaying an event materializes once.
* `IX_Notifications_Retention_Read` (filtered `ReadAtUtc IS NOT NULL`) and `IX_Notifications_Retention_Deleted` (filtered `DeletedAtUtc IS NOT NULL`) - the retention scans.
* `IX_Notifications_TemplateVersionId`, `IX_Notifications_SourceEventId` - the foreign keys.

### NotificationDeliveries

One channel's delivery stream for one notification; the row `Id` is the delivery identity and is used in the email `Message-ID`. Cascades from `Notifications`. Holds `Channel`, `Status`, denormalized `Priority`, `RecipientKind`, `RecipientAddress` (only for explicit-address recipients, never for the support mailbox; masked in every admin view), the `Language` and `TemplateVersionId` actually rendered, `AttemptCount`/`MaxAttempts`, `NextAttemptAtUtc`, `LastAttemptAtUtc`, the lease (`LeaseOwner`, `LeaseExpiresAtUtc`), `SkipReason`, `FailureKind`, `FailureReason` and `ProviderResponse` (safe text only, never a token, link or credential), `SentAtUtc`, `DeliveredAtUtc`, `ExpiresAtUtc` and `CorrelationId`.

Indexes:

* `IX_NotificationDeliveries_Queue` on `(Status, Priority DESC, NextAttemptAtUtc)`, filtered to `Pending`, `Retrying` and `Sending` - the worker's claim scan and the lease sweeper.
* `IX_NotificationDeliveries_Failed` on `(Status, LastAttemptAtUtc DESC)`, filtered to `Failed` and `DeadLettered` - the admin failed view.
* `UX_NotificationDeliveries_Notification_Channel` (unique on `(NotificationId, Channel)`) - one delivery per channel per notification; also serves the `NotificationId` foreign key.
* `IX_NotificationDeliveries_TemplateVersionId` - the foreign key.

### NotificationOutboxEvents

The transactional outbox: a durable "something happened" record written in the emitter's own transaction. Holds `Type`, `EventKey` (not unique here, so a duplicate can never roll back the emitter), `RecipientJson` and `VariablesJson` (declared variables only, 16 KB at most), the related item, `ExplicitLanguage`, `CorrelationId`, `OccurredAtUtc`, `Status`, `Attempts`, the lease, `NextAttemptAtUtc`, `FanOutCursor` (the last user id of a batched announcement fan-out), `Outcome`, `LastError` (a safe summary) and `ProcessedAtUtc`. There is no soft-delete filter.

Indexes: `IX_NotificationOutboxEvents_Due` on `(Status, NextAttemptAtUtc, OccurredAtUtc)` filtered to `Pending` and `Processing` (the dispatcher's claim scan); `IX_NotificationOutboxEvents_Processed` on `(ProcessedAtUtc)` filtered to `Completed` (retention); `IX_NotificationOutboxEvents_EventKey` filtered to non-null keys.

### NotificationTemplates / NotificationTemplateVersions

One template per `(Type, Channel, Language)` (`UX_NotificationTemplates_Type_Channel_Language`, unique), with a nullable `PublishedVersionId` pointing at the current published version (nullable to break the cycle). Versions are immutable once published and hold the structured fields: for email `Subject`, `Preheader`, `Greeting`, `Heading`, `BodyParagraphsJson` (1 to 10 paragraphs), `SafetyNote`, `FooterNote`; for in-app `Title`, `Message`; `ActionLabel` for both; `UsedVariablesJson` (computed on save and checked against the type's declared variables); and the publish and archive stamps. Version statuses are `Draft`, `Published` and `Archived`.

Indexes: `UX_NotificationTemplateVersions_Template_Version` (unique on `(TemplateId, VersionNumber)`), `UX_NotificationTemplateVersions_OnePublished` (unique on `(TemplateId)` filtered to `Published`, so a template can never have two), and `IX_NotificationTemplates_PublishedVersionId`.

### NotificationPreferences

Sparse per-user overrides of the catalogue defaults, one row per `(UserId, Category, Channel)` with `IsEnabled` and `Frequency` (only `Immediate` is accepted). `UserId` is an FK to `AspNetUsers` with cascade delete. `UX_NotificationPreferences_User_Category_Channel` is unique. The Domain and the validator reject disabling a mandatory pair.

### SystemAnnouncements

Administrator announcements: `Kind`, `Title` (150) and `Message` (2,000) as plain text that is never translated, `Audience` (all active users or roles), `TargetRoleIdsJson`, `IsCritical` (enables email), `EndsAtUtc` (the expiry of the fan-out notifications and emails), `PublishedAtUtc`, `PublishedByUserId` (FK) and `RecipientCount`, set when fan-out completes. Immutable once published; a correction is a new announcement. Indexes: `IX_SystemAnnouncements_PublishedAt` and `IX_SystemAnnouncements_PublishedByUserId`.

### NotificationAuditLogs

Append-only audit of administrator actions and of approval-notification history, mirroring the role audit log. It has no update or delete path, and the soft-delete filter is not used. Holds `OccurredAtUtc`, `ActorUserId` (a plain string, not a foreign key, so the trail survives user deletion; null for system rows), `Action`, `TargetType`, `TargetId`, `Outcome` (`Succeeded`, `Rejected`, `Failed`), `DetailsJson` (a safe before/after summary, never a token, credential or rendered email body) and `CorrelationId`. Indexes: `IX_NotificationAuditLogs_OccurredAt` and `IX_NotificationAuditLogs_Target` on `(TargetType, TargetId)`. Retention never deletes these rows. Read-only admin views are audited as `...Viewed` rows at most once per administrator per resource per hour.

### LocalizationSettings and AspNetUsers.PreferredLanguage

`LocalizationSettings` is a singleton in `AskLucy.Domain.Localization`: `IsEnabled` (default false), `SupportedLanguagesJson` (`nvarchar(200)`, must contain `en`) and `RowVersion` for optimistic concurrency (a conflict returns 409). `AspNetUsers.PreferredLanguage` is `nvarchar(10)` and nullable; null means none chosen. It is kept when localization is disabled or the language stops being supported.

### Retention windows

The daily Hangfire job `notification-retention` (03:00, `RetentionService`) deletes in batches of 1,000 with `ExecuteDeleteAsync`. Windows are `Notifications:Retention:*`; the defaults are:

| Data | Deleted after | Option |
|---|---|---|
| Read notifications | 90 days after `ReadAtUtc` | `ReadDays` |
| Owner-deleted notifications | 30 days after `DeletedAtUtc` | `DeletedDays` |
| `Failed` and `DeadLettered` deliveries | 30 days after the last attempt | `FailedDays` |
| Finished non-in-app deliveries | 90 days | `DeliveryDays` |
| `Completed` outbox events | 7 days | `CompletedOutboxDays` |
| `NotificationAuditLogs` | never | |

In-app deliveries are removed with their notification, and a notification that still has an active delivery is skipped. `Failed` outbox events are not purged by this job.

### Legacy tables pending removal

`DocumentNotifications` and `MemoryNotifications` take no new rows: their producers publish to the hub instead, and their endpoints and events are gone. Existing rows were imported idempotently at startup by `LegacyNotificationImporter` (event keys `legacy:document:{Id}` and `legacy:memory:{Id}`, language `en`, read state preserved, the legacy rows left untouched). The tables, the importer and the three `[Obsolete]` forwarding shims (`AccountEmailJob`, `PasswordEmailJob`, `PasswordResetIssuanceJob`) are removed in a follow-up release, after the import counts are verified in production. That release adds a `DropLegacyDocumentAndMemoryNotifications` migration; it is not part of specs/067.

### Migration notes

* `AddNotificationHub` (20260928041922) creates the eight notification tables and all their indexes in one migration. It seeds nothing.
* `AddLocalizationSettingAndUserPreferredLanguage` (20261007050713) adds `AspNetUsers.PreferredLanguage`, creates `LocalizationSettings` and seeds the singleton row (fixed id `0c6f4c2a-5b1e-4d3a-9a57-7a1f0b6c3e10`, disabled, `["en"]`).
* Default template content is **not** in a migration. `NotificationTemplateSeeder`, a hosted service, installs a published version 1 of each shipped English and Arabic template from embedded `Seed/{language}/{type}.{channel}.json` files at startup, only where the template does not exist yet, so administrator edits survive every deploy.
* Both migrations are reversible, contain no UTF-8 BOM and put `System` usings first. Apply them by hand to the shared test databases before CI runs (see the repo's migration notes).

---

# 16. Audit Context

## AuditLogs

Stores:

User

Action

Entity

Old Values

New Values

Timestamp

---

## LoginHistory

Tracks authentication events.

---

## SecurityEvents

Examples:

Failed Login

Password Reset

2FA Enabled

Token Revoked

---

## ErrorLogs

Application-level exceptions.

---

# 17. Relationships

```text
User
 │
 ├──────── Conversations
 │              │
 │              └──────── Messages
 │                          │
 │                          └──────── Attachments
 │
 ├──────── KnowledgeBases ──────── KnowledgeBaseCategories (nullable FK, shared or private)
 │              │
 │              ├──────── KnowledgeBaseFolders (self-referencing, ParentFolderId)
 │              │
 │              ├──────── KnowledgeBaseDocuments (each in at most one Folder)
 │              │
 │              └──────── KnowledgeBaseTags
 │
 ├──────── KnowledgeBaseAuditLogs (append-only, not hard-FK'd to KnowledgeBases)
 │
 ├──────── PromptTemplates
 │
 ├──────── Agents
 │
 ├──────── AIUsage
 │
 └──────── UserSubscriptions
```

---

# 18. Indexing Strategy

Create indexes for:

* Email
* Username
* Conversation Owner
* Conversation Updated Date
* Message Timestamp
* Document Status
* Knowledge Base Owner
* AI Usage Date
* Payment Date

Add full-text indexes for:

* Messages
* Prompt Templates
* Documents
* Chunks

Vector indexes should be created on embedding columns when SQL Server vector indexing is available in the deployment environment.

---

# 19. Data Retention

Default:

* Soft delete conversations
* Soft delete documents
* Keep audit logs indefinitely
* Retain payment history permanently
* Never physically delete AI usage required for billing

Background jobs may permanently purge soft-deleted records after the configured retention period.

---

# 20. Security

Never store:

* Plain-text passwords
* AI provider API keys in plain text
* JWT access tokens
* SMTP passwords in plain text

Sensitive secrets should be encrypted using ASP.NET Core Data Protection or an enterprise secret store when available.

---

# 21. Future Expansion

The schema must support future additions without breaking existing relationships.

Planned modules include:

* Team Workspaces
* Organization Accounts
* Shared Knowledge Bases
* Prompt Marketplace
* AI Marketplace
* Workflow Automation
* Mobile Synchronization
* Desktop Client
* BIM Catalyst Integrations
* Autodesk Platform Services
* Revit Automation
* Civil 3D Automation
* Oracle Fusion Integration

No future module should require redesigning the existing core schema. Instead, it should extend the model using new bounded contexts, foreign keys, and interfaces while preserving backward compatibility.
