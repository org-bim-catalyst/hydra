# DOMAIN_SERVICES.md

> **Project:** Ask Lucy AI Workspace
>
> **Version:** 2.0
>
> **Architecture:** Domain-Driven Design + Clean Architecture
>
> **Last Updated:** July 2026

---

# 1. Purpose

This document defines the **core domain and application services** that power Ask Lucy.

These services represent the business capabilities of the platform and form the orchestration layer between the Domain Model and Infrastructure.

**Business rules belong in the Domain.**

**Workflow orchestration belongs in these services.**

**External APIs belong in Infrastructure.**

---

# 2. Design Principles

Every service must:

* Have a single responsibility.
* Be provider-independent.
* Be fully testable.
* Be asynchronous.
* Use dependency injection.
* Depend on abstractions only.
* Avoid direct coupling to other services whenever possible.

All services should be registered through Dependency Injection.

---

# 3. Core Service Map

```text
                        User Request
                             │
                             ▼
                     ConversationService
                             │
      ┌──────────────┬───────────────┬───────────────┐
      ▼              ▼               ▼
 MemoryService   RagService     AgentService
      │              │               │
      └──────────────┼───────────────┘
                     ▼
              PromptBuilderService
                     ▼
             AIProviderService
                     ▼
             Selected AI Provider
                     │
             ToolExecutionService
                     │
             External Systems (MCP)
```

---

# 4. Conversation Service

## Responsibility

Owns the lifecycle of conversations.

Responsible for:

* Create chat
* Rename chat
* Archive chat
* Pin chat
* Delete chat
* Restore chat
* Export chat
* Search chats
* Manage conversation metadata

Interface

```csharp
IConversationService
```

Example methods

```text
CreateConversation()

RenameConversation()

ArchiveConversation()

DeleteConversation()

GetConversation()

SearchConversations()
```

Never communicates directly with OpenAI or any AI provider.

---

# 5. Message Service

Responsible for message persistence.

Functions

* Add user message
* Add assistant message
* Add system message
* Edit message (admin only)
* Regenerate response
* Delete message
* Attach files

Interface

```csharp
IMessageService
```

Messages remain immutable after AI generation unless administrative intervention is required.

---

# 6. Chat Engine Service

This is the heart of Ask Lucy.

Responsibilities

* Receive user prompt
* Load conversation
* Load memory
* Load RAG context
* Load agent instructions
* Build prompt
* Call AI Provider
* Stream response
* Save messages
* Record usage

Pipeline

```text
User Prompt

↓

Conversation

↓

Memory

↓

Knowledge Bases

↓

Prompt Builder

↓

AI Provider

↓

Streaming Response

↓

Persist Messages

↓

Update Usage
```

Interface

```csharp
IChatEngine
```

The Chat Engine never knows whether GPT, Claude, Gemini, or another provider is being used.

---

# 7. Prompt Builder Service

Centralizes prompt construction.

Responsibilities

Combine:

* System Prompt
* User Prompt
* Chat History
* Long-Term Memory
* Retrieved Documents
* Agent Instructions
* Tool Results

Output

```text
PromptContext
```

Interface

```csharp
IPromptBuilder
```

No prompt construction should exist elsewhere in the application.

---

# 8. AI Provider Service

Acts as the gateway to every LLM.

Interface

```csharp
IAIProvider
```

Implementations

```text
OpenAIProvider

AnthropicProvider

GeminiProvider

OpenRouterProvider

AzureOpenAIProvider

OllamaProvider
```

Functions

```text
Chat()

Stream()

GenerateImage()

Embeddings()

SpeechToText()

TextToSpeech()
```

Providers advertise capabilities.

Example

```text
SupportsStreaming

SupportsVision

SupportsImages

SupportsReasoning

SupportsTools
```

The application chooses the provider at runtime based on user settings.

---

# 9. AI Provider Factory

Responsible for resolving the correct provider.

Interface

```csharp
IAIProviderFactory
```

Example

```text
GetProvider(UserSettings)
```

No switch statements should appear elsewhere.

---

# 10. AI Model Catalog Service

Maintains available models.

Responsibilities

* Discover models
* Enable/Disable models
* Cache model metadata
* Validate capabilities

Example

```text
GPT-5

Claude Sonnet

Gemini Pro

DeepSeek

Llama
```

Interface

```csharp
IModelCatalogService
```

---

# 11. Memory Service

Provides persistent AI memory.

Responsibilities

Store

* Preferences
* Facts
* Writing Style
* Projects

Retrieve

Relevant memories for current conversation.

Interface

```csharp
IMemoryService
```

Supports

Short-term memory

Long-term memory

Semantic memory (future)

---

# 12. Conversation Summarization Service

Long conversations become expensive.

Responsibilities

Summarize old messages into compact context.

Interface

```csharp
IConversationSummaryService
```

Automatically invoked when token thresholds are reached.

---

# 13. Knowledge Base Service

Responsible for knowledge management.

Functions

* Create Knowledge Base
* Rename
* Delete
* Share
* Attach to Conversation

Interface

```csharp
IKnowledgeBaseService
```

---

# 14. Document Processing Service

Coordinates ingestion.

Pipeline

```text
Upload

↓

Parser

↓

OCR (future)

↓

Chunking

↓

Embeddings

↓

Vector Store
```

Interface

```csharp
IDocumentProcessingService
```

---

# 15. Document Parser Service

Supported formats

* PDF
* DOCX
* PPTX
* XLSX
* Markdown
* CSV
* TXT

Future

* IFC
* RVT
* DWG

Interface

```csharp
IDocumentParser
```

---

# 16. Chunking Service

Converts extracted text into AI chunks.

Responsibilities

