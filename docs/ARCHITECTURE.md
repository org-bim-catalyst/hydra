# ARCHITECTURE.md

> **Project:** Ask Lucy AI Workspace
>
> **Version:** 2.1
>
> **Architecture:** Clean Architecture + Modular Monolith
>
> **Framework:** ASP.NET Core (.NET 10)
>
> **Frontend:** React + TypeScript + Vite
>
> **Last Updated:** August 2026 (v2.1: added §29 Prompt Library & Prompt Engineering Workspace, specs/019)

---

# 1. Architecture Overview

Ask Lucy is designed as a **Modular Monolith** following **Clean Architecture** principles.

The application is divided into independent feature modules that communicate through well-defined interfaces. The architecture is designed so that any module can later be extracted into its own microservice with minimal effort.

This approach provides:

* Simpler deployment
* Easier debugging
* Lower infrastructure cost
* Better maintainability
* Clear separation of concerns
* Future migration path to distributed services

---

# 2. Architectural Goals

The architecture must satisfy the following goals:

* Provider-independent AI integration
* Enterprise-grade security
* Modular feature development
* Scalable RAG infrastructure
* Multi-model AI support
* Extensible Agent framework
* MCP compatibility
* Testability
* Maintainability
* High cohesion and low coupling

---

# 3. High-Level System Architecture

```text
                    React + TypeScript (Vite)
                             │
                             ▼
                     ASP.NET Core Web API
                             │
                     Authentication Layer
                             │
                     Application Layer
                             │
        ┌──────────────┬───────────────┬───────────────┐
        ▼              ▼               ▼
   Chat Engine     Memory Engine    RAG Engine
        │              │               │
        └──────────────┼───────────────┘
                       ▼
                 Prompt Builder
                       ▼
                AI Provider Engine
                       ▼
        GPT | Claude | Gemini | OpenRouter
                       │
             Tool / MCP Orchestrator
                       │
      SharePoint | GitHub | APS | SQL | Revit
```

---

# 4. Solution Structure

```text
AskLucy.sln

/src

    AskLucy.Domain

    AskLucy.Application

    AskLucy.Infrastructure

    AskLucy.Persistence

    AskLucy.Web

    AskLucy.Frontend

/tests

    Domain.Tests

    Application.Tests

    Infrastructure.Tests

    Integration.Tests
```

---

# 5. Backend Layer Responsibilities

## Domain Layer

Contains only business concepts.

Contains:

* Entities
* Value Objects
* Domain Events
* Enumerations
* Interfaces
* Business Rules

Never reference:

* Entity Framework
* ASP.NET
* SQL Server
* OpenAI SDK
* React

The Domain layer must remain pure C#.

---

## Application Layer

Contains business use cases.

Includes:

* CQRS Commands
* CQRS Queries
* Handlers
* DTOs
* Validators
* Interfaces
* Authorization Policies
* Mapping Profiles

Uses:

* MediatR
* FluentValidation
* AutoMapper

The Application layer orchestrates the business logic but does not know implementation details.

---

## Infrastructure Layer

Contains external integrations.

Examples:

* OpenAI
* Anthropic
* Gemini
* SMTP
* PayPal
* SignalR
* File Storage
* Logging
* Embeddings
* MCP Clients

Infrastructure implements interfaces defined in the Application layer.

---

## Persistence Layer

Responsible for:

* Entity Framework Core
* SQL Server
* DbContext
* Entity Configurations
* Migrations
* Repositories (only where appropriate)

Persistence knows nothing about controllers or React.

---

## WebAPI Layer

Responsible only for:

* Controllers
* Authentication
* Middleware
* Dependency Injection
* Swagger
* SignalR Hubs

Controllers must remain thin.

No business logic belongs here.

---

# 6. Frontend Architecture

```text
src/

api/

assets/

components/

features/

hooks/

layouts/

pages/

routes/

services/

store/

theme/

types/

utils/
```

Each feature owns its own UI components.

Example:

```text
features/

    chat/

    rag/

    settings/

    agents/

    profile/

    admin/

    billing/
```

Each feature contains:

```text
components/

pages/

hooks/

api/

types/

validators/
```

Avoid a large shared components folder.

---

# 7. Feature-Based Backend Organization

Inside Application:

```text
Application/

Authentication/

Users/

Chats/

Messages/

KnowledgeBases/

Documents/

Embeddings/

Memory/

Providers/

Agents/

Tools/

Payments/

Notifications/

Admin/
```

Each feature contains:

```text
Commands/

Queries/

DTOs/

Validators/

Mappings/

Events/
```

This keeps features isolated and maintainable.

---

# 8. Dependency Rules

Dependencies must always flow inward.

```text
WebAPI
      │
Application
      │
Domain

Infrastructure ─────► Application

Persistence ───────► Application
```

Never allow:

Application → Infrastructure

Domain → Persistence

Domain → ASP.NET

Application → SQL Server

---

# 9. AI Provider Architecture

Never call OpenAI directly.

Instead:

```text
IAIProvider

        ▲

        │

OpenAIProvider

ClaudeProvider

GeminiProvider

OpenRouterProvider

AzureOpenAIProvider

OllamaProvider
```

The AI Provider Engine selects the active provider based on user settings.

Every provider must implement identical interfaces.

---

# 10. Chat Engine

Responsibilities:

* Create chat
* Rename chat
* Archive
* Delete
* Stream responses
* Persist messages
* Count tokens
* Store model metadata
* Handle attachments

