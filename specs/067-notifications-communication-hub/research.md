# Research: Notifications & Communication Hub

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-09-23

Phase 0 of `/speckit-plan`. Each topic records the decision, why it was chosen, and what else was considered. The Technical Context in the plan has no remaining `NEEDS CLARIFICATION`; everything is resolved below.

---

## R1. Real-time transport (FR-018)

**Decision**: Add one new SignalR hub, `NotificationHub`, at `/hubs/notifications`. It is `[Authorize]`, and on connect it joins the caller to the group `user:{userId}`, matching `DocumentProcessingHub`. The hub is server-to-client only (`notificationCreated`, `notificationUpdated`, `unreadCountChanged`). Every write goes through the REST API. Token delivery uses the cookie-delivered access token that the other six hubs moved to (the frozen-token fix, 1205b21).

`DocumentProcessingHub` keeps its stage and progress pushes. Those are live progress, not notifications. Only its `notificationCreated` event and the memory `notificationCreated` event are removed.

**Rationale**: FR-018 forbids a second real-time mechanism. SignalR is already the platform's push channel, with group-per-user conventions, auth and reconnect handling. One hub per concern matches the existing hubs.

**Alternatives considered**:
- SSE, as the source spec suggested. Rejected because it is a second real-time mechanism, forbidden by FR-018.
- Pushing notifications over each existing domain hub. Rejected because it would duplicate the unread-count logic across hubs, and the client would need to join every hub just to keep the bell correct.
- Polling only. Rejected because it can't meet SC-001 (5 s p95) without aggressive polling.

## R2. Durable capture of notification requests (FR-006, FR-007)

**Decision**: A **transactional outbox**. Each module calls `INotificationPublisher.Publish(NotificationRequest)` (Application abstraction). The Persistence implementation *adds* a `NotificationOutboxEvent` entity to the scoped `AskLucyDbContext` and does not save. The row commits atomically with the caller's own state change on the caller's existing `IUnitOfWork.SaveChangesAsync`. The publisher never performs I/O, so it cannot fail or slow the originating operation (FR-007). A new cross-cutting pattern, so it needs **ADR 0016**.

**Rationale**:
- Today's pattern is `SaveChangesAsync` followed by a separate `IBackgroundJobClient.Enqueue`. Hangfire's SQL storage is not enlisted in the EF transaction, so a crash between the two loses the notification. An enqueue that happens before a rollback notifies about a change that never happened.
- Constitution §5 prescribes "domain events or an outbox, not multiple partial commits". §3 requires reactions to state transitions to be dispatched *after* commit, never as in-process side effects.
- An outbox row satisfies both with no new dependency.

**Alternatives considered**:
- Direct Hangfire enqueue, the status quo. Rejected: not atomic with the state change (above).
- In-memory domain events dispatched after `SaveChanges`. Rejected: the platform has no dispatcher yet, and a crash after commit but before dispatch still loses the event.
- Hangfire enqueue inside a shared `TransactionScope`. Rejected: fragile, since it requires Hangfire storage on the same connection and ambient-transaction support. It also couples every emitter to Hangfire.

## R3. Dispatch and delivery processing (FR-007, FR-026–FR-030, SC-001, SC-002, SC-005)

**Decision**: Two `BackgroundService`s in Infrastructure, each running in its own DI scope per iteration:

1. **`NotificationOutboxDispatcher`**:
   - Claims pending outbox events and resolves recipients.
   - Applies routing (R7), then materializes `Notification` and `NotificationDelivery` rows in one `SaveChanges`.
   - After the commit, pushes in-app notifications over SignalR.
2. **`NotificationDeliveryWorker`**: claims due, non-in-app deliveries such as email, then renders, sends and records the outcome.

Both poll every 1 s when busy, backing off to 5 s when idle. A committed outbox row wakes the dispatcher immediately through `INotificationWakeSignal`, a process-local `SemaphoreSlim` that a `SaveChanges` interceptor pulses. A committed delivery wakes the worker the same way, so latency is milliseconds on the same instance and at most one poll interval across instances.

Hangfire stays in use for **recurring maintenance only**: retention cleanup (R19) and the lease sweeper (R5).

**Rationale**:
- Hangfire recurring jobs are cron-based with 1-minute granularity, which can't meet SC-001.
- Per-event fire-and-forget jobs would reintroduce the non-transactional enqueue that R2 removes.
- A polling `BackgroundService` over the outbox is the standard outbox relay. It has the same process lifetime as the Hangfire server that already runs in the web host.

**Alternatives considered**:
- Hangfire-only, with a recurring job every minute. Rejected: it misses SC-001 and SC-014.
- A message broker (Service Bus or RabbitMQ). Rejected: new infrastructure, and not justified at 10k/hour (§17).
- Delivering synchronously from the request. Rejected: violates FR-007.

## R4. Claiming, leases and multi-worker safety (FR-028, FR-029)

**Decision**: Claim each candidate row with a conditional `ExecuteUpdateAsync`. The candidate is either an outbox event or a delivery. The update is:

`UPDATE … SET LeaseOwner=@worker, LeaseExpiresAtUtc=@now+lease, Status=Processing|Sending WHERE Id=@id AND Status=Pending|Retrying AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc < @now)`

