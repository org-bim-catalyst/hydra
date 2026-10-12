# Quickstart: Validating the Notifications & Communication Hub

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

This guide covers how to run the automated suites and how to prove each user story end to end. Endpoint and entity details are in [contracts/](contracts/) and [data-model.md](data-model.md). This guide doesn't repeat them.

---

## 1. Prerequisites

| Need | Value |
|---|---|
| .NET SDK | 10.x |
| Node | per `ClientApp/.nvmrc` / CI |
| `PERSISTENCE_TESTS_CONNECTION_STRING` | the shared site4now test DB (value in `appsettings.Development.json`). Without it most `Web.Tests` fail at Hangfire startup. |
| `PERSISTENCE_TESTS_DEDICATED_DATABASE=1` | only when intentionally running persistence tests against a dedicated database |
| SMTP for manual email checks | the existing `Smtp` settings. In development `DevEmailSender` writes emails to the log/pickup folder instead of sending. |
| Seed admin | `SeedAdmin` user-secrets (the dev DB self-heals via `DevBaselineSeeder`) |

## 2. Automated suites

Run all commands from the repo root unless noted.

```bash
dotnet build "Ask Lucy.sln" -warnaserror
dotnet format "Ask Lucy.sln" --verify-no-changes          # CI gate; ENDOFLINE noise vs real \r\r\n per repo notes

dotnet test tests/AskLucy.Domain.Tests        --filter "FullyQualifiedName~Notifications|FullyQualifiedName~Localization"
dotnet test tests/AskLucy.Application.Tests   --filter "FullyQualifiedName~Notifications|FullyQualifiedName~Localization"
dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~Notifications"
dotnet test tests/AskLucy.Persistence.Tests    --filter "FullyQualifiedName~Notifications"
dotnet test tests/AskLucy.Web.Tests            --filter "FullyQualifiedName~Notifications|FullyQualifiedName~Localization"
dotnet test "Ask Lucy.sln"                     # full run before pushing

cd src/AskLucy.Web/ClientApp
npx tsc -b --noEmit        # NOT bare `tsc --noEmit` (project references → silent no-op)
npm run lint
npm test                   # full vitest suite; page-level tests catch component changes
```

**Expected**: everything is green. Pay particular attention to these suites:

| Suite | Proves |
|---|---|
| `NotificationRouterTests` | Covers the whole FR-003 decision table, including mandatory pairs that ignore preferences (SC-009). |
| `NotificationCatalogCoverageTests` | Every emitted type has published `en` and `ar` seed templates on each channel it uses (SC-006). |
| `TemplateRendererTests` | Escaping per context. Header CR/LF are stripped. Unknown tokens are rejected. A missing value uses the fallback and logs a warning (FR-041, FR-042, FR-050, FR-051). |
| `ProtectedTermsTests` (backend) and `protectedTerms.test.ts` (frontend) | SC-016 |
| `DeliveryFaultInjectionTests` (Web.Tests, real DB) | SC-003 (see §4) |
| `NotificationCenterPerformanceTests` | SC-004. Gated with the other scale tests: set `RUN_SCALE_PERFORMANCE_TESTS=1`. |
| `StubChannelProofTests` | SC-012: a `TestChannelSender` is registered with **no** emitter or router change. |
| `AccountEmailAntiEnumerationTests` | The same response and the same single outbox insert for known and unknown addresses (FR-009e). |
| `AccountEmailTokenLeakTests` | After sending, no reset or confirm token substring is present in any notification, delivery, outbox, audit or log sink (FR-009d, SC-007). |

## 3. End-to-end scenarios

Run the app locally (`dotnet run --project src/AskLucy.Web`, with Vite through the SPA proxy), then:

1. Sign in as **User A** in window 1.
2. Sign in as **Admin** in window 2.

For each scenario, record the checked values. Screenshots are only needed where a scenario calls for them.

### S1: In-app live delivery (US1, US2, SC-001)
1. In window 1, keep any page open with the bell visible, and note the unread count **N**.
2. Upload a small PDF as User A and wait for processing.
3. **Expected**:
   - Within about 5 s of processing completing, the badge shows **N+1** without a refresh.
   - The bell popover's first item is "Document ready …" with an **Open document** action.
4. Click the action. It opens `/documents/{id}`.
5. Reopen the bell. The item is now marked read and the badge shows **N**.