The Chat Engine never talks directly to OpenAI.

Instead:

```text
Chat Engine

↓

Prompt Builder

↓

AI Provider Engine

↓

Selected Provider
```

---

# 11. Prompt Builder

Prompt Builder assembles the final prompt.

Inputs:

* System Prompt
* Conversation History
* Retrieved Documents
* User Memory
* Agent Instructions
* Tool Results

Output:

A provider-neutral prompt object.

This prevents prompt logic from spreading across the application.

---

# 12. Memory Engine

Supports:

## Short-Term Memory

Conversation context.

## Long-Term Memory

Persistent preferences.

Examples:

* Preferred language
* Favorite model
* Writing style
* Recent projects

The Memory Engine can later evolve into semantic memory without affecting the Chat Engine.

---

# 13. RAG Engine

Pipeline:

```text
Upload

↓

Parser

↓

Chunker

↓

Embedding Generator

↓

Vector Store

↓

Retriever

↓

Prompt Builder
```

Services:

```text
IDocumentParser

IChunkingService

IEmbeddingService

IVectorStore

IRetriever

IRagService
```

The vector store is abstracted.

Initial implementation:

SQL Server

Future implementations:

Qdrant

Pinecone

Azure AI Search

Weaviate

No application code should depend on a specific vector database.

---

# 14. Knowledge Base Engine

Hierarchy:

```text
User

↓

Knowledge Base

↓

Folders

↓

Documents

↓

Chunks

↓

Embeddings
```

A conversation may attach multiple Knowledge Bases.

---

# 15. Agent Engine

Each AI agent consists of:

* Identity
* Instructions
* Available Tools
* Memory
* Model Preference
* Temperature
* Permissions

Examples:

Research Agent

Translator

Developer Assistant

BIM Assistant

Document Analyst

Meeting Assistant

Agents communicate through the AI Provider Engine.

---

# 16. MCP Tool Engine

**Superseded by §31 ("Model Context Protocol (MCP) Integration"), which describes the shipped
design (specs/021-mcp-integration).** This section is the pre-implementation sketch, retained for
history; the sketch below never became the actual architecture — MCP shipped as one more source
feeding the existing Agent Tool abstraction (§30), not a standalone engine with its own execution
path.

Tool execution is separated from AI.

Architecture:

```text
LLM

↓

Tool Decision

↓

MCP Tool Engine

↓

MCP Client

↓

External System
```

Supported future tools:

* Revit
* APS
* SQL Server
* SharePoint
* GitHub
* Oracle Fusion
* Microsoft 365

---

# 17. File Storage

Storage abstraction:

```text
IFileStorage

▲

LocalFileStorage

AzureBlobStorage

S3Storage

CloudflareR2Storage
```

Current implementation:

Server filesystem.

Files are downloaded only through signed URLs.

---

# 18. Background Processing

Use Hosted Services for:

* Embedding generation
* Email sending
* Cleanup jobs
* File indexing
* Token usage aggregation
* Notification delivery

Long-running tasks should not block HTTP requests.

---

# 19. Caching Strategy

Use layered caching.

Memory Cache

↓

Distributed Cache (future)

↓

Database

Suitable for:

* User settings
* AI model catalog
* Prompt templates
* System configuration

Do not cache security-sensitive data.

---

# 20. Event-Driven Design

Use domain and integration events where appropriate.

Examples:

```text
ChatCreated

MessageGenerated

KnowledgeBaseIndexed

EmbeddingCreated

SubscriptionActivated

PaymentCompleted
```

Avoid direct module coupling when events provide a cleaner solution.

---

# 21. Logging

Use Serilog with structured logging.

Log:

* Requests
* Exceptions
* Authentication
* AI provider calls
* Token usage
* Payment events
* Background jobs

Never log:

* Passwords
* JWTs
* API Keys
* Refresh Tokens
* Sensitive document contents

---

# 22. Error Handling

Implement centralized exception handling.

Return standardized error responses using RFC 9457 Problem Details (`application/problem+json`).

Include:

* Error code
* Title
* Detail (safe for clients)
* Correlation ID
* Timestamp

Never expose stack traces in production.

---

# 23. Testing Strategy

Unit Tests

* Domain
* Application

Integration Tests

* Database
* API
* Authentication

Frontend Tests

* Component tests
* Integration tests

End-to-End Tests

* Playwright

Every new feature should include appropriate automated tests.

---

# 24. Scalability Strategy

Current:

Single Server

↓

Future:

Multiple Web Servers

↓

Redis

↓

Dedicated Vector Database

↓

Message Queue

↓

Microservices (if justified)

The application must scale horizontally without major architectural changes.

---

# 25. Future Expansion

The architecture should support future modules without restructuring the solution.

Examples:

* AI Marketplace
* Workflow Designer
* Prompt Marketplace
* Team Collaboration
* Shared Knowledge Bases
* AI Automation Studio
* Voice Agents
* Mobile Applications
* Desktop Client (WinUI/.NET)
* BIM Catalyst Integration
* Autodesk Platform Services Integration

---

# 26. Consent & Privacy Engine

Introduced in specs/004-cookie-consent-privacy. A narrowly-scoped module (`Domain/Consent`,
`Application/Consent`, `CookieConsentController`) that records each user's cookie-category
consent decisions as an append-only history (`CookieConsentRecord` — a preference change is
always a new inserted row, never an update) and exposes the currently published
cookie/privacy policy version (`ICookiePolicyProvider`, configuration-bound, not a database
table) via one public endpoint.

