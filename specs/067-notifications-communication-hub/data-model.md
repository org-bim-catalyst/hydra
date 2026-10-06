# Data Model: Notifications & Communication Hub

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md) | **Date**: 2026-09-23

## Conventions

- **Base entity**: every persisted entity inherits `BaseEntity`, except the ones marked otherwise. That gives it:
  - `Guid Id` (v7, sequential).
  - The audit columns `CreatedAtUtc`, `CreatedBy`, `ModifiedAtUtc` and `ModifiedBy`, set by `AuditSaveChangesInterceptor`.
  - `DeletedAtUtc` and `DeletedBy` for soft delete, with a global query filter where it is used.
  - `RowVersion` as the concurrency token.
- **Mapping**: all mapping is Fluent API in `AskLucy.Persistence/Configurations/Notifications/`. Domain classes carry no attributes.
- **Enums**: stored as `nvarchar` strings (`HasConversion<string>()`), consistent with the API-wide string enums.
- **Foreign keys**: every FK is indexed. Composite indexes are justified inline.
- **Domain namespace**: `AskLucy.Domain.Notifications`, plus `AskLucy.Domain.Localization` for `LocalizationSetting`.

---

## Enumerations

| Enum | Values |
|---|---|
| `NotificationCategory` | `Security`, `Account`, `Agent`, `Workflow`, `Document`, `KnowledgeBase`, `Memory`, `System`, `Conversation`, `Billing` |
| `NotificationPriority` | `Low`, `Normal`, `High`, `Critical` |
| `NotificationStatus` | `Created`, `Queued`, `Processing`, `Sent`, `Delivered`, `Read`, `Failed`, `Cancelled`, `Expired` |
| `NotificationChannel` | `InApp`, `Email` (future values append: `Teams`, `Slack`, `WhatsApp`, `Sms`, `Push`, `Webhook`) |
| `DeliveryStatus` | `Pending`, `Sending`, `Retrying`, `Sent`, `Delivered`, `Skipped`, `Failed`, `DeadLettered`, `Cancelled`, `Expired` |
| `DeliveryFailureKind` | `Transient`, `Permanent`, `AmbiguousOutcome`, `RetryLimitReached`, `RecipientUnavailable`, `RequestExpired`, `RenderError` |
| `DeliverySkipReason` | `PreferenceDisabled`, `ChannelDisabled`, `NoVerifiedAddress`, `NotCritical` |
| `RecipientKind` | `User`, `Address`, `SupportMailbox` |
| `DeliveryFrequency` | `Immediate`, `DailyDigest`, `WeeklyDigest` (only `Immediate` offered, FR-033) |
| `TemplateVersionStatus` | `Draft`, `Published`, `Archived` |
| `OutboxEventStatus` | `Pending`, `Processing`, `Completed`, `Failed` |
| `AnnouncementKind` | `Maintenance`, `ServiceDegradation`, `ImportantAnnouncement` |
| `AnnouncementAudience` | `AllActiveUsers`, `Roles` |

---

## Entities

### Notification (aggregate root)

One message for one recipient about one event (FR-010).

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | PK |
| `RecipientUserId` | `string(450)?` | FK → `AspNetUsers.Id`. Null only for `SupportMailbox` and address-only account emails that have no user. |
| `Category` | `NotificationCategory` | from type definition |
| `Type` | `string(100)` | catalogue key, for example `workflow.execution.failed` |
| `Title` | `string(200)` | rendered in-app title (plain text) |
| `Message` | `string(2000)` | rendered in-app message (plain text; never HTML) |
| `Priority` | `NotificationPriority` | |
| `Status` | `NotificationStatus` | aggregated from deliveries (see state machine) |
| `Language` | `string(10)` | BCP-47 produced-in language (FR-044c) |
| `TemplateVersionId` | `Guid?` | FK → `NotificationTemplateVersions.Id`, the in-app version used (FR-039). Null for legacy imports. |
| `RelatedItemType` | `string(50)?` | for example `WorkflowExecution`, `Document`, `Memory` |
| `RelatedItemId` | `string(100)?` | |
| `ActionRoute` | `string(300)?` | app-relative route, built by `INotificationLinkBuilder` (FR-047) |
| `ActionLabel` | `string(60)?` | the template's `ActionLabel`, rendered in `Language` at materialization; null when there is no route |
| `MetadataJson` | `nvarchar(max)?` | non-sensitive display metadata only (FR-013); ≤ 4 KB enforced |
| `CorrelationId` | `string(100)` | from the originating request (FR-057) |
| `EventKey` | `string(200)?` | de-duplication identity (FR-008) |
| `SourceEventId` | `Guid?` | FK → `NotificationOutboxEvents.Id`, `ON DELETE SET NULL` |
| `ShowInCenter` | `bool` | false for email-only account types (FR-009c) |
| `ReadAtUtc` | `datetime2?` | |
| `ExpiresAtUtc` | `datetime2?` | |
| `DeletedAtUtc` / `DeletedBy` | (base) | owner deletion (FR-016a); global query filter hides the row |

