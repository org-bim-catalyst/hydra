---

description: "Task list for editable provider health-check retention"
---

# Tasks: Editable Provider Health-Check Retention

**Input**: Design documents from `specs/082-editable-health-retention/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/admin-provider-health-retention-api.md, quickstart.md

**Tests**: Included. The project constitution requires unit and integration testing, and plan.md lists the suites; tests are written before their implementation task.

**Organization**: Tasks are grouped by user story (P1 → P2 → P3), with shared domain/persistence work in the foundational phase.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete dependencies)
- **[Story]**: User story (US1, US2, US3)
- Every task names the exact file path

## Path Conventions

Repository root is `E:\Workshop\BIM Catalyst\Web Apps\Platform\Ask Lucy`. Backend projects live under `src/AskLucy.*`; tests under `tests/AskLucy.*.Tests`; the React client under `src/AskLucy.Web/ClientApp/src`.

---

## Phase 1: Setup

**Purpose**: Confirm a clean baseline before touching anything.

- [X] T001 Verify the baseline builds: run `dotnet build "Ask Lucy.sln"` and, in `src/AskLucy.Web/ClientApp`, `npx tsc -b --noEmit`; both must pass before changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entities, persistence, options relocation and the migration that every story depends on.

**⚠️ CRITICAL**: No user story can start until this phase is complete.

- [X] T002 Create `ProviderHealthRetentionSetting` in `src/AskLucy.Domain/Ai/ProviderHealthRetentionSetting.cs` — fixed `SingletonId`, `MinRetentionDays = 1`, `MaxRetentionDays = 365`, `DefaultRetentionDays = 30`, and `Update(int retentionDays, string actor, DateTime utcNow)` that throws `ArgumentOutOfRangeException` outside the range and changes nothing.
- [X] T003 [P] Create `ProviderHealthRetentionAuditLog` in `src/AskLucy.Domain/Ai/ProviderHealthRetentionAuditLog.cs` — `BaseEntity, ICorrelated`; `ActorUserId`, `PreviousRetentionDays`, `NewRetentionDays`, `OccurredAtUtc`, `CorrelationId`; static `Record(actor, previous, new, occurredAtUtc)` factory only (no setters).
- [X] T004 [P] Move `ProviderHealthCheckOptions` from `src/AskLucy.Infrastructure/Ai/ProviderHealthCheckOptions.cs` to `src/AskLucy.Application/Ai/ProviderHealthCheckOptions.cs` (namespace `AskLucy.Application.Ai`); update imports in `src/AskLucy.Infrastructure/DependencyInjection.cs`, `src/AskLucy.Infrastructure/Ai/ProviderHealthCheckHostedService.cs`, `src/AskLucy.Infrastructure/Ai/ProviderHealthCheckRetentionJob.cs`, and `src/AskLucy.Web/Controllers/v1/AdminProviderHealthRetentionController.cs`.
- [X] T005 Create `IProviderHealthRetentionRepository` in `src/AskLucy.Application/Abstractions/IProviderHealthRetentionRepository.cs` — `GetAsync`, `ExpectRowVersion(setting, rowVersion)`, `AddAudit(entry)`. No create method.
- [X] T006 Create `ProviderHealthRetentionDto` and the `ProviderHealthRetentionSource` enum in `src/AskLucy.Application/Ai/ProviderHealthRetentionDtos.cs` — fields `RetentionDays`, `IntervalMinutes`, `Source`, `RowVersion` (base64 string or null).
- [X] T007 [P] Create EF configurations `src/AskLucy.Persistence/Configurations/ProviderHealthRetentionSettingConfiguration.cs` (table, `Id.ValueGeneratedNever`, `RowVersion.IsRowVersion`, `HasData` seeding 30 days with a fixed `CreatedAtUtc` and `CreatedBy = "system"`) and `src/AskLucy.Persistence/Configurations/ProviderHealthRetentionAuditLogConfiguration.cs` (table, max lengths, correlation id).
- [X] T008 Add `DbSet<ProviderHealthRetentionSetting>` and `DbSet<ProviderHealthRetentionAuditLog>` to `src/AskLucy.Persistence/AskLucyDbContext.cs`.
- [X] T009 Implement `src/AskLucy.Persistence/Repositories/ProviderHealthRetentionRepository.cs` (singleton read, `ExpectRowVersion` via `OriginalValue`, `AddAudit`) and register it in `src/AskLucy.Persistence/DependencyInjection.cs`.
- [X] T010 Generate migration `AddProviderHealthRetentionSetting` in `src/AskLucy.Persistence/Migrations/` with `dotnet ef migrations add`; verify it creates both tables, inserts the singleton (30 days), and has a working `Down` that drops both tables; commit the updated model snapshot.
- [X] T011 Apply the new migration by hand to the shared test database used by `PERSISTENCE_TESTS_CONNECTION_STRING` / `PERSISTENCE_TESTS_2_CONNECTION_STRING` (repository convention; the test host does not migrate it).

**Checkpoint**: Schema and shared types ready; stories can begin.

---

## Phase 3: User Story 1 - Change the retention window from the Admin panel (Priority: P1) 🎯 MVP

**Goal**: An administrator can read, edit and save the retention window from `/admin/provider-health`, with validation and stale-edit protection.

**Independent Test**: Open the page as an administrator, save a valid value, reload, and confirm it persists; save an invalid value and confirm it is refused; save with a stale version and confirm 409; confirm a view-only user sees no edit control.

### Tests for User Story 1

- [X] T012 [P] [US1] Add domain tests in `tests/AskLucy.Domain.Tests/Ai/ProviderHealthRetentionSettingTests.cs` — boundary values 1 and 365 accepted; 0 and 366 throw; `Update` sets value/audit fields; constants and singleton id are stable.
- [X] T013 [P] [US1] Add Application tests in `tests/AskLucy.Application.Tests/Ai/ProviderHealthRetentionHandlerTests.cs` — GET returns stored value and `Source = Stored` with base64 row version; GET with no row returns `RetentionDays`/`IntervalMinutes` from options and `Source = Configuration`; PUT with matching version updates and saves once; stale version throws `ConcurrencyConflictException`; missing row on PUT throws `ConcurrencyConflictException`; validator rejects 0, 366 and an empty version.
- [X] T014 [P] [US1] Extend `tests/AskLucy.Web.Tests/OperationalFailures/AdminProviderHealthRetentionEndpointTests.cs` — GET 200 for a viewer (30, `source = "Stored"`, non-null `rowVersion`); GET 403 without view; PUT 200 for a manager; PUT 403 with view only; PUT 428 without `If-Match`; PUT 409 with a stale version; PUT 400 for 0/366. Restore the singleton value and re-read the version after every mutating test because the shared test database is reused.

### Implementation for User Story 1

- [X] T015 [US1] Implement the GET query and handler in `src/AskLucy.Application/Ai/Queries/GetProviderHealthRetention/GetProviderHealthRetentionQuery.cs` and `GetProviderHealthRetentionQueryHandler.cs` — stored row wins; missing row returns the options fallback and `Source = Configuration`; `RowVersion` is base64 or null.
- [X] T016 [US1] Implement the PUT command, validator and handler in `src/AskLucy.Application/Ai/Commands/UpdateProviderHealthRetention/` — FluentValidation `InclusiveBetween(1, 365)` plus non-empty version; handler compares the row version, calls `ExpectRowVersion`, updates the entity and saves once; mismatch or missing row throws `ConcurrencyConflictException`.
- [X] T017 [US1] Add `UpdateProviderHealthRetentionRequest` in `src/AskLucy.Web/Contracts/ProviderHealthRetentionContracts.cs` and extend `src/AskLucy.Web/Controllers/v1/AdminProviderHealthRetentionController.cs` — GET via MediatR; PUT parses `If-Match` (428 when absent/invalid), requires `admin.operational-failures.manage`, and declares 200/400/409/428 Problem Details responses.
- [X] T018 [US1] Extend `src/AskLucy.Web/ClientApp/src/features/admin/providerHealth/providerHealthRetentionApi.ts` — add `source` and `rowVersion` to `ProviderHealthRetention`, add `updateProviderHealthRetention(rowVersion, retentionDays)` sending `If-Match`.
- [X] T019 [US1] Replace the read-only content in `src/AskLucy.Web/ClientApp/src/features/admin/providerHealth/AdminProviderHealthRetentionPage.tsx` with an editable numeric field (1–365, whole days), Save button gated on `useIsAdmin() || useCan('admin.operational-failures.manage')`, success Snackbar, inline range errors, a 409 conflict alert with a reload action, and a read-only/fallback notice when `rowVersion` is null.
- [X] T020 [US1] Update `src/AskLucy.Web/ClientApp/src/features/admin/providerHealth/AdminProviderHealthRetentionPage.test.tsx` — load, save success, invalid value, conflict, and view-only permission scenarios via MSW.

**Checkpoint**: US1 works end-to-end; the page edits and the API enforces versioning, validation and permissions.

---

## Phase 4: User Story 2 - The cleanup job uses the saved value (Priority: P2)

**Goal**: The daily job deletes using the stored window, and uses the configured fallback only when the row is missing.

**Independent Test**: With a stored value of N days, the job deletes before `now - N`; with no row, it deletes before `now - options.RetentionDays`.

### Tests for User Story 2

- [X] T021 [P] [US2] Update `tests/AskLucy.Infrastructure.Tests/Ai/ProviderHealthCheckRetentionJobTests.cs` — stored value (for example 7) wins over options (30); no row falls back to options; both cutoffs are measured from `FakeTimeProvider`; the returned delete count is logged without changing behaviour.

### Implementation for User Story 2

- [X] T022 [US2] Modify `src/AskLucy.Infrastructure/Ai/ProviderHealthCheckRetentionJob.cs` — inject `IProviderHealthRetentionRepository`, read the singleton per run, use `setting?.RetentionDays ?? options.Value.RetentionDays`, and keep the existing `DeleteCheckedBeforeAsync` call and log message. Do not catch database exceptions.

**Checkpoint**: A saved change is picked up by the next run without a redeploy.

---

## Phase 5: User Story 3 - Every change is auditable (Priority: P3)

**Goal**: Every successful save writes one append-only audit row with actor and before/after values; rejected saves write none.

**Independent Test**: Save a new value and confirm exactly one audit row names the administrator, the previous value and the new value; repeat with an invalid or stale save and confirm no row is added.

**Dependency**: Builds on US1's command handler; complete US1 first.

### Tests for User Story 3

- [X] T023 [P] [US3] Extend `tests/AskLucy.Application.Tests/Ai/ProviderHealthRetentionHandlerTests.cs` — a successful PUT calls `AddAudit` once with the actor, previous and new value, then saves once; a stale version or validation failure adds no audit row.
- [X] T024 [US3] Extend `tests/AskLucy.Web.Tests/OperationalFailures/AdminProviderHealthRetentionEndpointTests.cs` — query `ProviderHealthRetentionAuditLogs` (resolve `AskLucyDbContext` from `CustomWebApplicationFactory.Services`) and assert one row after a successful PUT and none after a 400/409. Still restore the singleton after each test.

### Implementation for User Story 3

- [X] T025 [US3] Add the audit write to `src/AskLucy.Application/Ai/Commands/UpdateProviderHealthRetention/UpdateProviderHealthRetentionCommandHandler.cs` — inject `ICurrentUserAccessor` and `TimeProvider`; call `AddAudit(ProviderHealthRetentionAuditLog.Record(actor, previous, new, utcNow))` before the single `SaveChangesAsync`.

**Checkpoint**: All three stories work; the change trail is durable and rejected edits leave no trace.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T026 [P] Update `docs/DATABASE.md` — add the two tables, the seeded singleton, stored-value-over-configuration precedence, and the job's fallback.
- [X] T027 [P] Update the Admin provider health notes in `docs/ENTITY_MODEL.md` if the new entities are listed there; keep the description of `ProviderHealthChecks` unchanged.
- [X] T028 Run the full verification pass: `dotnet test "Ask Lucy.sln"`, frontend `npm run test`, `npx tsc -b --noEmit`, and `npm run lint` in `src/AskLucy.Web/ClientApp`.
- [ ] T029 Execute the manual scenarios in `specs/082-editable-health-retention/quickstart.md` sections 3–5 (API, job, page), including the stale-version conflict and the view-only user.
  - API scenarios (quickstart §3) are covered automatically by `AdminProviderHealthRetentionEndpointTests` against a real host and the real database: GET 200 with `source`/`rowVersion`, GET 403 without view, PUT 200, PUT 403 with view only, PUT 428 without `If-Match`, PUT 409 on a stale version, PUT 400 for 0/366, and the audit-row count after each.
  - Job scenario (quickstart §4) is covered by `ProviderHealthCheckRetentionJobTests` — the stored value wins, configuration is the fallback.
  - Page scenario (quickstart §5) is covered by `AdminProviderHealthRetentionPage.test.tsx` — load, save, range rejection, conflict-with-reload, and the read-only view.
  - **Still manual**: the browser click-through in §5, and a production run of the job. The seeded local admin's password does not match the shared test database, so an authenticated local session could not be established for a hand-driven pass.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001)**: No dependencies.
- **Foundational (T002–T011)**: Depends on T001; blocks every story.
- **US1 (T012–T020)**: Depends on T002–T011. Tests T012–T014 are written before T015–T020.
- **US2 (T021–T022)**: Depends on T002–T011 only; can run in parallel with US1 (different files) once foundational work is done.
- **US3 (T023–T025)**: Depends on US1 (T016 command handler).
- **Polish (T026–T029)**: Depends on the stories being implemented.

### Within Each Story

- Tests first (they fail until the implementation tasks land).
- Entities/configurations → repository → handlers → controller → frontend.

### Parallel Opportunities

- Foundational: T003, T004, T007 can run in parallel after T002; T008 can follow T007.
- US1 tests: T012, T013, T014 are separate files and can be written together.
- US2 (T021–T022) is independent of US1 and can be developed in parallel after foundational work.
- Polish: T026 and T027 are separate docs and can run together.

---

## Parallel Example: User Story 1

```text
Task: "Add domain tests in tests/AskLucy.Domain.Tests/Ai/ProviderHealthRetentionSettingTests.cs"
Task: "Add Application tests in tests/AskLucy.Application.Tests/Ai/ProviderHealthRetentionHandlerTests.cs"
Task: "Extend endpoint tests in tests/AskLucy.Web.Tests/OperationalFailures/AdminProviderHealthRetentionEndpointTests.cs"
```

Once those are red, T015 (query), T016 (command) and T018 (frontend API) touch separate files and can proceed concurrently; T017 depends on T015/T016, and T019 depends on T018.

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete T001.
2. Complete the foundational phase T002–T011 **including the manual test-database migration**.
3. Complete US1 T012–T020.
4. **STOP and VALIDATE**: run the endpoint tests and the page tests, then use the page to change the value and reload it.
5. Deploy/demo the editable control if the job change can wait.

### Incremental Delivery

1. Foundation → US1 (admin can edit) → validate.
2. US2 (job honours the value) → validate with the job tests.
3. US3 (audit trail) → validate with the handler/endpoint assertions.
4. Polish, documentation and the full quickstart pass.

### Notes

- The shared test database keeps the singleton row between runs; every mutating test must restore it (30 days) or re-read its version.
- Do not add a create path: a missing singleton row is recovery-only and PUT must fail.
- Keep the existing response field names so a mid-deploy client does not break.
- Commit after each logical group; no feature branch per the repository's solo-developer workflow.
