---

description: "Task list for 067 Notifications & Communication Hub"
---

# Tasks: Notifications & Communication Hub

**Input**: Design documents from `/specs/067-notifications-communication-hub/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md) (R1–R26), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required. Constitution §10 applies, and the spec's success criteria (SC-003, SC-004, SC-006, SC-007, SC-009, SC-011–SC-016) are defined as automated suites in [quickstart.md §2](quickstart.md). Test tasks come first in each phase and should fail before implementation.

**Organization**: Tasks are grouped by user story. Two stories are split across phases because of the delivery slices in [plan.md](plan.md#delivery-slices):
- **US9**: the legacy import ships with slice 1 (FR-009a, atomic with the retirement), while the account emails need the email channel (US3).
- **US8**: the user side is slice 5 and the admin-area Arabic is slice 6.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task in the same phase).
- **[Story]**: the user story the task belongs to (US1–US9). Setup, Foundational and Polish tasks have no story label.
- Paths are repository-relative. `ClientApp/` means `src/AskLucy.Web/ClientApp/`.

## Standing rules for every task

These apply to every task below, and each one assumes them:

1. **Clean Architecture**: Application never references EF Core, not even as a package. For concurrency use a generic `Exception`, and for monotonic bookkeeping writes use `ExecuteUpdateAsync` in Persistence.
2. **No silent failures**: Principle VIII in `.claude/CLAUDE.md`.
   - Backend: every catch logs, and either rethrows or returns a failure the caller can see.
   - Frontend: every query, mutation and hub handler has a visible error path (a toast, an inline error or a retry affordance).
3. **`INotificationPublisher.Publish` rules**: it is called *before* the caller's own `SaveChangesAsync`, in the same unit of work, and never wrapped in try/catch ([contracts/module-integration.md](contracts/module-integration.md)).
4. **Migrations**:
   - Create with `dotnet ef migrations add <Name> --project src/AskLucy.Persistence --startup-project src/AskLucy.Web`.
   - The file has no UTF-8 BOM, and `System` usings come first.
   - `Down()` fully reverses `Up()`.
   - The shared persistence-test DB must be migrated by hand.
5. **Options**: every options property has a safe default. Never use `ValidateOnStart()` on a required option with no default: it crashes the whole host.
6. **DI**: never use self-referential `sp => sp.GetRequiredService<T>()` factories. They hide dependency cycles from startup validation.
7. **Background work**: create an `IServiceScopeFactory` scope per batch, so hosted services never share a request `DbContext`.
8. **Verification**:
   - Frontend type-checks use `npx tsc -b --noEmit`. A bare `tsc --noEmit` is a silent no-op here.
   - Run the **full** vitest suite. Page-level tests catch component changes that the touched file's own tests miss.
   - `Web.Tests` needs `PERSISTENCE_TESTS_CONNECTION_STRING`. `Persistence.Tests` uses its own `PERSISTENCE_TESTS_2_CONNECTION_STRING` (the dedicated test2 database) and runs in CI, so migrate test2 by hand after each new migration or CI fails.
   - In `Web.Tests`, never call `WithWebHostBuilder` per test (it boots a new host plus Hangfire each time and stalls the suite). Use a derived factory as the `IClassFixture`.
9. **jsdom traps**:
   - Inside open MUI dialogs, use `getByText`, not `getByRole`.
   - Guard `setPointerCapture` with `?.`.
   - MSW's `onUnhandledRequest: 'bypass'` lets a missing handler hit the real network, so add a handler for every new endpoint a test renders.
10. **Hosted services**: .NET 10 can skip `ExecuteAsync` on a fast Start→Stop, so every `BackgroundService` here drains or releases its claimed work in `StopAsync`, not only at the end of `ExecuteAsync`.
11. **Rate limits**: partition "per-user" policies with `RateLimitPartitions.UserOrClientKey(context)`. Our JWTs carry no `name` claim, so `Identity.Name` is null and would silently make the limit per-IP.
12. **SignalR hooks**: new hub hooks use `keepHubConnected` from `ClientApp/src/api/hubConnection.ts`, which re-reads the token and retries start and close.
13. **Operational failures (spec 074)**: a dead-lettered delivery, an `AmbiguousOutcome`, and a failed indexing job each also call `IOperationalFailureRecorder.Record(...)` after logging, so they show on the admin failure trail.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuration and design records that every later phase reads.

- [X] T001 Create `src/AskLucy.Application/Options/NotificationsOptions.cs`. Follow the style of `AppOptions.cs`, and give every property a default:
  - `Dispatch`: `PollIntervalSeconds=1`, `IdlePollIntervalSeconds=5`, `BatchSize=50`, `LeaseMinutes=2`.
  - `Retry`: `MaxAttempts=5`, `DelaysMinutes=[1,4,10,20,30]`, `CriticalDelaysSeconds=[30,60,120,300,600]`.
  - `Email`: `MaxPerMinute=60`, `ReservedPerMinuteForMandatory=20`, `SendTimeoutSeconds=60`.
  - `Retention`: `ReadDays=90`, `DeletedDays=30`, `FailedDays=30`, `DeliveryDays=90`, `CompletedOutboxDays=7`, `BatchSize=1000`.
  - `Channels`: `InApp.Enabled=true`, `Email.Enabled=true`.
  - `Center`: `DefaultPageSize=25`, `MaxPageSize=100`.
  - `HealthChecks`: `HeartbeatStaleSeconds=30`, `BacklogDegradedMinutes=5`, `BacklogUnhealthyMinutes=30`, `SmtpProbeCacheMinutes=5`.
- [X] T002 [P] Add a `"Notifications"` section to `src/AskLucy.Web/appsettings.json` with the defaults from the task above. Add `Email.MaxPerMinute=600` to `src/AskLucy.Web/appsettings.Development.json`, so local runs aren't throttled.
- [X] T003 [P] Write `docs/adr/0018-transactional-notification-outbox.md` (context, decision, consequences). It records:
  - the transactional outbox and why Hangfire enqueue isn't transactional with EF;
  - the two `BackgroundService` workers, the wake signal and the lease-based claim (R2–R4);
  - at-most-once email with `AmbiguousOutcome` (R5);
  - Hangfire kept for recurring jobs only.

**Checkpoint**: Options bind and the app boots unchanged.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain model, catalogue, schema, outbox, dispatcher and realtime hub that every story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests for Foundational (write first)

- [X] T004 [P] `NotificationRouterTests` in `tests/AskLucy.Domain.Tests/Notifications/NotificationRouterTests.cs`. Cover the full FR-003 decision table (SC-009):
  - mandatory pairs ignore preference overrides;
  - optional pairs follow overrides, falling back to catalogue defaults;
  - a channel that is disabled or has no registered sender gives `Skipped(ChannelDisabled)`;
  - email-only types never produce `InApp`;
  - `system.announcement.published` email only when `IsCritical`, otherwise `Skipped(NotCritical)`;
  - a missing verified address gives `Skipped(NoVerifiedAddress)`;
  - preference precedence ([research.md R28](research.md)): a saved `(category, channel)` override switches **every optional type** in that category that supports the channel, on or off; with no saved override, each type uses its own catalogue default (so `workflow.execution.failed` emails and `workflow.execution.completed` doesn't); an override never affects a mandatory pair, and never adds a channel a type marks `—`.
- [X] T005 [P] `NotificationTypeCatalogTests` in `tests/AskLucy.Domain.Tests/Notifications/NotificationTypeCatalogTests.cs`. Assert:
  - keys are unique and every row of the [data-model.md catalogue](data-model.md#notification-type-catalogue) is present;
  - Security types have `MinimizeSensitiveContent`;
  - Account types have `ShowInCenter=false` and are email-only;
  - FR-005 types (`conversation.*`, `billing.*`) and `knowledge-base.updated` (R27) are defined but flagged not-emitted, and every other type is emitted;
  - every declared variable name is a valid token;
  - every route template resolves against the reconciled routes (see the route-reconciliation task below).
- [X] T006 [P] `NotificationStateMachineTests` in `tests/AskLucy.Domain.Tests/Notifications/NotificationStateMachineTests.cs`. Cover every allowed and forbidden transition of `Notification.Status`, `NotificationDelivery.Status`, `NotificationTemplateVersion.Status` and `NotificationOutboxEvent.Status` ([data-model.md § State machines](data-model.md#state-machines)). A forbidden transition throws `DomainRuleViolationException`.
- [X] T007 [P] `TemplateTokenParserTests` in `tests/AskLucy.Domain.Tests/Notifications/TemplateTokenParserTests.cs`. Cover:
  - `{{ var }}` with whitespace variants;
  - malformed `{{`/`}}`;
  - an unknown variable, reported with the offending token;
  - raw URL and HTML detection in text fields;
  - `actionUrl` accepted only in link fields.
- [X] T008 [P] `NotificationPublisherTests` in `tests/AskLucy.Application.Tests/Notifications/NotificationPublisherTests.cs`:
  - an unknown type throws;
  - an undeclared variable throws;
  - `Users` with more than 100 ids throws;
  - `Audience` from a non-announcement type throws;
  - the event is added to the unit of work without `SaveChangesAsync` being called;
  - the correlation id is captured.
- [X] T009 [P] `OutboxDispatchServiceTests` in `tests/AskLucy.Application.Tests/Notifications/OutboxDispatchServiceTests.cs`, using fakes:
  - an event materializes one `Notification` with an `InApp` delivery `Delivered` and an `Email` delivery `Pending` or `Skipped` per the router;
  - a duplicate `EventKey` materializes nothing;
  - an inactive recipient gets a cancelled delivery;
  - when `INotificationAccessCheck` denies, no notification is created and the outcome is logged;
  - a template render error gives `Failed(RenderError)` and is logged;
  - the realtime push happens only after the commit succeeds.
- [X] T010 [P] `NotificationOutboxClaimTests` in `tests/AskLucy.Persistence.Tests/Notifications/NotificationOutboxClaimTests.cs`:
  - two concurrent claimers never claim the same event;
  - an expired lease is reclaimable;
  - completing requires the claiming worker's lease.
- [X] T011 [P] `InAppTemplateRenderTests` and `NotificationLinkBuilderTests` in `tests/AskLucy.Infrastructure.Tests/Notifications/`. Cover:
  - a missing value uses the fallback and logs a warning;
  - the output is plain text, never HTML;
  - route placeholders are substituted;
  - external links are rejected unless `AllowsExternalLink`;
  - absolute URLs are built from `AppOptions.FrontendBaseUrl`.

  *Done. Also added `NotificationTemplateSeederTests` (`LoadSeeds()` parses to a real catalogue type/channel, covers every emitted in-app type except the deliberately-dormant `knowledge-base.updated`, and has no duplicate keys).*

### Domain

- [X] T012 [P] Create one file per enum in `src/AskLucy.Domain/Notifications/Enums/`, with values exactly as in [data-model.md § Enumerations](data-model.md#enumerations):
  - `NotificationCategory`, `NotificationPriority`, `NotificationStatus`, `NotificationChannel`
  - `DeliveryStatus`, `DeliveryFailureKind`, `DeliverySkipReason`, `RecipientKind`
  - `DeliveryFrequency`, `TemplateVersionStatus`, `OutboxEventStatus`
  - `AnnouncementKind`, `AnnouncementAudience`
- [X] T013 Route reconciliation. The catalogue routes in data-model.md don't match `ClientApp/src/routes/router.tsx`:
  - executions are `/agents/:agentId/executions/:executionId` and `/workflows/:workflowId/executions/:executionId`;
  - `/documents/{id}`, `/memory/{id}` and `/settings/security` don't exist, and Settings selects its tab via `location.state`, which an email link can't carry.

  Update the Route column in `specs/067-notifications-communication-hub/data-model.md` to:
  - `/agents/{parentId}/executions/{id}` and `/workflows/{parentId}/executions/{id}`, plus `?approval={approvalId}` for the approval types;
  - `/documents?documentId={id}`;
  - `/knowledge-bases/{id}`;
  - `/memory?memoryId={id}`;
  - `/settings?tab=security`.

  Add an optional `ParentId` to `RelatedItem` in `contracts/module-integration.md`, and record the change as an addendum in `research.md` under R11.

  **T013 blocks T014, T047, T050 and T092**, which read the reconciled routes; run it before any of them even though they are marked [P] against each other.
- [X] T014 [P] (after T013) Create `NotificationTypeDefinition.cs` and `NotificationTypeCatalog.cs` in `src/AskLucy.Domain/Notifications/`:
  - The definition fields are those of R7, plus `ShowInCenter` and `IsEmitted`.
  - The catalogue holds every row of the reconciled catalogue table (the route-reconciliation task above), and exposes `Get(key)`, which throws for an unknown key, `TryGet` and `All`.
  - Define a `NotificationTypeKeys` constants class so emitters never use string literals.
- [X] T015 [P] Create the `Notification.cs` aggregate root and its child `NotificationDelivery.cs` in `src/AskLucy.Domain/Notifications/`:
  - Fields are per data-model.md.
  - `Notification` methods: `Create`, `AddDelivery`, `MarkRead`, `DeleteByOwner`, `Expire`, `Cancel`, and `RecomputeStatus` (status is aggregated from the deliveries, R8).
  - `NotificationDelivery` methods: `MarkSending`, `MarkSent`, `MarkDelivered`, `Skip(reason)`, `ScheduleRetry(nextAttemptAt, kind, safeReason, providerResponse)`, `Fail(kind, …)`, `DeadLetter`, `Cancel`, `Expire` and `ResetForRetry`.
  - Both entities inherit `BaseEntity` and carry `RowVersion`.
- [X] T016 [P] Create `NotificationOutboxEvent.cs` in `src/AskLucy.Domain/Notifications/`:
  - Fields: `Type`, the recipient payload JSON, the variables JSON, `RelatedItem`, `EventKey`, `Language`, `CorrelationId`, `Attempts`, `NextAttemptAtUtc`, `LeaseOwner`, `LeaseExpiresAtUtc` and `FanOutCursor`.
  - The status machine is `Pending → Processing → Completed | Failed`.
- [X] T017 [P] Create `NotificationTemplate.cs` and `NotificationTemplateVersion.cs` in `src/AskLucy.Domain/Notifications/`:
  - Versions carry the email fields `Subject`, `Preheader`, `Greeting`, `Heading`, `BodyParagraphs`, `ActionLabel`, `SafetyNote` and `FooterNote`, and the in-app fields `Title`, `Message` and `ActionLabel`.
  - Enforce the length limits.
  - Only a `Draft` can be edited. `Publish` archives the previous published version. `Archive` refuses when it would leave a shipped default with no published version.
- [X] T018 [P] Create `NotificationPreference.cs`, a sparse override of `UserId`, `Category`, `Channel`, `Enabled` and `Frequency`. Only `Immediate` is accepted.
- [X] T019 [P] Create `SystemAnnouncement.cs` and `NotificationAuditLog.cs` in `src/AskLucy.Domain/Notifications/`, per data-model.md. The announcement is immutable after `Publish`. The audit log is append-only and has no mutators.
- [X] T020 [P] Create `TemplateTokenParser.cs` in `src/AskLucy.Domain/Notifications/`. It is pure: `Parse`, `ValidateAgainst(declaredVariables)` and `ContainsRawUrlOrHtml`.
- [X] T021 Create `NotificationRouter.cs` in `src/AskLucy.Domain/Notifications/`. It is a pure function:

  `(definition, recipientState, preferenceOverrides, availableChannels, isCritical) → IReadOnlyList<ChannelDecision>`

  `availableChannels` = the registered senders that are enabled in config, plus `InApp`. It depends on the catalogue, enum and preference tasks above.
- [X] T022 [P] Add `AdminArea.Notifications` to `src/AskLucy.Domain/Authorization/AdminArea.cs`, and add `admin.notifications.view` and `admin.notifications.manage` to `src/AskLucy.Domain/Authorization/AdminPermissionCatalog.cs`. `PermissionCatalogReconciler` syncs them at startup.

### Application abstractions and core services

- [X] T023 [P] Create `INotificationPublisher.cs` in `src/AskLucy.Application/Notifications/Abstractions/`. It contains `NotificationRequest`, `NotificationRecipient` (`User`, `Users`, `Audience`, `AddressForUser`, `AddressLookup`, `SupportMailbox`) and `RelatedItem(Type, Id, ParentId?)`, verbatim per [contracts/module-integration.md](contracts/module-integration.md).
- [X] T024 [P] Create one file per hub-internal interface in `src/AskLucy.Application/Notifications/Abstractions/`:
  - `INotificationOutboxStore`, `INotificationRepository`, `INotificationTemplateRepository`, `INotificationPreferenceRepository`
  - `IEffectiveLanguageResolver`, `INotificationTemplateRenderer` (with `RenderedInApp` and `RenderedEmail`), `INotificationLinkBuilder`
  - `INotificationRealtimePublisher`, `INotificationAccessCheck` (`ItemType`, `CanAccessAsync(userId, id)` and `GetAvailableAsync(ids)`)
  - `INotificationWakeSignal`, `INotificationAuditWriter`
  - *Done. Also added: `INotificationRecipientDirectory`, `INotificationChannelRegistry`, `INotificationAuditLogRepository` and `NotificationRecipientJson`. `RenderedEmail`/`RenderEmailAsync` are deferred to US3 (T085), where the email path lands.*
- [X] T025 [P] Add `ICorrelationIdAccessor` to `src/AskLucy.Application/Abstractions/ICorrelationIdAccessor.cs`, implemented in `src/AskLucy.Web/Middleware/HttpCorrelationIdAccessor.cs`:
  - It reads `HttpContext.Items[CorrelationIdMiddleware.HeaderName]`.
  - With no HTTP context, it falls back to `Activity.Current?.TraceId`, then to a new GUID v7.
  - *Already existed as `ICorrelationIdAccessor` / `Web/Middleware/CorrelationIdAccessor.cs` (a singleton). The trace and GUID v7 fallbacks live in `NotificationPublisher` instead.*
- [X] T026 Implement `NotificationPublisher` in `src/AskLucy.Application/Notifications/NotificationPublisher.cs`. It:
  - validates the type, the declared variables and the recipient shape;
  - serializes an outbox event and adds it through `INotificationOutboxStore.Add`;
  - performs no I/O.
- [X] T027 Implement `OutboxDispatchService` in `src/AskLucy.Application/Notifications/Processing/OutboxDispatchService.cs`. It:
  - claims a batch;
  - resolves `User`, `Users`, `AddressForUser` and `SupportMailbox` recipients. `AddressLookup` and `Audience` are added later by US9 and US6.
  - checks the recipient is active, plus `INotificationAccessCheck` when `RequiresItemAccess`;
  - de-duplicates on `EventKey`;
  - runs `NotificationRouter`;
  - resolves the language and renders in-app;
  - materializes the notification and its deliveries, then saves;
  - after the commit, pushes `notificationCreated` and `unreadCountChanged`;
  - completes the event.

  On failure it releases the event with backoff and logs `Type`, `EventKey` and `CorrelationId`.

  *Done, split to avoid a god class: `OutboxDispatchService` (claim, per-event scope, release with 10 s × 2ⁿ backoff capped at 15 min, operational-failure record after 10 attempts), `OutboxEventProcessor` (scoped; recipients, active and access re-checks, de-duplication, one commit), `NotificationMaterializer` (route, language, render, deliveries) and `NotificationCreatedPusher` (post-commit push, which carries the unread count). The access check fails closed: a related-item type with no registered `INotificationAccessCheck` throws and retries, so every emitting module must register one.*
- [X] T028 [P] Implement `NotificationAuditWriter` in `src/AskLucy.Application/Notifications/NotificationAuditWriter.cs`. It adds `NotificationAuditLog` rows to the current unit of work, with the actor from `ICurrentUserAccessor` (null for system) and the correlation id.

### Persistence

- [X] T029 [P] Add `NotificationConfiguration.cs` and `NotificationDeliveryConfiguration.cs` in `src/AskLucy.Persistence/Configurations/Notifications/`:
  - a soft-delete query filter on the owner delete, and `RowVersion`;
  - the filtered covering center index `(RecipientUserId, CreatedAtUtc DESC, Id DESC) WHERE DeletedAtUtc IS NULL AND ShowInCenter = 1`, plus an unread filtered index (R19);
  - the due-queue index `(Status, NextAttemptAtUtc)`, filtered to `Pending`/`Retrying`.

  *Deviation: the due-queue index also includes `Priority` (not just `Status`/`NextAttemptAtUtc`) so the dispatcher can order a claimed batch by priority without a second lookup; it is not part of the key, only an included column.*
- [X] T030 [P] Add `NotificationOutboxEventConfiguration.cs` in `src/AskLucy.Persistence/Configurations/Notifications/`: a **non-unique** filtered index on `EventKey` (data-model.md: de-duplication is enforced on `Notifications`, so the outbox may hold repeats), and a pending/lease index.
- [X] T031 [P] Add `NotificationTemplateConfiguration.cs` and `NotificationTemplateVersionConfiguration.cs`:
  - templates are unique on `(Type, Channel, Language)`;
  - a filtered unique index allows at most one `Published` version per template;
  - `RowVersion`.
- [X] T032 [P] Add `NotificationPreferenceConfiguration.cs` (unique on `(UserId, Category, Channel)`), `SystemAnnouncementConfiguration.cs` and `NotificationAuditLogConfiguration.cs`, all in `src/AskLucy.Persistence/Configurations/Notifications/`.
- [X] T033 Register `DbSet`s in `src/AskLucy.Persistence/AskLucyDbContext.cs` for the 6 roots and standalone entities only: `Notifications`, `NotificationOutboxEvents`, `NotificationTemplates`, `NotificationPreferences`, `SystemAnnouncements` and `NotificationAuditLogs`. The children `NotificationDelivery` and `NotificationTemplateVersion` get **no** `DbSet` (constitution §5). Their configurations are still applied, and only their aggregate's repository reaches them, through the parent's navigation or `Set<T>()`.
- [X] T034 Create the migration `AddNotificationHubCore` in `src/AskLucy.Persistence/Migrations/`. It adds `Notifications`, `NotificationDeliveries` and `NotificationOutboxEvents`, with their indexes.
- [X] T035 Create the migration `AddNotificationTemplates` in `src/AskLucy.Persistence/Migrations/`. It holds schema only; the content comes from the seeder.
- [X] T036 Create the migration `AddNotificationPreferences` in `src/AskLucy.Persistence/Migrations/`.
- [X] T037 Create the migration `AddSystemAnnouncementsAndNotificationAudit` in `src/AskLucy.Persistence/Migrations/`.

  *Deviation (T034–T037): all four schemas shipped as one migration, `20260928041922_AddNotificationHub`, instead of four. `SystemAnnouncement` and the templates/preferences tables have FK cycles back through `Notifications`/`NotificationDeliveries` (e.g. a delivery can reference an announcement, an announcement audit row references a template), so splitting them into separately-applied migrations would leave an intermediate migration with a dangling FK. Applied to the shared test2 DB.*
- [X] T038 [P] Implement `NotificationOutboxRepository` (`INotificationOutboxStore`) in `src/AskLucy.Persistence/Repositories/NotificationOutboxRepository.cs`:
  - `Add`;
  - `ClaimBatchAsync(workerId, lease, batchSize)` as a conditional `ExecuteUpdateAsync` and re-read (R4);
  - `CompleteAsync`;
  - `ReleaseAsync(nextAttemptAt)`.

  *Deviation: the class is `NotificationOutboxStore` (matches its interface name, `INotificationOutboxStore`, rather than the `…Repository` convention used elsewhere). `CompleteAsync`/`ReleaseAsync` are the domain methods (`Complete`, `Release`) plus a save, not repository-only logic. Also added `GetClaimedAsync` and `ReleaseClaimsAsync`, both needed by `NotificationOutboxDispatcher.StopAsync` to hand back an in-flight lease on shutdown.*
- [X] T039 [P] Implement `NotificationRepository` (`INotificationRepository`) in `src/AskLucy.Persistence/Repositories/NotificationRepository.cs`, with `Add`, `GetByIdAsync` and `ExistsByEventKeyAsync`. Center queries are added in US1, and the delivery queue in US3.

  *Deviation: method names differ slightly from the literal list above; the behavior (add, id lookup, event-key existence check) is unchanged.*
- [X] T040 [P] Implement `NotificationTemplateRepository` in `src/AskLucy.Persistence/Repositories/NotificationTemplateRepository.cs`. It looks up the published version by `(type, channel, language)` with an `en` fallback.

  *Deviation: `GetPublishedVersionAsync` is an exact `(type, channel, language)` match with no fallback here; the `en` fallback moved to `LogicFreeTemplateRenderer` (T048), which is also where it gets logged. The repository also gained `GetExistingKeysAsync` and `Add`, both needed by `NotificationTemplateSeeder` (T049).*
- [X] T041 [P] Implement `NotificationPreferenceRepository` in `src/AskLucy.Persistence/Repositories/NotificationPreferenceRepository.cs`, with `GetOverridesAsync(userId)` and `GetOverridesForUsersAsync(userIds)`.
- [X] T042 Implement `NotificationWakeInterceptor` in `src/AskLucy.Persistence/Interceptors/NotificationWakeInterceptor.cs`. In `SavedChangesAsync`, it pulses `INotificationWakeSignal` when the saved change set added any `NotificationOutboxEvent` or `NotificationDelivery`. Register it next to `AuditSaveChangesInterceptor`.
- [X] T043 Register the repositories and the interceptor in `src/AskLucy.Persistence/DependencyInjection.cs`.

  *Extras beyond the literal task list: `NotificationRecipientDirectory` and `NotificationAuditLogRepository` were also implemented and registered here (both declared in T024's "Also added").*

### Infrastructure

- [X] T044 [P] Implement `NotificationWakeSignal` in `src/AskLucy.Infrastructure/Notifications/NotificationWakeSignal.cs`. It is a singleton `SemaphoreSlim` wrapper with separate wait handles for the dispatcher and the delivery worker.
- [X] T045 [P] Implement `NotificationMetrics` in `src/AskLucy.Infrastructure/Notifications/NotificationMetrics.cs`, using the Meter `AskLucy.Notifications`:
  - counters `notifications.created`, `deliveries.sent`, `deliveries.failed`, `deliveries.retried` and `deliveries.dead_lettered`;
  - the counter `deliveries.provider_errors`, tagged by `channel` and `failure_kind` (FR-057);
  - the histogram `deliveries.latency_ms`;
  - an observable backlog gauge, and an observable `notifications.unread` gauge read from a cached (60 s) count (FR-057).

  *Deviation: added the Application-side abstraction `INotificationMetrics` (`NotificationMetrics` implements it) so `OutboxEventProcessor` can record metrics without Application referencing Infrastructure. The backlog and unread gauges report nothing until fed via `ReportBacklog`/`ReportUnread`; they are not self-polling 60 s caches here — the US3 health check (which already owns the DB query) is what will call them, so the count isn't computed twice.*
- [X] T046 [P] Implement `EffectiveLanguageResolver` in `src/AskLucy.Infrastructure/Notifications/EffectiveLanguageResolver.cs`. For now it returns the explicit `Language` when it is `en`, and otherwise `en`. US8 replaces this with the full FR-044 chain.
- [X] T047 [P] (after T013) Implement `NotificationLinkBuilder` in `src/AskLucy.Infrastructure/Notifications/NotificationLinkBuilder.cs`:
  - it builds the relative route for in-app and the absolute `AppOptions.FrontendBaseUrl` + route for email;
  - it substitutes `{id}`, `{parentId}` and query tokens, URL-encoded;
  - it rejects any external host.

  *Deviation: a route token the related item can't fill (e.g. no `parentId`) returns `null` and logs a warning, rather than throwing — the notification is still shown without its action link, per the spec's edge-case handling. An unknown token name in the route template itself (a code bug, not a data gap) still throws.*
- [X] T048 Implement the in-app path of `LogicFreeTemplateRenderer` in `src/AskLucy.Infrastructure/Notifications/Templates/LogicFreeTemplateRenderer.cs`. It uses `TemplateTokenParser`, gives plain text only, and applies the fallback-and-warn rule. The email path is added in US3.

  *Deviation: also truncates the rendered title/message/action label to `Notification.TitleMaxLength`/`MessageMaxLength`/`ActionLabelMaxLength` with an ellipsis, so a long substituted value (e.g. a document name) can't push the row past its column limit.*
- [X] T049 Implement `NotificationTemplateSeeder` in `src/AskLucy.Infrastructure/Notifications/Templates/NotificationTemplateSeeder.cs`:
  - It is an idempotent startup hosted service.
  - It reads embedded JSON `Seed/{lang}/{type}.{channel}.json`.
  - It creates the template and a published v1 only when the template is missing, and never overwrites.
  - Add `<EmbeddedResource Include="Notifications\Templates\Seed\**\*.json" />` to `src/AskLucy.Infrastructure/AskLucy.Infrastructure.csproj`.

  *Deviation: the embedded resource uses `LogicalName="NotificationSeed/%(RecursiveDir)%(Filename)%(Extension)"` so the resource name is stable regardless of the build machine's path separator. A single seed file that fails to parse or violates a domain rule is logged and skipped; it never stops the rest from installing or crashes the host. `PublishedBy` is `system:template-seeder`.*
- [X] T050 [P] (after T013) Create the English in-app seed files in `src/AskLucy.Infrastructure/Notifications/Templates/Seed/en/`, as `{type}.inapp.json`, one per in-app type:
  - `agent.execution.{started,completed,failed}`, `agent.approval.requested`
  - `workflow.execution.{started,completed,failed,paused}`, `workflow.approval.requested`
  - the 9 `document.*` types and the 2 emitted `knowledge-base.indexing.*` types (`knowledge-base.updated` is not emitted, R27, and gets no seed)
  - `memory.{auto-created,auto-approved,conflict.confirmation-needed}`
  - `security.two-factor.{enabled,disabled}`, `security.recovery-codes.regenerated`
  - `system.announcement.published`

  Base the wording on the current legacy messages where they exist (`ProcessingNotifier`, `MemoryNotifier`).

  *Done: 27 files, verified by `NotificationTemplateSeederTests` against the live catalogue.*
- [X] T051 Implement `NotificationHub` in `src/AskLucy.Infrastructure/Notifications/NotificationHub.cs`:
  - `[Authorize]`;
  - `OnConnectedAsync` adds the connection to the group `user:{userId}`;
  - there are no client-to-server methods (R1).

  Also implement `SignalRNotificationRealtimePublisher` in `src/AskLucy.Infrastructure/Notifications/SignalRNotificationRealtimePublisher.cs`, which sends `notificationCreated`, `notificationUpdated` and `unreadCountChanged` with the payloads in [contracts/notification-hub.md](contracts/notification-hub.md).
- [X] T052 Implement the `NotificationOutboxDispatcher` `BackgroundService` in `src/AskLucy.Infrastructure/Notifications/Workers/NotificationOutboxDispatcher.cs`:
  - Each loop waits for the wake signal or the poll interval, whichever comes first, and runs `OutboxDispatchService` in a new scope.
  - Worker id is `{machine}:{pid}:{guid}`.
  - It records a heartbeat timestamp for the health check.
  - It catches and logs per-iteration exceptions with backoff, and never exits silently.
  - `StopAsync` releases any claimed-but-unfinished events (standing rule 10).

  *Deviation: the heartbeat lives in a new singleton, `NotificationWorkerHeartbeats`, rather than on the dispatcher instance itself, so the US3 health check can read it without resolving the hosted service (a self-referential factory would otherwise be needed).*
- [X] T230 Add the view-audit pipeline behavior (constitution §3: queries never mutate state, and cross-cutting logging lives in `IPipelineBehavior`):
  - the marker `IAuditedAdminView` in `src/AskLucy.Application/Notifications/Abstractions/`, exposing `AuditAction`, `TargetType` and `TargetId`;
  - `AdminViewAuditBehavior<TRequest, TResponse>` in `src/AskLucy.Application/Common/Behaviors/`. For a request with the marker, after the handler succeeds, it writes the `…Viewed` row through `INotificationAuditWriter` in its own `IServiceScopeFactory` scope and save, at most once per admin, per resource, per hour (an `IMemoryCache` key, plus a DB check on a miss). If the audit write fails, the failure is logged and surfaced as a 500 Problem Details; it is not swallowed.
  - Register it in `src/AskLucy.Application/DependencyInjection.cs` and add `AdminViewAuditBehaviorTests` in `tests/AskLucy.Application.Tests/Notifications/`.

  The admin query handlers (T158, T159, T161, T176) implement the marker and contain no audit code.

  *Deviation: the behavior lives in `src/AskLucy.Application/Behaviors/`, this codebase's actual home for `IPipelineBehavior`s (`LoggingBehavior` etc.), not `Common/Behaviors` as written above — there is no `Common/Behaviors` folder in this codebase. The marker's property names are `AuditAction`/`AuditTargetType`/`AuditTargetId` (matching `NotificationAuditLog`'s own field names) rather than the literal `AuditAction`/`TargetType`/`TargetId`. Added the 8 `…Viewed` values to `NotificationAuditAction` (`NotificationAuditLog.cs`) and updated data-model.md's enum list to match.*
- [X] T053 Register the Foundational services in `src/AskLucy.Application/DependencyInjection.cs` (publisher, dispatch service, audit writer, options binding) and in `src/AskLucy.Infrastructure/DependencyInjection.cs` (wake signal as a singleton, metrics, resolver, link builder, renderer, seeder, realtime publisher). In `src/AskLucy.Web/Program.cs`:
  - register `AddHostedService<NotificationOutboxDispatcher>` near `PermissionCatalogReconciler` (~L230);
  - add `app.MapHub<NotificationHub>("/hubs/notifications")` beside the existing `MapHub` calls (~L791–797);
  - register `HttpCorrelationIdAccessor`.

  *Deviation: `HttpCorrelationIdAccessor` was already registered under the name `CorrelationIdAccessor` (see T025's note) — nothing further to add. `NotificationOutboxDispatcher` is registered in `Program.cs` itself (not `Infrastructure/DependencyInjection.cs`), so the test host can swap it for a manually-driven pass, matching how other per-request background workers are registered in this codebase.*
- [X] T054 Boot verification. Run `dotnet build "Ask Lucy.sln" -warnaserror`, then the full `tests/AskLucy.Web.Tests` suite. `CustomWebApplicationFactory` boots the real host, so it catches options validation failures and DI cycles that unit tests miss.

**Checkpoint**: Publishing a request in a test host produces a materialized in-app notification and a realtime push. User stories can start.

---

## Phase 3: User Story 1 - Notification Center (Priority: P1) 🎯 MVP

**Goal**: A signed-in user sees a live unread count on a bell in the header, opens a popover or a full center, filters and pages through their notifications, opens details, follows the related action, and marks them read (one or all) or deletes them.

**Independent Test**: Seed notifications (read and unread, several categories and priorities) for a user through the repository. Verify the bell count, the list, grouping, filtering, details, action navigation, mark-read, mark-all-read, delete and keyset paging. Publish one more notification and verify it arrives live with no reload. No email or preference functionality is needed.

### Tests for User Story 1

- [X] T055 [P] [US1] `NotificationCenterQueryTests` in `tests/AskLucy.Persistence.Tests/Notifications/NotificationCenterQueryTests.cs`:
  - the keyset order is stable while new rows are inserted;
  - the category and state filters work;
  - owner-deleted and `ShowInCenter=false` rows are excluded;
  - mark-all-read touches only the caller's rows, optionally filtered by category.
  - Runs against the real site4now.net test2 DB, gated by `PERSISTENCE_TESTS_2_CONNECTION_STRING` like every other `Persistence.Tests` suite; skips cleanly (4/4) when unset, as it was in this session. `dotnet build` clean.
- [X] T056 [P] [US1] `NotificationsEndpointsTests` in `tests/AskLucy.Web.Tests/Notifications/NotificationsEndpointsTests.cs`. Cover every endpoint in [contracts/notifications-api.md](contracts/notifications-api.md) (the center section):
  - status codes 200/204/400/401/404/429;
  - another user's id returns 404;
  - a deleted notification returns 404;
  - `title` and `message` are never HTML;
  - `relatedItem.available=false` for a deleted item;
  - a malformed cursor returns 400.
  - Deviation: built as `IClassFixture<NotificationsApiFactory>` (one real host shared by the whole class) rather than per-test `WithWebHostBuilder` — the latter stalled 5+ minutes (a documented Web.Tests anti-pattern: a fresh host + Hangfire server per test). `NotificationsApiFactory` mirrors `CustomModelsApiFactory` exactly. 429 is deliberately not exercised in-process, matching `AnalyticsControllerTests`. 15/15 pass in ~26s.
- [X] T057 [P] [US1] `NotificationCenterHandlerTests` in `tests/AskLucy.Application.Tests/Notifications/NotificationCenterHandlerTests.cs`:
  - mark-read is idempotent and pushes `notificationUpdated` and `unreadCountChanged`;
  - mark-all-read pushes `unreadCountChanged` exactly once;
  - delete pushes `notificationUpdated` with `deleted: true`.
  - "Pushes notificationUpdated and unreadCountChanged" is realized as the single `NotificationUpdatedAsync(..., unreadCount, ...)` call the handler actually makes (the DTO carries the fresh count, not a second push) — matches `INotificationRealtimePublisher`. Also extended `OutboxDispatchServiceTests`' pre-existing `FakeUnitOfWork` with the three `INotificationRepository` members (`CountUnreadAsync(string)`, `ListAsync`, `MarkAllReadAsync`) it was missing after this feature's earlier T060/T061 interface growth, mirroring `NotificationRepository`'s real filters. 53/53 pass in Application.Tests' Notifications namespace, 0 warnings.
- [X] T058 [P] [US1] Frontend tests: `ClientApp/src/features/notifications/components/NotificationBell.test.tsx`, `NotificationPopover.test.tsx`, `NotificationItem.test.tsx`, and `ClientApp/src/features/notifications/pages/NotificationsPage.test.tsx` with `.a11y.test.tsx`. Cover:
  - the badge count and its aria-label;
  - text rendered as plain text, where a `<b>` payload shows literally;
  - an unavailable item shows "no longer available";
  - an error toast on a failed mutation;
  - jest-axe with no serious or critical violations.
  - Deviations: (1) `NotificationList`'s outer container uses `role="region"` rather than `role="list"` — the virtualizer only mounts the visible row slice, so a real `listitem`-per-row structure would trip axe's `aria-required-children` rule against whatever happens to be (un)mounted at assertion time; a labeled region is accessible without asserting a DOM structure virtualization can't guarantee. (2) The category `Select` needed an explicit `aria-label` to satisfy axe's `aria-input-field-name` rule. (3) `NotificationPopover.test.tsx` uses `getByText` instead of `getByRole` inside the popover portal (jsdom's `getComputedStyle` crashes on MUI portal content under `getByRole`, per the `jsdom_getcomputedstyle_crash_mui_dialog` precedent). (4) `NotificationsPage.test.tsx`/`.a11y.test.tsx` stub `HTMLElement.prototype.clientHeight`/`offsetHeight` so the virtualized list actually renders rows under jsdom (mirrors `ChatSidebar.a11y.test.tsx`).
- [ ] T059 [P] [US1] `useNotificationHub.test.ts` in `ClientApp/src/features/notifications/hooks/`:
  - `notificationCreated` prepends the item and bumps the count;
  - on reconnect it invalidates the list and the unread-count queries;
  - the token factory re-reads the token on every call (the SignalR frozen-token regression).

### Implementation for User Story 1

- [X] T060 [US1] Add `NotificationCursor.cs` in `src/AskLucy.Persistence/Repositories/`, mirroring `ConversationCursor.cs`: `(CreatedAtUtc, Id)`, base64 JSON, and strict decoding that throws a validation error. Add the center methods to `NotificationRepository`:
  - `ListAsync(userId, categories, state, cursor, limit)`;
  - `CountUnreadAsync`;
  - `GetForOwnerAsync`;
  - `MarkAllReadAsync(userId, category?)` as a set-based `ExecuteUpdateAsync` that returns the count.
  - Deviation: no separate `GetForOwnerAsync` — every owner-scoped read (get-one, mark-read, delete) reuses the existing `GetByIdAsync` plus `NotificationOwnershipGuard.EnsureOwnedBy`, which throws `KeyNotFoundException` for a non-owned or missing row. One less method with the same 404 behavior the contract asks for.
- [X] T061 [P] [US1] Add the query `GetNotifications` (query, handler, validator with limit 1–100, and `NotificationDto` with `action` and `relatedItem` per the contract) in `src/AskLucy.Application/Notifications/Queries/GetNotifications/`:
  - It batch-resolves `relatedItem.available` through the registered `INotificationAccessCheck.GetAvailableAsync` for each item type in the page.
  - Types with no registered check report `available=true`.
  - Deviation: the DTO is named `NotificationListItemDto` (in `Notifications/Abstractions/INotificationRealtimePublisher.cs`, shared with the `notificationCreated` push per its own doc comment), not `NotificationDto` — same shape.
- [X] T062 [P] [US1] Add the queries `GetUnreadNotificationCount` and `GetNotification` (the latter includes non-sensitive `metadata`) in `src/AskLucy.Application/Notifications/Queries/`.
- [X] T063 [P] [US1] Add the commands `MarkNotificationRead`, `MarkAllNotificationsRead` and `DeleteNotification`, with their validators, in `src/AskLucy.Application/Notifications/Commands/`. After each save, push through `INotificationRealtimePublisher` to the caller's other sessions.
  - `MarkNotificationReadCommand`/`DeleteNotificationCommand` have no validator: their only input is the route's `Guid`, already model-bound, so there's nothing FluentValidation would add.
- [X] T064 [US1] Add `NotificationsController` in `src/AskLucy.Web/Controllers/v1/NotificationsController.cs`, with the routes in the contract's center section. Apply `[Authorize]` and `[EnableRateLimiting("notifications-endpoints")]`. Controllers only dispatch MediatR requests.
- [X] T065 [US1] Add the rate-limit policy `notifications-endpoints` (120 per minute, partitioned with `RateLimitPartitions.UserOrClientKey(context)`, standing rule 11) in `AddRateLimiter` in `src/AskLucy.Web/Program.cs` (~L240–580).
- [X] T066 [P] [US1] Add `ClientApp/src/features/notifications/api/notificationsApi.ts`, with Axios calls and TypeScript types matching the contract (`NotificationItem`, `NotificationPage`, `UnreadCount`), and MSW handlers in the existing test mocks location.
  - Deviations: (1) uses this codebase's `apiFetch` wrapper (`ClientApp/src/api/httpClient.ts`), not Axios — every other feature (`documentsApi.ts`, `workflowsApi.ts`, ...) already uses it, and Axios is not a dependency anywhere in the project despite CLAUDE.md's tech-stack listing. (2) There is no shared MSW mocks location in this codebase — every test file sets up its own inline `msw`/`msw/node` server, so the per-test-file handlers in `NotificationBell.test.tsx` etc. are that convention's equivalent.
- [X] T067 [US1] Add `ClientApp/src/features/notifications/hooks/useNotifications.ts` (`useInfiniteQuery`, keyset), `useUnreadCount.ts` and `useNotificationMutations.ts`. Mark-read, mark-all and delete each invalidate the relevant queries and show an error toast on failure.
  - Deviations: (1) `useUnreadCount` lives in `useNotifications.ts` rather than its own file — it's a one-line `useQuery` wrapper, consistent with how `useNotification`/`useNotifications` are grouped together in that file. (2) The hooks themselves don't render a toast — per this codebase's established error-surfacing convention (`RemoveCustomModelButton.tsx`), mutation hooks expose `.error`/`.isError` from TanStack Query and the *component* using the hook renders the `Snackbar`/`Alert`; hooks never render JSX here. `NotificationDetails.tsx`, `NotificationPopover.tsx` and `NotificationsPage.tsx` each render their own error `Snackbar` from the mutation's `.error`.
- [X] T068 [US1] Add `ClientApp/src/features/notifications/hooks/useNotificationHub.ts`:
  - It connects to `/hubs/notifications` through `keepHubConnected` in `ClientApp/src/api/hubConnection.ts` (standing rule 12), which carries the cookie-delivered token factory, the start/close retries and 401 detection.
  - It handles `notificationCreated`, `notificationUpdated` and `unreadCountChanged` by updating the query cache.
  - It refetches on reconnect and shows a non-blocking "live updates paused" indicator while disconnected.
  - Deviation: returns `isLive` (a boolean) rather than rendering the "live updates paused" indicator itself — the hook has no JSX, per the same hooks-never-render-JSX convention as T067; the indicator is the caller's responsibility to render from that value.
- [X] T069 [P] [US1] Add `NotificationItem.tsx` and `NotificationDetails.tsx` in `ClientApp/src/features/notifications/components/`:
  - The title and message render as React text only, never `dangerouslySetInnerHTML`.
  - Show the priority indicator, the relative time and the unread marker.
  - The action button navigates with React Router, or shows "no longer available".
  - Deviation: relative time is a small zero-dependency `Intl.RelativeTimeFormat` helper (`ClientApp/src/features/notifications/utils/relativeTime.ts`) — no relative-time library (`date-fns`/`dayjs`/`luxon`) is a dependency of this project, consistent with the "avoid unnecessary dependencies" principle.
- [X] T070 [US1] Add `NotificationList.tsx` in `ClientApp/src/features/notifications/components/`. It is virtualized with `@tanstack/react-virtual`, groups items by day, has empty, loading and error states with a retry, and supports keyboard navigation.
- [X] T071 [US1] Add `NotificationBell.tsx` (an icon button with an unread badge, where the aria-label includes the count) and `NotificationPopover.tsx` (the latest 10, mark all read, and a "View all" link to `/notifications`) in `ClientApp/src/features/notifications/components/`.
- [X] T072 [US1] Add `NotificationsPage.tsx` in `ClientApp/src/features/notifications/pages/`, with multi-select category filters, the all/unread/read state filter, infinite scroll, a details drawer and mark-all-read. Register the lazy routes `/notifications` and `/notifications/:id` in `ClientApp/src/routes/router.tsx`.
  - Both routes mount the same `NotificationsPage` component; the drawer's open state is driven by whether `useParams<{ id: string }>()` returns an `id` (standard React Router usage — no existing route-param-driven-drawer precedent was found elsewhere in the codebase to mirror instead).
- [X] T073 [US1] Mount `NotificationBell` in the header of `ClientApp/src/components/AppShell.tsx`, and start `useNotificationHub` once for authenticated sessions.
  - Both are mounted together from a small `AuthenticatedNotifications` child component, rendered only in the `isAuthenticated` branch of the header — `AppShell` is also reachable pre-login (e.g. `PrivacyPage`), so the hub connection must not attempt to open (and hit a 401) for a signed-out visitor.
- [X] T074 [US1] Run `npx tsc -b --noEmit`, `npm run lint` and the full `npm test` in `ClientApp/`, plus `dotnet test --filter "FullyQualifiedName~Notifications"` for the Application, Persistence and Web test projects.
  - `tsc -b --noEmit` and `npm run lint` are clean. `npm test -- --run`: 1991/1997 passing; the 6 failures are all in `ChatPage.test.tsx`/`ChatPage.a11y.test.tsx` around mic/voice-input buttons, reproduce in isolation, and touch only files owned by the concurrent session's in-flight Dictation work (`useSpeechRecognition.ts`, `useVoiceRecorder.ts`, `voiceApi.ts` — none touched by this task group) — out of scope per the standing instruction not to touch that session's Dictation work. `dotnet test --filter "FullyQualifiedName~Notifications"`: Domain.Tests 78/78, Infrastructure.Tests 13/13, Application.Tests 53/53, Web.Tests 16/16 all passing; Persistence.Tests skipped (8/8) — gated on `PERSISTENCE_TESTS_CONNECTION_STRING`, a standing environment condition unrelated to this change.

**Checkpoint**: US1 works on its own with seeded data. Don't push yet: slice 1 also needs US2 and US9-A.

---

## Phase 4: User Story 2 - Be told when platform work finishes, fails or needs me (Priority: P1)

**Goal**: Agents, workflows, documents, indexing, knowledge bases, memory and two-factor security changes publish to the hub. Each event produces exactly one correctly categorized, prioritized and linked in-app notification for the right recipient.

**Independent Test**: Trigger each supported event type through its real code path. Verify exactly one notification with the right category, priority, route and recipient. System announcements are covered by US6.

### Tests for User Story 2

- [ ] T075 [P] [US2] `AgentExecutionNotificationTests` in `tests/AskLucy.Application.Tests/Notifications/Emitters/AgentExecutionNotificationTests.cs`. The started, completed and failed events each publish once, to the execution owner, with `EventKey` `agent-execution:{id}:{event}` and `ParentId=agentId`, before `SaveChangesAsync`.
- [ ] T076 [P] [US2] `WorkflowExecutionNotificationTests` in `tests/AskLucy.Application.Tests/Notifications/Emitters/WorkflowExecutionNotificationTests.cs`:
  - started, completed and failed each publish once;
  - `paused` is published from `PauseWorkflowExecutionCommandHandler`;
  - nothing is published on the early-return and resume paths (the resumability gotcha).
- [ ] T077 [P] [US2] `DocumentNotificationEmitterTests` in `tests/AskLucy.Application.Tests/Notifications/Emitters/DocumentNotificationEmitterTests.cs`:
  - every `DocumentNotificationEventType` maps to its `document.*` key;
  - a failed OCR stage publishes `document.ocr.failed` **instead of** `document.processing.failed` (one notification per failure);
  - the indexing emits are tested in Phase 4b (T235), not here.
- [ ] T078 [P] [US2] `SecurityNotificationEmitterTests` in `tests/AskLucy.Application.Tests/Notifications/Emitters/SecurityNotificationEmitterTests.cs`: the enable-2FA, disable-2FA and regenerate-recovery-codes handlers each publish their `security.*` type once, in the same unit of work.
- [ ] T079 [P] [US2] Update `tests/AskLucy.Web.Tests/Documents/ProcessingNotifierTests.cs`, and add `tests/AskLucy.Infrastructure.Tests/Memory/MemoryNotifierTests.cs`:
  - `NotifyAsync` publishes through `INotificationPublisher`;
  - no `DocumentNotification` or `MemoryNotification` row is written;
  - no `notificationCreated` or `memoryNotificationCreated` hub event is sent;
  - the stage and progress pushes are unchanged.
- [ ] T080 [P] [US2] `EmitterEndToEndTests` in `tests/AskLucy.Web.Tests/Notifications/EmitterEndToEndTests.cs`, against the real host and DB. For each emitted type, trigger it through its command or job path, let the dispatcher run, and assert exactly one center item with the expected category, priority, route and recipient.

### Implementation for User Story 2

- [X] T081 [P] [US2] Add `LegacyNotificationTypeMap.cs` in `src/AskLucy.Application/Notifications/Legacy/`. It maps `DocumentNotificationEventType` and the memory event types to catalogue keys per [data-model.md § Legacy mapping](data-model.md#legacy-mapping-fr-009a-research-r13). US9-A's importer reuses it.
- [X] T082 [US2] Rewrite `ProcessingNotifier.NotifyAsync` in `src/AskLucy.Infrastructure/Documents/ProcessingNotifier.cs`:
  - It maps the event type, publishes (`documentName`, `failureSummary`, `RelatedItem("Document", documentId)`, `EventKey` `document:{id}:{type}:{jobId-or-version}`), and returns `Task.CompletedTask`.
  - Remove the `notificationCreated` push and the `IDocumentNotificationRepository` dependency.
  - Leave `documentStageChanged`, `documentProcessingCompleted` and `documentProcessingFailed` unchanged.
  - Deviation: the old single free-text `message` parameter couldn't decompose into the catalog's strictly-validated per-type variables, so `NotifyAsync` was widened to structured optional named parameters (`documentName`, `failureSummary`, `versionNumber`, `usedStorage`, `storageLimit`); a new `NotifyOcrCompletedAsync` method was added for `document.ocr.completed`, which has no legacy enum value to route through `LegacyNotificationTypeMap`.
- [X] T083 [US2] Reorder `src/AskLucy.Application/Documents/Processing/DocumentProcessingPipeline.cs` so `notifier.NotifyAsync(...)` runs **before** the final `unitOfWork.SaveChangesAsync`, both in the completion block (~L140–147) and in `FailAsync` (~L165–175):
  - Publish `document.ocr.completed` when the OCR stage completes.
  - When the failing stage is `Ocr`, publish `document.ocr.failed` instead of `document.processing.failed`.
- [X] T084 [US2] Apply the same before-save ordering to every other `IProcessingNotifier.NotifyAsync` caller:
  - `src/AskLucy.Application/Documents/Commands/` `{CompleteUpload, CompleteUploadAsNew, CompleteUploadAsVersion, ReplaceDocument, SimpleUpload, StartUpload}` handlers;
  - `src/AskLucy.Application/Documents/DocumentUploadFinalizer.cs`.

  This covers `document.upload.completed`, `document.version.created` and `document.storage.limit-reached`.
  - Deviation: `StartUploadCommandHandler` and `DocumentUploadFinalizer`'s `StorageLimitReached` notify was followed by a `throw`, not a save — both now call `unitOfWork.SaveChangesAsync` explicitly before the throw so the outbox event isn't dropped (`DocumentUploadFinalizer` gained a new `IUnitOfWork` constructor dependency for this).
- [X] T085 [US2] Rewrite `src/AskLucy.Infrastructure/Memory/MemoryNotifier.cs`:
  - It publishes `memory.auto-created`, `memory.auto-approved` and `memory.conflict.confirmation-needed` with `RelatedItem("Memory", id)`.
  - Remove the `memoryNotificationCreated` push and the `IMemoryNotificationRepository` write.
  - Reorder the callers (`src/AskLucy.Application/Memory/MemoryConflictDetectionService.cs` and `MemoryExtractionJob.cs`) so the notifier runs before their save.
  - Note: `MemoryConflictDetectionService.cs`'s notify was already correctly ordered before its save; only `MemoryExtractionJob.cs`'s `AutoApproved` notify needed reordering (moved before `SaveChangesAsync`, with the `consumedByMerge` early-return guard moved to after the save).
- [X] T086 [US2] Publish `agent.execution.started`, `.completed` and `.failed` in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs`, at the actual status transitions, before the save that persists them. Use `RelatedItem("AgentExecution", executionId, agentId)` and the variables `agentName`, `duration` and `failureSummary`. The failure summary is a user-safe message, never a stack trace.
  - `agentName` is fetched via `IAgentRepository.GetByIdAsync` (the orchestrator only otherwise loads `AgentVersion`, not the parent `Agent`); a placeholder `"your agent"` is used if the lookup fails before the fetch runs (e.g. a missing `AgentVersion`). Updated 5 existing test call sites (`TestExecutionSkipsMutatingToolsTests`, `AgentResourceConflictTests`, `AgentApprovalWorkflowTests`, `AgentExecutionOrchestratorTests`, `AgentExecutionRunnerJobTests`, `McpUntrustedContentFramingTests`, `McpAuthorizationBypassSecurityTests`, `McpHighRiskApprovalTests`, `McpToolExecutionOrchestratorIntegrationTests`, `AgentNodeExecutorTests`) for the new constructor dependency, plus pre-existing test breakage from T082/T084's `IProcessingNotifier`/`DocumentUploadFinalizer` signature changes (`NotificationTests`, `UploadValidationTests`, `DuplicateDetectionTests`, `ProcessingFailureAndRetryTests`) that hadn't been caught yet since the test project wasn't rebuilt until now.