**Indexes**:
- `IX_Notifications_Recipient_Center` on `(RecipientUserId, CreatedAtUtc DESC, Id DESC)` `INCLUDE (Category, ReadAtUtc, Priority, Status)`, filtered `DeletedAtUtc IS NULL AND ShowInCenter = 1`. Serves keyset paging (SC-004).
- `IX_Notifications_Recipient_Unread` on `(RecipientUserId)`, filtered `ReadAtUtc IS NULL AND DeletedAtUtc IS NULL AND ShowInCenter = 1`. Serves the unread count.
- `UX_Notifications_Recipient_EventKey` **unique** on `(RecipientUserId, EventKey)`, filtered `EventKey IS NOT NULL`. De-duplicates (FR-008) and makes replay idempotent. A recipient-less notification (support mailbox) uses the separate unique filtered index `UX_Notifications_Address_EventKey` on `(EventKey)` `WHERE RecipientUserId IS NULL`.
- `IX_Notifications_Retention` on `(ReadAtUtc)` and `(DeletedAtUtc)`, filtered not-null, for R20.
- Indexes on the FK columns `TemplateVersionId` and `SourceEventId`.

**Domain behavior**:
- `Create(...)`: validates lengths, and requires a recipient user when `ShowInCenter`.
- `MarkRead(now)`: idempotent.
- `DeleteByOwner(userId, now)`: rejects a non-owner.
- `RecalculateStatus()`: re-aggregates from deliveries.
- `Expire(now)`.

### NotificationDelivery (child of Notification)

One channel's delivery stream for one notification (FR-012, FR-028). The delivery `Id` **is** the unique delivery identity, and it is used in the email `Message-ID`.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | PK = delivery identity |
| `NotificationId` | `Guid` | FK → `Notifications.Id`, cascade |
| `Channel` | `NotificationChannel` | |
| `Status` | `DeliveryStatus` | see state machine |
| `Priority` | `NotificationPriority` | denormalized for queue ordering |
| `RecipientKind` | `RecipientKind` | |
| `RecipientAddress` | `string(320)?` | explicit address for `Address` kind only. Never set for `SupportMailbox` (FR-009c). Masked in admin DTOs. |
| `Language` | `string(10)?` | language actually rendered (FR-044c); set at send |
| `TemplateVersionId` | `Guid?` | FK, the version actually rendered for this channel |
| `AttemptCount` | `int` | |
| `MaxAttempts` | `int` | from the retry policy for the priority |
| `NextAttemptAtUtc` | `datetime2?` | |
| `LastAttemptAtUtc` | `datetime2?` | |
| `LeaseOwner` | `string(200)?` | claim owner (R4) |
| `LeaseExpiresAtUtc` | `datetime2?` | |
| `SkipReason` | `DeliverySkipReason?` | when `Skipped` |
| `FailureKind` | `DeliveryFailureKind?` | |
| `FailureReason` | `string(500)?` | safe, human-readable; never contains tokens, links or credentials |
| `ProviderResponse` | `string(500)?` | sanitized SMTP status line only (FR-056) |
| `SentAtUtc` / `DeliveredAtUtc` | `datetime2?` | |
| `ExpiresAtUtc` | `datetime2?` | request validity (R10) or announcement end (R22) |
| `CorrelationId` | `string(100)` | |

