# architecture-rules.md

> **Project:** Ask Lucy AI Workspace
> **Version:** 1.0
> **Status:** Mandatory Engineering Standard
> **Applies To:** All Contributors (Human & AI)
> **Last Updated:** July 2026

---

# 1. Purpose

This document defines the mandatory architectural rules governing the Ask Lucy platform.

These rules ensure that the application remains:

* Maintainable
* Scalable
* Testable
* Secure
* Modular
* Extensible

Every pull request, feature specification, architectural decision, and code review MUST comply with these rules.

---

# 2. Architecture Philosophy

Ask Lucy is designed as a long-lived enterprise SaaS platform.

Architecture decisions MUST prioritize:

* Maintainability over convenience
* Scalability over shortcuts
* Modularity over coupling
* Simplicity over cleverness
* Explicitness over implicit behavior

Never optimize for short-term implementation speed at the expense of long-term quality.

---

# 3. Architectural Style

The application MUST follow **Clean Architecture**.

Primary layers:

```text
Presentation

↓

Application

↓

Domain

↑

Infrastructure
```

Dependencies always point toward the Domain.

The Domain layer must never depend on any other project.

---

# 4. Solution Structure

The solution SHALL be organized as:

```text
src/

AskLucy.Domain

AskLucy.Application

AskLucy.Infrastructure

AskLucy.Persistence

AskLucy.Web

AskLucy.Frontend

tests/
```

No additional projects should be introduced without architectural justification.

---

# 5. Dependency Rule

Outer layers MAY depend on inner layers.

Inner layers MUST NEVER depend on outer layers.

Allowed:

```text
WebAPI
↓

Application
↓

Domain
```

Forbidden:

```text
Domain

↓

Infrastructure
```

---

# 6. Domain Layer

The Domain is the heart of the application.

It MUST contain only business concepts.

Examples:

* Entities
* Value Objects
* Domain Events
* Aggregate Roots
* Business Rules
* Domain Services
* Repository Interfaces

The Domain MUST NOT reference:

* Entity Framework
* SQL Server
* ASP.NET
* MediatR
* AutoMapper
* Serilog
* HTTP
* JSON
* React
* Material UI

---

# 7. Application Layer

The Application layer orchestrates business workflows.

Responsibilities include:

* CQRS
* Commands
* Queries
* DTOs
* Validators
* Mapping
* Authorization policies
* Transactions
* Use cases

The Application layer MUST NOT know how data is stored.

---

# 8. Infrastructure Layer

Infrastructure contains external integrations.

Examples:

* OpenAI
* Anthropic
* Gemini
* SMTP
* PayPal
* File Storage
* Vector Storage
* Logging
* Caching
* External APIs

Infrastructure MUST implement interfaces defined in Domain or Application.

Infrastructure MUST NEVER define business rules.

---

# 9. Persistence Layer

Persistence is responsible only for data access.

Responsibilities:

* DbContext
* Entity Configurations
* Migrations
* Repository Implementations
* Database Transactions

Business rules MUST NOT exist here.

---

# 10. Presentation Layer

Presentation includes:

* Web API
* React application

Presentation is responsible for:

* User interaction
* Request validation
* Authentication
* Formatting
* UI state

Presentation MUST NEVER contain business logic.

---

# 11. CQRS

All business operations SHALL use CQRS.

Commands

* Modify state
* Return minimal data

Queries

* Read state
* Never modify state

One request = One handler.

---

# 12. MediatR

All commands and queries SHALL be executed through MediatR.

Controllers should never directly invoke services containing business workflows.

---

# 13. Domain Events

Business events SHALL be represented as Domain Events.

Examples:

* ConversationCreated
* DocumentUploaded
* UserRegistered

Domain Events MUST describe business occurrences rather than technical actions.

---

# 14. Entities

Entities MUST:

* Have identity
* Protect invariants
* Encapsulate behavior

Entities SHOULD expose methods rather than mutable setters.

---

# 15. Aggregate Roots

Every aggregate MUST have a clearly defined root.

Only Aggregate Roots may be loaded directly from repositories.

Child entities should not be modified independently.

---

# 16. Value Objects

Use Value Objects for concepts without identity.

Examples:

* EmailAddress
* LanguageCode
* Money
* TemperatureSetting
* ModelIdentifier

Value Objects MUST be immutable.

---

# 17. Repository Rules

Repositories abstract persistence.

Repositories MUST:

* Return aggregates
* Hide persistence details
* Avoid business logic

Repositories MUST NOT:

* Perform authorization
* Send emails
* Call AI providers
* Execute unrelated workflows

---

# 18. Services

Business logic belongs in:

* Entities
* Value Objects
* Domain Services

Application Services orchestrate.

Infrastructure Services integrate.

Each service must have a single responsibility.

---

# 19. Dependency Injection

Every service MUST be registered through Dependency Injection.

Avoid service locators.

Avoid static services.

---

# 20. Configuration

Configuration MUST be centralized.

Strongly typed configuration objects are required.

Secrets MUST never appear in source code.

---

# 21. AI Provider Abstraction

The application MUST never depend directly on a specific AI vendor.

All providers implement a common abstraction.

Examples:

```text
IAIProvider

OpenAIProvider

AnthropicProvider

GeminiProvider

OpenRouterProvider
```

Switching providers must not require changes to application logic.

---

