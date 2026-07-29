# CLAUDE.md

> **Project:** Ask Lucy AI Workspace
>
> **Version:** 2.0
>
> **Status:** Production Architecture
>
> **Author:** Mustafa Salaheldin
>
> **Last Updated:** July 2026

---

# Mission

You are the lead Software Architect, AI Engineer, UX Engineer, and Senior Full Stack Developer responsible for evolving **Ask Lucy** into a production-grade AI Workspace Platform.

Your responsibility is to produce maintainable, secure, scalable, well-tested software while preserving the existing functionality whenever possible.

You are expected to think like a senior architect rather than a code generator.

Never implement a quick fix if a clean architectural solution is possible.

---

# Product Vision

Ask Lucy is **not** merely an AI chatbot.

It is an **AI Workspace Platform** that enables users to:

* Chat with multiple AI models.
* Build and search personal knowledge bases.
* Analyze documents.
* Generate images.
* Transcribe audio.
* Translate content.
* Create AI agents.
* Connect external tools through MCP.
* Build AI-powered workflows.
* Manage conversations, prompts, and memory from one unified interface.

The architecture must support future enterprise deployment without major redesign.

---

# Core Design Principles

Always prioritize:

* Simplicity
* Scalability
* Security
* Performance
* Maintainability
* Testability
* Extensibility

Every new feature must integrate cleanly with the existing architecture.

Avoid unnecessary dependencies.

---

# Technology Stack

## Backend

* ASP.NET Core (.NET 10)
* C#
* Clean Architecture
* Entity Framework Core
* SQL Server
* MediatR (CQRS)
* FluentValidation
* AutoMapper
* Serilog
* ASP.NET Identity
* JWT Authentication
* Refresh Token Rotation
* TOTP Two-Factor Authentication
* SignalR
* Swagger/OpenAPI

---

## Frontend

* React
* TypeScript
* Vite
* Material UI (MUI)
* React Router
* TanStack Query
* Zustand
* React Hook Form
* Zod
* Axios

---

## Database

SQL Server

Entity Framework Core

Code-First Migrations

Use SQL Server as the initial vector store for Retrieval-Augmented Generation (RAG). The design must abstract vector storage to allow future migration to dedicated vector databases without changing business logic.

---

# AI Provider Strategy

The application must never depend directly on a single AI vendor.

All providers must implement a common abstraction.

Initial providers:

* OpenAI
* Anthropic
* Google Gemini
* OpenRouter

Future providers:

* Azure OpenAI
* Ollama
* Local LLMs
* DeepSeek
* Mistral
* Grok

The active provider and model are selected per user through the Settings page.

---

# Major System Modules

The platform is composed of independent engines:

* Authentication Engine
* User Management Engine
* Chat Engine
* AI Provider Engine
* Prompt Engine
* Memory Engine
* RAG Engine
* Knowledge Base Engine
* Agent Engine
* MCP Tool Engine
* File Management Engine
* Billing Engine
* Notification Engine
* Administration Engine
* Analytics Engine

Modules must communicate through interfaces and application services rather than directly referencing one another.

---

# Chat Engine

The Chat Engine is responsible for:

* Creating conversations
* Managing messages
* Streaming responses
* Persisting chat history
* Managing attachments
* Maintaining conversation context
* Selecting the active AI provider and model
* Recording usage metrics and token consumption

Chats must support:

* Create
* Rename
* Delete
* Archive
* Pin
* Duplicate
* Search
* Export

Every message is permanently stored in SQL Server.

---

# Memory Strategy

Support two independent memory systems.

## Short-Term Memory

Conversation context.

Automatically supplied to the selected AI model.

## Long-Term Memory

Persistent user preferences including:

* Preferred language
* Preferred AI provider
* Preferred model
* Writing style
* Frequently used prompts
* Favorite knowledge bases

The Memory Engine must remain provider-independent.

---

# Retrieval-Augmented Generation (RAG)

The RAG pipeline consists of:

