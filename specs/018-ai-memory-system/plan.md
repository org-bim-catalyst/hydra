# Implementation Plan: AI Memory System

**Branch**: `018-ai-memory-system` | **Date**: 2026-08-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/018-ai-memory-system/spec.md`

## Summary

Introduce two new bounded contexts — `Memory` and `Projects` — that let Ask Lucy remember durable,
per-user facts and preferences across conversations and inject them into chat generation, without
coupling to any specific AI provider, vector database, or the existing RAG pipeline (FR-003).
Memory candidates are produced by a Hangfire background job (`MemoryExtractionJob`) that runs after
every assistant turn plus a periodic sweep, using a single structured LLM call (via the existing
`IAIProvider`/`IAIProviderResolver` abstraction) to classify content, category, sensitivity, and
whether it was explicitly requested. Relevant memories are selected by a composite score (vector
similarity + importance/confidence/recency/frequency) and injected as a clearly delimited,
defensively-framed system message immediately before RAG's own context message in
`SendChatMessageCommandHandler` — reusing that handler's proven never-block/never-throw
degrade-gracefully pattern (clarified 2026-08-09, FR-014a). Conflicting memories are detected via
vector-candidate retrieval plus one LLM judgment call: direct contradictions auto-update with full
history; ambiguous cases are resolved asynchronously through the Memory Center, never interrupting
the live conversation (clarified 2026-08-09, FR-016). A new lightweight `Project` entity
(`Projects` context) lets conversations be grouped so memories can be scoped to one project or kept
general (FR-002a/b). A Memory Center UI (new `features/memory` frontend module, mirroring the
existing `knowledge-base`/`retrieval` feature-folder conventions) gives users full visibility,
search, edit, delete, approve/reject, and account-level privacy controls (enable/disable, clear-all,
per-category settings, export). Every new piece — bounded-context placement, CQRS/MediatR handlers,
audit logging, SignalR notification, rate limiting, soft delete, encryption of PII content at rest —
extends an already-established pattern from specs/014–016 rather than inventing a new one; the one
genuinely new pattern (Hangfire's native `[AutomaticRetry]` attribute, unused elsewhere in this
codebase) is explicitly flagged in research.md Decision 6 rather than presented as precedent.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend, existing solution); TypeScript 5.x / React 19
(frontend, existing `ClientApp`). No new language.

**Primary Dependencies**: **Zero new NuGet packages.** Backend reuses everything already
referenced: `IAIProvider`/`IAIProviderResolver` (chat + classification calls, research.md Decisions
7/8/10), `IEmbeddingService`/`IEmbeddingServiceResolver` (reused verbatim from specs/016, research.md
Decision 5), `Microsoft.EntityFrameworkCore.SqlServer` 10.0.10's native `vector` column support (same
raw-ADO.NET technique `SqlServerVectorStore` already uses, research.md Decision 5), Hangfire (already
present since specs/015; this feature is the first to use its built-in `[AutomaticRetry]` attribute,
research.md Decision 6 — flagged as new *usage*, not a new package), `Microsoft.AspNetCore.SignalR`
(already present), and ASP.NET Core's Data Protection API (`IDataProtector`, already used for
provider credentials) for column-level encryption of `Memory.Content` (research.md Decision 12).
Frontend: existing MUI, TanStack Query, Zustand, React Hook Form + Zod, `@microsoft/signalr` — no new
frontend dependency.

**Storage**: SQL Server via EF Core (existing `AskLucy.Persistence`) — ten new tables in the `Memory`
bounded context (`Memory`, `MemoryVersion`, `MemoryApproval`, `MemoryConflict`, `MemoryEmbedding`,
`MemoryAuditLog`, `MemoryNotification`, `MemoryPreference`, `MemoryCategoryPreference`,
`MemoryReference`), one new table in the `Projects` bounded context (`Project`), plus one additive
column on an existing entity: `Chats.UserChat.ProjectId` (nullable FK). `MemoryEmbedding` reuses the
existing `Retrieval.EmbeddingProvider` catalog entity (no duplicate provider table) but is its own
table, not a reuse of `Retrieval.Embedding` (data-model.md, research.md Decision 5 — `Embedding`/
`IVectorStore` are hard-coupled to `DocumentChunkId`/`KnowledgeBaseId`, not reusable as-is).

**Testing**: xUnit (backend) for Domain/Application unit tests (ranking composite-score math,
conflict-classification branch logic, `IMemoryService`'s `Found`/`NoneRelevant`/`Unavailable`
branches tested with faked `IEmbeddingService`/`IMemoryVectorStore`/`IAIProvider`, no real SQL
Server/LLM dependency) and Infrastructure integration tests (real SQL Server test instance exercising
the native `vector` column and brute-force `VECTOR_DISTANCE` scan, `MemoryExtractionJob`'s
Hangfire-attribute retry behavior, encryption round-trip via `IDataProtector`); Vitest + React Testing
Library + MSW + jest-axe (frontend) for the Memory Center dashboard, preferences panel, and conflict
resolution dialog; Playwright E2E (`tests/AskLucy.E2E.Tests`) covering remember→recall-in-new-
conversation, approval workflow, and project-scoped-memory journeys, mirroring the existing
`KnowledgeBase*.spec.ts`/`Retrieval*.spec.ts` suites' shape.

**Target Platform**: ASP.NET Core 10 on the existing Windows/IIS (ANCM) deployment; React SPA static
build served the same way. No new deployment-time prerequisite beyond what specs/016 already flagged
(native SQL Server `vector` type support) — `MemoryEmbedding` reuses the same database capability,
not a new one.

**Project Type**: Web application — extends the existing layered .NET backend + React SPA. No new
top-level project. Two new bounded-context folders (`Memory`, `Projects`) at each backend layer
(research.md Decision 1).

**Performance Goals**: Directly from spec.md Success Criteria — a follow-up conversation started ≥1
day later correctly reflects ≥90% of previously stated stable facts (SC-001); Memory Center find→act
cycle under 30 seconds (SC-002); disable-or-clear-all in ≤3 actions with immediate effect (SC-003);
≥95% of sensitive candidates correctly held for manual review (SC-004); zero cross-user memory
exposure across security testing (SC-005); memory selection adds no perceptible response-start delay
even at thousands of memories per user (SC-006, satisfied structurally by FR-014a's degrade-gracefully
requirement plus per-user-scoped brute-force vector scan, research.md Decision 5).

**Constraints**: All list/search endpoints are cursor-paginated (constitution §6, matching
`KnowledgeBases`/`Documents`/`Retrieval`/`Chats`). Memory extraction, conflict detection, and
sensitivity classification all run as asynchronous Hangfire background jobs, never on the chat
request's critical path (research.md Decisions 6/7, clarified 2026-08-09 FR-014a). Memory-subsystem
failure at response-generation time degrades gracefully — the turn proceeds without memory context,
logged but never surfaced to the user as an error (FR-014a). Ambiguous conflict resolution happens
asynchronously via the Memory Center; the live conversation turn that surfaced the conflict is never
interrupted (FR-016, both clarified 2026-08-09). Background extraction failures retry automatically
with backoff and, once exhausted, are logged for the operating team without a user-facing error for
that pass (FR-006b, clarified 2026-08-09). Memory content is treated strictly as data, never as
instructions, when re-injected into a prompt (FR-029) — enforced via explicit defensive framing in
the injected system message (research.md Decision 9), stronger than RAG's current framing. Memory is
unmetered across all subscription tiers in this release — no Billing Engine integration (clarified
2026-08-09, spec.md Assumptions).

**Scale/Scope**: All authenticated users at launch, scoped to memories/Projects they own — same
private-only-in-this-release model as `KnowledgeBases` (specs/014) and `Retrieval` (specs/016). Scale
target is "thousands of stored memories per user" remaining responsive (SC-006/spec.md Assumptions) —
a per-user scale target, not a global-corpus target like RAG's 5M-chunk/organization figure, which is
why a per-user-scoped brute-force vector scan (no `CREATE VECTOR INDEX`, research.md Decision 5) is
sufficient here without the indexing concerns RAG had to navigate at its larger scale.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see "Post-Design
Re-check" below.*

| Principle / Gate | Status | Notes |
|---|---|---|
| §2.I / §3 Clean Architecture & Dependency Rule | PASS | All new entities live in `Domain/Memory`/`Domain/Projects` (plus the one additive field on `Chats.UserChat`, owned by the existing `Chats` aggregate via its own mutator method). `IMemoryService`, `IMemoryVectorStore`, `IMemoryConflictDetectionService`, `IMemoryNotifier` (naming mirrors `IRagService`/`IVectorStore`/`IProcessingNotifier`) are Application-owned interfaces; SQL/Hangfire/SignalR/vector specifics live only in `Infrastructure/Memory` and `Persistence/Memory`. No Domain/Application code references a specific AI vendor SDK, Hangfire's static facade, or raw SQL vector syntax directly. |
| §2.II SOLID (SRP/OCP/DIP) | PASS | `Memory` and `Projects` are each their own bounded context with a distinct reason to change (research.md Decision 1) rather than one folder for both. Adding a new `MemoryCategory` or `MemoryConflictType` value is a closed-enum change, not new class sprawl; adding a new memory *source* is a new `MemorySourceType` case plus a new caller, never an edit to `MemoryExtractionJob`'s existing classification logic (OCP). |
| §2.III Simplicity — DRY/KISS/YAGNI, avoid unnecessary dependencies | PASS (zero new dependencies) | `IEmbeddingService`/`IEmbeddingServiceResolver` and the `EmbeddingProvider` catalog entity are reused verbatim (research.md Decision 5) rather than duplicated. `Memory Category` and `Memory Source` (spec.md's conceptual key entities) are explicitly *not* separate lookup tables (data-model.md "Explicitly Not Modeled") — no rows/behavior beyond what an enum + nullable FK already provide, the exact same reasoning specs/016 applied to `ChunkEmbedding`/`VectorIndex`. A generalized `IVectorStore<TOwner>` spanning RAG and Memory was explicitly considered and rejected as premature abstraction (research.md Decision 5) — two purpose-built implementations sharing only a raw-SQL *technique* is simpler today. |
| §2.VIII No Silent Failures (NON-NEGOTIABLE) | PASS | FR-014a (memory-subsystem failure at response time — logged, never silent, never blocks), FR-006b (background extraction failure — automatic retry then team-observable log, never silently dropped), and FR-016 (ambiguous conflicts surfaced via a durable `MemoryNotification`, never quietly discarded) all route through this codebase's existing non-silent-failure conventions — `RagService`'s catch-log-degrade pattern (research.md Decision 3) and `DocumentProcessingPipeline`'s catch-per-stage-and-log pattern (research finding #2), reused rather than reinvented. |
| §3 CQRS/MediatR/Repository/FluentValidation | PASS | Every mutation (create/edit/delete/approve/reject a memory, resolve a conflict, update preferences, create/rename/delete a Project, assign a conversation to a Project) is an `IRequest`/handler pair validated by the existing `ValidationBehavior` pipeline (research finding #8) — mirrors `UpdateConversationKnowledgeBasesCommand`'s exact shape. Queries (list/search memories, get preferences, list Projects, get memory references) are separate `IRequest` query handlers, never mutating state. Repositories expose aggregate-oriented methods (e.g., `IMemoryRepository.GetActiveByUserAndProjectAsync`), not a leaky `IQueryable` escape hatch. |
| §5 Database — entity design, soft delete, auditing | PASS | Every new aggregate extends `BaseEntity` (surrogate `Guid` v7, audit columns via the existing `AuditSaveChangesInterceptor`, `RowVersion`). `Memory` and `Project` use the standard soft-delete/global-query-filter convention (constitution §5) rather than a bespoke deletion channel — chosen specifically because both are retention-sensitive, user-owned records exactly like `KnowledgeBaseDocument`. `MemoryVersion`/`MemoryApproval` are append-only-with-cascade (no meaning independent of their `Memory`); `MemoryAuditLog`/`MemoryReference`/`MemoryNotification` are append-only-*without* cascade (must survive their subject's deletion), matching `DocumentAuditLog`/`Citation`'s established pattern exactly. |
| §5 RAG & vector storage (explicit, non-negotiable clause) | PASS | This clause is written for RAG but generalizes as this platform's default posture for any vector-backed feature (research.md Decision 13): all Memory vector storage stays in SQL Server's native `vector` type — no separate vector database, no ADR needed. `MemoryEmbedding.EmbeddingProviderId` and the reused `EmbeddingProvider` catalog satisfy "chunking strategy [n/a here], embedding model identifier, and embedding version are stored alongside each vector" exactly as `Embedding` does today. |
| §5 Concurrency | PASS | `Memory.RowVersion` (from `BaseEntity`) guards concurrent edits/approvals of the same memory (e.g., a user approving a candidate from two open tabs) — a second concurrent write returns the standard `DbUpdateConcurrencyException`-handled `409 Conflict`, mirroring every other aggregate in this codebase. |
| §6 REST conventions, pagination, Problem Details | PASS | `/api/v1/memories` (CRUD + list/search), `/api/v1/memories/actions/*` and `/api/v1/memories/{id}/actions/*` (approve/reject/resolve-conflict/clear-all/export — non-CRUD state changes as sub-resource actions, matching `POST /chats/{id}/actions/retry`'s established shape), `/api/v1/memories/preferences`, `/api/v1/projects`, `/api/v1/chats/{id}/project` — matches the `KnowledgeBases`/`Retrieval` contract shape exactly (contracts/*.md). List endpoints are cursor-paginated. |
| §6 Streaming | PASS | Memory retrieval itself is non-streaming (a bounded, synchronous-from-the-caller's-view lookup before generation begins, exactly like RAG's retrieval step) — the chat *response* that follows continues to stream unaffected, and the extraction/conflict-detection/sensitivity LLM calls are explicitly background, non-streaming, batch-classification work — the constitution §9-cited justified exception, same reasoning specs/016 already used. |
| §6 AuthN/AuthZ | PASS | `[Authorize]` by default; a new `MemoryOwnershipGuard` (mirroring `ChatOwnershipGuard`/`KnowledgeBaseOwnershipGuard`, research finding #4/#8) enforced in every Memory/Project command and query handler. A request naming a memory the caller doesn't own returns `404`, not `403` (FR-027, avoiding existence disclosure). |
| §6 Rate limiting | PASS | New `memory-endpoints` policy (research.md Decision 17), matching the existing per-feature named-policy convention. |
| §8 Security — encryption of PII at rest | PASS | `Memory.Content`, `MemoryVersion.PreviousContent`, and `MemoryReference.ContentSnapshot` are encrypted at rest via an `IDataProtector`-backed value converter (research.md Decision 12) — this feature's content is PII by construction (personal facts/preferences), so constitution §8's "data at rest for secrets and sensitive PII uses column/field-level encryption" clause applies to the whole column, not only `IsSensitive` rows. |
| §8 Security — audit logging | PASS | `MemoryAuditLog` (FR-028) mirrors `KnowledgeBaseAuditLog`/`DocumentAuditLog`'s established convention: append-only, sanitized `DetailsJson` (never raw/decrypted content), no cascade FK so it survives a hard-purged memory. |
| §8 Security — prompt injection | PASS, strengthened | FR-029 explicitly requires memory content be treated as data, never instructions — the injected `<user_memory>` system message includes explicit defensive framing (research.md Decision 9) beyond what RAG's current `<context>` message has, directly satisfying constitution §8's general prompt-injection clause with the extra rigor spec.md itself demands. |
| §8 Security — least privilege & secure defaults | PASS | Every new feature flag/setting defaults to the more restrictive/secure option where a choice exists: `IsSensitive` candidates *always* require manual review regardless of configured mode (FR-008); a memory conflict *always* pauses that memory from use until resolved, never silently keeping a possibly-wrong version live (FR-016). The one deliberately permissive default — `MemoryEnabled = true` / `ApprovalMode = Automatic` out of the box — is not a security default but a clarified *product* decision (2026-08-09) with explicit user-facing controls to tighten it at any time, distinct from a security posture default. |
| §9 AI Principles — provider/model abstraction | PASS | Every LLM call this feature makes (extraction/classification, conflict judgment) goes through the existing `IAIProvider`/`IAIProviderResolver` abstraction with a configurable "utility model" key, never a hardcoded vendor SDK call — no Application/Domain code references OpenAI/Anthropic/Gemini/OpenRouter directly. |
| §9 AI Principles — memory (explicit clause) | PASS | This clause ("AI memory... stored distinctly from chat history, with explicit user visibility and control... never inferred silently and used without a way for the user to audit it") is this feature's own governing principle end to end: `Memory` is a distinct bounded context from `Chats`; the Memory Center gives full visibility/edit/delete; `MemoryAuditLog` + `MemoryNotification` (FR-006a) ensure nothing accumulates silently even under `Automatic` mode. |
| §9 AI Principles — token usage & cost monitoring | PASS | Extraction/classification/conflict-detection LLM calls record input/output token counts against the initiating user via the same `ChatCompletionResult` usage-tracking convention every other `IAIProvider` call already uses — no separate, uninstrumented LLM call path is introduced. |
| §10 Testing | PASS (planned in tasks) | Domain/Application unit-tested with faked `IEmbeddingService`/`IMemoryVectorStore`/`IAIProvider`; Infrastructure integration-tested against a real SQL Server vector-column instance and `IDataProtector` round-trip; new frontend hooks/components covered by Vitest+RTL+jest-axe; Playwright E2E covers remember→recall, approval, and project-scoping journeys end to end. |
| §14 Observability | PASS | Serilog structured logging in `MemoryExtractionJob`, `IMemoryService`, and `IMemoryConflictDetectionService`'s every outcome branch (mirrors `RagServiceLog`); `MemoryAuditLog` (security/compliance trail) kept distinct from operational Serilog output, matching the `IndexingLog`/`DocumentAuditLog` split precedent — no separate operational-log *table* is introduced for extraction (research.md's Decision 6 rationale: lower per-pass criticality than multi-stage document indexing, "log and continue" is sufficient, same as `DocumentStatisticsRecomputeJob`'s simpler sweep pattern). |
| §15 Performance | PASS | All extraction/classification/conflict-detection work is asynchronous/background (never blocking chat, FR-014a); memory selection is a per-user-scoped query (indexed `UserId`/`ProjectId`), not a global scan, keeping SC-006 achievable without `CREATE VECTOR INDEX` (research.md Decision 5's inherited platform constraint). |
| §7 UI — accessibility, responsive, theming | PASS | Memory Center mirrors the existing `knowledge-base`/`retrieval` dashboard components, already jest-axe-tested and MUI-themed — no new design system surface introduced. |

No Complexity Tracking entries — every gate above is a clean PASS. The one genuinely new *pattern*
in this feature (Hangfire's native `[AutomaticRetry]` attribute, research.md Decision 6) is not a
constitution violation requiring justification — it introduces zero new dependencies (Hangfire is
already referenced) and is explicitly flagged as new-but-warranted in research.md rather than
misrepresented as following existing convention.

**Post-Design Re-check** (after Phase 1 — data-model.md, contracts/, quickstart.md): No new gate
concerns emerged during data-model/contract design. One design choice worth recording as having been
considered against constitution §2.III (Simplicity/YAGNI) during Phase 1 and resolved in favor of
*not* adding a table: spec.md's conceptual "Memory Source" and "Memory Category" key entities were
both found, once the field-level design was worked out, to carry no independent rows/behavior beyond
`Memory.SourceType`/`SourceConversationId` and an `enum MemoryCategory` respectively
(data-model.md "Explicitly Not Modeled") — confirmed still correct after writing out every other
entity's full field list, and directly parallel to specs/016's identical `ChunkEmbedding`/
`VectorIndex` finding. All gates remain PASS; no Complexity Tracking entries were added.

## Project Structure

### Documentation (this feature)

```text
specs/018-ai-memory-system/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── memories-api.md
│   ├── memory-privacy-api.md
│   └── projects-api.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

This is the existing Web application (Option 2 shape), not a new project — it extends the
already-established `src/AskLucy.*` layered backend and `src/AskLucy.Web/ClientApp` React SPA
exactly as specs/014, specs/015, and specs/016 did.

```text
src/
├── AskLucy.Domain/
│   ├── Memory/                         # NEW — Memory, MemoryVersion, MemoryApproval,
│   │                                   #   MemoryConflict, MemoryEmbedding, MemoryAuditLog,
│   │                                   #   MemoryNotification, MemoryPreference,
│   │                                   #   MemoryCategoryPreference, MemoryReference
│   ├── Projects/                       # NEW — Project, ProjectDeletedDomainEvent
│   ├── Chats/                          # EXTENDED — UserChat gains ProjectId + AssignToProject(...)
│   └── Retrieval/                      # UNCHANGED — EmbeddingProvider is read (referenced by FK),
│                                        #   Embedding/IVectorStore are not reused (research.md Dec. 5)
│
├── AskLucy.Application/
│   ├── Abstractions/                   # EXTENDED — IMemoryService, IMemoryVectorStore,
│   │                                   #   IMemoryConflictDetectionService, IMemoryNotifier
│   ├── Memory/
│   │   ├── Authorization/              # MemoryOwnershipGuard
│   │   ├── Commands/                   # CreateMemory (explicit), EditMemory, DeleteMemory,
│   │   │                               #   ApproveMemory, RejectMemory, ResolveMemoryConflict,
│   │   │                               #   UpdateMemoryPreferences, ClearAllMemories,
│   │   │                               #   RequestMemoryExport
│   │   ├── Queries/                    # ListMemories, GetMemory, GetMemoryPreferences,
│   │   │                               #   GetMemoryReferences, ListMemoryNotifications,
│   │   │                               #   GetMemoryExportStatus
│   │   └── MemoryService.cs            # IMemoryService impl — ranking/selection (research.md Dec. 4)
│   └── Projects/
│       ├── Commands/                   # CreateProject, RenameProject, DeleteProject,
│       │                               #   AssignConversationToProject
│       └── Queries/                    # ListProjects
│
├── AskLucy.Infrastructure/
│   ├── Memory/
│   │   ├── MemoryExtractionJob.cs      # Per-turn + swept extraction (research.md Decision 6)
│   │   ├── MemoryExtractionSweepJob.cs
│   │   ├── MemoryConflictDetectionService.cs  # research.md Decision 10
│   │   ├── MemoryHub.cs                # research.md Decision 11
│   │   └── MemoryNotifier.cs
│   └── Ai/                             # UNCHANGED — IAIProvider/IAIProviderResolver reused as-is
│
├── AskLucy.Persistence/
│   ├── Configurations/Memory/          # NEW — EF Fluent API configs for every new entity
│   ├── Configurations/Projects/        # NEW — Project config
│   └── Memory/
│       └── SqlServerMemoryVectorStore.cs  # research.md Decision 5
│
└── AskLucy.Web/
    ├── Controllers/v1/
    │   ├── MemoriesController.cs                # NEW
    │   └── ProjectsController.cs                # NEW
    ├── Controllers/v1/ChatsController.cs         # EXTENDED — PUT /chats/{id}/project
    └── ClientApp/src/features/
        └── memory/                     # NEW — Memory Center, preferences panel,
                                         #   conflict-resolution dialog, Project picker
            ├── api/
            ├── components/
            ├── hooks/
            ├── pages/
            └── store/

tests/
├── AskLucy.Domain.Tests/{Memory,Projects}/
├── AskLucy.Application.Tests/{Memory,Projects}/
├── AskLucy.Infrastructure.Tests/Memory/
├── AskLucy.Persistence.Tests/Memory/
├── AskLucy.Web.Tests/{Memory,Projects}/
└── AskLucy.E2E.Tests/                  # NEW specs: remember-and-recall, memory-approval,
                                         #   project-scoped-memory, memory-conflict-resolution
```

**Structure Decision**: Extends the existing single-solution layered backend
(`Domain`→`Application`→`Infrastructure`/`Persistence`→`Web`) plus the existing React SPA under
`AskLucy.Web/ClientApp`, per constitution §3. `Memory` and `Projects` are two new bounded-context
folders at each layer (research.md Decision 1); `Chats` receives one additive field only
(`UserChat.ProjectId`), never a new parallel entity for a concept it already owns (conversations).

## Complexity Tracking

*No entries — the Constitution Check above has no violations requiring justification.*
