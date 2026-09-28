# Implementation Plan: Notifications & Communication Hub

**Branch**: `main` (solo workflow, pushed directly to main) | **Date**: 2026-09-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/067-notifications-communication-hub/spec.md`

## Summary

This feature adds one central notification hub that every module uses to say *what happened*. The hub decides who is told, on which channel, in which language and with which template. It then delivers reliably through in-app and email, and moves every existing notification source onto it.

The technical approach:
- **Capture**: modules call `INotificationPublisher.Publish(...)`. That writes a **transactional outbox** row inside the caller's own unit of work, so a crash can never lose a notification or report a rolled-back change.
- **Dispatch**: an in-process `BackgroundService` claims outbox events and routes them through a pure Domain `NotificationRouter`, driven by a code-owned type catalogue, sparse user preferences and the effective language. It then materializes notifications and per-channel deliveries idempotently.
- **In-app delivery**: pushed through a new SignalR `NotificationHub`, since FR-018 allows no second real-time mechanism.
- **Email delivery**: a lease-claiming worker renders logic-free, structured templates into the existing spec-061 branded shell and sends them through the existing MailKit STARTTLS sender. Sending is at-most-once, with a reserved rate-limit lane for account emails.
- **One-time links**: minted at send time, so no token is ever stored.
- **Migration**:
  - Legacy document and memory notifications are imported idempotently, and their old channels are retired in the same release.
  - Account emails move behind the hub without weakening anti-enumeration.
- **Localization**: disabled by default. It adds an admin setting, a user language choice, Arabic templates, and an in-house typed i18n layer with scoped RTL for the notification screens and the whole admin area.

Full rationale is in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10 (backend). TypeScript 5 with React 19 (frontend).

**Primary Dependencies**:
- **Existing**:
  - ASP.NET Core 10, EF Core 10 (SQL Server), MediatR 14, FluentValidation 12 and AutoMapper.
  - Hangfire 1.8, used only for recurring maintenance jobs here.
  - SignalR, MailKit 4.17, Serilog and ASP.NET Identity.
  - MUI 9 with Emotion 11, TanStack Query 5, Zustand 5, RHF, Zod 4, d3 7, `@microsoft/signalr`, and Vitest with jest-axe and MSW.
- **New**:
  - Frontend: `stylis-plugin-rtl`, plus an explicit `@emotion/cache` (already a transitive dependency). Recorded in ADR 0019.
  - Backend: no new packages. Metrics use BCL `System.Diagnostics.Metrics`, and resources use BCL resx.

**Storage**: SQL Server via EF Core code-first.
- **New tables**: `Notifications`, `NotificationDeliveries`, `NotificationOutboxEvents`, `NotificationTemplates`, `NotificationTemplateVersions`, `NotificationPreferences`, `SystemAnnouncements`, `NotificationAuditLogs` and `LocalizationSettings`.
- **New column**: `AspNetUsers.PreferredLanguage`.
- **Legacy tables**: `DocumentNotifications` and `MemoryNotifications` take no new rows in this release (their rows are imported, then only deleted by retention) and are dropped in a follow-up (see [data-model.md](data-model.md)).

**Testing**:
- **Backend**: xUnit across `tests/AskLucy.{Domain,Application,Infrastructure,Persistence,Web}.Tests`. `Web.Tests` and `Persistence.Tests` run against the shared site4now test DB.
- **Frontend**: Vitest, React Testing Library, jest-axe for a11y, and MSW for the API. `tsc -b --noEmit` enforces catalog completeness.

**Target Platform**: A shared-hosting Windows/IIS web host (site4now) running ASP.NET Core in-process, with the SPA served by the same app. Outbound mail goes to the myasp.net SMTP host over STARTTLS.

**Project Type**: A web application: a Clean Architecture backend with the React SPA in `src/AskLucy.Web/ClientApp`.

**Performance Goals**:

| Criterion | Target |
|---|---|
| SC-001 | In-app appears within 5 s (p95) |
| SC-002 | Email handed off within 2 min (p95) |
| SC-014 | Account emails handed off within 1 min (p95) |
| SC-004 | Center first page and unread count load in under 1 s at 100k notifications per user |
| SC-005 | 10k notifications per hour, with the backlog drained within 10 min of a burst |

**Constraints**:
- 0 lost and 0 duplicate emails under fault injection (SC-003).
- The email send rate stays within the mail host's limit, which is configurable and must be confirmed before release.
- Shared-host outbound timeouts must be generous (60 s for SMTP).
- The request path does no delivery I/O (FR-007).
- One-time tokens are never persisted (FR-009d).
- English rendering is unchanged while localization is disabled.

**Scale/Scope**:
- 33 emitted notification types across 8 categories, plus defined-but-not-emitted Conversation and Billing types.
- 2 channels and 2 languages.
- About 20 user-facing and about 25 admin endpoints.
- The frontend has 4 new user surfaces (bell, center, details, preferences plus the language switch) and 5 new admin screens.
- Arabic retrofit of 12 existing admin sections plus the admin shell.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Checked against constitution v1.1.1. **Pre-research: PASS. Post-design re-check: PASS**, with one scoping entry justified in Complexity Tracking.

| Gate | Evidence in this design | Result |
|---|---|---|
| **I. Clean Architecture / dependency rule** | Domain holds the entities, catalogue and router, with no packages. Application holds the abstractions, CQRS handlers and processing services. Persistence and Infrastructure implement the Application interfaces. Web is composition root and controllers only. Application gains no EF, SMTP or SignalR reference: the resx resources are BCL. | ✅ |
| **II. SOLID** | One channel sender per channel (`INotificationChannelSender`). The router, renderer, link builder, language resolver and link issuer each have a single responsibility. New channels are added without editing the router (OCP; SC-012). | ✅ |
| **III. Simplicity (YAGNI)** | There is no broker, no templating engine, no i18n runtime and no attempts table. There is one outbox and two polling workers, and Hangfire is kept only for recurring jobs. Digests are only stored (FR-033). | ✅ |
| **V. DI and testability** | Every external concern sits behind an interface: SMTP, SignalR, the clock via `TimeProvider`, Identity token minting and the wake signal. The router and renderer are pure and unit-testable without I/O. | ✅ |
| **VIII. No silent failures** | Every delivery failure is recorded with a reason and correlation id, and is visible to admins (FR-030). An ambiguous outcome is flagged, not dropped. A push failure is logged at Warning. The worker loops catch, log and back off; they never swallow. Frontend: every query and mutation has a toast, inline error or retry path (FR-019). | ✅ |
| **§3 CQRS and domain events** | All behavior is MediatR commands and queries. Reactions are dispatched **after commit** through the outbox, not as in-handler side effects (R2). Queries never mutate: admin `…Viewed` audit rows are written by `AdminViewAuditBehavior`, an `IPipelineBehavior` keyed on the `IAuditedAdminView` marker, not by the query handlers (T230). | ✅ |
| **§3 Infrastructure isolation** | SMTP stays behind the extended `IEmailSender`. The provider is swappable through DI only (FR-023). | ✅ |
| **§5 Keys, FKs, indexes** | Guid v7 surrogate keys everywhere. Every FK is indexed. Every query path has a covering or filtered index, as listed in data-model.md. | ✅ |
| **§5 Migrations** | Five additive, reversible migrations. Legacy tables are dropped in a later release (two-step). No BOM, and `System` usings come first. | ✅ |
| **§5 Concurrency** | `RowVersion` on templates, versions, the localization setting and announcements. `If-Match` on edits, with 409 handled in Application. Worker claims use conditional updates (R4). | ✅ |
| **§5 Soft delete** | Owner deletion uses `DeletedAtUtc` plus the global filter. Retention does the hard delete (FR-016a, FR-059). | ✅ |
| **§5 One transaction per business operation** | The publisher joins the caller's unit of work. Materialization is one `SaveChanges` per event (or per fan-out batch). | ✅ |
| **§6 API standards** | `/api/v1`, kebab-case plural resources, `…/actions/{verb}` sub-resources, Problem Details with `traceId`, cursor pagination for the center, deliveries and audit, and OpenAPI through the existing generator. | ✅ |
| **§6 Rate limiting** | `notifications-endpoints`, `admin-endpoints` and `notifications-test-send`. The hub has no client methods. | ✅ |
| **§7 UI** | MUI theme with light, dark and RTL. WCAG AA with jest-axe per screen in light, dark and RTL. Responsive layouts. TanStack Query for server state and Zustand only for UI flags. RHF with Zod mirroring the validators. Lazy routes. The full-page list is virtualized. | ✅ |
| **§7 Internationalization** | An i18n framework is introduced: typed catalogs plus `useT`. Every string on the localized surfaces comes from catalogs, which fails the build if one is missing. Other surfaces keep their centralized copy. | ⚠️ scoped, see Complexity Tracking |
| **§7 Voice** | Not touched. Notifications have no TTS. | n/a |
| **§8 Security** | OWASP coverage per the spec's Security Threats table: ownership returns 404, plain-text in-app rendering, context escaping, header sanitization, constructed links only, no stored tokens, a masked PII address, server-only secrets, and an immutable audit. | ✅ |
| **§10 Testing** | Unit tests (Domain router, catalogue, renderer), integration tests (repositories, workers, fault injection against the real DB), a11y tests, an OpenAPI contract test, gated scale and latency tests, and a stub-channel proof. | ⚠️ perf gating, see Complexity Tracking |
| **§13 and §17 ADRs** | **ADR 0018**: transactional notification outbox and in-process workers, a new cross-cutting pattern. **ADR 0019**: frontend i18n and RTL approach, a new dependency and a precedent. Both ship with the implementation. | ✅ (planned deliverables) |
| **§14 Observability** | Correlation id from edge to delivery. The `AskLucy.Notifications` meter. `notifications-*` checks on `/health/ready`. Structured Serilog events. | ✅ |
| **§15 Performance** | Keyset paging, filtered covering indexes, set-based mark-all and retention, and admin screens lazy-loaded. | ✅ |

## Project Structure

### Documentation (this feature)

```text
specs/067-notifications-communication-hub/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0: R1–R26 decisions
├── data-model.md        # Phase 1: entities, catalogue, state machines, legacy mapping, migrations
├── quickstart.md        # Phase 1: automated suites + end-to-end validation + release checklist
├── contracts/
│   ├── notifications-api.md        # user REST (center, preferences, language)
│   ├── admin-notifications-api.md  # admin REST (stats, deliveries, templates, announcements, localization, audit)
│   ├── notification-hub.md         # SignalR events; removed legacy events
│   ├── module-integration.md       # INotificationPublisher + internal seams + emit points
│   └── localization-ui.md          # frontend i18n/RTL contract
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks — not created here)

