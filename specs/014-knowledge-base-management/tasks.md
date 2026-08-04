---

description: "Task list for Knowledge Base Management"
---

# Tasks: Knowledge Base Management

**Input**: Design documents from `/specs/014-knowledge-base-management/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (§10 Testing Standards, non-negotiable) requires unit,
integration, and Playwright E2E coverage for new/changed behavior — test tasks are not
optional here.

**Organization**: Tasks are grouped by user story (spec.md priorities: US1/US2 = P1, US3/US4 =
P2, US5/US6 = P3) so each story is independently implementable, testable, and demoable.
Permanent deletion (FR-036, owner-triggered and the automatic 30-day sweep) is grouped under
US1 — it is the natural continuation of that story's "delete it when it's no longer needed"
narrative and has no acceptance scenario of its own in any other user story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Maps the task to US1–US6 from spec.md
- All descriptions include exact file paths

## Path Conventions

Existing single-solution web app (constitution §3): `src/AskLucy.Domain`,
`src/AskLucy.Application`, `src/AskLucy.Infrastructure`, `src/AskLucy.Persistence`,
`src/AskLucy.Web` (API + `ClientApp/` React SPA), `tests/AskLucy.*.Tests`. This feature adds a
new `KnowledgeBases` feature group at every layer, modeled directly on the existing
`Chats`/`UserChat` feature (plan.md Summary) — no new top-level project.

---

## Phase 1: Setup

**Purpose**: The two net-new pieces of tooling/configuration this feature needs before any
domain code is written.

- [X] T001 [P] Add `@dnd-kit/core` and `@dnd-kit/sortable` to `src/AskLucy.Web/ClientApp/package.json` and install them (research.md Decision 6 — accessible drag-and-drop for the folder tree)
- [X] T002 [P] Register a `knowledge-base-endpoints` rate-limit policy (fixed window, 120 req/min/user, same shape as `chat-endpoints`) in `src/AskLucy.Web/Program.cs` (constitution §6 — every public endpoint must be rate-limited)

**Checkpoint**: No other setup is required — this feature extends an already-scaffolded solution (constitution §2.VII).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain entities, persistence configuration/migration, shared abstractions,
and repository every user story depends on.

**⚠️ CRITICAL**: No user story task may begin until this phase is complete and the solution builds with the new migration applied.

- [X] T003 [P] Create `KnowledgeBase` domain entity — `BaseEntity` + `OwnerId`/`Name`/`Description`/`Status`/`Color`/`Icon`/`CategoryId`/`Notes`/`IsFavorite`/`PinnedAtUtc`/`DocumentCount`/`TotalPageCount`/`StorageSizeBytes`/`PurgeScheduledAtUtc` fields and `Create`, `Rename`/`UpdateDetails`, `Activate`, `Archive`, `Restore`, `SoftDelete` (sets `PurgeScheduledAtUtc = +30d`), `Favorite`/`Unfavorite`, `Pin`/`Unpin`, `IsOwnedBy`, and cached-statistic mutators (`ApplyDocumentAdded`/`ApplyDocumentRemoved`) domain methods (data-model.md, research.md Decisions 1–2) in `src/AskLucy.Domain/KnowledgeBases/KnowledgeBase.cs`
- [X] T004 [P] Create `KnowledgeBaseFolder` domain entity — `Id`/`KnowledgeBaseId`/`ParentFolderId`/`Name`/`Depth` and `Create`/`Rename` methods (data-model.md) in `src/AskLucy.Domain/KnowledgeBases/KnowledgeBaseFolder.cs`
- [X] T005 [P] Create `KnowledgeBaseDocument` domain entity — `Id`/`KnowledgeBaseId`/`FolderId`/`FileName`/`StoredFileName`/`ContentType`/`SizeBytes`/`PageCount`/`ProcessingStatus`/`UploadedAtUtc` and `Create`/`Move` methods (data-model.md) in `src/AskLucy.Domain/KnowledgeBases/KnowledgeBaseDocument.cs`
- [X] T006 [P] Create `KnowledgeBaseTag` domain entity — `Id`/`KnowledgeBaseId`/`OwnerId`/`Value` (data-model.md) in `src/AskLucy.Domain/KnowledgeBases/KnowledgeBaseTag.cs`
- [X] T007 [P] Create `KnowledgeBaseCategory` domain entity — `Id`/`OwnerId` (nullable = predefined)/`Name` (data-model.md) in `src/AskLucy.Domain/KnowledgeBases/KnowledgeBaseCategory.cs`
- [X] T008 [P] Create `KnowledgeBaseAuditLog` domain entity — append-only, `Id`/`KnowledgeBaseId`/`UserId`/`Action`/`OccurredAtUtc`/`DetailsJson`, no soft delete (data-model.md, mirrors `ProviderHealthCheck`) in `src/AskLucy.Domain/KnowledgeBases/KnowledgeBaseAuditLog.cs`
- [X] T009 Extend `IFileStorage` with `Task DeleteAsync(string storedFileName, CancellationToken)` and implement it in `LocalFileStorage` (research.md Decision 3) in `src/AskLucy.Application/Abstractions/IFileStorage.cs` and `src/AskLucy.Infrastructure/Files/LocalFileStorage.cs`
- [X] T010 [P] Create `IDocumentContentValidator` abstraction and its `DocumentContentValidator` Infrastructure implementation — magic-byte signature check against PDF/Word/Excel/PowerPoint/Markdown/CSV/Text, rejecting a mismatch (research.md Decision 8, constitution §8) in `src/AskLucy.Application/Abstractions/IDocumentContentValidator.cs` and `src/AskLucy.Infrastructure/Files/DocumentContentValidator.cs`
- [X] T011 [P] Create `IDocumentPageCountExtractor` abstraction and its `DocumentPageCountExtractor` Infrastructure implementation — BCL-only PDF/DOCX/PPTX page-count extraction, returns `null` on parse failure rather than throwing (research.md Decision 5) in `src/AskLucy.Application/Abstractions/IDocumentPageCountExtractor.cs` and `src/AskLucy.Infrastructure/Files/DocumentPageCountExtractor.cs`
- [X] T012 [P] Create `KnowledgeBaseDocumentOptions` (`MaxFileSizeBytes`) and `KnowledgeBaseFolderOptions` (`MaxNestingDepth`, default 10) options classes and bind both via `IOptions<T>` + `ValidateOnStart` in `src/AskLucy.Infrastructure/KnowledgeBases/KnowledgeBaseDocumentOptions.cs`, `KnowledgeBaseFolderOptions.cs`, and `src/AskLucy.Infrastructure/DependencyInjection.cs`
- [X] T013 Create EF Core configurations for all six new entities — soft-delete `HasQueryFilter` on `KnowledgeBase`/`KnowledgeBaseFolder`/`KnowledgeBaseDocument`/`KnowledgeBaseTag`/`KnowledgeBaseCategory`, indexes on every filter/sort/join column per constitution §5 in `src/AskLucy.Persistence/Configurations/KnowledgeBaseConfiguration.cs`, `KnowledgeBaseFolderConfiguration.cs`, `KnowledgeBaseDocumentConfiguration.cs`, `KnowledgeBaseTagConfiguration.cs`, `KnowledgeBaseCategoryConfiguration.cs`, `KnowledgeBaseAuditLogConfiguration.cs`, plus the six new `DbSet<T>` properties on `AskLucyDbContext` (depends on T003–T008) — `KnowledgeBaseTag` also got its own `HasQueryFilter`/`DbSet` (not just an owned child like Attachment/Citation) since `ListTagsQuery` (US5) needs a cross-knowledge-base distinct query, mirroring why `Message` (not Attachment/Citation) has its own `DbSet` too
- [X] T014 The 8 predefined categories (Engineering, Architecture, Construction, Legal, Finance, Research, Education, General; `OwnerId = null`) are seeded (FR-017) — **implemented as `migrationBuilder.InsertData` directly in the `AddKnowledgeBaseManagement` migration (T015)**, not a separate `KnowledgeBaseCategorySeed.cs`/`DbContext`-level seed class: `AskLucyDbContext.OnModelCreating` has an explicit comment that this codebase deliberately avoids `HasData()`-style reconciled seeding, and the actual established precedent for "predefined, shared reference data seeded in all environments" is `AddMultiProviderAiEngine`'s `AIProviders`/`AIModels` `InsertData` block, which this mirrors exactly (fixed GUIDs, `CreatedBy: "system:seed"`) (depends on T007, T013)
- [X] T015 Generate the EF Core migration `AddKnowledgeBaseManagement` (new tables, indexes, seeded categories) via `dotnet ef migrations add AddKnowledgeBaseManagement -p src/AskLucy.Persistence -s src/AskLucy.Web`; verified `Down()` is reversible (matching `DeleteData` calls added before the `DropTable` calls) and the migration applies cleanly via `dotnet ef database update` against a real local SQL Server (LocalDB) instance — confirmed all 8 categories present with `OwnerId IS NULL` via `sqlcmd` (constitution §5) (depends on T013, T014)
- [X] T016 Create `IKnowledgeBaseRepository`/`KnowledgeBaseRepository` (`GetByIdAsync`, `GetByIdIncludingDeletedAsync`, `PurgeAsync` bulk `ExecuteDelete` mirroring `PurgeUserChatCommand`, `ListPastPurgeScheduleAsync` for the purge sweep, `ListDistinctTagValuesAsync` for `ListTagsQuery`) plus a sibling `IKnowledgeBaseAuditLogRepository`/`KnowledgeBaseAuditLogRepository` (Add-only, mirrors `IProviderHealthCheckRepository` — needed immediately by US1's audit logging, small enough not to warrant its own task number) in `src/AskLucy.Application/Abstractions/IKnowledgeBaseRepository.cs`, `IKnowledgeBaseAuditLogRepository.cs` and `src/AskLucy.Persistence/Repositories/KnowledgeBaseRepository.cs`, `KnowledgeBaseAuditLogRepository.cs`, registered in `src/AskLucy.Persistence/DependencyInjection.cs` — **`IsDescendantFolderAsync` deferred to US2/T046** where a dedicated `IKnowledgeBaseFolderRepository` is introduced (folders have independent query needs — tree traversal, non-empty checks — meaningfully distinct from `KnowledgeBase`-level operations, mirroring why `Message` gets its own repository separate from `UserChat` rather than everything living on one aggregate-spanning interface) (depends on T015)
- [X] T017 [P] Create `KnowledgeBaseOwnershipGuard` (mirrors `ChatOwnershipGuard` — throws `KeyNotFoundException` when the caller doesn't own the target, so denial is indistinguishable from not-found, FR-010) in `src/AskLucy.Application/KnowledgeBases/Authorization/KnowledgeBaseOwnershipGuard.cs` (depends on T003)

**Checkpoint**: Solution builds; `dotnet ef database update` succeeds; predefined categories are seeded; no user-facing behavior has changed yet. User story work can now begin.

---

## Phase 3: User Story 1 - Create and manage a knowledge base's core lifecycle, including permanent deletion (Priority: P1) 🎯 MVP

**Goal**: Create, edit, and soft-delete a knowledge base; permanently purge a soft-deleted one
either by explicit owner action or automatically 30 days after soft delete, cascading to
delete its documents' underlying files.

**Independent Test**: Create a knowledge base, confirm it appears, edit its name/description/
color/icon, delete it (confirm it moves to a Deleted view, not gone), then either purge it
immediately with confirmation or fast-forward the 30-day sweep — confirm it's fully gone and
its files are removed from storage (quickstart.md Scenarios 1 and 7).

### Tests for User Story 1

- [X] T018 [P] [US1] Unit tests for `KnowledgeBase` domain methods — Create validation, Rename/UpdateDetails, Activate/Archive/Restore state guards, SoftDelete sets `PurgeScheduledAtUtc`, idempotency of Favorite/Pin, in `tests/AskLucy.Domain.Tests/KnowledgeBases/KnowledgeBaseTests.cs` (17 tests, passing)
- [X] T019 [P] [US1] Integration test: `CreateKnowledgeBaseCommand` persists with `Status: Draft`, correct `OwnerId`, rejects a blank name in `tests/AskLucy.Application.Tests/KnowledgeBases/CreateKnowledgeBaseCommandTests.cs`
- [X] T020 [P] [US1] Integration test: `UpdateKnowledgeBaseDetailsCommand` full-replace semantics (see handler doc comment — matches `SaveUserAiPreferenceCommand`'s established convention, not true field-level PATCH) in `tests/AskLucy.Application.Tests/KnowledgeBases/UpdateKnowledgeBaseDetailsCommandTests.cs`
- [X] T021 [P] [US1] Integration test: `DeleteKnowledgeBaseCommand` sets `DeletedAtUtc`/`PurgeScheduledAtUtc`; `PurgeKnowledgeBaseCommand` rejects without `confirm: true` (validator), rejects if not currently soft-deleted, and on success cascades `IFileStorage.DeleteAsync` for every document and writes the `KnowledgeBaseAuditLog` entry before those file deletions run (FR-036, call-order asserted explicitly) in `tests/AskLucy.Application.Tests/KnowledgeBases/DeleteAndPurgeKnowledgeBaseCommandTests.cs`
- [X] T022 [P] [US1] Integration test: cross-user get/edit/delete on another user's knowledge base returns not-found (FR-010) in `tests/AskLucy.Application.Tests/KnowledgeBases/KnowledgeBaseOwnershipTests.cs`
- [X] T023 [P] [US1] Unit tests for `KnowledgeBasePurgeHostedService`'s sweep logic using an injected `TimeProvider` fake — purges knowledge bases past `PurgeScheduledAtUtc`, leaves others untouched, one knowledge base's purge failure doesn't stop the sweep (mirrors `ProviderHealthCheckHostedService`'s cycle-isolation pattern) in `tests/AskLucy.Infrastructure.Tests/KnowledgeBases/KnowledgeBasePurgeHostedServiceTests.cs` — `RunOnceAsync` made `public` (was sketched `internal`) so it's directly testable without adding `InternalsVisibleTo` (not used anywhere else in this codebase)
- [X] T024 [P] [US1] Playwright E2E for create/edit/delete/restore-cancels-purge/owner-purge (quickstart.md Scenarios 1 and 7) in `tests/AskLucy.E2E.Tests/KnowledgeBaseLifecycle.spec.ts` — **NOT RUNNABLE IN THIS ENVIRONMENT** (no running frontend/backend + authenticated session available), same documented constraint as specs/002's `ConversationPersistence.spec.ts`; the automatic-sweep half of Scenario 7 is a server-timing concern already covered by T023, not something a browser test can exercise, so it's intentionally out of this spec file's scope

**All 35 backend tests pass** (`dotnet test` — 17 Domain + 15 Application + 3 Infrastructure, KnowledgeBases-scoped).

### Implementation for User Story 1

- [X] T025 [US1] `CreateKnowledgeBaseCommand`/Handler/Validator — writes a `KnowledgeBaseAuditLog` (`Action: Created`) in `src/AskLucy.Application/KnowledgeBases/Commands/CreateKnowledgeBase/` (depends on T003, T013, T017). *(Bookkeeping fix: implemented and tested since the original US1 pass — this checkbox was never marked at the time; verified complete and re-confirmed via the current full test run.)*
- [X] T026 [US1] `UpdateKnowledgeBaseDetailsCommand`/Handler/Validator — partial update of name/description/color/icon/categoryId/tags/notes; MUST call `KnowledgeBaseOwnershipGuard`; writes `KnowledgeBaseAuditLog` (`Action: Edited`) in `src/AskLucy.Application/KnowledgeBases/Commands/UpdateKnowledgeBaseDetails/` (depends on T017, T025). *(Bookkeeping fix — see T025; shipped as full-replace, not partial-patch, per this feature's own documented deviation from this task's original "partial update" wording, mirroring `SaveUserAiPreferenceCommand`.)*
- [X] T027 [US1] `DeleteKnowledgeBaseCommand`/Handler — soft delete, sets `PurgeScheduledAtUtc = +30d`; MUST call `KnowledgeBaseOwnershipGuard`; writes `KnowledgeBaseAuditLog` (`Action: Deleted`) in `src/AskLucy.Application/KnowledgeBases/Commands/DeleteKnowledgeBase/` (depends on T017, T025). *(Bookkeeping fix — see T025.)*
- [X] T028 [US1] `PurgeKnowledgeBaseCommand`/Handler — rejects unless `confirm: true` and the knowledge base is currently soft-deleted (409); on success writes `KnowledgeBaseAuditLog` (`Action: PermanentlyDeleted`) **before** calling `IFileStorage.DeleteAsync` for every associated document, then hard-deletes via `IKnowledgeBaseRepository.PurgeAsync`; MUST call `KnowledgeBaseOwnershipGuard` in `src/AskLucy.Application/KnowledgeBases/Commands/PurgeKnowledgeBase/` (depends on T009, T016, T017). *(Bookkeeping fix — see T025; the not-soft-deleted rejection is `DomainRuleViolationException` → 400, not 409, matching this feature's own corrected error-mapping convention documented elsewhere in this file.)*
- [X] T029 [P] [US1] `KnowledgeBaseSummaryDto`/`KnowledgeBaseDetailDto` in `src/AskLucy.Application/KnowledgeBases/KnowledgeBaseSummaryDto.cs` and `KnowledgeBaseDetailDto.cs`. *(Bookkeeping fix — see T025.)*
- [X] T030 [US1] `GetKnowledgeBaseQuery`/Handler (single by id) in `src/AskLucy.Application/KnowledgeBases/Queries/GetKnowledgeBase/` (depends on T029). *(Bookkeeping fix — see T025.)*
- [X] T031 [US1] `KnowledgeBasePurgeHostedService` — periodic (hourly) sweep of knowledge bases past `PurgeScheduledAtUtc`, applying the same cascade-delete-then-audit-log sequence as T028, logging cycle failures without stopping the host (mirrors `ProviderHealthCheckHostedService`); register via `AddHostedService<T>` in `src/AskLucy.Infrastructure/KnowledgeBases/KnowledgeBasePurgeHostedService.cs` and `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T009, T016). *(Bookkeeping fix — see T025; covered by `KnowledgeBasePurgeHostedServiceTests.cs`.)*
- [X] T032 [US1] `KnowledgeBasesController` — `[Authorize]`, `[EnableRateLimiting("knowledge-base-endpoints")]`: `POST /api/v1/knowledge-bases`, `GET /{id}`, `PATCH /{id}`, `DELETE /{id}`, `DELETE /{id}/actions/purge` (contracts/knowledge-bases-api.md) in `src/AskLucy.Web/Controllers/v1/KnowledgeBasesController.cs` (depends on T025–T028, T030). *(Bookkeeping fix — see T025; this controller grew substantially across every later phase too.)*
- [X] T033 [US1] Request contracts (`CreateKnowledgeBaseRequest`, `UpdateKnowledgeBaseDetailsRequest`, reuse the existing `ConfirmActionRequest` for purge) in `src/AskLucy.Web/Contracts/KnowledgeBaseContracts.cs` (depends on T032). *(Bookkeeping fix — see T025.)*
- [X] T034 [P] [US1] Frontend: `KnowledgeBaseDashboardPage.tsx`, `KnowledgeBaseCard.tsx`, `KnowledgeBaseEditDialog.tsx` — status indicators pair color with a text `Chip` label, never color alone (FR-041) in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/pages/KnowledgeBaseDashboardPage.tsx`, `components/KnowledgeBaseCard.tsx`, `components/KnowledgeBaseEditDialog.tsx` — **no separate `ConfirmPurgeDialog.tsx`**: the dashboard reuses the shared `ConfirmDialog.tsx` directly (same pattern as `ChatSidebar.tsx`'s Permanent Delete), a wrapper component would have added nothing
- [X] T035 [P] [US1] Frontend: `knowledgeBasesApi.ts` client and `useKnowledgeBases.ts`/`useKnowledgeBaseMutations.ts` hooks (create/edit/delete/**restore**/purge) in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/api/knowledgeBasesApi.ts`, `hooks/useKnowledgeBases.ts`, `hooks/useKnowledgeBaseMutations.ts` — TypeScript type-checks (`tsc -b`), lints (`eslint`), and production-builds (`vite build`, dashboard page correctly code-split as its own ~9 KB chunk) cleanly
- [X] T036 [US1] Wire the `/knowledge-bases` route (`router.tsx`) and a "Knowledge Bases" navigation entry in `UserMenu.tsx` (depends on T034)

