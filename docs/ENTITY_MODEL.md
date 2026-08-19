# ENTITY_MODEL.md

> **Project:** Ask Lucy AI Workspace
>
> **Version:** 2.0
>
> **Architecture:** Domain-Driven Design (DDD) + Clean Architecture
>
> **Persistence:** Entity Framework Core (.NET 10)
>
> **Database:** SQL Server
>
> **Last Updated:** July 2026

---

# 1. Purpose

This document defines the **Domain Model** for Ask Lucy.

It is **not** a database schema.

Instead, it defines:

* Aggregate Roots
* Entities
* Value Objects
* Enumerations
* Relationships
* Ownership
* Delete Behavior
* Navigation Properties
* Domain Events

The Entity Framework configuration should be generated from this document.

---

# 2. Domain Principles

The domain model follows Domain-Driven Design (DDD).

Guidelines:

* Every Aggregate has one Aggregate Root.
* Aggregate boundaries must be respected.
* Cross-aggregate references should use IDs rather than object references where practical.
* Domain objects never depend on Entity Framework.
* Business rules belong inside aggregates.
* Persistence concerns belong in Infrastructure/Persistence.

---

# 3. Base Classes

## BaseEntity

Every entity inherits:

```text
Id : Guid

CreatedAtUtc : DateTime

CreatedBy : Guid?

ModifiedAtUtc : DateTime?

ModifiedBy : Guid?

DeletedAtUtc : DateTime?

DeletedBy : Guid?

RowVersion : byte[]
```

---

## AggregateRoot

Marker class inheriting BaseEntity.

Adds:

```text
DomainEvents
```

---

# 4. Identity Aggregate

## Aggregate Root

ApplicationUser

### Properties

```text
Id

Email

UserName

DisplayName

AvatarUrl

PreferredLanguage

PreferredTheme

TimeZone

DefaultProviderId

DefaultModelId

EmailVerified

TwoFactorEnabled

IsActive
```

### Navigation

```text
Conversations

KnowledgeBases

Agents

PromptTemplates

Files

Subscriptions

AIUsage

Memories
```

### Domain Events

```text
UserRegistered

EmailVerified

UserLocked

TwoFactorEnabled
```

---

# 5. AI Aggregate

## Aggregate Root

AIProvider

### Properties

```text
Name

DisplayName

IsEnabled

SupportsVision

SupportsStreaming

SupportsFunctions

SupportsReasoning
```

Navigation

```text
Models
```

---

## Entity

AIModel

Properties

```text
ProviderId

Name

DisplayName

ContextWindow

MaxOutputTokens

SupportsImages

SupportsVision

SupportsJSON

SupportsFunctionCalling

SupportsStreaming

SupportsReasoning
```

Delete Behavior

Restrict

---

## Aggregate Root

UserAISettings

Properties

```text
UserId

ProviderId

ModelId

Temperature

TopP

PresencePenalty

FrequencyPenalty

MaxTokens

Streaming

SystemPrompt
```

---

# 6. Conversation Aggregate

## Aggregate Root

Conversation

Properties

```text
OwnerId

Title

ProviderId

ModelId

SystemPrompt

Temperature

Pinned

Archived

Favorite

LastMessageAtUtc
```

Navigation

```text
Messages

Tags

KnowledgeBases
```

Business Rules

* Owner required
* Title required
* Archive does not delete messages
* Deleting conversation soft deletes messages

Domain Events

```text
ConversationCreated

ConversationArchived

ConversationDeleted
```

---

## Entity

Message

Properties

```text
ConversationId

Role

Content

ModelName

InputTokens

OutputTokens

ProcessingMilliseconds

EstimatedCost

ParentMessageId
```

Navigation

```text
Attachments

Citations

```

Business Rules

Messages are immutable after completion except for moderation or administrator actions.

---

## Entity

MessageAttachment

Properties

```text
MessageId

FileId

AttachmentType
```

---

## Entity

ConversationTag

Properties

```text
ConversationId

Name
```

---

# 7. Knowledge Aggregate

## Aggregate Root

KnowledgeBase

Properties

```text
OwnerId

Name

Description

Visibility

EmbeddingModel

DefaultChunkSize

DefaultChunkOverlap
```

Navigation

```text
Documents

Members
```

Business Rules

Only owners or editors may upload documents.

---

## Entity

KnowledgeBaseMember

Properties

```text
KnowledgeBaseId

UserId

Role
```

Roles

```text
Owner

Editor

Viewer
```

---

## Entity

KnowledgeDocument

Properties

```text
KnowledgeBaseId

FileId

Language

Parser

Status

PageCount

WordCount

CharacterCount
```