docs/adr/0018-transactional-notification-outbox.md     # written during implementation
docs/adr/0019-frontend-i18n-and-rtl.md                  # written during implementation
docs/localization/do-not-translate.md                   # canonical protected-terms list (FR-046b)
docs/ARCHITECTURE.md, docs/DATABASE.md, docs/API_GUIDELINES.md   # updated (Documentation principle)
```

### Source Code (repository root)

```text
src/AskLucy.Domain/
├── Notifications/                      # NEW
│   ├── Notification.cs  NotificationDelivery.cs  NotificationOutboxEvent.cs
│   ├── NotificationTemplate.cs  NotificationTemplateVersion.cs  NotificationPreference.cs
│   ├── SystemAnnouncement.cs  NotificationAuditLog.cs
│   ├── NotificationTypeCatalog.cs  NotificationTypeDefinition.cs  NotificationRouter.cs
│   ├── TemplateTokenParser.cs          # {{ var }} syntax + declared-variable check (pure)
│   └── Enums/ (Category, Priority, Status, Channel, DeliveryStatus, FailureKind, …)
├── Localization/                       # NEW: LocalizationSetting.cs, PlatformLanguages.cs
└── Authorization/                      # CHANGED: AdminArea.Notifications + 2 catalogue permissions

src/AskLucy.Application/
├── Notifications/                      # NEW
│   ├── Abstractions/                   # INotificationPublisher, repositories, renderer, link builder,
│   │                                   # IAccountLinkIssuer, INotificationChannelSender, IEffectiveLanguageResolver, …
│   ├── Processing/                     # OutboxDispatchService, DeliveryProcessingService, LeaseSweepService,
│   │                                   # RetentionService (logic; hosted by Infrastructure workers)
│   ├── Commands/                       # MarkRead, MarkAllRead, Delete, UpdatePreferences, RetryDelivery,
│   │                                   # BulkRetryDeliveries, Create/UpdateTemplateDraft, PublishTemplateVersion,
│   │                                   # ArchiveTemplateVersion, SendTemplateTest, PublishSystemAnnouncement
│   └── Queries/                        # GetNotifications, GetUnreadCount, GetNotification, GetPreferences,
│                                       # GetStatistics, GetDeliveries, GetDelivery, GetTemplates, GetTemplate,
│                                       # GetTemplateVersion, PreviewTemplateVersion, GetAnnouncements,
│                                       # GetChannelHealth, GetNotificationAudit
├── Localization/                       # NEW: Get/UpdateLocalizationSettings, Get/SetMyLanguage,
│                                       # Messages.resx + Messages.ar.resx (server text on localized surfaces)
├── Authentication/Commands/…           # CHANGED: account handlers publish hub requests (R12)
├── Agents/Runtime/AgentExecutionOrchestrator.cs          # CHANGED: emit agent.* (+ approval)
├── Workflows/Runtime/WorkflowExecutionOrchestrator.cs    # CHANGED: emit workflow.* (+ approval)
└── Abstractions/IEmailSender.cs        # CHANGED: EmailMessage overload

