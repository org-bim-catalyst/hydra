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

## KnowledgeBases

Stores:

* Owner
* Name
* Description
* Visibility
* Default Embedding Model

---

## KnowledgeBaseMembers

Future collaboration support.

Roles:

Owner

Editor

Viewer

---

## Documents

Stores:

* Knowledge Base
* File
* Parser
* Status
* Language
* Page Count

---

## DocumentChunks

Stores:

* Document
* Chunk Index
* Text
* Token Count
* Embedding Status

---

## Embeddings

Stores:

* Chunk
* Embedding Model
* Embedding Vector
* Dimensions

Initial implementation uses SQL Server vector support.

Future implementations simply replace the repository.

---

# 8. Memory Context

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

# 9. Prompt Library

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

# 10. Agent Context

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

# 11. MCP Context

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

# 12. File Context

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

# 13. Payment Context

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

# 14. Administration Context

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

# 15. Audit Context

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

# 16. Relationships

```text
User
 │
 ├──────── Conversations
 │              │
 │              └──────── Messages
 │                          │
 │                          └──────── Attachments
 │
 ├──────── KnowledgeBases
 │              │
 │              └──────── Documents
 │                           │
 │                           └──────── Chunks
 │                                        │
 │                                        └──────── Embeddings
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

# 17. Indexing Strategy

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

# 18. Data Retention

Default:

* Soft delete conversations
* Soft delete documents
* Keep audit logs indefinitely
* Retain payment history permanently
* Never physically delete AI usage required for billing

Background jobs may permanently purge soft-deleted records after the configured retention period.

---

# 19. Security

Never store:

* Plain-text passwords
* AI provider API keys in plain text
* JWT access tokens
* SMTP passwords in plain text

Sensitive secrets should be encrypted using ASP.NET Core Data Protection or an enterprise secret store when available.

---

# 20. Future Expansion

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