### S2: Center paging, filters and delete (US1, FR-014 to FR-016a)
1. Open `/notifications`. Items are grouped by date, and scrolling loads the next page (network: `GET /notifications?cursor=…`).
2. Filter by **Document** and **Unread**. Only matching items are listed.
3. Delete one item. It disappears from the list, the badge and window 1's other tab.
4. `GET /api/v1/notifications/{deletedId}` returns **404**.

### S3: Email channel and preferences (US3, US4)
1. As User A, set **Workflow → Email = on** in Settings → Notifications. **Security** rows show as locked.
2. Try `PUT /users/me/notification-preferences` disabling `Security/Email`. It returns **422**, and nothing changes.
3. Run a workflow designed to fail. The failure notification appears in-app, and an email is handed off within about 2 min. The dev pickup or log shows the branded email with HTML and a text part, and a `Message-ID` of `<{deliveryId}@…>`.
4. Turn Workflow email off and fail the workflow again. In-app only; the delivery shows `Skipped (PreferenceDisabled)` in admin Deliveries.

### S4: Approval notification (US5, FR-035, FR-036)
1. Start an agent or workflow with an approval step.
2. **Expected**:
   - The notification links to the existing approval screen.
   - The email contains **no** approve or reject buttons.
   - Opening the link while signed out forces sign-in, after which the approval screen re-checks access.
3. Admin → Audit shows `ApprovalNotificationCreated`, then `…Delivered`, then `…Read`.

### S5: Account emails (US9, SC-014, FR-009c to FR-009e)
1. Request a password reset for User A's address. The page response appears immediately. The email is handed off within **60 s** and the link works once.
2. Request a reset for `nobody@example.invalid`. The page response is identical. Admin Deliveries shows no delivery, and the log shows outcome `NoRecipient` with a hashed address.
3. Register a new account. The confirmation email arrives, and **no** in-app notification is created for it.
4. Admin → Deliveries → open the reset delivery. The recipient is masked, and there is no body, token or link anywhere.

### S6: Failure, retry and dead letter (US6, FR-026 to FR-030)
1. Point `Smtp:Host` at an unroutable address and restart. Trigger an email-enabled notification.
2. **Expected**:
   - Admin → Deliveries shows the delivery as `Retrying`, with an increasing `nextAttemptAtUtc`.
   - After 5 attempts it becomes `DeadLettered` (`RetryLimitReached`). You can shorten this in dev with `Notifications:Retry:*`.
   - `/health/ready` reports `notifications-smtp: Degraded`, and the site stays up.
3. Restore SMTP, then **Retry** the delivery. It becomes `Sent`, and the audit shows `DeliveryRetried`.
4. Delete the underlying notification as the user, then retry again. The API returns **409** with `NotificationDeleted`.

### S7: Templates (US7, FR-038 to FR-043)
1. Admin → Notifications → Templates → `workflow.execution.failed` / Email / en, then **New draft**.
2. Insert `{{ secretToken }}`. Save returns **422**, naming the unknown variable.
3. Insert `<script>` or `https://evil.test` in a paragraph. Both are rejected.
4. A valid edit, then **Preview**, renders in the sandboxed iframe. **Send test** reaches the admin's own address only.
5. **Publish**. The version increments, and the previous version shows `Archived`. The next failure email records the new `templateVersionId`.
6. Try to archive the only published version of a shipped default. It returns **409** with `LastPublishedDefault`.

### S8: Announcements (FR-004a)
1. Publish a **non-critical** Maintenance announcement to all users. Every active user gets it in-app and no email is queued.
2. Publish a **critical** announcement with `endsAtUtc` 10 minutes out.
   - Emails drain at no more than `MaxPerMinute`.
   - Anything still queued at the end time becomes `Expired`.
   - A password reset requested mid-drain still arrives within 60 s, through the reserved lane.

### S9: Localization (US8, SC-015, SC-016)
1. With localization disabled (the default), confirm there is no language switch anywhere. `GET /users/me/localization` returns `effectiveLanguage: "en"`.
2. Admin → Localization: enable it with `en` and `ar`. Unticking `en` is impossible. The audit shows `LocalizationSettingChanged`.
3. As User A, switch to **العربية**.
   - The bell popover, `/notifications` and preferences render RTL, in Arabic.
   - Chat, workspace and every other page stay English, LTR.
   - Numbers use Western digits, and dates use Arabic month names.