**Indexes**:
- `IX_NotificationDeliveries_Queue` on `(Status, Priority DESC, NextAttemptAtUtc)` `INCLUDE (Channel, LeaseExpiresAtUtc)`, filtered `Status IN ('Pending','Retrying','Sending')`. The worker's claim scan (R4) uses it; the sweeper uses it for `Sending` rows.
- `IX_NotificationDeliveries_Failed` on `(Status, LastAttemptAtUtc DESC)`, filtered `Status IN ('Failed','DeadLettered')`. Serves the admin failed view.
- An index on `NotificationId`.
- `UX_NotificationDeliveries_Notification_Channel` **unique** on `(NotificationId, Channel)`: a notification has one delivery per channel.

**Domain behavior**: `Claim`, `MarkSending`, `MarkSent`, `MarkDelivered`, `ScheduleRetry(failure, policy, now)`, `Fail(kind, reason)`, `DeadLetter`, `Skip(reason)`, `Cancel`, `Expire`, and `AdminRetry(now)`. `AdminRetry` is allowed only from `Failed` or `DeadLettered`, and only if the notification is neither deleted nor expired (spec edge case).

### NotificationOutboxEvent

A durable "something happened" record, written in the emitter's own transaction (FR-006; research R2). This is the spec's *Notification Event*.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | PK |
| `Type` | `string(100)` | catalogue key |
| `EventKey` | `string(200)?` | **not** unique here, because de-duplication happens at materialization and a duplicate must never roll back the emitter's transaction |
| `RecipientJson` | `nvarchar(max)` | `NotificationRecipient` spec: user ids, `{ roles: [...] }` / `{ all: true }` (announcements), explicit address, or support mailbox. It contains no display data. |
| `VariablesJson` | `nvarchar(max)` | declared variables only; validated against the type's allow-list at publish; ≤ 16 KB |
| `RelatedItemType` / `RelatedItemId` | | |
| `ExplicitLanguage` | `string(10)?` | FR-044 first candidate |
| `CorrelationId` | `string(100)` | |
| `OccurredAtUtc` | `datetime2` | |
| `Status` | `OutboxEventStatus` | |
| `Attempts` | `int` | dispatcher attempts |
| `LeaseOwner` / `LeaseExpiresAtUtc` | | claim (R4) |
| `NextAttemptAtUtc` | `datetime2?` | dispatcher retry backoff |
| `FanOutCursor` | `string(450)?` | last user id processed in batched fan-out (R22) |
| `Outcome` | `string(50)?` | `Materialized`, `NoRecipient`, `Duplicate` or `Rejected` |
| `LastError` | `string(1000)?` | safe summary; the full exception goes to the log with the correlation id |
| `ProcessedAtUtc` | `datetime2?` | |

**Indexes**:
- `IX_NotificationOutboxEvents_Due` on `(Status, NextAttemptAtUtc, OccurredAtUtc)`, filtered `Status IN ('Pending','Processing')`.
- `IX_NotificationOutboxEvents_Processed` on `(ProcessedAtUtc)`, filtered `Status = 'Completed'`, for retention.

It does not inherit the soft-delete filter. `Completed` rows are purged by retention after 7 days. `Failed` rows are kept for 30 days and shown in the admin backlog view.

### NotificationTemplate

One row per `(Type, Channel, Language)` (FR-038).

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | PK |
| `Type` | `string(100)` | must exist in `NotificationTypeCatalog` |
| `Channel` | `NotificationChannel` | must be a supported channel of the type |
| `Language` | `string(10)` | must be a content language the platform ships (`en`, `ar`) |
| `Name` | `string(150)` | display name |
| `Category` | `NotificationCategory` | copied from the type (read-only) |
| `PublishedVersionId` | `Guid?` | FK → current published version (nullable to break the cycle; set on publish) |

**Index**: `UX_NotificationTemplates_Type_Channel_Language` is **unique**.

### NotificationTemplateVersion

Immutable once published (FR-039).

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | PK |
| `TemplateId` | `Guid` | FK, cascade |
| `VersionNumber` | `int` | monotonically increasing per template |
| `Status` | `TemplateVersionStatus` | |
| `Subject` | `string(200)?` | email only; required for email |
| `Preheader` | `string(200)?` | email only |
| `Greeting` | `string(200)?` | email only |
| `Heading` | `string(200)?` | email only; required for email |
| `BodyParagraphsJson` | `nvarchar(max)?` | email only; a JSON array of 1–10 paragraphs, each ≤ 1,000 characters |
| `SafetyNote` | `string(500)?` | email only; required for email |
| `FooterNote` | `string(500)?` | email only |
| `Title` | `string(200)?` | in-app only; required for in-app |
| `Message` | `string(1000)?` | in-app only; required for in-app |
| `ActionLabel` | `string(60)?` | both channels |
| `UsedVariablesJson` | `nvarchar(max)` | variables referenced, computed on save and checked ⊆ declared (FR-041) |
| `PublishedAtUtc` / `PublishedBy` | | |
| `ArchivedAtUtc` / `ArchivedBy` | | |