- [X] T087 [US2] Publish `workflow.execution.started`, `.completed` and `.failed` in `src/AskLucy.Application/Workflows/Runtime/WorkflowExecutionOrchestrator.cs`. Place the calls past the early-return branches in `RunAsync`, and never inside Parallel-branch code, which shares one `DbContext`. Also publish `workflow.execution.paused` in `src/AskLucy.Application/Workflows/Commands/PauseWorkflowExecution/PauseWorkflowExecutionCommandHandler.cs`.
  - `workflowName` is fetched via `IWorkflowRepository.GetByIdAsync` (the orchestrator only otherwise loads `WorkflowVersion`, not the parent `Workflow`), mirroring T086's `agentName` pattern exactly, including the `"your workflow"` fallback for a failure before the fetch runs. The `.failed` publish is centralized in one `PublishWorkflowFailed()` local function (closes over `execution`/`workflowName`) called at all 8 real terminal-failure sites in `RunAsync` — budget exceeded, loop-budget exceeded, approval timeout, Parallel-node failure, Condition node with no matching connection, node failure under `Stop`/`Compensate`/unrecognized error-policy strategies, and the top-level `catch` — since none of those sites are inside `ExecuteParallelAsync`'s concurrent `RunBranchAsync` dispatch, the "never inside Parallel-branch code" constraint holds. `PauseWorkflowExecutionCommandHandler` gained `IWorkflowRepository` as a new constructor dependency (for the same `workflowName` lookup) alongside `INotificationPublisher`. Updated 14 existing test call sites for the new `WorkflowExecutionOrchestrator`/`PauseWorkflowExecutionCommandHandler` constructor dependencies (added a shared `WorkflowOrchestratorTestHelpers.NoOpNotificationPublisher()` helper); full `Workflow*` test suite (245 tests) passes.
