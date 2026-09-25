# Implementation Plan: Admin Operational Failure Audit Trail

**Branch**: `074-operational-failure-audit` (work lands on `main`) | **Date**: 2026-09-25 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/074-operational-failure-audit/spec.md`

## Summary

Every system-side failure is recorded once, through one Application seam,
`IOperationalFailureRecorder`, into one new cross-cutting store. The store keeps grouped
**incidents** (mutable triage state) and append-only **occurrences**. At the same point, every
user-visible failure text collapses to two calm, cause-free sentences.

**Recording path:**
- `Record()` is a non-blocking enqueue onto a bounded in-memory channel.
- A `BackgroundService` with its own DI scope drains the channel. For each report it sanitises the
  reason, derives the kind and severity, computes the grouping and root-cause keys, and upserts the
  open incident. A filtered unique index guarantees one open incident per key.
- Recording failures are logged and never reach the user (research D2, D3).
- An exception recorded at its site is marked, so the boundary does not record it again. That
  keeps it to exactly one occurrence per failure (D11).

**Hook points:**
- The chat stream `catch`.
- `ProblemDetailsMiddleware` (boundary).
- Voice failover and recovery.
- The document pipeline, workflow nodes, agent steps, MCP, image generation and the health check.
- A new global Hangfire filter: final failures only, plus a per-job correlation id (D7).
- The Access engine: the 403 handler, a new `OwnershipDeniedException : KeyNotFoundException` that
  keeps the 404 (D12), and the sign-in, 2FA and external-login handlers (D13).

**Admin side:**
- A new *Operational failures* page with filters, paging, a detail drawer, and an occurrence list.
- Per-incident and per-root-cause acknowledge/resolve, corrective-action deep links, and a
  60-second badge of distinct unacknowledged-Critical root causes.
- Read-only investigation views. Their content projection is gated server-side by the
  Super-User-controlled *View user content* permission, and every content view writes an immutable
  access event (D14, D15).

A daily Hangfire sweep enforces 90/180-day retention. Account hard-deletion anonymises the trail in
the same operation (D16).

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript 5 / React 19 (ClientApp).

**Primary Dependencies**: existing only. These are MediatR, FluentValidation, EF Core (SQL
Server), Hangfire 1.8.24, Serilog, `System.Threading.Channels` (part of the BCL), MUI, TanStack
Query, and MSW. **There is no new package** (spec constraint).

**Storage**: SQL Server. There are four new tables, `OperationalFailureIncidents`,
`OperationalFailureOccurrences`, `OperationalFailureIncidentParticipants` and
`UserContentAccessEvents`, created by one additive migration. Details are in
[data-model.md](data-model.md).

**Testing**: xUnit, NSubstitute, FluentAssertions and `FakeLogger`. Use `FakeLogger`, not
`Received().Log`, because `[LoggerMessage]` source-gen breaks NSubstitute matching (memory note).
Frontend uses Vitest, RTL and MSW, with a handler for every new endpoint (memory note: an unmocked
endpoint is a CI-only flake). Web.Tests use a derived `CustomWebApplicationFactory` fixture,
**not** `WithWebHostBuilder` per test (memory note: spec 072 stall).

**Target Platform**: IIS on the site4now shared host, single instance.

**Project Type**: Web application (ASP.NET Core API plus the React SPA in `src/AskLucy.Web/ClientApp`).

**Performance Goals**:
- Recording adds microseconds to the failing request (an enqueue only). SC-004 allows ≤ 5% or
  20 ms.
- The list query takes ≤ 2 s at 100k occurrences (SC-007). The list reads **incidents** only,
  using the indexes in data-model.md. Occurrences are only read per incident, and paged.
- The badge is one indexed `COUNT(DISTINCT)` over the filtered badge index, polled every 60 s.

**Constraints**:
- Recording never throws into, blocks, or replaces the caller's error (FR-020, FR-021).
- No recursion: the recorder never records its own failures.
- The Application layer never references EF Core. The unique-violation retry and counter updates
  live in Persistence.
- No `ValidateOnStart` on the new options.
- No vendor text stored.
- The user-visible text is exactly two sentences.

**Scale/Scope**: At most tens of failures a day in normal running; storms of thousands an hour
during a provider outage (10k channel, 1,000-occurrence cap per incident); one to three admins.

There are no NEEDS CLARIFICATION items. All five clarifications are integrated in the spec, and
every design unknown is resolved in [research.md](research.md), D1–D18.

## Constitution Check

*GATE: checked before Phase 0 and re-checked after Phase 1 design.*

| Principle | Status | How the design complies |
|---|---|---|
| **I. Clean Architecture (NON-NEGOTIABLE)** | ✅ | See the layer-by-layer notes below this table. |
| **II. SOLID** | ✅ | Narrow seams, each with one job: recorder, store, classifier, sanitizer, correlation accessor, corrective-action catalogue. Engines depend on `IOperationalFailureRecorder` only (ISP/DIP). |
| **III. Simplicity / YAGNI** | ✅ | One store instead of eight modified audit logs (D1). No notifier is built; there is only a notification seam (D18). No generic "browse user data" admin feature, only three incident-scoped read models (D15). |
| **IV. Composition over inheritance** | ✅ | The one inheritance is `OwnershipDeniedException : KeyNotFoundException`. It exists **only** to keep the existing 404 contract and every existing catch site unchanged (D12). |
| **V. Dependency Inversion / Testability** | ✅ | Every engine test substitutes the recorder. The writer, ingestor, sanitizer, classifier and severity policy are each unit-testable without a host. |
| **VI. Separation of Concerns** | ✅ | User-facing text (`UserFacingFailureText`) and the admin record (classifier, recorder) are separate outputs of the same `catch` (D9). |
| **VIII. No Silent Failures (NON-NEGOTIABLE)** | ✅ | See the notes below this table. |
| **§3 Module communication** | ✅ | Modules call an interface. No module reads another's tables. The trail holds soft references, like `McpAuditLog`. |
| **§5 Database** | ✅ | Code-first additive migration. Strings for enums, `rowversion` for triage, filtered unique index, no cascade diamond. BOM-less file with `System.*` usings first. Both DBs migrated by hand (data-model.md). All four entities derive from `BaseEntity`; set-based counter, retention and anonymisation writes leave the audit columns to the interceptor's tracked writes, following the existing `ExecuteUpdateAsync` convention. Retention hard-deletes via `ExecuteDeleteAsync` ([data-model.md → Audit columns](data-model.md#audit-columns)). |
| **§6 API** | ✅ | `/api/v1/admin/...`, Problem Details, `{id}/actions/{verb}` for transitions, offset paging with `PagedResult` (as `AdminCustomModelsController`), 409 for conflicts, and the `admin-endpoints` rate limit. |
| **§8 Security** | ✅ | See the notes below this table. |
| **§9 AI Principles** (provider independence) | ✅ | The kind vocabulary reuses `AiProviderFailureKind` names. No vendor-specific branch exists outside the provider adapters. |
| **§10 Testing** | ✅ | Domain, Application, Infrastructure, Persistence, Web and frontend tests are all planned. The quickstart maps each SC to a check. |
| **§13 Documentation** | ✅ | This plan, the contracts, and the data model. Updates go to `docs/` architecture/API/database notes, plus a CLAUDE.md-style note on "record, don't expose". The architectural decisions (one store, the droppable channel, the Administrator exclusion) are recorded in ADR 0017 (`docs/adr/0017-operational-failure-trail.md`). |
| **§14 Observability** | ✅ | The correlation id is extended to Hangfire jobs, which have none today (D6, D7). Every record ties to a log line, and the role and MCP audit logs gain a correlation id (FR-006b). There is no metrics pipeline or `Meter` anywhere in the platform yet, so channel drops and write failures are logged (`OperationalFailureDropped`, `OperationalFailureRecordingFailed`) rather than counted; a metric is deferred until an exporter exists. |

**Principle I, layer by layer:**
- **Domain** holds the aggregate, the enums, the severity policy and the catalogue keys, with no
  I/O.
- **Application** holds the recorder and store interfaces, the ingestor, the sanitizer, the
  classifier, the CQRS handlers and the retention job. It has no EF Core (memory note).
- **Infrastructure** holds the channel recorder, the writer `BackgroundService`, the Hangfire
  filter and the correlation accessor.
- **Persistence** holds the configurations, the store (`ExecuteUpdateAsync` counters, the
  unique-violation retry) and the migration.
- **Web** holds the controller, which only calls `ISender`, plus the middleware and 403-handler
  hooks.

**Principle VIII, how every failure surfaces:**
- This feature is the §2.VIII capture mechanism. Every failure is logged *and* recorded, and hiding
  the cause from end users is compliant (memory note).
- The recorder's own failures are logged, never swallowed.
- A failed retention sweep is rethrown to Hangfire and recorded.
- A failed erasure anonymisation fails the erasure.
- A failed access-event write fails the content request (D15).
- On the frontend, every query and mutation has an inline or toast error, the badge fetch error is
  visible, and a 409 shows a message.

**§8 Security, in detail:**
- Server-side permission on every endpoint.
- Content is gated in the handler, not the UI (FR-016a).
- Immutable content-access audit.
- Super-User-only grant paths, covering all 7 in SC-010.
- The redactor runs at ingestion, not per caller (D8).
- No password, code or token field exists on the report type (FR-012a).
- Ownership refusals keep their 404, so the record adds no existence oracle (D12).

**Post-design re-check (after Phase 1):** ✅ Passes. Neither of the two deliberate exceptions to
existing patterns, both recorded in Complexity Tracking below, violates a principle:

- the Administrator role no longer gets the full catalogue automatically;
- content-access audit writes are synchronous.

The Phase 1 design added:

- the participants table;
- the filtered badge index;
- the `OwnershipDeniedException` subclass;
- the Super-User-only Administrator content-access endpoint.

None of these adds a dependency or crosses a layer.

## Project Structure

### Documentation (this feature)

```text
specs/074-operational-failure-audit/
├── plan.md               # This file
├── research.md           # D1–D18 decisions
├── data-model.md         # 4 tables, enums, state machine, corrective-action map, migration notes
├── quickstart.md         # Validation run-book (S1–S13 ↔ SC-001…SC-013)
├── contracts/
│   ├── admin-operational-failures.md      # REST: incidents, transitions, badge, investigations
│   ├── operational-failure-recorder.md    # in-process seam, caller rules, call-site list
│   └── view-user-content-permission.md    # Super-User-controlled permission rules
├── checklists/requirements.md
└── tasks.md              # /speckit-tasks (not created here)
```

### Source Code

```text
src/AskLucy.Domain/
├── Authorization/
│   ├── AdminArea.cs                                  # + OperationalFailures
│   └── AdminPermissionCatalog.cs                     # + 3 keys, SuperUserControlledKeys
└── OperationalFailures/
    ├── OperationalFailureIncident.cs                 # aggregate + transitions
    ├── OperationalFailureOccurrence.cs · IncidentParticipant.cs · UserContentAccessEvent.cs
    ├── OperationalFailureEngine.cs · OperationalFailureKind.cs (+ OperationalFailureKinds.FromProvider)
    ├── OperationalFailureSeverity.cs · OperationalFailureOutcome.cs · IncidentTriageState.cs
    └── OperationalFailureSeverityPolicy.cs

