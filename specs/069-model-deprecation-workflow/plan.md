# Implementation Plan: Model Deprecation Workflow

**Branch**: `069-model-deprecation-workflow` | **Date**: 2026-10-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/069-model-deprecation-workflow/spec.md`

## Summary

Confirming a "no longer listed by vendor" row in the sync review will deprecate the model instead of marking it Unavailable. In the same save, the platform picks a same-provider replacement (a pure ranking over the catalog's price, context size and capabilities), reassigns or flags every platform default, and notifies admins. A background job then permanently switches user-owned items (conversations, agent and workflow drafts, prompts) to the replacement and sends each owner one notice. Published agent and workflow versions are immutable, so a run-time resolver redirects them to the replacement or fails with a clear message. Deprecated becomes terminal in the Domain, closing the gap that only the UI enforced it. Admins validate the replacement afterwards. Notices go through the notifications hub (spec 067), whose in-app delivery works today and whose email channel is not built yet. Details: [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10; React 19 + TypeScript (strict) + Vite

**Primary Dependencies**: MediatR, FluentValidation, EF Core, Hangfire (existing), the spec 067 hub and the spec 074 failure recorder (existing); MUI, TanStack Query, React Hook Form (existing). No new packages.

**Storage**: SQL Server. Four new tables plus a migration (see [data-model.md](data-model.md)). No vector or file storage.

**Testing**: xUnit for Domain, Application, Persistence and Web; Vitest, Testing Library and axe for the frontend. Persistence tests use the dedicated test database (`PERSISTENCE_TESTS_2_CONNECTION_STRING`).

**Target Platform**: Existing ASP.NET Core host on site4now.net, plus the existing React client.

**Project Type**: Web application (backend + frontend).

**Performance Goals**: Confirm returns in under 10 s with thousands of affected users (SC-007). Admin summary visible within 5 minutes (SC-006). Impact preview adds one query per diff call.

**Constraints**: No domain-event dispatcher or transaction API on `IUnitOfWork` exists, so there is one save per request, with chunked, idempotent background work. `Application` must not reference EF Core. Published versions are never rewritten. Email goes out only once 067's channel exists.

**Scale/Scope**: A few dozen models per provider, hundreds of workflow drafts, thousands of users at most.

## Constitution Check

*Gate: pass before research, re-checked after design. Result: **pass**, with one justified exception.*

| Principle | Assessment |
|---|---|
| I, III Dependency rule, Domain purity | Ranking (`ReplacementModelSelector`), the capability map and the status lock are pure Domain. EF-dependent queries live in Persistence behind interfaces owned by Application. Pass. |
| II, IV SOLID, composition | `ModelDeprecationService`, `ExecutableModelResolver`, `PlatformDefaultFlagResolver` and the user-impact job each have one reason to change. Pass. |
| V DRY, YAGNI | One ranking function serves preview and confirm. One payload builder serves both summary types. No workflow-reference projection. Pass. |
| VIII No silent failures | Run-time redirects log and attribute the output. A retired model with no successor fails loudly with a user-safe message and is reported to the failure trail. Notice and job failures are recorded. Pass. |
| §3 CQRS, UoW | Commands for apply, accept and change. Queries for list and detail. One `SaveChangesAsync` at confirm. The job commits one chunk per save. Pass. |
| §3 Domain events | The rule says state changes other parts must react to use domain events. No dispatcher exists, so flag resolution is a direct call inside the same unit of work. See Complexity Tracking. |
| §5 Database | Surrogate keys, indexed foreign keys, concurrency tokens, reversible migration, filters for soft delete. Pass. |
| §6 API | Problem Details, cursor paging on lists, versioned routes, sub-resource actions, OpenAPI. Pass. |
| §7 UI | MUI theme, light and dark, keyboard and axe tests, virtualized impact list not needed (dozens of rows). Pass. |
| §8 Security | Server-side permission check on every route, counts-only preview (no user identities), audited admin actions, rate limits. Pass. |
| §9 AI | Runs resolve models through the abstraction. No vendor types in Application. Pass. |
| §10 Testing | Tests in the same change, including fault injection for the job. Pass. |

## Project Structure

### Documentation (this feature)

```text
specs/069-model-deprecation-workflow/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── admin-model-deprecation.md
│   └── notification-types.md
├── checklists/requirements.md
└── tasks.md              # created by /speckit-tasks
```

### Source code

```text
src/
├── AskLucy.Domain/
│   ├── Ai/
│   │   ├── AIModel.cs                         # Deprecate(), terminal SetStatus
│   │   ├── ReplacementModelSelector.cs        # new, pure ranking (D1)
│   │   ├── PlatformFunctionRequirements.cs    # new, capability map (D2)
│   │   ├── ModelDeprecationBatch.cs           # new
│   │   ├── ModelDeprecation.cs                # new
│   │   ├── PlatformDefaultChange.cs           # new
│   │   └── ItemSwitch.cs                      # new
│   └── Notifications/NotificationTypeCatalog.cs   # 4 new types
├── AskLucy.Application/
│   ├── Ai/
│   │   ├── Commands/ApplyProviderModelSync/       # deprecate instead of Unavailable
│   │   ├── Commands/AcceptModelReplacement/       # new
│   │   ├── Commands/ChangeModelReplacement/       # new
│   │   ├── Queries/GetProviderModelSyncDiff/      # adds impact
│   │   ├── Queries/ListModelDeprecations/         # new
│   │   ├── Queries/GetModelDeprecation/           # new
│   │   ├── Deprecation/ModelDeprecationService.cs
│   │   ├── Deprecation/PlatformDefaultFlagResolver.cs
│   │   ├── Deprecation/ExecutableModelResolver.cs
│   │   ├── Deprecation/DeprecationNoticePublisher.cs
│   │   └── Abstractions/IModelDeprecationRepository.cs, IModelReferenceStore.cs
│   ├── Agents/Runtime/AgentExecutionOrchestrator.cs   # resolver call
│   ├── Workflows/Runtime/PromptNodeExecutor.cs        # resolver call
│   ├── Prompts/Commands/ExecutePrompt/                # resolver call
│   └── Chats/Commands/SendChatMessage/                # resolver in validator
├── AskLucy.Infrastructure/
│   ├── Ai/Deprecation/ModelDeprecationUserImpactJob.cs, ModelDeprecationSweepJob.cs
│   └── Notifications/Templates/Seed/en/system.ai-model.*.json   # 8 files
├── AskLucy.Persistence/
│   ├── Configurations/                                # 4 new configurations
│   ├── Repositories/ModelDeprecationRepository.cs, ModelReferenceStore.cs
│   └── Migrations/…_AddModelDeprecationWorkflow.cs
└── AskLucy.Web/
    ├── Controllers/v1/AdminAiProvidersController.cs   # new routes
    ├── Program.cs                                     # recurring sweep registration
    └── ClientApp/src/features/admin/
        ├── components/   # SyncReviewDialog impact + replacement picker, DeprecationRecordDialog, FlagBanner
        ├── api/ hooks/   # deprecation API and hooks
        └── pages/AdminAiProvidersPage.tsx                 # banner, record entry

