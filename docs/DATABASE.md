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
> **Last Updated:** July 2026

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
* Standard audit columns, `RowVersion`

Messages are immutable/append-only once created — no update path exists.

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

## Agents

Stores:

* Name
* Description
* Instructions
* Preferred Model
* Preferred Provider

---

## AgentTools

Maps agents to available tools.

---

## AgentRuns

Tracks every execution.

Stores:

* Inputs
* Outputs
* Tokens
* Duration
* Success

---

# 12. MCP Context

## MCPServers

Stores:

* Name
* Endpoint
* Authentication
* Status

---

## MCPTools

Stores every discovered tool.

Examples:

Search

SQL

GitHub

Revit

APS

SharePoint

---

## MCPExecutions

Audit every tool execution.

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