4. Trigger S1 again. The new notification is Arabic (`language: "ar"`), and older ones keep their English text.
5. As an Admin with Arabic selected, walk every admin section: Dashboard, Users, Roles, Role assignments, System agents, AI providers, Default models, AI capabilities, Agent policies, Workflow policies, MCP servers, Jobs, and the five Notifications screens. On every screen:
   - The page is RTL.
   - No English UI text remains, apart from protected terms such as OpenAI, Anthropic, MCP, AI, SMTP and model ids, which appear verbatim.
   - Chart time axes run left to right.
   - Submitting an invalid form shows Arabic validation text, with the `traceId` untranslated.
6. Disable localization. User A immediately sees English everywhere. Re-enabling it restores Arabic, because the choice was retained.

### S10: Legacy migration (US9, SC-013)
Run this against a DB copy that has legacy rows. It is **required against production after deploy**.

```sql
-- Source vs imported counts: every pair must match.
SELECT 'document' src, COUNT(*) legacy,
       (SELECT COUNT(*) FROM Notifications WHERE EventKey LIKE 'legacy:document:%') imported
FROM DocumentNotifications
UNION ALL
SELECT 'memory', COUNT(*),
       (SELECT COUNT(*) FROM Notifications WHERE EventKey LIKE 'legacy:memory:%')
FROM MemoryNotifications;

-- Read-state preserved: expect 0 rows.
SELECT d.Id FROM DocumentNotifications d
JOIN Notifications n ON n.EventKey = CONCAT('legacy:document:', d.Id)
WHERE (d.IsRead = 1 AND n.ReadAtUtc IS NULL) OR (d.IsRead = 0 AND n.ReadAtUtc IS NOT NULL);
```

**Expected**:
- The counts match.
- Restarting the app doesn't change them, because the import is idempotent.
- No new rows appear in the legacy tables after deploy.
- The old document and memory inbox UIs are gone.

## 4. Fault-injection run (SC-003)

`DeliveryFaultInjectionTests` drives 1,000 deliveries through the real pipeline, against the shared test DB with a fake SMTP server, while injecting four faults:
- (a) killing the worker's scope mid-send;
- (b) SMTP 4xx and connection drops;
- (c) two concurrent worker instances;
- (d) throwing after `SaveChanges` in the emitting handler.

**Expected**:
- `lost = 0`: every outbox event is materialized, and every delivery reaches a terminal or retrying state.
- `duplicates = 0`: no `Message-ID` is sent twice.
- Any `AmbiguousOutcome` deliveries are listed and are **not** auto-resent.

## 5. Release checklist (production is hand-deployed)

Every push to main deploys, so the feature ships as six slices (plan.md "Delivery Slices", tasks.md "Deployment order"). Each slice leaves production coherent. The steps below apply to every slice unless a step says otherwise.

### 5.1 Before deploying

1. **Apply the EF migrations first**, by hand, to every database the build will touch (production, and the shared test databases that CI uses; CI fails against a database that is behind). The feature adds two migrations:
   - `AddNotificationHub` (slice 1) creates the eight notification tables and their indexes.
   - `AddLocalizationSettingAndUserPreferredLanguage` (slice 5) adds `AspNetUsers.PreferredLanguage`, creates `LocalizationSettings` and seeds the singleton row (disabled, `["en"]`).

   A host that boots before its migration has run fails the `pending-migrations` readiness check.
2. **Hand-add configuration keys** to the untracked `appsettings.Production.json`. Only key names are listed here; never commit or paste values. All are optional (each has a default and a missing section never stops the host), but the first two must be set deliberately before slice 2:

   | Key | Default | Notes |
   |---|---|---|
   | `Notifications:Email:MaxPerMinute` | 60 | Set to the sending limit of the myasp.net plan, confirmed with the host. |
   | `Notifications:Email:ReservedPerMinuteForMandatory` | 20 | Capacity bulk mail can never use, so a password reset is never queued behind an announcement. Keep it below `MaxPerMinute`. |
   | `Notifications:Email:SendTimeoutSeconds` | 60 | Keep generous on the shared host. |
   | `Notifications:Retention:ReadDays` | 90 | Read notifications. |
   | `Notifications:Retention:DeletedDays` | 30 | Owner-deleted notifications. |
   | `Notifications:Retention:FailedDays` | 30 | Failed and dead-lettered deliveries, from the last attempt. |
   | `Notifications:Retention:DeliveryDays` | 90 | Finished non-in-app deliveries. |
   | `Notifications:Retention:CompletedOutboxDays` | 7 | Completed outbox events. |
   | `Notifications:Retention:BatchSize` | 1000 | Rows per delete batch (clamped to 1-5000). |

   The existing `Smtp` and support-mailbox settings are unchanged, and **no new secret is introduced**. The other `Notifications:*` sections (`Dispatch`, `Retry`, `Channels`, `Center`, `HealthChecks`) have defaults that need no production override.