An affected-row count of 1 means the row is claimed. Candidates are read with a keyset query over the covering index `(Status, NextAttemptAtUtc, Priority)`. The worker id is `{machine}:{pid}:{guid}`. The lease is 2 minutes, well above the 60 s SMTP timeout (R6).

**Rationale**:
- Two workers can never both win the conditional update, which satisfies FR-029.
- It uses the existing EF/`ExecuteUpdateAsync` idiom the codebase prefers for monotonic bookkeeping writes, with no raw SQL.
- At 10k/hour (about 3 per second) the per-row claim cost is negligible.

**Alternatives considered**:
- `UPDATE TOP(n) … WITH (READPAST, UPDLOCK) OUTPUT` in raw SQL. More efficient at very high volume, but not needed, and it is SQL Server-specific raw SQL in the repository.
- `sp_getapplock` single-worker locking. Rejected: it blocks scale-out.

## R5. Exactly-once is impossible: prefer at-most-once for email (FR-028, SC-003, edge case "clock skew / worker crash")

**Decision**: Each email follows this sequence:
1. The claim sets `Status=Sending` and increments `AttemptCount`, committed **before** the SMTP call.
2. On a server acceptance response, it sets `Sent` and `SentAtUtc`.
3. On a classified failure, it sets `Retrying` with `NextAttemptAtUtc`, or `Failed` or `DeadLettered`.

A delivery still in `Sending` when its lease expires is in an unknown state: the crash happened mid-SMTP. The Hangfire **lease sweeper**, running every minute, marks it `Failed` with `FailureKind=AmbiguousOutcome` and does **not** resend it automatically. Administrators see it in the failed-deliveries view and can retry it deliberately (FR-030).

Every email carries a deterministic `Message-ID: <{deliveryId}@{configured-domain}>`, so receiving systems can collapse a duplicate if an admin retry follows a delivery that did in fact arrive.

The in-app channel has no ambiguity. Its delivery row is created as `Delivered` in the same commit as the notification (R8).

**Rationale**:
- The spec explicitly prefers at-most-once over duplicates where exactly-once is impossible, and the ambiguity must be recorded for administrators.
- The ambiguous window is only the SMTP round-trip, so under SC-003 fault injection an ambiguous delivery is *visible and recoverable*, not lost.

**Alternatives considered**:
- At-least-once with automatic resend. Rejected: it produces duplicate emails and violates SC-003.
- A two-phase handshake with the mail server. Not available over SMTP.

## R6. SMTP transport, failure classification and throughput (FR-021, FR-026, FR-027, Risks: mail-service limits and shared-host timeouts)

**Decision**:
- **Transport**: Extend the existing `IEmailSender` (MailKit `SmtpEmailSender`, `SecureSocketOptions.StartTls`, certificate validation on) with an `EmailMessage` overload. The message carries `To`, `Subject`, `HtmlBody`, `TextBody`, `ReplyTo?`, `MessageId`, and `Attachments?` (streamed). The current 4-argument method stays as a thin adapter until its last caller is migrated.
- **Connection reuse**: The worker sends a claimed batch over one authenticated connection instead of connecting per message.
- **Timeouts**: The SMTP timeout is **60 s**, configurable. The site4now short-timeout history is the reason for the long default.
- **Failure classification**:
  - Permanent, no retry: SMTP 5xx replies, and `550`/`553` on the recipient.
  - Transient: 4xx replies, connection, TLS-handshake and timeout errors.
  - Authentication failure: transient for the delivery, and it raises a channel-health alert.
- **Retry policy**: Configured per priority in `Notifications:Retry`. The default is 5 attempts at 1, 4, 10, 20 and 30 min, about 65 min in total. Critical priority uses 30 s, 1, 2, 5 and 10 min.
- **Rate limiting**: A token-bucket send limiter, `Notifications:Email:MaxPerMinute`, defaults to 60 and must be confirmed against the myasp.net plan before release. Mandatory account and security emails draw from a **reserved lane** (`ReservedPerMinuteForMandatory`, default 20) that bulk traffic can't consume, so a critical-announcement fan-out never delays a password reset (SC-014).

**Rationale**:
- FR-023 requires the provider to stay replaceable, and extending the existing abstraction follows the spec's assumption: "extended rather than duplicated".
- The reserved lane is the simplest way to keep account emails prompt under bulk load.

**Alternatives considered**:
- A new SMTP client library. Rejected: MailKit already covers STARTTLS and streaming attachments.
- A third-party transactional-email API. Out of scope. The architecture allows it later as another `IEmailSender`.

## R7. Routing and the notification type catalogue (FR-002, FR-003, FR-031–FR-034, FR-060)

**Decision**: The **notification type catalogue** is code in Domain: `NotificationTypeCatalog`, the same style as `AdminPermissionCatalog`. Each `NotificationTypeDefinition` declares:
- `Key` (for example `workflow.execution.failed`) and `Category`.
- `DefaultPriority` and `SupportedChannels`.
- `DefaultChannelState` per channel.
- `IsMandatory`: mandatory channels can't be disabled.
- `IsEmailOnly`: account emails, which have no in-app copy (FR-009c).
- `MinimizeSensitiveContent` (FR-025).
- `DeclaredVariables`: the allow-list for FR-041.
- `SensitiveLinkKind?` (R10).
- `RequestValidity?`: how long a queued request stays sendable.