- [X] T088 [US2] The `document.indexing.*` and `knowledge-base.indexing.*` publishes are **not** placed in `IndexingOrchestrator`. They live in `KnowledgeBaseIndexingJob` (T237), which owns the job's final save. `IndexingOrchestrator` is unchanged by this feature.
  - Note-only task — no code change here; the actual publishes are implemented as part of Phase 4b (T235-T240), not yet started.
- [X] T089 [US2] Set `IsEmitted=false` on the `knowledge-base.updated` catalogue row, with a comment pointing to [research.md R27](research.md). `KnowledgeBase` has no sharing, so every user edit is made by the owner, and the type fires only for a non-owner actor, which no code path produces yet.
  - Already satisfied by the existing `NotificationTypeCatalog` entry (`with { IsEmitted = false }` plus the R27 comment) — no code change needed.
- [X] T090 [US2] Publish `security.two-factor.enabled`, `security.two-factor.disabled` and `security.recovery-codes.regenerated` in `src/AskLucy.Application/Authentication/Commands/TwoFactor/{EnableTwoFactorCommandHandler, DisableTwoFactorCommandHandler, GenerateRecoveryCodesCommandHandler}.cs`, with `changedAt` and the route `/settings?tab=security`.
  - Deviation: all three handlers previously depended only on `IIdentityService`; added `INotificationPublisher` and `IUnitOfWork` (with the `MediatR.INotificationPublisher` alias fix, mirroring T087's `PauseWorkflowExecutionCommandHandler` pattern, since all three already `using MediatR;`). No `RelatedItem` — the route is the fixed `/settings?tab=security`, not a per-item link. `changedAt` formatted as `{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC`, matching the existing convention in `PasswordEmailJob.SendPasswordChangedNoticeAsync` (no other precedent existed for this catalogue's `changedAt` variable — `security.password.changed` itself has no publish call site anywhere yet). `EventKey` is `security:{userId}:{event}:{ticks}` — a per-occurrence tick suffix, since these are repeatable user actions and the outbox's `EventKey` dedup index is permanent per recipient, not time-windowed (unlike the id-based keys used for one-shot document/workflow events). No pre-existing test directly constructed these handlers, so no test call sites needed updating; Application + Application.Tests build clean. Full `Application.Tests` run (2199 tests) shows 2 pre-existing failures in `Documents/NotificationTests.cs` (`StartUpload_ShouldFireStorageLimitReachedNotification_...`, `ReplaceDocument_ShouldFireVersionCreatedNotification_...`) unrelated to this task — not touched by T090, left for the Slice 1 gate's full-suite pass.