src/AskLucy.Infrastructure/
├── Notifications/                      # NEW
│   ├── NotificationHub.cs  SignalRNotificationRealtimePublisher.cs
│   ├── Workers/ NotificationOutboxDispatcher.cs  NotificationDeliveryWorker.cs  (BackgroundService hosts)
│   ├── Jobs/ NotificationLeaseSweepJob.cs  NotificationRetentionJob.cs  (Hangfire recurring)
│   ├── Email/ EmailChannelSender.cs  EmailSendRateLimiter.cs  SmtpFailureClassifier.cs
│   ├── Templates/ LogicFreeTemplateRenderer.cs  NotificationTemplateSeeder.cs  ProtectedTerms.cs
│   │   └── Seed/{en,ar}/*.json         # embedded default template content
│   ├── NotificationLinkBuilder.cs  EffectiveLanguageResolver.cs  NotificationWakeSignal.cs
│   ├── NotificationMetrics.cs          # Meter "AskLucy.Notifications"
│   └── HealthChecks/ (dispatcher, delivery-worker, backlog, smtp)
├── Identity/AccountLinkIssuer.cs       # NEW: send-time token/link minting (R10)
├── Email/                              # CHANGED: SmtpEmailSender(EmailMessage), BrandedAccountEmailTemplateRenderer lang/dir;
│                                       # AccountEmailJob/PasswordEmailJob → one-release forwarding shims
├── Documents/ProcessingNotifier.cs     # CHANGED: NotifyAsync → publisher; drop notificationCreated push
└── Memory/MemoryNotifier.cs            # CHANGED: → publisher

src/AskLucy.Persistence/
├── Configurations/Notifications/       # NEW Fluent API configs (+ Localization, ApplicationUser column)
├── Repositories/Notification*Repository.cs  NotificationCursor.cs  NotificationOutboxRepository.cs
├── Interceptors/NotificationWakeInterceptor.cs  # pulses the wake signal after commit
└── Migrations/                         # 5 migrations (data-model.md § Migrations)

src/AskLucy.Web/
├── Controllers/v1/ NotificationsController.cs  NotificationPreferencesController.cs
│   UserLocalizationController.cs  AdminNotificationsController.cs  AdminNotificationTemplatesController.cs
│   AdminAnnouncementsController.cs  AdminLocalizationController.cs
├── Middleware/LocalizedSurfaceCultureMiddleware.cs  (+ [LocalizedSurface] metadata on Admin* controllers)
├── StartupTasks/LegacyNotificationImporter.cs      # idempotent one-shot import (R13)
└── Program.cs                          # CHANGED: rate-limit policies, MapHub, workers, health checks, recurring jobs

src/AskLucy.Web/ClientApp/src/
├── i18n/                               # NEW (contracts/localization-ui.md)
├── features/notifications/             # NEW: api/, hooks/ (useNotifications, useUnreadCount, useNotificationHub),
│                                       # components/ (NotificationBell, NotificationPopover, NotificationList,
│                                       # NotificationItem, NotificationDetails), pages/ (NotificationsPage)
├── features/settings/                  # CHANGED: Notifications tab (preferences) + LanguageSwitch
├── features/admin/                     # CHANGED: adminNav Notifications group; pages/AdminNotifications*.tsx
│                                       # (Dashboard, Deliveries, Templates, Announcements, Localization);
│                                       # every existing admin page/chart moved onto useT + LocalizedSurface
├── features/documents/, features/memory/   # CHANGED: legacy inbox UI + notificationCreated handlers removed
├── components/AppShell.tsx             # CHANGED: NotificationBell in header
└── routes/router.tsx                   # CHANGED: lazy /notifications and admin notification routes

tests/
├── AskLucy.Domain.Tests/Notifications/         # router, catalogue coverage, state machines, token parser
├── AskLucy.Application.Tests/Notifications/    # handlers, dispatch/delivery services with fakes, anti-enumeration
├── AskLucy.Infrastructure.Tests/Notifications/ # renderer escaping, SMTP classifier, rate limiter, protected terms
├── AskLucy.Persistence.Tests/Notifications/    # repositories, claim races, keyset paging, legacy import
└── AskLucy.Web.Tests/Notifications/            # endpoints, authz/ownership, fault injection (SC-003), stub channel (SC-012)
```

**Structure Decision**: This uses the existing five-project Clean Architecture solution and the in-app React SPA. There are no new projects. The hub lives in a `Notifications` folder in each layer, alongside the existing per-module folders such as `Documents`, `Memory` and `Workflows`. SignalR hubs live in Infrastructure, as `DocumentProcessingHub` already does.

## Delivery Slices

Every merge to main deploys, so each slice must leave production coherent (research R26). Slices 5 and 6 are dark until an administrator enables localization.

| # | Slice | Contents | Stories | Ships when |
|---|---|---|---|---|
| 1 | **Hub core and in-app** | Domain model and catalogue, outbox, dispatcher, NotificationHub, center, bell and popover, English in-app templates, all non-account emitters, **legacy document and memory import plus retirement** (these must ship atomically, FR-009a), and the **knowledge-base upload → indexing trigger** (FR-004b, R27) | US1, US2, part of US9 | first |
| 2 | **Email channel and account emails** | Delivery worker, SMTP sender extension, retry, dead letter, sweeper, rate limiter, English email templates, link issuer, account handlers moved onto the hub, legacy job shims | US3, rest of US9 | after 1 |
| 3 | **Preferences and approvals** | Preferences API and UI, mandatory locks, approval emit points and audit | US4, US5 | after 2 |
| 4 | **Admin operations** | Permissions, statistics, deliveries, retry and bulk retry, channel health, templates (draft, preview, test, publish, archive), announcements, audit, retention job, metrics and health checks | US6, US7, FR-004a | after 2 |
| 5 | **Localization foundation and notification screens** | Localization setting, user language, effective-language resolver, Arabic templates, `i18n/` layer, RTL `LocalizedSurface`, Arabic center, details, preferences and switch, `[LocalizedSurface]` server culture, do-not-translate list and tests | US8 (user side) | after 3 and 4 |
| 6 | **Admin-area Arabic** | Arabic catalogs and RTL for the admin shell and all 12 existing sections plus the 5 notification admin screens, with a11y and RTL tests per screen | US8 (admin side), FR-046a | independently, after 5 |

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| §7 i18n rule scope: introducing an i18n framework triggers "user-facing strings MUST NOT be hardcoded inline", but this feature moves only the notification screens and the admin area onto catalogs | The spec limits Arabic to those surfaces (Assumptions: Arabic scope). Retrofitting chat, the workspace, the studio, landing and auth pages is explicitly out of scope. | Converting the whole app now would multiply the scope and risk with no user-visible benefit, because those screens stay English. Mitigation: those surfaces already use centralized copy (§7's pre-i18n rule), which keeps extraction mechanical, and ADR 0019 records moving each surface onto `i18n/` catalogs when it is localized. |
| New cross-cutting infrastructure pattern: a transactional outbox plus in-process `BackgroundService` workers, alongside Hangfire | The durability required by FR-006, plus the latency in SC-001 and SC-014, can't be met by Hangfire enqueue (not transactional with EF) or Hangfire recurring jobs (1-minute granularity). | Hangfire-only misses the latency targets and keeps the lost-event window. A message broker is new infrastructure that 10k/hour doesn't justify. Recorded in ADR 0018. |
| §10 "performance tests fail the build": the throughput and latency tests (T224, T232, T233, T234) run only when `RUN_SCALE_PERFORMANCE_TESTS=1`, like the existing scale tests | CI runs against a shared hosted SQL Server whose IO latency varies by an order of magnitude between runs, so timings there measure the host, not the code. Ungated, the tests fail at random and get ignored. | Running them ungated on the shared host gives flaky, meaningless failures. Loosening the thresholds until they pass would hide real regressions. Mitigation: the tests exist and assert the real SC targets now; the gate is switched on at go-live on dedicated infrastructure, the same plan as the existing scale tests. |
