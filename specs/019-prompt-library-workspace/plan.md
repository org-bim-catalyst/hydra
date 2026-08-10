# Implementation Plan: Prompt Library & Prompt Engineering Workspace

**Branch**: `019-prompt-library-workspace` | **Date**: 2026-08-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/019-prompt-library-workspace/spec.md`

## Summary

Introduce a new `Prompts` bounded context that lets users author, version, test, organize, and reuse
structured prompts as standalone assets — independent of any single conversation — then use them from
a dedicated Prompt Workspace, from a live chat conversation, or (optionally) augmented with RAG/Memory
context. A `Prompt` aggregate owns an immutable `PromptVersion` history (every content/variable/
model-setting change creates a new version, never overwriting prior ones); variables are detected from
`{{name}}` placeholders by a small, dependency-free regex helper and validated (type/required/length/
format/allowed-values) before any execution. Execution is entirely built on existing abstractions —
`IAIProvider`/`IAIProviderResolver` for the provider call (streamed via the same MediatR
`IStreamRequest` + SSE shape `AiController` already uses for voice replies), `IRagService`/
`IMemoryService` reused verbatim (their `userChatId` parameter is confirmed, by reading both
implementations, to be a logging-correlation id only — `PromptExecution.Id` is passed in that slot,
requiring zero interface changes), and `CostEstimator` for token-cost display. Inserting a prompt into
a live conversation resolves variables locally and then delegates entirely to the existing
`SendChatMessageCommand` — no parallel send-message path is built. Folders (nested, arbitrary depth)
and categories/tags reuse `KnowledgeBaseFolder`/`KnowledgeBaseCategory`/`KnowledgeBaseTag`'s exact
established shapes. Name-uniqueness-per-owner and concurrent-edit rejection are satisfied by patterns
already proven elsewhere in this codebase (`DuplicateResourceException` → 409; `BaseEntity.RowVersion`
+ `DbUpdateConcurrencyException` → 409) rather than new mechanisms. Search reuses SQL Server full-text
search, matching the existing conversation-search precedent. Bulk export/import uses one JSON schema
(a single prompt is a one-element array) with atomic, all-or-nothing validation. AI Agents, MCP,
Workflow Automation, a Prompt Marketplace, team sharing, and automated evaluation are explicitly out
of scope (spec.md) — the data model documents where those attach later without restructuring what
ships now, but builds no unused tables for them today.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend, existing solution); TypeScript 5.x / React 19
(frontend, existing `ClientApp`). No new language.

**Primary Dependencies**: **Zero new NuGet packages and zero new frontend dependencies**
(research.md "Summary of dependencies"). Backend reuses `IAIProvider`/`IAIProviderResolver`
(execution, research.md Decision 2), `IRagService`/`IMemoryService` (context augmentation, Decision
3), `CostEstimator` (Decision 11), `MediatR` `IStreamRequest`/SSE (Decision 2), `FluentValidation`,
SQL Server native `FULLTEXT INDEX` (Decision 12, already used by
`specs/002-chat-history-management`). Frontend reuses existing MUI, TanStack Query,
`@tanstack/react-virtual` (long prompt-library lists, FR-053), React Hook Form + Zod, Zustand — no new
frontend dependency; the only new frontend surface is a `features/prompts` module mirroring
`features/knowledge-base`/`features/memory`'s established shape.

**Storage**: SQL Server via EF Core (existing `AskLucy.Persistence`) — twelve new tables in the new
`Prompts` bounded context (`Prompt`, `PromptVersion`, `PromptVariable`, `PromptCategory`, `PromptTag`,
`PromptFolder`, `PromptTestCase`, `PromptExecution`, `PromptExecutionResult`, `PromptRating`,
`PromptUsageStatistics`, `PromptAuditLog` — data-model.md). No existing table is altered;
`PromptExecution.ResultMessageId` references `Chats.Message` by id without adding a column to it. No
placeholder tables are created for `PromptPermission`/`PromptShare`/`PromptEvaluation`
(data-model.md "Explicitly Not Modeled" — FR-061 requires the schema not *preclude* adding them later,
not that they exist unused today).

**Testing**: xUnit (backend) for Domain/Application unit tests (variable-placeholder detection/
validation, version-snapshot/restore invariants, name-uniqueness and folder-cycle domain rules,
`ExecutePromptCommandHandler`'s message-assembly ordering per research.md Decision 14, all tested with
faked `IAIProvider`/`IRagService`/`IMemoryService`/`IAIProviderResolver`, no real SQL Server/LLM
dependency) and Infrastructure/Persistence integration tests (EF configurations, the
`FULLTEXT INDEX` search path, filtered unique index behavior, `DbUpdateConcurrencyException` on a
stale `RowVersion`); Vitest + React Testing Library + jest-axe (frontend) for the Prompt Library,
Editor, Variable Editor, Version History/Compare, and Testing Console; Playwright E2E
(`tests/AskLucy.E2E.Tests`) covering create→test→version→restore, organize→search-at-scale,
insert-into-conversation, RAG/Memory-augmented execution, and export→import journeys, mirroring the
existing `KnowledgeBase*.spec.ts`/`Memory*.spec.ts` suites' shape.

**Target Platform**: ASP.NET Core 10 on the existing Windows/IIS (ANCM) deployment; React SPA static
build served the same way. No new deployment-time prerequisite — the `FULLTEXT INDEX` capability is
already required and used by `specs/002-chat-history-management`.

**Project Type**: Web application — extends the existing layered .NET backend + React SPA. No new
top-level project. One new bounded-context folder (`Prompts`) at each backend layer (research.md
Decision 1).

**Performance Goals**: Directly from spec.md Success Criteria — create a reusable prompt in under 3
minutes (SC-001); open-prompt-to-first-streamed-test-result in under 60 seconds (SC-002); locate a
prompt among 1,000+ via search/filter in under 10 seconds (SC-003, satisfied structurally by the
`FULLTEXT INDEX` + cursor pagination, research.md Decision 12); zero destroyed version history
(SC-005); conversation context never lost when inserting a prompt (SC-006, 100%); zero cross-user
prompt/version/execution exposure (SC-008).

**Constraints**: All list/search endpoints are cursor-paginated (constitution §6, matching
`KnowledgeBases`/`Documents`/`Retrieval`/`Chats`/`Memories`). Execution is blocked outright — no
partial/best-effort AI call — when required-variable or model-capability validation fails (FR-004,
FR-013, SC-004). A second concurrent edit to the same prompt is rejected, never silently merged or
overwritten (FR-007). Inserting a prompt into a conversation must not alter that conversation's
already-selected provider/model or discard prior context (FR-080, SC-006). Variable values and
retrieved RAG/Memory content are structurally separated from system/developer instructions at
execution time and can never override them (FR-083, FR-092, research.md Decision 14). Bulk
export/import is atomic — any invalid entry rejects the entire operation (FR-071).

**Scale/Scope**: All authenticated users at launch, scoped to prompts they own — same
private-only-in-this-release model as `KnowledgeBases` (specs/014) and `Memory` (specs/018). Scale
target is "thousands of prompts per user" remaining responsive (FR-053, spec.md Assumptions) — a
per-user scale target, matching the `FULLTEXT INDEX` + cursor-pagination approach already proven
sufficient at this scale for conversation search.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see "Post-Design
Re-check" below.*

| Principle / Gate | Status | Notes |
|---|---|---|
| §2.I / §3 Clean Architecture & Dependency Rule | PASS | All new entities live in `Domain/Prompts`. No Domain/Application code references a specific AI vendor SDK, EF Core, or raw SQL directly — `IAIProvider`/`IRagService`/`IMemoryService` stay the only touchpoints, all pre-existing Application-owned interfaces (research.md Decisions 2–3). SQL/full-text specifics live only in `Infrastructure/Prompts`/`Persistence/Configurations/Prompts`. |
| §2.II SOLID (SRP/OCP/DIP) | PASS | `Prompts` is its own bounded context with a distinct reason to change from `Ai`/`Chats`/`KnowledgeBases` (research.md Decision 1). Adding a new `PromptType` or `PromptVariableType` value is a closed-enum change, not new class sprawl; adding a new variable type is a new enum case plus a new resolver branch, never an edit to `Prompt`'s versioning/uniqueness invariants (OCP). |
| §2.III Simplicity — DRY/KISS/YAGNI, avoid unnecessary dependencies | PASS (zero new dependencies) | Folders/categories/tags/search/cost/streaming/concurrency all reuse an already-proven pattern rather than inventing a parallel one (research.md Decisions 5, 6, 7, 8, 11, 12, 2). Variable placeholders use a plain regex, not a templating-library dependency (Decision 10). `PromptPermission`/`PromptShare`/`PromptEvaluation` are explicitly *not* built as empty tables (data-model.md "Explicitly Not Modeled") — FR-061 is satisfied by the schema not precluding their later addition, not by pre-building unused structure. Inserting a prompt into a conversation delegates to the existing `SendChatMessageCommand` rather than reimplementing chat delivery (Decision 4) — the single largest duplication risk in this feature, closed. |
| §2.VIII No Silent Failures (NON-NEGOTIABLE) | PASS | FR-101/SC-010 (every execution failure — validation, provider error, timeout — reaches the user with a specific message, never a silently truncated SSE stream or a swallowed exception) is enforced the same way `StreamVoiceReplyCommand` already does: the stream itself carries a terminal error event, and `PromptExecution.Outcome = Failed` is persisted with a sanitized `ErrorDetail`, never silently dropped. Bulk import failures reject the whole operation with a specific error rather than partially succeeding silently (FR-071). |
| §3 CQRS/MediatR/Repository/FluentValidation | PASS | Every mutation (create/update/delete/archive/restore/duplicate a prompt, create/rename/move/delete a folder, create/restore a version, execute a prompt, save a test case, rate an execution, export/import) is an `IRequest`/`IStreamRequest` + handler pair validated by the existing `ValidationBehavior` pipeline. Queries (list/search prompts, list/compare versions, list/compare executions, statistics) are separate query handlers, never mutating state. Repositories expose aggregate-oriented methods (e.g., `IPromptRepository.GetByOwnerAndNameAsync`), not a leaky `IQueryable` escape hatch. |
| §5 Database — entity design, soft delete, auditing | PASS | Every new aggregate extends `BaseEntity` (surrogate `Guid` v7, audit columns via the existing `AuditSaveChangesInterceptor`, `RowVersion`). `Prompt` uses the standard soft-delete/global-query-filter convention (constitution §5), same reasoning as `KnowledgeBaseDocument`/`Memory`. `PromptVersion`/`PromptVariable` are append-only-with-cascade (no meaning independent of their `Prompt`/`PromptVersion`); `PromptAuditLog` is append-only-*without* cascade (must survive its subject's deletion), matching `DocumentAuditLog`/`MemoryAuditLog`'s established pattern exactly. |
| §5 Concurrency | PASS | `Prompt.RowVersion` (from `BaseEntity`) guards concurrent edits from two sessions; a second concurrent write returns the standard `DbUpdateConcurrencyException`-handled `409 Conflict` — zero new code, already-wired middleware (research.md Decision 8, directly implementing FR-007). |
| §6 REST conventions, pagination, Problem Details | PASS | `/api/v1/prompts` (CRUD + list/search), `/api/v1/prompts/{id}/actions/*` (archive/restore/duplicate — non-CRUD state changes as sub-resource actions, matching `POST /chats/{id}/actions/retry`'s established shape), `/api/v1/prompts/{id}/versions/*`, `/api/v1/prompt-folders`, `/api/v1/prompts/{id}/executions`, `/api/v1/prompts/export`\|`/import` (contracts/*.md). List endpoints are cursor-paginated. No new Problem Details `type` is introduced — every failure mode maps onto an already-registered type. |
| §6 Streaming | PASS | Prompt test execution streams via SSE, the identical mechanism chat and voice-reply already use (research.md Decision 2) — no new streaming transport. |
| §6 AuthN/AuthZ | PASS | `[Authorize]` by default; a new `PromptOwnershipGuard` (mirrors `MemoryOwnershipGuard`/`ChatOwnershipGuard`) enforced in every Prompt command/query handler. A request naming a prompt the caller doesn't own returns `404`, not `403` (FR-090, avoiding existence disclosure, same convention as Memory). |
| §6 Rate limiting | PASS | New `prompt-endpoints` policy (contracts/*.md), matching the existing per-feature named-policy convention. |
| §8 Security — prompt injection | PASS, directly addressed | FR-083/FR-092 (spec.md's own "Prompt Injection Considerations" section) are satisfied by explicit, ordered, delimited message assembly (research.md Decision 14) reusing the exact delimiter/defensive-framing convention `specs/018-ai-memory-system` already proved correct for the identical constitutional requirement — variable values and retrieved RAG/Memory content are only ever interpolated into content strings, never allowed to replace or be concatenated into the instruction segments themselves. |
| §8 Security — audit logging | PASS | `PromptAuditLog` (FR-090) mirrors `KnowledgeBaseAuditLog`/`MemoryAuditLog`'s established convention: append-only, sanitized `DetailsJson` (never raw prompt content, FR-091), no cascade FK so it survives a hard-purged prompt. |
| §8 Security — least privilege & secure defaults | PASS | Prompts are private-only in this release with no opt-out (FR-060); a required-but-unmet model capability always blocks execution rather than silently degrading (FR-004); a required-but-missing/invalid variable always blocks execution rather than substituting a guessed value (FR-013). |
| §9 AI Principles — provider/model abstraction | PASS | Every LLM call this feature makes goes through the existing `IAIProvider`/`IAIProviderResolver` abstraction — no Application/Domain code references OpenAI/Anthropic/Gemini/OpenRouter directly (FR-046). |
| §9 AI Principles — token usage & cost monitoring | PASS | Every prompt execution records input/output token counts and estimated cost via the existing `ChatUsage`/`CostEstimator` convention every other `IAIProvider` call already uses (FR-042, FR-100). |
| §10 Testing | PASS (planned in tasks) | Domain/Application unit-tested with faked `IAIProvider`/`IRagService`/`IMemoryService`; Infrastructure/Persistence integration-tested against a real SQL Server instance (full-text search, filtered unique index, `RowVersion` conflict); new frontend hooks/components covered by Vitest+RTL+jest-axe; Playwright E2E covers create→test→version, organize-at-scale, conversation-insertion, RAG/Memory execution, and export/import journeys end to end. |
| §14 Observability | PASS | Serilog structured logging on every execution outcome branch (mirrors `RagServiceLog`/`MemoryServiceLog`); `PromptAuditLog` (security/lifecycle trail) kept distinct from operational Serilog output, matching the established split precedent. |
| §15 Performance | PASS | Search/list is `FULLTEXT INDEX` + cursor-paginated, not a full-table scan (SC-003/FR-053); execution streams rather than buffering the full response before the first byte (SC-002). |
| §7 UI — accessibility, responsive, theming | PASS | Prompt Workspace mirrors the existing `knowledge-base`/`memory` dashboard components' established MUI/jest-axe-tested shape — no new design-system surface introduced; long prompt lists use `@tanstack/react-virtual`, already a dependency (FR-053). |

No Complexity Tracking entries — every gate above is a clean PASS. The one design choice worth
flagging as a deliberate, justified reuse (not a new pattern requiring justification) is passing
`PromptExecution.Id` into `IRagService`/`IMemoryService`'s `userChatId` parameter slot (research.md
Decision 3) — verified safe by reading both implementations, not assumed.

**Post-Design Re-check** (after Phase 1 — data-model.md, contracts/, quickstart.md): No new gate
concerns emerged during data-model/contract design. Two design choices worth recording as having been
considered against constitution §2.III (Simplicity/YAGNI) during Phase 1 and resolved in favor of
*not* adding structure: (1) spec.md's `PromptPermission`/`PromptShare`/`PromptEvaluation` key entities
are confirmed, once the field-level design was worked out, to need no table today — `Prompt.OwnerId`
alone satisfies every FR-060/FR-061 requirement in this release, and adding those tables later is
purely additive (data-model.md "Explicitly Not Modeled"). (2) `PromptExecutionResult` is intentionally
**not** created for `Origin: ConversationInsertion` executions — the AI output already lives on the
referenced `Chats.Message`, and duplicating it would create two divergent copies of the same data
(data-model.md `PromptExecutionResult` note). All gates remain PASS; no Complexity Tracking entries
were added.

## Project Structure

### Documentation (this feature)

```text
specs/019-prompt-library-workspace/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
│   ├── prompts-api.md
│   ├── prompt-execution-api.md
│   └── prompt-conversation-integration-api.md
├── checklists/
│   └── requirements.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

This is the existing Web application (Option 2 shape), not a new project — it extends the
already-established `src/AskLucy.*` layered backend and `src/AskLucy.Web/ClientApp` React SPA exactly
as specs/014, specs/016, and specs/018 did.

```text
src/
├── AskLucy.Domain/
│   ├── Prompts/                         # NEW — Prompt, PromptVersion, PromptVariable,
│   │                                    #   PromptCategory, PromptTag, PromptFolder,
│   │                                    #   PromptTestCase, PromptExecution,
│   │                                    #   PromptExecutionResult, PromptRating,
│   │                                    #   PromptUsageStatistics, PromptAuditLog
│   ├── Ai/                              # UNCHANGED — AIModelCapabilities reused as-is
│   ├── Chats/                           # UNCHANGED — Message referenced by id only
│   ├── Retrieval/                       # UNCHANGED — IRagService reused as-is
│   └── Memory/                          # UNCHANGED — IMemoryService reused as-is
│
├── AskLucy.Application/
│   ├── Abstractions/                    # UNCHANGED — IAIProvider/IAIProviderResolver/
│   │                                    #   IRagService/IMemoryService reused verbatim;
│   │                                    #   EXTENDED with IPromptRepository et al.
│   ├── Prompts/
│   │   ├── Authorization/               # PromptOwnershipGuard
│   │   ├── Commands/                    # CreatePrompt, UpdatePrompt, DeletePrompt,
│   │   │                                #   ArchivePrompt, RestorePrompt, DuplicatePrompt,
│   │   │                                #   CreateFolder, RenameFolder, MoveFolder,
│   │   │                                #   DeleteFolder, RestoreVersion, DuplicateVersion,
│   │   │                                #   ExecutePrompt (IStreamRequest), SaveTestCase,
│   │   │                                #   RateExecution, ExportPrompts, ImportPrompts,
│   │   │                                #   InsertPromptIntoConversation, AddTag, RemoveTag,
│   │   │                                #   CreateCustomCategory
│   │   ├── Queries/                     # GetPrompt, ListPrompts (search/filter), ListVersions,
│   │   │                                #   GetVersion, CompareVersions, ListExecutions,
│   │   │                                #   GetExecution, CompareExecutions, GetFolderTree,
│   │   │                                #   ListTestCases, GetPromptStatistics, ListTags,
│   │   │                                #   ListCategories, PreviewPrompt
│   │   └── PromptContentAnalyzer.cs     # Pure Domain-adjacent helper — placeholder detection
│   │                                    #   (research.md Decision 10)
│
├── AskLucy.Infrastructure/
│   └── Prompts/                         # PromptExportFileBuilder, PromptImportValidator
│                                        #   (research.md Decision 13)
│
├── AskLucy.Persistence/
│   ├── Configurations/Prompts/          # NEW — EF Fluent API configs for every new entity
│   └── Migrations/                      # NEW — Prompts tables + FULLTEXT INDEX (Decision 12)
│
└── AskLucy.Web/
    ├── Controllers/v1/
    │   ├── PromptsController.cs                  # NEW
    │   └── PromptFoldersController.cs             # NEW
    ├── Controllers/v1/ChatsController.cs          # EXTENDED — POST /chats/{id}/prompt-messages
    └── ClientApp/src/features/
        └── prompts/                     # NEW — Library, Editor, Variable Editor,
                                         #   Version History/Compare, Testing Console,
                                         #   Execution History, Folder tree, Import/Export
            ├── api/
            ├── components/
            ├── hooks/
            ├── pages/
            └── store/

tests/
├── AskLucy.Domain.Tests/Prompts/
├── AskLucy.Application.Tests/Prompts/
├── AskLucy.Infrastructure.Tests/Prompts/
├── AskLucy.Persistence.Tests/Prompts/
├── AskLucy.Web.Tests/Prompts/
└── AskLucy.E2E.Tests/                  # NEW specs: PromptLifecycle, PromptVersioning,
                                         #   PromptTestingWorkspace, PromptOrganizationAtScale,
                                         #   PromptConversationInsertion,
                                         #   PromptRagMemoryExecution, PromptExportImport
```

**Structure Decision**: Extends the existing single-solution layered backend
(`Domain`→`Application`→`Infrastructure`/`Persistence`→`Web`) plus the existing React SPA under
`AskLucy.Web/ClientApp`, per constitution §3. `Prompts` is one new bounded-context folder at each
layer (research.md Decision 1); `Chats` receives no new entity or column — only a new controller
action that delegates to its existing `SendChatMessageCommand` (research.md Decision 4).

## Complexity Tracking

*No entries — the Constitution Check above has no violations requiring justification.*