A pure Domain `NotificationRouter` receives the definition, the recipient's sparse preference overrides, the effective language (R14) and channel availability. It returns the channel plan: which deliveries to create, and the skip reason for each channel it leaves out.

**Rationale**:
- Types are emitted by code. Their channel rules, mandatory flags and variable contracts belong with the code that emits them, and can be unit-tested without a database.
- Templates, the only part administrators edit, live in the database (R9).
- A pure router makes FR-003's decision table exhaustively testable.
- Adding a channel means adding a `NotificationChannel` enum value and an `INotificationChannelSender` implementation. The router and emitters don't change (FR-060, SC-012).

**Alternatives considered**:
- A database-driven type registry. Rejected: types without emitting code are meaningless, and it adds admin surface the spec doesn't ask for.
- Routing in each emitter. Violates FR-001 and FR-002.

## R8. Notification and delivery status model (FR-010–FR-012, FR-016a)

**Decision**: `Notification.Status` follows FR-011's lifecycle. It is **aggregated** from its deliveries:
- `Created` on materialization.
- `Queued` while any delivery is pending.
- `Sent` or `Delivered` from the best channel outcome.
- `Read` when the owner reads it.
- `Failed`, `Cancelled` or `Expired` only when every delivery ends that way.

`NotificationDelivery.Status` is independent, per FR-012. Its values are `Pending`, `Sending`, `Retrying`, `Sent`, `Delivered`, `Skipped`, `Failed`, `DeadLettered`, `Cancelled` and `Expired`.

Transitions are guarded by Domain methods that throw `DomainRuleViolationException` on invalid moves.

The in-app delivery is `Delivered` at materialization, because the notification center *is* the in-app surface. The SignalR push is best-effort latency, and a client that reconnects refetches.

Owner deletion (FR-016a) uses `BaseEntity.DeletedAtUtc` with the standard global query filter, so the notification is hidden from every user-facing query at once. Delivery rows and audit are untouched.

**Rationale**: This keeps the spec's single lifecycle vocabulary while honoring per-channel independence. It also reuses the platform's soft-delete convention (§5) rather than a bespoke flag.

**Alternatives considered**:
- Treating the SignalR push as the in-app "delivery". Rejected: offline users would count as failures, which contradicts the edge case "offline … nothing is lost".

## R9. Templates: storage, versioning, format and rendering (FR-038–FR-043, FR-009b, FR-022, FR-050)

**Decision**:
- **Storage**:
  - `NotificationTemplate` holds one row per `(TypeKey, Channel, Language)`.
  - `NotificationTemplateVersion` holds immutable-once-published revisions.
  - Publishing a version archives the previously published one. A filtered unique index guarantees at most one `Published` version per template.
- **Structured fields, not free HTML**:
  - An email version has the structured fields `Subject`, `Preheader`, `Greeting?`, `Heading`, `BodyParagraphs[]`, `ActionLabel?`, `SafetyNote`, `FooterNote?`. These match the existing `AccountEmailContent` exactly.
  - An in-app version has `Title`, `Message`, and `ActionLabel?`.
  - The code-owned branded shell renders HTML and plain text from the same fields, so every email is responsive, accessible, dark-mode-safe and script-free (FR-022). That shell is spec 061's `BrandedAccountEmailTemplateRenderer`, extended with `lang` and `dir`.
- **Renderer**: an in-house logic-free substituter for `{{ variableName }}` tokens.
  - There are no conditionals, loops, partials or expressions (FR-042).
  - Unknown tokens are rejected when a draft is saved and when it is published (FR-041).
  - Values are escaped per output context: HTML encoding in HTML bodies; CR, LF and control characters stripped in subjects and headers (FR-051); plain text as-is.
  - URLs are only ever produced by the platform's link builder (R11), never taken from a variable's raw value.
  - A value missing at render time uses the variable's declared fallback text and logs a warning (spec edge case).
- **Seeding**:
  - `NotificationTemplateSeeder` runs at startup, is idempotent, and inserts published v1 only for templates that don't exist yet. It never overwrites administrator edits.
  - English and Arabic seed content lives in embedded JSON resources under `Infrastructure/Notifications/Templates/Seed/{en,ar}/`.
  - Account-email v1 content is transcribed from today's `AccountEmailJob` and `PasswordEmailJob` copy, so it looks unchanged (FR-009b).

**Rationale**:
- Structured fields remove the largest template-misuse risk, admins writing raw HTML, while keeping every template editable (FR-040).
- They give HTML and text from one source, and reuse the approved branded design verbatim.
- An in-house substituter is about 100 lines. A general templating engine brings expression evaluation this feature must forbid.

**Alternatives considered**:
- Scriban, Fluid or Handlebars.Net. Rejected: they are Turing-complete or near it, and would need to be sandboxed down to what we would build anyway.
- Raw HTML bodies with a sanitizer. Rejected: phishing and layout risk, and no automatic plain-text version.
- Markdown bodies. Rejected: needs a new dependency (Markdig) for no benefit over paragraphs.
- `HasData` seeding in migrations. Rejected: large bilingual text churns the migrations, and it can't respect administrator edits.

## R10. One-time tokens and links: mint at send time, store nothing (FR-009d, FR-009e, FR-013)