src/AskLucy.Application/
├── Common/OwnershipDeniedException.cs                # : KeyNotFoundException (D12)
├── Abstractions/ICorrelationIdAccessor.cs            # D6
├── Options/OperationalFailuresOptions.cs             # defaults, no ValidateOnStart
├── Authorization/
│   ├── EffectivePermissionResolver.cs                # Administrator = Full − controlled + stored grants
│   ├── SuperUserControlledPermissionGuard.cs         # used by role create/update/delete/assign handlers
│   └── Roles/Commands/SetAdministratorContentAccess/ # NEW, Super User only
├── {Chats,Documents,KnowledgeBases,Memory,Projects,Prompts,Retrieval,Workflows,Agents}/Authorization/*OwnershipGuard.cs
│                                                     # 14 guards: null → KeyNotFound, not-owned → OwnershipDenied
├── Authentication/Commands/Login/…                   # + SourceIp; records Access occurrences (D13)
├── Users/Commands/DeleteMyAccount/…Handler.cs        # + AnonymizeUserAsync (D16)
├── Ai/TextToSpeechStreamer.cs · Ai/Commands/CreateSpeechToTextSession/… · Ai/Commands/GenerateImage/…  # call sites
├── Documents/Processing/DocumentProcessingPipeline.cs # call site
├── Workflows/Runtime/WorkflowExecutionOrchestrator.cs · Agents/Runtime/AgentExecutionOrchestrator.cs · Mcp/Tools/McpToolAdapter.cs  # call sites
└── OperationalFailures/
    ├── Abstractions/
    │   ├── IOperationalFailureRecorder.cs · OperationalFailureReport.cs · VoiceRecoveryReport.cs
    │   ├── IOperationalFailureStore.cs · IUserContentAccessEventRepository.cs
    │   └── OperationalFailureMarkers.cs              # MarkOperationalFailureRecorded() (D11)
    ├── FailureClassifier.cs · FailureReasonSanitizer.cs · UserFacingFailureText.cs
    ├── OperationalFailureIngestor.cs · OperationalFailureKeys.cs · CorrectiveActionCatalog.cs
    ├── Commands/ Acknowledge · Resolve · Reopen · AcknowledgeRootCause · ResolveRootCause
    ├── Queries/  ListIncidents · GetIncident · ListOccurrences · ListRelatedIncidents · GetSummary
    ├── Investigations/ GetChatInvestigation · GetWorkflowRunInvestigation · GetDocumentInvestigation
    ├── Jobs/OperationalFailureRetentionJob.cs
    └── OperationalFailureDtos.cs

src/AskLucy.Infrastructure/
├── DependencyInjection.cs                            # recorder singleton, writer hosted service, config.UseFilter(...)
└── OperationalFailures/
    ├── ChannelOperationalFailureRecorder.cs
    ├── OperationalFailureWriterService.cs            # BackgroundService, own scope, drain on stop
    ├── OperationalFailureJobFilter.cs                # IServerFilter + IElectStateFilter (D7)
    └── CorrelationIdAccessor.cs                      # AsyncLocal → HttpContext.Items

src/AskLucy.Persistence/
├── AskLucyDbContext.cs                               # + 4 DbSets
├── Configurations/OperationalFailures/*.cs
├── Repositories/OperationalFailureStore.cs · UserContentAccessEventRepository.cs
└── Migrations/<ts>_AddOperationalFailureAudit.cs

src/AskLucy.Web/
├── Controllers/v1/AdminOperationalFailuresController.cs
├── Controllers/v1/AiController.cs                    # DescribeTurnFailure → generic; record classified (D9)
├── Controllers/v1/AdminRolesController.cs            # + administrator/content-access
├── Controllers/v1/AuthController.cs                  # pass SourceIp
├── Middleware/ProblemDetailsMiddleware.cs            # generic non-admin detail; boundary record; OwnershipDenied arm
├── Middleware/CorrelationIdMiddleware.cs             # expose Items key constant
├── Auth/PermissionDeniedAuditResultHandler.cs        # + Access record
├── Program.cs                                        # recurring retention job
├── appsettings.json                                  # + "OperationalFailures" defaults
└── ClientApp/src/
    ├── routes/router.tsx                             # page + 3 investigation routes
    └── features/admin/
        ├── adminPermissions.ts · adminNav.tsx        # keys, controlled set, nav entry + badgeKey
        ├── components/AdminShell.tsx                 # badge rendering
        ├── api/adminOperationalFailuresApi.ts
        ├── hooks/useOperationalFailureBadge.ts
        ├── components/operationalFailures/           # Filters, IncidentTable, IncidentDrawer, OccurrenceTable,
        │                                             # RelatedIncidents, CorrectiveActionLink, TransitionButtons
        ├── pages/AdminOperationalFailuresPage.tsx
        ├── pages/investigations/{Chat,WorkflowRun,Document}InvestigationPage.tsx
        ├── pages/AdminAiProvidersPage.tsx · AdminVoicePage.tsx · (MCP servers page) · AdminUsersPage.tsx  # ?select= / ?search=
        └── pages/AdminRolesPage.tsx · AdminRoleAssignmentsPage.tsx   # disabled controlled key, Administrator switch

tests/
├── AskLucy.Domain.Tests/OperationalFailures/         # transitions, severity policy, kind mapping totality
├── AskLucy.Application.Tests/OperationalFailures/    # ingestor, sanitizer corpus, classifier, keys, handlers, investigations
├── AskLucy.Application.Tests/Authorization/          # resolver exception, guard × every path, ownership guards
├── AskLucy.Infrastructure.Tests/OperationalFailures/ # recorder G1–G6, writer, Hangfire filter (final-only, correlation)
├── AskLucy.Persistence.Tests/OperationalFailures/    # upsert race, cap, counters, retention, anonymise, secret scan
└── AskLucy.Web.Tests/OperationalFailures/            # endpoints, permissions, content gating, calm text, replay SC-003
```

**Structure Decision**: Feature folders named `OperationalFailures/` in each existing layer,
matching `CustomModels/` and `Workflows/`. Cross-cutting edits (the 14 ownership guards, the
middleware and the role handlers) stay in their current files. The frontend lives in
`features/admin`, because the page is admin-only.

## Key flows

1. **Chat turn fails mid-stream (US1).**
   - The `catch` logs as today.
   - It calls `recorder.Record(new() { Engine = Chat, Operation = "Chat reply", Kind =
     classifier.Classify(ex, ct), Outcome = Failed, ProviderName, Model, UserId, ChatId, MessageId,
     Exception = ex })`.
   - It appends `UserFacingFailureText.For(kind)` to the stream and records
     `RecordedTurnOutcome.FailedBeforeCompleting` with that same generic sentence.
   - The writer persists the report shortly after, in its own scope.
2. **Provider failure before streaming (US1 scenario 1).**
   - The handler throws `AiProviderException`.
   - The middleware logs `ProviderFailureSurfaced`, records (it is not marked), and returns Problem
     Details. The detail is the calm sentence for non-admins and today's classified prose plus
     `providerFailure` for admins.
3. **Voice burst (US2).**
   - Each failover records `Voice / CredentialRejected / DegradedServed`. It is Critical, because
     the kind is Critical whatever the outcome (D5).
   - Each recovery calls `RecordRecovery` → `RecoveryCount + 1`.
   - The result is one incident with 7 occurrences and 7 recoveries.
4. **Job final failure (US4).**
   - The filter sees `FailedState` after `AutomaticRetry` has declined to reschedule, and records
     `BackgroundJob / JobFailedAfterRetries` with the job's stable correlation id.
5. **Triage (US3).**
   - The list reads incidents.
   - Acknowledge or resolve sends `rowVersion`; a stale one returns 409.
   - Root-cause bulk transitions iterate the open incidents with that key, transition each with its
     own `rowVersion`, and report succeeded, skipped and failed counts (FR-026b).
   - Mutations invalidate the list, the detail and the badge queries.
6. **Content view (US1 scenario 7).**
   - The handler checks that the incident references the item and loads the metadata.
   - If the caller holds `content.view` and is not the owner, it inserts a `UserContentAccessEvent`
     and then loads the content, all in one unit of work. It returns.

## Testing strategy

These are required. Each maps to a spec SC.

- **Domain**:
  - The triage state machine, including the no-op results.
  - The severity policy table (every engine × kind × outcome row in FR-009).
  - `FromProvider` totality over `AiProviderFailureKind`.
- **Application**:
  - The sanitizer corpus (SC-006): bearer, JWT, `sk-`/`sk-ant-`/`AIza`/`xi-` keys, `Authorization`
    and `Cookie` headers, connection strings, `?key=`, long hex/base64, multi-line vendor JSON, and
    the 500-character truncation.
  - Classifier arms, including a caller-cancelled `TaskCanceledException`, which must **not**
    be classified.
  - Grouping and root-cause key normalisation.
  - Corrective-action coverage for every kind.
  - Ingestor severity and cap behaviour.
  - Every handler's permission and 409 paths.
  - The investigation handlers: content absent without the permission, and an access event written
    before content is returned.
  - `SuperUserControlledPermissionGuard` × the 7 SC-010 paths.
  - `EffectivePermissionResolver`: Administrator without, then with, the stored grant; Super User
    always has it.
  - Each ownership guard: null → `KeyNotFoundException` and not-owned → `OwnershipDeniedException`,
    with the same message.
  - The DeleteMyAccount ordering (anonymise before delete, and fail when anonymisation fails).
  - Document processing never persists or notifies raw `ex.Message`; the owner sees only the calm sentence.
  - The workflow-run and document investigations: gating, one access event, 200 KB truncation.
- **Infrastructure**:
  - Recorder G1–G6 (the contract table).
  - The writer continues after a throwing batch and drains on stop.
  - The Hangfire filter: 1 record for 3 attempts, 0 records for a success-after-retry, 0 for a
    marked exception, and the correlation id identical across attempts. Uses Hangfire's in-memory
    test context (`PerformContext` / `ElectStateContext` substitutes).
- **Persistence** (real SQL Server):
  - Concurrent upserts for the same key → 1 incident.
  - The resolved → recurrence path.
  - The cap and counters.
  - Participants distinct under the cap.
  - The retention windows (SC-008).
  - Anonymise (SC-012).
  - The stored-reason secret scan.
  - The 100k-occurrence list timing (SC-007), under the existing scale-test gate
    `RUN_SCALE_PERFORMANCE_TESTS=1` (memory note).
- **Web**:
  - Calm text for every kind × surface as a non-admin (SC-001), and admins unchanged (FR-003), across chat, voice, image, workflow, agent and document failures.
  - All three investigation routes: 403 without view, null content without the grant, read-only.
  - The role and MCP audit logs carry the request's correlation id.
  - The per-engine matrix → exactly one occurrence with the log correlation id (SC-002).
  - The 2026-09-22 replay (SC-003).
  - Store-unavailable → same response plus a log line (SC-004).
  - Access matrix inclusions and exclusions (SC-013), with ownership still 404.
  - Content gating (SC-009).
  - Role grant paths (SC-010).
  - Root-cause bulk resolve for N = 1,000 (SC-011).
- **Frontend**:
  - The page's filters to URL params.
  - Paging, drawer and transitions, including the 409 message.
  - The badge's hide-at-zero, error state and invalidation.
  - Deep-link `?select=` on the three target pages, and `?search=` on the users page.
  - Access incidents show distinct sources and accounts.
  - The investigation pages have no write controls and render the `null`-content state.
  - The role picker's disabled controlled key and the Super-User-only switch.
  - Axe a11y tests for the new page, following the existing `*.a11y.test.tsx` pattern.
  - The **full** suite is run.

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
|---|---|---|
| One built-in role (Administrator) no longer gets the *entire* catalogue automatically: `content.view` is excluded unless a Super User stored it (D14; ADR 0017) | FR-016g. Content access must be delegable by Super Users only. Today "built-in ⇒ `PermissionSet.Full`" would hand it to every Administrator. | Making `content.view` a Super-User-role check instead of a permission: the user explicitly asked for "a permission that can be only assigned by the super user" (clarification 2). Removing it from the catalogue and special-casing it everywhere would scatter the rule. The resolver exception is one guarded line with a named set (`SuperUserControlledKeys`). |
| The access-event write is on the request path, synchronously, for content views (D15; ADR 0017), unlike every other write in this feature | FR-016c. An audit of a privileged read that could be dropped is not an audit. | The non-blocking recorder: it can drop under load (D2), which is acceptable for failure triage but not for a record of who read someone's conversation. |