**Indexes**:
- `UX_NotificationTemplateVersions_Template_Version` **unique** on `(TemplateId, VersionNumber)`.
- `UX_NotificationTemplateVersions_OnePublished` **unique** on `(TemplateId)`, filtered `Status = 'Published'`.

**Validation**, in FluentValidation for the commands and in Domain guards:
- The token syntax is exactly `{{ name }}`, where the name matches `^[a-zA-Z][a-zA-Z0-9_]{0,49}$`.
- Any other `{{` or `}}` sequence is rejected.
- No raw URLs are allowed in text fields. `https?://` and `www.` are rejected; links come only from `ActionLabel` and the type's route (FR-047).
- No HTML is allowed. `<` followed by a letter or `/` is rejected, which keeps the fields structured text.

**Behavior**:
- Only `Draft` versions are editable.
- `Publish()` archives the previous published version and sets `Template.PublishedVersionId`.
- `Archive()` is allowed from Draft or Published. Archiving the only published version of a *shipped default* is rejected, because SC-006 requires a published template for every emitted type.

### NotificationPreference

Sparse per-user overrides; defaults come from the catalogue (FR-031–FR-034).

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserId` | `string(450)` | FK → `AspNetUsers.Id`, cascade |
| `Category` | `NotificationCategory` | not `Billing` or `Conversation` until they are emitted |
| `Channel` | `NotificationChannel` | |
| `IsEnabled` | `bool` | |
| `Frequency` | `DeliveryFrequency` | `Immediate` only (validator) |

**Index**: `UX_NotificationPreferences_User_Category_Channel` is **unique**. Attempts to disable a mandatory `(category, channel)` pair are rejected by the Domain and the validator, and return 422 (FR-032). The API returns a preference as **effective** state: the default merged with the override, with `isLocked` set on mandatory pairs.

### SystemAnnouncement (aggregate root)

Defined by FR-004a.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | PK |
| `Kind` | `AnnouncementKind` | |
| `Title` | `string(150)` | admin-entered; never translated (FR-046b) |
| `Message` | `string(2000)` | admin-entered plain text |
| `Audience` | `AnnouncementAudience` | |
| `TargetRoleIdsJson` | `nvarchar(max)?` | required when `Audience = Roles`; the role ids must exist |
| `IsCritical` | `bool` | enables email (FR-004a) |
| `EndsAtUtc` | `datetime2?` | must be in the future when set; drives `ExpiresAtUtc` of fan-out deliveries |
| `PublishedAtUtc` | `datetime2` | |
| `PublishedByUserId` | `string(450)` | FK |
| `RecipientCount` | `int?` | set when fan-out completes |

**Index**: on `(PublishedAtUtc DESC)` for the admin list, and on the FK.

Published announcements are immutable: there is no edit or delete, and a correction is a new announcement.

### NotificationAuditLog

This is append-only (FR-037, FR-054, SC-008) and mirrors `RoleAuditLog`. It is **not** a `BaseEntity` subclass: it has no soft delete and no modification columns.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | PK |
| `OccurredAtUtc` | `datetime2` | |
| `ActorUserId` | `string(450)?` | null for system-generated approval-history rows |
| `Action` | `string(60)` | for example `TemplateVersionPublished`, `DeliveryRetried`, `DeliveriesBulkRetried`, `AnnouncementPublished`, `LocalizationSettingChanged`, `TemplateTestSent`, `ApprovalNotificationCreated`, `ApprovalNotificationDelivered`, `ApprovalNotificationRead`, and the admin-view actions `StatisticsViewed`, `ChannelsViewed`, `DeliveriesViewed`, `DeliveryViewed`, `AuditViewed`, `TemplatesViewed`, `TemplateViewed`, `TemplateVersionViewed` (T230) |
| `TargetType` | `string(60)` | |
| `TargetId` | `string(100)` | |
| `Outcome` | `string(20)` | `Succeeded` / `Rejected` / `Failed` |
| `DetailsJson` | `nvarchar(max)?` | safe before/after summary; never token, credential or rendered-email content |
| `CorrelationId` | `string(100)` | |

**Indexes**: `(OccurredAtUtc DESC)` and `(TargetType, TargetId)`. Retention never deletes these rows.

### LocalizationSetting (singleton)

Defined by FR-044a, in `AskLucy.Domain.Localization`.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | fixed well-known id; seeded disabled by the migration |
| `IsEnabled` | `bool` | default `false` |
| `SupportedLanguagesJson` | `nvarchar(200)` | e.g. `["en","ar"]`; must contain `en`; ⊆ platform content languages |

It uses `RowVersion` for optimistic concurrency. A conflict returns 409, and the admin UI reloads.

### ApplicationUser (existing, changed)

| Field | Type | Rules |
|---|---|---|
| `PreferredLanguage` | `nvarchar(10)?` | **new**. Null means none chosen. Must be a platform content language when set. It is kept even when localization is disabled or the language is unsupported (FR-044b). |

### Code-owned (not persisted)

- **`NotificationTypeDefinition` / `NotificationTypeCatalog`** (Domain): the catalogue in the next section.
- **Channel registry**: each `INotificationChannelSender` registered in DI is one channel. Its enabled state comes from configuration (`Notifications:Channels:{Channel}:Enabled`), and its health comes from the R21 health checks. This is the spec's *Notification Channel*. It has no table, because channels come with code and administrators only inspect them (FR-055).

---

## Notification type catalogue

The definitions below are code-owned in `NotificationTypeCatalog`. Routes were reconciled against `ClientApp/src/routes/router.tsx` (research R11 addendum): `{id}` is `RelatedItem.Id` and `{parentId}` is `RelatedItem.ParentId`.

**Column key**:
- **In-app / Email**: the default state. **on** or **off** means optional, following the user preference; **M** means mandatory and can't be disabled; **—** means the channel isn't used.
- **Link**: marks a type that uses `SensitiveLinkKind`.

**Standard variables** are available to every type: `recipientDisplayName`, `appName`, `actionUrl` (built, never supplied) and `occurredAt`.

| Type key | Category | Priority | In-app | Email | Extra declared variables | Route |
|---|---|---|---|---|---|---|
| `agent.execution.started` | Agent | Low | on | off | `agentName` | `/agents/{parentId}/executions/{id}` |
| `agent.execution.completed` | Agent | Normal | on | off | `agentName`, `duration` | same |
| `agent.execution.failed` | Agent | High | on | on | `agentName`, `failureSummary` | same |
| `agent.approval.requested` | Agent | High | on | on | `agentName`, `intendedAction`, `approvalId` | `/agents/{parentId}/executions/{id}?approval={approvalId}` (the execution page opens that approval) |
| `workflow.execution.started` | Workflow | Low | on | off | `workflowName` | `/workflows/{parentId}/executions/{id}` |
| `workflow.execution.completed` | Workflow | Normal | on | off | `workflowName`, `duration` | same |
| `workflow.execution.failed` | Workflow | High | on | on | `workflowName`, `failureSummary` | same |
| `workflow.execution.paused` | Workflow | Normal | on | off | `workflowName` | same |
| `workflow.approval.requested` | Workflow | High | on | on | `workflowName`, `nodeName`, `intendedAction`, `approvalId` | `/workflows/{parentId}/executions/{id}?approval={approvalId}` (the execution page opens that approval) |
| `document.upload.completed` | Document | Normal | on | off | `documentName` | `/documents?documentId={id}` |
| `document.processing.completed` | Document | Normal | on | off | `documentName` | same |
| `document.processing.failed` | Document | High | on | on | `documentName`, `failureSummary` | same |
| `document.ocr.completed` | Document | Normal | on | off | `documentName` | same |
| `document.ocr.failed` | Document | High | on | on | `documentName`, `failureSummary` | same |
| `document.version.created` | Document | Normal | on | off | `documentName`, `versionNumber` | same |
| `document.storage.limit-reached` | Document | High | on | on | `usedStorage`, `storageLimit` | `/documents` |
| `document.indexing.completed` | Document | Normal | on | off | `documentName` | same |
| `document.indexing.failed` | Document | High | on | on | `documentName`, `failureSummary` | same |
| `knowledge-base.indexing.completed` | KnowledgeBase | Normal | on | off | `knowledgeBaseName` | `/knowledge-bases/{id}` |
| `knowledge-base.indexing.failed` | KnowledgeBase | High | on | on | `knowledgeBaseName`, `failureSummary` | same |
| `knowledge-base.updated` | KnowledgeBase | Low | on | off | `knowledgeBaseName`, `changeSummary` | same |
| `memory.auto-created` | Memory | Low | on | off | `memorySummary` | `/memory?memoryId={id}` |
| `memory.auto-approved` | Memory | Low | on | off | `memorySummary` | same |
| `memory.conflict.confirmation-needed` | Memory | Normal | on | off | `memorySummary` | `/memory?memoryId={id}` |
| `account.email-confirmation.requested` | Account | Critical | — | **M** | Link: `EmailConfirmation` | — |
| `account.email-change.requested` | Account | Critical | — | **M** | Link: `EmailChange`, `newEmailMasked` | — |
| `account.password-reset.requested` | Account | Critical | — | **M** | Link: `PasswordReset` | — |
| `account.support-request.submitted` | Account | High | — | **M** (support mailbox) | `requesterEmail`, `requestKind`, `messageBody` | — |
| `security.password-changed` | Security | Critical | — | **M** | `changedAt`, `ipAddress` | — |
| `security.two-factor.enabled` | Security | Critical | **M** | **M** | `changedAt` | `/settings?tab=security` |
| `security.two-factor.disabled` | Security | Critical | **M** | **M** | `changedAt` | same |
| `security.recovery-codes.regenerated` | Security | Critical | **M** | **M** | `changedAt` | same |
| `system.announcement.published` | System | Normal, or High when critical | **M** (FR-004a: every announcement reaches its audience in-app) | on, sent only if `IsCritical` (FR-004a) | `announcementTitle`, `announcementMessage`, `announcementKind`, `endsAt` | `/notifications/{notificationId}` (the materialized notification's own id, filled by the link builder) |
| `template.test` | System | Normal | — | **M** (the calling admin's own verified address only) | the template's own variables, sample values | — |
| `conversation.export.completed` | Conversation | Normal | defined, **not emitted** (FR-005) | | | |
| `billing.payment.failed`, `billing.subscription.renewed` | Billing | High / Normal | defined, **not emitted** (FR-005) | | | |

Rules encoded in the catalogue:
- Security-category types set `MinimizeSensitiveContent = true` (FR-025). Their email says only what happened, when, and to sign in and review. It gives no device or location detail beyond the variables listed.
- Account types have `ShowInCenter = false` (FR-009c).
- `account.email-confirmation.requested` covers both the first confirmation and the resend (FR-004).
- `account.support-request.submitted` interpolates `messageBody` into the email to the support mailbox, HTML-encoded, which matches the current behavior. It is never shown to administrators in delivery views.
- Every emitted type ships published `en` and `ar` templates on each channel it uses (SC-006). A catalogue unit test enforces this against the seed files.

---

## State machines

### Notification.Status (FR-011)

```text
Created ──► Queued ──► Processing ──► Sent ──► Delivered ──► Read
   │          │            │            │          │
   └──────────┴────────────┴────────────┴──────────┴──► Failed | Cancelled | Expired   (terminal)