- [X] T091 [P] [US2] Implement the `INotificationAccessCheck` for each item type, and register them as an `IEnumerable`:
  - `src/AskLucy.Application/Documents/Notifications/DocumentNotificationAccessCheck.cs`
  - `src/AskLucy.Application/Agents/Notifications/AgentExecutionNotificationAccessCheck.cs`
  - `src/AskLucy.Application/Workflows/Notifications/WorkflowExecutionNotificationAccessCheck.cs`
  - `src/AskLucy.Application/KnowledgeBases/Notifications/KnowledgeBaseNotificationAccessCheck.cs`
  - `src/AskLucy.Application/Memory/Notifications/MemoryNotificationAccessCheck.cs`

  Each check is owner-scoped and reuses the module's existing ownership guard, for example `RetrievalOwnershipGuard`.
  - Deviation: each check reuses its module's ownership *predicate* (`Document.IsOwnedBy`, `KnowledgeBase.IsOwnedBy`, `Memory.IsOwnedBy`, or `RunByUserId` equality via the repository's existing `GetByIdForUserAsync` for the two execution types) directly, rather than calling the throwing `*OwnershipGuard.EnsureOwnedBy` helpers themselves — those guards convert "not owned" into a thrown 404, which is the wrong shape for `CanAccessAsync`/`GetAvailableAsync`'s "just hide it" boolean semantics; `RetrievalOwnershipGuard` doesn't apply to any of these five item types (it guards `KnowledgeBase` search-scope resolution, not a single item), so `KnowledgeBaseOwnershipGuard`'s predicate was used instead. `GetAvailableAsync` loops `CanAccessAsync` per id for Document/AgentExecution/WorkflowExecution/Memory (none of their repositories expose an owner-filtered bulk-by-ids lookup; page sizes are small and bounded) except `KnowledgeBase`, which already had `IKnowledgeBaseRepository.GetByIdsAsync` to batch. `MemoryNotificationAccessCheck` deliberately uses `IMemoryRepository.GetByIdAsync`, not `GetActiveByIdsAsync`/`GetByIdsAsync` — those two additionally filter by lifecycle/conflict state for retrieval purposes, not ownership. Registered in `AskLucy.Application/DependencyInjection.cs` as five `AddScoped<INotificationAccessCheck, T>()` calls. Added `tests/AskLucy.Application.Tests/Notifications/NotificationAccessCheckTests.cs` (5 tests, one per check) covering owner-true/other-user-false/malformed-id-false and `GetAvailableAsync` filtering — full `Application.Tests` run: 2204 tests, same pre-existing 2 `Documents/NotificationTests.cs` failures noted in T090, unrelated to this task.
- [X] T092 [P] [US2] Add URL deep links for the reconciled routes:
  - `ClientApp/src/features/documents/pages/DocumentWorkspacePage.tsx` opens `DocumentDetailPanel` for `?documentId=`.
  - The memory center page opens the memory for `?memoryId=`.
  - `ClientApp/src/features/settings/pages/SettingsPage.tsx` reads `?tab=security` (and the other `SETTINGS_TAB_INDEX` names) in addition to `location.state`.
  - An unknown or inaccessible id shows an inline "not available" message.

  Add a page test for each.

  Deviation: `MemoryCenterPage.tsx` has no separate "view" panel — opening a memory here means opening the existing `MemoryEditDialog` (edit-only UI), so `?memoryId=` opens that dialog rather than a new read-only view. `useMemory(id)` returns `MemoryDetail`, a different shape from `MemoryEditDialog`'s `MemoryListItem` prop (missing `projectName`/`sourceType`/`sourceConversationId`/`createdAtUtc`/`lastReinforcedAtUtc`); since the dialog only reads `.content`, a `toListItem()` adapter backfills the missing fields with placeholders rather than widening the dialog's prop type. `SettingsPage.tsx`'s `?tab=` reads a name (e.g. `security`), not the numeric `SETTINGS_TAB_INDEX` value `location.state.tab` uses (a notification has no `location.state` to carry it) — added a `SETTINGS_TAB_NAME_INDEX` name→index map; an unrecognized name falls back to the default tab and shows the inline warning rather than rendering nothing. The knowledge-base document deep link (`/knowledge-bases/{id}`) is deferred to T240 (Phase 4b), since knowledge-base indexing notifications don't exist yet. New tests: `DocumentWorkspacePage.test.tsx`, `MemoryCenterPage.test.tsx` (both new files — neither page had a test file before), and two new cases in the existing `SettingsPage.test.tsx`. Full frontend suite: 2002/2003 passing, 1 pre-existing unrelated failure (`WorkflowDesignerPage.a11y.test.tsx`, a canvas/axe timeout, not touched by this task).

**Checkpoint**: Every non-account emitter produces exactly one in-app notification. Slice 1 still needs Phases 4b and 5.

---

## Phase 4b: User Story 2 (part B) - Knowledge-base indexing trigger (Priority: P1, ships with slice 1)

**Goal**: Uploading a document to a knowledge base indexes it (chunk, embed, vector store) in the background, so `document.indexing.*` and `knowledge-base.indexing.*` fire from a real code path (FR-004, FR-004b). Before this feature, `IIndexingOrchestrator` had no production caller and uploaded knowledge-base documents were never indexed ([research.md R27](research.md)).

**Independent Test**: Upload a Markdown file to a knowledge base through the API with a fake embedding service. The document's chunks and embeddings exist, the knowledge base's index status goes Indexing → Indexed, the `knowledgeBaseIndexStatusChanged` hub event is sent for each transition, and exactly one `document.indexing.completed` and one `knowledge-base.indexing.completed` notification reach the owner. With a failing embedding service, the job ends `Failed`, the knowledge base `Failed`, one `document.indexing.failed` and one `knowledge-base.indexing.failed` notification arrive, and the failure is on the spec 074 admin trail.

### Tests for Phase 4b

- [X] T235 [P] [US2] `KnowledgeBaseIndexingJobTests` and `KnowledgeBaseDocumentUploadedIndexingHandlerTests` in `tests/AskLucy.Application.Tests/Retrieval/Indexing/`:
  - the upload handler skips a document whose `ProcessingStatus` is `Failed`; otherwise it creates one `IndexingJob` (`Queued`), calls `KnowledgeBase.MarkIndexing`, saves, enqueues `IKnowledgeBaseIndexingJob`, and stores the Hangfire job id;
  - a run calls `IndexingJob.Start`, then the orchestrator; `Completed` and `PartiallyCompleted` call `Complete`, and `Failed` or an exception calls `Fail` with a user-safe reason (never a stack trace or provider body);
  - when no other `Queued`/`InProgress` job remains for the knowledge base, it calls `MarkIndexed(partial)` or `MarkIndexFailed` (partial when any job since the last settle failed);
  - it publishes `document.indexing.completed`/`.failed` once per job (`EventKey` `indexing-job:{jobId}:{outcome}`) and, on settle, `knowledge-base.indexing.completed`/`.failed` once (`EventKey` `knowledge-base:{id}:indexing:{settledAtTicks}`), all before the final save;
  - it calls `IRetrievalIndexingNotifier.NotifyIndexStatusChangedAsync` after each index-status transition commits;
  - a failed job calls `IOperationalFailureRecorder.Record` (standing rule 13);
  - a document deleted before the job runs ends the job `Failed` with reason "document removed" and publishes nothing;
  - a Hangfire retry of the same job id publishes nothing twice.
- [X] T236 [P] [US2] `KnowledgeBaseIndexingEndToEndTests` in `tests/AskLucy.Web.Tests/Retrieval/`, with the real host and DB and a fake `IEmbeddingService` registered through a derived factory (standing rule 8): the Independent Test above, both the success and the failure path.
  - Deviation: discovered and fixed a pre-existing gap while building this test's own Independent Test scenario — no `IDocumentTextExtractor` handled `DocumentFileType.Markdown`/`Csv`/`Text` (only Word/Excel/PowerPoint and PDF were registered), so every knowledge-base upload of these types would fail indexing with an `InvalidOperationException`. Added `PlainTextExtractor` in `src/AskLucy.Infrastructure/Documents/Extraction/` and registered it in `src/AskLucy.Infrastructure/DependencyInjection.cs`, scoped narrowly (plain UTF-8 read, no structure extraction) since `IndexingOrchestrator` itself is otherwise unchanged by this feature (per T237/T238).
  - Also fixed a pre-existing, unrelated build break found while verifying this test's build: `tests/AskLucy.Web.Tests/Documents/ProcessingNotifierTests.cs` still called `ProcessingNotifier`'s old 3-arg `(hubContext, notificationRepository, unitOfWork)` constructor, which T082 (`5e90851b`) had already replaced with `(hubContext, INotificationPublisher)` — the test file was simply never updated (part of the T075-T080 backlog item "the `ProcessingNotifierTests.cs` update"). Rewrote it against the current constructor/`NotifyAsync`/`NotifyOcrCompletedAsync` signatures, asserting `INotificationPublisher.Publish` is called with the expected `NotificationRequest` instead of the removed repository/`SaveChangesAsync`/`notificationCreated` push.
  - Added `CleanupAsync` (removes the seeded `ApplicationUser` and every row it cascades to — knowledge base, documents, chunks, notifications, incidents) since this is a real-DB suite against the shared test host.