Navigation

```text
Chunks
```

Domain Events

```text
DocumentUploaded

DocumentIndexed

DocumentDeleted
```

---

## Entity

DocumentChunk

Properties

```text
DocumentId

ChunkIndex

Content

TokenCount

EmbeddingStatus
```

Navigation

```text
Embedding
```

Business Rule

Chunk order is immutable.

---

## Entity

Embedding

Properties

```text
ChunkId

EmbeddingModel

Dimensions

Vector

CreatedAtUtc
```

Future providers must reuse this entity.

---

# 8. Memory Aggregate

## Aggregate Root

UserMemory

Properties

```text
UserId

Category

Key

Value

Confidence

Source
```

Categories

```text
Preference

Fact

Project

WritingStyle

Language

Other
```

Business Rules

Memory may expire based on policy.

---

## Entity

ConversationMemory

Properties

```text
ConversationId

Summary

LastUpdated
```

Purpose

Conversation compression.

---

# 9. Prompt Library Aggregate

## Aggregate Root

PromptTemplate

Properties

```text
OwnerId

CategoryId

Title

Description

Prompt

Variables

Favorite

Version
```

Navigation

```text
Category
```

---

## Entity

PromptCategory

Properties

```text
Name

DisplayOrder
```

---

# 10. Agent Aggregate

Shipped in specs/020-ai-agent-framework (superseding the earlier sketch below) as four
aggregates: `Agent` (definition/versioning), `AgentExecution` (one run and everything it
produced), `AgentPolicy` (administrator auto-approval rules), `AgentAuditLog` (security audit,
standalone). See `specs/020-ai-agent-framework/data-model.md` for the authoritative field list;
this section lists structure and business rules only.

## Aggregate Root

Agent

Properties

```text
OwnerId

Name, Description

AgentType (Conversational | Research | Document | Knowledge | Task)

Status (Draft | Published | Archived)

PreArchiveStatus (nullable — where Restore returns to)

Instructions (SystemInstructions, Objectives, Constraints, BehavioralRules,
OutputRequirements, ToolUsageRules, SafetyRules)

ModelProviderId, ModelId

OutputFormat (PlainText | Markdown | Json | StructuredOutput | Files)

ExecutionPolicy (MaxSteps, MaxExecutionDurationSeconds, MaxTokens, MaxCost,
MaxToolCalls, MaxRetries — all nullable, fall back to AgentRuntimeOptions defaults)

PublishedVersionNumber (nullable)
```

Navigation

```text
Tools (AgentTool)

KnowledgeBases (AgentKnowledgeBase)

MemoryPolicy (AgentMemoryPolicy, 0..1)

Versions (AgentVersion)
```

Business Rules

Agents may only be configured with tools/Knowledge Bases the owner is authorized for — an
agent's effective access at execution time is always the intersection of its configuration and
the executing user's own authorization, never broader. Publishing snapshots the current draft
into an immutable `AgentVersion`; a published version's fields never change afterward.
Duplicate/Archive/Restore/Delete never touch version or execution history.

---

## Entity

AgentTool / AgentKnowledgeBase / AgentMemoryPolicy

The agent's *draft* configuration — mutated freely until publish. `AgentTool` and
`AgentKnowledgeBase` are simple `(AgentId, ToolName | KnowledgeBaseId, …)` join rows;
`AgentMemoryPolicy` is a 0..1 owned entity (`AllowRead`, `AllowWriteProposals`,
`PreApprovedCategoriesJson`).

---

## Entity

AgentVersion

Properties

```text
AgentId, VersionNumber (unique per agent)

Instructions, ModelProviderId, ModelId, ExecutionPolicy, OutputFormat (snapshotted)

ToolsSnapshotJson, KnowledgeBasesSnapshotJson, MemoryPolicySnapshotJson

ChangeDescription
```

Business Rules

Immutable once created — every `AgentExecution` references the exact `AgentVersionId` it ran
under, so a later draft edit or republish never retroactively changes what a past execution
reports.

---

## Aggregate Root

AgentExecution

Properties

```text
AgentId, AgentVersionId, RunByUserId, Objective

Status (Queued | Running | Paused | WaitingForApproval | Completed | Failed | Cancelled)

IsTestExecution, ConversationIntegrationMode (Standalone | NewConversation |
ExistingConversation), UserChatId

PlanJson, FinalOutputText, FinalOutputJson, TerminationReason

StartedAtUtc, CompletedAtUtc
```

Navigation

