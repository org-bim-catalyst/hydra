# Implementation Plan: Knowledge Base Management

**Branch**: `014-knowledge-base-management` | **Date**: 2026-08-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/014-knowledge-base-management/spec.md`

## Summary

Introduce a Knowledge Base Engine that lets each user create, organize, and manage private,
foldered containers of documents in preparation for a future RAG pipeline — without
implementing embedding, chunking, or retrieval. Architecturally this is a new
`KnowledgeBases` aggregate group deliberately modeled on the existing, already-shipped
`Chats`/`UserChat` pattern (specs/002-chat-history-management): same CQRS command/query
shape, same lifecycle mechanics (status enum + `BaseEntity.DeletedAtUtc` for soft delete +
ownership guard + audit log), same controller/rate-limit/pagination conventions. The one
genuinely new piece of infrastructure is a `KnowledgeBaseDocument` entity that associates an
uploaded file (via the existing `IFileStorage` abstraction) with a knowledge base/folder —
nothing like it exists yet; today `IFileStorage` is only used for avatars and chat
attachments. A new periodic `BackgroundService` (mirroring `ProviderHealthCheckHostedService`)
sweeps soft-deleted knowledge bases past their 30-day retention window and cascades a hard
delete to their documents' underlying files, per the spec's Clarifications.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend, existing solution); TypeScript 5.x / React 19
(frontend, existing `ClientApp`). No new language.

**Primary Dependencies**: No new NuGet or npm packages for the core feature. Backend: existing
MediatR, FluentValidation, EF Core, ASP.NET rate limiting, Serilog, and the existing
`IFileStorage`/`ISignedUrlService` abstractions (extended, not replaced — see research.md
Decision 3). Page-count extraction for PDF/DOCX/PPTX uses only the .NET BCL
(`System.IO.Compression` for the OOXML zip formats, a small hand-rolled PDF trailer reader) —
see research.md Decision 5 for why a full parsing library was rejected for now. Frontend:
existing MUI, TanStack Query, Zustand, React Hook Form + Zod; drag-and-drop uses a small
existing-pattern-consistent library choice recorded in research.md Decision 6 (none of the
existing features use drag-and-drop yet, so this is the one genuinely new frontend
dependency).

**Storage**: SQL Server via EF Core (existing `AskLucy.Persistence`) — five new tables
(`KnowledgeBase`, `KnowledgeBaseFolder`, `KnowledgeBaseDocument`, `KnowledgeBaseTag`,
`KnowledgeBaseCategory`) plus one append-only log table (`KnowledgeBaseAuditLog`). Document
bytes themselves are *not* a new storage mechanism — they go through the existing
`IFileStorage`/`LocalFileStorage` server-filesystem implementation exactly as avatars and
chat attachments do today.

**Testing**: xUnit (backend, existing `tests/AskLucy.*.Tests` projects) for Domain/Application
unit tests and Infrastructure integration tests (real SQL Server test instance, per
constitution §10); Vitest + React Testing Library + MSW + jest-axe (frontend, existing
`ClientApp` tooling) for the dashboard, tree view, and drag-and-drop UI, including the
accessibility requirements added during `/speckit-clarify` (FR-039–FR-042).

**Target Platform**: ASP.NET Core 10 hosted on the existing myASP.NET Windows/IIS (ANCM)
deployment; React SPA static build served the same way it is today. No new hosting
capability required.

**Project Type**: Web application (existing two-part layered .NET backend + React SPA) —
extends the existing structure, introduces no new top-level project.

**Performance Goals**: Directly from spec.md Success Criteria — first-KB-visible within 30s
of workspace open (SC-001, dominated by UX flow, not backend latency); search/filter results
in <1s for 95% of queries (SC-002); dashboard list/search/filter/sort operations complete in
<2s at 1,000 knowledge bases per user (SC-003); duplicate-and-usable in <10s for a knowledge
base with up to 1,000 documents (SC-006, see research.md Decision 4 on why duplication is
processed synchronously up to that document count rather than backgrounded); sustained
10,000 knowledge bases/user and 1,000,000 documents platform-wide without measurable dashboard
slowdown (SC-007).

**Constraints**: All list/search endpoints are cursor-paginated (constitution §6), matching
`ChatsController.Search`'s existing shape. Dashboard summary statistics are cached, not
recomputed per request (FR-035) — research.md Decision 7 picks the cache mechanism and TTL.
Document files are validated by content (magic-byte sniffing), not extension/MIME header
alone, and size-limited before being persisted (constitution §8) — `LocalFileStorage` does
not do this today for any caller, so this feature adds the check (research.md Decision 8).
Every list/mutation endpoint is authorization-scoped to the caller's own knowledge bases
(FR-010) using the same `KeyNotFoundException`-on-not-owned pattern as `ChatOwnershipGuard`,
so an unauthorized access attempt is indistinguishable from a 404.

**Scale/Scope**: All authenticated users at launch — no tier gating (nothing in the spec
requires it); scale targets are SC-007's 10,000 KBs/user and 1,000,000 documents
platform-wide.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see "Post-Design
Re-check" below.*

| Principle / Gate | Status | Notes |
|---|---|---|
| §3 Clean Architecture & Dependency Rule | PASS | `KnowledgeBase`/`KnowledgeBaseFolder`/`KnowledgeBaseDocument`/`KnowledgeBaseTag`/`KnowledgeBaseCategory`/`KnowledgeBaseAuditLog` live in `Domain/KnowledgeBases`; repositories are interfaces in `Application/KnowledgeBases`, implemented in `Infrastructure`/`Persistence`. No Domain/Application code references EF Core or the filesystem directly. |
| §2.III Simplicity — DRY/KISS/YAGNI | **Deviation, justified** | The spec's Database section (carried from the original feature request) asks for a `KnowledgeBasePermission` entity described as "future-ready." No functional requirement in the *finalized* spec (post-clarify) exercises sharing/permissions — FR-009 is explicit that every knowledge base is private to its owner in this release. Constitution §2.III is explicit that tables MUST NOT be built for hypothetical future requirements absent from an approved specification, and Governance states the constitution supersedes a conflicting template/request default. **Decision: `KnowledgeBasePermission` is not created in this release.** Authorization is enforced purely via `KnowledgeBase.OwnerId` (mirrors `UserChat.IsOwnedBy`). When team/organization sharing ships in a later spec, a permission table is a small additive migration — not a breaking change — so nothing is foreclosed by deferring it. See data-model.md "Explicitly Not Modeled." |
| §5 Database — entity design, soft delete, auditing | PASS | Every new aggregate root uses `BaseEntity` (surrogate `Guid` v7 key, audit columns via `AuditSaveChangesInterceptor`, `RowVersion` concurrency token). `KnowledgeBase`/`KnowledgeBaseFolder`/`KnowledgeBaseDocument` use soft delete (`DeletedAtUtc`) via a per-entity `HasQueryFilter`, matching `UserChat`/`Message`/`Attachment`. `KnowledgeBaseAuditLog` is deliberately append-only, no soft delete — same documented exception as `ProviderHealthCheck`/`VoiceProviderFailoverEvent`. |
| §5 Concurrency | PASS | `RowVersion` (inherited from `BaseEntity`) covers the "two sessions edit the same knowledge base concurrently" edge case; `DbUpdateConcurrencyException` is caught explicitly in the relevant command handlers and surfaced as a 409 Problem Details conflict (constitution §2.VIII no-silent-failures), never left to bubble as a generic 500. |
| §3 CQRS/MediatR/Repository/FluentValidation/AutoMapper | PASS | Every mutation is an `IRequest`/handler pair with a `FluentValidation` validator running in the existing `ValidationBehavior` pipeline, mirroring `Chats/Commands/*`. Manual DTO mapping is used (no AutoMapper profile is added) — consistent with `Chats`/`Ai`, which map by hand in the handler; AutoMapper is referenced in the project's technology list but not actually used anywhere in the existing codebase for this class of simple entity→DTO shape, so introducing it here would be a new, undocumented pattern, not a followed convention (constitution §7 Convention Over Configuration favors the established hand-mapping convention). |
| §6 REST conventions, pagination, Problem Details | PASS | `/api/v1/knowledge-bases` (list/create), `{id}` (get/rename/delete), `{id}/actions/{verb}` for non-CRUD state transitions (archive/restore/activate/favorite/unfavorite/pin/unpin/duplicate/purge) — exactly `ChatsController`'s shape. Lists are cursor-paginated via the existing `PagedResult<T>`. |
| §6 AuthN/AuthZ | PASS | `[Authorize]` by default on the new controllers; ownership enforced in Application-layer guards (`KnowledgeBaseOwnershipGuard`, mirrors `ChatOwnershipGuard`), not scattered controller `if` checks. |
| §6 Rate limiting | PASS | New `knowledge-base-endpoints` rate-limit policy registered in `Program.cs`, same generous non-AI-cost-tiered shape as `chat-endpoints` (these endpoints never invoke an AI provider). |
| §8 Security — file validation | PASS (feature adds it) | Constitution §8 requires uploads to be validated by content (magic-byte sniffing) and size-limited before persisting — not currently done by any existing `IFileStorage` caller. This feature adds a `IDocumentContentValidator` (Application-owned interface, Infrastructure implementation) so knowledge-base document uploads are the first caller to satisfy this rule; existing callers (avatars, chat attachments) are out of scope for this spec and are not retrofitted. |
| §8 Security — least privilege, ownership | PASS | Same `OwnerId`-only-access model as every other private-per-user aggregate (`UserChat`, `UserAiPreference`). |
| §14 Observability | PASS | `KnowledgeBaseAuditLog` (FR-011) mirrors the existing append-only audit-log pattern; Serilog structured logging for the purge hosted service mirrors `ProviderHealthCheckHostedService`'s cycle-failure logging (never lets one failure take down the host). |
| §7 UI — accessibility, responsive, theming | PASS (feature adds explicit requirements) | FR-039–FR-042 (added during `/speckit-clarify`) restate constitution §7's existing WCAG 2.1 AA baseline at the stricter WCAG 2.2 AA level the original feature request asked for. This is a knowingly stricter bar than the constitution's baseline for this one feature's UI, not a conflict — nothing in §7 caps conformance at 2.1; it only sets2.1 AA as the floor. Automated a11y checks (jest-axe) and keyboard-only manual verification are added to this feature's test plan (constitution §10). |
| §10 Testing | PASS (planned in tasks) | Domain/Application unit-tested with faked repositories/`IFileStorage`; Infrastructure (EF Core repositories, `LocalFileStorage` cascade-delete path, page-count extractors) integration-tested; new frontend hooks/components covered by Vitest+RTL+jest-axe; the purge hosted service's 30-day sweep logic is unit-tested with an injectable clock, not a real 30-day wait. |
| §9 AI Principles | N/A | This spec explicitly excludes embedding/retrieval/prompt augmentation — no AI provider is invoked anywhere in this feature. |

No Complexity Tracking entries beyond the `KnowledgeBasePermission` deviation above, which is
a *simplification* relative to the original request (fewer tables), not added complexity — it
does not require a Complexity Tracking justification in the "why is this more complex" sense,
but is called out here because it deliberately diverges from what spec.md's Database section
literally lists, and the constitution requires that kind of divergence to be explained, not
silent.

## Project Structure

### Documentation (this feature)

```text
specs/014-knowledge-base-management/
├── spec.md                          # Feature specification
├── plan.md                          # This file
├── research.md                      # Phase 0 output
├── data-model.md                    # Phase 1 output
├── quickstart.md                    # Phase 1 output
├── contracts/                       # Phase 1 output
│   ├── knowledge-bases-api.md       # CRUD, lifecycle actions, search, dashboard, export
│   ├── knowledge-base-folders-documents-api.md
│   └── knowledge-base-taxonomy-api.md
├── checklists/
│   └── requirements.md
└── tasks.md                         # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

Existing two-part layout (layered .NET backend + React SPA) — extended, not restructured.
New backend code mirrors `Chats/` at every layer; new frontend code mirrors
`features/chat/`.

```text
src/AskLucy.Domain/KnowledgeBases/
├── KnowledgeBase.cs                       # NEW — aggregate root, mirrors UserChat.cs's shape
├── KnowledgeBaseFolder.cs                 # NEW
├── KnowledgeBaseDocument.cs               # NEW
├── KnowledgeBaseTag.cs                    # NEW
├── KnowledgeBaseCategory.cs               # NEW
└── KnowledgeBaseAuditLog.cs               # NEW — append-only, mirrors ProviderHealthCheck.cs

src/AskLucy.Application/
├── Abstractions/
│   ├── IFileStorage.cs                    # EXTENDED — adds DeleteAsync (research.md Decision 3)
│   ├── IDocumentContentValidator.cs       # NEW — magic-byte + size validation (research.md Decision 8)
│   └── IDocumentPageCountExtractor.cs     # NEW — research.md Decision 5
└── KnowledgeBases/
    ├── Authorization/
    │   └── KnowledgeBaseOwnershipGuard.cs # NEW — mirrors ChatOwnershipGuard.cs
    ├── Commands/
    │   ├── CreateKnowledgeBase/           # NEW
    │   ├── UpdateKnowledgeBaseDetails/    # NEW — name/description/color/icon/category/tags/notes
    │   ├── ActivateKnowledgeBase/         # NEW — Draft -> Active (research.md Decision 1)
    │   ├── ArchiveKnowledgeBase/          # NEW
    │   ├── RestoreKnowledgeBase/          # NEW
    │   ├── DeleteKnowledgeBase/           # NEW — soft delete, schedules purge (+30d)
    │   ├── PurgeKnowledgeBase/            # NEW — owner-triggered immediate hard delete + cascade file delete
    │   ├── DuplicateKnowledgeBase/        # NEW — deep copy incl. independent physical file copies
    │   ├── FavoriteKnowledgeBase/         # NEW
    │   ├── UnfavoriteKnowledgeBase/       # NEW
    │   ├── PinKnowledgeBase/              # NEW
    │   ├── UnpinKnowledgeBase/            # NEW
    │   ├── CreateFolder/                  # NEW
    │   ├── RenameFolder/                  # NEW
    │   ├── MoveFolder/                    # NEW — rejects circular moves (FR-013)
    │   ├── DeleteFolder/                  # NEW — requires confirm if non-empty (FR-015)
    │   ├── UploadDocument/                # NEW — validates content, extracts page count, updates cached stats
    │   ├── MoveDocument/                  # NEW
    │   ├── DeleteDocument/                # NEW
    │   ├── CreateCustomCategory/          # NEW — private to owner (FR-038)
    │   └── DeleteCategory/                # NEW — falls back assignments to Uncategorized (FR-021)
    └── Queries/
        ├── SearchKnowledgeBases/          # NEW — mirrors SearchUserChatsQuery's cursor pagination
        ├── GetKnowledgeBaseDashboardSummary/ # NEW — cached (FR-035, research.md Decision 7)
        ├── GetKnowledgeBaseFolderTree/    # NEW
        ├── ListCategories/                # NEW — predefined + caller's private custom ones
        ├── ListTags/                      # NEW — distinct tag values for the caller, for filter UI
        └── ExportKnowledgeBase/           # NEW — mirrors ExportUserChatQuery

src/AskLucy.Infrastructure/
├── Files/
│   ├── LocalFileStorage.cs                # EXTENDED — adds DeleteAsync
│   ├── DocumentContentValidator.cs        # NEW — magic-byte sniffing (research.md Decision 8)
│   └── DocumentPageCountExtractor.cs      # NEW — PDF/DOCX/PPTX, BCL-only (research.md Decision 5)
└── KnowledgeBases/
    └── KnowledgeBasePurgeHostedService.cs # NEW — mirrors ProviderHealthCheckHostedService.cs

src/AskLucy.Persistence/
├── Configurations/
│   ├── KnowledgeBaseConfiguration.cs          # NEW
│   ├── KnowledgeBaseFolderConfiguration.cs    # NEW
│   ├── KnowledgeBaseDocumentConfiguration.cs  # NEW
│   ├── KnowledgeBaseTagConfiguration.cs       # NEW
│   ├── KnowledgeBaseCategoryConfiguration.cs  # NEW
│   └── KnowledgeBaseAuditLogConfiguration.cs  # NEW
├── Repositories/
│   └── KnowledgeBaseRepository.cs             # NEW (+ folder/document-focused query methods)
├── Seed/
│   └── KnowledgeBaseCategorySeed.cs           # NEW — the 8 predefined categories (FR-017)
└── Migrations/
    └── <timestamp>_AddKnowledgeBaseManagement.cs  # NEW

src/AskLucy.Web/Controllers/v1/
├── KnowledgeBasesController.cs            # NEW — CRUD, lifecycle actions, search, dashboard, export
└── KnowledgeBaseTaxonomyController.cs     # NEW — categories/tags list+create

src/AskLucy.Web/ClientApp/src/features/knowledge-base/
├── pages/
│   └── KnowledgeBaseDashboardPage.tsx     # NEW — grid/list toggle, search, filters, sections
├── components/
│   ├── KnowledgeBaseCard.tsx              # NEW
│   ├── KnowledgeBaseFolderTree.tsx        # NEW — keyboard-operable (FR-040), drag-and-drop
│   ├── KnowledgeBaseStatCards.tsx         # NEW
│   ├── KnowledgeBaseEditDialog.tsx        # NEW
│   └── ConfirmPurgeDialog.tsx             # NEW — explicit confirmation (FR-036, SC-004)
├── hooks/
│   ├── useKnowledgeBases.ts               # NEW — TanStack Query wrapper over the search endpoint
│   ├── useKnowledgeBaseMutations.ts       # NEW
│   └── useKnowledgeBaseDragAndDrop.ts     # NEW — keyboard-accessible equivalent per FR-040
└── store/
    └── knowledgeBaseDashboardStore.ts     # NEW — Zustand: view mode, active filters (UI-only state)
```

**Structure Decision**: Extends the existing layered backend
(`Domain` → `Application` → `Infrastructure`/`Persistence` → `Web`) and the existing
`src/features/<domain>` frontend convention. No new project, no new top-level directory —
consistent with constitution §7 (Convention Over Configuration) and modeled directly on the
already-shipped `Chats` feature, which has the closest-matching lifecycle shape of anything
in the codebase.

## Post-Design Re-check

Re-evaluated after Phase 1 (data-model.md, contracts/, quickstart.md): no new violations were
introduced. The `KnowledgeBasePermission` deviation flagged above remains the only notable
departure from the spec's literal Database section, and it is a *reduction* in surface area,
not a new architectural pattern requiring an ADR. `KnowledgeBaseDocument` is a genuinely new
entity not named in spec.md's Database section, but it is required to make FR-014/FR-016/
FR-030 concrete (folders must contain *something*, and that something needs a queryable
association to a knowledge base/folder/stats) — the spec's own Assumptions section already
anticipated this by describing "Document" as data owned elsewhere for upload mechanics while
leaving its organizational association to this spec. All Constitution Check gates above
remain PASS.

## Complexity Tracking

*No entries — the one flagged deviation (§2.III row above) is a simplification relative to
the spec's literal Database section, not an unjustified complexity addition.*
