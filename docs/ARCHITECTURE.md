# ARCHITECTURE.md

> **Project:** Ask Lucy AI Workspace
>
> **Version:** 2.0
>
> **Architecture:** Clean Architecture + Modular Monolith
>
> **Framework:** ASP.NET Core (.NET 10)
>
> **Frontend:** React + TypeScript + Vite
>
> **Last Updated:** July 2026

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

# 27. Architecture Principles

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
