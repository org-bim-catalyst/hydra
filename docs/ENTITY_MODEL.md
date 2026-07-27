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

## Aggregate Root

Agent

Properties

```text
OwnerId

Name

Description

Instructions

PreferredProvider

PreferredModel

Temperature

Enabled
```

Navigation

```text
Tools

Runs
```

Business Rules

Agents may only execute authorized tools.

---

## Entity

AgentTool

Properties

```text
AgentId

ToolId
```

---

## Entity

AgentRun

Properties

```text
AgentId

StartedAt

CompletedAt

Success

InputTokens

OutputTokens

Cost
```

---

# 11. MCP Aggregate

## Aggregate Root

McpServer

Properties

```text
Name

DisplayName

Endpoint

AuthenticationType

Enabled
```

Navigation

```text
Tools
```

---

## Entity

McpTool

Properties

```text
ServerId

Name

Description

InputSchema

OutputSchema

Enabled
```

---

## Entity

McpExecution

Properties

```text
ToolId

ConversationId

StartedAt

CompletedAt

Status

Result
```

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

## AgentRunStatus

```text
Pending

Running

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
| Agent         | AgentRuns     | Restrict            |
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

AgentCreated

AgentRunStarted

AgentRunCompleted

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
* An AgentRun cannot reference a disabled Agent.
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