* Chunk sizing
* Overlap
* Metadata

Interface

```csharp
IChunkingService
```

Should support multiple chunking strategies.

---

# 17. Embedding Service

Responsible for embedding generation.

Interface

```csharp
IEmbeddingService
```

Initial provider

OpenAI Embeddings

Future

Gemini

Voyage

Local models

---

# 18. Vector Store Service

Stores and retrieves embeddings.

Interface

```csharp
IVectorStore
```

Initial implementation

SQL Server Vector Search

Future implementations

* Azure AI Search
* Qdrant
* Pinecone
* Weaviate

The application should never depend on a specific vector database.

---

# 19. Retrieval Service

Semantic search engine.

Responsibilities

Retrieve

Top K

Similarity

Filtering

Ranking

Interface

```csharp
IRetrievalService
```

---

# 20. RAG Service

Coordinates complete RAG workflow.

Pipeline

```text
Question

↓

Embedding

↓

Similarity Search

↓

Ranking

↓

Prompt Builder
```

Interface

```csharp
IRagService
```

---

# 21. Agent Service

Manages AI agents.

Functions

* Create Agent
* Update Agent
* Execute Agent
* Disable Agent

Interface

```csharp
IAgentService
```

Agents may invoke:

Memory

Tools

Knowledge Bases

Multiple models

---

# 22. Tool Execution Service

Responsible for all tool calls.

Examples

* SQL
* GitHub
* Revit
* SharePoint
* REST APIs

Interface

```csharp
IToolExecutionService
```

Never let LLM providers directly invoke external systems.

---

# 23. MCP Service

Coordinates Model Context Protocol.

Responsibilities

* Discover tools
* Authenticate
* Execute
* Return structured results

Interface

```csharp
IMcpService
```

---

# 24. File Storage Service

Abstraction

```csharp
IFileStorage
```

Implementations

```text
Local Storage

Azure Blob

Amazon S3

Cloudflare R2
```

Functions

Upload

Download

Delete

Generate Signed URL

---

# 25. Authentication Service

Responsibilities

* Login
* Logout
* Refresh Token
* Email Verification
* Password Reset
* Two-Factor Authentication

Interface

```csharp
IAuthenticationService
```

---

# 26. User Settings Service

Stores user preferences.

Examples

Provider

Model

Theme

Language

Voice

Temperature

Streaming

System Prompt

Interface

```csharp
IUserSettingsService
```

---

# 27. Billing Service

Responsibilities

* Subscription validation
* Usage limits
* Payment processing
* Credits
* Token tracking

Interface

```csharp
IBillingService
```

---

# 28. Notification Service

Supports

Email

In-App Notifications

Future

SMS

Push Notifications

Interface

```csharp
INotificationService
```

---

# 29. Usage Analytics Service

Tracks

Requests

Tokens

Storage

Model usage

Latency

Failures

Interface

```csharp
IUsageAnalyticsService
```

---

# 30. Audit Service

Records

Security events

AI requests

Payments

Administrative actions

Interface

```csharp
IAuditService
```

---

# 31. Background Job Service

Long-running tasks.

Examples

* Embeddings
* Email sending
* Cleanup
* Re-indexing
* Usage aggregation

Interface

```csharp
IBackgroundTaskService
```

---

# 32. Service Dependency Rules

The following dependency direction is mandatory:

```text
Controllers
      │
      ▼
Application Services
      │
      ▼
Domain
      │
      ▼
Infrastructure Interfaces
      │
      ▼
Infrastructure Implementations
```

Application services must never reference concrete implementations.

---

# 33. Cross-Service Communication

Services communicate through interfaces, MediatR requests, and domain events.

Avoid direct service chaining where an event-driven approach is more appropriate.

Examples:

```text
ConversationCreated
        ↓
AuditService

KnowledgeBaseIndexed
        ↓
NotificationService

PaymentCompleted
        ↓
BillingService
```

---

# 34. Service Lifetime Guidelines

| Service Type                   | Lifetime                                    |
| ------------------------------ | ------------------------------------------- |
| Stateless application services | Scoped                                      |
| EF Core DbContext              | Scoped                                      |
| AI provider implementations    | Scoped                                      |
| Provider factory               | Singleton                                   |
| Model catalog cache            | Singleton                                   |
| Configuration providers        | Singleton                                   |
| Background workers             | Hosted Service                              |
| HTTP clients                   | Typed `HttpClient` via `IHttpClientFactory` |

Never inject `DbContext` into singleton services.

---

# 35. Resilience

External integrations must implement:

* Retry policies
* Exponential backoff
* Timeouts
* Circuit breakers
* Cancellation token support

Use Polly (or the .NET resilience pipeline) for outbound HTTP calls.

---

# 36. Future Services

The architecture should allow additional services without breaking existing interfaces.

Planned services include:

* Workflow Engine
* Prompt Marketplace
* Plugin Marketplace
* AI Automation Engine
* Voice Agent Service
* Meeting Intelligence Service
* Calendar Service
* BIM Automation Service
* Autodesk Platform Services Service
* Oracle Fusion Service
* Microsoft 365 Service
* GitHub Automation Service

Each new capability should be introduced as a new service rather than expanding existing services beyond their responsibilities.

---

# 37. Service Design Checklist

Before introducing a new service, verify:

* Does it have a single responsibility?
* Can it be unit tested in isolation?
* Does it depend only on abstractions?
* Can it be replaced without affecting consumers?
* Does it belong in the Application layer rather than Infrastructure?
* Does it expose a clear, cohesive interface?
* Can it scale independently in the future?
* Does it avoid duplicating responsibilities already owned by another service?

If any answer is "No", revisit the design before implementation.