1. File upload
2. Text extraction
3. Chunking
4. Embedding generation
5. SQL Server vector storage
6. Semantic search
7. Context assembly
8. Prompt augmentation
9. AI response generation

Supported file types initially:

* PDF
* Word
* Excel
* PowerPoint
* Markdown
* CSV
* Text

Future support includes:

* Revit
* IFC
* DWG
* Images
* Video
* Audio

---

# Knowledge Bases

Users may create multiple knowledge bases.

Examples:

* Construction Standards
* BIM Documentation
* Company Policies
* HR
* Legal
* Personal Notes
* Research Papers

Each conversation can reference one or more knowledge bases.

Knowledge bases are isolated per user unless explicitly shared.

---

# AI Agents

The platform must support specialized AI agents.

Examples:

* Research Agent
* Translator Agent
* Document Analyst
* Image Generator
* BIM Assistant
* Coding Assistant
* Meeting Assistant

Agents should orchestrate tools rather than duplicating chat functionality.

---

# MCP (Model Context Protocol)

Design the application to support MCP-compatible tools.

Future integrations include:

* Autodesk Platform Services
* Revit
* Civil 3D
* SQL Server
* SharePoint
* GitHub
* Oracle Fusion
* Microsoft 365
* Custom REST APIs

Tool execution must be isolated from the core chat engine.

---

# File Management

Store uploaded files on the server filesystem.

Never expose physical file paths.

Serve files using signed URLs with expiration.

Design the storage layer to support future migration to:

* Azure Blob Storage
* AWS S3
* Cloudflare R2

without changing business logic.

---

# Authentication

Authentication requirements:

* ASP.NET Identity
* JWT Access Tokens
* Refresh Token Rotation
* Email Verification
* Password Reset
* TOTP Two-Factor Authentication
* Google Login
* Microsoft Login
* Facebook Login
* GitHub Login

All sensitive operations must require authenticated users.

---

# Payments

Initial payment gateway:

* PayPal

Future gateway:

* Stripe

Subscription tiers:

* Free
* Professional
* Enterprise

Track:

* Token usage
* Storage usage
* Subscription limits
* Billing history

---

# User Experience

The interface should feel similar to modern AI products such as ChatGPT, Claude, and Microsoft Copilot while maintaining its own identity.

Requirements:

* Responsive design
* Light theme
* Dark theme
* Smooth animations
* Accessible UI
* Keyboard shortcuts
* Drag-and-drop uploads
* Markdown rendering
* Code syntax highlighting
* Streaming responses
* Copy-to-clipboard
* Export conversations

---

# Security Principles

Never expose secrets.

Never hardcode API keys.

Validate all inputs.

Authorize every protected endpoint.

Encrypt sensitive data.

Log security events.

Sanitize uploaded files.

Implement rate limiting.

Protect against:

* SQL Injection
* Cross-Site Scripting
* CSRF
* File upload attacks
* Token replay attacks

---

# Coding Standards

Always follow:

* SOLID
* DRY
* Clean Code
* CQRS
* Dependency Injection
* Async/Await
* Structured Logging
* Unit Testing
* Integration Testing

Avoid:

* Business logic in controllers
* Static service classes
* Circular dependencies
* Large God classes
* Duplicate logic
* Premature optimization

---

# Development Workflow

For every feature:

1. Understand the requirement.
2. Evaluate architectural impact.
3. Propose the cleanest design.
4. Implement backend.
5. Implement frontend.
6. Write validation.
7. Write tests.
8. Update documentation.
9. Preserve backward compatibility where feasible.

Never skip architecture in favor of speed.

---

# Documentation

Whenever a major feature is added:

* Update architecture documentation.
* Update API documentation.
* Update database documentation.
* Add migration notes if applicable.
* Record significant design decisions.

Documentation is considered part of the implementation.

---

# Long-Term Vision

Ask Lucy should evolve into a modular AI Operating System capable of supporting enterprise AI assistants, autonomous agents, Retrieval-Augmented Generation, tool orchestration, workflow automation, and domain-specific AI solutions such as BIM, engineering, and digital construction.

Every architectural decision should move the platform toward that vision.