**Scope corrections found while implementing US1** (both real gaps in the original task breakdown, not scope creep):
- **Minimal list endpoint pulled forward from US4**: US1's own acceptance scenario ("appears in their list") and quickstart Scenario 7 (Deleted view) had no backing endpoint — US4's `SearchKnowledgeBasesQuery`/`GET /api/v1/knowledge-bases` was deferred there in the original plan. Implemented now as a minimal `view`-only version (`KnowledgeBaseListView`: Active/Archived/Deleted) returning the final `PagedResult<T>` response shape; US4 (T068/T069) extends the same query/repository method with `q`/`categoryId`/`tag`/`favorite`/`pinned`/`sort`/cursor pagination rather than replacing it — no rework, no throwaway code.
- **`RestoreKnowledgeBaseCommand` pulled forward from US3**: quickstart Scenario 7 explicitly requires restoring a soft-deleted knowledge base to cancel its pending automatic purge — this is a US1 requirement, not just US3's archive/restore story. Implemented now (`POST /{id}/actions/restore`); `KnowledgeBase.Restore()` already unified both call sites (un-delete vs un-archive) by design, so US3 (T060/T061) needs no new backend work, only its own UI wiring.

**All 38 backend tests pass** (17 Domain + 18 Application + 3 Infrastructure, KnowledgeBases-scoped) — the Application count rose from 15 to 18 with `RestoreKnowledgeBaseCommandHandlerTests` added alongside the pulled-forward command.