# 22. Model Abstraction

Models are configuration, not code.

Examples:

GPT-5

Claude Opus

Gemini Pro

Future models should be configurable without recompilation.

---

# 23. RAG Architecture

The Retrieval-Augmented Generation (RAG) subsystem MUST be modular.

Core responsibilities:

* Document ingestion
* Parsing
* Chunking
* Embeddings
* Retrieval
* Prompt augmentation

Embedding providers and vector stores must be replaceable.

---

# 24. Knowledge Base

Knowledge Bases are independent aggregates.

Documents belong to Knowledge Bases.

Chunks belong to Documents.

Retrieval operates on indexed chunks rather than raw documents.

---

# 25. Conversation Architecture

Conversation is the aggregate root.

Hierarchy:

```text
Conversation

↓

Messages

↓

Attachments

↓

References
```

Messages must not exist independently of a Conversation.

---

# 26. User Preferences

User settings are isolated from authentication.

Preferences include:

* Theme
* Language
* AI Provider
* Default Model
* Temperature
* Streaming
* Voice

Authentication data must remain separate.

---

# 27. Authentication

Authentication SHALL use ASP.NET Identity.

Business logic must never depend directly on Identity classes.

Wrap framework-specific functionality behind application abstractions where appropriate.

---

# 28. Authorization

Authorization is policy-based.

Never hardcode roles throughout the application.

Use centralized authorization policies.

---

# 29. API Design

All APIs MUST:

* Be versioned
* Return Problem Details for errors
* Be RESTful
* Use DTOs
* Support cancellation tokens

Controllers remain thin.

---

# 30. Streaming

Streaming responses should use Server-Sent Events (SSE) for AI chat.

SignalR is reserved for bidirectional real-time features such as notifications or collaborative functionality.

---

# 31. State Management

Frontend state separation:

Zustand

* Authentication
* Theme
* Preferences
* UI State

TanStack Query

* Server Data
* Conversations
* Models
* Documents
* Knowledge Bases

Do not duplicate server state.

---

# 32. File Storage

Applications interact only through an abstraction.

Never expose filesystem paths.

Support future migration to cloud storage without changing business logic.

---

# 33. Logging

Logging is an infrastructure concern.

Business entities must never perform logging.

Use structured logs with correlation IDs.

---

# 34. Error Handling

Use centralized exception handling.

Business exceptions should be meaningful and translated into appropriate API responses.

Never expose internal stack traces to clients.

---

# 35. Database Rules

Entity Framework Core is the persistence implementation.

The Domain must remain ORM-agnostic.

Every migration must be reversible where practical.

---

# 36. Performance

Architecture should optimize for:

* Minimal allocations
* Efficient database queries
* Streaming
* Async I/O
* Lazy loading of UI modules

Measure before optimizing.

---

# 37. Security

Security is cross-cutting.

Every layer must assume external input is untrusted.

Validate input at application boundaries.

Apply least privilege throughout the system.

---

# 38. Observability

The platform must support:

* Structured logging
* Correlation IDs
* Health checks
* Metrics
* Distributed tracing (future-ready)

Observability should be built in rather than added later.

---

# 39. Extensibility

The architecture must support future capabilities without major restructuring, including:

* Additional AI providers
* Local LLMs
* AI Agents
* MCP servers
* Team workspaces
* Billing providers
* Cloud storage providers
* Alternative vector databases

Extension should occur by adding implementations, not modifying core business logic.

---

# 40. Forbidden Practices

The following are prohibited:

* Business logic inside controllers
* Business logic inside React components
* Direct `DbContext` usage outside Persistence
* Static service classes for business workflows
* Circular dependencies
* Hardcoded secrets
* Hardcoded AI provider names
* Duplicate business logic
* Cross-layer shortcuts
* Shared mutable global state

---

# 41. Architecture Decision Records (ADR)

Significant architectural decisions MUST be documented as ADRs.

Examples:

* Selecting SQL Server Vector Search
* Choosing Server-Sent Events for streaming
* Introducing a new AI provider abstraction
* Adopting a new storage mechanism

Each ADR should record:

* Context
* Decision
* Alternatives considered
* Consequences

---

# 42. Architecture Reviews

Every feature specification and pull request must answer:

* Does it violate Clean Architecture?
* Does it introduce unnecessary coupling?
* Does it duplicate functionality?
* Is it extensible?
* Is it testable?
* Is it secure?
* Does it increase maintainability?

Any "No" response requires justification before approval.

---

# 43. AI Coding Agent Rules

AI coding assistants MUST:

* Read the relevant specification before implementation.
* Follow this document and the project constitution.
* Preserve layer boundaries.
* Prefer extension over modification.
* Explain architectural trade-offs before major changes.
* Never bypass abstractions for convenience.
* Never invent new business requirements.
* Ask for clarification when architectural intent is unclear.

---

# 44. Definition of Architectural Compliance

A change is architecturally compliant only when:

* Layer boundaries are respected.
* Dependencies follow Clean Architecture.
* Business logic resides in the Domain/Application layers.
* Infrastructure remains replaceable.
* Framework dependencies are isolated.
* Automated tests remain valid.
* Documentation is updated when architecture changes.
* No prohibited practices are introduced.

Architecture is considered a product feature. Every implementation decision should strengthen the long-term health of the codebase rather than simply satisfy immediate requirements.