```

The status is aggregated from the deliveries:
- `Delivered` when the in-app delivery is `Delivered`, or when any delivery reaches `Delivered`.
- `Sent` when the best outcome is an email hand-off.
- `Read` is set only by the owner, and only from `Delivered`, `Sent` or a later state. It never regresses.
- `Failed`, `Cancelled` or `Expired` apply only when *every* non-skipped delivery ends that way.

Invalid transitions throw `DomainRuleViolationException`, which the API maps to 409.

### NotificationDelivery.Status

```text
Pending ──claim──► Sending ──accepted──► Sent
   │                  │   └─transient fail─► Retrying ──due+claim──► Sending
   │                  │   └─permanent fail─► Failed (Permanent)
   │                  │   └─attempts exhausted─► DeadLettered (RetryLimitReached)
   │                  └─lease expired (sweeper)─► Failed (AmbiguousOutcome)   [no auto-resend, R5]
   ├──► Skipped (preference/channel/no address)          [at materialization]
   ├──► Cancelled (recipient deleted)
   └──► Expired (ExpiresAtUtc passed / RequestValidity exceeded)

Failed | DeadLettered ──admin retry (Manage, audited)──► Pending   [refused if notification deleted/expired]
InApp: created directly as Delivered (R8).
```

### NotificationTemplateVersion.Status

```text
Draft ──publish──► Published ──(newer version published)──► Archived
  │                    └──archive (not last published of a default)──► Archived
  └──archive──► Archived