**Binding convention for any future analytics/marketing integration**: this feature does
not add an analytics or marketing SDK — none exists in the codebase today. `useCookieConsent()`
(`ClientApp/src/features/consent/hooks/useCookieConsent.ts`) is the single source of truth
for which categories the current user has granted. Any analytics or marketing script
loader added in the future — a tag manager, a pixel, a marketing SDK — **MUST** check
`consent.analytics` / `consent.marketing` from this hook before initializing, and MUST NOT
fire before it resolves. This is what makes the strict-opt-in requirement ("no
Functional/Analytics/Marketing cookie activity before an explicit decision," spec.md
FR-019) a real, enforceable gate rather than aspirational documentation — the enforcement
point already exists even though nothing calls it yet.

---

# 27. Document Intelligence Pipeline

Introduced in specs/015-document-intelligence-pipeline. Lives in `Domain/Documents`,
`Application/Documents`, `Infrastructure/Documents`, `Persistence` (via `AskLucyDbContext`),
and the three Documents controllers under `Web/Controllers/v1`. Frontend lives in
`ClientApp/src/features/documents`.

**Upload**: two paths share one duplicate-detection/validation pipeline — a `multipart/form-data`
single-request path (`POST /documents/uploads/simple`) for small files, and a resumable
chunked path (`DocumentUploadSession` + `IResumableUploadStorage`, sequential chunk indices
derived from actual bytes-on-disk rather than a separate counter, with a
`DeclaredSizeBytes`-derived ceiling rejecting chunks that would exceed the declared upload
size). SHA-256 checksums drive duplicate detection; a duplicate can be resolved as either a
new version of an existing document (US5) or a separate new document.

**Processing pipeline**: a Hangfire-durable job (`DocumentProcessingPipeline`, driven by
injected `IBackgroundJobClient` rather than the static `Hangfire.BackgroundJob` facade, for
testability) runs a fixed sequence of `IProcessingStageHandler` strategies — validation, OCR
(Tesseract, `IOcrEngine`), text extraction, metadata extraction, classification, language
detection, preview generation — each returning `ProcessingStageOutcome.Completed` or
`.Skipped` (skipped is not a failure). Re-processing a document (a new version replacing an
old one) uses idempotent upsert methods (`DocumentMetadata.ApplyReExtraction`,
`DocumentClassification.ApplyAutomaticReclassification`) that no-op when the user already
manually edited that field, so automatic reprocessing never silently overwrites a user's
edit. Progress pushes live over SignalR (`DocumentProcessingHub`), with 5-second REST polling
as a reconciliation fallback, never the primary path.