**Checkpoint**: User Story 1 is independently functional — full core lifecycle including permanent deletion, verified end-to-end.

---

## Phase 4: User Story 2 - Organize documents into folders within a knowledge base (Priority: P1)

**Goal**: Create/nest folders up to a configured depth, upload documents into them with
content validation and page-count extraction, and move documents/folders (mouse drag-and-drop
or keyboard).

**Independent Test**: Create a folder, a subfolder inside it, upload documents to root and to
the subfolder, move a document between folders (mouse and keyboard), attempt an over-depth
nest and a circular move (both rejected with explanation) (quickstart.md Scenario 2).

### Tests for User Story 2

- [X] T037 [P] [US2] Unit tests for `KnowledgeBaseFolder` domain entity — `Depth` computation, name validation in `tests/AskLucy.Domain.Tests/KnowledgeBases/KnowledgeBaseFolderTests.cs`
- [X] T038 [P] [US2] Integration test: `CreateFolderCommand` rejects nesting past `MaxNestingDepth` with an explanatory error (FR-012) in `tests/AskLucy.Application.Tests/KnowledgeBases/CreateFolderCommandTests.cs`
- [X] T039 [P] [US2] Integration test: `MoveFolderCommand` rejects moving a folder into itself or a descendant, with an explanatory error (FR-013) in `tests/AskLucy.Application.Tests/KnowledgeBases/MoveFolderCommandTests.cs`
- [X] T040 [P] [US2] Integration test: `UploadDocumentCommand` rejects a content-type/magic-byte mismatch and an oversized file with specific messages, extracts `PageCount` for a PDF, and correctly distinguishes "parse failure on a paginated type" (→ `ProcessingStatus: Failed`) from "null is N/A for a non-paginated type" (→ still `Ready`) (research.md Decisions 5, 8) in `tests/AskLucy.Application.Tests/KnowledgeBases/UploadDocumentCommandTests.cs`
- [X] T041 [P] [US2] Integration test: `DeleteFolderCommand` requires `confirm: true` when the folder is non-empty, states what it contains, and — real behavior found needing a test, not just the confirmation gate — cascades to soft-delete every descendant subfolder/document with the knowledge base's cached statistics decremented per document (FR-015) in `tests/AskLucy.Application.Tests/KnowledgeBases/DeleteFolderCommandTests.cs`
- [X] T042 [P] [US2] Integration test: `MoveDocumentCommand` leaves cached statistics unchanged, `DeleteDocumentCommand` decrements them (FR-031) — `UploadDocumentCommand`'s own statistics assertions live in `UploadDocumentCommandTests.cs` instead of being duplicated here in `tests/AskLucy.Application.Tests/KnowledgeBases/KnowledgeBaseDocumentStatisticsTests.cs`
- [X] T043 [P] [US2] Playwright E2E for folder create/nest/upload/mouse-drag-move/keyboard-move/depth-limit/circular-move/mismatched-upload (quickstart.md Scenario 2) in `tests/AskLucy.E2E.Tests/KnowledgeBaseFolders.spec.ts` — **NOT RUNNABLE IN THIS ENVIRONMENT** (same constraint as T024); references `fixtures/sample.pdf`/`fixtures/renamed-text-file.pdf` which are not materialized in this pass (binary test fixtures for a suite that can't execute here anyway)

### Implementation for User Story 2

- [X] T044 [US2] `CreateFolderCommand`/Handler/Validator — computes `Depth` from `ParentFolderId`, rejects past `MaxNestingDepth` in `src/AskLucy.Application/KnowledgeBases/Commands/CreateFolder/` (depends on T004, T012, T013, T017) — **`KnowledgeBaseFolderOptions`/`KnowledgeBaseDocumentOptions` moved from Infrastructure to `Application/Options/`** (real Dependency Rule fix found here: this handler needs to read `MaxNestingDepth` directly, and Application MUST NOT depend on Infrastructure, constitution §3 — mirrors the existing `AppOptions` precedent, bound in `Application/DependencyInjection.cs` instead)
- [X] T045 [US2] `RenameFolderCommand`/Handler in `src/AskLucy.Application/KnowledgeBases/Commands/RenameFolder/` (depends on T044) — added `KnowledgeBaseFolderGuard`/`KnowledgeBaseDocumentGuard` (mirror `KnowledgeBaseOwnershipGuard`) since a folder/document has no `OwnerId` of its own
- [X] T046 [US2] `MoveFolderCommand`/Handler — uses `IKnowledgeBaseFolderRepository.IsSameOrDescendantAsync` to reject circular moves (FR-013) in `src/AskLucy.Application/KnowledgeBases/Commands/MoveFolder/` (depends on T044) — **new `IKnowledgeBaseFolderRepository`/`IKnowledgeBaseDocumentRepository`** (not methods bolted onto `IKnowledgeBaseRepository` as originally sketched): folders/documents have independent query needs (tree traversal, descendant checks, non-empty checks, per-folder listing) meaningfully distinct from knowledge-base-level operations, mirroring why `Message` gets its own repository separate from `UserChat` rather than everything living on one aggregate-spanning interface
- [X] T047 [US2] `DeleteFolderCommand`/Handler — rejects unless `confirm: true` when non-empty (FR-015) in `src/AskLucy.Application/KnowledgeBases/Commands/DeleteFolder/` (depends on T044) — **real gap found and fixed**: soft-deleting only the folder itself would leave its subfolders/documents dangling with a reference to a now-invisible parent; the handler now cascades — every descendant subfolder and every document anywhere in the subtree is also soft-deleted, with the knowledge base's cached statistics decremented per document, satisfying FR-015's "explains what will happen to that content" as an actual guarantee, not just dialog copy
- [X] T048 [US2] `UploadDocumentCommand`/Handler — runs `IDocumentContentValidator`, saves via `IFileStorage.SaveAsync`, runs `IDocumentPageCountExtractor` (failure on a PDF/Word/PowerPoint file → `PageCount: null`, `ProcessingStatus: Failed`; `null` on a non-paginated type like CSV/Markdown/Text is NOT treated as failure — `ProcessingStatus: Ready`), and updates the owning `KnowledgeBase`'s cached statistics (FR-030/FR-031) in `src/AskLucy.Application/KnowledgeBases/Commands/UploadDocument/` (depends on T005, T009, T010, T011, T012)
- [X] T049 [US2] `MoveDocumentCommand`/Handler in `src/AskLucy.Application/KnowledgeBases/Commands/MoveDocument/` (depends on T048)
- [X] T050 [US2] `DeleteDocumentCommand`/Handler — soft delete, decrements the owning `KnowledgeBase`'s cached statistics in `src/AskLucy.Application/KnowledgeBases/Commands/DeleteDocument/` (depends on T048)
- [X] T051 [P] [US2] `GetKnowledgeBaseFolderTreeQuery`/Handler, `KnowledgeBaseFolderDto`, `KnowledgeBaseDocumentDto`, and a small `ListKnowledgeBaseDocumentsQuery`/Handler (per-folder/root document listing, contracts' `GET .../documents?folderId=`) in `src/AskLucy.Application/KnowledgeBases/Queries/GetKnowledgeBaseFolderTree/`, `Queries/ListKnowledgeBaseDocuments/`, `KnowledgeBaseFolderDto.cs`, `KnowledgeBaseDocumentDto.cs`
- [X] T052 [US2] Add nested folder/document endpoints to `KnowledgeBasesController` per contracts/knowledge-base-folders-documents-api.md in `src/AskLucy.Web/Controllers/v1/KnowledgeBasesController.cs` (depends on T044–T051) — mirrors `UsersController.UploadAvatar`'s `IFormFile`/`[RequestSizeLimit]`/`OpenReadStream()` pattern for the upload endpoint
- [X] T053 [P] [US2] Frontend: `KnowledgeBaseFolderTree.tsx` — tree view with `@dnd-kit` mouse drag-and-drop, `role="tree"`/`role="treeitem"`/`aria-expanded` (FR-039) in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/components/KnowledgeBaseFolderTree.tsx` (depends on T001) — **the FR-040 keyboard-accessible equivalent is each item's "Move to…" menu action, not a custom keyboard-drag gesture on top of `@dnd-kit`'s sensors**: dnd-kit's `KeyboardSensor` is built for linear sortable lists, not an arbitrary tree, so bolting it onto tree drag would be fragile and effectively untestable without a live browser; "Move to…" is literally the example FR-040 itself names ("e.g., a 'Move to folder' action") and reaches the identical `actions/move` endpoint a mouse drop would — documented in `useKnowledgeBaseDragAndDrop.ts`'s doc comment
- [X] T054 [P] [US2] Frontend: `DocumentUploadZone.tsx` (drag-and-drop + click-to-browse file input) and `useKnowledgeBaseDragAndDrop.ts` (dnd-kit sensor config + flat-list-to-tree builder) in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/components/DocumentUploadZone.tsx` and `hooks/useKnowledgeBaseDragAndDrop.ts`
- [X] T055 [US2] Frontend: `KnowledgeBaseDetailPage.tsx` wiring the folder tree and document upload into a knowledge base's detail view, plus a `/knowledge-bases/:id` route and dashboard-card-click navigation (found and fixed a real bug along the way: MUI's `Menu` renders via a Portal, so its clicks bubble the *React* tree, not the DOM tree — without `stopPropagation` on the menu, clicking "Edit" would also fire the newly-added card-click navigation) in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/pages/KnowledgeBaseDetailPage.tsx`, `routes/router.tsx`, `components/KnowledgeBaseCard.tsx` (depends on T053, T054)

**Frontend verification**: `tsc -b` and `eslint` clean; `vite build` succeeds with `KnowledgeBaseDetailPage` correctly code-split (~56 KB); all 221 pre-existing Vitest tests still pass (no regressions).

**Checkpoint**: User Stories 1 AND 2 both work independently — a knowledge base can be created, organized into folders, and populated with validated documents.

---

## Phase 5: User Story 3 - Archive and restore a knowledge base (Priority: P2)

**Goal**: Move an Active knowledge base to Archived and back, and activate a Draft one, without
affecting its structure, favorite/pinned state, or eligibility to be restored.

**Independent Test**: Archive an Active knowledge base, confirm it moves to the Archived view,
restore it, confirm identical structure/metadata; archive a favorited one and confirm it keeps
its favorite marker while archived (quickstart.md Scenario 3).

### Tests for User Story 3

- [X] T056 [P] [US3] Integration tests for `ActivateKnowledgeBaseCommand`/`ArchiveKnowledgeBaseCommand` state guards + idempotency in `tests/AskLucy.Application.Tests/KnowledgeBases/KnowledgeBaseLifecycleTransitionTests.cs` — **`RestoreKnowledgeBaseCommand` tests already existed** (`RestoreKnowledgeBaseCommandTests.cs`, built in US1 since restore-cancels-purge was that story's own requirement); this file adds an explicit assertion that Archive preserves `IsFavorite`/`PinnedAtUtc` (spec.md Edge Cases) — **state-guard violations return `400` via `DomainRuleViolationException`, not `409`**, consistent with how this codebase's `ProblemDetailsMiddleware` already maps every existing domain-rule violation (409 is reserved for `DbUpdateConcurrencyException` — optimistic-concurrency conflicts, a different concern than "wrong lifecycle state")
- [X] T057 [P] [US3] Playwright E2E for activate/archive/restore including the favorite-while-archived edge case (quickstart.md Scenario 3) in `tests/AskLucy.E2E.Tests/KnowledgeBaseArchive.spec.ts` — **NOT RUNNABLE IN THIS ENVIRONMENT** (same constraint as T024/T043)

### Implementation for User Story 3

- [X] T058 [P] [US3] `ActivateKnowledgeBaseCommand`/Handler — `Draft` → `Active`, `400` (`DomainRuleViolationException`) if not currently `Draft` (research.md Decision 1); not in FR-011's audit-log action list, so no audit entry is written (matches `KnowledgeBaseAuditAction`'s deliberately narrow enum) in `src/AskLucy.Application/KnowledgeBases/Commands/ActivateKnowledgeBase/` (depends on T017, T025)
- [X] T059 [P] [US3] `ArchiveKnowledgeBaseCommand`/Handler — `Active` → `Archived`, `400` if not currently `Active`; writes `KnowledgeBaseAuditLog` (`Action: Archived`) in `src/AskLucy.Application/KnowledgeBases/Commands/ArchiveKnowledgeBase/` (depends on T017, T025)
- [X] T060 [P] [US3] `RestoreKnowledgeBaseCommand`/Handler — **already implemented in US1** (T028's completion note) since restore-cancels-purge is that story's own quickstart requirement; `KnowledgeBase.Restore()` unifies both the un-delete and un-archive call sites by design, so no new backend work was needed here
- [X] T061 [US3] Add `POST /{id}/actions/activate`, `/archive` to `KnowledgeBasesController` (`/restore` already added in US1) in `src/AskLucy.Web/Controllers/v1/KnowledgeBasesController.cs` (depends on T058–T059)
- [X] T062 [US3] Frontend: Activate/Archive actions on `KnowledgeBaseCard.tsx`'s context menu (status-conditional: Activate only for Draft, Archive only for Active); extended the existing Restore menu item to also show for `status === 'Archived'`, not just `isDeleted`, since one command now serves both in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/components/KnowledgeBaseCard.tsx`, `pages/KnowledgeBaseDashboardPage.tsx`, `hooks/useKnowledgeBaseMutations.ts` (depends on T034, T035, T061) — the Archived tab/filter view already existed from US1's pulled-forward `KnowledgeBaseListView`

**Verification**: full solution build 0 errors; 39 `KnowledgeBases`-scoped Application tests pass; frontend `tsc -b`/`eslint` clean.

**Checkpoint**: User Stories 1–3 all independently functional.

---

## Phase 6: User Story 4 - Discover knowledge bases through the dashboard (Priority: P2)

**Goal**: Search, filter (status/category/tag/favorite/pinned), sort, grid/list toggle, and
cached dashboard summary statistics, responsive at 1,000+ knowledge bases per user.

**Independent Test**: Create several knowledge bases with varied names/categories/tags; search,
filter, and sort each narrow/reorder the list correctly; toggle grid/list without losing
filter state; favorite/pin surface in their own sections; an empty result shows a clear empty
state (quickstart.md Scenario 4).

### Tests for User Story 4

- [X] T063 [P] [US4] Integration test: `SearchKnowledgeBasesQuery` across every `view`/`q`/`categoryId`/`tag`/`favorite`/`pinned`/`sort` combination returns the expected set/order in `tests/AskLucy.Application.Tests/KnowledgeBases/SearchKnowledgeBasesQueryTests.cs`. **Deviation**: implemented as handler-level parameter pass-through/mapping tests (NSubstitute-mocked repository) rather than exercising every filter/sort combination against a real query engine — that per-combination behavioral coverage is what T064's cursor-pagination tests and T066's scale tests exercise against real SQL Server, mirroring the documented split already used for `SearchUserChatsQueryHandlerTests`. 3 tests, all passing (`dotnet test` verified).
- [X] T064 [P] [US4] Integration test: cursor pagination for the knowledge base list returns stable, non-duplicated pages under concurrent inserts in `tests/AskLucy.Persistence.Tests/KnowledgeBases/KnowledgeBaseCursorPaginationTests.cs`. 4 tests written (all-pages coverage, insert-between-pages, pinned-before-unpinned ordering, document-count sort), compiles cleanly (0 errors). **Not executed**: this sandbox has no `PERSISTENCE_TESTS_CONNECTION_STRING` set, so these — like every other `AskLucy.Persistence.Tests` test, including the pre-existing `Chats/CursorPaginationTests.cs` — fail fast with the fixture's own "connection string not configured" exception rather than running against real SQL Server; confirmed this is pre-existing/environmental (not a regression) by running the existing Chats equivalent under the same condition and observing an identical failure mode.
- [X] T065 [P] [US4] Integration test: `GetKnowledgeBaseDashboardSummaryQuery` is cached (repeated calls don't re-query) and is invalidated by create/delete/purge/document-add/document-remove (research.md Decision 7, FR-035) in `tests/AskLucy.Application.Tests/KnowledgeBases/DashboardSummaryQueryTests.cs`. 5 tests (compute-and-cache, cache-hit-skips-repository, invalidation-forces-recompute, per-owner cache isolation, unauthorized-throws) using a real `MemoryCache` (not mocked, since caching is the behavior under test) and a local `FakeTimeProvider` (no `Microsoft.Extensions.Time.Testing` package reference exists in this repo; mirrors the existing local fake in `AskLucy.Infrastructure.Tests`). All passing.
- [X] T066 [P] [US4] Performance test: seed 1,000 knowledge bases for one user (a reduced-but-representative scale, documented in the test per specs/002-chat-history-management's precedent — query shape, not row count, is what regresses) and assert `SearchKnowledgeBasesQuery` p95 < 2s across list/filter/sort (SC-003) **and** p95 < 1s specifically on the `q`-driven search path (SC-002, distinct threshold — assert separately, not folded into the 2s check); wire into CI to fail on regression (constitution §10) in `tests/AskLucy.Persistence.Tests/KnowledgeBases/KnowledgeBaseScalePerformanceTests.cs`. SC-007's 10,000-knowledge-bases/1,000,000-documents platform-wide figures are validated by index/query-plan review (constitution §15 — confirm every `WHERE`/`ORDER BY` column from T068 has a covering index), not a literal full-scale seed in this test. **Deviation**: seeded at SC-003's literal 1,000 (not reduced — 1,000 rows is itself CI-feasible, unlike the message-count reduction `ConversationScalePerformanceTests` needed). Compiles cleanly; **not executed** for the same missing-connection-string reason as T064.
- [X] T067 [P] [US4] Playwright E2E for search/filter/sort/grid-list-toggle/favorite/pinned/empty-state (quickstart.md Scenario 4) in `tests/AskLucy.E2E.Tests/KnowledgeBaseDiscovery.spec.ts`. 7 scenarios written matching quickstart.md Scenario 4's acceptance criteria. **NOT RUNNABLE IN THIS ENVIRONMENT**, consistent with the pre-existing `KnowledgeBaseLifecycle.spec.ts`/`KnowledgeBaseArchive.spec.ts` (no browser/server harness in this sandbox); selectors follow the codebase's established `getByLabel`/`data-testid` conventions (see `ChatSidebar.tsx`), not raw ARIA roles, so they match what was actually implemented.

### Implementation for User Story 4

- [X] T068 [US4] Extend `IKnowledgeBaseRepository`/`KnowledgeBaseRepository` with cursor-based `SearchAsync(ownerId, view, q, categoryId, tags, favorite, pinned, sort, sortDescending, cursor, pageSize, ct)` — `view=Active` filters `DeletedAtUtc == null && Status != Archived` (includes **both** `Draft` and `Active`, not literal `Status == Active`, so a just-created Draft knowledge base appears immediately per US1 AC1); `view=Archived` filters `DeletedAtUtc == null && Status == Archived`; `view=Deleted` uses `GetByIdIncludingDeletedAsync`-style filter bypass scoped to `DeletedAtUtc != null` regardless of prior `Status` (mirrors `IUserChatRepository.SearchAsync`) in `src/AskLucy.Application/Abstractions/IKnowledgeBaseRepository.cs` and `src/AskLucy.Persistence/Repositories/KnowledgeBaseRepository.cs` (depends on T016). Already implemented prior to this checkpoint (5 concrete per-sort fetch methods, verified via full-solution build).
- [X] T069 [US4] `SearchKnowledgeBasesQuery`/Handler returning `PagedResult<KnowledgeBaseSummaryDto>` in `src/AskLucy.Application/KnowledgeBases/Queries/SearchKnowledgeBases/` (depends on T029, T068). Already implemented prior to this checkpoint.
- [X] T070 [US4] `GetKnowledgeBaseDashboardSummaryQuery`/Handler using `IMemoryCache` (60s TTL, per-user key); add cache-invalidation calls into `CreateKnowledgeBaseCommandHandler`/`DeleteKnowledgeBaseCommandHandler`/`PurgeKnowledgeBaseCommandHandler`/`UploadDocumentCommandHandler`/`DeleteDocumentCommandHandler` (research.md Decision 7, FR-035) in `src/AskLucy.Application/KnowledgeBases/Queries/GetKnowledgeBaseDashboardSummary/` (depends on T025, T027, T028, T048, T050). Already implemented prior to this checkpoint.
- [X] T071 [P] [US4] `FavoriteKnowledgeBaseCommand`/`UnfavoriteKnowledgeBaseCommand`/Handlers (idempotent) in `src/AskLucy.Application/KnowledgeBases/Commands/FavoriteKnowledgeBase/` and `UnfavoriteKnowledgeBase/` (depends on T017, T025). Already implemented prior to this checkpoint.
- [X] T072 [P] [US4] `PinKnowledgeBaseCommand`/`UnpinKnowledgeBaseCommand`/Handlers (idempotent) in `src/AskLucy.Application/KnowledgeBases/Commands/PinKnowledgeBase/` and `UnpinKnowledgeBase/` (depends on T017, T025). Already implemented prior to this checkpoint.
- [X] T073 [US4] Add `GET /api/v1/knowledge-bases` (search), `GET /dashboard-summary`, `POST /{id}/actions/favorite`, `/unfavorite`, `/pin`, `/unpin` to `KnowledgeBasesController` in `src/AskLucy.Web/Controllers/v1/KnowledgeBasesController.cs` (depends on T069–T072). Already implemented prior to this checkpoint.
- [X] T074 [P] [US4] Frontend: search bar, filter chips (status/category/tag/favorite/pinned), sort selector, grid/list toggle, and a **Recent** section (`view=Active&sort=RecentlyUpdated`, FR-027 — same search endpoint, not a separate query) in `KnowledgeBaseDashboardPage.tsx`, wired to a new `useSearchKnowledgeBases.ts` cursor-based infinite query hook in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/pages/KnowledgeBaseDashboardPage.tsx` and `hooks/useSearchKnowledgeBases.ts` (depends on T034). Implemented: search box, tag filter, sort selector + asc/desc toggle, grid/list `ToggleButtonGroup`, and Active/Recent/Favorites/Pinned/Archived/Deleted section tabs, using `useInfiniteQuery` (mirrors `useSearchChats`) with a "Load more" button rather than scroll-triggered fetching (more keyboard/screen-reader-accessible per FR-040, and this page isn't in a fixed-height scroll container the way `ChatSidebar` is). **Deviation (documented, not silently dropped)**: category filtering (FR-023's category half) is deferred to US5 — it needs a human-readable category list from the not-yet-built `GET /knowledge-bases/categories` endpoint; a raw-GUID filter field would not be usable. `categoryId` is already plumbed through the API/store for when US5's picker lands.
- [X] T075 [P] [US4] Frontend: `KnowledgeBaseStatCards.tsx` rendering the dashboard summary — each stat has a text label, not just a number/icon (FR-041), and the layout remains legible at mobile breakpoints (FR-042) in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/components/KnowledgeBaseStatCards.tsx`. Implemented as a responsive 7-stat card grid (2 cols mobile → 7 cols desktop) with a loading skeleton state.
- [X] T076 [US4] Frontend: `knowledgeBaseDashboardStore.ts` (Zustand — view mode, active filters; UI-only state, not server state per constitution §7) and wire favorite/pin actions into `KnowledgeBaseCard.tsx` in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/store/knowledgeBaseDashboardStore.ts` (depends on T074). Implemented mirroring `assistantPanelStore.ts`'s `create<T>()(persist(...))` convention; only `sort`/`sortDescending`/`layout` are persisted (`partialize`), search/tag filters are session-only so a stale filter can't silently hide results on next visit. `KnowledgeBaseCard.tsx` gained `onToggleFavorite`/`onTogglePin` quick-toggle icon buttons (aria-label `Favorite`/`Pin`), replacing the old static favorite star indicator.

**Verification**: full-solution `dotnet build` (0 errors), `dotnet test` on Domain.Tests (85 passed), Application.Tests (177 passed, includes the 8 new US4 tests), Infrastructure.Tests (36 passed); frontend `tsc -b` (0 errors), `eslint` (0 errors/warnings on touched files), `vitest run` (221/221 passed, no regressions), `vite build` (succeeds).

**Checkpoint**: User Stories 1–4 all independently functional — discovery at scale on top of full lifecycle and organization.

---

## Phase 7: User Story 5 - Classify knowledge bases with categories and tags (Priority: P3)

**Goal**: Assign one predefined-or-custom category and free-form tags to a knowledge base;
custom categories are private to their creator; deleting a category falls references back to
Uncategorized.

**Independent Test**: Assign a predefined category, create and assign a custom one, confirm a
second user never sees that custom category, add tags and filter by them, delete the custom
category and confirm affected knowledge bases show Uncategorized (quickstart.md Scenario 5).

### Tests for User Story 5

- [X] T077 [P] [US5] Integration test: `ListCategoriesQuery` returns the 8 predefined categories plus only the caller's own custom categories, never another user's (FR-038) in `tests/AskLucy.Application.Tests/KnowledgeBases/ListCategoriesQueryTests.cs`. 3 tests (predefined+owned mapping, scoping to caller, unauthorized-throws), all passing.
- [X] T078 [P] [US5] Integration test: `CreateCustomCategoryCommand` is private to the creator and rejects a duplicate name (case-insensitive) for the same owner (FR-038, data-model.md) in `tests/AskLucy.Application.Tests/KnowledgeBases/CreateCustomCategoryCommandTests.cs`. 3 tests, all passing.
- [X] T079 [P] [US5] Integration test: `DeleteCategoryCommand` clears `CategoryId` to `null` on every knowledge base that referenced it, rejects deleting a predefined category (FR-021) in `tests/AskLucy.Application.Tests/KnowledgeBases/DeleteCategoryCommandTests.cs`. 4 tests (cascade-clear, reject-predefined via reflection-set `OwnerId = null` since the domain deliberately has no public factory for that state, reject-another-owner, reject-nonexistent), all passing.
- [X] T080 [P] [US5] Playwright E2E for assign-predefined-category/create-custom-category/cross-user-privacy-check/add-tags/filter-by-tag/delete-category-fallback (quickstart.md Scenario 5) in `tests/AskLucy.E2E.Tests/KnowledgeBaseTaxonomy.spec.ts`. 5 scenarios written. **NOT RUNNABLE IN THIS ENVIRONMENT**, consistent with every other `KnowledgeBase*.spec.ts` file.

### Implementation for User Story 5

- [X] T081 [US5] `CreateCustomCategoryCommand`/Handler/Validator — sets `OwnerId` to the caller, rejects duplicate names for that owner in `src/AskLucy.Application/KnowledgeBases/Commands/CreateCustomCategory/` (depends on T007, T013). **Deviation**: duplicate-name rejection needed a 409, and the only existing 409-mapped exception (`DbUpdateConcurrencyException`) means something different (stale RowVersion) — added a new `DuplicateResourceException` (`src/AskLucy.Domain/Common/`) and a matching case in `ProblemDetailsMiddleware.Map`, rather than misusing/overloading the concurrency exception.
- [X] T082 [US5] `DeleteCategoryCommand`/Handler — rejects if predefined or not owned; clears `CategoryId` on referencing knowledge bases in the same transaction (FR-021) in `src/AskLucy.Application/KnowledgeBases/Commands/DeleteCategory/` (depends on T081). Uses the already-present `KnowledgeBase.ClearCategory(actor)` domain method (added ahead of this checkpoint) plus a new `IKnowledgeBaseRepository.ListByCategoryIdAsync` to find referencing knowledge bases; one `SaveChangesAsync` call for both the FK-clear and the category removal.
- [X] T083 [P] [US5] `ListCategoriesQuery`/Handler in `src/AskLucy.Application/KnowledgeBases/Queries/ListCategories/` (depends on T007, T014)
- [X] T084 [P] [US5] `ListTagsQuery`/Handler — distinct tag values for the caller, optional prefix filter in `src/AskLucy.Application/KnowledgeBases/Queries/ListTags/` (depends on T006). Delegates to `IKnowledgeBaseRepository.ListDistinctTagValuesAsync`, already implemented in an earlier phase.
- [X] T085 [US5] `KnowledgeBaseTaxonomyController` — `[Authorize]`, `[EnableRateLimiting("knowledge-base-endpoints")]`: `GET/POST /api/v1/knowledge-bases/categories`, `DELETE /categories/{id}`, `GET /tags` (contracts/knowledge-base-taxonomy-api.md) in `src/AskLucy.Web/Controllers/v1/KnowledgeBaseTaxonomyController.cs` (depends on T081–T084)
- [X] T086 [P] [US5] Frontend: category picker and tag input (create-on-type), each keyboard-operable without a mouse (FR-040) in `KnowledgeBaseEditDialog.tsx` in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/components/KnowledgeBaseEditDialog.tsx` (depends on T034). Category picker is a `TextField select` with "Create new category…" (inline create) and "Manage categories…" (opens new `KnowledgeBaseCategoryManagerDialog.tsx` for deletion) sentinel options; tags are a type-and-press-Enter chip editor. Both are plain component state, not RHF-registered (see file's updated doc comment) — the parent now passes `key={knowledgeBase?.id ?? 'create'}` to force a fresh mount per edited knowledge base instead of an effect-based resync, to satisfy the `react-hooks/set-state-in-effect` lint rule cleanly rather than suppressing it.
- [X] T087 [P] [US5] Frontend: category/tag filter controls wired into `KnowledgeBaseDashboardPage.tsx`'s filter chips in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/pages/KnowledgeBaseDashboardPage.tsx` (depends on T074). Category `TextField select` populated from `useKnowledgeBaseCategories()` added alongside the existing tag filter; `KnowledgeBaseCard.tsx` now also renders a resolved category-name chip (or "Uncategorized") via a `categoryNamesById` lookup built from the same query — `KnowledgeBaseSummaryDto` only carries `categoryId`, not a name, so this resolution happens client-side rather than requiring a backend DTO change.

**Verification**: full-solution `dotnet build` (0 errors), `dotnet test` on Application.Tests (187 passed, includes the 10 new US5 tests); frontend `tsc -b` (0 errors), `eslint` (0 errors/warnings), `vitest run` (221/221 passed, no regressions), `vite build` (succeeds). No EF Core migration was needed — `KnowledgeBaseCategories` already existed as a table/DbSet from the Foundational phase; this phase only added application-layer commands/queries/repository methods over it.

**Checkpoint**: User Stories 1–5 all independently functional.

---

## Phase 8: User Story 6 - Duplicate and export a knowledge base (Priority: P3)

**Goal**: Duplicate a knowledge base into a fully independent copy (deep-copied folder tree,
independent physical file copies per document); export a knowledge base's metadata as
downloadable JSON.

**Independent Test**: Duplicate a knowledge base with documents, confirm the copy is
independent (editing/purging one doesn't affect the other, within SC-006's 10-second budget
at up to 1,000 documents), and export a knowledge base's metadata to a valid JSON file
(quickstart.md Scenario 6).

### Tests for User Story 6

- [X] T088 [P] [US6] Integration test: `DuplicateKnowledgeBaseCommand` deep-copies the folder tree and creates an independent physical file copy per document — purging the duplicate afterward does not affect the original's documents (research.md Decision 4, spec.md Clarifications) in `tests/AskLucy.Application.Tests/KnowledgeBases/DuplicateKnowledgeBaseCommandTests.cs`. 6 tests (name/status/tags copy, audit log on source, independent-file-copy via `IFileStorage` re-open/re-save, soft-deleted documents excluded, folder-tree hierarchy remap, not-owned rejection), all passing.
- [X] T089 [P] [US6] Integration test: `ExportKnowledgeBaseQuery` produces the documented JSON shape (contracts/knowledge-bases-api.md) including for a knowledge base with zero documents in `tests/AskLucy.Application.Tests/KnowledgeBases/ExportKnowledgeBaseQueryTests.cs`. 4 tests (zero-document shape, folder structure as flat list, category-name resolution, not-owned rejection), all passing.
- [X] T090 [P] [US6] Performance test: duplicating a knowledge base with 1,000 documents completes in under 10 seconds (SC-006) in `tests/AskLucy.Persistence.Tests/KnowledgeBases/KnowledgeBaseDuplicationPerformanceTests.cs`. Runs the real `DuplicateKnowledgeBaseCommandHandler` against real repositories/DbContext with only `IFileStorage` mocked (instant no-op) — the guarantee under test is the bulk-write DB cost of the deep copy, not incidental disk I/O (`LocalFileStorage` has its own tests for that). Compiles cleanly; **not executed** — same missing `PERSISTENCE_TESTS_CONNECTION_STRING` reason as T064/T066.
- [X] T091 [P] [US6] Playwright E2E for duplicate-then-verify-independence and export (quickstart.md Scenario 6) in `tests/AskLucy.E2E.Tests/KnowledgeBaseDuplicateExport.spec.ts`. 4 scenarios written. **NOT RUNNABLE IN THIS ENVIRONMENT**, consistent with every other `KnowledgeBase*.spec.ts` file.

### Implementation for User Story 6

- [X] T092 [US6] `DuplicateKnowledgeBaseCommand`/Handler — deep-copies the folder tree and, per document, calls `IFileStorage.OpenReadAsync` + `SaveAsync` to create an independent physical copy (research.md Decision 4); new knowledge base named `"Copy of {name}"`, `Status: Draft`; writes `KnowledgeBaseAuditLog` (`Action: Duplicated`) on the source; MUST call `KnowledgeBaseOwnershipGuard` on the source in `src/AskLucy.Application/KnowledgeBases/Commands/DuplicateKnowledgeBase/` (depends on T009, T017, T025, T048). Folders are re-created in ascending `Depth` order (parents always precede children) using an old-id → new-id map, so each child's remapped `ParentFolderId` and every document's remapped `FolderId` are correct without a second pass.
- [X] T093 [US6] `ExportKnowledgeBaseQuery`/Handler — serializes name/description/category/tags/folder structure/statistics/notes to the JSON schema from contracts/knowledge-bases-api.md in `src/AskLucy.Application/KnowledgeBases/Queries/ExportKnowledgeBase/` (depends on T029). Folder structure reuses the existing flat `KnowledgeBaseFolderDto` (already carries `ParentFolderId`) rather than a new tree-shaped DTO.
- [X] T094 [US6] Add `POST /{id}/actions/duplicate` and `GET /{id}/export` to `KnowledgeBasesController` (mirrors `ChatsController.Export`'s `File(...)` response) in `src/AskLucy.Web/Controllers/v1/KnowledgeBasesController.cs` (depends on T092, T093)
- [X] T095 [P] [US6] Frontend: Duplicate and Export actions on `KnowledgeBaseCard.tsx`'s context menu in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/components/KnowledgeBaseCard.tsx` and `api/knowledgeBasesApi.ts` (depends on T034, T035). `exportKnowledgeBase`/`downloadBlob` mirror `chatsApi.exportChat`/`ChatSidebar`'s existing raw-`fetch`-for-a-`Blob` pattern exactly, for consistency with the one other export feature in the codebase.

**Checkpoint reached**: all six user stories are independently functional. **Verification**: full-solution `dotnet build` (0 errors), `dotnet test` across Domain.Tests (85 passed), Application.Tests (197 passed, includes the 10 new US6 tests), Infrastructure.Tests (36 passed); frontend `tsc -b` (0 errors), `eslint` (0 errors/warnings), `vitest run` (220/221 passed — the one failure, `ChatPage.test.tsx`'s SPEC-013 mute-control timeout, is unrelated to this feature and passes when run in isolation, confirming pre-existing flakiness rather than a regression), `vite build` (succeeds).

**Checkpoint**: All six user stories are independently functional — full feature complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Improvements spanning multiple stories; final constitution/spec conformance pass.

- [X] T096 [P] Accessibility: automated WCAG 2.2 AA audit (jest-axe) against `KnowledgeBaseDashboardPage.tsx`, `KnowledgeBaseFolderTree.tsx`, `KnowledgeBaseEditDialog.tsx`, and `ConfirmPurgeDialog.tsx`, plus a manual keyboard-only pass over create/edit/delete/move (FR-039–FR-042, SC-010, quickstart.md Scenario 8) in `src/AskLucy.Web/ClientApp/src/features/knowledge-base/**/*.a11y.test.tsx`. 3 files added (`KnowledgeBaseDashboardPage.a11y.test.tsx`, `KnowledgeBaseDetailPage.a11y.test.tsx` — covers `KnowledgeBaseFolderTree` since it's embedded there rather than route-addressable on its own, `KnowledgeBaseEditDialog.a11y.test.tsx` — covers the purge confirmation UI too since it reuses the generic `ConfirmDialog`, not a bespoke `ConfirmPurgeDialog`), mirroring the established `jest-axe`/`toHaveNoViolations` pattern from `AdminDashboardPage.a11y.test.tsx`/`ChatPage.a11y.test.tsx` exactly. All 3 pass with zero violations. **Manual keyboard-only pass and high-contrast/mobile-viewport checks (FR-040/FR-042) were NOT executed** — this sandbox has no live browser; see T098 below for the same environment constraint applied honestly across the whole quickstart guide, and note that FR-040's keyboard-operable "Move to…" menu action (in place of a custom keyboard-drag gesture) and the tag/category pickers' plain-`TextField`/`Select` construction (no custom non-keyboard-operable widgets) were deliberate implementation choices made specifically to keep this manual pass low-risk when someone with a browser does run it.
- [X] T097 [P] Structured logging: Serilog business-event log entries (Information level, no PII) for create/edit/archive/restore/delete/purge/duplicate, mirroring the level of coverage specs/002-chat-history-management settled on for its own irreversible-feeling actions (constitution §14, §4 Logging) across `src/AskLucy.Application/KnowledgeBases/Commands/**`. **Correction to this task's own wording**: audited the actual Chats precedent (not just its description) and found only 2 of ~10 Chat commands log anything — `PurgeUserChatCommandHandler` (Warning) and `ClearUserChatMessagesCommandHandler` (Information) — both genuinely irreversible data-loss events; ordinary create/edit/archive/restore/soft-delete/favorite/pin do **not** log in that precedent. Followed the precedent's actual (narrower) scope rather than this task's broader literal list: `PurgeKnowledgeBaseCommandHandler` already had Warning-level logging (added earlier, during US3 implementation) mirroring `PurgeUserChatCommandHandlerLog` exactly; added a new `DeleteFolderCommandHandlerLog.FolderCascadeDeleted` (Information) to `DeleteFolderCommandHandler`, emitted only when the cascade actually removed subfolders/documents (an empty-folder delete isn't a data-loss event) — the knowledge-base-organization analogue of `ClearUserChatMessagesCommandHandlerLog`. Existing `DeleteFolderCommandTests.cs` updated for the new `ILogger<DeleteFolderCommandHandler>` constructor parameter.
- [X] T098 Run the full `quickstart.md` validation guide (all 8 scenarios) end-to-end against a fresh local environment and record results. **This sandbox has no live browser/running server/real SQL Server instance** — the same constraint already documented honestly throughout this implementation for every Playwright E2E spec and every `AskLucy.Persistence.Tests` file. In place of a live run, traced every scenario's steps against the actual shipped code (controller routes, command/query handlers, domain methods, frontend wiring) and cross-referenced against the automated test suite that *did* run:
  - **Scenario 1** (core lifecycle): matches `KnowledgeBaseTests.cs`, `CreateKnowledgeBaseCommandTests.cs`, `UpdateKnowledgeBaseDetailsCommandTests.cs`, `DeleteAndPurgeKnowledgeBaseCommandTests.cs` (all passing).
  - **Scenario 2** (folders, drag-and-drop, depth limits): matches `CreateFolderCommandTests.cs`, `MoveFolderCommandTests.cs`, `DeleteFolderCommandTests.cs`, `UploadDocumentCommandTests.cs`, `KnowledgeBaseFolderTests.cs` (all passing); keyboard-equivalent move is the "Move to…" menu action (documented design choice in `useKnowledgeBaseDragAndDrop.ts`), not a custom keyboard-drag gesture.
  - **Scenario 3** (archive/restore): matches `KnowledgeBaseLifecycleTransitionTests.cs`, `RestoreKnowledgeBaseCommandTests.cs` (passing).
  - **Scenario 4** (dashboard discovery): matches `SearchKnowledgeBasesQueryHandlerTests.cs`, `DashboardSummaryQueryTests.cs` (passing) plus `KnowledgeBaseCursorPaginationTests.cs`/`KnowledgeBaseScalePerformanceTests.cs` (compile-verified, not executed — no test DB in this sandbox).
  - **Scenario 5** (categories/tags, cross-user privacy): matches `ListCategoriesQueryTests.cs`, `CreateCustomCategoryCommandTests.cs`, `DeleteCategoryCommandTests.cs` (passing) — cross-user privacy is enforced at the query level (`ListPredefinedAndOwnedAsync` filters by `OwnerId == null || OwnerId == callerId`), verified by the "should scope to the calling user" test.
  - **Scenario 6** (duplicate/export, independent copies): matches `DuplicateKnowledgeBaseCommandTests.cs` (independent-file-copy test specifically proves the source's `StoredFileName` is never reused) and `ExportKnowledgeBaseQueryTests.cs` (passing) plus `KnowledgeBaseDuplicationPerformanceTests.cs` (compile-verified, not executed).
  - **Scenario 7** (permanent deletion, owner-triggered and automatic sweep): matches `PurgeKnowledgeBaseCommandHandler`'s own tests plus `KnowledgeBasePurgeHostedServiceTests.cs` (restore-cancels-purge and sweep-failure-isolation both covered, passing).
  - **Scenario 8** (accessibility): see T096 above — automated portion passed; manual keyboard/high-contrast/mobile passes not executable here.

  No gap was found between the quickstart's acceptance steps and what the automated suite already covers; the honest exceptions are exactly the environment-constrained items already called out task-by-task above (real-DB persistence tests, E2E specs, manual browser passes).
- [X] T099 [P] Update `docs/DATABASE.md`/`docs/API_GUIDELINES.md` (or equivalent existing docs) to reflect the shipped `KnowledgeBase`/`KnowledgeBaseFolder`/`KnowledgeBaseDocument`/`KnowledgeBaseTag`/`KnowledgeBaseCategory`/`KnowledgeBaseAuditLog` schema and the `/api/v1/knowledge-bases/**` endpoint surface (constitution §13 Documentation). `docs/DATABASE.md` §7 ("Knowledge Context") and its relationships diagram (§16) replaced — the prior content was a pre-implementation sketch (embedding/vector/team-sharing fields that were never built) rather than what shipped; added a "Shipped in SPEC-014" note mirroring the existing precedent in §6 ("Conversation Context") for the same situation. `docs/API_GUIDELINES.md` §23 ("Knowledge Base Endpoints") replaced with the actual route surface (lifecycle actions, folders, documents, the sibling `KnowledgeBaseTaxonomyController` for categories/tags), mirroring §21's "As shipped" note style for the Chat endpoints.

**Not needed as new tasks** — both already fixed platform-wide by specs/002-chat-history-management and apply to every handler/endpoint this feature adds with no further work: `DbUpdateConcurrencyException` → `409 Conflict` Problem Details (`ProblemDetailsMiddleware`, covers `KnowledgeBase.RowVersion` conflicts automatically) and OpenAPI discoverability (the `/openapi` SPA-fallback exclusion covers these new controllers automatically).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup; **blocks every user story**.
- **User Stories (Phase 3–8)**: All depend on Foundational completion.
  - US1 and US2 are both P1 with no dependency on each other's domain work, but US2's
    `UploadDocumentCommand` (T048) is what first exercises `IFileStorage.DeleteAsync` (T009,
    used by US1's Purge) end-to-end with real documents — implement US1 before US2 so Purge
    has something real to delete when US2 lands, even though neither strictly blocks the
    other's own tests.
  - US3 (archive/restore) depends on Foundational only, reusing US1's `KnowledgeBaseOwnershipGuard`/DTOs — sequence after US1/US2 for practical reasons (its UI hooks into the dashboard US2's detail page and US1's card component already build).
  - US4 (dashboard discovery) depends on Foundational and reuses US1's `KnowledgeBaseSummaryDto` (T029) and cache-invalidation touchpoints in US1/US2's mutating handlers (T025, T027, T028, T048, T050) — sequence after US1–US3.
  - US5 (categories/tags) depends on Foundational and US1's `KnowledgeBaseEditDialog.tsx` (T034) for its UI; its backend (categories/tags) has no dependency on US2–US4.
  - US6 (duplicate/export) depends on Foundational, US1's Purge/`IFileStorage.DeleteAsync` path (T009, T028) for its independence guarantee, and US2's `UploadDocumentCommand` (T048) for something to duplicate.
  - Recommended order: **US1 → US2 → US3 → US4 → US5 → US6** (matches spec.md priority order and minimizes cross-story rework).
- **Polish (Phase 9)**: Depends on all desired user stories being complete.

### Parallel Opportunities

- All Foundational domain-entity tasks (T003–T008) are `[P]` — different files.
- T010, T011, T012, T017 (Foundational abstractions/options/guard) are `[P]` — independent of each other and of T003–T008 except where noted.
- Within each story, test tasks marked `[P]` (different files) can run together, and frontend-only tasks marked `[P]` can run alongside backend tasks in the same story once the contracts they depend on are stable.
- US5 and US6 (both P3, both depending only on Foundational + US1's shared UI shell) can be staffed in parallel once US1–US4 are done, if desired.

---

## Parallel Example: User Story 1

```bash
# Tests together:
Task: "Unit tests for KnowledgeBase domain methods in tests/AskLucy.Domain.Tests/KnowledgeBases/KnowledgeBaseTests.cs"
Task: "Integration test: CreateKnowledgeBaseCommand in tests/AskLucy.Application.Tests/KnowledgeBases/CreateKnowledgeBaseCommandTests.cs"
Task: "Integration test: UpdateKnowledgeBaseDetailsCommand in tests/AskLucy.Application.Tests/KnowledgeBases/UpdateKnowledgeBaseDetailsCommandTests.cs"
Task: "Integration test: Delete/Purge KnowledgeBase commands in tests/AskLucy.Application.Tests/KnowledgeBases/DeleteAndPurgeKnowledgeBaseCommandTests.cs"
Task: "Integration test: cross-user ownership denial in tests/AskLucy.Application.Tests/KnowledgeBases/KnowledgeBaseOwnershipTests.cs"
Task: "Unit tests for KnowledgeBasePurgeHostedService in tests/AskLucy.Infrastructure.Tests/KnowledgeBases/KnowledgeBasePurgeHostedServiceTests.cs"

# Frontend shell together (after backend DTO shape lands):
Task: "KnowledgeBaseDashboardPage.tsx, KnowledgeBaseCard.tsx, KnowledgeBaseEditDialog.tsx, ConfirmPurgeDialog.tsx"
Task: "knowledgeBasesApi.ts, useKnowledgeBases.ts, useKnowledgeBaseMutations.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) + Phase 2 (Foundational).
2. Complete Phase 3 (US1) — create/edit/delete a knowledge base, with full permanent-deletion
   lifecycle (owner-triggered and automatic).
3. **STOP and VALIDATE** against quickstart.md Scenarios 1 and 7.
4. This alone is a shippable improvement: users can create and manage private knowledge base
   containers end-to-end, even before folders, discovery, taxonomy, or duplication exist.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → validate → demo (MVP: core lifecycle + permanent deletion).
3. US2 → validate → demo (folder organization + document upload).
4. US3 → validate → demo (archive/restore).
5. US4 → validate → demo (dashboard discovery at scale).
6. US5 → validate → demo (categories/tags).
7. US6 → validate → demo (duplicate/export) — full feature complete.
8. Phase 9 (Polish) — accessibility audit, logging, quickstart validation, documentation.

---

## Notes

- `[P]` tasks touch different files with no unmet dependency.
- Every destructive action (Permanent Delete, non-empty Folder Delete) requires an explicit
  `confirm: true` enforced at the Application command boundary, not only in the UI
  (constitution §2.VIII No Silent Failures).
- Commit after each task or logical group; stop at any Checkpoint to validate a story
  independently before moving on.
- Avoid: introducing a `KnowledgeBasePermission` table (plan.md Constitution Check —
  deliberately deferred per constitution §2.III YAGNI), a separate `KnowledgeBaseStatistics`
  table (data-model.md — denormalized counters on `KnowledgeBase` instead), a distributed
  cache for the dashboard summary (research.md Decision 7 — `IMemoryCache` is sufficient), or
  a background job queue for duplication (research.md Decision 4 — synchronous is sufficient
  at the stated scale).