```text
Steps (AgentExecutionStep)

Events (AgentExecutionEvent, append-only)

Approvals (AgentApproval)

Errors (AgentExecutionError)

Usage (AgentExecutionUsage, 0..1), Cost (AgentExecutionCost, 0..1)
```

Business Rules

Never hard-deleted. Resumable: a pause persists all state needed to continue from exactly
where it stopped, never re-running a completed step. A test execution (`IsTestExecution`)
never invokes a mutating tool.

---

## Entity

AgentExecutionStep / AgentToolCall

`AgentExecutionStep` (`StepIndex` unique per execution, `StepType`: ToolCall | ModelReasoning
| Validation, `Status`: Pending | Running | Completed | Failed | Skipped | Cancelled |
WaitingForApproval) is one plan step. `AgentToolCall` (`RiskLevel`, `RequiredPermissionsJson`,
`ValidatedInputJson`/`ValidatedOutputJson`, `WasApprovalRequired`) is one specific tool
invocation within a `ToolCall`-type step.

---

## Entity

AgentApproval

Properties

```text
AgentExecutionId, AgentToolCallId (nullable)

IntendedActionDescription, IntendedParametersJson

Decision (Pending | Approved | Rejected), DecidedByUserId, WasPolicyBased,
MatchedAgentPolicyId
```

---

## Aggregate Root

AgentPolicy

Properties

```text
OrganizationId (reserved, always null this release)

Name, Description, ToolName, ConditionsJson (flat parameter-equality match; empty =
always), IsEnabled

CreatedByUserId (must hold Administrator/Super User role)
```

Business Rules

Administrator/Super User only. Matched against an intended High/Critical-risk tool call
before it pauses for interactive approval.

---

## Entity

AgentUserExecutionLimit

Properties

```text
UserId (unique), MaxConcurrentExecutions, SetByUserId
```

Business Rules

Per-user override of `AgentRuntimeOptions.DefaultMaxConcurrentExecutions` (FR-042/043); no
`SubscriptionTier` concept exists yet.

---

## Aggregate Root

AgentAuditLog

Properties

```text
AgentExecutionId (not a hard FK — survives a later-purged execution), UserId

Action (PermissionChecked | PermissionDenied | ApprovalDecided |
CrossUserAccessAttempted | ExecutionCompleted | ExecutionFailed)

DetailsJson, OccurredAtUtc
```

Business Rules

Append-only, tamper-resistant. Distinct from the operational `AgentExecutionEvent` stream —
this is the security-audit record.

---

# 11. MCP Aggregate

Shipped in specs/021-mcp-integration (superseding the earlier `McpServer`/`McpTool`/`McpExecution`
sketch) as eight entities — no `McpExecution` entity was built; an MCP tool call reuses spec 020's
existing `AgentToolCall`/`AgentExecutionStep` unmodified (research.md Decision — see
`specs/021-mcp-integration/data-model.md` for the authoritative field list; this section lists
structure and business rules only).

## Aggregate Root

McpServer

Properties

```text
Name, Description, Endpoint, Transport (StreamableHttp | Stdio)

AuthenticationType (None | ApiKey | BearerToken | OAuth2ClientCredentials)

RequiresUnauthenticatedConfirmation, AllowInsecureTransport,
InsecureTransportJustification

EndpointValidationOverride, EndpointValidationJustification

IsEnabled, OwnerUserId, ConfigurationVersion, CapabilityRefreshIntervalMinutes

LastHealthCheckAtUtc, LastCapabilityDiscoveryAtUtc
```

Business Rules

`(Endpoint, Transport)` unique platform-wide. Starts `IsEnabled: false` regardless of input —
an administrator must explicitly enable it. Registration and every update independently
re-validates the endpoint against SSRF rules (`IMcpEndpointValidator`); a private/loopback/
link-local/cloud-metadata destination is rejected unless explicitly overridden with a
justification. Soft-delete is blocked while any `AgentTool.ToolName` still references one of
its tools (`McpServerHasReferencesException`).

---

## Entity

McpServerCredential

Properties

```text
McpServerId (unique), CiphertextBlob, RotatedAtUtc, RotatedByUserId
```

Business Rules

Server-side only; never a plaintext value in any DTO, log, or audit record. Rotation replaces
`CiphertextBlob` in place (never delete+re-insert) and invalidates the connection-pool entry for
that server so the next call reconnects with the new value — an already in-flight call on the old
connection is unaffected and completes/fails independently.

---

## Entity

McpServerHealth

Properties

```text
McpServerId (unique — one current row per server, overwritten on every check)

Status (Healthy | Degraded | Unavailable | AuthenticationFailed | ConfigurationError |
Unknown)

FailureCategory (nullable), Detail, CheckedAtUtc, ConsecutiveFailureCount
```