3. Make sure the host can run two background services (the dispatcher and the delivery worker) and the Hangfire jobs `notification-lease-sweep` (every minute) and `notification-retention` (daily, 03:00).

### 5.2 After deploying

1. Check `/health/ready`. All four checks must be Healthy: `notifications-dispatcher`, `notifications-delivery-worker`, `notifications-backlog` and `notifications-smtp`. SMTP may show Degraded on the first probe (its result is cached for 5 minutes); a Degraded SMTP check never takes the site out of rotation. From slice 1 the dispatcher and backlog checks apply; the delivery-worker and SMTP checks are meaningful from slice 2.
2. Slice 1 only: run the S10 SQL against prod (see the repo notes on querying the prod DB). The counts must match, the read state must be preserved, and a restart must not change them.
3. Slice 2 only: confirm there are no pending legacy `AccountEmailJob` or `PasswordEmailJob` entries in the Hangfire dashboard after about 5 minutes, because the shims drain them.
4. Request a password reset for a test account and confirm the email is handed off within 60 s.
5. An Arabic native speaker has signed off on the templates and admin copy (spec Assumptions) **before** localization is enabled in production. Localization stays disabled until then, and slices 5 and 6 are dark until an administrator enables it.

### 5.3 Release notes per slice (constitution section 13)

1. **Slice 1: Hub core and in-app notifications.** One notification center for the whole app: a bell with a live unread badge, a popover, a `/notifications` page with filters, mark-read, mark-all-read and delete, and a details view. Agents, workflows, documents, knowledge bases and memory now report through the hub, and two-factor and recovery-code changes appear as security notices. The old document and memory inboxes, their endpoints (`/documents/notifications`, `/memories/notifications`) and their `notificationCreated` pushes are removed in this same release, and existing legacy rows are imported once at startup (no new rows reach `DocumentNotifications` or `MemoryNotifications`). Migration: `AddNotificationHub`. Knowledge-base uploads now trigger indexing (FR-004b). **Knowledge-base documents uploaded before this slice stay un-indexed until the user re-uploads them; they are not back-filled.**
2. **Slice 2: Email channel and account emails.** A delivery worker with retry, dead letter, a lease sweeper and a rate-limited sender. Account emails (confirmation, email change, password reset and support requests) move behind the hub, and the security notices (password changed, two-factor changes, recovery codes) gain their email, which is mandatory and cannot be switched off, with links minted at send time and no change to what a requester can learn. `AccountEmailJob`, `PasswordEmailJob` and `PasswordResetIssuanceJob` remain for one release as `[Obsolete]` forwarding shims that drain jobs already queued. New configuration: `Notifications:Email:*`.
3. **Slice 3: Preferences and approvals.** Settings has a Notifications tab with per-category, per-channel toggles. Security notices are locked on. Agent and workflow approval requests notify the approver in-app and by email with a link to the existing approval screen (no approve or reject in the email), and their history is audited.
4. **Slice 4: Admin operations.** A new Notifications group in Admin: dashboard and statistics, delivery list with retry and bulk retry, channel health, template editor with preview, test send, publish and archive, announcements (plain text, in-app for all, email only if critical) and an audit view. New permissions `admin.notifications.view` and `admin.notifications.manage` need assigning to the roles that should have them. The daily retention job starts. New configuration: `Notifications:Retention:*`.
5. **Slice 5: Localization foundation and notification screens.** An administrator setting (off by default) and a per-user language choice; Arabic templates; Arabic, right-to-left bell, center, details and preferences; server error text localized on notification and admin endpoints. English is unchanged while the setting is off. Migration: `AddLocalizationSettingAndUserPreferredLanguage`.
6. **Slice 6: Admin area in Arabic.** The admin shell, the 12 existing sections and the five notification screens are available in Arabic and right-to-left when localization is enabled. No backend or schema change.

### 5.4 Follow-up release

Once the S10 counts are verified in production, a separate release drops `DocumentNotifications` and `MemoryNotifications` (migration `DropLegacyDocumentAndMemoryNotifications`), the startup importer and the three `[Obsolete]` forwarding shims (research R13 and R12). It is not part of this feature.