**Decision**: Account-email requests never carry a token. Their type declares a `SensitiveLinkKind`: `EmailConfirmation`, `EmailChange` or `PasswordReset`. At render time the email worker calls `IAccountLinkIssuer.IssueAsync(kind, userId, targetAddress)`, an Application abstraction implemented in Infrastructure/Identity. It mints the link at that moment:

- **Email confirmation**: Identity's `GenerateEmailConfirmationTokenAsync`. This is already how `ResendConfirmationAsync` works inside its job.
- **Email change**: Identity's `GenerateChangeEmailTokenAsync` for the new address.
- **Password reset**: the spec-058 owned token (ADR 0009), issued and persisted as a hash at send time. Issuance moves from `PasswordResetIssuanceJob` into the issuer.

The rendered body lives only in memory for the SMTP call. It is never persisted, logged, measured or shown to administrators. Admin previews and test sends of these templates use an obviously fake sample link.

Each type's `RequestValidity` bounds how long a queued request stays sendable: 60 min for password reset, 24 h for confirmation. Beyond that the delivery becomes `Expired` rather than sending a fresh link for a stale request. This implements the edge case "one-time link expires while waiting for a retry". Each retry mints a new link, so a retried email never carries a dead link.

**Rationale**:
- If nothing sensitive is stored, FR-009d is satisfied structurally, not by discipline.
- It also removes the Data Protection key ring from the retry path (memory: `dataprotection_ephemeral_keyring`).

**Alternatives considered**:
- Store a Data Protection-protected token payload with the delivery and unprotect it at send time. Rejected: it is still a reversible secret at rest, it depends on key-ring durability, and a token could expire while waiting for retry.
- Keep account emails outside the hub. Rejected: violates FR-009.

## R11. Links and content security (FR-047–FR-050)

**Decision**:
- **Action links**: `INotificationLinkBuilder` builds every action URL from `App:BaseUrl` plus a **route template** declared on the type, for example `/workflows/executions/{relatedItemId}`. Variables are URL-encoded path segments.
- **External links**: generated only for types that declare `AllowsExternalLink`. None do in this feature. Any such link is labeled external in the rendered output.
- **File links**: use the existing expiring signed-URL service, minted at send time by the same mechanism as R10. The spec's attachments are "where permitted". No emitted type in this feature attaches files. The `EmailMessage.Attachments` contract streams from `IFileStorage`, so it never loads whole files into memory (FR-049).
- **In-app rendering**: the frontend renders `Title` and `Message` as **plain text** through React's default escaping. No `dangerouslySetInnerHTML` is used, and no Markdown is rendered for notifications.

**Rationale**: Links are constructed, never passed through from a variable, which closes the phishing-via-variable path. Plain-text in-app rendering removes the XSS surface.

**Alternatives considered**: allow-listing hosts on arbitrary URL variables. Rejected: it is easier to get wrong than never accepting URLs at all.

## R12. Anti-enumeration and the account-email request path (FR-009c, FR-009e, SC-014)

**Decision**: The account handlers are `Register`, `ResendConfirmation`, `RequestEmailChange`, `RequestPasswordReset`, `ChangePassword`, the 2FA change handlers, and `RequestAccountSupport`. They stop calling `IEmailSender`, `IAccountEmailJob` and `IPasswordEmailJob` directly and call `INotificationPublisher.Publish` instead.

- **Password reset** publishes `account.password-reset.requested` with the *submitted address*, whether or not an account exists. The request path is therefore identical, one outbox insert, for known and unknown addresses (FR-009e).
- **Recipient resolution** happens in the dispatcher. An address with no account completes the event with outcome `NoRecipient`, logged at Information with a hashed address. No notification is created, and that is not a failure.
- **Support mailbox**: support-request emails target `RecipientKind.SupportMailbox`. The address is resolved from server configuration only at send time. It is never stored on the delivery or shown in admin views (FR-009c).
- **Explicit addresses**: confirmation and email-change deliveries store their explicit target address. Admin views show it masked, for example `j•••@example.com`.
- **Legacy Hangfire jobs**: `AccountEmailJob`, `PasswordEmailJob` and `PasswordResetIssuanceJob` remain for **one release** as forwarding shims. They translate any still-queued legacy job into a hub request, and are deleted in the follow-up release. This drains the old queue without losing emails (Migration Considerations).
- **Priority**: account types are `Critical` priority with the reserved send lane (R6). Wake-signal dispatch keeps hand-off well under SC-014's 1 minute.

**Rationale**: The request thread does a constant amount of work regardless of whether the account exists, matching the current Hangfire-job-based design. The existence check moves into the background, where timing isn't observable.

**Alternatives considered**: resolving the user in the request handler and publishing only when found. Rejected: it reintroduces the timing and branch difference FR-009e forbids.

## R13. Migrating legacy document and memory notifications (FR-009, FR-009a, SC-013)

**Decision**:
- **Switch emitters in the same release**:
  - `ProcessingNotifier.NotifyAsync` and `MemoryNotifier` publish hub requests instead of creating `DocumentNotification`/`MemoryNotification` rows and pushing `notificationCreated`.
  - `IDocumentNotificationRepository`, `IMemoryNotificationRepository`, their endpoints, and the frontend inbox lists and hooks are removed.