Business Rules

Checked on-demand (admin "Test connection") and by a 5-minute recurring job, both through the
same command handler. `Unavailable`/`AuthenticationFailed` excludes every tool on that server
from `IMcpToolRegistry.ActiveTools` the moment the health-check job's sweep completes.

---

## Entity

McpCapabilitySnapshot

Properties

```text
McpServerId, SnapshotVersion (unique per server), DiscoveredAtUtc,
DeclaredCapabilitiesJson, ChangeSummaryJson, WasSuccessful, FailureCategory (nullable)
```

Business Rules

Append-only. A failed discovery run leaves every prior successful snapshot's `McpTool`/
`McpResource`/`McpPrompt` rows untouched.

---

## Entity

McpTool

Properties

```text
McpServerId, McpCapabilitySnapshotId, NamespacedName (unique — "mcp:{serverId}:{toolName}",
the same string `AgentTool.ToolName`/`AgentToolCall.ToolName`/`AgentPolicy.ToolName`
reference), ToolName, DisplayName, Description

InputSchemaJson, OutputSchemaJson, DeclaredCapabilitiesJson

ServerDeclaredRiskLevel (nullable, advisory only), EffectiveRiskLevel (governs runtime
behavior), RequiredPermissionsJson

ActivationStatus (PendingReview | Active | Deactivated), ActivatedByUserId, ActivatedAtUtc

Version, IsAvailable
```

Business Rules

Always starts (or reverts to, on any detected schema/description change since the prior
snapshot) `PendingReview` — an administrator must explicitly activate it before any agent can
call it, regardless of what risk level the server itself declares. `EffectiveRiskLevel` defaults
to `Critical` when the server declares none.

---

## Entity

McpResource / McpPrompt

Properties

```text
McpResource: McpServerId, McpCapabilitySnapshotId, NamespacedName (unique), Uri, Name,
Description, ContentType, IsAvailable

McpPrompt: McpServerId, McpCapabilitySnapshotId, NamespacedName (unique), Name,
Description, ContentTemplate, IsAvailable
```

Business Rules

`McpResource` is a normal snapshot-per-discovery row, like `McpTool`. `McpPrompt` is instead a
read-only mirror mutated in place on every refresh (`RefreshFromSnapshot`, never a new row per
snapshot) — a user who wants an editable copy duplicates it into an independent, native `Prompt`
(`DuplicateMcpPromptCommand`), after which the two have no further relationship.

---

## Aggregate Root

McpAuditLog

Properties

```text
McpServerId (not a hard FK — survives a later-purged server), UserId

Action (ServerRegistered | ServerUpdated | ServerEnabled | ServerDisabled |
ServerRemovalBlocked | ServerRemoved | CredentialRotated | CapabilityDiscoveryStarted |
CapabilityDiscoverySucceeded | CapabilityDiscoveryFailed | HealthStateChanged |
ToolActivated | ToolDeactivated | UnauthorizedAccessAttempted)

FailureCategory (nullable), DetailsJson, OccurredAtUtc
```

Business Rules

Administrative/security events only — deliberately does not duplicate `AgentToolCall`'s
per-execution tool-call activity (already captured there, distinct from but
cross-referenceable with this table). A failed MCP tool *call* (as opposed to a server-level
administrative action) never writes here; its granular `McpFailureCategory` is instead embedded
as a `[CategoryName]` prefix in the same `AgentToolCall.FailureReason` text every native tool's
failure already uses.

---

# 12. File Aggregate

## Aggregate Root

StoredFile

Properties

```text
OwnerId

OriginalName

StoredName

Extension

ContentType

Length

SHA256

StorageProvider

StoragePath
```

Business Rules

StoragePath is never exposed outside Infrastructure.

---

## Entity

SignedDownload

Properties

```text
FileId

Token

ExpiresAtUtc

DownloadedAtUtc
```

---

# 13. Billing Aggregate

## Aggregate Root

Subscription

Properties

```text
UserId

PlanId

Status

StartedAt

ExpiresAt

AutoRenew
```

---

## Entity

SubscriptionPlan

Properties

```text
Name

MonthlyPrice

AnnualPrice

MonthlyTokenLimit

StorageLimit

KnowledgeBaseLimit

AgentLimit
```

---

## Entity

PaymentTransaction

Properties

```text
SubscriptionId

Provider

TransactionId

Amount

Currency

Status

ProcessedAt
```

---

# 14. Audit Aggregate

## Aggregate Root

AuditLog

Properties

```text
UserId

EntityName

EntityId

Action

OldValues

NewValues

IPAddress

Timestamp
```

