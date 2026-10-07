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
> **Last Updated:** October 2026 (v2.1: added §36 Notification Hub, specs/067; earlier: §29 Prompt Library & Prompt Engineering Workspace, specs/019)

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

## The viewer is a platform, not a feature

`src/viewer/` sits alongside `features/` rather than inside it. It is the extensible rendering
platform behind the Flumeria workspace (specs/027, 049-052) — engine, camera, layers, panels,
extensions — and features consume it through the published `IViewerEngine` surface rather than
reaching into it. `src/AskLucy.Web/ClientApp/src/viewer/README.md` is its orientation document.

One rule from that package is worth stating here, because violating it is easy and the symptom is
remote from the cause: **the viewer camera belongs to the user.** Only an explicit user action or a
deliberately established location may move it — never a remount, and never new data arriving about
the place already on screen. See [ADR 0015](adr/0015-viewer-camera-belongs-to-the-user.md).

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

## Credential resolution order: database first, configuration as fallback only

Every provider resolves its API key from the administrator's stored `AiProvider.CredentialCiphertext`
(decrypted through the credential protector) and reads configuration only when no stored credential
exists. This is the platform-wide rule, not a per-provider choice — the long-term direction is that
configuration keys disappear entirely and the database is the only source.

It is written down because the two once disagreed: chat and image generation read `OpenAI:ApiKey`
from configuration and never consulted the database, while embeddings preferred the database. Saving
a bad key in the admin UI therefore broke memory and RAG with a 401 while chat carried on working
from the config key — which presents as "OpenAI is half down" rather than "that key is wrong". A
provider that adds a new capability must route it through the same resolver as the existing ones.

A stored credential that cannot be decrypted (a Data Protection key-ring change) is reported as
*credential unreadable* — it never silently falls back to configuration, because falling back would
turn a recoverable, nameable state into a confusing "not configured" or a 401 from some other key.

## Failure classification

Every provider-originated failure classifies to exactly one `AiProviderFailureKind`
(specs/043, ADR 0008): credential rejected, credential unreadable, not configured, quota
exhausted, rate limited, usage restricted, unavailable, request invalid, or response not
understood. There is deliberately no "internal error" member — that condition is the *absence*
of a classified provider failure, and continues to map to the generic 500.

The split across layers follows the dependency rule:

- **Domain** owns `AiProviderFailureKind`, because `AIProvider` and `ProviderHealthCheck`
  persist it as part of their own state and Domain may reference nothing.
- **Application** owns the `AiProviderException` hierarchy keyed by that enum. The four
  original exception types are subtypes, so existing catch-by-type sites are unaffected.
- **Infrastructure** owns `AiProviderResponseClassifier`, the single place an
  `HttpResponseMessage` and a vendor error envelope become a classification. Vendor reason
  codes never escape this layer.

Two invariants hold across all of it:

1. **The vendor reason wins over the HTTP status.** Google returns 403 for an invalid key, a
   disabled API and disabled billing alike; status alone told administrators to check a key
   that was fine.
2. **The vendor response body never enters an exception message.** It is logged server-side,
   truncated. The message becomes the administrator-visible Problem Details `detail`, and the
   classification is disclosed to administrators only — end-user messaging is unchanged.

Provider health records the same `Kind` alongside its coarse status, and the admin provider
list returns a computed staleness horizon (3x the configured check interval) so a status that
has not been reconfirmed is never presented as current fact.

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

## Account recovery runs on a worker for a security reason, not a performance one

`POST /auth/forgot-password` enqueues and returns immediately. That is not about response time —
it is what makes the endpoint enumeration-resistant. Every step of issuing a reset link (eligibility
read, throttle count, supersede sweep, save, send) costs database round trips against an address
that has an account and nothing against an address that does not. On a remote database that
difference is over a second: a timing oracle no amount of neutral response body can hide. Keeping
all of it off the request thread is what makes the four account states indistinguishable.

The token is protected with `IDataProtector` before it becomes a Hangfire job argument, because job
arguments are serialised into the same database the design deliberately keeps hash-only. See
[ADR 0009](adr/0009-owned-password-reset-token.md).

## A recurring job whose runs can overlap must declare it

A recurring job that can take longer than its own schedule interval will be started again while
the previous run is still going. `DocumentStatisticsRecomputeJob` carries
`[DisableConcurrentExecution(timeoutInSeconds: 0)]` for that reason: it sweeps every user's
statistics against a remote database, so two overlapping sweeps load, refresh and save the same
concurrency-checked rows and one always dies with a `DbUpdateConcurrencyException` — which is how
three copies piled up stuck in `Processing`. The `0` timeout means a run that cannot take the lock
gives up immediately rather than queueing behind its predecessor; nothing is lost, because each
sweep is a full idempotent recompute and the next interval covers it. Apply the same attribute to
any new recurring job that writes rows a sibling run would also write.

