# Contract: Admin Notifications & Localization API

**Base**: `/api/v1/admin` | **Auth**: JWT bearer plus `[RequirePermission]` | **Rate limit**: `admin-endpoints`. Test sends are also limited by `notifications-test-send`, 10 per hour per admin. | **Errors**: Problem Details with `traceId`, and a machine-readable `reason` where stated.
**Localized surface**: yes (research R15). Every state-changing call writes a `NotificationAuditLog` row (FR-054, SC-008). Read-only views that expose delivery data are audited as `…Viewed` at most once per admin per resource per hour.

**Permissions** (FR-054a):
- **V** = `admin.notifications.view`.
- **M** = `admin.notifications.manage`. Holding M implies V in the UI, but the server checks each endpoint independently.

Responses never contain:
- mail credentials, the support-mailbox address, one-time tokens or links, or rendered bodies of account emails;
- an unmasked recipient address (FR-009c, FR-009d, FR-024, FR-056).

---

## Statistics & health

### GET `/notifications/statistics` (V)

| Query | Default | Notes |
|---|---|---|
| `from`, `to` | the last 7 days | UTC, maximum range 90 days |

**200**:
```json
{
  "created": 4210, "sent": 1180, "failed": 12, "deadLettered": 2, "ambiguous": 0,
  "emailSuccessRate": 0.989, "averageDeliveryLatencyMs": 3400, "p95DeliveryLatencyMs": 41000,
  "retries": 31, "backlog": { "outboxPending": 0, "deliveriesDue": 3, "oldestDueAgeSeconds": 12 },
  "unreadNotifications": 9123,
  "byCategory": [ { "category": "Workflow", "created": 800, "failed": 3 } ],
  "series": [ { "bucketStartUtc": "2026-09-22T00:00:00Z", "created": 610, "sent": 170, "failed": 1 } ]
}
```

Values are DB aggregates (research R21). `series` uses hourly buckets for ranges of 2 days or less and daily buckets otherwise.

### GET `/notifications/channels` (V)

**200**:
```json
[
  {
    "channel": "Email",
    "enabled": true,
    "provider": "SMTP",
    "health": "Healthy",
    "checkedAtUtc": "…",
    "detail": "STARTTLS ok",
    "sendLimitPerMinute": 60
  },
  {
    "channel": "InApp",
    "enabled": true,
    "provider": "SignalR",
    "health": "Healthy",
    "checkedAtUtc": "…",
    "detail": null
  }
]
```

`health` is one of `Healthy`, `Degraded` or `Unhealthy`, taken from the cached health-check results. `detail` is a safe summary. It never contains the host credentials or the full server banner.

---

## Deliveries

### GET `/notifications/deliveries` (V)

This endpoint lists deliveries with cursor pagination (FR-055, FR-056).

| Query | Notes |
|---|---|
| `status` | may repeat. Default: `Failed`, `DeadLettered`. |
| `channel`, `category`, `type` | optional filters |
| `from`, `to` | limits on `lastAttemptAtUtc` |
| `cursor`, `limit` | `limit` defaults to 50, maximum 200 |

**200**:
```json
{
  "items": [
    {
      "deliveryId": "…", "notificationId": "…",
      "type": "workflow.execution.failed", "category": "Workflow", "channel": "Email",
      "status": "DeadLettered", "failureKind": "RetryLimitReached",
      "failureReason": "Mail server rejected the message (temporary): mailbox busy.",
      "providerResponse": "451 4.3.0",
      "attempts": 5, "lastAttemptAtUtc": "…", "nextAttemptAtUtc": null,
      "recipient": { "kind": "User", "userId": "…", "displayName": "M. S.", "address": "m•••@bimcatalyst.com" },
      "correlationId": "…", "retryable": true, "notRetryableReason": null
    }
  ],
  "nextCursor": null
}
```