- **Copy existing rows** with `LegacyNotificationImporter`, a one-shot idempotent startup task that runs after migrations. It performs `INSERT … SELECT … WHERE NOT EXISTS` keyed on `EventKey = 'legacy:document:{id}'` or `'legacy:memory:{id}'`. It preserves:
  - Recipient.
  - Type, via the mapping table in [data-model.md](data-model.md#legacy-mapping).
  - Message, as the notification's Message with the English `Language`. Legacy rows had no title, so the title comes from the type's English in-app template title.
  - Related item and route.
  - `CreatedAtUtc`.
  - Read state. `ReadAtUtc` comes from the memory row's `ReadAtUtc`. Document rows have only `IsRead`, so theirs is `ModifiedAtUtc ?? CreatedAtUtc`.
  
  Migrated rows get one `Delivered` in-app delivery and no email. The importer logs source and target counts per source.
- **Retire in two steps (§5)**: this release stops reading and writing `DocumentNotifications` and `MemoryNotifications`. A follow-up migration drops them, together with the importer and the Hangfire shims, once production counts are verified (see quickstart).

**Rationale**:
- Running as a startup task, rather than SQL inside the EF migration, lets the copy re-run safely and catch rows written between the migration and the new code taking traffic. `NOT EXISTS` on the event key makes it repeatable without duplicates (the Migration Considerations).
- The copy is a cheap no-op once complete.

**Alternatives considered**:
- Data migration SQL in the schema migration. Rejected: it runs once, so it can't pick up stragglers.
- Leaving the legacy lists live. Rejected: it produces double notification, forbidden by FR-009a.

## R14. Localization setting, user language and effective language (FR-044–FR-044c)

**Decision**:
- **Platform setting**: `LocalizationSetting` is a singleton row holding `IsEnabled` (default `false`), `SupportedLanguages` (always containing `en`), and `RowVersion`. It is cached in `IMemoryCache` with a 30 s TTL and evicted on change, so an admin toggle applies without redeployment (FR-044a).
- **User choice**: `ApplicationUser.PreferredLanguage` is a nullable BCP-47 `nvarchar(10)`, stored with the user profile as the spec requires. It is retained even when unsupported (FR-044b).
- **Resolution**: `IEffectiveLanguageResolver.Resolve(explicitLanguage?, userId?)` implements FR-044's order. When localization is disabled it returns `en`. Otherwise it takes the first *supported* candidate from: the explicit language, then the user's choice, then `en`. Template lookup falls back to the English published version when the chosen language has none.
- **Timing**: the language is resolved **at render time** (spec edge case "user switches language while queued"), and recorded on the notification and delivery (FR-044c).
- **Separate from the AI language**: `UserVoicePreference.DefaultLanguage` is the *AI response* language (spec 026). It is deliberately **not** reused as the interface language.

**Rationale**: This is the smallest model that meets FR-044a and FR-044b. The singleton-plus-cache approach matches the platform's existing policy caches.

**Alternatives considered**:
- `appsettings` for the platform setting. Rejected: it can't change without a redeploy.
- A separate `UserLanguagePreference` table. Rejected: the spec says the choice is stored with the existing user profile, without a duplicate user record.

## R15. Server-side text on localized surfaces (FR-046a validation, confirmation and toast text; edge case "server-side error on an Arabic admin screen")

**Decision**: The backend localizes **only** on localized surfaces:
- **Opt-in**: controllers opt in with endpoint metadata `[LocalizedSurface]`. That covers all `Admin*` controllers, the notification and preference endpoints, and the new admin notification endpoints.
- **Culture middleware**: `LocalizedSurfaceCultureMiddleware` runs after authentication. When the endpoint has the metadata, it sets `CultureInfo.CurrentUICulture` to the caller's effective language (R14). Other endpoints stay invariant English, so the rest of the app is unchanged, per the Arabic scope.
- **Validation messages**: FluentValidation's built-in `LanguageManager` already ships Arabic for its default validators. Custom messages in admin and notification validators, and exception `Detail` strings, move into `.resx` resources in Application: `Application/Localization/Messages.resx` plus `.ar.resx`. They are read through the generated strongly-typed class, which is BCL `ResourceManager` with no new package.
- **Problem Details**: `ProblemDetailsMiddleware` localizes `title` and `detail` the same way. `traceId`, `reason` codes and error codes stay untranslated (FR-046b).

**Rationale**:
- Admin screens mostly display server messages verbatim today (`errors` and `detail`), so localizing at the source keeps one rendering path.
- Scoping by endpoint metadata stops English-only surfaces, such as chat, from suddenly showing Arabic validation text.
- The mechanism is opt-in for future sections (FR-046c).

**Alternatives considered**: returning stable error codes and translating them only in the frontend. Rejected: it would need a code for every FluentValidation rule with placeholder arguments, and a cross-stack coverage contract. The server-side approach reuses FluentValidation's shipped Arabic.

## R16. Frontend i18n and right-to-left (FR-020, FR-045, FR-046, FR-046a, SC-011, SC-015)

**Decision**:
- **i18n library**: in-house and typed, in `src/i18n/`:
  - Catalogs per surface, such as `messages/en/notifications.ts` and `messages/en/admin/*.ts`.
  - Arabic catalogs typed `satisfies MessagesOf<typeof en>`, so a missing Arabic key fails `tsc -b` (SC-015 enforced at compile time).
  - A `useT(namespace)` hook that interpolates `{name}` and resolves plurals with `Intl.PluralRules`. Arabic has 6 plural forms.
  - Formatting via `Intl.NumberFormat('ar-u-nu-latn')` and `Intl.DateTimeFormat('ar-u-ca-gregory-nu-latn')`, giving Western digits and the Gregorian calendar with Arabic month names (Assumptions).
- **Direction**: `LocalizedSurface` wraps each localized subtree and provides `lang`, `dir`, an MUI theme with `direction: 'rtl'`, and an Emotion `CacheProvider` with `stylis-plugin-rtl`.
  - On `/admin/*` routes, the page shell sets `<html lang dir>` for the whole page. That page includes the header controls (FR-046a).
  - The notification bell popover and the notification pages wrap only their own content.
  - Portaled MUI surfaces get `dir` on their paper through `LocalizedSurface`'s slot defaults.
  - d3 time-series charts render their axes inside `dir="ltr"` groups (Assumptions: charts keep time left-to-right), with translated captions and legends.
- **Effective language**: fetched once through TanStack Query (`GET /api/v1/users/me/localization`) and invalidated on change. No Zustand duplication (§7).
- **New dependencies**: `stylis-plugin-rtl`, plus an explicit `@emotion/cache`, which is already present transitively. Both need **ADR 0017**, which records the i18n approach as the platform precedent.

**Rationale**:
- Typed catalogs give compile-time completeness for the "100% Arabic" criterion without an i18n runtime.
- `Intl` covers plurals and number and date formatting natively.
- `stylis-plugin-rtl` is MUI's documented RTL mechanism, and hand-flipping styles across twelve existing admin sections is not viable.
- Scoping direction per surface respects the spec's rule that the rest of the app stays English.

**Alternatives considered**:
- `react-i18next` or `i18next`. Mature, but two runtime dependencies and string keys without compile-time completeness. Deferred to whenever the whole app is localized, and ADR 0017 records that option.
- FormatJS (`react-intl`). Heavier ICU runtime, and still needs RTL handling.
- Global RTL on the whole app. Contradicts the Arabic scope.

## R17. Do-not-translate list (FR-046b, SC-016)

**Decision**:
- **Canonical list**: `docs/localization/do-not-translate.md`, human-readable for translators, mirrored as data in `ClientApp/src/i18n/protectedTerms.ts` and `Infrastructure/Notifications/Templates/ProtectedTerms.cs`.
- **Frontend check**: a Vitest test walks every English catalog string. For each protected term found in an English string, it asserts the Arabic counterpart contains the identical term.
- **Backend check**: a unit test does the same over seeded English and Arabic templates. `{{variables}}` must be preserved verbatim in both.
- **Runtime content**: user-entered values (role, agent and user names) are never passed through `t()`, only interpolated.

**Rationale**: This turns SC-016 into an automated gate instead of a manual review item. The native-speaker review stays a release step (Assumptions).

**Alternatives considered**: manual review only. Rejected: over-translation is the named risk.

## R18. Permissions and admin surfaces (FR-054a, FR-055)

**Decision**: Add `AdminArea.Notifications` and two catalogue entries:
- `admin.notifications.view` ("View notifications")
- `admin.notifications.manage` ("Manage notifications")

They follow the existing `AdminPermissionCatalog` pattern and are automatically held by Super User and Administrator. Admin endpoints use the existing `[RequirePermission(...)]` attribute and the `admin-endpoints` rate-limit policy. The frontend adds a **Notifications** group to `ADMIN_NAV` with Dashboard, Deliveries, Templates, Announcements and Localization, gated by `usePermissions`.

**Rationale**: This is the exact spec-055 mechanism, with no new authorization concept.

**Alternatives considered**: finer-grained permissions. Rejected by the clarification (Q4 → A).

## R19. Notification center queries and performance (FR-014, FR-017, SC-004)

**Decision**:
- **Paging**: keyset cursor pagination ordered `(CreatedAtUtc DESC, Id DESC)`, using the existing `*Cursor` encoding pattern (for example `ConversationCursor`), with a page size of 25 and a maximum of 100. Filters: category and read state.
- **Indexes**:
  - `IX_Notifications_Recipient_Center` on `(RecipientUserId, CreatedAtUtc DESC, Id DESC) INCLUDE (Category, ReadAtUtc, Priority)`, filtered `WHERE DeletedAtUtc IS NULL AND ShowInCenter = 1`.
  - `IX_Notifications_Recipient_Unread` on `(RecipientUserId)`, filtered `WHERE ReadAtUtc IS NULL AND DeletedAtUtc IS NULL AND ShowInCenter = 1`.
  - The unread count is an index-only `COUNT`.
- **Date grouping**: done client-side on the loaded page, in the viewer's time zone.
- **Virtualization**: the full-page list is virtualized (§7). The bell popover shows the latest 10.
- **Mark all read**: a set-based `ExecuteUpdateAsync` bounded to the caller's rows, followed by one `unreadCountChanged` push.

**Rationale**: Keyset paging plus filtered covering indexes keep the first page and the count constant-time in history size, which covers SC-004's 100k-row user.

**Alternatives considered**: offset paging. Its cost degrades with depth, and the constitution requires cursor paging for high-churn lists.

## R20. Retention (FR-059)

**Decision**: `NotificationRetentionJob` is a daily Hangfire recurring job. It deletes in batches of 1,000 with `ExecuteDeleteAsync` until none remain. The defaults, under `Notifications:Retention`, all configurable:

| Data | Default retention |
|---|---|
| Read notifications | 90 d after read |
| User-deleted notifications | 30 d after deletion |
| Failed and dead-lettered deliveries | 30 d after final failure |
| Other delivery rows | 90 d |
| Completed outbox events | 7 d |

Audit rows are **never** deleted by this job (Assumptions: audit follows project policy). Deleting a notification cascades to its deliveries. Deletion is ordered so a delivery that is still active is never orphaned: a notification with a non-terminal delivery is skipped.

**Rationale**: This follows existing recurring-job conventions in `Program.cs`. Batching keeps lock durations short on the shared host.

**Alternatives considered**: SQL Agent jobs. Not available on the shared host.

## R21. Observability (FR-057, FR-058, §14)

**Decision**:
- **Metrics**: a `System.Diagnostics.Metrics` `Meter` named `AskLucy.Notifications`, from the BCL with no new package. It emits these instruments:
  - Counters: `notifications.created`, `deliveries.sent`, `deliveries.failed` (tag `failure_kind`), `deliveries.retried`, and `provider.errors`.
  - Histograms: `delivery.latency_ms` (event occurred to sent, tag `channel`/`priority`).
  - Observable gauges: `outbox.backlog` and `deliveries.backlog` (oldest-pending age).
  
  Admin dashboards read **DB aggregates**, the source of truth, through `GetNotificationStatisticsQuery`. The meter feeds a future OTLP exporter without code change.
- **Health checks**, tagged `ready` and added to `/health/ready`:
  - `notifications-dispatcher` and `notifications-delivery-worker`: heartbeat age under 30 s.
  - `notifications-backlog`: oldest due outbox event or delivery under 5 min. Above that it is Degraded, and Unhealthy above 30 min.
  - `notifications-smtp`: connect, STARTTLS and authenticate, without sending. It is cached for 5 min so the probe never hammers the mail host, and it returns Degraded rather than Unhealthy so a mail outage doesn't take the site out of rotation.
  
  Database connectivity is covered by the existing DB check.
- **Correlation**: the originating request's correlation id is captured by `INotificationPublisher` from `CorrelationIdMiddleware`'s context. It is stored on the outbox event, the notification and the deliveries, and pushed into the Serilog `LogContext` for every worker iteration.

**Rationale**: This satisfies §14 with BCL primitives. Admin views don't depend on an external metrics backend the host doesn't have.

**Alternatives considered**: adding the OpenTelemetry SDK now. A platform-wide decision that is out of this feature's scope.

## R22. System announcements and fan-out (FR-004a; edge case "large critical announcement")

**Decision**:
- **Publishing**: `PublishSystemAnnouncementCommand` saves the `SystemAnnouncement` and publishes one outbox event, `system.announcement.published`, in one commit, and writes an audit entry.
- **Fan-out**: the dispatcher expands the audience in **batches of 500 users**, in keyset order of user id. It stores `FanOutCursor` on the event after each batch, so a crash resumes the fan-out without re-creating or skipping users. The unique `(RecipientUserId, EventKey)` index makes a replayed batch a no-op.
- **In-app**: in-app deliveries are created for every targeted active user.
- **Email**:
  - Email deliveries are created only when `IsCritical` is set, and only for recipients whose System-email preference is on (the default).
  - They are paced by the R6 bulk limiter.
  - Each delivery's `ExpiresAtUtc` is the announcement's end time. A due delivery past it becomes `Expired`.
- **Audience**: "active users" means not soft-deleted, not locked out, and email-confirmed for the email channel.

**Rationale**: Resumable batched fan-out keeps each transaction small on the shared DB and meets the "all in-app copies appear at once, emails spread over time" edge case.

**Alternatives considered**: one fan-out transaction. Rejected: an unbounded transaction and lock escalation.

## R23. Approval notifications (FR-035–FR-037)

**Decision**:
- **Recipient**: the approval systems resolve the approver themselves. Today that is the execution owner: `AgentExecution` and `WorkflowExecution` have no separate approver. They publish `agent.approval.requested` and `workflow.approval.requested` with that recipient in the same unit of work as `RequestApproval(...)`, inside `AgentExecutionOrchestrator` and `WorkflowExecutionOrchestrator`.
- **Content and links**: the notification carries only a route to the existing approval screen. There is no approve or reject mechanism in any channel (FR-036). The screen re-authorizes on load, as it does today.
- **Audit**: create, delivery and read events for Approval-kind notifications are written to `NotificationAuditLog` (FR-037). Retention never deletes them.

**Rationale**: The hub stays ignorant of approval semantics (Dependencies: approval systems own approver resolution).

**Alternatives considered**: the hub querying approval tables. Rejected: it couples engines, which Mission forbids.

## R24. Authorization before content generation (FR-052, FR-053)

**Decision**:
- **Ownership**: emitters only publish to the owner of the related item, and every emitter in this feature notifies the item's owner.
- **Active-recipient check**: the dispatcher re-validates that each recipient is active (not deleted) at materialization and cancels deliveries for deleted accounts (edge case). For types flagged `RequiresItemAccess`, it calls the owning module's `INotificationAccessCheck` for that item type (for example documents and executions) before rendering.
- **Endpoints**: every notification endpoint filters by `ICurrentUserAccessor.UserId`. Accessing another user's notification id returns 404, not 403, so existence isn't revealed.

**Rationale**: This is defense in depth for future shared items, such as shared knowledge bases, without changing emitters today.

**Alternatives considered**: trusting emitters only. Rejected: FR-053 requires the check at generation time.

## R25. Rate limiting and API shape (§6)

**Decision**:
- **Rate limits**: a new `notifications-endpoints` per-user policy, with a fixed window of 120 requests per minute, applies to the user endpoints. Admin endpoints reuse `admin-endpoints`. Test sends are additionally capped at 10 per hour per admin through a named policy, `notifications-test-send`.
- **Paths**: `/api/v1/notifications…`, `/api/v1/users/me/notification-preferences`, `/api/v1/users/me/localization`, `/api/v1/admin/notifications/…` and `/api/v1/admin/localization`.
- **Actions**: action sub-resources for non-CRUD verbs, such as `POST …/actions/retry`, `…/actions/publish`, `…/actions/archive` and `…/actions/mark-all-read`.

Full shapes are in [contracts/](contracts/).

**Rationale**: This follows constitution §6 and existing controller conventions.

## R26. Delivery slicing (Risk: localization groundwork)

**Decision**: The work ships as ordered, independently deployable slices (see the plan's *Delivery Slices*). Admin-area Arabic is its own final slice, deployable separately. It stays inert until an administrator enables localization, so it can't regress English behaviour. The feature stays in one spec (067) as the user scoped it. Only the slicing is independent.

**Rationale**: The user pushes directly to main and every merge deploys, so each slice must leave production coherent. Localization-disabled-by-default makes the localization slices dark launches.

**Alternatives considered**: splitting admin localization into its own spec (would now be 069). Offered earlier and not taken. The slice boundary keeps that option cheap.

## R27. Knowledge-base indexing trigger and emit points

**Finding**: `IIndexingOrchestrator.IndexKnowledgeBaseDocumentAsync` has no production caller, and neither does `IRetrievalIndexingNotifier`. `UploadDocumentCommandHandler` publishes `DocumentUploadedNotification` after its save, but only `WorkflowEventTriggerHandler` handles it. Knowledge-base uploads are therefore never indexed today, so `knowledge-base.indexing.*` could never fire.

**Decision**: this feature wires the trigger (Phase 4b). A new `KnowledgeBaseDocumentUploadedIndexingHandler` handles `DocumentUploadedNotification` for documents that belong to a knowledge base: it creates an `IndexingJob` and enqueues `IKnowledgeBaseIndexingJob` through `IBackgroundJobClient`. `KnowledgeBaseIndexingJob` (`[AutomaticRetry(Attempts = 3)]`) marks the job and the knowledge base, calls the orchestrator, settles the job from the `IndexingOutcome`, and publishes the notification in the same save:

- `document.indexing.{completed,failed}` with EventKey `indexing-job:{jobId}:{outcome}`;
- `knowledge-base.indexing.{completed,failed}` when the knowledge base settles, with EventKey `knowledge-base:{id}:indexing:{settledAtTicks}`.

A failed job is also recorded through spec 074's `IOperationalFailureRecorder`. `IndexingOrchestrator` itself is unchanged. `knowledge-base.updated` stays catalogued with `IsEmitted=false`: knowledge bases are per-user today, so no one other than the owner can update one.

**Rationale**: the user chose to wire indexing here (analysis C4, option a) rather than ship dormant types. Doing it in a Hangfire job keeps the upload request fast and gives retries for free.

**Consequences**: every knowledge-base upload now spends embedding credits. Documents uploaded before this release are not back-filled; they are indexed when re-uploaded (release note in T229).

**Alternatives considered**: calling the orchestrator inline in the upload handler (slow requests, no retry); a back-fill job for existing documents (unbounded embedding cost with no user asking for it; can be added later); leaving the types dormant (the user rejected it).

## R28. Preference precedence

**Decision**: a saved (category, channel) preference applies to every optional type in that category whose catalogue entry supports that channel. With no saved preference, each type uses its own catalogue default. A saved preference never changes a mandatory (type, channel) pair and never adds a channel the type marks `—`.

**Rationale**: users think in categories ("no Workflow email"), and the preferences screen shows categories. Per-type defaults still let routine "started" events default to off while failures default to on inside the same category.

**Consequences**: every emitted type whose Email column is `on` or `off` can end up sending email, so each needs an email template (T119, SC-006).

**Alternatives considered**: per-type preferences (a much larger screen, and nothing in the spec asks for it); a category preference that only applies where the type default matches (surprising: "Email on" would not turn on emails for routine events).

## R29. SC-014 baseline (account email latency)

**Decision**: before the account emails move onto the hub, T233 measures the current Hangfire path: p95 time from a password-reset or email-confirmation request to the hand-off to the mail sender, over 50 runs with a capturing sender. The hub path must then meet p95 ≤ 60 s and stay within the baseline + 10%.

**Baseline**: _to be filled by T233_.

**Rationale**: SC-014 says "no slower than before migration", which is untestable without a number from before the migration.
