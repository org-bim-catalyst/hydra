---

description: "Task list for 074 Admin Operational Failure Audit Trail"
---

# Tasks: Admin Operational Failure Audit Trail

**Input**: Design documents from `/specs/074-operational-failure-audit/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md) (D1–D18), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required. Every success criterion (SC-001 to SC-013) is defined by a test, and constitution §10 applies. The required cases are listed in [plan.md → Testing strategy](plan.md#testing-strategy). In each phase, write the test tasks first and confirm they fail before implementing.

**Organization**: Tasks are grouped by user story, in priority order:

1. **US1** (P1): calm user message plus an admin record.
2. **US2** (P1): bursts collapse into one incident.
3. **US3** (P2): triage, badge and root cause.
4. **US1b** (P2): Super-User-controlled content permission.
5. **US4** (P2): every engine reports.
6. **US5** (P3): retention and erasure.

US1b comes after US3 because its UI lives on the Roles page, not on the new page.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task in the same phase).
- **[Story]**: the user story. Setup, Foundational and Polish tasks have no story label.
- Paths are repository-relative. `ClientApp/` means `src/AskLucy.Web/ClientApp/`. `OF` in a path means `OperationalFailures`.

## Standing rules for every task

1. **Recording never hurts the caller** (FR-020, FR-021):
   - `IOperationalFailureRecorder.Record` is `void`, synchronous and never throws.
   - Nothing on a request or job path awaits the store.
   - Every recording site **logs first, then records**.
   - The writer and ingestor never call the recorder, so there is no recursion.
2. **Only system prose in `Reason`** (FR-011, FR-012, FR-012a):
   - Never pass `ex.Message` from a vendor, or a response body, prompt, reply, transcript, file name, password, 2FA code or token.
   - When all you have is an exception, pass `Exception = ex` plus a fixed short sentence.
3. **Exactly one occurrence per failure** (research D11). A site that records and then lets the exception propagate calls `ex.MarkOperationalFailureRecorded()` first.
4. **Never record** (FR-006a):
   - user input validation errors;
   - a plain `KeyNotFoundException`;
   - the platform's own 429s;
   - token expiry or refresh;
   - `OperationCanceledException` caused by the caller's or request's token.
5. **User-visible text** comes from `UserFacingFailureText` only (research D9). No non-admin string may contain: credential, key, quota, rate limit, billing, a provider or model name, or "administrator".
6. **Clean Architecture**:
   - Application never references EF Core (CS0234). Counter updates use `ExecuteUpdateAsync`, and the unique-violation catch lives in Persistence.
   - Controllers call only `ISender`.
7. **No silent failures** (constitution §2 VIII):
   - Backend: every catch logs, and either rethrows or returns a caller-visible failure.
   - Frontend: every query and mutation has an inline error with retry, or a toast. No floating promises.
8. **Options**: every property has a default. Never use `ValidateOnStart()`, which crashes the whole host.
9. **DI**:
   - No factory that resolves its own service type (the `sp => sp.GetRequiredService<T>()` self-reference). One hid a DI cycle that hung every chat turn. Forwarding an interface to an already-registered concrete singleton (T035) is allowed.
   - Background work opens its own `IServiceScopeFactory` scope and never shares the request `DbContext`.
10. **Migrations**:
    - Create with `dotnet ef migrations add AddOperationalFailureAudit --project src/AskLucy.Persistence --startup-project src/AskLucy.Web`.
    - The file has no BOM, `System` usings come first, and `Down()` fully reverses `Up()`.
    - Apply to **both** the test DB and the production DB by hand.
11. **Verification**:
    - Type-check with `npx tsc -b --noEmit`, not a bare `tsc`.
    - Run the **full** vitest suite: ChatPage and the admin pages have their own assertions.
    - `Web.Tests` needs `PERSISTENCE_TESTS_CONNECTION_STRING`, and uses a derived factory fixture, never `WithWebHostBuilder` per test.
    - Log assertions use `FakeLogger`. `Received().Log(...)` never matches `[LoggerMessage]`.
12. **jsdom traps**:
    - Inside open MUI dialogs or drawers, use `getByText`, not `getByRole`.
    - Stub pointer capture on the prototype.
    - Add an MSW handler for every endpoint a test renders.
13. **Enums** serialize as strings API-wide (already configured). Don't add per-property converters.

---

## Phase 1: Setup

**Purpose**: Options, configuration, and folder scaffolding. There are no new packages.

- [X] T001 [P] Create `src/AskLucy.Application/Options/OperationalFailuresOptions.cs`:
  - `SectionName = "OperationalFailures"`.
  - `ResolvedRetentionDays = 90`, `UnacknowledgedRetentionDays = 180`, `OccurrenceRetentionDays = 90`.
  - `MaxStoredOccurrencesPerIncident = 1000`, `QueueCapacity = 10000`, `WriterBatchSize = 100`.
  - A static `Normalize()` that clamps each value to ≥ 1 and returns the clamped copy (research D16).
- [X] T002 [P] Add an `"OperationalFailures"` section with the T001 defaults to `src/AskLucy.Web/appsettings.json`.
- [X] T003 [P] Create empty feature folders, each with a placeholder-free first file from later tasks:
  - `src/AskLucy.Domain/OperationalFailures/`
  - `src/AskLucy.Application/OperationalFailures/{Abstractions,Commands,Queries,Investigations,Jobs}/`
  - `src/AskLucy.Infrastructure/OperationalFailures/`
  - `src/AskLucy.Persistence/Configurations/OperationalFailures/`
  - `tests/*/OperationalFailures/`

  Just confirm the target paths. There is nothing to commit on its own.
- [X] T004 [P] Write `docs/adr/0017-operational-failure-trail.md` (constitution §13/§17), following the existing ADRs' format: context, decision, consequences, alternatives considered, trade-off accepted. It records three decisions:
  1. One cross-cutting store behind `IOperationalFailureRecorder`, instead of unioning the 8 module audit logs, a Serilog sink, or a synchronous write (research D1–D3).
  2. A bounded channel plus a background writer, accepting that a full channel drops a report (logged) rather than slowing the caller (D2), and why the content-access event is the one write that stays synchronous (D15).
  3. The built-in Administrator role excludes `SuperUserControlledKeys` from `PermissionSet.Full`: the first exception to "built-in ⇒ full catalogue" (D14).

  plan.md's Constitution Check (§13) and both Complexity Tracking rows already link to this path.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The Domain model, the store, and the non-blocking recording pipeline every story uses. It also includes the permission-catalogue keys and the resolver exception, because US1's investigation views need them.

**⚠️ No user-story work starts until this phase is complete.**

### Tests for Foundational (write first, confirm failing)

- [X] T005 [P] Domain tests in `tests/AskLucy.Domain.Tests/OperationalFailures/OperationalFailureIncidentTests.cs`:
  - Open→Acknowledged→Resolved, Open→Resolved and Resolved→Reopen (which clears the ack and resolve fields).
  - `Acknowledge` on Acknowledged or Resolved returns `AlreadyInState`.
  - `Resolve` note length ≤ 500.
  - A new occurrence never changes `TriageState`.
- [X] T006 [P] Domain tests in `tests/AskLucy.Domain.Tests/OperationalFailures/OperationalFailureSeverityPolicyTests.cs`: a theory over every FR-009 row.
  - The Critical kinds give Critical even with `DegradedServed`.
  - The `Access` engine always gives Warning.
  - `RateLimited` gives Warning.
  - `DegradedServed` / `RecoveredByRetry` give Warning.
  - Everything else gives Error.
- [X] T007 [P] Domain test in `tests/AskLucy.Domain.Tests/OperationalFailures/OperationalFailureKindsTests.cs`: for every `Enum.GetValues<AiProviderFailureKind>()`, `OperationalFailureKinds.FromProvider(k).ToString() == k.ToString()`.
- [X] T008 [P] Application tests in `tests/AskLucy.Application.Tests/OperationalFailures/FailureReasonSanitizerTests.cs`. This is the corpus from research D8:
  - `Bearer eyJ…`, a bare JWT, `sk-…`, `sk-ant-…`, `AIza…` and `xi-…` keys.
  - `Authorization:` / `Cookie:` / `Set-Cookie:` values.
  - `Server=…;Password=…;User ID=…`, `?key=`, `&api_key=`, `token=`, `sig=`.
  - A 40-character hex run and a 60-character base64 run.
  - Multi-line vendor JSON collapses to one line.
  - Output ≤ 500 characters with an ellipsis.
  - Plain system prose passes through unchanged.
- [X] T009 [P] Application tests in `tests/AskLucy.Application.Tests/OperationalFailures/FailureClassifierTests.cs`:
  - `AiProviderException` subtypes map to their kind.
  - `TimeoutException` and a `TaskCanceledException` whose token was **not** cancelled map to `TimedOut`.
  - A caller-cancelled `OperationCanceledException` returns `null`, meaning "do not record".
  - `HttpRequestException` / `SocketException` map to `DependencyUnreachable`.
  - Anything else maps to `UnexpectedError`, with the reason = the exception type name, never the message.
- [X] T010 [P] Application tests in `tests/AskLucy.Application.Tests/OperationalFailures/OperationalFailureKeysTests.cs`:
  - The grouping key is stable under case and whitespace.
  - It differs when any of engine, provider, model, kind, operation or subject differs.
  - It is identical across different chats, users or runs.
  - The root cause for a provider kind ignores engine, operation and subject.
  - The root cause for a non-provider kind is engine|kind|operation.
- [X] T011 [P] Application tests in `tests/AskLucy.Application.Tests/OperationalFailures/OperationalFailureIngestorTests.cs`, using a substituted `IOperationalFailureStore`:
  - The reason is sanitised before the store sees it.
  - Severity comes from the policy.
  - A null `CorrelationId` gets a generated id, logged via `FakeLogger`.
  - A store exception is logged with correlation id, engine and kind, and is not rethrown.
  - The ingestor never calls `IOperationalFailureRecorder`.
- [X] T012 [P] Infrastructure tests in `tests/AskLucy.Infrastructure.Tests/OperationalFailures/ChannelOperationalFailureRecorderTests.cs`, covering G1–G6 from [contracts/operational-failure-recorder.md](contracts/operational-failure-recorder.md):
  - `Record` returns in < 1 ms while the writer is blocked forever.
  - A full channel logs `OperationalFailureDropped` and returns.
  - A null report and a throwing logger do not throw.
  - The correlation id and time are captured at call time (set the accessor, call `Record`, change the accessor, and assert the first value).
- [X] T013 [P] Infrastructure tests in `tests/AskLucy.Infrastructure.Tests/OperationalFailures/OperationalFailureWriterServiceTests.cs`:
  - Batches are ≤ `WriterBatchSize`.
  - Each batch gets a new DI scope; assert two batches get different scoped instances.
  - A throwing batch is logged per report and the loop continues.
  - `StopAsync` drains for ≤ 5 s and logs the abandoned count.
  - _Implemented:_ the drain runs in `StopAsync` after `base.StopAsync`, not at the end of `ExecuteAsync`. On .NET 10 a host that stops before the framework starts `ExecuteAsync` never runs it, and the queue would be lost without a log line. An extra test covers that case. No `BatchFailed` event: each report of a failed batch gets its own `OperationalFailureRecordingFailed`/`VoiceRecoveryRecordingFailed` line.
- [X] T014 [P] Infrastructure tests in `tests/AskLucy.Infrastructure.Tests/OperationalFailures/CorrelationIdAccessorTests.cs`: the order is AsyncLocal, then `HttpContext.Items`, then null.
  - _Implemented in `tests/AskLucy.Web.Tests/Middleware/CorrelationIdAccessorTests.cs`:_ the accessor lives in Web (see T032).
- [X] T015 [P] Persistence tests in `tests/AskLucy.Persistence.Tests/OperationalFailures/OperationalFailureStoreTests.cs`, against real SQL Server:
  - The first append opens an incident. The second append with the same key joins it: `OccurrenceCount = 2`, `LastSeenUtc` = the max, `HighestSeverity` = the max.
  - 20 concurrent appends of one key from separate `DbContext`s produce exactly 1 incident with count 20.
  - After `Resolved`, an append opens a new incident with `RecurrenceOfIncidentId` set.
  - The cap: with `MaxStoredOccurrencesPerIncident = 3`, 5 appends give `StoredOccurrenceCount = 3` and `OccurrenceCount = 5`.
  - Participants: 5 appends from 2 users give `DistinctUserCount = 2`. Beyond the cap, a new user still increments the count.
  - Audit columns: the incident's `CreatedBy` is `"system"`, and a joined occurrence does **not** change `ModifiedAtUtc`, because counter updates are bookkeeping. `LastSeenUtc` is the activity timestamp.
  - _Written and compiling, not yet executed:_ the Persistence fixture wipes every table, and the only DB available is the shared dev/test DB, so these run only where `PERSISTENCE_TESTS_DEDICATED_DATABASE=1` is safe. The store is exercised end to end against real SQL Server by T036.
- [X] T016 [P] Application tests in `tests/AskLucy.Application.Tests/Authorization/EffectivePermissionResolverTests.cs` (extend the existing file):
  - Super User gets every key, including `admin.operational-failures.content.view`.
  - Administrator gets every key **except** content.view when the Administrator role has no stored grant, and gets it when the grant is stored.
  - A custom role with a stored content.view has it.
  - Built-in roles still get the other new keys automatically.

### Implementation for Foundational

- [X] T017 [P] Create the Domain enums in `src/AskLucy.Domain/OperationalFailures/`, following [data-model.md → Enums](data-model.md#enums-domain-asklucydomainoperationalfailures):
  - `OperationalFailureEngine.cs`
  - `OperationalFailureKind.cs`, plus a static class `OperationalFailureKinds` with a total-switch `FromProvider` and `IsProviderKind`
  - `OperationalFailureSeverity.cs` (`Warning = 1`, `Error = 2`, `Critical = 3`)
  - `OperationalFailureOutcome.cs`
  - `IncidentTriageState.cs`
  - `IncidentParticipantType.cs`
  - `InvestigatedItemType.cs`
- [X] T018 [P] Create `src/AskLucy.Domain/OperationalFailures/OperationalFailureSeverityPolicy.cs`, a static `Classify(engine, kind, outcome)` per research D5 (makes T006 pass).
- [X] T019 Create these entities in `src/AskLucy.Domain/OperationalFailures/`, each (except `IncidentParticipant`, a plain child row — see data-model) deriving from `BaseEntity` so the existing SaveChanges interceptor stamps the audit columns (constitution §5; [data-model.md → Audit columns](data-model.md#audit-columns)):
  - `OperationalFailureIncident.cs`: every column in [data-model.md](data-model.md#operationalfailureincidents-aggregate-root-the-only-mutable-part); `Open(...)` factory; `Acknowledge(userId, now)`, `Resolve(userId, now, note)` and `Reopen()` returning `IncidentTransitionResult { Applied, AlreadyInState }`; and `RowVersion`.
  - `OperationalFailureOccurrence.cs`: append-only, with a static `Create(...)`.
  - `IncidentParticipant.cs`
  - `UserContentAccessEvent.cs`: static `Record(...)`, no setters.

  This makes T005 pass.
- [X] T020 [P] In `src/AskLucy.Domain/Authorization/AdminArea.cs`, add `OperationalFailures`. In `src/AskLucy.Domain/Authorization/AdminPermissionCatalog.cs`:
  - Add `admin.operational-failures.view` (Read), `admin.operational-failures.manage` (Write) and `admin.operational-failures.content.view` (Read), with names and descriptions taken from spec FR-025 and FR-016e.
  - Add `public static IReadOnlySet<string> SuperUserControlledKeys = { "admin.operational-failures.content.view" }`.
  - Follow the existing manage-implies-view convention for the first two keys.
- [X] T021 Update `src/AskLucy.Application/Authorization/EffectivePermissionResolver.cs` per research D14:
  - Super User returns `PermissionSet.Full`.
  - Administrator returns `Full` minus `SuperUserControlledKeys`, plus the controlled keys stored on the built-in Administrator role's grants. Load them through `IRoleRepository.GetByNormalizedNameAsync("ADMINISTRATOR")`; this is the only extra round-trip, and only for Administrators.
  - Update the class doc comment to describe the one exception.

  This makes T016 pass. `UpdateRoleCommandHandler` refuses built-in roles, so add `SetControlledGrantsAsync(roleId, keys, actorUserId, ct)` to `IRoleRepository` and `src/AskLucy.Persistence/Repositories/RoleRepository.cs`. It replaces only the `SuperUserControlledKeys` grants on the role and writes a `RoleAuditLog` `RoleUpdated` row with before/after JSON. T084 uses it.
- [X] T022 [P] Create in `src/AskLucy.Application/OperationalFailures/Abstractions/`:
  - `IOperationalFailureRecorder.cs`: `Record` and `RecordRecovery`.
  - `OperationalFailureReport.cs`: the fields from [data-model.md → Application-level types](data-model.md#application-level-types-not-persisted), with **no** severity, body, password, code or token fields, plus internal `CorrelationId`/`OccurredAtUtc` set by the recorder.
  - `VoiceRecoveryReport.cs`
  - `IOperationalFailureStore.cs`: `AppendAsync(IncidentAppendRequest) → IncidentAppendResult`, plus `IncrementRecoveryAsync`, the read, transition, retention and anonymise methods. Declare these now; they are implemented in later phases.
  - `IUserContentAccessEventRepository.cs`: `AddAsync` and `AnonymizeOwnerAsync` only.
  - `OperationalFailureMarkers.cs`: `MarkOperationalFailureRecorded(this Exception)` and `IsOperationalFailureRecorded(this Exception)` via `ex.Data`.
  - _Implemented:_ only `AppendAsync` and `IncrementRecoveryAsync` are declared now. The read, transition, retention and anonymise methods are added with the phases that implement them, so no interface member is left unimplemented.
- [X] T023 [P] Create `src/AskLucy.Application/Abstractions/ICorrelationIdAccessor.cs` (`string? Current { get; }`).
- [X] T024 [P] Create `src/AskLucy.Application/OperationalFailures/FailureReasonSanitizer.cs`, pure and static, using `[GeneratedRegex(..., matchTimeoutMilliseconds: 100)]` patterns (research D8). A regex timeout returns `"[reason withheld]"` rather than throwing. Makes T008 pass.
- [X] T025 [P] Create `src/AskLucy.Application/OperationalFailures/FailureClassifier.cs`, `IFailureClassifier` plus its implementation: `OperationalFailureKind? Classify(Exception ex, CancellationToken callerToken)` and `string FallbackReason(Exception ex)`. Makes T009 pass.
- [X] T026 [P] Create `src/AskLucy.Application/OperationalFailures/OperationalFailureKeys.cs`: `Grouping(report)` and `RootCause(report)`, SHA-256 lowercase hex over the normalised `|`-joined parts. Makes T010 pass.
- [X] T027 Create `src/AskLucy.Application/OperationalFailures/OperationalFailureIngestor.cs`:
  - `IngestAsync(IReadOnlyList<OperationalFailureReport>, CancellationToken)` does sanitise → classify severity → keys → `store.AppendAsync`, one report at a time.
  - It catches per report and logs via a new `[LoggerMessage]` class `OperationalFailureLog.cs` in the same folder, with events `OperationalFailureRecorded` (Debug), `OperationalFailureRecordingFailed` (Error) and `OperationalFailureDropped` (Warning).
  - It returns the `IncidentAppendResult`s for D18.

  Makes T011 pass.
- [X] T028 Create `src/AskLucy.Persistence/Configurations/OperationalFailures/`:
  - `OperationalFailureIncidentConfiguration.cs`
  - `OperationalFailureOccurrenceConfiguration.cs`
  - `IncidentParticipantConfiguration.cs`
  - `UserContentAccessEventConfiguration.cs`

  Include every column type, index and filtered index exactly as in [data-model.md](data-model.md): the `UX_Incidents_GroupingKey_Unresolved` filter `[TriageState] <> N'Resolved'` and the badge filter `[TriageState] = N'Open'`. Enums use `HasConversion<string>().HasMaxLength(40)`. Only `Incident→Occurrence` and `Incident→Participant` have foreign keys, both cascade. Add the four `DbSet`s to `src/AskLucy.Persistence/AskLucyDbContext.cs`.
- [X] T029 Create `src/AskLucy.Persistence/Repositories/OperationalFailureStore.cs` implementing `AppendAsync` per research D3:
  - Find the open incident by `GroupingKey`, or insert one (setting `RecurrenceOfIncidentId` from the latest resolved incident with that key).
  - Catch a `DbUpdateException` whose inner `SqlException.Number` is 2601 or 2627, detach, re-read and join.
  - Counters use `ExecuteUpdateAsync`: `OccurrenceCount + 1`, `LastSeenUtc`/`HighestSeverity` via `CASE`, `LatestReason`, `LatestCorrelationId`, and `StoredOccurrenceCount + 1` only when under the cap. These counter updates are bookkeeping writes. Like the repo's 6 existing `ExecuteUpdateAsync` uses, they do not touch `ModifiedAtUtc`/`ModifiedBy`; the audit columns belong to the SaveChanges interceptor (constitution §5).
  - Insert the occurrence only when under the cap.
  - Use `INSERT … WHERE NOT EXISTS` for participants via `ExecuteSqlInterpolatedAsync`, and increment the distinct counts only when a row was inserted.
  - Register it in the Persistence DI.

  Makes T015 pass.
- [X] T030 Give the role and MCP audit logs a correlation id (FR-006b):
  - Add `ICorrelated { string? CorrelationId { get; } }` to `src/AskLucy.Domain/Common/`, and implement it on `src/AskLucy.Domain/Authorization/RoleAuditLog.cs` and `src/AskLucy.Domain/Mcp/McpAuditLog.cs` with a private setter.
  - Stamp it once, centrally: the existing SaveChanges audit interceptor in `src/AskLucy.Persistence/` sets `CorrelationId` from `ICorrelationIdAccessor.Current` on every **added** `ICorrelated` entity whose value is null. This covers the 4 `RoleRepository`, 2 `RoleAssignmentRepository`, `PermissionDeniedAuditResultHandler`, `PermissionCatalogReconciler` and 10 MCP handler call sites without editing them.
  - Map `CorrelationId nvarchar(64) NULL` with a nonclustered index in both existing configurations, so T031's migration includes it.
  - Test in `tests/AskLucy.Persistence.Tests/OperationalFailures/AuditLogCorrelationTests.cs`: with the accessor returning `"abc"`, a saved `RoleAuditLog` and `McpAuditLog` both read back `"abc"`; with null, they stay null.
  - _Test written and compiling, not yet executed:_ it needs the dedicated persistence DB (see T015). `PersistenceTestFixture.CreateAuditedDbContext` adds the interceptor, which the bare fixture context lacks.
- [X] T031 Create the migration `src/AskLucy.Persistence/Migrations/<ts>_AddOperationalFailureAudit.cs`, using the command in standing rule 10:
  - Check there is no BOM (`xxd … | head -1`) and that `System` usings come first.
  - Run `dotnet ef migrations has-pending-model-changes`; it must report none.
  - Apply to the shared **test** DB (the `appsettings.Development.json` `DefaultConnection`).
  - Confirm `dotnet ef migrations list … | grep -c '(Pending)'` returns 0.
  - _Done:_ `20260925231854_AddOperationalFailureAudit`, applied to the shared test DB, 0 pending.
- [X] T032 [P] Create `src/AskLucy.Infrastructure/OperationalFailures/CorrelationIdAccessor.cs`:
  - A static `AsyncLocal<string?> JobCorrelationId`, used by T097 (the Hangfire job filter).
  - Otherwise `IHttpContextAccessor.HttpContext?.Items[CorrelationIdKeys.ItemsKey]`, whose value stays `"X-Correlation-Id"`, the key the middleware writes today via `HeaderName`.
  - Put the `HttpContext.Items` key in `src/AskLucy.Application/Abstractions/CorrelationIdKeys.cs` (`public const string ItemsKey`), and change `src/AskLucy.Web/Middleware/CorrelationIdMiddleware.cs` to use it. Infrastructure can't reference Web.

  Makes T014 pass.
  - _Implemented differently:_ Infrastructure has no ASP.NET Core framework reference, so `CorrelationIdAccessor` is in `src/AskLucy.Web/Middleware/` next to `HttpContextCurrentUserAccessor` and is registered in `Program.cs`. The `AsyncLocal` is `src/AskLucy.Infrastructure/OperationalFailures/JobCorrelationContext.cs`, so the Hangfire filter (T097) can set it.
- [X] T033 Create `src/AskLucy.Infrastructure/OperationalFailures/ChannelOperationalFailureRecorder.cs`, a singleton per research D2:
  - `Channel.CreateBounded` with `FullMode = Wait`, `SingleReader = true`; writes use `TryWrite`.
  - Stamp the correlation id and time on the caller's thread.
  - Wrap everything in `try/catch` that logs.
  - Expose `ChannelReader` to the writer through an internal `IOperationalFailureQueue`.

  Makes T012 pass.
- [X] T034 Create `src/AskLucy.Infrastructure/OperationalFailures/OperationalFailureWriterService.cs`, a `BackgroundService`:
  - Use `ReadAllAsync` batching up to `WriterBatchSize`.
  - Per batch, call `IServiceScopeFactory.CreateAsyncScope()`, then `OperationalFailureIngestor.IngestAsync`.
  - Per batch, `catch (Exception)` → log and continue.
  - In `StopAsync`, drain with a 5 s timeout and log the abandoned count.

  Makes T013 pass.
- [X] T035 Register in `src/AskLucy.Infrastructure/DependencyInjection.cs` and `src/AskLucy.Application/DependencyInjection.cs`:
  - `Configure<OperationalFailuresOptions>(section)`, with **no** `ValidateOnStart`.
  - The singleton recorder, registered once as the concrete type and then `AddSingleton<IOperationalFailureRecorder>(sp => sp.GetRequiredService<ChannelOperationalFailureRecorder>())`. This is **not** a cycle: document why. Also `AddSingleton<IOperationalFailureQueue>(sp => sp.GetRequiredService<ChannelOperationalFailureRecorder>())`, so the writer reads the **same** channel the recorder writes. T036 covers both registrations.
  - The `AddHostedService<OperationalFailureWriterService>()`.
  - Scoped `OperationalFailureIngestor`, `IOperationalFailureStore`, `IUserContentAccessEventRepository`, singleton `IFailureClassifier`, singleton `ICorrelationIdAccessor`.
- [X] T036 [P] Web.Tests smoke test in `tests/AskLucy.Web.Tests/OperationalFailures/OperationalFailureHostTests.cs`:
  - The host boots.
  - `IOperationalFailureRecorder` and `IOperationalFailureQueue` both resolve, to the same `ChannelOperationalFailureRecorder` instance (`Assert.Same`).
  - A `Record` call appears as an incident within 5 s (poll the DB).

  This proves the whole pipeline end to end and catches the "required option crashes the host" trap.

**Checkpoint**: A report passed to `Record` lands as a grouped incident, the permission keys exist, and Administrators no longer hold content.view implicitly.

---
  - _Note:_ the store commits a new incident before joining its first occurrence (research D3), so a reader can briefly see `OccurrenceCount = 0`. The test polls for a joined incident, and the US1 list query must hide zero-count rows.

## Phase 3: User Story 1 — Calm user message, precise admin record (Priority: P1) 🎯 MVP

**Goal**: A chat failure, whether before or during streaming, shows the user one calm sentence. An admin sees one Critical record with the corrective-action link and a role-gated chat view.

**Independent Test**: With an invalid provider key, a non-admin chat turn fails. The user-visible text passes the SC-001 word list, and exactly one Critical incident appears with the right kind, provider, user, chat and correlation id. Quickstart S1–S4 and S7.

### Tests for User Story 1 (write first, confirm failing)

- [X] T037 [P] [US1] Web test in `tests/AskLucy.Web.Tests/OperationalFailures/UserFacingFailureTextTests.cs`. For every `AiProviderFailureKind`, as a **non-admin**, force the kind through a substituted `IAIProvider`:
  - (a) before the stream: the Problem Details `detail` is one of the two `UserFacingFailureText` sentences, and there is no `providerFailure` extension;
  - (b) mid-stream: the appended notice and the `__TURN_OUTCOME__` reason are generic;
  - (c) on the next turn: the `RecentTurnOutcomeSummary` passed to the provider contains only the generic sentence.

  Assert that none contains the SC-001 word list. Repeat (a) as an Administrator and assert today's classified detail plus `providerFailure` (FR-003).
- [X] T038 [P] [US1] Web test in `tests/AskLucy.Web.Tests/OperationalFailures/ChatFailureRecordingTests.cs`:
  - A `CredentialRejected` before the stream gives one occurrence (Engine Chat, kind CredentialRejected, Critical), with `CorrelationId` == the response `traceId` and the correct `UserId`/`ChatId`.
  - The same mid-stream gives the classified kind, **not** `UnexpectedError` (FR-004), and exactly one occurrence, because the marked exception is not re-recorded by the middleware.
  - A `ValidationException` and a caller cancel give 0 occurrences.
- [X] T039 [P] [US1] Application tests in `tests/AskLucy.Application.Tests/OperationalFailures/CorrectiveActionCatalogTests.cs`:
  - Every `OperationalFailureEngine` × `OperationalFailureKind` combination returns non-empty `Text`.
  - The credential kinds for Chat, AiProvider and Embeddings give `/admin/ai-providers?select={providerId}`; for Voice, `/admin/voice?select=…`.
  - `RateLimited`/`Unavailable`/`TimedOut` give the "No action needed unless this persists" text with no route.
  - BackgroundJob gives `AdminAction = OpenJobsDashboard`.
- [X] T040 [P] [US1] Application tests in `tests/AskLucy.Application.Tests/OperationalFailures/IncidentQueriesTests.cs`: `ListIncidentsQuery`, `GetIncidentQuery` and `ListOccurrencesQuery` handlers map to the contract shapes, including `UserRef.status` Active/Deleted/Erased, the `subject.deleted` flag and the `providerHealth` projection (FR-017).
- [X] T041 [P] [US1] Application tests in `tests/AskLucy.Application.Tests/OperationalFailures/ChatInvestigationTests.cs`:
  - An incident not referencing the chat gives `KeyNotFoundException`.
  - Without content.view: metadata plus `failurePoints`, `transcript == null`, and no access event.
  - With content.view and the viewer ≠ owner: the transcript with `isFailedTurn` set, and exactly one `UserContentAccessEvent` added **before** the transcript loads (assert call order).
  - With content.view and the viewer == owner: no event.
  - If the access-event insert throws, the handler throws and returns no content.
  - A soft-deleted chat gives `deleted: true` and no transcript.
- [X] T042 [P] [US1] Web test in `tests/AskLucy.Web.Tests/OperationalFailures/AdminOperationalFailuresEndpointsTests.cs`:
  - `GET incidents`, `incidents/{id}`, `incidents/{id}/occurrences` and `incidents/{id}/chats/{chatId}` return 403 without view, as an ordinary user and as a custom role lacking it.
  - They return 200 with view.
  - The chat investigation JSON has `transcript: null` for an Administrator (no grant) and non-null for a Super User (SC-009).
  - There is no POST/PUT/DELETE route under `incidents/{id}/chats`: a POST returns 404 or 405.
- [X] T043 [P] [US1] Frontend tests in `ClientApp/src/features/admin/pages/AdminOperationalFailuresPage.test.tsx`, with MSW handlers for every new endpoint:
  - The list renders severity, engine, provider/model, kind, counts and last seen.
  - Opening a row shows the drawer with the reason, correlation id (copy button), user link, chat link and corrective-action link to `/admin/ai-providers?select=…`.
  - A list fetch 500 shows an inline error with retry.
  - An Access-engine incident also shows its distinct-source and distinct-account counts (FR-012a).
  - The user link points to `/admin/users?search={email}`.
- [X] T044 [P] [US1] Frontend tests in `ClientApp/src/features/admin/pages/investigations/ChatInvestigationPage.test.tsx`:
  - With `transcript: null` it renders metadata and failure points and the text "Content is visible only to staff with *View user content*".
  - With a transcript it renders messages with the failed turn highlighted.
  - There is no composer, textbox or edit/delete/download control, asserted with `queryByRole('textbox')` null.
  - `deleted: true` renders "This chat was deleted".
- [X] T045 [P] [US1] Frontend tests for the deep-link targets US1 renders:
  - `ClientApp/src/features/admin/pages/AdminAiProvidersPage.test.tsx` (extend): `?select=<id>` selects and scrolls to that provider on mount; an unknown id shows an inline "no longer exists" note.
  - `ClientApp/src/features/admin/pages/AdminUsersPage.test.tsx` (extend): `?search=<email>` pre-fills the search box and the list request carries it (FR-016).

### Implementation for User Story 1

- [X] T046 [P] [US1] Create `src/AskLucy.Application/OperationalFailures/UserFacingFailureText.cs` with the `Retry` and `Later` constants and `For(OperationalFailureKind?)`. `Later` is for NotConfigured, CredentialRejected, CredentialUnreadable, QuotaExhausted and UsageRestricted; `Retry` is for everything else, including null (research D9).
- [X] T047 [US1] In `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs`:
  - `MapProviderFailure` (≈L414): the **non-administrator** `detail` becomes `UserFacingFailureText.For(kind)`. Leave the administrator branch, status, title and `providerFailure` extension unchanged.
  - Add the boundary recording: for an exception that is not `IsOperationalFailureRecorded()`, is not a validation/`KeyNotFoundException`/request-aborted cancel, and maps to a system-side failure, call `recorder.Record(...)` **after** the existing log line. Use `Engine` = `Chat` for `/api/v1/ai/*` routes and `AiProvider` otherwise, `Operation` from the endpoint display name, the classifier's kind, and `Exception = ex`.
  - Also replace any other non-admin detail string that names a cause.
- [X] T048 [US1] In `src/AskLucy.Web/Controllers/v1/AiController.cs`:
  - `DescribeTurnFailure` (≈L649) returns `UserFacingFailureText.For(classifier.Classify(ex, ct))`.
  - In the mid-stream `catch` (≈L342–400), after the existing log, call `recorder.Record(new() { Engine = Chat, Operation = "Chat reply", Kind = …, Outcome = Failed, ProviderId, ProviderName, Model, UserId, ChatId, MessageId, Exception = ex })`, then `ex.MarkOperationalFailureRecorded()` if it rethrows.
  - The `RecordedTurnOutcome.Reason` and streamed notice use the generic sentence.

  Makes T037 and T038 pass.

  _Done:_ `Operation` is `"{METHOD} /{route template}"` rather than the endpoint display name, so ids in the URL never split one failure into several incidents. The middleware records when the exception is a provider failure or maps to ≥ 500. `DescribeTurnFailure` was removed; the mid-stream catch uses `UserFacingFailureText.For(kind)` directly and records without marking, because nothing is rethrown. The `AiCapabilityNotConfiguredException` detail is now `Later` for non-admins; admins keep the old prose. T037 (c) asserts the persisted `TurnOutcomeJson` `failureReason`, which is the exact input `RecentTurnOutcomeSummary` reads on the next turn.
- [X] T049 [P] [US1] Create `src/AskLucy.Application/OperationalFailures/CorrectiveActionCatalog.cs` following the [data-model.md](data-model.md#application-level-types-not-persisted) mapping table (`CorrectiveAction { Text, AdminRoute?, AdminAction? }`). Makes T039 pass. _Done:_ `For` also takes an optional `accountEmail` (Access → `/admin/users?search=`). Workflow/Agent/DocumentProcessing return "Open the item…" with no route; the investigation links live on each occurrence.
- [X] T050 [P] [US1] Create `src/AskLucy.Application/OperationalFailures/OperationalFailureDtos.cs` with `IncidentSummaryDto`, `IncidentDetailDto`, `OccurrenceDto`, `UserRefDto`, `BulkTransitionResultDto`, `ChatInvestigationDto`, `WorkflowRunInvestigationDto` and `DocumentInvestigationDto`, exactly per [contracts/admin-operational-failures.md → Shapes](contracts/admin-operational-failures.md#shapes).
- [X] T051 [US1] Add these read methods to `IOperationalFailureStore` and implement them in `OperationalFailureStore`:
  - `ListIncidentsAsync(IncidentFilter, page, pageSize)`, with every filter from the contract: user via `EXISTS` on participants, `state=Unresolved` by default, ordered by `LastSeenUtc DESC`, returning a total count.
  - `GetIncidentAsync`
  - `ListOccurrencesAsync(incidentId, page, pageSize)`
  - `IncidentReferencesAsync(incidentId, InvestigatedItemType, itemId)`
- [X] T052 [US1] Create these queries (query, validator and handler) under `src/AskLucy.Application/OperationalFailures/Queries/`:
  - `ListIncidents/`: validator for `from ≤ to`, `pageSize ≤ 100` and enum values.
  - `GetIncident/`: resolves users through `IIdentityService`, provider health through the AI provider repository, and the corrective action.
  - `ListOccurrences/`

  Makes T040 pass.

  _Done:_ users and item labels resolve through a new batch `IOperationalFailureReferenceLookup` (one query per kind, soft-delete filters ignored) rather than `IIdentityService`, which has no batch lookup; `OperationalFailureReadModelBuilder` shares the mapping between the three handlers. A user that no longer resolves is `Erased`; an item that no longer resolves is `deleted`. `providerHealth` is null when `ProviderId` is not an AI provider (voice providers). `OperationalFailuresManage` joined the catalogue constants for `canManage`.
- [X] T053 [US1] Create `src/AskLucy.Application/OperationalFailures/Investigations/GetChatInvestigation/` (query and handler) per research D15:
  - Metadata from the chat repository, read-only, ignoring the ownership guard. This is the only intended bypass, gated by the incident reference.
  - The `content.view` check via `ICurrentUserPermissions` (or the existing permission accessor).
  - `IUserContentAccessEventRepository.AddAsync` plus the unit-of-work save **before** loading messages.

  Makes T041 pass.

  _Done:_ the store method is named `FindItemReferencesAsync` (null = not referenced). Metadata comes from a new content-free `IMessageRepository.ListOutlineByChatIdAsync` (`MessageOutline`), so message count and turn numbers need no content read; content is loaded only after the access event is saved, and only when viewer ≠ owner is an event written. The permission check uses `IEffectivePermissionResolver` on the viewer's stored role.
- [X] T054 [US1] Create `src/AskLucy.Web/Controllers/v1/AdminOperationalFailuresController.cs`:
  - `[ApiController]`, `[EnableRateLimiting("admin-endpoints")]`, `[Route("api/v1/admin/operational-failures")]`.
  - `GET incidents`, `incidents/{id:guid}`, `incidents/{id:guid}/occurrences` and `incidents/{incidentId:guid}/chats/{chatId:guid}`, each `[RequirePermission("admin.operational-failures.view")]`, calling only `ISender`.
  - Every action documents its status codes for OpenAPI with `[ProducesResponseType]` (200 with the DTO type, 403, 404; 409 where it applies), as `AdminAiProvidersController` does (constitution §6). This applies to every endpoint later tasks add to this controller.

  Makes T042 pass.

  _Done:_ the transcript tests seed real Identity users (Administrator, Super User, owner) because the handler resolves permissions from the stored role, not the token.
- [X] T055 [P] [US1] Create `ClientApp/src/features/admin/api/adminOperationalFailuresApi.ts`: typed functions and TS types mirroring the contract shapes, and TanStack Query keys under `['admin','operational-failures',…]`.
- [X] T056 [P] [US1] In `ClientApp/src/features/admin/adminPermissions.ts`, mirror the three new keys and `SUPER_USER_CONTROLLED_KEYS`. In `ClientApp/src/features/admin/adminNav.tsx`, add `{ path: '/admin/operational-failures', label: 'Operational failures', icon: <ReportProblemOutlinedIcon fontSize="small" />, permission: 'admin.operational-failures.view' }`.
- [X] T057 [US1] Create `ClientApp/src/features/admin/components/operationalFailures/`:
  - `IncidentTable.tsx`: server-paged, with severity chip, engine, provider/model, kind, `occurrenceCount` ("showing N of M" when capped), users, last seen, and state.
  - `IncidentDrawer.tsx`: reason, correlation id with copy, provider health, sample users (each linking to `/admin/users?search={email}`), occurrences table and corrective action. For Access incidents it also shows `distinctSourceCount` and `distinctUserCount` as "N sources · M accounts" (FR-012a).
  - `OccurrenceTable.tsx`: paged, with links rendering "deleted"/"erased user".
  - `CorrectiveActionLink.tsx`: `adminRoute` renders a `RouterLink`; `OpenJobsDashboard` reuses the nav's Hangfire action.

  Each fetch shows an inline error with a retry button.
  _Done:_ plus shared `operationalFailureLabels.ts` and `UserRefLink.tsx`. Workflow/document/agent/MCP items render as text until their investigation pages land in US4.
- [X] T058 [US1] Create `ClientApp/src/features/admin/pages/AdminOperationalFailuresPage.tsx`, rendered inside `AdminShell`, with the default view (unresolved, last 7 days). Add routes in `ClientApp/src/routes/router.tsx` for `/admin/operational-failures` and `/admin/operational-failures/:incidentId/chats/:chatId`, using the same guard pattern as the other admin routes. Makes T043 pass.
  _Done:_ both routes use `<AdminRoute permission="admin.operational-failures.view">` so custom roles with view reach them. The filter toolbar is T076 (US3); US1 ships the fixed default view with paging.
- [X] T059 [US1] Create `ClientApp/src/features/admin/pages/investigations/ChatInvestigationPage.tsx`: read-only; reuses the chat message markdown renderer used by `features/chat/components/MessageBubble.tsx` without any action props; no composer. Makes T044 pass.
- [X] T060 [US1] Deep-link handling for the pages US1 links to:
  - `ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx`: `?select=`, read once on mount; select and scroll to the provider, and show an inline note for an unknown id.
  - `ClientApp/src/features/admin/pages/AdminUsersPage.tsx`: `?search=`, if it does not already read it; pre-fill the search box.

  Makes T045 pass.
  _Done:_ the selected row gets `selected` and scrolls once via a callback ref (`scrollIntoView?.` — jsdom has none).

**Checkpoint**: US1 is independently demonstrable (quickstart S1–S4, S7). This is the MVP.

---

## Phase 4: User Story 2 — Bursts collapse into one incident (Priority: P1)

**Goal**: The 2026-09-22 ElevenLabs burst shows as 1 incident, with 7 occurrences, 7 recoveries and 1 user. Recurrence after resolve opens a new incident; recurrence after acknowledge does not re-flag.

**Independent Test**: The SC-003 replay test (quickstart S5).

### Tests for User Story 2 (write first, confirm failing)

- [X] T061 [P] [US2] Web test in `tests/AskLucy.Web.Tests/OperationalFailures/ElevenLabsBurstReplayTests.cs`: drive `TextToSpeechStreamer` with a substituted ElevenLabs engine that throws `AiProviderCredentialRejectedException` and a fallback engine that succeeds, 7 failovers then 7 recoveries within 70 s (fake `TimeProvider`). Assert:
  - 1 incident, Critical, Voice, kind CredentialRejected.
  - `OccurrenceCount = 7`, `RecoveryCount = 7`, `DistinctUserCount = 1`.
  - Every occurrence `IsFailover`.
  - The `summary` endpoint increased by exactly 1.
  - `VoiceProviderFailoverEvents` still has its 14 rows, unchanged.
  - *Done 2026-09-26*: drives the real `VoiceProviderRouter` + `VoiceFailureReporter` + `ChannelOperationalFailureRecorder` into the real ingestor/store (the router, not `TextToSpeechStreamer`, is where engines fail over — see T064). The first four assertions pass. The `summary`-endpoint assertion is deferred to US3 (the endpoint does not exist yet, T069), and the `VoiceProviderFailoverEvents` assertion is dropped: the router's per-engine failover never wrote those rows (only the streamer's whole-voice give-up does), so there were never 14 to preserve.
- [X] T062 [P] [US2] Persistence tests in `tests/AskLucy.Persistence.Tests/OperationalFailures/OperationalFailureGroupingTests.cs`:
  - The same key across 3 users gives 1 incident with `DistinctUserCount = 3`.
  - Acknowledge, then a new occurrence, leaves the state `Acknowledged` (not re-flagged).
  - Resolve, then a new occurrence, opens a new incident with `RecurrenceOfIncidentId`.
  - `IncrementRecoveryAsync` with no open incident is a no-op.
  - The same kind with a different model gives 2 incidents.
  - The same kind with a different operation gives 2 incidents.
  - *Done*: 7 tests, gated like the rest of the persistence suite (compiled, skipped without the dedicated-database flag).

### Implementation for User Story 2

- [X] T063 [US2] Implement `IncrementRecoveryAsync(groupingKey)` in `src/AskLucy.Persistence/Repositories/OperationalFailureStore.cs`: `ExecuteUpdateAsync` `RecoveryCount + 1` on the unresolved incident for the key, returning 0 rows affected when there is none. Add `RecordRecovery` handling to `ChannelOperationalFailureRecorder` and to `OperationalFailureIngestor`, which computes the key from `VoiceRecoveryReport` with the same inputs as the failover report. *Already delivered in Foundational (T027 ingestor, T029 store, T033 recorder).*
- [X] T064 [US2] In `src/AskLucy.Application/Ai/TextToSpeechStreamer.cs`:
  - At the failover site (≈L44), after the existing `VoiceProviderHealthRecorder` call, call `recorder.Record(new() { Engine = Voice, Operation = "Text-to-speech", Kind = classifier.Classify(ex, cancellationToken) ?? UnexpectedError, Outcome = fallbackServed ? DegradedServed : Failed, ProviderName, Model, UserId, IsFailover = true, Exception = ex, Reason = "Text-to-speech request failed" })`.
  - Stop passing `Truncate(ex.Message)` into anything user-visible.
  - At the recovery site (≈L67), call `recorder.RecordRecovery(...)` with the same provider, model, kind and operation. Keep the kind of the last failover, held for the session.
  - *Design deviation (done)*: the engine-to-engine failover and the recovery happen in `VoiceProviderRouter`, not the streamer — the streamer only sees the router's final give-up. Reporting therefore lives in a new `IVoiceFailureReporter` (`src/AskLucy.Application/Ai/VoiceFailureReporter.cs`) called by the router: a failover records Engine=Voice with the classified kind and `DegradedServed`/`Failed`; an engine that serves again records the recovery. The failover's kind and model are held in the singleton `VoiceFailoverMemory` keyed by (user, operation, provider), so a failover in one request pairs with a recovery in a later one. An all-engines failure is recorded once, and the thrown exception is marked recorded. The streamer's `Truncate(ex.Message)` is replaced by `FailureReasonSanitizer.Sanitize`.
- [X] T065 [US2] In `src/AskLucy.Application/Ai/Commands/CreateSpeechToTextSession/CreateSpeechToTextSessionCommandHandler.cs` (≈L48), do the same, with `Operation = "Transcription"`. *Done through the same `IVoiceFailureReporter`; `ISpeechToTextSessionProvider` gained `ProviderName` for the record.*
- [X] T066 [US2] Show `recoveryCount` ("recovered N×") and `isRecurrence` ("Recurrence of an earlier resolved incident", linked) in `ClientApp/src/features/admin/components/operationalFailures/IncidentTable.tsx` and `IncidentDrawer.tsx`, and extend `AdminOperationalFailuresPage.test.tsx` for both.

**Checkpoint**: SC-003 passes. US1 and US2 together are the P1 deliverable.

---

## Phase 5: User Story 3 — Triage: filter, paginate, acknowledge, resolve, badge (Priority: P2)

**Goal**: Filters, transitions with concurrency, root-cause bulk actions, and a nav badge of distinct unacknowledged-Critical root causes.

**Independent Test**: Seeded incidents across every filter dimension; the transitions and 409; the 40-document root cause (badge 1, Resolve all clears it). Quickstart S6.

### Tests for User Story 3 (write first, confirm failing)

- [X] T067 [P] [US3] Application tests in `tests/AskLucy.Application.Tests/OperationalFailures/IncidentTransitionTests.cs`:
  - Acknowledge, Resolve (with note) and Reopen handlers record who and when.
  - A stale `rowVersion` gives a conflict exception that maps to 409.
  - *Done 2026-09-26*: per research D19 the conflict is a transition whose precondition no longer holds, not a stale `rowVersion`; the tests drive every `IncidentTransitionStatus` through `IncidentTriageService`.
  - Reopen while a newer unresolved incident holds the key gives 409 with `newerIncidentId`.
  - The bulk root-cause Resolve over 40 incidents gives `succeeded = 40`. With 1 already resolved: `skipped = 1`. With 1 concurrently changed: `failed = [{…}]`, and the others still succeed.
- [X] T068 [P] [US3] Persistence tests in `tests/AskLucy.Persistence.Tests/OperationalFailures/OperationalFailureTriageQueryTests.cs`:
  - Each filter (time, severity, engine, provider, kind, user, state) alone and combined.
  - Stable paging.
  - `CountUnacknowledgedCriticalRootCausesAsync`: 40 Critical open incidents sharing one root cause plus 1 other give 2; acknowledging all 40 gives 1; Warning incidents are never counted.
  - `ListRelatedAsync` excludes the incident itself and resolved ones.
  - *Done 2026-09-26*: also covers multi-value OR within a filter and the reopen collision (`NewerIncidentOpen`). Gated, so compiled but not run against the shared database. Each test scopes itself by a unique provider name or root cause, and the badge assertions are deltas.
- [X] T069 [P] [US3] Web tests in `tests/AskLucy.Web.Tests/OperationalFailures/AdminOperationalFailuresTriageEndpointsTests.cs`:
  - The transition endpoints return 403 with view-only and 200 with manage.
  - `summary` returns 403 without view.
  - A 409 body has `type: …/incident-conflict`.
  - N = 1,000 root-cause Resolve completes and the badge becomes 0 (SC-011).
  - *Done 2026-09-26*: the database is shared, so "the badge becomes 0" is asserted as "this root cause has no Open incident left"; a global count of 0 would depend on every other test. The N = 1,000 resolve runs well inside a minute.
- [ ] T070 [P] [US3] Frontend tests in `ClientApp/src/features/admin/components/AdminShell.test.tsx` (extend) and `ClientApp/src/features/admin/pages/AdminOperationalFailuresPage.test.tsx` (extend):
  - The badge shows the count, is hidden at 0, and shows an error dot with a tooltip on a fetch error.
  - It refetches after a transition mutation (use fake timers for the 60 s interval).
  - The filter bar writes URL params and they round-trip on reload.
  - Acknowledge/Resolve/Reopen buttons are hidden without manage; Reopen shows only on a resolved incident.
  - A 409 shows "This incident was changed by someone else — reloaded." and refetches.
  - "39 other incidents share this cause" leads to a related list, and Resolve all shows the result summary toast.

### Implementation for User Story 3

- [X] T071 [US3] Add these to `IOperationalFailureStore` and `OperationalFailureStore`:
  - `GetForTransitionAsync(id)`, which returns a tracked aggregate.
  - `SaveTransitionAsync(incident, expectedRowVersion)`, which maps a concurrency conflict to an Application `IncidentConflictException` (research D3, no EF type leaking).
  - `ListOpenByRootCauseAsync(rootCauseKey)`
  - `ListRelatedAsync(id, page, pageSize)`
  - `CountUnacknowledgedCriticalRootCausesAsync()`
  - `HasOtherUnresolvedAsync(groupingKey, excludingId)`
  - *Done 2026-09-26, differently*: research D19. The four transition methods became `TransitionAsync(ids, transition)` (batched, state-based, returns an `IncidentTransitionOutcome` per id) and `ListUnresolvedIdsByRootCauseAsync`. The list filters became multi-valued.
- [X] T072 [US3] Create command, validator and handler for each under `src/AskLucy.Application/OperationalFailures/Commands/`:
  - `AcknowledgeIncident/`
  - `ResolveIncident/` (note ≤ 500)
  - `ReopenIncident/`
  - `AcknowledgeRootCause/`
  - `ResolveRootCause/`

  The bulk commands iterate per incident, each with its own reload and save, collecting a `BulkTransitionResultDto`. Add `IncidentConflictException` handling to `ProblemDetailsMiddleware` (409, `type` suffix `incident-conflict`, and a `newerIncidentId` extension). Makes T067 pass.
- [X] T073 [US3] Create `src/AskLucy.Application/OperationalFailures/Queries/ListRelatedIncidents/` and `Queries/GetSummary/`. Add `relatedOpenCount` to the list and detail projections. Makes T068 pass.
- [X] T074 [US3] Add the transition, related and summary endpoints to `src/AskLucy.Web/Controllers/v1/AdminOperationalFailuresController.cs`, per the [contract](contracts/admin-operational-failures.md#incidents): manage for `actions/*`, view for `related` and `summary`. Makes T069 pass.
- [ ] T075 [P] [US3] Create `ClientApp/src/features/admin/hooks/useOperationalFailureBadge.ts`: `useQuery` with `refetchInterval: 60_000`, enabled only when the caller holds view, and exposing `{ count, isError }`. In `ClientApp/src/features/admin/adminNav.tsx`, add `badgeKey?: 'operationalFailures'` to `AdminNavItem` and set it on the entry. In `ClientApp/src/features/admin/components/AdminShell.tsx`, render an MUI `Badge` (hidden at 0) and the error dot with a tooltip.
- [ ] T076 [P] [US3] Create `ClientApp/src/features/admin/components/operationalFailures/IncidentFilters.tsx`: time presets (1 h, 24 h, 7 d, 30 d, custom from/to), multi-select severity, engine and kind, a provider select, a user autocomplete over the existing admin users search API, and a state select. It reads from and writes to `useSearchParams`.
- [ ] T077 [US3] Create `ClientApp/src/features/admin/components/operationalFailures/TransitionButtons.tsx` and `RelatedIncidents.tsx`:
  - The mutations send no `rowVersion` (research D19).
  - `onSuccess` invalidates the list, detail and badge queries.
  - On 409, show a toast plus a refetch.
  - The bulk result shows a toast "Resolved 38, skipped 1, failed 1", with the failed ids listed in the drawer.
  - Resolve opens a note dialog (≤ 500 characters, with a counter).
  - Reopen shows on a resolved incident. A 409 carrying `newerIncidentId` shows "A newer incident is already open for this cause" with a link to it.

  Wire them into `IncidentDrawer.tsx` and `AdminOperationalFailuresPage.tsx`. Makes T070 pass.

**Checkpoint**: Quickstart S6 passes, and SC-005 holds (badge → incident → fix in ≤ 3 clicks).

---

## Phase 6: User Story 1b — A Super User controls who may read user content (Priority: P2)

**Goal**: Only a Super User can grant, revoke, assign, remove or delete anything that carries `content.view`, including enabling it for the Administrator role. Every change is audited.

**Independent Test**: The 7 SC-010 paths, refused as an Administrator and accepted and audited as a Super User. Quickstart S8.

### Tests for User Story 1b (write first, confirm failing)

- [ ] T078 [P] [US1b] Application tests in `tests/AskLucy.Application.Tests/Authorization/SuperUserControlledPermissionGuardTests.cs`, as an Administrator actor. The refusals are:
  - Create with content.view.
  - Update adding content.view.
  - Update **omitting** a stored content.view (FR-016i).
  - Delete a role holding it.
  - Bulk-delete including one.
  - Assign a role holding it.
  - Replace a user's role that holds it.
  - Bulk-assign a role holding it.
  - `ChangeUserRoleCommand` to a role holding it.

  Each throws `UnauthorizedAccessException` with the detail "Only a Super User can grant or remove *View user content*." As a Super User actor, every one of these succeeds. Update echoing the stored value unchanged as an Administrator succeeds (US1b scenario 3).
- [ ] T079 [P] [US1b] Application tests in `tests/AskLucy.Application.Tests/Authorization/SetAdministratorContentAccessCommandTests.cs`:
  - An Administrator actor is refused.
  - A Super User granting stores the key, writes a `RoleUpdated` audit with before/after, and evicts every Administrator's permission cache.
  - Revoking removes the key.
  - Afterwards the resolver reflects each change.
- [ ] T080 [P] [US1b] Web test in `tests/AskLucy.Web.Tests/Authorization/ContentPermissionGrantPathsTests.cs`: every endpoint in [contracts/view-user-content-permission.md](contracts/view-user-content-permission.md#mutations-and-who-may-perform-them), as an Administrator, returns 403 with that detail; as a Super User it returns 2xx, and a `RoleAuditLog` row exists (SC-010).
- [ ] T081 [P] [US1b] Frontend tests in `ClientApp/src/features/admin/components/PermissionPicker.test.tsx` (new) and `ClientApp/src/features/admin/pages/AdminRolesPage.test.tsx` (extend):
  - For a non-Super-User, the content.view checkbox is disabled, keeps its checked state, and shows the tooltip "Only a Super User can grant this". The saved payload echoes the stored value.
  - The "Administrators may view user content" switch renders for a Super User only, and a 403 shows a toast with the server detail.
  - The role-assignment picker disables roles that hold the key for a non-Super-User.

### Implementation for User Story 1b

- [ ] T082 [US1b] Create `src/AskLucy.Application/Authorization/SuperUserControlledPermissionGuard.cs`:
  - `EnsureCanChangeRolePermissions(actor, storedKeys, requestedKeys)`
  - `EnsureCanAssignOrRemove(actor, roleKeysBeingAssigned, roleKeysBeingRemoved)`
  - `EnsureCanDelete(actor, roleKeys)`

  Use `ICurrentUserAccessor.IsInRole(PrivilegedRoleNames.SuperUser)`.
- [ ] T083 [US1b] Call the guard from these handlers, before any write:
  - `src/AskLucy.Application/Authorization/Roles/Commands/CreateRole/CreateRoleCommandHandler.cs`
  - `…/UpdateRole/UpdateRoleCommandHandler.cs`
  - `…/DeleteRole/DeleteRoleCommandHandler.cs`
  - `…/BulkDeleteRoles/BulkDeleteRolesCommandHandler.cs` (all-or-nothing)
  - `src/AskLucy.Application/Authorization/Assignments/Commands/AssignRole/AssignRoleCommandHandler.cs`
  - `…/BulkAssignRole/BulkAssignRoleCommandHandler.cs`
  - `src/AskLucy.Application/Users/Commands/ChangeUserRole/ChangeUserRoleCommandHandler.cs` (confirm it delegates to `AssignRoleCommand`; if so, one guard call covers it)

  Makes T078 pass.
- [ ] T084 [US1b] Create `src/AskLucy.Application/Authorization/Roles/Commands/SetAdministratorContentAccess/` (command, handler): Super User only, calls the `RoleRepository.SetControlledGrantsAsync` from T021, then `IAuthorizationCacheInvalidator.Evict` for every Administrator via `IRoleAssignmentRepository.ListUserIdsByRoleAsync`. Add `PUT administrator/content-access` to `src/AskLucy.Web/Controllers/v1/AdminRolesController.cs`, and a `GET` that returns `{ granted }` for the switch's initial state. Makes T079 and T080 pass.
- [ ] T085 [US1b] Frontend changes:
  - `ClientApp/src/features/admin/components/PermissionPicker.tsx`: disable `SUPER_USER_CONTROLLED_KEYS` for non-Super-Users, keeping state, with a tooltip.
  - `ClientApp/src/features/admin/pages/AdminRolesPage.tsx`: the Super-User-only "Administrators may view user content" switch using new functions in `ClientApp/src/features/admin/api/adminRolesApi.ts`, with a toast on error.
  - `ClientApp/src/features/admin/components/AssignRoleDialog.tsx` and `ClientApp/src/features/admin/pages/AdminRoleAssignmentsPage.tsx`: mark and disable key-holding roles for non-Super-Users.

  Makes T081 pass.

**Checkpoint**: Quickstart S8 passes, and SC-010 holds.

---

## Phase 7: User Story 4 — Every engine reports, not just chat (Priority: P2)

**Goal**: Documents, embeddings, image generation, agents, workflows, MCP, the provider health check, background jobs and the Access engine all record through the same seam, and their users see the same calm text.

**Independent Test**: The per-engine matrix produces exactly one occurrence each, with the right engine, kind, severity, links and log correlation id (SC-002), and the Access inclusions and exclusions (SC-013). Quickstart S9 and S10.

### Tests for User Story 4 (write first, confirm failing)

- [ ] T086 [P] [US4] Infrastructure tests in `tests/AskLucy.Infrastructure.Tests/OperationalFailures/OperationalFailureJobFilterTests.cs`, using substituted `PerformContext`/`ElectStateContext`:
  - With `AutomaticRetry(Attempts = 2)`, a job failing 3× gives exactly 1 record, on the final election only.
  - A job that fails then succeeds gives 0.
  - A marked exception gives 0.
  - The correlation id parameter is created on attempt 1 and reused on attempts 2 and 3.
  - `LogContext` has `CorrelationId` during `OnPerforming`.
  - The AsyncLocal is cleared after `OnPerformed`.
- [ ] T087 [P] [US4] Application tests in `tests/AskLucy.Application.Tests/OperationalFailures/EngineRecordingTests.cs`, with a substituted recorder, one test per site. Each asserts exactly one `Record` with the right engine, operation, subject and ids, and system prose only as the reason:
  - `DocumentProcessingPipeline`: stage fail → Embeddings or DocumentProcessing, subject Document, `KnowledgeBaseId`.
  - `GenerateImageCommandHandler` → ImageGeneration.
  - `WorkflowExecutionOrchestrator`: node fail → Workflow, subject Workflow, `WorkflowExecutionId`/`NodeId`, recorded once, not again at run level.
  - `AgentExecutionOrchestrator`: step fail → Agent, subject Agent, operation = the tool name.
  - `McpToolAdapter`: failure → Mcp, subject McpServer.
  - `RefreshMcpCapabilitiesCommandHandler` and `TestMcpServerConnectionCommandHandler` → Mcp.
- [ ] T088 [P] [US4] Infrastructure test in `tests/AskLucy.Infrastructure.Tests/Ai/ProviderHealthCheckHostedServiceTests.cs` (extend): a failed check gives one `Record` with Engine AiProvider, Operation "Health check", no `UserId`, and `ProviderId` set (US4 scenario 4).
- [ ] T089 [P] [US4] Application tests in `tests/AskLucy.Application.Tests/Authorization/OwnershipGuardsTests.cs`, a theory over all 14 guards (paths in plan.md): a null item throws exactly `KeyNotFoundException` (not the subclass); a not-owned item throws `OwnershipDeniedException` with an **identical** message.
- [ ] T090 [P] [US4] Web tests in `tests/AskLucy.Web.Tests/OperationalFailures/AccessEngineRecordingTests.cs` (SC-013):
  - A wrong password 5× gives 1 Warning incident (Access, SignInRefused, "Sign-in") with 5 occurrences, `DistinctUserCount = 1` and `SourceIp` set.
  - An unknown email gives 1 occurrence with `UserId = null`.
  - A locked account gives `AccountLocked`.
  - A failed 2FA gives `TwoFactorRefused`.
  - A refused external callback gives `SignInRefused`, operation "External sign-in: {provider}".
  - A 403 on a `[RequirePermission]` endpoint gives `AccessDenied` (operation = the key), and the `RoleAuditLog` row still exists with the same correlation id.
  - Another user's chat gives 404, unchanged, and 1 `AccessDenied`.
  - A missing chat gives 404 and 0 occurrences.
  - A bad request body gives 0. The platform's own 429 gives 0. A token refresh gives 0.
  - `EmailNotConfirmed` gives 0.
  - No stored reason contains the password or code (seed a distinctive password and search for it).
  - The badge is unchanged.
- [ ] T091 [P] [US4] Web test in `tests/AskLucy.Web.Tests/OperationalFailures/EngineMatrixCorrelationTests.cs` (SC-002). For each engine, force one failure with Serilog captured by a test sink, and assert: exactly one occurrence, and its `CorrelationId` appears on a captured log event for the same failure.
- [ ] T092 [P] [US4] Frontend tests in `ClientApp/src/features/admin/pages/investigations/WorkflowRunInvestigationPage.test.tsx` and `DocumentInvestigationPage.test.tsx`: metadata only when content is null; with content, the failed step is highlighted or the extracted text is shown with the "truncated" note; no write controls. Plus `?select=` tests for `AdminVoicePage.test.tsx` and `features/mcp/pages/McpAdministrationPage`. (The `AdminUsersPage` `?search=` test is in T045.)
- [ ] T093 [P] [US4] Application test in `tests/AskLucy.Application.Tests/Documents/DocumentProcessingFailureTextTests.cs`: the embedding stage throws `AiProviderCredentialRejectedException` whose message contains `sk-test-123`. Assert that the stage and job `FailureReason`, the `DocumentProcessingLog` message shown to the owner, and the payload passed to `NotifyProcessingFailedAsync` all equal `UserFacingFailureText.Later`, contain no SC-001 word and no `sk-`.
- [ ] T094 [P] [US4] Web test in `tests/AskLucy.Web.Tests/OperationalFailures/UserFacingFailureTextMatrixTests.cs` (SC-001 across engines), as a non-admin user, forcing a classified provider failure (message containing `sk-test-123`) through voice, image generation, a workflow run, an agent execution and document processing. Every user-visible string (Problem Details `detail`, stored `FailureReason`/`ErrorMessage` the owner's API returns, notification text) contains no SC-001 word and no `sk-`. It also pins that the workflow and agent `SafeFailureMessage` send an `AiProviderException` to the generic arm.
- [ ] T095 [P] [US4] Application tests in `tests/AskLucy.Application.Tests/OperationalFailures/WorkflowRunInvestigationTests.cs` and `DocumentInvestigationTests.cs`, mirroring T041:
  - Without content permission the content is null and no access event is written.
  - With it, the content is returned and exactly one `UserContentAccessEvent` names the actor, owner, item and incident.
  - A run or document not referenced by the incident gives not-found.
  - Extracted text over 200 KB is truncated with `isTruncated = true`.
- [ ] T096 [P] [US4] Web tests: extend `tests/AskLucy.Web.Tests/OperationalFailures/AdminOperationalFailuresEndpointsTests.cs` with the workflow-run and document investigation routes. Expect 403 without view; `content: null` for an Administrator without the grant; non-null content for a Super User; 404 or 405 for POST, PUT and DELETE.

### Implementation for User Story 4

- [ ] T097 [US4] Create `src/AskLucy.Infrastructure/OperationalFailures/OperationalFailureJobFilter.cs` (`IServerFilter`, `IElectStateFilter`, `Order` greater than `AutomaticRetryAttribute`'s) per research D7. In the existing `services.AddHangfire((sp, config) => …)` block in `src/AskLucy.Infrastructure/DependencyInjection.cs`, add `config.UseFilter(new OperationalFailureJobFilter(sp.GetRequiredService<IOperationalFailureRecorder>(), sp.GetRequiredService<IFailureClassifier>(), sp.GetRequiredService<ILogger<OperationalFailureJobFilter>>()))`. Makes T086 pass.
- [ ] T098 [US4] Record at `src/AskLucy.Application/Documents/Processing/DocumentProcessingPipeline.cs` (≈L167–168, `stage.Fail`/`job.Fail`):
  - Engine `Embeddings` for the embedding stage and `DocumentProcessing` otherwise.
  - Operation = the stage name, subject = the document, plus `KnowledgeBaseId` and `UserId`.
  - Mark the exception if it propagates to Hangfire.
  - Replace the `ex.Message` passed to `FailAsync` (≈L135) with `UserFacingFailureText.For(classifier.Classify(ex, cancellationToken))` for every value persisted on the document or sent to the owner. The classified detail goes only to the log and the recorder. Makes T093 pass.
- [ ] T099 [US4] Record in `src/AskLucy.Application/Ai/Commands/GenerateImage/GenerateImageCommandHandler.cs` (ImageGeneration, "Generate image"), then mark and rethrow. Its non-admin Problem Details text is already covered by T047.
- [ ] T100 [US4] Record in `src/AskLucy.Application/Workflows/Runtime/WorkflowExecutionOrchestrator.cs` through one private `RecordNodeFailure(execution, node, error)` helper, called at each node-failure branch (≈L189, L358, L390, L732, L971). The subject is the workflow definition. Do **not** record again at the run-level `execution.Fail`, and do not record the budget or timeout branches twice. The reason is a fixed "Workflow step failed: {nodeKey}", never `error.Message`.
- [ ] T101 [US4] Record in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs` at the tool-call and step failure (≈L346–347) and the unexpected-exception path (≈L406), through one helper. The subject is the agent, the operation is the tool name, and the reason is fixed prose.
- [ ] T102 [US4] Record in `src/AskLucy.Application/Mcp/Tools/McpToolAdapter.cs` (tool-call failure) and in `src/AskLucy.Application/Mcp/Commands/RefreshMcpCapabilities/RefreshMcpCapabilitiesCommandHandler.cs` and `…/TestMcpServerConnection/TestMcpServerConnectionCommandHandler.cs`. The kind comes from `McpFailureCategory` through a small switch in the same files; the subject is the MCP server. The correlation id is ambient, which ties the record to `McpAuditLog`.
- [ ] T103 [US4] Record in `src/AskLucy.Infrastructure/Ai/ProviderHealthCheckHostedService.cs` on a failed check: AiProvider, "Health check", no user. The existing health column update is unchanged. Makes T088 pass. T087 should pass after T098–T102.
- [ ] T104 [US4] Create `src/AskLucy.Application/Common/OwnershipDeniedException.cs` (`: KeyNotFoundException`, with `ItemType` and `ItemId`). Split the null and not-owned branches in all 14 guards:
  - `Chats/Authorization/ChatOwnershipGuard.cs`
  - `Documents/Authorization/DocumentOwnershipGuard.cs`
  - `Documents/Authorization/DocumentFolderOwnershipGuard.cs`
  - `Documents/Authorization/DocumentUploadSession…` (locate it)
  - `KnowledgeBases/Authorization/KnowledgeBaseOwnershipGuard.cs`
  - `KnowledgeBaseFolder…` (locate it)
  - `Memory/Authorization/MemoryOwnershipGuard.cs`
  - `Projects/Authorization/ProjectOwnershipGuard.cs`
  - `Prompts/Authorization/PromptOwnershipGuard.cs`
  - `Retrieval/Authorization/RetrievalOwnershipGuard.cs`
  - `Workflows/Authorization/WorkflowOwnershipGuard.cs`
  - `Workflows/Authorization/WorkflowExecutionOwnershipGuard.cs`
  - `Agents/Authorization/AgentOwnershipGuard.cs`
  - `Agents/Authorization/AgentExecutionOwnershipGuard.cs`

  All paths are under `src/AskLucy.Application/`. The messages must stay identical. Makes T089 pass.
- [ ] T105 [US4] In `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs`, add an `OwnershipDeniedException` arm **before** the `KeyNotFoundException` arm. It keeps the existing `AccessDenied` log, records `Access / AccessDenied / "Open {ItemType}"`, then produces the same 404 as today. In `src/AskLucy.Web/Auth/PermissionDeniedAuditResultHandler.cs`, after the existing `RoleAuditLog` write, record `Access / AccessDenied` with operation = the required permission key.
- [ ] T106 [US4] Sign-in recording (research D13):
  - Add `string? SourceIp` to `src/AskLucy.Application/Authentication/Commands/Login/LoginCommand.cs` and the 2FA and external-callback commands, filled from `HttpContext.Connection.RemoteIpAddress?.ToString()` in `src/AskLucy.Web/Controllers/v1/AuthController.cs`.
  - Make `IdentityService.ValidateCredentialsAsync` (`src/AskLucy.Persistence/Identity/IdentityService.cs`, `IIdentityService`) return the matched user id internally on a wrong password; the client-facing `AuthResult` is unchanged.
  - Record in `LoginCommandHandler.cs`, `src/AskLucy.Application/Authentication/Commands/LoginTwoFactor/LoginTwoFactorCommandHandler.cs` and `src/AskLucy.Application/Authentication/Commands/ExternalLogin/ProcessExternalLoginCallbackCommandHandler.cs`: kinds SignInRefused, AccountLocked (locked or suspended) and TwoFactorRefused. Do not record `EmailNotConfirmed`. Pass no password, code or token.

  Makes T090 pass.
- [ ] T107 [US4] Create `src/AskLucy.Application/OperationalFailures/Investigations/GetWorkflowRunInvestigation/` and `GetDocumentInvestigation/`, with the same gating and access-event rules as T053. Extracted text is truncated to 200 KB with a flag. Add their routes to `AdminOperationalFailuresController.cs`, with `[ProducesResponseType]` as T054. Makes T095 and T096 pass; T094 should pass after T098–T101.
- [ ] T108 [US4] Frontend changes:
  - Create `ClientApp/src/features/admin/pages/investigations/WorkflowRunInvestigationPage.tsx` and `DocumentInvestigationPage.tsx`, both read-only, and add their routes in `ClientApp/src/routes/router.tsx`.
  - Add `?select=` handling to `ClientApp/src/features/admin/pages/AdminVoicePage.tsx` and `ClientApp/src/features/mcp/pages/McpAdministrationPage.tsx`.

  Makes T092 pass. T091 should now pass.

**Checkpoint**: The whole engine matrix records (SC-002, SC-013). Quickstart S9 and S10 pass.

---

## Phase 8: User Story 5 — Records age out; erasure anonymises (Priority: P3)

**Goal**: A daily retention sweep, configurable windows, and hard-erasure anonymisation that never deletes content-access events.

**Independent Test**: Seeded ages and states, then the sweep; a hard-deleted account then shows as anonymised. Quickstart S12 and S13.

### Tests for User Story 5 (write first, confirm failing)

- [ ] T109 [P] [US5] Persistence tests in `tests/AskLucy.Persistence.Tests/OperationalFailures/OperationalFailureRetentionTests.cs` (SC-008):
  - Acknowledged at 91 d: removed, with its occurrences and participants.
  - Resolved at 91 d: removed.
  - Unacknowledged at 91 d: kept.
  - Unacknowledged at 181 d: removed.
  - A kept incident with occurrences at 100 d and 10 d: the 100 d occurrence is removed, and the counts, `FirstSeenUtc` and the triage state are unchanged.
  - `UserContentAccessEvents` at 400 d: kept.
  - Row counts are asserted against the table, using `IgnoreQueryFilters()` or a raw `COUNT(*)`, so a soft-deleted row can't pass as removed.
- [ ] T110 [P] [US5] Persistence tests in `tests/AskLucy.Persistence.Tests/OperationalFailures/OperationalFailureAnonymizationTests.cs` (SC-012). After `AnonymizeUserAsync(userId)`:
  - 0 occurrences have `UserId`, `ChatId`, `MessageId`, `DocumentId` or `SourceIp` for that user.
  - `IsUserErased = 1`.
  - Participant keys are rewritten to `erased:*`.
  - `OccurrenceCount` and `DistinctUserCount` are unchanged.
  - Access events have `OwnerUserId`/`ItemId` null and `IsOwnerErased = 1`, and their row count is unchanged.
  - Row counts are asserted against the table, using `IgnoreQueryFilters()` or a raw `COUNT(*)`, so a soft-deleted row can't pass as kept or removed.
- [ ] T111 [P] [US5] Application tests in `tests/AskLucy.Application.Tests/Users/DeleteMyAccountCommandHandlerTests.cs` (extend):
  - Both anonymise calls happen before `identityService.DeleteAsync`.
  - If `AnonymizeUserAsync` throws, the exception propagates and `DeleteAsync` is never called.
- [ ] T112 [P] [US5] Application tests in `tests/AskLucy.Application.Tests/OperationalFailures/OperationalFailureRetentionJobTests.cs`:
  - It passes the options' days to the store and logs the three counts.
  - A store exception is rethrown, so the Hangfire filter records it.
  - A negative or zero option is clamped via `Normalize()` and logged.

### Implementation for User Story 5

- [ ] T113 [US5] Add these to `IOperationalFailureStore` and `OperationalFailureStore`, as set-based `ExecuteDeleteAsync`/`ExecuteUpdateAsync` methods:
  - `PurgeTriagedIncidentsAsync(cutoff)`
  - `PurgeUnacknowledgedIncidentsAsync(cutoff)`
  - `PurgeOldOccurrencesAsync(cutoff)`
  - `AnonymizeUserAsync(userId)`, in one transaction covering occurrences, participants and `UserContentAccessEventRepository.AnonymizeOwnerAsync`.

  The purge methods must use `ExecuteDeleteAsync`, never a tracked `Remove`. `AuditSaveChangesInterceptor` would turn a tracked `Remove` into a soft delete, and retention requires physical removal (FR-029). None of these methods sets the audit columns (constitution §5; [data-model.md → Audit columns](data-model.md#audit-columns)). Makes T109 and T110 pass.
- [ ] T114 [US5] Create `src/AskLucy.Application/OperationalFailures/Jobs/OperationalFailureRetentionJob.cs` (with `IOperationalFailureRetentionJob`) and register it as a recurring job in `src/AskLucy.Web/Program.cs` next to the existing recurring jobs (≈L749–772): daily at `30 3 * * *` UTC, resolved through the container like the others. Makes T112 pass.
- [ ] T115 [US5] In `src/AskLucy.Application/Users/Commands/DeleteMyAccount/DeleteMyAccountCommandHandler.cs`, inject `IOperationalFailureStore` and call `AnonymizeUserAsync(userId)` after the memory anonymisation and before `DeleteAsync`. Update the class doc comment. Makes T111 pass.

**Checkpoint**: Quickstart S12 and S13 pass. SC-008 and SC-012 hold.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T116 [P] SC-006 secret scan: create `tests/AskLucy.Persistence.Tests/OperationalFailures/StoredReasonsSecretScanTests.cs`. It seeds vendor exceptions whose messages carry each corpus secret, runs them through the real ingestor and store, then scans every stored `Reason` for the patterns in quickstart §3. Expect 0 matches.
- [ ] T117 [P] SC-004 latency and resilience: create `tests/AskLucy.Web.Tests/OperationalFailures/RecordingLatencyTests.cs`.
  - The same forced failure with a real recorder vs a no-op recorder: the median over 50 runs is within max(5%, 20 ms).
  - With `IOperationalFailureStore` substituted to throw: 100% of responses are identical, and `OperationalFailureRecordingFailed` is logged with the correlation id.
- [ ] T118 [P] SC-007 scale test: create `tests/AskLucy.Persistence.Tests/OperationalFailures/OperationalFailureScaleTests.cs`, gated by `RUN_SCALE_PERFORMANCE_TESTS=1`. It seeds 100k occurrences over about 2k incidents and asserts that the unfiltered and a 4-filter `ListIncidentsAsync` each return in < 2 s. Like the existing scale tests, it is off in CI until go-live (memory note: shared-host IO, not the query). Mark the constitution §10 performance gate "N/A until go-live — gated test exists" in the commit message.
- [ ] T119 [P] Accessibility: create `ClientApp/src/features/admin/pages/AdminOperationalFailuresPage.a11y.test.tsx` and `ClientApp/src/features/admin/pages/investigations/ChatInvestigationPage.a11y.test.tsx`, following the existing `*.a11y.test.tsx` pattern.
- [ ] T120 [P] Documentation. Update `docs/`:
  - Architecture: an "Operational failure trail" section covering the recorder seam, one-occurrence rule, and "record, don't expose".
  - API: link to [contracts/admin-operational-failures.md](contracts/admin-operational-failures.md).
  - Database: the four tables and the retention.
  - The admin guide: the page, the badge, and the *View user content* permission.

  Add a "Recording a failure" note to whichever developer doc lists cross-cutting conventions, pointing to [contracts/operational-failure-recorder.md](contracts/operational-failure-recorder.md).
- [ ] T121 Apply the migration to **production** (`appsettings.Production.json` `DefaultConnection`) with `dotnet ef database update …`, confirm `(Pending)` count 0 on both DBs, and record the date in the Migrations `README.md` if that file tracks applications.
- [ ] T122 Run the full backend suites (all five test projects) and the full frontend suite (`npx tsc -b --noEmit && npx vitest run`). Fix every failure. Don't filter to the touched files.
- [ ] T123 Run the CI-equivalent lint `dotnet format "Ask Lucy.sln" --no-restore --verify-no-changes --severity error`. Treat only whole-file `ENDOFLINE` as noise, and run the lone-CR scan from the memory note. Then fix anything real (`IMPORTS`, `WHITESPACE`, `CHARSET`, BOM).
- [ ] T124 Walk through [quickstart.md](quickstart.md) S1–S13 on localhost:7170 (and S1, S2, S7 on production after deploy), recording each expected value. Follow up the spec Assumption about the `/terms` privacy disclosure ("authorised staff may view conversation content to diagnose failures") with the open specs/070 legal review; this is a note, not code.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: needs Setup. **Blocks every story.** Within it:
  - T017 → T018/T019;
  - T019 + T022 → T027 → T028 → T029 → T031;
  - T022/T023 → T032 → T033 → T034 → T035 → T036;
  - T023 → T030 → T031 (the correlation-id columns ride the same migration);
  - T020 → T021.
- **US1 (Phase 3)**: needs Foundational. This is the MVP.
- **US2 (Phase 4)**: needs Foundational. Its UI tasks (T066) touch US1's components, so run US2 after US1, or do T066 last.
- **US3 (Phase 5)**: needs US1's controller, DTOs and page (T050, T054, T055, T057, T058).
- **US1b (Phase 6)**: needs only Foundational (T020, T021). It is independent of US1–US3 and can run in parallel with them.
- **US4 (Phase 7)**:
  - The engine sites (T098–T103) and the Access engine (T104–T106) need only Foundational.
  - The investigations (T107, T108) need US1's T053, T054 and T058 patterns.
  - T105 edits the same middleware file as T047, so do it after US1.
- **US5 (Phase 8)**: needs Foundational only.
- **Polish (Phase 9)**: needs everything. T121 needs T031.

### Shared-file sequencing (not parallel)

- `ProblemDetailsMiddleware.cs`: T047 (US1) → T072 (US3, the 409 arm) → T105 (US4).
- `OperationalFailureStore.cs`: T029 → T051 → T063 → T071 → T113.
- `AdminOperationalFailuresController.cs`: T054 → T074 → T107.
- `AdminOperationalFailuresEndpointsTests.cs`: T042 → T096.
- `DocumentProcessingPipeline.cs`: T098 (recording and the `ex.Message` fix in one edit).
- `AdminUsersPage.tsx`: T060 only (moved from US4 to US1).
- `router.tsx`: T058 → T108.
- `adminNav.tsx`: T056 → T075.
- `IncidentDrawer.tsx`: T057 → T066 → T077.

### Parallel opportunities

- **Foundational**: every test task T005–T016 is [P]. T017, T018, T020, T022–T026 and T032 touch distinct files.
- **US1**: T037–T045 are all [P]. T046, T049, T050, T055 and T056 are [P].
- **US4**: T098–T103 are separate engine files and can be split across workers. T104's 14 guard edits are independent of each other.
- **Across stories**, once Foundational is done: US1b and US5 can proceed in parallel with US1→US2→US3.

## Parallel Example: User Story 1

```text
# Tests together:
T037 UserFacingFailureTextTests · T038 ChatFailureRecordingTests · T039 CorrectiveActionCatalogTests
T040 IncidentQueriesTests · T041 ChatInvestigationTests · T042 endpoint tests
T043 page test · T044 investigation page test · T045 AI providers ?select= and users ?search= tests

# Then independent implementation files together:
T046 UserFacingFailureText · T049 CorrectiveActionCatalog · T050 DTOs · T055 api client · T056 permissions + nav
```

## Implementation Strategy

### MVP (US1 only)

1. Phase 1 and Phase 2. The recording pipeline, store and permission keys are live.
2. Phase 3 (US1). Users see calm text, chat failures are recorded with the correct kind, admins have the list, drawer, fix link and a gated chat view.
3. **Stop and validate** with quickstart S1–S4 and S7, and migrate production (T121) before deploying.

### Incremental delivery

1. + US2: burst grouping, plus voice recording. This closes the 2026-09-22 case.
2. + US3: triage and the badge. At this point the page is operationally useful.
3. + US1b: delegate content access.
4. + US4: the remaining engines, the jobs and the Access engine.
5. + US5: retention and erasure. **This must ship before the page has been live for 90 days.** Erasure support should ship with US1 if any production user may request deletion in the meantime; T115 is small and independent.
6. Polish.

## Notes

- Commit after each task or logical group, and push straight to `main` (solo-developer workflow).
- Check that your changed files actually landed in the commit, because a concurrent session here has reset the tree mid-work before.
- Each checkpoint is a working, deployable state.