`notRetryableReason` is one of `NotificationDeleted`, `NotificationExpired`, `RecipientDeleted` or `NotFailed`, and is null otherwise. For `SupportMailbox` recipients, `recipient.address` is always `null`.

### GET `/notifications/deliveries/{deliveryId}` (V)

**200**: the item shape plus a `notification` summary (`title`, `createdAtUtc`, `language`, `templateVersionId`). Per-attempt history is not stored: the delivery keeps its attempt count and last outcome (FR-056), and each attempt's details are in the structured log under the delivery's `correlationId`. The message body is **omitted** for Security and Account categories.

### POST `/notifications/deliveries/{deliveryId}/actions/retry` (M)

**202**: `{ "deliveryId": "…", "status": "Pending" }`. Returns **409** with `reason` when the delivery isn't retryable: `NotificationDeleted`, `NotificationExpired`, `RecipientDeleted` or `NotFailed` (spec edge case). The retry is audited as `DeliveryRetried`.

### POST `/notifications/deliveries/actions/retry` (M)

This is the bulk retry.

Request: `{ "deliveryIds": ["…"] }` (1–200), **or** `{ "filter": { "status": ["DeadLettered"], "channel": "Email", "from": "…", "to": "…" } }` (at most 1,000 matched).

**200**: `{ "requested": 40, "retried": 37, "skipped": [ { "deliveryId": "…", "reason": "NotificationExpired" } ] }`. The action is audited once as `DeliveriesBulkRetried`, with the counts.

---

## Templates

### GET `/notifications/templates` (V)

| Query | Notes |
|---|---|
| `category`, `channel`, `language`, `type` | optional filters |

**200**: `[{ "templateId", "type", "category", "channel", "language", "name", "publishedVersion": { "id", "versionNumber", "publishedAtUtc" } | null, "hasDraft": true }]`. The list is small (< 200 rows), so it isn't paginated.

### GET `/notifications/templates/{templateId}` (V)

**200**: the template, plus `versions: [{ id, versionNumber, status, createdAtUtc, createdBy, publishedAtUtc, archivedAtUtc }]`, plus `declaredVariables: [{ name, description, sample, fallback }]` from the catalogue, plus `isShippedDefault`.

### GET `/notifications/templates/{templateId}/versions/{versionId}` (V)

**200**: the full version fields for the template's channel. For email: `subject`, `preheader`, `greeting`, `heading`, `bodyParagraphs[]`, `actionLabel`, `safetyNote` and `footerNote`. For in-app: `title`, `message` and `actionLabel`.

### POST `/notifications/templates/{templateId}/versions` (M)

This creates a draft. Request: the channel's version fields, with an optional `copyFromVersionId`. **201** with a `Location` header.