```

### NotificationOutboxEvent.Status

```text
Pending ──claim──► Processing ──► Completed (Materialized | NoRecipient | Duplicate)
                       │  └─fan-out batch done, more remain─► Pending (FanOutCursor advanced)
                       └─error─► Pending (backoff) … after 10 attempts ─► Failed (admin-visible)
```

---

## Relationships

```text
AspNetUsers 1 ──< Notification >── 0..1 NotificationTemplateVersion
Notification 1 ──< NotificationDelivery >── 0..1 NotificationTemplateVersion
NotificationOutboxEvent 1 ──< Notification            (SourceEventId, SET NULL on purge)
NotificationTemplate 1 ──< NotificationTemplateVersion; Template ──> 0..1 published Version
AspNetUsers 1 ──< NotificationPreference
AspNetUsers 1 ──< SystemAnnouncement (publisher)
SystemAnnouncement 1 ── 1 NotificationOutboxEvent ──< Notification (fan-out, EventKey = announcement:{id})
```

When a user is deleted:
- Pending deliveries are cancelled by the dispatcher and worker, which check that the recipient is active.
- The hard-erasure path cascades `Notification`, `NotificationDelivery` and `NotificationPreference`.
- `NotificationAuditLog` keeps its `ActorUserId` string, which is not a foreign key, so audit survives.

---

<a id="legacy-mapping"></a>
## Legacy mapping (FR-009a, research R13)

| Legacy source | Legacy event | Hub type | Route |
|---|---|---|---|
| `DocumentNotifications` | `UploadCompleted` | `document.upload.completed` | `/documents?documentId={DocumentId}` (or none if null) |
| | `ProcessingCompleted` | `document.processing.completed` | same |
| | `ProcessingFailed` | `document.processing.failed` | same |
| | `OcrFailed` | `document.ocr.failed` | same |
| | `VersionCreated` | `document.version.created` | same |
| | `StorageLimitReached` | `document.storage.limit-reached` | `/documents` |
| `MemoryNotifications` | `AutoCreated` | `memory.auto-created` | `/memory?memoryId={MemoryId}` |
| | `AutoApproved` | `memory.auto-approved` | same |
| | `ConflictNeedsConfirmation` | `memory.conflict.confirmation-needed` | `/memory?memoryId={MemoryId}` |

Each imported row is set as follows:

| Target field | Imported value |
|---|---|
| `RecipientUserId` | `UserId` |
| `Message` | legacy `Message` |
| `Title` | the type's English in-app template title, as rendered at import |
| `Language` | `en` |
| `CreatedAtUtc` | legacy `CreatedAtUtc` |
| `ReadAtUtc` | memory rows: `ReadAtUtc`. Document rows: `IsRead ? (ModifiedAtUtc ?? CreatedAtUtc) : null`. |
| `EventKey` | `legacy:document:{Id}` or `legacy:memory:{Id}` |
| `ShowInCenter` | `1` |
| `Priority` | the type default |
| `CorrelationId` | `legacy-import` |
| `TemplateVersionId` | `NULL` |

Each row also gets one `InApp` delivery with status `Delivered`. The legacy rows themselves are not modified.

---

## Migrations (one logical change each; all reversible)

1. `AddNotificationHubCore`: `Notifications`, `NotificationDeliveries` and `NotificationOutboxEvents`, with their indexes.
2. `AddNotificationTemplates`: `NotificationTemplates` and `NotificationTemplateVersions`. Content is seeded by the startup seeder, not by the migration (R9).
3. `AddNotificationPreferences`.
4. `AddSystemAnnouncementsAndNotificationAudit`.
5. `AddLocalizationSettingAndUserPreferredLanguage`: seeds the singleton row with localization disabled and `["en"]`.
6. *(follow-up release, not this feature)* `DropLegacyDocumentAndMemoryNotifications`: step 2 of the two-step retirement (§5).

The constitution's migration gotchas apply: no UTF-8 BOM in migration files, and `System` usings first.