tests/
├── AskLucy.Domain.Tests/Ai/                    # selector, status lock, requirements
├── AskLucy.Application.Tests/Ai/Deprecation/   # service, resolver, flag resolver, notices
├── AskLucy.Persistence.Tests/Ai/               # impact query, rewrite chunks, uniqueness
└── AskLucy.Web.Tests/Ai/ModelDeprecation/      # endpoints, end-to-end, job fault injection
```

**Structure Decision**: The existing Clean Architecture layout and the `Ai` feature folders. Frontend changes stay inside `features/admin`, extending the sync-review dialog and AI Providers page from specs 008/009. No new project.

## Delivery order

1. Domain: status lock, `Deprecate()`, selector, requirement map, new entities, tests.
2. Persistence: configurations, migration, repositories, impact query.
3. Apply path: `ModelDeprecationService`, platform defaults, admin summaries, apply-handler change.
4. Status endpoint lock, run-time resolver wired into chat, agents, workflow steps and prompts.
5. User-impact job, sweep, user notices, replacement validation commands.
6. Frontend: impact in the sync dialog, replacement picker, record dialog, flag banner.
7. Documentation, quickstart pass, full test suites.

Steps 1 to 4 already remove the silent-fallback risk. Steps 5 and 6 can ship after.

## Complexity Tracking

| Violation | Why needed | Simpler alternative rejected because |
|---|---|---|
| Flag resolution as a direct call inside the admin's save, not a domain event (§3) | There is no domain-event dispatcher in the codebase, and FR-010 wants the flag to close with the fix | Building a dispatcher for one use is a cross-cutting change that would need its own ADR; a post-commit event would also open a window where the fix is saved but the flag is still open |
| Two summary notification types instead of one (research D7) | FR-020b needs a role-dependent email lock, and 067's preferences work (US4) isn't built | Changing the hub's mandatory-channel rule from outside spec 067. Merge the two types when 067 supports role-conditional locks |

## Spec changes made during planning

- **FR-020a, SC-006 and the 067 assumption** were reworded: until 067's email channel ships, the email is recorded as Skipped and is not retried later (research D8).
- **FR-020c** was added so the follow-up notice after a replacement change (FR-011b) is explicitly its own notification type.
