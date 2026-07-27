# ROADMAP.md

> **Project:** Ask Lucy AI Workspace
> **Version:** 1.0
> **Status:** Product Roadmap
> **Planning Horizon:** 24–36 Months
> **Last Updated:** July 2026

---

# Vision

Ask Lucy aims to become an **Enterprise AI Workspace**—a platform where individuals, teams, and organizations can interact with multiple AI models, build AI-powered workflows, manage organizational knowledge, automate business processes, and extend AI capabilities through agents and integrations.

The long-term objective is to evolve from a conversational AI assistant into an extensible AI operating platform.

---

# Product Strategy

The roadmap follows four guiding principles:

* **Modernize before expanding**
* **Build a scalable foundation**
* **Deliver value incrementally**
* **Maintain enterprise-grade quality**

Every phase should produce a stable, deployable application.

---

# Development Phases

```text id="j5v4jm"
Phase 0
Platform Modernization

↓

Phase 1
Core Workspace

↓

Phase 2
Multi-Provider AI

↓

Phase 3
Knowledge & RAG

↓

Phase 4
Productivity

↓

Phase 5
AI Agents

↓

Phase 6
Enterprise

↓

Phase 7
Marketplace

↓

Phase 8
Performance & Scale
```

---

# Phase 0 – Platform Modernization (Current Priority)

> **Implementation status (2026-07-27)**: Tracked in
> [`specs/000-legacy-modernization/`](../specs/000-legacy-modernization/). Backend
> (Clean Architecture, CQRS, JWT auth, all four AI endpoints, chat CRUD, admin
> role-gating) and frontend (React 19/Vite/MUI, chat/auth/profile/admin UI) are
> implemented and passing 54 automated backend tests + 2 frontend tests. Remaining
> before this phase can close: the production data migration rehearsal (no database
> access in the environment that built this), a live deployment to actually run the
> Playwright regression matrix and measure the SC-006 performance target, and the two
> deferred items recorded in [`docs/adr/0001`](adr/0001-defer-credential-secret-remediation.md)
> and [`docs/adr/0002`](adr/0002-defer-docker-azure-cutover.md).

## Objective

Modernize the existing Ask Lucy application without changing user-facing functionality.

This phase establishes the technical foundation for all future development.

## Deliverables

### Solution

* Migrate to .NET 10
* React 19
* TypeScript
* Vite
* Material UI
* SQL Server
* Entity Framework Core

### Architecture

* Clean Architecture
* CQRS
* MediatR
* FluentValidation
* AutoMapper
* Serilog

### Authentication

* ASP.NET Identity
* JWT
* Refresh Token Rotation
* TOTP 2FA
* Email Verification

### Infrastructure

* GitHub Actions
* Docker
* Environment Configuration
* SMTP
* File Storage

### UI

* Design System
* Responsive Layout
* Dark Mode
* Light Mode
* Accessibility Improvements

### Quality

* Unit Tests
* Integration Tests
* Playwright
* CI/CD
* Documentation

## Exit Criteria

* Existing functionality preserved
* Architecture approved
* CI passing
* No critical regressions
* Production deployment successful

---

# Phase 1 – Core AI Workspace

## Objective

Transform the existing chatbot into a complete AI workspace.

## Features

### Conversations

* Multiple Conversations
* New Chat
* Rename Chat
* Delete Chat
* Archive Chat
* Pin Chat
* Favorites
* Conversation Search

### Messages

* Streaming Responses
* Markdown
* Syntax Highlighting
* Attachments
* Citations
* Token Usage
* Copy
* Export

### User Experience

* Improved Sidebar
* Keyboard Shortcuts
* Responsive Workspace
* Drag & Drop Upload
* Better Mobile Support

## Exit Criteria

Users can manage conversations similarly to ChatGPT and Claude.

---

# Phase 2 – Multi-Provider AI

## Objective

Support multiple AI providers through a unified abstraction layer.

## Providers

* OpenAI
* Anthropic
* Google Gemini
* OpenRouter

Future

* Azure OpenAI
* Ollama
* Local Models

## Features

* Provider Selection
* Model Selection
* Temperature
* Top P
* Streaming Options
* System Prompt
* User Defaults
* Provider Health Monitoring

## Exit Criteria

Users can switch providers and models without affecting the application architecture.

---

# Phase 3 – Knowledge Management & RAG

## Objective

Allow users to build private AI knowledge bases.

## Features

### Knowledge Bases

* Create
* Edit
* Delete
* Share (future)

### Documents

* Upload
* OCR
* Parsing
* Metadata
* Versioning

### Processing

* Chunking
* Embeddings
* Vector Search
* Citations

### Retrieval

* Semantic Search
* Hybrid Search (future)
* Context Injection

## Exit Criteria

Users can chat with their own documents using Retrieval-Augmented Generation.

---

# Phase 4 – Productivity Workspace

## Objective

Expand Ask Lucy into a daily productivity platform.