## The Hangfire dashboard authenticates through a purpose-scoped cookie, not the SPA's bearer token

`GET /hangfire` is a separate, server-rendered surface that a browser reaches via a genuine new
top-level tab (`window.open`), so the SPA's bearer token cannot ride along. `POST
/api/v1/admin/hangfire/session` (Administrator/Super User only) mints a token through the existing
`ITokenService` carrying a `purpose=hangfire-dashboard` claim and sets it as an httpOnly,
`Path=/hangfire` cookie; the default JWT Bearer scheme's `OnMessageReceived` reads it for
`/hangfire` requests only, and rejects any token missing that claim — including an otherwise valid
ordinary session token. No second authentication scheme was added. The dashboard's palette is
themed to match Ask Lucy via two embedded stylesheets registered through `Hangfire.Dashboard.
DashboardRoutes.AddStylesheet`/`AddStylesheetDarkMode`, but Hangfire 1.8.24 has no mechanism to
select dark mode from anything other than the browser/OS `prefers-color-scheme` setting — the
dashboard's colors always match Ask Lucy, but light/dark *selection* does not follow the admin
panel's own theme toggle. See [ADR 0013](adr/0013-hangfire-dashboard-scoped-session-and-theming-gap.md).

## Provider-sourced avatar sync runs off the sign-in path, and its fetch is host-restricted

Google/Facebook sign-in and account-linking populate `FirstName`/`LastName` synchronously (a
cheap read-then-write against the profile already being resolved), but the profile picture is
fetched and stored by `ExternalProfilePictureSyncJob`, enqueued via `IBackgroundJobClient` the
same way `IPasswordEmailJob`/`IPasswordResetIssuanceJob` are elsewhere in the Authentication
feature area. A slow or failing picture fetch must never add latency to, or fail, sign-in itself.
The job validates the claim-supplied picture URL's host against a small per-provider allow-list
before fetching, and both it and `UploadAvatarCommandHandler` now share `IImageContentValidator`
for magic-byte content validation — a claim value from a third party's OAuth response is still
external input driving a server-side fetch. See
[ADR 0014](adr/0014-external-profile-picture-fetch-hardening.md).

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

## Request logging is Serilog's, not ASP.NET Core's

`app.UseSerilogRequestLogging()` emits one compact structured summary per request (method, path,
status, elapsed). ASP.NET Core's own hosting diagnostics would emit a "Request starting"/"Request
finished" pair for the same request, so `Microsoft.AspNetCore.Hosting.Diagnostics` is pinned to
`Warning` in `appsettings.json` while `Microsoft.AspNetCore` stays at `Information` — the rest of
the framework's information-level events are still wanted, only the duplicated pair is not. If a
request appears twice in the log, that override has been lost; do not solve it by removing
`UseSerilogRequestLogging`.

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

## Two cases where the handler must stand down

`ProblemDetailsMiddleware` has exactly two escape hatches, and both exist because writing a
problem response would make things worse than the original failure:

* **The client disconnected.** An `OperationCanceledException` raised while this request's own
  `HttpContext.RequestAborted` is signalled is the caller hanging up, not a server fault; it is
  logged at its own level and not mapped. An `OperationCanceledException` from any *other* source
  is a real failure and is still classified and reported — the `RequestAborted` check is what
  keeps the two apart.
* **The response has already started.** Once the status line and headers are on the wire — the
  normal state of a streaming endpoint such as the chat SSE stream — setting `StatusCode` throws
  a *second* exception from inside the error handler, which replaces the real one in the log and
  escapes to Kestrel, which resets the connection. That reset is what a browser surfaces as
  `ERR_HTTP2_PROTOCOL_ERROR` on an apparently-200 response. The middleware logs the path and
  status it would have written and returns; the connection simply ends.

Neither is a silent failure: both are recorded before the handler stands down. What is suppressed
is only the attempt to *deliver* a response that cannot be delivered.

Errors are written with `WriteAsJsonAsync(..., contentType: "application/problem+json")`. Setting
`Response.ContentType` beforehand does not work — the no-content-type overload unconditionally
overwrites it with `application/json`, which silently made every error response non-compliant
until the explicit argument was added.

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

**Anonymous consent and the login-time merge** (specs/023-flumeria-landing-experience,
ADR 0011): a second, parallel consent store exists for signed-out visitors, since
`/users/me/cookie-consent` is `[Authorize]`-only. `PublicConsentGate`/`PublicConsentBanner`
(`ClientApp/src/features/consent/{components,hooks}/*Public*`) read/write a first-party
`flumeria_public_consent` browser cookie instead, keyed by policy version the same way the
account-level record is. On `useLogin`/`useLoginTwoFactor`/`useCompleteExternalLogin`
success, `migratePublicConsentToAccount()` promotes that cookie's decision into the
account's DB record **only if the account has no record yet** — an existing account-level
decision (e.g. from another device) always wins over this browser's anonymous one. This
is what makes "ask once per browser, never twice for the same policy version" hold across
the anonymous→authenticated boundary instead of `ConsentGate` re-blocking every new user
immediately after their first sign-in.

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

**Tool registration shape is load-bearing.** Register every tool directly —
`services.AddScoped<IAgentTool, TheTool>()` — never as `services.AddScoped<IAgentTool>(sp =>
sp.GetRequiredService<TheTool>())`. The factory shape re-enters `ServiceProvider.GetService` from
inside a call-site visit that already holds the runtime resolver's lock; once the tool's own graph
is deep enough for `StackGuard` to move the rest of the resolution onto another thread, the two
deadlock and **every** request resolving `IEnumerable<IAgentTool>` hangs forever — no exception,
no log entry, and invisible to startup DI validation. This is not hypothetical: it hung every chat
turn during specs/057 and was only found with `dotnet-stack`. Add a concrete-type registration
alongside it only when something genuinely resolves the tool that way (e.g. a `ScopeIsolated*`
wrapper), never as the route through which `IAgentTool` itself is satisfied.

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

# 33. Site Analysis Agent

Introduced in specs/057-site-analysis-agent. Lives in `Domain/SiteAnalysis`,
`Application/SiteAnalysis` (`Tools/`, `Providers/`, `Queries/GetSiteAnalysis`,
`Queries/ListSiteAnalysesByChat`), `Infrastructure/SiteAnalysis` (`SiteAnalysisHub`/
`SiteAnalysisNotifier`, `SystemWorkflowProvisioner`, `RemoteFileDownloader` — only these reference
SignalR/HttpClient, never `Application`), `Persistence` (via `AskLucyDbContext`), and
`SiteAnalysesController` under `Web/Controllers/v1`. Frontend lives in
`ClientApp/src/features/siteAnalysis`.

**A hierarchical agent, not a new orchestration mechanism.** Lucy hands a resolved site to a
coordinating agent, which fans the analysis out to independent specialists and relays only
validated findings back — but the fan-out itself is a system-owned Workflow (§32)
(`Start → Parallel → [one NativeTool node per specialist] → Merge → End`), not a new execution
engine. `Workflow.IsSystemOwned`/`SystemKey` (mirrors `Agent.IsSystemOwned`/`SystemKey`, §30
exactly) let `SiteAnalysisDispatcher` start it directly — bypassing `StartWorkflowExecutionCommand`,
whose `WorkflowOwnershipGuard` (`OwnerId == userId`) can never be satisfied by a workflow shared
across every user. `SystemWorkflowProvisioner`/`SystemWorkflowProvisioningHostedService` upsert
that workflow at startup, mirroring `SystemAgentProvisioner` (§30) including its
defer-on-pending-migrations behavior. Each specialist is a plain `IAgentTool` behind a `NativeTool`
node (never `AiAgent` — that node type resolves agents by owner and cannot find a system-owned
one, and discards the triggering chat id); adding a further specialist (Site Geometry, Urban
Context, Connectivity & Access, Environmental Context, Character Analysis — all deferred) is one
provisioner branch entry plus one tool class, no orchestration change.

**The relay, not the workflow engine, delivers findings.** `WorkflowExecutionOrchestrator`
batches a Parallel node's branch-completion notifications until every branch settles — riding
those would deliver every finding at once. Instead, each specialist tool calls
`ISiteAnalysisResultRelay.ReportSuccessAsync`/`ReportFailureAsync` **inline**, as the last step of
its own execution, so the fastest specialist is delivered the moment it finishes, independent of
its siblings. The relay is the only component permitted to validate, persist, and deliver — a
specialist never pushes a panel or a chat message itself. Delivery is: persist the
`SiteAnalysisResult`; persist a real assistant `Message` for the chat notice (not only a transient
push — a reload must still show it via ordinary message history); push `SiteAnalysisResultReceived`
(a dedicated `SiteAnalysisHub`, since chat itself streams over SSE per turn, not SignalR) for
immediate rendering; push the finding's panel via the existing `IPanelNotifier`/`PanelHub`
(specs/028, unchanged). When every specialist has settled, an atomic
`SiteAnalysis.TryClaimClosingOutcome` (a `RowVersion`-guarded claim, not a distributed lock) picks
exactly one caller to emit a single closing outcome — silent when everything succeeded, one brief
line otherwise — so concurrent final reports can never double-announce completion.

**Concurrency isolation.** `WorkflowExecutionOrchestrator`'s own Parallel branches share one
scoped `DbContext` (its own doc comment: "neither is thread-safe" for the orchestrator's
bookkeeping). No prior `IAgentTool` wrote to the database, so this was latent until this feature's
specialists became the first to. `ScopeIsolatedSiteAnalysisResultRelay` (mirrors
`ScopeIsolatedLocationResolutionService`, itself created after an identical race caused a
production `DbUpdateConcurrencyException`) resolves a fresh `IServiceScopeFactory`-created scope
per relay call, so concurrent specialists never share a `DbContext` instance regardless of how many
run side by side.

**Provenance, never invented.** Every finding carries a `SiteAnalysisResultMetadata` — analysis
type, data source, a rule-based `SiteAnalysisConfidenceLevel` (High/Medium/Low, computed by
`SiteAnalysisConfidence.ResolveConfidence` from plain grounding facts), and generation time —
composed into a trailing `keyValue` block by `SiteAnalysisContentComposer`. Confidence is never
derived from `GeocodingCandidate.Importance`: Google and Nominatim populate that field on
incompatible scales, a divergence that already caused a production defect once. Three stub
provider interfaces (`IZoningDataProvider`/`IFloodDataProvider`/`IClimateDataProvider`) exist with
no implementation — OpenStreetMap, the only geospatial source integrated today, has none of floor
area ratio, flood modelling, or wind/climate data; a deferred specialist reports "unavailable" with
a reason rather than presenting an estimate as measured.

**No new content vocabulary.** Findings compose entirely from the existing panel content-block
vocabulary (heading/text/keyValue/image) — the same envelope `PresentPanelContentCapability`
already produces. The one specialist in this release, `SiteSchematicImageGenerationTool`, generates
through the platform-wide `IImageGenerationService` and persists the result through the existing
`DocumentUploadFinalizer` — only a platform `Document` id ever reaches an `ImageBlock`.

**Image generation (platform-wide).** One service, `IImageGenerationService`, serves every caller —
the chat's image command and the site-analysis map. Its provider **and model** come from the
`ImageGeneration` AI capability: `AiCapabilityAssignment` gained an optional `ModelId` that pins one
of the provider's models (null everywhere else keeps following the provider's default), and
`ImageGeneration` requires a pinned `SupportsImageOutput` model with **no platform-default fallback**
— an unassigned capability throws `AiCapabilityNotConfiguredException` (→ 503) instead of asking a
chat model to draw. `IAIProvider.GenerateImageAsync` returns a `GeneratedImagePayload` in whatever
form the vendor produced (hosted URL, base64, data URL, binary — OpenAI's GPT image models return
base64 only, DALL·E returns a URL, Gemini returns `inlineData`); `GeneratedImageMaterializer`
normalises every form to bytes and verifies the format from the content itself (PNG/JPEG/WebP)
before storage. Generated images are always stored as the user's `Document`; a chat `Image`
message's content is that document id, resolved to a fresh signed URL at render time — a provider
URL is never persisted or handed to the client.

**Rehydration.** `SiteAnalysis`/`SiteAnalysisResult` persist every outcome — including failed and
rejected ones, for operator diagnosis — so `GET /api/v1/site-analyses/{id}` and the
chat-scoped list endpoint can replay a conversation's findings exactly as first delivered after a
reload or navigation, closing the one gap the existing floating-panel framework (session-scoped
only) would otherwise have for a feature whose results can arrive minutes apart.

**Error handling**: every specialist failure is captured at its origin, persisted with its
reason and diagnostic detail, and structure-logged — constitution §2 VIII governs capture and
diagnosability, not end-user disclosure. A failing specialist produces no notice or panel of its
own; the single closing outcome is what guarantees the user is never left with only a start
acknowledgement and silence.

**Security**: `GetSiteAnalysisQueryHandler` scopes every read to the caller
(`ISiteAnalysisRepository.GetByIdForUserAsync`), returning `404` for another user's analysis —
identical to `AgentOwnershipGuard`'s convention (§30). `SiteAnalysisResultDetailDto` deliberately
never exposes `FailureReason`, consistent with the no-per-specialist-disclosure rule above.

# 34. Voice Output Engines

specs/070 replaced the single ElevenLabs text-to-speech dependency with an ordered set of
engines. The rest of the voice pipeline (`TextToSpeechStreamer`, `StreamVoiceReplyCommandHandler`,
the client's MediaSource player) is unchanged: it still speaks through one
`ITextToSpeechProvider`, which is now `VoiceProviderRouter`.

**Engines**: each `ITextToSpeechEngine` (Infrastructure) is one way to turn text into speech —
`ElevenLabsTextToSpeechEngine` (hosted, needs an API key) and `SupertonicTextToSpeechEngine`
(Supertonic 3 running in-process on ONNX Runtime, 44.1 kHz, 32 languages, ten preset voices).
Every engine yields `audio/mpeg`, so the browser's single MediaSource pipeline plays any of them
and a mid-reply failover stays seamless. Supertonic's raw PCM is encoded to 96 kbps mono MP3 by
`Mp3StreamEncoder` (GroovyMp3, fully managed — no native codec to deploy).

**Ordering and failover**: `VoiceProviderRouter` reads the administrator's `VoiceProvider` rows
(priority 0 is Lucy's voice) and tries each engine in order. It fails over only before the first
audio chunk: a failure after the listener has heard part of a sentence is rethrown, because
finishing the sentence in a different voice is worse than the existing audio-failed path. An
engine that fails is skipped for the rest of the request (the router is scoped), and an engine
whose stored credential cannot be decrypted is treated as failed rather than crashing the reply.
Only when every engine fails does the caller see an `AiProviderUnavailableException`, which
`TextToSpeechStreamer` already turns into an `audio-failed` event and the browser-voice fallback.
A user's voice override applies only to the engine it was resolved for; a failover engine speaks
with its own administrator-chosen voice.

**Supertonic model hosting**: `SupertonicModel` is a process-wide singleton that loads the four
ONNX sessions lazily on first use (fp32; int8 was rejected as unintelligible) and caps concurrent
syntheses with `Supertonic:MaxConcurrentSyntheses`, since each synthesis holds a few hundred MB of
working memory. The model files are not in the repository — `scripts/download-supertonic.ps1`
installs them at a pinned Hugging Face revision with SHA-256 verification. A missing file fails
only the request that needed it, as `AiProviderUnavailableException`, so the router fails over
instead of the host refusing to start.

**Administration**: the admin **Voice** page (`/admin/voice`, `admin.ai-providers.view` to see,
`.manage` to change) lists the configured providers, adds an installed engine with **+**, lists the
chosen provider's voices, previews a sample sentence through that provider only (never failing
over — the administrator is auditioning a specific voice), and makes a provider/voice Lucy's voice
by renumbering priorities densely with the chosen provider first.

**Licensing**: Supertonic's code is MIT; its weights are OpenRAIL-M, whose use restrictions must
be passed through to end users. See `docs/THIRD_PARTY_NOTICES.md`.

**Model availability** (specs/072): an on-server engine implements `IHostedModelEngine` as well,
naming the Hugging Face repository it runs (`Supertone/supertonic-3`). It asks `IHostedModelLocator`
where its files are rather than assuming a fixed folder — see §35. `FindModelProblemAsync` is a
read-only check (it never loads the model) that the admin Voice page uses to show a **Model
unavailable** chip, and the **+** menu omits an engine whose model an administrator has marked
Unavailable.

# 35. Custom Models

specs/072 lets an administrator deploy a Hugging Face model repository to the production host from
**Admin → AI Providers → Custom models**, without routing any bytes through their browser.

**Flow**: `SubmitCustomModelDeploymentCommand` validates the source (`HuggingFaceModelSource` — the
`huggingface.co` host only, optional `/tree/<rev>` or `/resolve/<rev>`) and the destination
(`DeploymentDestination` — relative, no `..`, under an allowed prefix, `Models/` or
`App_Data/Models/` by default), persists a `CustomModel` and enqueues `CustomModelDeploymentJob` on
Hangfire. The job lists the repository at a pinned commit (`IModelRepositorySource`), enforces the
size cap before and during the download (`CustomModels:MaxDeploymentBytes`), streams each file to
server temp storage with integrity checks, uploads it through `IDeploymentFileUploader`, then deletes
the temp files whatever the outcome. Progress is pushed on `CustomModelDeploymentHub`
(`/hubs/custom-model-deployments`, `admin.custom-models.view`) and persisted periodically, so a page
reload resumes from the last known state. Every failure ends as a `Failed` state with a
`CustomModelFailureKind` and a reason the admin can read; a deployment interrupted by a restart is
swept to `Failed` (`InterruptedByRestart`) by `CustomModelDeploymentRecoveryHostedService`.

**SSRF guard**: `HuggingFaceModelRepositorySource` refuses redirects off Hugging Face's own CDN
hosts (`HuggingFaceRedirectPolicy`), and `SafeConnectCallback` refuses to connect to a private,
loopback or link-local address whatever the host name resolved to.

**Deployment target — deliberately temporary**: the job never reads FTP settings itself. Everything
about the remote target comes through `IDeploymentTargetSettingsProvider`, whose one
implementation, `ConfigurationDeploymentTargetSettingsProvider`, reads the `Ftp` configuration
section (`Host`, `Port`, `Username`, `Password`, `RootPath`, `AllowPlainFtp`). A future Connectors
feature (spec 071) replaces that one class; nothing else changes. Replacing it is the planned
design, not a regression — see ADR 0016. FTPS is required unless `AllowPlainFtp` is set, in which
case the section shows a **Plain FTP** warning chip. The password, host and root path never appear
in a log line, an API response or an exception message.

**Overwrites**: a deployment that replaces a file already on the target records it in
`CustomModelOverwrittenFiles` (path, previous size, time), so the admin can see exactly what was
replaced.

**Availability and hosted engines**: a completed model is Available or Unavailable, with at most one
Available model per repository (enforced by a filtered unique index behind the handler's check).
`IHostedModelLocator` (`ScopedHostedModelLocator`, a singleton opening a fresh scope per call, no
cache) tells an engine which folder to load its repository from:

* **Available** — the engine loads from that model's destination.
* **Unavailable** — the engine refuses with `AiProviderUnavailableException`, so the voice router
  fails over and logs why.
* **No record** — the engine keeps its pre-072 behaviour (its configured folder). Only Completed,
  non-deleted records count, so a server that has never used Custom Models behaves exactly as before.

`CustomModelSummaryDto.BacksEngine` names the engine a model's repository feeds, so the admin can
see what making it Available will affect.

# 36. Notification Hub

specs/067 gives every module one way to say *what happened*, and one place that decides who is told,
on which channel, in which language and with which template. Modules never send email, write a
notification row or push over SignalR themselves. The decision record is
[ADR 0018](adr/0018-transactional-notification-outbox.md); the frontend language and right-to-left
decision is [ADR 0019](adr/0019-frontend-i18n-and-rtl.md).

**Flow**: a module calls `INotificationPublisher.Publish(...)` inside its own unit of work. That adds
a `NotificationOutboxEvent` row that commits or rolls back with the module's own change, so a crash
can neither lose a notification nor announce a change that was rolled back. After the commit,
`NotificationWakeInterceptor` pulses `INotificationWakeSignal`. The outbox dispatcher claims the
event, routes it, and materializes one `Notification` per recipient plus one `NotificationDelivery`
per channel. In-app deliveries are created already `Delivered` and pushed over SignalR. Email
deliveries are queued for the delivery worker, which renders, sends and records the result.

```
Module handler ──Publish──► NotificationOutboxEvents ──(same SaveChanges)
                                   │  wake signal after commit
                                   ▼
                     NotificationOutboxDispatcher (BackgroundService)
        claim → NotificationRouter → materialize Notification + Deliveries
                    │ InApp: Delivered, pushed on /hubs/notifications
                    │ Email: Pending
                                   ▼
                     NotificationDeliveryWorker (BackgroundService)
       claim → resolve language, mint link → INotificationChannelSender → record result
```

## Emitting: `INotificationPublisher`

`Publish` is synchronous and performs no I/O. A request names a catalogue type (a
`NotificationTypeKeys` constant), a `NotificationRecipient`, the declared variables, an optional
`RelatedItem` (type, id, parent id), an optional `EventKey` for de-duplication and an optional
explicit language. It never carries a channel, template, HTML, URL or secret. It throws only for
programming errors (an unknown type, an undeclared variable, a malformed recipient), which surface
in tests, so a caller does not wrap it in `try/catch`; the caller commits through its own
`IUnitOfWork.SaveChangesAsync`. Recipients are one or more users (at most 100), an announcement
audience (all active users, or roles), an address that belongs to a user, an address to be looked up
in the background (password reset), or the support mailbox (the address comes from server
configuration only). The outbox row holds no display data, so a recipient's name is read at
dispatch, not at publish.

## The type catalogue

`NotificationTypeCatalog` (Domain) is code-owned: one `NotificationTypeDefinition` per type, with its
category, default priority, the default state of each channel it uses (on, off or mandatory), its
declared variables, its route template, and flags such as `ShowInCenter`, `MinimizeSensitiveContent`,
`SensitiveLinkKind`, `RequestValidity`, `RequiresItemAccess` and `EmailOnlyWhenCritical`.
Administrators edit templates, never the catalogue. Types for conversation export and billing are
defined but not emitted (`IsEmitted = false`). Every emitted type ships published `en` and `ar`
templates on each channel it uses, and a catalogue unit test enforces it against the seed files.

## Routing

`NotificationRouter` is a pure Domain function (the FR-003 decision table). For each channel the type
uses it returns send or skip, with a `DeliverySkipReason`: `ChannelDisabled` (no registered sender,
or switched off in configuration), `NotCritical` (an announcement email without the critical flag),
`PreferenceDisabled` (the user's saved override, or the type's default) and `NoVerifiedAddress`.
A mandatory channel ignores preferences but is still skipped when the channel itself is unavailable.
Preferences are sparse `(category, channel)` overrides, so a user with none gets the catalogue
defaults.

## Dispatcher and delivery worker

Both are in-process `BackgroundService` loops in Infrastructure (`Notifications/Workers`). The
logic lives in Application (`OutboxDispatchService`, `DeliveryProcessingService`,
`DeliveryProcessor`), so it is tested with fakes and the hosts stay thin. The workers sit beside
Hangfire, not inside it: Hangfire enqueue is not transactional with EF, and its recurring jobs have
one-minute granularity, which cannot meet the in-app latency target. Hangfire still runs the
recurring maintenance jobs.

* **Claims and leases**: a worker claims a batch with a conditional update that stamps `LeaseOwner`
  and `LeaseExpiresAtUtc`, so two instances never process the same row. The owner id is unique per
  process, so a restarted host never finishes rows leased to its previous life. The lease defaults to
  2 minutes, well above the 60 s SMTP timeout, so a live send never loses its lease.
* **Polling**: a worker runs a pass, then waits for the wake signal or the poll interval, 1 s while
  there is work and 5 s when idle. Each event or delivery is processed in a scope of its own, so one
  failure leaves nothing tracked for the next. A failed pass is logged and backed off; it never ends
  the loop.
* **Dispatcher retry**: an event that throws is released with exponential backoff (10 s, 20 s, 40 s
  and so on, capped at 15 minutes). After 10 attempts it becomes `Failed`, is visible in the admin
  backlog view and is recorded on the operational failure trail (ADR 0017).
* **Idempotent materialization**: `(RecipientUserId, EventKey)` is a unique filtered index, so a
  replayed or duplicated event materializes once and the outcome is `Duplicate`. An announcement's
  fan-out runs in batches and advances `FanOutCursor`, so an interrupted fan-out resumes where it
  stopped.
* **Retry schedule**: a transient send failure schedules the next attempt by priority. Normal and
  lower priorities wait 1, 4, 10, 20 and 30 minutes; `Critical` waits 30 s, 1, 2, 5 and 10 minutes.
  The default is 5 attempts, then `DeadLettered (RetryLimitReached)`. A permanent failure (5xx, a
  render error) is `Failed` at once. Both schedules are configurable under `Notifications:Retry`.
* **At-most-once email**: the channel sender calls `SendProgress.MarkTransmissionStarted()` right
  before the first byte leaves the process. A delivery interrupted before that point goes safely back
  to the queue. One interrupted after it has an unknown outcome, so `LeaseSweepService` (a Hangfire
  job every minute) fails it as `AmbiguousOutcome` and never resends it automatically: a duplicate
  email is worse than one an administrator retries on purpose. The email `Message-ID` is
  `<{deliveryId}@domain>`, so a receiver can collapse a duplicate that does slip through. The sweeper
  also returns outbox events stuck `Processing` past their lease to `Pending`.
* **Rate-limited lane**: `EmailSendRateLimiter` is a singleton token bucket of
  `Notifications:Email:MaxPerMinute`, with `ReservedPerMinuteForMandatory` tokens that bulk mail
  can never use. Mandatory mail (a password reset, a security notice) draws from the reserved lane
  first, so an announcement cannot delay it. When no token is free the sender returns `Deferred`, the
  delivery goes back to the queue without using up an attempt, and the worker waits the returned
  time.
* **Shutdown**: finishing a send and recording its outcome gets a short grace period after host
  shutdown begins, so an accepted email is not recorded ambiguous, but a hung database cannot hold
  the shutdown for ever.

## Send-time rendering and links

An account email's one-time link is minted by `IAccountLinkIssuer` when the worker sends, never when
the event is published. It goes only to the renderer, which turns it into the email's button, so no
token is stored in an outbox row, a notification, a delivery, the audit log or a log line. A refused
issue (`AccountLinkRefusedException`: unconfirmed, locked out, throttled) cancels the delivery with a
safe reason. Every other type's link comes from `INotificationLinkBuilder`, which fills the type's
route template from the related item, URL-encodes every value and rejects anything that would leave
the app. The language is resolved at send time, so a user's change applies to mail still queued.
Notifications that need item access (`RequiresItemAccess`) re-check through
`INotificationAccessCheck` that the recipient can still see the item before they render.

## Templates and the renderer

A template row is one `(Type, Channel, Language)`; its content is versioned (`Draft`, `Published`,
`Archived`) and immutable once published. Fields are structured (subject, preheader, greeting,
heading, up to ten body paragraphs, safety note, footer for email; title and message for in-app) and
the only syntax is `{{ name }}` against the type's declared variables. `LogicFreeTemplateRenderer`
substitutes first and encodes after: in-app output is plain text that the client renders as text,
and the email HTML part encodes every value, so a variable can never inject markup. The email is
wrapped in the branded shell from spec 061, with `lang` and `dir` set from the language. A missing
value takes the variable's fallback and logs a warning. `NotificationTemplateSeeder`, a hosted
service, installs the shipped English and Arabic defaults as a published version 1 for each missing
template and never overwrites an administrator's edit.

## The channel seam

Each channel is one `INotificationChannelSender` (`Channel` and `SendAsync(DeliveryContext)`). A
sender never reads the database for the message: the context carries the resolved address, language,
variables and link. It returns a classified `ChannelSendResult` (sent, delivered, transient failure,
permanent failure or deferred) instead of throwing for a delivery problem, and lets only a host
shutdown through as an exception. `NotificationChannelRegistry` reports which channels routing may
use: in-app unless `Notifications:Channels:InApp:Enabled` is false, plus every channel with a
registered sender that configuration has not disabled. Adding Teams, Slack or SMS means registering
a sender and adding an enum value; the router, publisher and emitters do not change (SC-012). In-app has no sender, because it is delivered at
materialization. `EmailChannelSender` wraps the existing MailKit STARTTLS `IEmailSender`, and
`SmtpFailureClassifier` maps MailKit errors onto retry or give up, storing only the numeric reply and
enhanced status code, never the exception text.

## In-app delivery

`NotificationHub` at `/hubs/notifications` is server to client only: the caller joins the group
`user:{userId}`, built from the authenticated identity, and there are no client-invocable methods, so
every mutation goes through REST with its rate limiting, validation and audit. Pushes
(`notificationCreated`, `notificationUpdated`, `unreadCountChanged`) are best effort and happen after
the commit. A failed push is logged at Warning with the correlation id and does not fail
materialization, because the client refetches the unread count and first page on reconnect.
`DocumentProcessingHub` keeps its stage-progress events, and its old `notificationCreated` push is
gone.

## Retention

`RetentionService`, run by the daily Hangfire job `notification-retention` (03:00), deletes in
batches with `ExecuteDeleteAsync`, each batch in its own scope: read notifications after 90 days,
owner-deleted after 30 days, failed or dead-lettered deliveries 30 days after the last attempt,
finished non-in-app deliveries after 90 days and completed outbox events after 7 days. A notification
that still has an active delivery is skipped. `NotificationAuditLog` rows are never deleted. Windows
are `Notifications:Retention:*`.

## Health and metrics

Four checks are tagged `ready` on `/health/ready`:

| Check | Meaning |
|---|---|
| `notifications-dispatcher` | The dispatcher's heartbeat is fresh (30 s by default); a stale one is Unhealthy. |
| `notifications-delivery-worker` | The delivery worker's heartbeat is fresh; a stale one is Unhealthy. |
| `notifications-backlog` | The oldest due item is within 5 minutes (Degraded) and 30 minutes (Unhealthy). |
| `notifications-smtp` | An SMTP probe, cached for 5 minutes. A failure is Degraded, never Unhealthy, so the site stays up. |

The workers write heartbeats to a singleton store, and the checks read it, so neither depends on the
other. `NotificationMetrics` exposes the `AskLucy.Notifications` meter. The admin Channels view reads
the same cached results. The correlation id travels from the request through the outbox, the
notification and the delivery to every log line.

## Localization

Localization is a platform setting, not a build option. `LocalizationSetting` is a singleton row
(`IsEnabled`, `SupportedLanguagesJson`), seeded disabled with `["en"]`, edited by an administrator
under `admin.notifications.manage` with `If-Match`. `ApplicationUser.PreferredLanguage` holds a
user's choice and is kept when localization is switched off or the language is removed.

`IEffectiveLanguageResolver` answers one question for the whole backend: while localization is off
the answer is always `en`; otherwise it is the first *supported* candidate of the request's explicit
language, the user's own choice, then English. A language that is no longer supported falls through
without touching the user's choice. The setting is cached for 30 seconds
(`CachedLocalizationSettingsProvider`) and evicted on change. Notifications record the language they
were produced in and are never retranslated.

Server text (Problem Details `title` and `detail`) is localized only on controllers and actions
marked `[LocalizedSurface]`, from `Application/Localization/Messages.resx` and `Messages.ar.resx`.
`LocalizedSurfaceCultureMiddleware` runs after authentication, sets the culture for such endpoints
and stores it in `HttpContext.Items`, because an async-local culture does not reach the Problem
Details middleware that wraps it. Every other endpoint stays English. The frontend side (typed
catalogs, `useT`, scoped RTL for the notification screens and the admin area) is described in
ADR 0019.

# 37. Architecture Principles

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