---

## Entity

SecurityEvent

Properties

```text
UserId

Type

Severity

Description

OccurredAt
```

---

# 15. Enumerations

## ConversationRole

```text
User

Assistant

System

Tool

Function
```

---

## FileType

```text
PDF

Word

Excel

PowerPoint

Markdown

CSV

Text

Image

Audio

Video

Other
```

---

## DocumentStatus

```text
Uploaded

Parsing

Parsed

Chunking

Embedding

Indexed

Failed
```

---

## Visibility

```text
Private

Shared

Organization

Public
```

---

## AgentExecutionStatus

```text
Queued

Running

Paused

WaitingForApproval

Completed

Failed

Cancelled
```

---

## PaymentStatus

```text
Pending

Completed

Failed

Refunded

Cancelled
```

---

# 16. Value Objects

The following should be implemented as Value Objects where appropriate:

```text
EmailAddress

Money

TokenUsage

PromptParameters

TemperatureSettings

EmbeddingMetadata

GeoLocation

LanguageCode

ThemePreference

FileHash
```

Value Objects are immutable.

---

# 17. Delete Behavior

| Parent        | Child         | Behavior            |
| ------------- | ------------- | ------------------- |
| User          | Conversations | Cascade Soft Delete |
| Conversation  | Messages      | Cascade Soft Delete |
| Message       | Attachments   | Cascade             |
| KnowledgeBase | Documents     | Cascade Soft Delete |
| Document      | Chunks        | Cascade             |
| Chunk         | Embedding     | Cascade             |
| Agent         | AgentExecutions | Restrict — never cascades (FR-050 audit trail) |
| Subscription  | Payments      | Restrict            |

---

# 18. Aggregate Ownership

```text
User
 ├── Conversation
 │      ├── Message
 │      │      └── Attachment
 │      └── Tag
 │
 ├── KnowledgeBase
 │      ├── Document
 │      │      ├── Chunk
 │      │      │      └── Embedding
 │      │      └── Members
 │
 ├── PromptTemplate
 │
 ├── Agent
 │      ├── Tool
 │      └── Runs
 │
 ├── Subscription
 │      └── Payments
 │
 └── Memories
```

---

# 19. Domain Events

Representative domain events include:

```text
UserRegistered

EmailVerified

ConversationCreated

ConversationArchived

MessageSent

MessageCompleted

MessageRegenerated

KnowledgeBaseCreated

DocumentUploaded

DocumentParsed

DocumentChunked

EmbeddingGenerated

KnowledgeBaseIndexed

AgentPublished

AgentExecutionStarted

AgentExecutionCompleted

AgentExecutionFailed

AgentApprovalRequested

FileUploaded

SubscriptionActivated

SubscriptionRenewed

PaymentCompleted

SecurityEventRaised
```

Events should be raised inside aggregates and handled in the Application layer.

---

# 20. EF Core Configuration Guidelines

* Use Fluent API for all mappings.
* Keep configuration classes separate from entities.
* Configure all indexes explicitly.
* Configure concurrency with `RowVersion`.
* Prefer owned types for Value Objects.
* Use `DateTimeOffset` for timestamps when external systems require timezone preservation; otherwise store UTC consistently.
* Use lazy loading only when explicitly justified; prefer eager loading and projections.
* Use `AsNoTracking()` for read-only queries.
* Use optimistic concurrency.
* Configure cascade deletes explicitly rather than relying on EF defaults.

---

# 21. Aggregate Invariants

Every aggregate must enforce its own business rules.

Examples:

* A Conversation must always have exactly one owner.
* A Message cannot exist without a Conversation.
* A Chunk cannot exist without a Document.
* An Embedding cannot exist without a Chunk.
* An AgentExecution always references the exact AgentVersion it ran under, never the Agent's current draft.
* A PaymentTransaction cannot exist without a Subscription.
* A SignedDownload cannot be used after expiration.
* A User cannot have multiple active default AI settings.

No aggregate may rely on another aggregate to maintain its internal consistency.

---

# 22. Future Evolution

The domain model is intentionally designed to accommodate future capabilities without breaking existing aggregates, including:

* Team workspaces
* Organizations and departments
* Shared conversations
* Shared knowledge bases
* AI workflow automation
* Plugin marketplace
* Agent-to-agent collaboration
* Multi-region deployments
* BIM Catalyst integrations
* Autodesk Platform Services
* Revit, Civil 3D, and Navisworks automation
* Model Context Protocol (MCP) ecosystem

Future functionality should extend the domain model by introducing new aggregates or entities while preserving existing aggregate boundaries and invariants.