## Features

### Prompt Library

* Categories
* Favorites
* Templates
* Variables

### Memory

* User Memory
* Project Memory
* Session Memory
* Memory Management

### Files

* File Browser
* Organization
* Search
* Preview

### Notes

* AI Notes
* Summaries
* Rich Text
* Markdown

### Search

* Global Search
* Conversations
* Documents
* Prompts
* Memories

---

# Phase 5 – AI Agents

## Objective

Enable autonomous AI workflows.

## Features

### Agents

* Agent Builder
* Agent Templates
* Multi-Step Reasoning
* Tool Calling

### Tools

* File System
* Email
* Search
* Document Processing

### MCP

* Model Context Protocol
* External Tools
* Enterprise Connectors

### Automation

* Scheduled Tasks
* Background Jobs
* Workflow Execution

## Exit Criteria

Users can build reusable AI agents without changing application code.

---

# Phase 6 – Enterprise Collaboration

## Objective

Support organizations and teams.

## Features

### Organizations

* Workspaces
* Teams
* Departments

### Collaboration

* Shared Conversations
* Shared Knowledge Bases
* Shared Prompts
* Shared Agents

### Administration

* User Management
* Roles
* Permissions
* Audit Logs

### Governance

* Usage Policies
* Model Restrictions
* Data Retention
* Compliance Controls

---

# Phase 7 – Marketplace & Ecosystem

## Objective

Open the platform to third-party extensions.

## Features

### Marketplace

* Extensions
* AI Agents
* Prompt Packs
* Templates

### Developer Platform

* SDK
* REST API
* Webhooks
* Documentation

### Integrations

Future examples include:

* Autodesk Platform Services
* Microsoft 365
* GitHub
* SharePoint
* Jira
* Slack
* Microsoft Teams

---

# Phase 8 – Performance, Scale & Operations

## Objective

Prepare the platform for enterprise-scale deployment.

## Features

### Scalability

* Horizontal Scaling
* Distributed Caching
* Background Processing
* Queue Management

### Monitoring

* Metrics
* Distributed Tracing
* Dashboards
* Alerts

### Reliability

* Backup Strategy
* Disaster Recovery
* High Availability
* Health Monitoring

### Optimization

* Database Tuning
* API Optimization
* Bundle Optimization
* AI Cost Optimization

---

# Cross-Cutting Themes

The following areas evolve throughout every phase.

## Security

* OWASP Compliance
* Security Reviews
* Penetration Testing
* Secret Management
* Rate Limiting

---

## Quality

* Automated Testing
* Continuous Integration
* Code Reviews
* Static Analysis
* Documentation

---

## User Experience

* Accessibility
* Responsive Design
* Performance
* Localization
* Design System

---

## Observability

* Structured Logging
* Correlation IDs
* Metrics
* Health Checks
* Error Reporting

---

# Technical Debt Management

Every phase should allocate time to:

* Dependency Updates
* Refactoring
* Performance Improvements
* Security Improvements
* Documentation
* Test Improvements

Technical debt should be tracked explicitly and addressed continuously rather than deferred indefinitely.

---

# Future Research

Potential long-term initiatives include:

* Local AI Models
* AI-Assisted Coding
* AI Workflow Designer
* Voice Assistants
* Computer Vision
* Video Understanding
* Realtime Collaboration
* Autonomous Project Assistants
* Digital Twin Integration
* BIM Copilot
* CAD Automation
* AI-Powered Engineering Analysis

These initiatives are exploratory and require separate specifications before implementation.

---

# Milestones

| Milestone | Goal                                 |
| --------- | ------------------------------------ |
| M0        | Platform Modernization Complete      |
| M1        | Core AI Workspace Released           |
| M2        | Multi-Provider AI Available          |
| M3        | Knowledge Bases & RAG Released       |
| M4        | Productivity Workspace Complete      |
| M5        | AI Agents Platform Available         |
| M6        | Enterprise Collaboration Released    |
| M7        | Marketplace & SDK Available          |
| M8        | Enterprise-Scale Operations Complete |

---

# Definition of Phase Completion

A phase is complete only when:

* All approved specifications are implemented.
* Acceptance criteria are satisfied.
* Architecture remains compliant.
* Automated tests pass.
* Security review is complete.
* Performance targets are met.
* Documentation is updated.
* CI/CD pipeline succeeds.
* Product Owner approves the release.

---

# Guiding Principles

The Ask Lucy roadmap follows several long-term principles:

* Build on a stable architectural foundation before introducing new capabilities.
* Favor extensibility over short-term optimization.
* Deliver production-ready increments at every phase.
* Keep the platform provider-agnostic and vendor-neutral.
* Preserve backward compatibility whenever practical.
* Use specifications and Architecture Decision Records (ADRs) to guide evolution.
* Continuously improve quality, security, performance, and developer experience.

Every future feature should align with this roadmap or be introduced through an approved specification and, where appropriate, an updated roadmap revision.