**Storage and delivery**: files are never served by physical path. `ISignedUrlService` mints
short-lived, resource-id-bound tokens via ASP.NET Core Data Protection (the resource id is
itself the encrypted/authenticated payload, so a signature minted for one id cannot validate
against another); `[AllowAnonymous]` download/preview actions validate the signature before
streaming any bytes. Preview supports page images (PDF, via Docnet.Core — never combine with
another PDFium-wrapping package in the same process; see `pdfium_native_dll_collision`
memory note), image thumbnails, structured content (Office documents, extracted headings/
paragraphs/tables/lists as JSON, rendered without pixel-perfect layout), and direct Markdown
rendering (`react-markdown` with no raw-HTML passthrough plugin — deliberately, so an
uploaded `.md` file can't inject a script tag).

**Organization**: documents can live in a nested folder tree (`DocumentFolder`), carry
system or user-defined categories/tags, and support cursor-based (keyset) pagination
throughout (`DocumentCursor`) rather than offset paging. A per-user dashboard
(`IDocumentStatisticsRepository`) and an admin-only organization-wide dashboard share one
`DashboardBody` component; live counts are computed from only the latest processing job per
document, so a document that failed and later succeeded on retry is never double-counted.

---

# 28. AI Memory System

Introduced in specs/018-ai-memory-system. Lives in `Domain/Memory`, `Domain/Projects`,
`Application/Memory`, `Application/Projects`, `Infrastructure/Memory`, `Persistence` (via
`AskLucyDbContext` and `SqlServerMemoryVectorStore`), and `MemoriesController`/
`ProjectsController` under `Web/Controllers/v1`. Frontend lives in
`ClientApp/src/features/memory`. Supersedes/extends the earlier "Memory Engine" sketch in
§12 with the shipped design.

**Lifecycle**: `Memory` moves through `PendingApproval → Active → Archived` (soft-deleted,
never hard-deleted, so audit history survives). Candidates originate from passive conversation
analysis (`MemoryExtractionJob`, a Hangfire job enqueued fire-and-forget per chat turn from
`SendChatMessageCommandHandler`) or explicit user save requests. Each `MemoryCategoryPreference`
independently selects an `MemoryApprovalMode` (`Automatic` / `Manual` / `Disabled`); sensitive
categories always require manual approval regardless of preference. Approved/auto-approved
candidates become `Active` immediately; a rejected or expired (never-reinforced) candidate is
soft-deleted by the recurring `MemoryCleanupJob`.

**Retrieval and injection** (`IMemoryService.RetrieveRelevantMemoriesAsync`): before generation,
the Chat Engine asks the Memory Engine for relevant memories, ranked by a composite score —
`similarity × recencyDecay × importance × confidence` — filtered to the caller's own `Active`
memories, the current Project scope (or general/unscoped), and any category the user hasn't
disabled. Results are injected as a `<user_memory>`-framed system message ahead of the RAG
system message (defensive framing prevents memory content from being interpreted as
instructions). A `MemoryReference` row is recorded per assistant message per memory used, so
the UI can show *why* Lucy remembered something (`MemoryTraceIndicator`, `GET
/chats/{id}/messages/{messageId}/memory-references`).

**Storage abstraction**: `IMemoryVectorStore` is a narrow, provider-neutral interface —
`SqlServerMemoryVectorStore` is the only implementation, using SQL Server's native `vector(n)`
column type and `VECTOR_DISTANCE` (raw ADO.NET, isolated to `AskLucy.Persistence`; the
`Domain`/`Application` Memory assemblies never reference `Microsoft.Data.SqlClient` or any AI
vendor SDK — enforced by `MemoryLayeringTests`). No `CREATE VECTOR INDEX` is issued — on
non-Azure SQL Server 2025 it makes the table read-only (see `sql_server_2025_vector_index_readonly`
memory note); retrieval instead does a filtered full scan (`State = 'Active'`), acceptable at
current per-user memory volumes.

**Conflict detection** (`IMemoryConflictDetectionService`): before a new candidate is upserted,
its embedding is compared against the user's existing memory pool; a single AI classification
call determines `NoConflict` / `DirectContradiction` / `AmbiguousConflict`. A direct
contradiction (e.g. "I use Angular" → "I moved to React") auto-merges, archiving the old
memory. An ambiguous conflict creates a `MemoryConflictNeedsConfirmation` notification and
leaves both memories `Active` until the user resolves it (`ResolveMemoryConflictCommand`:
`KeepExisting` / `KeepNew` / `KeepBoth`).

**Memory Center** (`/memory`, `MemoryCenterPage`): list/search/edit/delete, an approval queue,
per-category preferences, a notification feed (delivered live via `MemoryHub`, SignalR,
per-user groups keyed off `ClaimTypes.NameIdentifier`), and Projects management. **Projects**
(`Domain/Projects`) are a lightweight grouping construct — a conversation may be assigned to
one Project (`PUT /chats/{id}/project`), and memories are scoped `general` (no Project),
Project-specific, or queried across all scopes; deleting a Project archives its scoped
memories rather than deleting them.

**Privacy controls** (FR-023–FR-026): users can disable memory entirely, disable individual
categories, clear all memories (`ClearAllMemoriesCommand`, requires explicit `confirm: true`),
or request an export (`MemoryExportJob` entity tracks async generation status — never a bare
guessable filename — polled via `GetMemoryExportStatusQuery`, delivered through the same
signed-URL pattern as Document downloads). Account deletion (`DeleteMyAccountCommandHandler`)
anonymizes `MemoryAuditLog`/`MemoryNotification` rows (the two tables deliberately not FK-linked
to the user for audit-retention reasons) before the real hard delete; `Memory`/`Project`/
`MemoryPreference`/`MemoryCategoryPreference` cascade-delete via FK `ON DELETE CASCADE`.

**Security** (FR-027, SC-005): every Memory/Project endpoint is implicitly scoped to the
caller via `MemoryOwnershipGuard`/`ProjectOwnershipGuard`; a request naming another user's
memory or Project returns `404`, never `403` (least-information-disclosure, consistent with
the rest of the codebase's ownership-guard convention).

---

# 29. Prompt Library & Prompt Engineering Workspace

Introduced in specs/019-prompt-library-workspace. Lives in `Domain/Prompts`, `Application/Prompts`,
`Persistence` (via `AskLucyDbContext`), and `PromptsController`/`PromptFoldersController` under
`Web/Controllers/v1`, plus one new action on the existing `ChatsController`. Frontend lives in
`ClientApp/src/features/prompts`, with one integration point in `ClientApp/src/features/chat`
(`InsertPromptPicker`).

**Aggregate and versioning**: `Prompt` is the aggregate root — owns its `PromptVersion` history and
`PromptTag` assignments, and carries a denormalized copy of the current version's content so
full-text search can index the `Prompts` table directly without a join. Every content edit
(`ApplyEdit`) creates a new, immutable `PromptVersion`; nothing is ever deleted or overwritten —
`RestoreFrom` creates a brand-new version copying the restored content rather than reverting in
place, so version history only ever grows. Organizational metadata (name/folder/category/favorite/
pinned) changes via dedicated mutators that never version the prompt.

**Variables**: `{{name}}` placeholders are auto-detected from content (`PromptContentAnalyzer`, a
pure static class — no templating library) and validated against declared `PromptVariable`
definitions before a prompt can be saved or executed (`PromptVariableResolver`). Execution-time
resolution is strict (missing required variables block before any provider call, FR-013); preview
resolution is lenient (falls back to default/example values, no AI call).

**Execution and instruction priority**: `ExecutePromptCommandHandler` (Testing Workspace) and
`InsertPromptIntoConversationCommandHandler` (chat insertion) never call an `IAIProvider`
implementation directly or duplicate provider selection — the latter delegates to the existing
`SendChatMessageCommand` unchanged (research.md Decision 4), the same pattern
`StreamVoiceReplyCommandHandler` already established for cross-command delegation via `ISender`.
Both RAG (`IRagService`) and Memory (`IMemoryService`) context are opt-in per execution and reuse
those services verbatim — zero new retrieval/memory logic — passing a fresh per-attempt correlation
id in the `userChatId` parameter slot rather than a real conversation id (confirmed a logging-only
correlation id in both implementations, never a foreign key). Assembled messages follow one fixed
order — system/developer instructions, then memory context (`<user_memory>`-framed), then RAG
context (`<context>`-framed), then the resolved user instructions — so instruction priority stays
structurally distinguishable and no combination of variable/RAG/memory content can override the
prompt's own instructions (FR-083/FR-092). `RetrievalPromptFraming` (`Application/Ai`) holds this
framing text once, shared verbatim with `SendChatMessageCommandHandler`'s own RAG/Memory injection.

**Organization and search**: nested folders (`PromptFolder`, depth computed and stored at
create/move time, cycle rejection enforced at the application layer via
`IPromptFolderRepository.IsSameOrDescendantAsync`), predefined-and-shared or custom-and-private
categories, and per-prompt tags mirror the equivalent `KnowledgeBases` constructs exactly. Search
(`ListPromptsQuery`) is cursor-paginated (keyset, not offset) and full-text indexed
(`FULLTEXT INDEX` on `Prompts`, matching `Conversations`' own full-text search); the `recentlyUsed`
view is driven entirely by `PromptUsageStatistics.LastSuccessfulUseAtUtc`, which only a successful
execution ever advances.

**Export/import**: `PromptExportFileBuilder`/`PromptImportValidator` (`Application/Prompts`) are
plain, dependency-free static classes — not Infrastructure services behind an interface — since
they have no file-system or network dependency, only pure JSON-shape assembly/validation over
already-loaded aggregates. One schema (`{ schemaVersion, prompts: [...] }`) covers both single and
bulk export; import validates every entry before creating any row — a single invalid entry rejects
the whole file, nothing is partially created (FR-071).

**Security**: every Prompt/PromptFolder endpoint is implicitly scoped to the caller via
`PromptOwnershipGuard`, returning `404` (never `403`) for another user's prompt — the same
least-information-disclosure convention `MemoryOwnershipGuard`/`ChatOwnershipGuard` already
establish. No prompt content is ever passed to structured logging above Debug level.

---

# 30. AI Agent Framework & Agent Runtime

Introduced in specs/020-ai-agent-framework. Lives in `Domain/Agents`, `Application/Agents`
(`Tools/`, `Runtime/`, `Commands/`, `Queries/`, `Authorization/`), `Infrastructure/Agents`
(`AgentExecutionHub`/`AgentExecutionNotifier` only — SignalR is never referenced from
`Application`), `Persistence` (via `AskLucyDbContext`), and `AgentsController`/
`AgentExecutionsController`/`AgentPoliciesController` under `Web/Controllers/v1`. Frontend lives
in `ClientApp/src/features/agents`. Supersedes/extends the earlier "Agent Engine"/"MCP Tool
Engine" sketches in §15/§16 with the shipped design — MCP itself remains out of scope (§16 is
still the forward-looking placeholder for it).

**Orchestration model**: `AgentExecutionOrchestrator.RunAsync` (Application, not Infrastructure —
mirrors `IDocumentProcessingPipeline`'s precedent for where a Hangfire-driven, multi-step
orchestration belongs) drives one `AgentExecution` through its plan step-by-step: plan once
(`IAgentPlanner`, one structured `IAIProvider` call, never a bespoke planning API), then for each
step either a reasoning turn or a tool call, accumulating token usage/cost and citations as it
goes. It is fully **resumable**: a pause (user-initiated, or an approval gate) persists state and
returns; the next `RunAsync` invocation reuses the already-persisted plan and rebuilds in-memory
context from whichever steps already completed, so no step ever re-runs and no progress is lost.
A lightweight `IAgentExecutionRepository.GetStatusAsync` (untracked read) checked at every step
boundary lets a concurrent `PauseAgentExecutionCommand`/`CancelAgentExecutionCommand` (issued from
a different HTTP request against its own tracked entity) stop the run within one step boundary
(SC-009: ≤5s) without the two requests conflicting over the same tracked aggregate.

**Tools**: `IAgentTool` is a compile-time-registered catalog (`AgentToolCatalog` wrapping a DI-
resolved `IEnumerable<IAgentTool>`) — no dynamic/runtime tool discovery, since MCP (the platform's
actual dynamic-tool mechanism) is out of scope this release. The eight built-in tools
(`ConversationTool`, `KnowledgeSearchTool`, `DocumentSearchTool`, `MemorySearchTool`,
`MemoryWriteTool`, `PromptExecutionTool`, `FileReadTool`, `FileMetadataTool`) each wrap an
existing platform capability through its existing abstraction (`IRagService`, `IMemoryService`,
`IDocumentRepository`, etc.) — zero new retrieval/search/provider logic, the same "reuse, never
duplicate" rule §28/§29 already establish for Memory/Prompts. Every tool call's permission set is
declared up front (`AgentToolPermission`) and enforced by the tool's own scoped repository/guard
call, never a separate abstract permission registry — an agent's effective access is always the
intersection of its configuration and the executing user's own authorization (FR-049), never
broader (`AgentToolAccessBoundaryTests`).

**Approval gate**: a High/Critical-risk tool call pauses the execution
(`AgentExecutionStatus.WaitingForApproval`, an `AgentApproval` row created `Pending`) unless an
administrator-published `AgentPolicy` matches it (`AgentPolicyEvaluator` — a flat JSON
parameter-equality match against the policy's `ConditionsJson`, empty conditions meaning "always
match"). `ApproveAgentActionCommand`/`RejectAgentActionCommand` decide it and, on approval,
re-enqueue the same execution id to resume. Every decision — interactive or policy-based — is
recorded on the `AgentApproval` row itself (FR-028); no tool ever executes speculatively before a
decision.

**Real-time visibility**: `AgentExecutionHub` (SignalR, `/hubs/agent-execution`) mirrors
`MemoryHub`/`DocumentProcessingHub` exactly — one per-user group (never per-execution), joined via
the server-verified JWT claim. `AgentExecutionNotifier` pushes a live payload at every orchestrator
transition (execution/plan/step/tool-call/approval/usage), each mirroring an already-persisted
`AgentExecutionEvent` row (append-only, safe-metadata-only per FR-035 — never chain-of-thought or
raw provider/tool payloads) so a client that misses a push can always reconcile via
`GET /agent-executions/{id}/events?since=`. The frontend's `useAgentExecutionHub` hook only
invalidates the relevant TanStack Query cache entry on a matching event — the existing 2s REST poll
remains the actual source of truth and reconnect-gap fallback, exactly as `useDocumentProcessingHub`
already established for Document processing.

**Loop/budget protection** (FR-039/040): `AgentBudgetGuard` checks max steps/duration/tokens/
cost/tool-calls/retries (system-wide defaults via `AgentRuntimeOptions`, overridable per-user via
`AgentUserExecutionLimit` for the concurrency cap specifically) before every new step;
`AgentDuplicateToolCallDetector` halts on a repeated identical successful call. A user already at
their concurrency cap is rejected with `429 Too Many Requests`
(`AgentConcurrencyLimitExceededException`), not silently queued (FR-042/043) — checked before any
side effect (e.g. a `NewConversation`-mode conversation creation) so a rejected request never
leaves one behind.

**Versioning and testing**: `Agent.Publish` snapshots the current draft into an immutable
`AgentVersion` (tools/Knowledge Bases/memory policy serialized verbatim); every `AgentExecution`
references the exact `AgentVersionId` it ran under, so a later draft edit or republish can never
retroactively change what an already-started (or historical) execution reports. `Duplicate`/
`Archive`/`Restore`/soft-`Delete` never touch version/execution history (FR-050 audit trail). A
test execution (`isTestExecution: true`, the Testing Console) never invokes a mutating tool
(`WriteFile`/`ModifyData`/`SendEmail`/`ExecuteCode`/`HighRiskOperation` permissions) at all — the
step is recorded `Skipped`, not gated behind an inert approval, guaranteeing zero production-data
changes (SC-007) more simply than relying on nobody approving the result.

**Audit**: `AgentAuditLog` (deliberately not hard-FK'd to `AgentExecution`, mirroring
`KnowledgeBaseAuditLogs`/`MemoryAuditLog`, so an entry for a later-purged execution survives) is a
tamper-resistant record distinct from the operational `AgentExecutionEvent` stream — written at
execution start (`PermissionChecked`), a verified cross-user access attempt (never a genuine 404,
and never on the hot polled `GetAgentExecutionQuery` happy path), a tool's own ownership guard
throwing (`PermissionDenied`), every approval decision (`ApprovalDecided`), and execution
completion/failure.

**Security**: every Agents/AgentExecutions endpoint is implicitly scoped to the caller via
`AgentOwnershipGuard`/`AgentExecutionOwnershipGuard`, returning `404` (never `403`) for another
user's agent or execution — the same least-information-disclosure convention `PromptOwnershipGuard`/
`MemoryOwnershipGuard`/`ChatOwnershipGuard` already establish. `AgentPoliciesController` (policy
CRUD and the per-user concurrency override) is Administrator/Super User only. Retrieved/tool
content is always framed as untrusted data (`RetrievalPromptFraming.BuildToolResultSystemMessage`)
before it re-enters any subsequent provider call, so it can never be interpreted as an instruction.

---

# 31. Model Context Protocol (MCP) Integration

Introduced in specs/021-mcp-integration. Supersedes §16 ("MCP Tool Engine")'s aspirational sketch
with the shipped design — MCP is implemented as one more `IAgentTool` source feeding into spec
020's existing Agent Runtime, never a second, parallel tool-execution framework.

```text
AgentExecutionOrchestrator (unchanged, MCP-agnostic)
        │
        ▼
   AgentToolCatalog
   (merges native IAgentTool + IMcpToolRegistry.ActiveTools)
        │
        ▼
  ┌─────────────┬──────────────────┐
  │ Native tools │  McpToolAdapter  │  ← one per discovered, Active McpTool
  └─────────────┴──────────────────┘
                        │
                        ▼
                 IMcpClientFactory
                        │
                        ▼
                    IMcpClient  (Infrastructure wraps the official MCP C# SDK)
                        │
                        ▼
                External MCP Server
```

Lives in `Domain/Mcp`, `Application/Mcp` (`Tools/`, `Commands/`, `Queries/`, `Resilience/`,
`Validation/`), `Infrastructure/Mcp` (`McpClient`/`McpClientFactory`/`McpEndpointValidator`/
`McpCredentialProtector`/`McpRateLimiter`/the two recurring jobs), `Persistence` (via
`AskLucyDbContext`), and `McpServersController` (admin)/`McpCatalogController` (any authenticated
user) under `Web/Controllers/v1`. Frontend lives in `ClientApp/src/features/mcp`.

**Zero orchestrator coupling**: `AgentExecutionOrchestrator` has no MCP-specific branch anywhere —
`AgentToolCatalog`'s constructor changed from `(IEnumerable<IAgentTool>)` to
`(IEnumerable<IAgentTool> nativeTools, IMcpToolRegistry mcpToolRegistry)`, and that one signature
change is the entire integration surface. An MCP tool's namespaced identity (`mcp:{serverId}:{toolName}`)
flows through every existing native-tool mechanism unmodified: `AgentPolicy.ToolName` matching,
approval-gate risk checks, duplicate-call detection, `AgentToolCall.ToolName` persistence.

**`McpToolAdapter`** wraps rate limiting (`IMcpRateLimiter`), connection acquisition
(`IMcpClientFactory`, singleton, connection-pooled per server), a circuit breaker + retry policy
for idempotent operations only (`McpConnectionResiliencePolicy` — a tool call itself is never
retried, since its success/failure is ambiguous after a dropped connection), and a defense-in-depth
output re-check (`IJsonSchemaValidator`) on top of the Agent Runtime's own existing input/output
validation. A failed call always resolves to the ordinary `AgentExecutionErrorCategory.ToolFailure`
at the execution-history level (FR-032); the granular cause (`McpFailureCategory`) is embedded as a
`[CategoryName]` prefix in `AgentToolResult.FailureReason` — never written to `McpAuditLog`, which
is scoped to administrative/security events and deliberately never duplicates per-execution
tool-call activity already captured by `AgentToolCall`.

**Security boundary**: every remote endpoint is SSRF-validated (`IMcpEndpointValidator` — rejects
private/loopback/link-local/cloud-metadata addresses) both at registration/update time and again on
every new connection (closing the DNS-rebinding gap where a hostname was safe at registration but
resolves elsewhere later); credentials are Data-Protection-encrypted at rest and never appear in any
DTO, log, or audit record; a `McpTool` always starts (or reverts to, on any detected schema/
description change) `PendingReview` — an administrator must explicitly activate it regardless of
what risk level the server itself declares, which is advisory-only input.

**MCP-agnostic runtime, MCP-aware discovery**: capability discovery (`RefreshMcpCapabilitiesCommand`,
also driven by a Hangfire recurring job per server's own configured interval) and health checks
(`McpServerHealthCheckJob`, another recurring job reusing the same on-demand
`TestMcpServerConnectionCommand` handler) are the only places MCP-specific protocol concepts exist;
`IMcpToolRegistry.InvalidateAsync()` is called after every state change that could affect which
tools are callable (activation, deactivation, server enable/disable, health transition), so
`ActiveTools` — the live, in-memory snapshot the orchestrator reads — never drifts from the
database for longer than one invalidation cycle.

# 32. Workflow & Tool Orchestration Engine

Introduced in specs/022-workflow-orchestration-engine. Lives in `Domain/Workflows`,
`Application/Workflows` (`Runtime/`, `Commands/`, `Queries/`, `EventTriggers/`, `Validation/`,
`Expressions/`, `Authorization/`), `Infrastructure` (`WorkflowExecutionHub`/
`WorkflowExecutionNotifier` only — SignalR is never referenced from `Application`), `Persistence`
(via `AskLucyDbContext`), and `WorkflowsController`/`WorkflowExecutionsController`/
`WorkflowPoliciesController` under `Web/Controllers/v1`. Frontend lives in
`ClientApp/src/features/workflows`. Coexists with, never replaces, the Agent Runtime (§30): an
Agent is goal-driven with the model deciding its next action; a Workflow is an explicit,
predefined, deterministic node graph — an AI Agent may be one node inside a Workflow, but a
Workflow never re-implements the Agent Runtime's own planning loop.

**Orchestration model**: `WorkflowExecutionOrchestrator.RunAsync` (Application, not
Infrastructure — the same "Hangfire-driven, multi-step orchestration belongs in Application"
precedent §21/§27 already establish) walks a published `WorkflowVersion`'s node graph from its
`Start` node, dispatching each node through a uniform `IWorkflowNodeExecutor` interface regardless
of node type (`Start`/`End`/`AiPrompt`/`AiAgent`/`RagSearch`/`MemorySearch`/`DocumentProcessing`/
`FileOperation`/`McpTool`/`NativeTool`/`Transform`/`Condition`/`Parallel`/`Merge`/`HumanApproval`/
`Validation`/`Delay`), resolving `{{...}}` variable/step-output references via a sandboxed
`IWorkflowExpressionEvaluator` before each dispatch — never arbitrary user-supplied C#/JavaScript.
It is fully **resumable**: a Human Approval pause, a user-initiated pause, or a manually retried
failed node all persist state and return; the next `RunAsync` invocation reuses whichever
`WorkflowExecutionNode` rows already completed and resumes from the first `Pending`/
`WaitingForApproval` row, exactly mirroring `AgentExecutionOrchestrator`'s (§30) own
resume-without-re-running guarantee. A lightweight `IWorkflowExecutionRepository.GetStatusAsync`
(untracked read), checked first thing every dispatch-loop iteration, lets a concurrent
`PauseWorkflowExecutionCommand`/`CancelWorkflowExecutionCommand` stop the run at the next node
boundary without conflicting over the same tracked aggregate (FR-048, SC-007).

**Node model**: every capability node (`AiPrompt`/`AiAgent`/`RagSearch`/`MemorySearch`/
`DocumentProcessing`/`FileOperation`/`McpTool`/`NativeTool`) is a thin adapter wrapping an
*existing* `IAgentTool` from the Agent Runtime's own catalog (§30) via `AgentToolCatalog` and a
shared `WorkflowCapabilityToolInvoker` — zero new retrieval/search/provider/MCP logic, the same
"reuse, never duplicate" rule §28/§29/§30 already establish. Security inheritance follows
automatically: `WorkflowNodeExecutionContext.UserId` (the execution's own initiating user, set
once at start, never re-derived per node or accepted from node configuration) is passed straight
into `AgentToolExecutionContext.UserId`, so a node's effective access is always exactly what the
underlying tool already enforces for that user — never broader (`WorkflowToolAccessBoundaryTests`,
SC-005). `Condition`/`Parallel`/`Merge`/bounded-`Transform`-loop nodes are pure control-flow, no
tool involved; `Parallel` respects a configurable max-concurrency semaphore and one of four Merge
strategies (All Completed/First Completed/Any Completed/Collect All).

**Approval gate**: reuses the Agent Runtime's exact risk-based pause pattern (§30) rather than a
parallel implementation — a `HumanApproval` node, or any High/Critical-risk capability node,
pauses the execution (`WaitingForApproval`, a `WorkflowApproval` row created `Pending`) unless an
administrator-published `WorkflowPolicy` matches it (`WorkflowPolicyEvaluator`, the same flat
JSON parameter-equality match `AgentPolicyEvaluator` uses). A workflow author's own stricter
`ApprovalPolicy` opt-in can never be bypassed by a `WorkflowPolicy` — only the platform's own
baseline is ever policy-matchable.

**Error handling**: per-node retry with configurable backoff (`WorkflowNodeRetryPolicyParser`),
idempotency-key reuse of a prior `Completed` row's output for mutating nodes retried after a
pause/resume cycle (`WorkflowNode.IdempotencyKeyExpression`), per-node timeout via a linked
`CancellationTokenSource`, and workflow-level failure strategies (Stop/Continue/Retry/Fallback/
Compensate, `WorkflowErrorPolicyParser`) that govern what happens when a node exhausts its own
retries. `Fallback`/`Compensate` both reuse the single `WorkflowNode.CompensatingNodeId` field for
mutually-exclusive purposes (run instead of me vs. run to undo me) — no second field, since
spec.md never described one.

**Event-driven triggers** (FR-063/FR-064): the one place this feature extends an existing
module's public contract rather than only reusing one — `DocumentUploadedNotification`/
`DocumentProcessedNotification`/`KnowledgeBaseUpdatedNotification` (MediatR `INotification`s) are
published, immediately after an already-successful commit, from the three existing handlers that
own those state transitions. `WorkflowEventTriggerHandler` (one `INotificationHandler<T>` per
event type) matches the event against every published Event-Driven `Workflow`'s trigger scope,
re-checks the **workflow owner's** current authorization (not the event's own actor's — the
trigger runs as whoever configured it) and the same concurrency cap a manual start respects, then
starts an execution exactly as `StartWorkflowExecutionCommand` would. This is the first real
instance of "domain events dispatched after a successful commit" (constitution §3) actually
implemented anywhere in this codebase.

**Real-time visibility**: `WorkflowExecutionHub` (SignalR, `/hubs/workflow-execution`) and
`WorkflowExecutionNotifier` mirror `AgentExecutionHub`/`AgentExecutionNotifier` (§30) exactly —
one per-user group, a live push at every orchestrator transition mirroring an already-persisted
`WorkflowExecutionEvent` row, and a 2s REST poll as the actual source of truth/reconnect-gap
fallback.

**Versioning**: `Workflow.Publish` snapshots the current draft (`DraftDefinitionJson`, parsed by
the Application layer — Domain never parses raw JSON) into an immutable `WorkflowVersion`; every
`WorkflowExecution` references the exact `WorkflowVersionId` it ran under, so a later draft edit
or republish can never retroactively change what an already-started or historical execution
reports (FR-014, mirrors §30's `AgentVersion` guarantee identically). `Disable` stops event-trigger
dispatch only (manual starts remain allowed); `Deprecate` is one-way and blocks both manual and
event-triggered starts.

**Audit**: `WorkflowAuditLog` (deliberately not hard-FK'd to `Workflow`/`WorkflowExecution`,
mirroring `AgentAuditLog`) records creation/modification/publication, execution
start/completion/failure/cancellation, approval decisions, a verified cross-user access attempt,
and a node's own permission denial — written by the calling command handlers/orchestrator, never
inside a guard itself (guards stay pure).

**Security**: every Workflows/WorkflowExecutions endpoint is implicitly scoped to the caller via
`WorkflowOwnershipGuard`/`WorkflowExecutionOwnershipGuard`, returning `404` (never `403`) for
another user's workflow or execution — identical to `AgentOwnershipGuard`'s convention (§30).
`WorkflowPoliciesController` (policy CRUD and the per-user concurrency override) is
Administrator/Super User only. A workflow's effective permissions are always the intersection of
its configuration and the executing user's own authorization, never broader — the same guarantee
§30 establishes for Agents, extended here through every node type rather than re-derived.

# 33. Architecture Principles

Before implementing any feature, ask:

* Does it violate Clean Architecture?
* Is the module reusable?
* Is the provider abstracted?
* Can it be unit tested?
* Is it secure?
* Is it scalable?
* Is it maintainable?
* Does it preserve backward compatibility?
* Does it minimize coupling?
* Can it evolve without major refactoring?

If the answer to any of these questions is "No," redesign the solution before writing code.

The architecture is considered a long-term asset and must take precedence over short-term implementation speed.
