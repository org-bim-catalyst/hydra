# Implementation Plan: Chat History & Conversation Management

**Branch**: `002-chat-history-management` | **Date**: 2026-07-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-chat-history-management/spec.md`

## Summary

Extend Ask Lucy's existing persisted-chat foundation (SPEC-000's `UserChat`/`Message`
aggregate) into a full conversation-management layer: archive/pin/favorite/duplicate/
clear/permanent-delete lifecycle actions, a "Recently Deleted" trash view, richer message
metadata (provider/model/tokens/generation params/attachments/citations), searchable/
filterable/sortable/cursor-paginated discovery at scale, locally-derived auto-titling,
and structured export — implemented by extending the existing entities/endpoints/
frontend feature folder rather than introducing a parallel model (research.md Topic 1).

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript 5 (frontend)

**Primary Dependencies**: ASP.NET Core, EF Core, MediatR, FluentValidation, AutoMapper,
Serilog (backend); React 19, Vite, Material UI, TanStack Query, Zustand, React Hook Form,
Zod, Axios (frontend) — all already established by SPEC-000. New for this feature:
`@tanstack/react-virtual` (list virtualization, research.md Topic 8); SQL Server
Full-Text Search (no new package — a SQL Server feature enabled via migration, research.md
Topic 5).

**Storage**: SQL Server via EF Core Code-First migrations (existing `AskLucyDbContext`).

**Testing**: xUnit (`AskLucy.Domain.Tests`, `AskLucy.Application.Tests`,
`AskLucy.Persistence.Tests`, `AskLucy.Web.Tests`), Playwright (`AskLucy.E2E.Tests`) —
existing test projects, extended with this feature's cases. `AskLucy.Infrastructure.Tests`
is unaffected — this feature does not change the Infrastructure/AI-provider layer.

**Target Platform**: ASP.NET Core web API + server-rendered SPA (`AskLucy.Web` hosts
both the API and the built `ClientApp`), existing deployment targets unchanged.

**Project Type**: Web application (backend + frontend within one solution/repo).

**Performance Goals**: Conversation search/filter results in <3s at 10,000+ owned
conversations (SC-001); message-content search reflects a just-sent message within a few
seconds (SC-001a, near-real-time via FTS); responsive scrolling/incremental loading at
hundreds of thousands of stored messages (SC-003).

**Constraints**: No new datastore beyond SQL Server (constitution §5 — full-text search
via native SQL Server FTS, not a separate search engine, research.md Topic 5); cursor-
based pagination required for chat messages (constitution §6, explicit); no AI-provider
call for auto-titling (spec.md clarification); export excludes embedded attachment file
bytes (spec.md clarification).

**Scale/Scope**: 6 user stories (P1×2, P2×2, P3×2); extends 2 existing entities
(`UserChat`, `Message`) and adds 2 new entities (`Attachment`, `Citation`); extends 1
existing controller/resource (`/api/v1/chats`) with ~12 new actions/parameters; no new
top-level frontend feature folder (extends existing `features/chat`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design below.*

| Principle / Rule | Status | Notes |
|---|---|---|
| I. Clean Architecture & Dependency Rule | PASS | New entities/handlers follow the existing `Domain → Application → Infrastructure/Persistence → Web` layering already established by `UserChat`/`Message`; no new cross-layer reference introduced. |
| II. SOLID | PASS | Each new lifecycle action is a separate MediatR command with a single handler (SRP); no existing closed logic is edited beyond adding new fields/methods (OCP-friendly extension of `UserChat`/`Message`). |
| III. Simplicity — DRY/KISS/YAGNI | PASS | Reuses existing soft-delete convention for Trash and existing GDPR hard-delete pattern for Permanent Delete (research.md Topic 2) instead of inventing new mechanisms; no speculative abstraction added beyond what FR-001..FR-028 require. |
| IV. Composition over inheritance | PASS | No new inheritance introduced; `Attachment`/`Citation` are plain `BaseEntity` children, not subtypes of `Message`. |
| V. Dependency Inversion & Testability | PASS | New repository methods added to existing `IUserChatRepository`/`IMessageRepository` interfaces (Application-owned); handlers remain unit-testable with fakes. |
| VI. Separation of Concerns | PASS | Title-derivation, search-query construction, and export-serialization logic live in Application, not in the controller. |
| VII. Convention over Configuration | PASS | Explicit basis for research.md Topic 1 (extend `UserChat`, don't rename) and Topic 2 (reuse soft-delete/GDPR-hard-delete conventions). |
| VIII. No Silent Failures | PASS | Concurrency conflicts → 409 (research.md Topic 10); frontend optimistic mutations roll back and toast on failure (research.md Topic 9); confirmation-gated destructive actions return explicit 400 when unconfirmed (contracts/chats-api.md). |
| §3 Architecture Rules (CQRS, Repository/UoW, Infrastructure isolation) | PASS | Every action is a MediatR command/query; duplicate/clear operations commit through one `IUnitOfWork.SaveChangesAsync()` (research.md Topic 3); SQL Server FTS accessed only from `Infrastructure`/`Persistence`, never referenced from Domain/Application. |
| §5 Database Principles (soft delete, concurrency, indexing, no new datastore) | PASS | No new datastore (SQL Server FTS only); `RowVersion` concurrency reused; new indexes for `ArchivedAtUtc`/`PinnedAtUtc`/`IsFavorite` filters added in the same migration that needs them. |
| §6 API Standards (REST actions, pagination, Problem Details, versioning) | PASS | Non-CRUD actions modeled as `/actions/{verb}` sub-resources (contracts/chats-api.md); cursor pagination for messages/conversations; existing `/api/v1/chats` version, no breaking change to already-shipped fields. |
| §7 UI Principles (virtualization, accessibility, theming) | PASS | `@tanstack/react-virtual` for sidebar/message list (research.md Topic 8); no bespoke component before checking MUI/shared library first. |
| §8 Security | PASS | Ownership enforced via existing `ChatOwnershipGuard` pattern extended to new actions; permanent-delete/purge is an explicit, confirmed, audited command (constitution's GDPR-erasure pattern). |
| §10 Testing Standards | PASS | Plan allocates unit tests (Domain/Application), integration tests (Persistence/repository + FTS query), and Playwright E2E per user story (tasks.md, next phase). |

No violations requiring justification — **Complexity Tracking is empty** (see below).

**Post-Phase-1 re-check**: The table above was re-validated after Phase 1 design
(research.md, data-model.md, contracts/chats-api.md) was written, not only against the
spec in isolation — every row cites the specific research topic or contract section that
grounds it (e.g., FTS instead of a new search engine, sub-resource actions instead of new
resources, cursor pagination, GDPR-hard-delete reuse for Permanent Delete). No gate
regressed once the concrete data model and API shapes were fixed; Complexity Tracking
remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/002-chat-history-management/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── chats-api.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── AskLucy.Domain/
│   └── Chats/
│       ├── UserChat.cs                # extended: ArchivedAtUtc, PinnedAtUtc, IsFavorite, IsTitleManuallySet
│       ├── Message.cs                 # extended: Provider, Model, GenerationParametersJson, token counts
│       ├── Attachment.cs              # new
│       └── Citation.cs                # new
├── AskLucy.Application/
│   └── Chats/
│       ├── Commands/
│       │   ├── ArchiveUserChat/ RestoreUserChat/ PinUserChat/ UnpinUserChat/
│       │   ├── FavoriteUserChat/ UnfavoriteUserChat/ DuplicateUserChat/
│       │   ├── ClearUserChatMessages/ PurgeUserChat/ (permanent delete)
│       │   └── ... (existing CreateUserChat/RenameUserChat/DeleteUserChat/AppendMessage unchanged in shape, extended in data captured)
│       ├── Queries/
│       │   ├── SearchUserChats/ (replaces/extends GetMyUserChats with filter/sort/cursor/q params)
│       │   ├── GetChatMessages/ (extended: cursor pagination)
│       │   └── ExportUserChat/
│       └── Authorization/ChatOwnershipGuard.cs  # reused, unchanged contract
├── AskLucy.Persistence/
│   ├── Configurations/ (UserChatConfiguration, MessageConfiguration extended; AttachmentConfiguration, CitationConfiguration new)
│   ├── Repositories/ (IUserChatRepository/IMessageRepository extended with new aggregate-oriented methods)
│   └── Migrations/ (new migration: new columns/tables/indexes/FTS catalog)
└── AskLucy.Web/
    ├── Controllers/v1/ChatsController.cs   # extended with new actions (contracts/chats-api.md)
    ├── Contracts/ChatContracts.cs          # extended with new request/response records
    └── ClientApp/src/features/chat/
        ├── components/ (ChatSidebar extended: search/filter/sort/context menu/virtualized list; new ExportDialog, ConfirmDialog)
        ├── hooks/ (useChats extended: search/filter/sort/pagination; new useConversationActions, useExportConversation)
        └── api/chatsApi.ts             # extended with new endpoints

tests/
├── AskLucy.Domain.Tests/Chats/         # new entity behavior (archive/pin/favorite/duplicate rules)
├── AskLucy.Application.Tests/Chats/    # new command/query handlers
├── AskLucy.Persistence.Tests/          # repository + FTS query integration tests
├── AskLucy.Web.Tests/Chats/            # controller/contract tests
└── AskLucy.E2E.Tests/                  # new Playwright specs per quickstart.md scenario
```

**Structure Decision**: Web application (Option 2), realized as the existing single
`AskLucy.Web` project (API + built `ClientApp`) plus `Domain`/`Application`/
`Infrastructure`/`Persistence` — this feature adds files under each existing project's
already-established `Chats`/`chat` feature folder rather than creating new top-level
projects or frontend feature folders, per constitution §2.VII (Convention over
Configuration) and research.md Topic 1.

## Complexity Tracking

*No entries — Constitution Check reported no violations requiring justification.*