### Implementation for Phase 4b

- [X] T237 [US2] Add `IKnowledgeBaseIndexingJob` in `src/AskLucy.Application/Abstractions/` and `KnowledgeBaseIndexingJob` in `src/AskLucy.Application/Retrieval/Indexing/`, per T235. Mirror the other Hangfire job interfaces (`IMemoryExtractionJob`): enqueued via `IBackgroundJobClient` against the interface. Retries: `[AutomaticRetry(Attempts = 3)]`, and the final attempt's failure is the one that settles the job.
- [X] T238 [US2] Add `KnowledgeBaseDocumentUploadedIndexingHandler : INotificationHandler<DocumentUploadedNotification>` in `src/AskLucy.Application/Retrieval/Indexing/`, per T235. `DocumentUploadedNotification` is published after the upload's commit, so the handler owns its own save. If the enqueue fails, it marks the job `Failed`, logs, records the operational failure, and rethrows.
- [X] T239 [US2] Register the job and the handler in `src/AskLucy.Application/DependencyInjection.cs`, then run the boot verification (T054's command). Existing knowledge-base documents uploaded before this release are **not** back-filled (spec Assumptions); note this in the T229 release checklist. `dotnet build "Ask Lucy.sln" -warnaserror` succeeded (0 warnings, 0 errors).
- [X] T240 [US2] Add the knowledge-base document deep link to T092's route checks (`/knowledge-bases/{id}`).
  - Deviation: `/knowledge-bases/:id` was already a plain page route (`router.tsx`) before this feature, and `NotificationItem`'s click handler already navigates generically via `navigate(item.action.route)` — no new frontend code was needed, unlike documents/memory/settings in T092. Verified the backend link resolves correctly with a new `NotificationLinkBuilderTests` case (`BuildRelative_ShouldResolveTheKnowledgeBaseIndexingRoute_ToTheKnowledgeBaseDetailPage`); the `KnowledgeBaseNotificationAccessCheck` and `KnowledgeBaseRoute` catalog wiring already existed from T081-T092.
  - Deferred: adding `knowledge-base.indexing.{completed,failed}` to `EmitterEndToEndTests` (T080) — T080 itself is still unwritten (part of the still-open T075-T080 backlog, out of this task's own scope). `KnowledgeBaseIndexingEndToEndTests` (T236) already proves both notification types are published end-to-end and reach the owner exactly once.

**Checkpoint**: Knowledge-base uploads are indexed in the background and report through the hub.

---

## Phase 5: User Story 9 (part A) - Legacy document and memory notifications move onto the hub (Priority: P2, ships with slice 1)

**Goal**: Every existing document and memory notification appears once in the hub center with the same read state, message and link, and the old inboxes, endpoints and hub events are gone. FR-009a requires this to ship atomically with Phases 3–4.

**Independent Test**: Record every legacy row and its read state, then boot the app. Every row appears once in `/notifications`. A second boot adds nothing. The legacy UI and endpoints return nothing (404 or removed).

### Tests for User Story 9 (part A)

- [X] T093 [P] [US9] `LegacyNotificationImportTests` in `tests/AskLucy.Persistence.Tests/Notifications/LegacyNotificationImportTests.cs` (SC-013):
  - every legacy document and memory row is imported once with its read state, message, link, `CreatedAtUtc`, and one `InApp` delivery with status `Delivered`;
  - a second run inserts 0 rows;
  - the legacy rows are unmodified;
  - rows of a deleted user are skipped.
  - Passes against the real test2 DB (2/2). Persistence.Tests has no `AskLucy.Infrastructure` project reference, so the real `INotificationTemplateRenderer`/`INotificationLinkBuilder` implementations aren't reachable there; used local deterministic `FakeTemplateRenderer`/`FakeLinkBuilder` test doubles in the test file, mirroring the project's existing `Base64MemoryContentProtector` convention for this exact situation.
- [X] T094 [P] [US9] Update `tests/AskLucy.Application.Tests/Memory/AccountDeletionCascadeTests.cs` and `tests/AskLucy.Application.Tests/Users/DeleteMyAccountCommandHandlerTests.cs`, so account deletion also removes the user's hub notifications. Delete `tests/AskLucy.Application.Tests/Documents/NotificationTests.cs`, whose feature is being removed.
  - Deviation: deleting `NotificationTests.cs` removes the only test coverage of handler → `IProcessingNotifier.NotifyAsync` wiring for the three document upload/replace/quota scenarios. No replacement was added — the notifier call sites are unchanged and the notifier's own behavior is already covered by `ProcessingNotifier`/`MemoryNotifier` tests; only the now-deleted handler-level wiring assertions are lost.

### Implementation for User Story 9 (part A)

- [X] T095 [US9] Add `ILegacyNotificationImport` in `src/AskLucy.Application/Notifications/Abstractions/`, implemented by `src/AskLucy.Persistence/Repositories/LegacyNotificationImportRepository.cs`:
  - one `INSERT … SELECT … WHERE NOT EXISTS` per legacy table, on `EventKey` `legacy:document:{id}` or `legacy:memory:{id}`;
  - the field mapping comes from `LegacyNotificationTypeMap` and the data-model's legacy mapping table;
  - a paired insert creates the `InApp` `Delivered` deliveries;
  - it returns the row counts.
  - Deviation: implemented as an EF Core read-loop-then-add (in-memory dedup via a `HashSet<string>` of already-imported `EventKey`s) rather than a literal `INSERT … SELECT … WHERE NOT EXISTS` SQL statement, so it can reuse `INotificationTemplateRenderer`/`INotificationLinkBuilder`/`Notification.Create` exactly like the live `NotificationMaterializer` path instead of duplicating that logic in raw SQL. A row whose type has no published in-app template yet (`NotificationRenderException`) is skipped and logged rather than failing the batch, since `NotificationTemplateSeeder` is a hosted service that starts after this import runs in `Program.cs` — the skipped row is picked up on a later restart once templates exist, by the same `EventKey` dedup.
- [X] T096 [US9] Add `LegacyNotificationImporter` in `src/AskLucy.Web/StartupTasks/LegacyNotificationImporter.cs`, following the pattern of `CredentialHintBackfillService.cs`. It runs once at startup, logs the counts, and on failure logs an error and lets the health check surface it. Register it in `Program.cs`.
  - Deviation: on failure, logs a warning and swallows the exception (matching `CredentialHintBackfillService`'s own try/catch at the `Program.cs` call site) rather than letting it propagate to a health check, so a missing/unreachable DB at startup can't crash the host. The import is idempotent, so a skipped run is retried for free on the next restart.
- [X] T097 [US9] Remove the legacy notification endpoints: the notification actions in `src/AskLucy.Web/Controllers/v1/DocumentProcessingController.cs` (~L54, L59) and `src/AskLucy.Web/Controllers/v1/MemoriesController.cs` (~L75, L80). Also delete these handlers:
  - `src/AskLucy.Application/Documents/Commands/MarkNotificationRead/`
  - `src/AskLucy.Application/Documents/Queries/GetNotifications/`
  - `src/AskLucy.Application/Memory/Commands/MarkNotificationRead/`
  - `src/AskLucy.Application/Memory/Queries/ListMemoryNotifications/`
- [X] T098 [US9] Make the legacy repositories read-only:
  - Reduce `IDocumentNotificationRepository` and `IMemoryNotificationRepository`, and their Persistence implementations, to the delete-by-user method that account deletion needs.
  - Keep the entities, configurations and tables. Two-step drop, §5: the drop happens in the follow-up release. The actual `DropLegacyDocumentAndMemoryNotifications` migration was not created this slice, per plan.
- [X] T099 [US9] Update `src/AskLucy.Application/Users/Commands/DeleteMyAccount/DeleteMyAccountCommandHandler.cs` to delete the user's hub notifications (a new `INotificationRepository.DeleteAllForUserAsync`, set-based) as well as the legacy rows.
  - Note: also required adding `DeleteAllForUserAsync` to the `FakeUnitOfWork` test double in `tests/AskLucy.Application.Tests/Notifications/OutboxDispatchServiceTests.cs`, a third `INotificationRepository` implementer not touched by the primary edit — CS0535 surfaced only when running the full `Application.Tests` suite, not a targeted build.
- [X] T100 [P] [US9] Remove the legacy document inbox from the frontend:
  - delete `ClientApp/src/features/documents/components/NotificationInbox.tsx` and `ClientApp/src/features/documents/hooks/useNotificationHub.ts`;
  - strip the notification parts from `documentsApi.ts`, `useDocuments.ts`, `useDocumentMutations.ts` and `DocumentWorkspacePage.tsx`;
  - update their tests and MSW handlers.
  - Note: also deleted `useNotificationHub.test.tsx` (test for the deleted hook) and the `latestNotification` snackbar/`isNotificationHubLive` connection Chip in `DocumentWorkspacePage.tsx` — both depended on the deleted hook, whose backend push (`notificationCreated` on `DocumentProcessingHub`) was already retired in T082; the global `AppShell` → `NotificationBell` (`features/notifications`) already covers this page. No MSW handler mocked the legacy `/documents/notifications` endpoint, and `DocumentWorkspacePage.test.tsx` didn't reference it, so neither needed updating.
- [X] T101 [P] [US9] Remove the legacy memory inbox from the frontend:
  - delete `ClientApp/src/features/memory/components/MemoryNotificationList.tsx` and `ClientApp/src/features/memory/hooks/useMemoryNotificationsHub.ts`;
  - strip the notification parts from `memoryApi.ts`, `useMemories.ts`, `useMemoryMutations.ts` and `MemoryCenterPage.tsx`;
  - update their tests and MSW handlers.
  - Note: also deleted `useMemoryNotificationsHub.test.tsx` and removed the whole "Notifications" tab (`MemoryCenterTab` union, `Tab`, and content branch) plus the `isMemoryHubLive` connection Chip from `MemoryCenterPage.tsx`, since its only content was the now-deleted `MemoryNotificationList`. Same backend precedent as T100 (`memoryNotificationCreated` push already retired). No MSW handler or test referenced the legacy endpoints.
- [ ] T102 [US9] Slice 1 gate: run the full backend suite and the full frontend suite (`tsc -b`, lint, `npm test`), then walk through quickstart S1, S2 and S10.

**Checkpoint**: Slice 1 (US1 + US2 + US9-A) is deployable. Commit and push to main, then run quickstart S10 against production.

---

## Phase 6: User Story 3 - Receive important notifications by email (Priority: P2)

**Goal**: Email-enabled notifications are delivered by SMTP with branded HTML and plain text, safe links and minimized sensitive content. Delivery survives outages, worker restarts and concurrent workers with no duplicates, and retries until dead-lettered.

**Independent Test**: With a test SMTP server, raise email-enabled events and verify one email each, with the right subject, HTML and text. Then inject failures (outage, restart mid-send, two workers) and verify retries, no duplicates, `AmbiguousOutcome` classification, and a dead letter after 5 attempts (SC-003).

### Tests for User Story 3

- [ ] T103 [P] [US3] `SmtpFailureClassifierTests` in `tests/AskLucy.Infrastructure.Tests/Notifications/SmtpFailureClassifierTests.cs`:
  - a 5xx is `Permanent`;
  - a 4xx, a timeout or a dropped connection is `Transient`;
  - an authentication failure is `Transient` plus a health alert flag;
  - the safe reason never contains credentials or the full server banner.
- [ ] T104 [P] [US3] `EmailSendRateLimiterTests` in `tests/AskLucy.Infrastructure.Tests/Notifications/EmailSendRateLimiterTests.cs`:
  - the token bucket enforces `MaxPerMinute`;
  - the reserved lane (`ReservedPerMinuteForMandatory`) is always available to mandatory types, even when optional traffic has drained the bucket.
- [ ] T105 [P] [US3] `EmailTemplateRenderTests` in `tests/AskLucy.Infrastructure.Tests/Notifications/EmailTemplateRenderTests.cs`:
  - variables are HTML-escaped in the HTML and raw in the text;
  - CR/LF and control characters are stripped from the subject;
  - `lang` and `dir` are set on the root;
  - `MinimizeSensitiveContent` types render only their declared variables;
  - an unknown token gives `RenderError`.
- [ ] T106 [P] [US3] `DeliveryProcessingServiceTests` in `tests/AskLucy.Application.Tests/Notifications/DeliveryProcessingServiceTests.cs`, using fakes:
  - the retry schedule follows the normal and Critical delays;
  - a dead letter happens after `MaxAttempts`;
  - `Permanent` fails immediately;
  - a deleted recipient gives `Cancelled`;
  - an expired notification gives `Expired`;
  - the language is resolved at send time;
  - the aggregate status is recomputed;
  - metrics are incremented.
- [ ] T107 [P] [US3] `DeliveryClaimRaceTests` in `tests/AskLucy.Persistence.Tests/Notifications/DeliveryClaimRaceTests.cs`:
  - concurrent claimers never claim the same delivery;
  - the sweeper turns an expired `Sending` lease into `Failed(AmbiguousOutcome)` with no requeue;
  - a claimed-but-unsent delivery is released.
- [ ] T108 [P] [US3] `DeliveryFaultInjectionTests` in `tests/AskLucy.Web.Tests/Notifications/DeliveryFaultInjectionTests.cs`, against the real DB with a scriptable fake `IEmailSender`. Implement the [quickstart §4](quickstart.md) matrix (outage, restart mid-send, two concurrent workers, rejected recipient) and assert zero duplicates, zero lost deliveries, and correct terminal states (SC-003).

### Implementation for User Story 3

- [ ] T109 [US3] Extend `src/AskLucy.Application/Abstractions/IEmailSender.cs` with `SendAsync(EmailMessage, CancellationToken)`, and add the records `EmailMessage` and `EmailAttachment` per [contracts/module-integration.md](contracts/module-integration.md). Keep the existing members.
- [ ] T110 [US3] Implement the overload in `src/AskLucy.Infrastructure/Email/SmtpEmailSender.cs`:
  - MailKit with STARTTLS and a timeout of `SendTimeoutSeconds`;
  - the `Message-ID` header set from `EmailMessage.MessageId`;
  - `From` and `Return-Path` taken only from `SmtpOptions`, and a validated `ReplyTo`;
  - the connection reused across a worker batch.

  Implement it in `src/AskLucy.Infrastructure/Email/ConsoleEmailSender.cs` too.
- [ ] T111 [P] [US3] Add `SmtpFailureClassifier.cs` and `EmailSendRateLimiter.cs` (a singleton token bucket with a reserved lane) in `src/AskLucy.Infrastructure/Notifications/Email/`.
- [ ] T112 [P] [US3] Add `INotificationChannelSender`, `DeliveryContext`, `ChannelSendResult` and `ChannelSendOutcome` in `src/AskLucy.Application/Notifications/Abstractions/INotificationChannelSender.cs`.
- [ ] T113 [US3] Add the email path to `LogicFreeTemplateRenderer`, and extend `src/AskLucy.Infrastructure/Email/BrandedAccountEmailTemplateRenderer.cs` with `lang` and `dir` parameters (defaulting to `en`/`ltr`, so existing output is unchanged). The structured email fields map onto the branded layout, producing HTML and a plain-text alternative.
- [ ] T114 [US3] Implement `EmailChannelSender` in `src/AskLucy.Infrastructure/Notifications/Email/EmailChannelSender.cs`. It:
  - acquires a rate-limiter token (from the reserved lane for mandatory types);
  - renders the email;
  - builds an `EmailMessage` with the `Message-ID` `<{deliveryId}@{domain}>`;
  - sends it;
  - classifies the result into a `ChannelSendResult`.
- [ ] T115 [US3] Add the delivery-queue methods to `NotificationRepository`:
  - `ClaimDueDeliveriesAsync(workerId, lease, batchSize)`, a conditional `ExecuteUpdateAsync` to `Sending`;
  - `SweepExpiredLeasesAsync(now)`, where `Sending` becomes `Failed(AmbiguousOutcome)` and a claimed `Pending` is released;
  - `GetDueBacklogAsync`, for health and statistics.
- [ ] T116 [US3] Implement `DeliveryProcessingService` in `src/AskLucy.Application/Notifications/Processing/DeliveryProcessingService.cs`. It:
  - claims deliveries;
  - re-validates the recipient and the expiry;
  - resolves the language and renders;
  - calls the channel sender;
  - applies the retry schedule from `NotificationsOptions.Retry`, or dead-letters;
  - recomputes the aggregate status and saves;
  - pushes `notificationUpdated`;
  - logs with the delivery's correlation id.
- [ ] T117 [US3] Implement the `NotificationDeliveryWorker` `BackgroundService` in `src/AskLucy.Infrastructure/Notifications/Workers/NotificationDeliveryWorker.cs`. It has the same loop, scope, heartbeat and error rules as the dispatcher, including releasing claimed deliveries in `StopAsync` (standing rule 10).
- [ ] T118 [US3] Implement `LeaseSweepService` in `src/AskLucy.Application/Notifications/Processing/LeaseSweepService.cs`, and the Hangfire job `NotificationLeaseSweepJob` in `src/AskLucy.Infrastructure/Notifications/Jobs/NotificationLeaseSweepJob.cs`. Register it with `RecurringJob.AddOrUpdate` every minute in `Program.cs` (~L748–771). It also sweeps expired outbox leases.
- [ ] T119 [P] [US3] Create the English email seeds `Seed/en/{type}.email.json` in `src/AskLucy.Infrastructure/Notifications/Templates/Seed/en/` for **every emitted non-account type whose catalogue Email column is `on`, `off` or `M`** (not `—`). An `off` default can be switched on by the user (R28), so it needs a template too (FR-043, SC-006):
  - `agent.execution.{started,completed,failed}`, `agent.approval.requested`
  - `workflow.execution.{started,completed,failed,paused}`, `workflow.approval.requested`
  - every emitted `document.*` type and both `knowledge-base.indexing.*` types
  - `memory.{auto-created,auto-approved,conflict.confirmation-needed}`
  - `security.two-factor.{enabled,disabled}`, `security.recovery-codes.regenerated` (minimized content)
  - `system.announcement.published`

  Take the exact list from the catalogue rather than this summary. `NotificationCatalogCoverageTests` (T187) asserts one published English template per emitted type per used channel, and T199 adds the Arabic versions of every file created here.
- [ ] T120 [US3] Register the Phase 6 services in `src/AskLucy.Infrastructure/DependencyInjection.cs` and `src/AskLucy.Application/DependencyInjection.cs`: `EmailChannelSender` as `INotificationChannelSender`, the rate limiter as a singleton, the classifier, the delivery and sweep services. Register the `NotificationDeliveryWorker` hosted service in `Program.cs`. Then run the boot verification (the full `Web.Tests` suite).

**Checkpoint**: Email delivery works on its own. Don't push yet: slice 2 also needs Phase 7.

---

## Phase 7: User Story 9 (part B) - Account emails move onto the hub (Priority: P2)

**Goal**: Every account email type (confirmation and its resend, email change, password reset, password changed, support request) and both admin-triggered sends go through the hub. They look the same as before, carry one-time links minted at send time, never leak tokens into storage, and never reveal whether an address exists.

**Independent Test**: Trigger every account email type. Each is produced once through the hub and shows in delivery monitoring with a masked address, and each link works exactly once. Scanning every notification, delivery, outbox, audit and log sink finds no token substring. A known and an unknown address give an identical response and a single outbox insert.

### Tests for User Story 9 (part B)

- [ ] T121 [P] [US9] `AccountEmailAntiEnumerationTests` in `tests/AskLucy.Web.Tests/Notifications/AccountEmailAntiEnumerationTests.cs` (FR-009e, SC-014). For password reset and confirmation-resend, a known and an unknown address give the same status, body and timing envelope, and exactly one outbox insert. An unknown address ends as `NoRecipient` with only a hashed address in the logs.
- [ ] T122 [P] [US9] `AccountEmailTokenLeakTests` in `tests/AskLucy.Web.Tests/Notifications/AccountEmailTokenLeakTests.cs` (FR-009d, SC-007). After each account email is sent, no token or link substring appears in `Notifications`, `NotificationDeliveries`, `NotificationOutboxEvents`, `NotificationAuditLogs` or a captured Serilog sink.
- [ ] T123 [P] [US9] `AccountLinkIssuerTests` in `tests/AskLucy.Infrastructure.Tests/Identity/AccountLinkIssuerTests.cs`:
  - a link is minted per kind;
  - validity is 60 min for reset and 24 h for confirmation and email change;
  - the link targets `AppOptions.FrontendBaseUrl`;
  - a reset link can be redeemed only once.
- [ ] T124 [P] [US9] `AccountEmailParityTests` in `tests/AskLucy.Infrastructure.Tests/Notifications/AccountEmailParityTests.cs`. For each account type, the subject, heading and body paragraphs rendered by the seeded hub template match the output of the current `BrandedAccountEmailTemplateRenderer` for the same inputs.
- [ ] T125 [P] [US9] Update the existing handler tests under `tests/AskLucy.Application.Tests/Authentication/`. Each changed handler publishes the right type and recipient, and no longer enqueues `IAccountEmailJob`, `IPasswordEmailJob` or `IPasswordResetIssuanceJob`.

### Implementation for User Story 9 (part B)

- [ ] T126 [US9] Add `IAccountLinkIssuer` (`IssueAsync(SensitiveLinkKind, userId, targetAddress, ct) → Uri`) in `src/AskLucy.Application/Notifications/Abstractions/IAccountLinkIssuer.cs`. Implement it in `src/AskLucy.Infrastructure/Identity/AccountLinkIssuer.cs`:
  - Use the same token providers and single-use persistence that `src/AskLucy.Application/Authentication/PasswordResetIssuanceJob.cs` and `AccountEmailJob` use today.
  - Only the timing moves to send time.
  - The link is never returned to anything except the renderer.
- [ ] T127 [US9] In `DeliveryProcessingService`, for types with a `SensitiveLinkKind`, call `IAccountLinkIssuer` at send time and pass the result only as the render-time `actionUrl`. Never persist or log it. `RequestValidity` sets the delivery `ExpiresAtUtc`: an expired request gives `Expired(RequestExpired)`, with no send.
- [ ] T128 [US9] Extend `OutboxDispatchService` to resolve two more recipient kinds:
  - `AddressLookup`: look up a user by normalized email. If there is none, complete the event with outcome `NoRecipient` and log only a SHA-256 hash of the address.
  - `SupportMailbox`: read the address from the existing SMTP support configuration only. It is never shown in any API response.
- [ ] T129 [P] [US9] Create the English account email seeds in `src/AskLucy.Infrastructure/Notifications/Templates/Seed/en/`:
  - `account.email-confirmation.requested.email.json`, `account.email-change.requested.email.json` and `account.password-reset.requested.email.json`
  - `account.support-request.submitted.email.json`, which HTML-encodes `messageBody`
  - `security.password-changed.email.json`

  Copy the wording from the current `BrandedAccountEmailTemplateRenderer` and `AccountEmailJob` content.
- [ ] T233 [US9] **Before T130**, capture the SC-014 baseline ("no slower than before"). Add `AccountEmailLatencyBaselineTests` in `tests/AskLucy.Web.Tests/Notifications/`, gated behind `RUN_SCALE_PERFORMANCE_TESTS=1`. Through the current Hangfire job path, with a capturing `IEmailSender`, it measures p95 time from the password-reset and email-confirmation requests to the mail hand-off over 50 runs. Record the numbers in [research.md R29](research.md). After T133 the same test measures the hub path and asserts p95 ≤ 60 s and ≤ the recorded baseline + 10%.
- [ ] T130 [US9] Replace the job enqueues with `INotificationPublisher.Publish` and the handler's own save, in `src/AskLucy.Application/Authentication/Commands/`:
  - `Register/RegisterCommandHandler.cs` and `ResendEmailConfirmation/ResendEmailConfirmationCommandHandler.cs`: `account.email-confirmation.requested`
  - `ChangeEmail/RequestEmailChangeCommandHandler.cs`: `AddressForUser` with the new address and `newEmailMasked`
- [ ] T131 [US9] Replace the job enqueues in the following handlers:
  - `src/AskLucy.Application/Authentication/Commands/RequestPasswordReset/RequestPasswordResetCommandHandler.cs`: `AddressLookup` for any submitted address, with an identical response.
  - `ResetPassword/ResetPasswordCommandHandler.cs` (~L100) and `ChangePassword/ChangePasswordCommandHandler.cs`: `security.password-changed`.
  - `RequestAccountSupport/RequestAccountSupportCommandHandler.cs`: `SupportMailbox`. This adds an `IUnitOfWork` dependency.
- [ ] T132 [US9] Replace the job calls in `src/AskLucy.Application/Users/Commands/AdminResendConfirmation/AdminResendConfirmationCommandHandler.cs` and `src/AskLucy.Application/Users/Commands/AdminSendPasswordReset/AdminSendPasswordResetCommandHandler.cs` with publishes of the matching account types to the target user.
- [ ] T133 [US9] Convert the legacy jobs into one-release forwarding shims: `src/AskLucy.Infrastructure/Email/AccountEmailJob.cs`, `src/AskLucy.Infrastructure/Email/PasswordEmailJob.cs` and `src/AskLucy.Application/Authentication/PasswordResetIssuanceJob.cs`.
  - Keep their public methods, because Hangfire jobs already enqueued with the old signatures must still run.
  - Each method publishes the equivalent hub request and saves.
  - Mark them `[Obsolete("Removed in the release after 067; forwards to the notification hub.")]`.
- [ ] T134 [US9] Slice 2 gate: run the full backend and frontend suites, then quickstart S3 (the email half) and S5 against the local SMTP catcher.

**Checkpoint**: Slice 2 (US3 + US9-B) is deployable. Before pushing, set `Notifications:Email:*` in the hand-deployed `appsettings.Production.json` ([quickstart §5](quickstart.md)).

---

## Phase 8: User Story 4 - Control which notifications I receive and how (Priority: P2)

**Goal**: A user sees their effective preferences for each category and channel, and changes the optional ones. Mandatory pairs are visibly locked, and they are enforced on the server.

**Independent Test**: Toggle category/channel pairs and verify that later events are delivered only on the enabled channels. Mandatory security notifications are always delivered. A direct `PUT` that disables a locked pair returns 422 and applies nothing.

### Tests for User Story 4

- [ ] T135 [P] [US4] `NotificationPreferencesHandlerTests` in `tests/AskLucy.Application.Tests/Notifications/NotificationPreferencesHandlerTests.cs`:
  - the effective merge of defaults and overrides;
  - `Billing` and `Conversation` are omitted;
  - a change back to the default deletes the override (sparse storage);
  - disabling a mandatory pair returns 422 with `errors["changes[i]"]` and applies nothing;
  - 1–40 changes are accepted;
  - only `Immediate` is accepted as the frequency.
- [ ] T136 [P] [US4] `NotificationPreferencesEndpointsTests` in `tests/AskLucy.Web.Tests/Notifications/NotificationPreferencesEndpointsTests.cs`. Test `GET` and `PUT`, with 401, 422 and 429. After a user turns off Workflow email, a `workflow.execution.failed` event gives email `Skipped(PreferenceDisabled)` and in-app `Delivered`.
- [ ] T137 [P] [US4] `NotificationPreferencesTab.test.tsx` and `.a11y.test.tsx` in `ClientApp/src/features/settings/components/`:
  - locked switches are disabled, with an explanation that screen readers announce;
  - a failed save shows an error toast and reverts the switch;
  - jest-axe passes.

### Implementation for User Story 4

- [ ] T138 [US4] Add upsert and delete to `NotificationPreferenceRepository`, then add the query `GetNotificationPreferences` in `src/AskLucy.Application/Notifications/Queries/GetNotificationPreferences/` and the command `UpdateNotificationPreferences` with its validator in `src/AskLucy.Application/Notifications/Commands/UpdateNotificationPreferences/`. The validator reads `NotificationTypeCatalog` to find the mandatory pairs.
- [ ] T139 [US4] Add `NotificationPreferencesController` in `src/AskLucy.Web/Controllers/v1/NotificationPreferencesController.cs`, serving `GET` and `PUT /api/v1/users/me/notification-preferences` under `notifications-endpoints`.
- [ ] T140 [P] [US4] Add the preferences API and hooks in `ClientApp/src/features/notifications/api/notificationPreferencesApi.ts` and `ClientApp/src/features/notifications/hooks/useNotificationPreferences.ts`.
- [ ] T141 [US4] Add `NotificationPreferencesTab.tsx` in `ClientApp/src/features/settings/components/`. It shows a table of categories by channel with switches, locks the mandatory pairs with a tooltip, and shows the frequency as "Immediate" only. Register it as a new **appended** tab index in `ClientApp/src/features/settings/settingsTabs.ts` (never renumber the existing tabs), and add it to `SettingsPage.tsx` and the `?tab=notifications` deep link.
- [ ] T142 [US4] Run the full backend and frontend suites.

**Checkpoint**: The preferences affect routing. Slice 3 also needs US5.

---

## Phase 9: User Story 5 - Act on approval requests securely (Priority: P2)

**Goal**: When a workflow or agent run waits for approval, the approver (the execution owner) is notified in-app and by email, with a link to the right approval screen. The screen re-checks authorization, and the approval notification's creation, delivery and reading are audited.

**Independent Test**: Put a workflow and an agent run into "awaiting approval". Only the approver is notified, on both channels. Opening the link signed out, as another user, or after approval rights were revoked is denied. Audit rows exist for created, delivered and read.

### Tests for User Story 5

- [ ] T143 [P] [US5] `ApprovalNotificationTests` in `tests/AskLucy.Application.Tests/Notifications/Emitters/ApprovalNotificationTests.cs`:
  - `RequestApproval` in both orchestrators publishes once to the execution owner, with `EventKey` `{agent|workflow}-approval:{approvalId}:requested`, the route with `?approval={approvalId}`, and the variables `intendedAction` and `nodeName`;
  - a repeated `RequestApproval` for the same approval doesn't duplicate.
- [ ] T144 [P] [US5] `ApprovalNotificationAccessTests` in `tests/AskLucy.Web.Tests/Notifications/ApprovalNotificationAccessTests.cs`:
  - signed-out access is 401;
  - another user gets 404 on the notification and on the execution or approval endpoints;
  - an approver whose rights were revoked gets 403 or 404 from the approval endpoint;
  - audit rows `ApprovalNotificationCreated`, `ApprovalNotificationDelivered` and `ApprovalNotificationRead` exist with the correlation id.
- [ ] T145 [P] [US5] Frontend page tests for `?approval=` deep links. The agent and workflow execution pages open their `ApprovalDialog` for a pending approval, and show "already decided" for a decided one (`features/agents/components/ApprovalDialog.tsx`, `features/workflows/components/ApprovalDialog.tsx`).

### Implementation for User Story 5

- [ ] T146 [US5] Publish `agent.approval.requested` in `AgentExecutionOrchestrator.RequestApproval` (~L311) in `src/AskLucy.Application/Agents/Runtime/AgentExecutionOrchestrator.cs`, before the save that persists the approval.
- [ ] T147 [US5] Publish `workflow.approval.requested` in `WorkflowExecutionOrchestrator.RequestApproval` (~L539/547) in `src/AskLucy.Application/Workflows/Runtime/WorkflowExecutionOrchestrator.cs`, before the save. Keep it out of Parallel-branch scopes.
- [ ] T148 [US5] Add approval auditing through `INotificationAuditWriter`, for types ending in `.approval.requested` only:
  - `OutboxDispatchService` writes created;
  - `DeliveryProcessingService` writes delivered, on email `Sent`;
  - `MarkNotificationRead` writes read.
- [ ] T149 [P] [US5] Support `?approval={approvalId}` on the agent execution page (route `/agents/:agentId/executions/:executionId`) and the workflow execution page (route `/workflows/:workflowId/executions/:executionId`): open the existing `ApprovalDialog` for that approval, and show an inline message when it is decided or not found.
- [ ] T150 [US5] Slice 3 gate: run the full backend and frontend suites, then quickstart S3 (the preferences half) and S4.

**Checkpoint**: Slice 3 (US4 + US5) is deployable.

---

## Phase 10: User Story 6 - Administrators monitor delivery and recover failures (Priority: P3)

**Goal**: Administrators with `admin.notifications.view` see statistics, channel health, deliveries and the audit. Those with `admin.notifications.manage` can also retry, bulk-retry and publish system announcements. Every admin action is audited, and no secret, token or unmasked address is ever exposed. This phase also adds the retention job, health checks and announcements (FR-004a).

**Independent Test**: Force failures (SMTP down, a rejected recipient) and check four things:
- the dashboard counts and health are accurate;
- failed items show safe details;
- a retry succeeds once the cause is fixed, and each action is audited;
- the permission rules hold: a user without View is refused everything, and View-only can see but not retry. A critical announcement reaches every active user in-app, plus email.

### Tests for User Story 6

- [ ] T151 [P] [US6] `AdminNotificationsEndpointsTests` in `tests/AskLucy.Web.Tests/Notifications/AdminNotificationsEndpointsTests.cs`:
  - the permission matrix: none gives 403 everywhere, V gives 200 on the GETs and 403 on the actions, M gives 200 everywhere;
  - no response contains mail credentials, the support address, tokens, account email bodies or an unmasked address;
  - `…Viewed` is audited at most once per admin per resource per hour (written by `AdminViewAuditBehavior`, T230, never by the query handlers).
- [ ] T152 [P] [US6] `RetryDeliveryHandlerTests` in `tests/AskLucy.Application.Tests/Notifications/RetryDeliveryHandlerTests.cs`:
  - 409 with each `reason` (`NotificationDeleted`, `NotificationExpired`, `RecipientDeleted`, `NotFailed`);
  - a successful retry resets to `Pending` and is audited as `DeliveryRetried`;
  - bulk retry by ids (1–200) or by filter (at most 1,000) reports `requested`, `retried` and `skipped`, and is audited once.
- [ ] T153 [P] [US6] `NotificationStatisticsQueryTests` in `tests/AskLucy.Persistence.Tests/Notifications/NotificationStatisticsQueryTests.cs`: the counts, success rate, average and p95 latency, backlog, hourly or daily buckets, and a maximum range of 90 days.
- [ ] T154 [P] [US6] `SystemAnnouncementTests` in `tests/AskLucy.Application.Tests/Notifications/SystemAnnouncementTests.cs`:
  - validation (title and message lengths, roles required for `Roles`, `endsAtUtc` in the future, no HTML or URLs);
  - fan-out in batches of 500 that resumes from `FanOutCursor` after a crash;
  - email only when `IsCritical`, with `ExpiresAtUtc = EndsAtUtc`;
  - audited as `AnnouncementPublished`.
- [ ] T155 [P] [US6] `NotificationRetentionTests` in `tests/AskLucy.Persistence.Tests/Notifications/NotificationRetentionTests.cs`: each retention class is deleted after its window in batches, and audit rows are never deleted.
- [ ] T156 [P] [US6] `NotificationHealthCheckTests` in `tests/AskLucy.Web.Tests/HealthChecks/NotificationHealthCheckTests.cs`:
  - a heartbeat older than 30 s is Unhealthy;
  - a backlog over 5 min is Degraded and over 30 min is Unhealthy;
  - an SMTP probe failure is only ever Degraded, and is cached for 5 min.
- [ ] T157 [P] [US6] Frontend tests, with a `.test.tsx` and an `.a11y.test.tsx` each, for `AdminNotificationsDashboardPage`, `AdminNotificationDeliveriesPage` and `AdminAnnouncementsPage` in `ClientApp/src/features/admin/pages/`:
  - View-only hides the retry and publish controls;
  - a failed retry shows a toast;
  - the publish dialog validates with Zod.

### Implementation for User Story 6

- [ ] T158 [P] [US6] Add the queries `GetNotificationStatistics` and `GetNotificationChannels` in `src/AskLucy.Application/Notifications/Queries/`. Statistics come from DB aggregates. Channel health reads the cached health-check results through a new `INotificationChannelHealthReader` abstraction. Both implement `IAuditedAdminView` (T230).
- [ ] T159 [P] [US6] Add the queries `GetNotificationDeliveries` (cursor, filters, masked recipient, `retryable`/`notRetryableReason`) and `GetNotificationDelivery` in `src/AskLucy.Application/Notifications/Queries/`. Omit the body for the Security and Account categories. Both implement `IAuditedAdminView` (T230).
- [ ] T160 [P] [US6] Add the commands `RetryNotificationDelivery` and `BulkRetryNotificationDeliveries` in `src/AskLucy.Application/Notifications/Commands/`, with their validators and audit writes.
- [ ] T161 [P] [US6] Add the query `GetNotificationAudit` (keyset, filters) in `src/AskLucy.Application/Notifications/Queries/GetNotificationAudit/`. It implements `IAuditedAdminView` (T230).
- [ ] T162 [US6] Add the command `PublishSystemAnnouncement` (with its validator) and the query `GetSystemAnnouncements` in `src/AskLucy.Application/Notifications/`. The command creates a `SystemAnnouncement` and publishes `system.announcement.published` with an `Audience` recipient.
- [ ] T163 [US6] Extend `OutboxDispatchService` to resolve `Audience` recipients: batches of 500 active users ordered by id, with `FanOutCursor` persisted after each batch and preferences loaded per batch.
- [ ] T164 [US6] Add `AdminNotificationsController.cs` (statistics, channels, deliveries, retry, bulk retry, audit) and `AdminAnnouncementsController.cs` in `src/AskLucy.Web/Controllers/v1/`. Use `[RequirePermission]` with V or M as the contract specifies, and the `admin-endpoints` rate policy.
- [ ] T165 [US6] Implement `RetentionService` in `src/AskLucy.Application/Notifications/Processing/RetentionService.cs` and `NotificationRetentionJob` in `src/AskLucy.Infrastructure/Notifications/Jobs/NotificationRetentionJob.cs`:
  - batched `ExecuteDeleteAsync` through a repository method;
  - registered as a daily `RecurringJob` in `Program.cs`.
- [ ] T166 [US6] Add the health checks `NotificationDispatcherHealthCheck`, `NotificationDeliveryWorkerHealthCheck`, `NotificationBacklogHealthCheck` and `NotificationSmtpHealthCheck` in `src/AskLucy.Infrastructure/Notifications/HealthChecks/`. Register them in `AddHealthChecks()` in `Program.cs` (~L619) with the readiness tag.
- [ ] T167 [P] [US6] Add the admin API client and hooks in `ClientApp/src/features/admin/api/adminNotificationsApi.ts` and `ClientApp/src/features/admin/hooks/useAdminNotifications.ts`, with MSW handlers.
- [ ] T168 [US6] Add the admin navigation:
  - add `admin.notifications.view` and `.manage` to `ClientApp/src/features/admin/adminPermissions.ts`;
  - add a Notifications group to `ClientApp/src/features/admin/adminNav.tsx` (Dashboard, Deliveries, Announcements, with Templates and Localization added later);
  - add lazy, permission-guarded routes to `ClientApp/src/routes/router.tsx` under `/admin/notifications/*`, using `AdminRoute`.
- [ ] T169 [P] [US6] Add `ClientApp/src/features/admin/pages/AdminNotificationsDashboardPage.tsx`. It has statistic cards, channel health chips, and a created/sent/failed series chart following the d3 patterns in `features/admin/charts/`.
- [ ] T170 [P] [US6] Add `ClientApp/src/features/admin/pages/AdminNotificationDeliveriesPage.tsx`, with filters, a cursor table, a detail drawer, and single and bulk retry that are hidden without manage permission.
- [ ] T171 [P] [US6] Add `ClientApp/src/features/admin/pages/AdminAnnouncementsPage.tsx`. It lists announcements and has a publish dialog (RHF + Zod) that shows the estimated recipients and email minutes, with a critical confirmation step.
- [ ] T172 [US6] Run the full backend and frontend suites, then quickstart S6 and S8.

**Checkpoint**: Admin operations work on their own.

---

## Phase 11: User Story 7 - Administrators manage versioned notification templates (Priority: P3)

**Goal**: Administrators create drafts, preview them with sample data in a sandboxed frame, send a test to themselves, and publish or archive versions, with concurrency control and validation of variables, URLs and HTML.

**Independent Test**:
1. Create a draft, preview it, send a test, and publish it. New notifications use it.
2. Editing the published version is rejected.
3. Create v2 and publish it. v1 is archived but kept, and v2 is used.
4. Unknown variables and code constructs are rejected.

### Tests for User Story 7

- [ ] T173 [P] [US7] `NotificationTemplateHandlerTests` in `tests/AskLucy.Application.Tests/Notifications/NotificationTemplateHandlerTests.cs`:
  - create and edit are allowed on drafts only (409 `VersionNotDraft`);
  - an `If-Match` mismatch returns 409 `ConcurrencyConflict`;
  - publish archives the previous version;
  - archiving the last published default returns 409 `LastPublishedDefault`;
  - an unknown variable, a malformed `{{`, or a raw URL or HTML returns 422 naming the token.
- [ ] T174 [P] [US7] `NotificationTemplateEndpointsTests` in `tests/AskLucy.Web.Tests/Notifications/NotificationTemplateEndpointsTests.cs`:
  - preview uses the production renderer and the sample link `https://example.invalid/sample-link`;
  - send-test goes only to the caller's verified address as type `template.test`, and appears in the deliveries;
  - the 11th send-test in an hour returns 429;
  - an admin with no verified address gets 422;
  - every action is audited.
- [ ] T175 [P] [US7] `AdminNotificationTemplatesPage.test.tsx` and `AdminNotificationTemplateEditorPage.test.tsx`, with `.a11y.test.tsx`, in `ClientApp/src/features/admin/pages/`:
  - the preview renders in an `<iframe sandbox="">` with `srcDoc`, never injected into the DOM;
  - the variable chips come from `declaredVariables`;
  - a 409 shows a reload prompt.

### Implementation for User Story 7

- [ ] T176 [P] [US7] Add the queries `GetNotificationTemplates`, `GetNotificationTemplate` (versions, `declaredVariables` and `isShippedDefault`) and `GetNotificationTemplateVersion` in `src/AskLucy.Application/Notifications/Queries/`. They implement `IAuditedAdminView` (T230).
- [ ] T177 [P] [US7] Add the commands `CreateTemplateDraft` and `UpdateTemplateDraft` in `src/AskLucy.Application/Notifications/Commands/`. Their validators use `TemplateTokenParser` against the catalogue's `DeclaredVariables`, and the length limits.
- [ ] T178 [P] [US7] Add the commands `PublishTemplateVersion` and `ArchiveTemplateVersion` in `src/AskLucy.Application/Notifications/Commands/`, with `RowVersion` concurrency and audit writes (`TemplateVersionPublished` with the previous and new version numbers, and `TemplateVersionArchived`).
- [ ] T179 [US7] Add `PreviewTemplateVersion` (a query) and `SendTemplateTest` (a command) in `src/AskLucy.Application/Notifications/`. The command publishes `template.test` to the caller, with sample variables and a sample link, and is audited as `TemplateTestSent`.
- [ ] T180 [US7] Add `AdminNotificationTemplatesController` in `src/AskLucy.Web/Controllers/v1/AdminNotificationTemplatesController.cs`, with the `If-Match` header parsed from base64 `RowVersion`. Add the rate-limit policy `notifications-test-send` (10 per hour per admin) in `Program.cs`, applied to send-test.
- [ ] T181 [P] [US7] Add `ClientApp/src/features/admin/pages/AdminNotificationTemplatesPage.tsx`, listing templates with filters (category, channel, language, type).
- [ ] T182 [US7] Add `ClientApp/src/features/admin/pages/AdminNotificationTemplateEditorPage.tsx`:
  - a version list;
  - a draft form (RHF + Zod) with the channel-specific fields and insertable variable chips;
  - a sandboxed-iframe preview;
  - send test;
  - publish and archive with confirmations, sending `If-Match`.

  Add both template routes and the Templates nav entry in `adminNav.tsx`.
- [ ] T183 [US7] Run the full backend and frontend suites, then quickstart S7.

**Checkpoint**: Slice 4 (US6 + US7) is deployable.

---

## Phase 12: User Story 8 (part A) - Localization foundation and Arabic notification screens (Priority: P3)

**Goal**: An administrator can enable localization and choose the supported languages. A user can then switch to Arabic and receive notifications, emails and notification screens in Arabic, right-to-left, with protected terms left untranslated. With localization disabled, everything stays exactly as it is today.

**Independent Test**:
1. With localization disabled, everything is English and no language switch is shown.
2. Enable `en` + `ar` and switch a user to Arabic. Trigger every notification type: Arabic content, RTL in the center, details and preferences, and `dir="rtl"` in email.
3. Remove `ar`. That user falls back to English.

### Tests for User Story 8 (part A)

- [ ] T184 [P] [US8] `EffectiveLanguageResolverTests` in `tests/AskLucy.Infrastructure.Tests/Notifications/EffectiveLanguageResolverTests.cs`:
  - the FR-044 chain: explicit request language, then the user's preferred language (only if enabled and supported), then `en`;
  - removing `ar` falls back to English;
  - the 30 s cache is evicted on update.
- [ ] T185 [P] [US8] `LocalizationEndpointsTests` in `tests/AskLucy.Web.Tests/Localization/LocalizationEndpointsTests.cs`:
  - admin `GET` and `PUT /api/v1/admin/localization`: `If-Match` 409, `en` required (422), unknown code 422, audited as `LocalizationSettingChanged`;
  - user `GET` and `PUT /api/v1/users/me/localization`: 422 when disabled or unsupported.
- [ ] T186 [P] [US8] `LocalizedSurfaceCultureMiddlewareTests` in `tests/AskLucy.Web.Tests/Middleware/LocalizedSurfaceCultureMiddlewareTests.cs`:
  - Problem Details `title`, `detail` and FluentValidation `errors` are in Arabic for an Arabic user on `[LocalizedSurface]` endpoints only;
  - non-localized endpoints stay English;
  - `traceId` is unchanged.
- [ ] T187 [P] [US8] `NotificationCatalogCoverageTests` in `tests/AskLucy.Infrastructure.Tests/Notifications/NotificationCatalogCoverageTests.cs` (SC-006). Every emitted type has an `en` and an `ar` seed for each channel it uses, and every seed parses and validates against its declared variables.
- [ ] T188 [P] [US8] `ProtectedTermsTests` in `tests/AskLucy.Infrastructure.Tests/Notifications/ProtectedTermsTests.cs` (SC-016):
  - every protected term in an `en` seed appears verbatim in the `ar` seed;
  - `ProtectedTerms.cs` matches `docs/localization/do-not-translate.md`.
- [ ] T189 [P] [US8] `ArabicEmailRenderTests` in `tests/AskLucy.Infrastructure.Tests/Notifications/ArabicEmailRenderTests.cs`: `lang="ar" dir="rtl"`, Western digits, Gregorian dates, and protected terms wrapped in `<bdi>`.
- [ ] T190 [P] [US8] Frontend i18n tests in `ClientApp/src/i18n/`:
  - `catalogCompleteness.test.ts`: all six Arabic plural forms, and no `en` value copied into `ar` unless it is on the allow-list;
  - `protectedTerms.test.ts`;
  - `format.test.ts`: `ar-u-nu-latn` and `ar-u-ca-gregory-nu-latn`;
  - `LocalizedSurface.test.tsx`: pass-through when disabled, and portaled Dialog and Menu content carries `dir="rtl"`.
- [ ] T191 [P] [US8] Add Arabic and RTL variants to the notification screen tests (`NotificationBell`, `NotificationPopover`, `NotificationsPage`, `NotificationDetails`, `NotificationPreferencesTab`) and add `LanguageSwitch.test.tsx`. Each renders in `ar`/`rtl`, asserts `dir="rtl"` on the surface root, has no dev-fallback spy hits, and passes jest-axe in light and dark.

### Implementation for User Story 8 (part A)

- [ ] T192 [P] [US8] Add `LocalizationSetting.cs` (a singleton with `IsEnabled`, `SupportedLanguages` and `RowVersion`, where `en` is always included) and `PlatformLanguages.cs` (`en` and `ar`, with native names) in `src/AskLucy.Domain/Localization/`. Add `PreferredLanguage` (`nvarchar(10)`, nullable) to `src/AskLucy.Persistence/Identity/ApplicationUser.cs`.
- [ ] T193 [US8] Add the configurations `src/AskLucy.Persistence/Configurations/Localization/LocalizationSettingConfiguration.cs` and the `ApplicationUser` column, and the `DbSet`. Create the migration `AddLocalizationSettingAndUserPreferredLanguage`: it seeds the singleton row with `IsEnabled=false` and `["en"]`, and has no BOM.
- [ ] T194 [US8] Add `ILocalizationSettingsProvider` in `src/AskLucy.Application/Localization/`, with a Persistence implementation cached in `IMemoryCache` for 30 s and evicted on update. Replace the English-only `EffectiveLanguageResolver` with the full FR-044 chain.
- [ ] T195 [P] [US8] Add the queries and commands `GetLocalizationSettings`, `UpdateLocalizationSettings` (audited), `GetMyLocalization` and `SetMyLanguage` in `src/AskLucy.Application/Localization/`.
- [ ] T196 [US8] Add `AdminLocalizationController.cs` (V/M, `admin-endpoints`) and `UserLocalizationController.cs` (`notifications-endpoints`) in `src/AskLucy.Web/Controllers/v1/`.
- [ ] T197 [US8] Add the server-side localization:
  - `src/AskLucy.Application/Localization/Messages.resx` and `Messages.ar.resx` (Problem Details titles, validation and confirmation messages);
  - `LocalizedSurfaceAttribute` and `src/AskLucy.Web/Middleware/LocalizedSurfaceCultureMiddleware.cs`, which set `CultureInfo.CurrentUICulture` from the effective language on endpoints with that metadata only;
  - localize `title` and `detail` in `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs`;
  - enable FluentValidation's built-in Arabic messages;
  - apply `[LocalizedSurface]` to `NotificationsController`, `NotificationPreferencesController`, `UserLocalizationController` and every `Admin*` controller.
- [ ] T198 [P] [US8] Write `docs/localization/do-not-translate.md`, the canonical list: vendor, product and model names such as OpenAI, Anthropic, Gemini and OpenRouter, and acronyms such as API, MCP, SMTP, 2FA, RAG, OCR and BIM. Add `src/AskLucy.Infrastructure/Notifications/Templates/ProtectedTerms.cs` mirroring it.
- [ ] T199 [P] [US8] Create the Arabic seeds in `src/AskLucy.Infrastructure/Notifications/Templates/Seed/ar/`: one file for every `en` seed file (in-app and email). Protected terms stay verbatim, and dates and numbers are variables. The seeder picks them up without code changes.
- [ ] T200 [P] [US8] Write `docs/adr/0019-frontend-i18n-and-rtl.md`, recording:
  - the in-house `i18n/` module instead of a library;
  - the scope (notification screens and the admin area only), with the §7 complexity-tracking justification;
  - the RTL Emotion cache;
  - d3 charts kept LTR.
- [ ] T201 [US8] In `ClientApp/`, run `npm install stylis-plugin-rtl @emotion/cache`. Then restore any `@emnapi/*` entries that the Windows install pruned from `package-lock.json`, and verify with `npm ci`, not `npm install`.
- [ ] T202 [US8] Create the `ClientApp/src/i18n/` module per [contracts/localization-ui.md](contracts/localization-ui.md):
  - `types.ts` (`MessagesOf`, `Language`), `useT.ts`, `format.ts`, `protectedTerms.ts` and `useLocalization.ts` (TanStack Query);
  - `LocalizedSurface.tsx`, with `scope="page"` and `"subtree"`, an RTL `ThemeProvider` that keeps `themeStore` light/dark, a `muirtl` Emotion cache, and portal `slotProps`;
  - `messages/en/common.ts` and `messages/ar/common.ts`.
- [ ] T203 [US8] Add `messages/en/notifications.ts` and `messages/ar/notifications.ts` (`satisfies MessagesOf<typeof enNotifications>`). Move `NotificationBell`, `NotificationPopover`, `NotificationList`, `NotificationItem`, `NotificationDetails`, `NotificationsPage` and `NotificationPreferencesTab` onto `useT` and `format.ts`, each wrapped in `<LocalizedSurface scope="subtree">`. Use logical CSS properties only.
- [ ] T204 [US8] Add `ClientApp/src/features/settings/components/LanguageSwitch.tsx`. It appears in the account menu through `ClientApp/src/components/account/useAccountMenuItems.tsx`, only when localization is enabled with more than one supported language. Show an error toast on failure.
- [ ] T205 [US8] Add `ClientApp/src/features/admin/pages/AdminLocalizationPage.tsx`:
  - an enable toggle and supported-language checkboxes, with `en` locked;
  - `If-Match` handling, with a reload prompt on 409.

  Add its route and a Localization entry in the Notifications group of `adminNav.tsx`.
- [ ] T206 [US8] Slice 5 gate: run the full backend and frontend suites, then quickstart S9 (the user half). Confirm that with localization disabled the English rendering is unchanged, and that the existing page tests pass untouched.

**Checkpoint**: Slice 5 is deployable. It stays dark until an administrator enables localization.

---

## Phase 13: User Story 8 (part B) - Admin area in Arabic (Priority: P3)

**Goal**: When an Arabic-preferring administrator opens the admin area, every screen is in Arabic and right-to-left. That covers the shell, all 12 existing sections and the 5 notification admin screens. Protected names and acronyms stay untranslated, and the screens remain accessible (FR-046a, SC-011).

**Independent Test**: With localization on, switch an administrator to Arabic and open every admin screen. Each one is in Arabic, RTL, and free of catalog fallbacks, with d3 axes kept LTR and protected terms intact, and jest-axe passes in light and dark.

### Tests for User Story 8 (part B)

- [ ] T207 [P] [US8] Add `ar`/`rtl` cases to `ClientApp/src/features/admin/components/AdminShell.test.tsx` and `AdminShell.a11y.test.tsx`:
  - `<html dir="rtl" lang="ar">` while mounted, restored on unmount;
  - the collapse arrow flips;
  - sidebar labels are in Arabic.
- [ ] T208 [P] [US8] Add an `.rtl.test.tsx` for each of the 12 admin sections. Each renders the page in `ar`/`rtl`, asserts `dir`, has no fallback-spy hits, and passes jest-axe in light and dark:
  - `AdminDashboardPage`, `AdminUsersPage`, `AdminRolesPage`, `AdminRoleAssignmentsPage`
  - `AdminSystemAgentsPage`, `AdminAiProvidersPage`, `AdminDefaultModelsPage`, `AdminAiCapabilitiesPage`
  - `features/agents/pages/AgentPoliciesAdminPage`, `features/workflows/pages/WorkflowPoliciesAdminPage`, `features/mcp/pages/McpAdministrationPage`
  - the Jobs entry, which is part of the shell test
- [ ] T209 [P] [US8] Add an `.rtl.test.tsx` for each of the 5 notification admin screens: Dashboard, Deliveries, Templates (list and editor), Announcements and Localization.

### Implementation for User Story 8 (part B)

- [ ] T210 [US8] Add `messages/{en,ar}/admin/shell.ts`. Wrap `ClientApp/src/features/admin/components/AdminShell.tsx` in `<LocalizedSurface scope="page">`, move the shell and `adminNav.tsx` labels onto `useT('admin.shell')`, flip the directional icons, and use logical CSS properties.
- [ ] T211 [P] [US8] Dashboard: add `messages/{en,ar}/admin/dashboard.ts`. Move `AdminDashboardPage.tsx` and `features/admin/charts/{NewUsersTrendChart, RoleDistributionChart, StatusSplitChart}.tsx` onto `useT`. The SVG roots keep `direction="ltr"`, while the legends, captions and axis labels are translated.
- [ ] T212 [P] [US8] Users: add `messages/{en,ar}/admin/users.ts` and move `AdminUsersPage.tsx` onto `useT`. Mirror the table, and wrap identifier cells in `<bdi dir="ltr">`.
- [ ] T213 [P] [US8] Roles and role assignments: add `messages/{en,ar}/admin/roles.ts` and `roleAssignments.ts`, and move `AdminRolesPage.tsx` and `AdminRoleAssignmentsPage.tsx` onto `useT`. Role names come from users, so they pass through as parameters only.
- [ ] T214 [P] [US8] System agents: add `messages/{en,ar}/admin/systemAgents.ts` and move `AdminSystemAgentsPage.tsx` onto `useT`.
- [ ] T215 [P] [US8] AI providers, default models and AI capabilities: add `messages/{en,ar}/admin/aiProviders.ts`, `defaultModels.ts` and `aiCapabilities.ts`, and move `AdminAiProvidersPage.tsx`, `AdminDefaultModelsPage.tsx` and `AdminAiCapabilitiesPage.tsx` onto `useT`. Provider and model names are protected terms and stay verbatim.
- [ ] T216 [P] [US8] Agent and workflow policies: add `messages/{en,ar}/admin/agentPolicies.ts` and `workflowPolicies.ts`, and move `features/agents/pages/AgentPoliciesAdminPage.tsx` and `features/workflows/pages/WorkflowPoliciesAdminPage.tsx` onto `useT`.
- [ ] T217 [P] [US8] MCP servers and jobs: add `messages/{en,ar}/admin/mcpServers.ts` and `jobs.ts`, and move `features/mcp/pages/McpAdministrationPage.tsx` and the Jobs sidebar entry's label and helper text onto `useT`. The Hangfire dashboard itself stays untranslated.
- [ ] T218 [US8] Add `messages/{en,ar}/admin/notifications.ts`, and move the 5 notification admin screens (`AdminNotificationsDashboardPage`, `AdminNotificationDeliveriesPage`, `AdminNotificationTemplatesPage`/`EditorPage`, `AdminAnnouncementsPage` and `AdminLocalizationPage`) onto `useT` and `format.ts`.
- [ ] T219 [US8] Slice 6 gate: run `npx tsc -b --noEmit` (catalog completeness), lint, the full `npm test`, and quickstart S9 (the admin half).

**Checkpoint**: Slice 6 is deployable. The whole admin area works in Arabic.

---

## Phase 14: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, the gated scale and proof suites, a security review, and release readiness.

- [ ] T220 [P] Update `docs/ARCHITECTURE.md` with a Notification hub section: the outbox, dispatcher and delivery worker; the catalogue, router and templates; the channel seam; how modules emit. Link ADR 0018 and ADR 0019.
- [ ] T221 [P] Update `docs/DATABASE.md` with the 9 new tables and column, the indexes and their purpose, the retention windows, the legacy tables pending the follow-up drop, and the migration notes.
- [ ] T222 [P] Update `docs/API_GUIDELINES.md` with the user and admin notification endpoints, the localization endpoints, the new rate-limit policies, the `/hubs/notifications` events, and the removed legacy endpoints and events.
- [ ] T223 [P] Update `docs/SECURITY.md` with send-time link minting, anti-enumeration, content minimization, the masking rules, the sandboxed preview, and the audit coverage.
- [ ] T224 [P] Add `NotificationCenterPerformanceTests` in `tests/AskLucy.Web.Tests/Notifications/NotificationCenterPerformanceTests.cs` (SC-004). Seed 100k notifications per user, then assert p95 targets for paging and unread count. Gate it behind `RUN_SCALE_PERFORMANCE_TESTS=1`, like the existing scale tests. The gate is a justified deviation from constitution §10, recorded in plan.md Complexity Tracking.
- [ ] T231 [P] Add `NotificationsOpenApiTests` in `tests/AskLucy.Web.Tests/Notifications/` (constitution §6, §13). Load the generated OpenAPI document from the test host and assert that every route in [contracts/](contracts/) (user notifications, preferences, localization, admin notifications, templates, announcements) is present with the status codes the contract documents. Add `[ProducesResponseType]` to the controllers where the test finds a gap.
- [ ] T232 [P] Add `NotificationThroughputTests` in `tests/AskLucy.Web.Tests/Notifications/` (SC-005 and the 500-document edge case), gated behind `RUN_SCALE_PERFORMANCE_TESTS=1`. Publish 10,000 notifications over a compressed hour with a fake email sender, plus a burst of 500 `document.processing.failed` for one user. Assert the in-app backlog is empty within 10 minutes of the burst, and that the email backlog drains at the configured send limit with mandatory sends never starved (see the SC-005 wording in spec.md).
- [ ] T234 [P] Add `NotificationLatencyTests` in `tests/AskLucy.Web.Tests/Notifications/`, gated behind `RUN_SCALE_PERFORMANCE_TESTS=1`. Assert p95 event→`notificationCreated` push under 5 s (SC-001) and p95 event→mail hand-off under 2 min with a healthy fake mail server (SC-002), over 200 events each.
- [ ] T225 [P] Add `StubChannelProofTests` in `tests/AskLucy.Web.Tests/Notifications/StubChannelProofTests.cs` (SC-012). Register a `TestChannelSender` and a test catalogue entry and template, and assert delivery with **no** change to the publisher, the router or the emitters.
- [ ] T226 Security review pass:
  - every notification query and command filters by the current user id or checks permissions;
  - there is no cross-tenant path through the cursor, bulk retry filter, audit filter or preview;
  - no `dangerouslySetInnerHTML` in `features/notifications` or the admin notification pages;
  - no token or link in logs.

  Fix the findings and add regression tests.
- [ ] T227 Run `dotnet format "Ask Lucy.sln" --verify-no-changes`. Tell ENDOFLINE noise apart from a real `\r\r\n`, check the migration files have no BOM and put `System` usings first, then run `dotnet build -warnaserror` and the full `dotnet test "Ask Lucy.sln"`.
- [ ] T228 Run every scenario in [quickstart.md](quickstart.md), S1–S10, plus the §4 fault-injection run. Record the outcomes in `specs/067-notifications-communication-hub/quickstart.md` under a "Validation log" heading.
- [ ] T229 Prepare the release checklist in [quickstart.md §5](quickstart.md). List for the user the keys they must hand-add to the untracked `appsettings.Production.json`: `Notifications:Email:MaxPerMinute`, `ReservedPerMinuteForMandatory` and `Retention:*`. Confirm the `/health/ready` checks after deploy. Include release notes for each slice (constitution §13), and state that knowledge-base documents uploaded before slice 1 stay un-indexed until re-uploaded.

---

## Dependencies & Execution Order

### Phase dependencies

| Phase | Depends on | Blocks |
|---|---|---|
| 1 Setup | — | 2 |
| 2 Foundational | 1 | every story |
| 3 US1 center | 2 | slice 1 deploy |
| 4 US2 emitters | 2 (runs in parallel with 3); the US1 center is needed to *see* results | slice 1 deploy |
| 4b KB indexing trigger | 4 (emitter patterns) | slice 1 deploy |
| 5 US9-A legacy import | 4 (`LegacyNotificationTypeMap`, notifier rewrites) | slice 1 deploy |
| 6 US3 email | 2 | 7, and the email half of 8, 9 and 10 |
| 7 US9-B account emails | 6 | slice 2 deploy |
| 8 US4 preferences | 2 (API and UI); the Settings deep-link in 4 | slice 3 deploy |
| 9 US5 approvals | 4 (orchestrator emit patterns), 6 (email) | slice 3 deploy |
| 10 US6 admin ops | 6 (deliveries to monitor) | 11 (nav group) |
| 11 US7 templates | 10 (nav group, admin API client) | slice 4 deploy |
| 12 US8-A localization | 3, 8, 10 (screens to localize); seeds from 2, 6 and 7 | 13 |
| 13 US8-B admin Arabic | 12 | — |
| 14 Polish | every story | — |

### Deployment order

Every push to main deploys, and the user pushes directly to main.

1. **Slice 1**: Phases 1–5, including 4b. Push only after the Phase 5 gate; FR-009a makes it atomic.
2. **Slice 2**: Phases 6–7.
3. **Slice 3**: Phases 8–9.
4. **Slice 4**: Phases 10–11.
5. **Slice 5**: Phase 12.
6. **Slice 6**: Phase 13.
7. **Follow-up release** (not in this task list): the `DropLegacyDocumentAndMemoryNotifications` migration, and removing the three `[Obsolete]` forwarding shims.

### Within each phase

- Write the test tasks first and confirm they fail.
- Order: Domain → Application abstractions → Persistence → handlers and services → controllers and `Program.cs` → frontend API → hooks → components → pages.
- Migrations run strictly in sequence, because they share the model snapshot.
- `Program.cs`, `DependencyInjection.cs`, `AskLucyDbContext.cs`, `router.tsx` and `adminNav.tsx` are shared files. Their edits are never marked [P] against each other.

---

## Parallel Examples

### Phase 2 (Foundational)

```text
# The domain tests together:
T: NotificationRouterTests      T: NotificationTypeCatalogTests
T: NotificationStateMachineTests T: TemplateTokenParserTests
# The domain entities together, after the enums:
T: Notification + NotificationDelivery   T: NotificationOutboxEvent
T: NotificationTemplate + Version        T: NotificationPreference   T: SystemAnnouncement + AuditLog
# The EF configurations together, before AskLucyDbContext and the migrations:
T: Notification/Delivery config  T: Outbox config  T: Template configs  T: Preference/Announcement/Audit configs
```

### User Story 1

```text
T: NotificationCenterQueryTests  T: NotificationsEndpointsTests  T: NotificationCenterHandlerTests  T: frontend component tests
# then:
T: GetNotifications query        T: GetUnreadCount/GetNotification   T: MarkRead/MarkAll/Delete commands   T: notificationsApi.ts
```

### User Story 2

```text
T: AgentExecutionNotificationTests  T: WorkflowExecutionNotificationTests  T: DocumentNotificationEmitterTests
T: SecurityNotificationEmitterTests T: notifier test updates               T: EmitterEndToEndTests
# then: the access checks (5 files) and the frontend deep links, alongside the emitter edits (different files)
```

### User Story 3

```text
T: SmtpFailureClassifierTests  T: EmailSendRateLimiterTests  T: EmailTemplateRenderTests
T: DeliveryProcessingServiceTests  T: DeliveryClaimRaceTests  T: DeliveryFaultInjectionTests
# then: SmtpFailureClassifier + EmailSendRateLimiter, INotificationChannelSender, and the English email seeds, in parallel
```

### User Story 6

```text
T: statistics/channels queries  T: deliveries queries  T: retry commands  T: audit query
T: Dashboard page  T: Deliveries page  T: Announcements page   (after the API client and nav)
```

### User Story 8 (part B)

```text
# After AdminShell moves to LocalizedSurface, the section refactors run fully in parallel (separate catalog and page files):
T: dashboard  T: users  T: roles+assignments  T: systemAgents  T: aiProviders/defaultModels/aiCapabilities
T: agent/workflow policies  T: mcpServers+jobs
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 Setup, then Phase 2 Foundational.
2. Phase 3 (US1). **Stop and validate** locally with seeded data: the bell, the center, live arrival, and the independent test.
3. Don't push the MVP alone, because production would get an empty center next to the legacy inboxes. Continue to Phases 4–5 and push slice 1 as one increment.

### Incremental delivery

| Slice | Phases | User-visible result |
|---|---|---|
| 1 | 1–5 (with 4b) | A live notification center fed by every platform module, with legacy notifications migrated; knowledge-base uploads are indexed |
| 2 | 6–7 | Email delivery with retry and dead letter; account emails through the hub |
| 3 | 8–9 | User preferences; secure approval notifications |
| 4 | 10–11 | Admin monitoring, retry, announcements and templates |
| 5 | 12 | Arabic notifications and screens, dark until enabled |
| 6 | 13 | A fully Arabic admin area |

Each slice ends with a gate task (full suites plus quickstart scenarios) and leaves production coherent.

---

## Notes

- [P] tasks touch different files and don't depend on an incomplete task in the same phase.
- The route reconciliation in Phase 2 corrects data-model.md: its catalogue routes predate a check against `router.tsx`.
- Phase 4b closes a pre-existing gap: retrieval indexing had no production caller, so knowledge-base uploads were never indexed (R27). Only `knowledge-base.updated` stays not-emitted.
- New tasks added after `/speckit-analyze` use IDs T230–T240 and sit where they run: T230 in Phase 2, T235–T240 in Phase 4b, T233 in Phase 7, and T231, T232 and T234 in Phase 14.
- Commit after each task or logical group, and verify that your files actually landed in the commit, because a concurrent session can reset the tree.
- `.specify/feature.json` is shared with concurrent sessions working on other specs. Resolve this feature's paths explicitly instead of switching it.