**422** field errors cover:
- an unknown variable (FR-041), with the offending token in the error;
- malformed `{{`;
- a raw URL or HTML in a text field;
- length limits (see [data-model.md](../data-model.md#notificationtemplateversion)).

### PUT `/notifications/templates/{templateId}/versions/{versionId}` (M)

This edits a **draft** only, and requires an `If-Match` header carrying the base64 `RowVersion`.

| Code | When |
|---|---|
| 200 | edited |
| 409 | the version isn't a draft (`reason: VersionNotDraft`), or the concurrency check failed (`reason: ConcurrencyConflict`) |
| 422 | same validation as create |

### POST `/notifications/templates/{templateId}/versions/{versionId}/actions/preview` (V)

Request: `{ "variables": { "workflowName": "Demo" } }`. The body is optional. Missing values use the catalogue's sample values.

**200**: `{ "subject", "html", "text" }` for email, or `{ "title", "message", "actionLabel" }` for in-app. The response is rendered by the production renderer with the template's `lang` and `dir`.

- Sensitive link kinds render the fixed sample link `https://example.invalid/sample-link`.
- The HTML is returned for display in a **sandboxed iframe** (`sandbox=""`, no scripts). The client MUST NOT inject it into the DOM directly.

### POST `/notifications/templates/{templateId}/versions/{versionId}/actions/send-test` (M)

This sends the rendered **email** version to the *calling admin's own verified address*. There is no free-form recipient. Sample variables and the sample link are used.

- **202**: `{ "deliveryId": "…" }`. It goes through the normal delivery pipeline with type `template.test`, so it is visible in deliveries.
- **429**: over `notifications-test-send`.
- **422**: the admin has no verified address.
- The send is audited as `TemplateTestSent`.

### POST `/notifications/templates/{templateId}/versions/{versionId}/actions/publish` (M)

Requires `If-Match`. **200** returns the published version and archives the previous one (FR-039).

| Code | Reason |
|---|---|
| 409 | `VersionNotDraft` or `ConcurrencyConflict` |
| 422 | publish-time variable validation failed |

The action is audited as `TemplateVersionPublished`, with the previous and new version numbers.

### POST `/notifications/templates/{templateId}/versions/{versionId}/actions/archive` (M)

**200**. Returns **409** with `reason: LastPublishedDefault` when archiving would leave a shipped default with no published version (SC-006). The action is audited.

---

## System announcements

### GET `/notifications/announcements` (V)

This lists announcements newest first, with `cursor` and `limit` paging. **200** items: `{ id, kind, title, audience, targetRoles: [{ id, name }], isCritical, endsAtUtc, publishedAtUtc, publishedBy, recipientCount, fanOutStatus: "InProgress" | "Completed", emailQueued, emailSent, emailExpired }`.

### POST `/notifications/announcements` (M)

Request:
```json
{
  "kind": "Maintenance",
  "title": "Scheduled maintenance",
  "message": "Ask Lucy will be unavailable 22:00–23:00 UTC.",
  "audience": "AllActiveUsers",
  "targetRoleIds": null,
  "isCritical": true,
  "endsAtUtc": "2026-09-30T23:00:00Z"
}
```

- **Validation**:
  - Title is 1–150 characters and message is 1–2,000.
  - `targetRoleIds` is required and non-empty when `audience = Roles`, and each role must exist.
  - `endsAtUtc` must be in the future when set.
  - Text is plain: no HTML, no raw URLs.
- **201**: `{ "id": "…", "estimatedRecipients": 1234, "emailEstimatedMinutes": 21 }`, where the minutes are computed from the send limiter.
- The action is audited as `AnnouncementPublished`.
- Announcements are immutable once published. There is no PUT or DELETE (FR-004a: never a marketing channel).

---

## Localization settings

### GET `/localization` (V)

**200**:
```json
{
  "isEnabled": false,
  "supportedLanguages": ["en"],
  "availableLanguages": [ { "code": "en", "nativeName": "English", "locked": true }, { "code": "ar", "nativeName": "العربية", "locked": false } ],
  "rowVersion": "AAAAAAAAB9E="
}
```

`availableLanguages` lists only the languages the platform ships content for (FR-044a, spec edge case).

### PUT `/localization` (M)

Requires `If-Match: <rowVersion>`. Request: `{ "isEnabled": true, "supportedLanguages": ["en", "ar"] }`.

| Code | When |
|---|---|
| 200 | the new state; the cache is evicted, so it takes effect without a redeploy |
| 409 | `ConcurrencyConflict` |
| 422 | `en` is missing, or a code isn't in `availableLanguages` |

The change is audited as `LocalizationSettingChanged`, with before and after values.

---

## Audit

### GET `/notifications/audit` (V)

| Query | Notes |
|---|---|
| `action`, `targetType`, `targetId`, `actorUserId`, `from`, `to` | optional filters |
| `cursor`, `limit` | keyset pagination |

**200** items: `{ id, occurredAtUtc, actor: { userId, displayName } | null, action, targetType, targetId, outcome, details, correlationId }`. The log is read-only; no endpoint modifies or deletes audit rows.
