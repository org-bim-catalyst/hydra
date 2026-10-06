# Tasks: Model Deprecation Workflow

**Input**: Design documents from `/specs/069-model-deprecation-workflow/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution (§10) requires tests in the same change as the behavior.

**Organization**: Grouped by user story so each can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: the user story (US1 to US5) the task belongs to
- All paths are relative to the repository root. `Web` = `src/AskLucy.Web`, `Client` = `src/AskLucy.Web/ClientApp/src`.

## Standing rules for every task

1. `Application` never references EF Core (use repository interfaces; set-based writes live in `Persistence`).
2. Every async method takes and passes a `CancellationToken`. No empty catch, no unawaited call (§2 VIII).
3. Frontend: every mutation and query has a visible error path (toast or inline) and a retry where it makes sense. Use `tsc -b --noEmit`, not bare `tsc --noEmit`.
4. Run the **full** frontend suite, not only the touched files. `Web.Tests` needs `PERSISTENCE_TESTS_CONNECTION_STRING`; Persistence tests need `PERSISTENCE_TESTS_2_CONNECTION_STRING`.
5. After a migration, apply it by hand to the shared test database or CI fails.

---

## Phase 1: Setup

- [ ] T001 Confirm the baseline: run `dotnet build` and the existing sync-review tests (`tests/AskLucy.Application.Tests/Ai/`, `Client/features/admin/**/SyncReview*.test.tsx`) and note any failures that already exist, so they aren't blamed on this feature.

---

## Phase 2: Foundational (blocks every story)

**Purpose**: the Domain model, persistence, and the ranking rule that all stories use.

### Tests first

- [ ] T002 [P] `ReplacementModelSelectorTests` in `tests/AskLucy.Domain.Tests/Ai/ReplacementModelSelectorTests.cs`: same-provider only; excludes non-Available and models in the batch set; requires every capability flag of the retired model; ranks by price distance (unknown price last; retired model without a price ignores price), then context at least as large, then newest, then `ModelKey`; returns the ranked list and `MissingCapabilities` for override candidates; deterministic on ties (research D1).
- [ ] T003 [P] `PlatformFunctionRequirementsTests` in `tests/AskLucy.Domain.Tests/Ai/PlatformFunctionRequirementsTests.cs`: required flags per target as in research D2.
- [ ] T004 [P] `AIModelDeprecateTests` in `tests/AskLucy.Domain.Tests/Ai/AIModelDeprecateTests.cs`: `Deprecate()` works only from Available and throws `DomainRuleViolationException` from Unavailable and Deprecated.
- [ ] T005 [P] `ModelDeprecationEntityTests` in `tests/AskLucy.Domain.Tests/Ai/ModelDeprecationEntityTests.cs`: `MarkUserImpactCompleted` only from `Pending`; `Accept()`/`ChangeReplacement()` only from `AwaitingValidation`; replacement must differ from the model.

### Domain

- [ ] T006 Add `AIModel.Deprecate()` in `src/AskLucy.Domain/Ai/AIModel.cs` (Available to Deprecated only, otherwise `DomainRuleViolationException`). Leave `SetStatus` for US5.
- [ ] T007 [P] Add `ReplacementModelSelector` and its result records in `src/AskLucy.Domain/Ai/ReplacementModelSelector.cs` (research D1).
- [ ] T008 [P] Add `PlatformFunctionRequirements` in `src/AskLucy.Domain/Ai/PlatformFunctionRequirements.cs` (research D2).
- [ ] T009 [P] Add the enums (`ModelDeprecationKind`, `UserImpactStatus`, `DeprecationReason`, `ReplacementSource`, `ReplacementValidationStatus`, `DefaultTargetKind`, `DefaultChangeOutcome`, `DefaultFlagReason`, `SwitchedItemKind`) in `src/AskLucy.Domain/Ai/ModelDeprecationEnums.cs`.
- [ ] T010 Add `ModelDeprecationBatch`, `ModelDeprecation`, `PlatformDefaultChange` and `ItemSwitch` in `src/AskLucy.Domain/Ai/` per [data-model.md](data-model.md), with the transition methods from T005.

### Persistence

- [ ] T011 [P] Add the four Fluent configurations in `src/AskLucy.Persistence/Configurations/`: string-stored enums, `ModelDeprecation.ModelId` unique, indexes on `UserImpactStatus + ConfirmedAtUtc`, `ReplacementModelId`, `PlatformDefaultChange (Outcome, ResolvedAtUtc)` filtered to open flags, `ItemSwitch (BatchId, OwnerUserId)`; restrict deletes as in the data model. Register the `DbSet`s in `AskLucyDbContext`.
- [ ] T012 Generate the reversible migration `AddModelDeprecationWorkflow` in `src/AskLucy.Persistence/Migrations/` (no BOM, System usings first). Apply it to the shared test database.
- [ ] T013 [P] Define `IModelDeprecationRepository` (add and load batch, load by model or id, list with cursor, open-flag counts, find open flags by target) and `IModelReferenceStore` (count and load references per model, chunked owner walk, rewrite helpers) in `src/AskLucy.Application/Ai/Abstractions/`.
- [ ] T014 Implement `ModelDeprecationRepository` in `src/AskLucy.Persistence/Repositories/ModelDeprecationRepository.cs` and register both repositories in the Persistence DI extension.
- [ ] T015 [P] `ModelDeprecationRepositoryTests` in `tests/AskLucy.Persistence.Tests/Ai/`: unique model, cursor paging, open-flag lookup and counts.

**Checkpoint**: the Domain compiles and its tests pass; the migration applies and rolls back.

---

## Phase 3: User Story 1 - Deprecate a vendor-removed model after seeing its impact (P1) 🎯 MVP

**Goal**: confirming a removed row deprecates the model, with an impact preview first and a deprecation record afterwards.

**Independent test**: [quickstart S1](quickstart.md#scenario-s1-deprecate-with-impact-preview-us1-fr-001fr-004).

### Tests

- [ ] T016 [P] [US1] `ModelReferenceStoreTests` in `tests/AskLucy.Persistence.Tests/Ai/`: counts per item type and distinct users; excludes soft-deleted items, messages and published versions; includes archived chats; workflow-step count parses node JSON and ignores non-AiPrompt nodes (research D10, D11).
- [ ] T017 [P] [US1] `GetProviderModelSyncDiffImpactTests` in `tests/AskLucy.Application.Tests/Ai/`: each removed row carries platform defaults, proposed replacement, eligible list with missing capabilities, counts only (no identities).
- [ ] T018 [P] [US1] `ApplyProviderModelSyncDeprecationTests` in `tests/AskLucy.Application.Tests/Ai/`: a selected removed row ends Deprecated with a record (reason, time, admin); an unselected row is unchanged; a stale row or an invalid explicit replacement fails per row while the rest apply; a stale proposed replacement is re-ranked and the used model is reported; an empty request is still rejected.
- [ ] T019 [P] [US1] `ModelDeprecationEndpointsTests` (apply, diff, list, detail) in `tests/AskLucy.Web.Tests/Ai/ModelDeprecation/`: 401 and 403 for non-admins (SC-004 of spec 008), custom role with the permission allowed, Problem Details shapes.
- [ ] T020 [P] [US1] Frontend tests for the sync dialog impact and replacement picker, record dialog, with `.a11y.test.tsx` each, in `Client/features/admin/components/`.

### Backend

- [ ] T021 [US1] Implement `ModelReferenceStore` in `src/AskLucy.Persistence/Repositories/ModelReferenceStore.cs`: the impact counts (research D10/D11) and the chunked owner walk used later by US3.
- [ ] T022 [US1] Extend `RemovedModelDto` with `impact`, and `GetProviderModelSyncDiffQueryHandler` to fill it using `ReplacementModelSelector` and `IModelReferenceStore` in `src/AskLucy.Application/Ai/Queries/GetProviderModelSyncDiff/`.
- [ ] T023 [US1] Implement `ModelDeprecationService` in `src/AskLucy.Application/Ai/Deprecation/ModelDeprecationService.cs`: for each non-stale row, re-rank, validate the choice, call `AIModel.Deprecate()`, create the batch and record with `UserImpactStatus = Pending` (research D5). Platform-default handling is added in US2.
- [ ] T024 [US1] Add `ReplacementChoice` to the apply command, update `ApplyProviderModelSyncCommandHandler` and its validator to use the service instead of `SetStatus(Unavailable)`, and add `deprecations[]` to `ApplyProviderModelSyncResultDto` in `src/AskLucy.Application/Ai/Commands/ApplyProviderModelSync/`. One `SaveChangesAsync`.
- [ ] T025 [P] [US1] Add `ListModelDeprecations` and `GetModelDeprecation` queries and DTOs in `src/AskLucy.Application/Ai/Queries/`.
- [ ] T026 [US1] Add the routes from [contracts §3](contracts/admin-model-deprecation.md) to `Web/Controllers/v1/AdminAiProvidersController.cs` (`[RequirePermission]`, XML docs, OpenAPI) and `openFlagCount` on the provider list.
- [ ] T027 [P] [US1] Log every deprecation as an admin action in `src/AskLucy.Application/Ai/AiAdminActionLog.cs` (FR-024).

### Frontend

- [ ] T028 [P] [US1] Add the deprecation API, types and hooks in `Client/features/admin/api/modelDeprecationApi.ts` and `Client/features/admin/hooks/useModelDeprecations.ts`.
- [ ] T029 [US1] Extend the removed side of the sync-review dialog in `Client/features/admin/components/` with the impact summary, the proposed replacement and an override picker (listing lost capabilities), and the permanence warning (FR-003, FR-004, FR-007). Show a partial-failure result per row (specs/009).
- [ ] T030 [US1] Add `DeprecationRecordDialog.tsx` in `Client/features/admin/components/` and an entry point from a Deprecated model's row in the catalog.

**Checkpoint**: a sync deprecates a model with a visible record. Defaults are not touched yet, so don't ship this story alone to production.

---

## Phase 4: User Story 2 - Platform defaults follow the replacement or get flagged (P1)

**Goal**: no platform default ever names a Deprecated model.

**Independent test**: [quickstart S2](quickstart.md#scenario-s2-defaults-follow-the-replacement-or-get-flagged-us2).

### Tests

- [ ] T031 [P] [US2] `PlatformDefaultReassignmentTests` in `tests/AskLucy.Application.Tests/Ai/Deprecation/`: provider default and capability assignment reassigned; required-capability loss flags instead; no replacement clears and flags; `ImageGeneration` assignment removed when flagged; the flag records the serving provider and model; models in the same batch are never chosen.
- [ ] T032 [P] [US2] `PlatformDefaultFlagResolverTests`: setting or clearing the default, or upserting the assignment, closes the matching flag in the same save and records who, when and which model.
- [ ] T033 [P] [US2] `FlaggedCapabilityBehaviorTests` in `tests/AskLucy.Application.Tests/Ai/`: a flagged `ImageGeneration` reports not configured and never falls back to an unsuitable model (FR-011); other flagged targets use the existing default rules.
- [ ] T034 [P] [US2] Frontend tests for the flag banner and per-provider flag count, with an axe test, in `Client/features/admin/`.

### Backend

- [ ] T035 [US2] Extend `ModelDeprecationService` to build `PlatformDefaultChange` rows: reassign `AIProvider.DefaultModelId` and `AiCapabilityAssignment.ModelId`, or flag per [research D2/D9](research.md), capturing the serving model from `DefaultProviderResolver`.
- [ ] T036 [US2] Implement `PlatformDefaultFlagResolver` in `src/AskLucy.Application/Ai/Deprecation/` and call it from the provider-update and capability-assignment handlers (FR-010).
- [ ] T037 [US2] Make `DefaultProviderResolver` log at Warning when it falls back past an unusable default (it is silent today), in `src/AskLucy.Application/Ai/`.

### Frontend

- [ ] T038 [US2] Show open flags as a banner and a count chip on the AI Providers page, each naming the retired model, the reason and the model now serving, with a link to set a new default, in `Client/features/admin/pages/AdminAiProvidersPage.tsx` and `Client/features/admin/components/`.

**Checkpoint**: deprecation is safe for the platform. This and US1 together are the minimum to ship.

---

## Phase 5: User Story 3 - Affected users keep working and are told once (P2)

**Goal**: user items move to the replacement, owners get one notice, runs keep working.

**Independent test**: [quickstart S3 and S6](quickstart.md#scenario-s3-users-keep-working-and-are-told-once-us3).

### Tests

- [ ] T039 [P] [US3] `ExecutableModelResolverTests` in `tests/AskLucy.Application.Tests/Ai/Deprecation/`: non-Deprecated model returned as is; follows the chain; stops at 10 hops; detects cycles; ends in `ModelRetiredException` when no Available successor; logs the redirect.
- [ ] T040 [P] [US3] `UserImpactProcessorTests`: rewrites each item type (chat, agent draft, prompt preference, workflow draft node, user default); never touches versions or messages; writes `ItemSwitch`; one notice per owner across all models; chunked; safe to re-run with no duplicate notices or switches; sets `Completed` and `NotifiedUserCount`.
- [ ] T041 [P] [US3] `ModelRetiredRunTests` in `tests/AskLucy.Application.Tests/`: chat send, agent run, workflow AiPrompt step and prompt execution redirect through the resolver, record the successor on the output, and fail with the user-safe message when none exists (agent and workflow failures reach `IOperationalFailureRecorder`).
- [ ] T042 [P] [US3] `ModelDeprecationUserImpactJobFaultTests` in `tests/AskLucy.Web.Tests/Ai/ModelDeprecation/`: kill and restart mid-chunk, the sweep re-enqueues a stuck `Pending` batch, no duplicates, no lost items (quickstart S3.4).
- [ ] T043 [P] [US3] `ItemRewriteTests` in `tests/AskLucy.Persistence.Tests/Ai/`: the rewrite touches only items that still name a Deprecated model, honours soft-delete filters, and handles the workflow node JSON edit.

### Backend

- [ ] T044 [US3] Implement `ExecutableModelResolver` and `ModelRetiredException` in `src/AskLucy.Application/Ai/Deprecation/` (research D3.2).
- [ ] T045 [US3] Call the resolver from `AgentExecutionOrchestrator` (~L133), `PromptNodeExecutor` (~L78), `ExecutePromptCommandHandler` (~L58) and the chat send validator, and report agent and workflow failures to `IOperationalFailureRecorder` (Engine `Agent` or `Workflow`, Kind `NotConfigured`). Update their existing tests.
- [ ] T046 [US3] Add the item rewrite methods to `ModelReferenceStore`: chats, agent drafts, prompt preference, workflow draft nodes, user defaults, each chunked by owner.
- [ ] T047 [US3] Add the `system.ai-model.deprecated` type to `NotificationTypeCatalog` and `NotificationTypeKeys` in `src/AskLucy.Domain/Notifications/`, and the English in-app and email seeds in `src/AskLucy.Infrastructure/Notifications/Templates/Seed/en/` ([contracts/notification-types.md](contracts/notification-types.md)).
- [ ] T048 [US3] Implement `UserImpactProcessor` and `DeprecationNoticePublisher` in `src/AskLucy.Application/Ai/Deprecation/`: per-owner notice with `EventKey = ai-model-deprecation:{batchId}:{userId}`, one `SaveChangesAsync` per chunk.
- [ ] T049 [US3] Add `ModelDeprecationUserImpactJob` and `ModelDeprecationSweepJob` in `src/AskLucy.Infrastructure/Ai/Deprecation/`, enqueue the job after the confirm save in the apply handler, and register the sweep with `RegisterRecurringJob` in `Web/Program.cs` (every minute, picks batches `Pending` for over 2 minutes).
- [ ] T050 [US3] Make a user's own model picker mark a retired model and prompt a new choice where an item still names one (FR-016), in `Client/features/chat/components/ProviderModelSelector.tsx` and `Client/features/agents/components/AgentBuilder.tsx`, with tests.

**Checkpoint**: users keep working and are informed. Run quickstart S3 and S6.

---

## Phase 6: User Story 4 - Administrators receive a summary and validate (P2)

**Goal**: every AI-provider admin gets the summary, and can accept or change the replacement.

**Independent test**: [quickstart S4](quickstart.md#scenario-s4-admins-validate-us4-fr-011ab-fr-020b).

### Tests

- [ ] T051 [P] [US4] `AdminRecipientResolverTests` in `tests/AskLucy.Application.Tests/Ai/Deprecation/`: union of Administrator, Super User and custom-role holders of `admin.ai-providers.manage`; Super User-only users go to the `.super-user` type; holding both counts as Administrator; chunks of 100; one summary per admin covering every model in the batch.
- [ ] T052 [P] [US4] `ReplacementValidationTests`: accept; change re-points defaults and items still on the first replacement, skips items the owner changed, queues the follow-up job, sends `replacement-changed` once per owner; both only from `AwaitingValidation`; a change to a model lacking a required capability is rejected unless acknowledged and then flagged.
- [ ] T053 [P] [US4] `DeprecationSummaryNotificationTests` in `tests/AskLucy.Web.Tests/Ai/ModelDeprecation/`: summary appears in-app for Administrator and Super User; email half is recorded per type rules (Skipped until 067's email channel exists).
- [ ] T054 [P] [US4] Frontend tests for accept and change actions in the record dialog, with an axe test.

### Backend

- [ ] T055 [US4] Add the other three notification types (`replacement-changed`, `deprecation-summary`, `deprecation-summary.super-user`) and their English seeds, per [contracts/notification-types.md](contracts/notification-types.md).
- [ ] T056 [US4] Implement `AdminRecipientResolver` using `IRoleRepository.ListByPermissionAsync` and `IRoleAssignmentRepository.ListUserIdsByRoleAsync`, and publish the summaries in the confirm save from `ModelDeprecationService`.
- [ ] T057 [US4] Add `AcceptModelReplacementCommand` and `ChangeModelReplacementCommand` with validators and handlers in `src/AskLucy.Application/Ai/Commands/`; the change creates a `ReplacementChange` batch and enqueues the job in re-point mode. Add the routes from [contracts §4](contracts/admin-model-deprecation.md).
- [ ] T058 [US4] Extend `UserImpactProcessor` with re-point mode (move only items whose current model equals `ItemSwitch.NewModelId`) and the follow-up notice.

### Frontend

- [ ] T059 [US4] Add accept and change actions, validation state and the open-flag list to `DeprecationRecordDialog.tsx`, and a "needs validation" indicator on the AI Providers page.

**Checkpoint**: the whole workflow works. Run quickstart S4.

---

## Phase 7: User Story 5 - A Deprecated model stays retired everywhere (P3)

**Goal**: the rule holds at every entry point, not only in the screen.

**Independent test**: [quickstart S5](quickstart.md#scenario-s5-status-lock-us5-fr-021022).

### Tests

- [ ] T060 [P] [US5] `AIModelStatusLockTests` in `tests/AskLucy.Domain.Tests/Ai/`: `SetStatus` throws when the current or target status is Deprecated; Available and Unavailable still toggle.
- [ ] T061 [P] [US5] `UpdateAiModelStatusEndpointTests` in `tests/AskLucy.Web.Tests/Ai/ModelDeprecation/`: `409` Problem Details for Deprecated to Available, to Unavailable, and Available to Deprecated, with the messages in [contracts §5](contracts/admin-model-deprecation.md); status unchanged; each rejection is logged.

### Implementation

- [ ] T062 [US5] Make `SetStatus` terminal for Deprecated in `src/AskLucy.Domain/Ai/AIModel.cs`, and make `UpdateAiModelStatusCommandHandler` translate the exception to a `409` through the global middleware, plus an admin-action log line (FR-024).
- [ ] T063 [US5] Check that `AiModelStatusMenu.tsx` still disables the toggle for a Deprecated model and shows the server's reason if a request is rejected anyway, in `Client/features/admin/components/`.

---

## Phase 8: Polish and cross-cutting

- [ ] T064 [P] Performance check (SC-007): the quickstart seed of 5,000 conversations and 2,000 users, apply returns in under 10 s; record the numbers in [research.md](research.md).
- [ ] T065 [P] Security review: permission check on every new route, counts-only preview, no user identity in any admin response (constitution §16.6).
- [ ] T066 [P] Update documentation: the API contract notes, the AI provider admin guide, and a note on `specs/008` (FR-002, FR-008) and `specs/009` (FR-007) that deprecation now supersedes Unavailable for removed-from-vendor rows.
- [ ] T067 Run the full backend and frontend suites, then quickstart S1 to S6 end to end against the dev test database.
- [ ] T068 When spec 067's email channel and role-conditional preference locks ship: revisit [research D7/D8](research.md), merge the two summary types if possible, and re-verify SC-006 for email.

---

## Dependencies and order

- Phase 2 blocks everything. Within it: T006 to T010 (Domain) before T011 to T014 (Persistence).
- **US1** needs Phase 2. **US2** extends US1's `ModelDeprecationService` (T035 after T023). **US3** needs US1's record and `ModelReferenceStore` (T046 after T021). **US4** needs US1's record and US3's job and processor (T058 after T048). **US5** is independent of US1 to US4 (only Domain and the status handler) and can run at any point after Phase 2.
- Within a story: tests, then Domain and Application, then Persistence, then endpoints, then frontend.

## Parallel opportunities

- Phase 2 tests T002 to T005 together, then T007 to T009 and T011 and T013 together.
- In US1, tests T016 to T020 together, then T025, T027 and T028 alongside T022 to T024.
- US2 tests T031 to T034 together. US3 tests T039 to T043 together. US4 tests T051 to T054 together.
- US5 (T060 to T063) can be done by a second person in parallel with US2 to US4.

## Implementation strategy

- **MVP**: Phase 2, US1 and US2 together. That alone removes the silent fallback on platform defaults and gives the admin a reviewed deprecation. Don't ship US1 on its own: it would deprecate models while leaving defaults pointing at them.
- **Increment 2**: US3 and US5. Users keep working and are told; the status lock closes the bypass.
- **Increment 3**: US4. Admin validation and the follow-up notices.
- **Later**: T068, once spec 067's email channel and role-conditional locks exist.
