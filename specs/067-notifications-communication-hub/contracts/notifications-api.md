# Contract: User Notifications API

**Base**: `/api/v1` | **Auth**: JWT bearer (all endpoints; FR-052) | **Rate limit**: `notifications-endpoints` (per user, 120/min) | **Errors**: RFC 7807 Problem Details with `traceId`
**Localized surface**: yes. `title`, `detail` and `errors` follow the caller's effective language (research R15).

All resources are scoped to the caller. A notification id that belongs to another user, or has been deleted by its owner, returns **404**.

---

## GET `/notifications`

This endpoint returns a keyset-paginated list, newest first (FR-014, FR-015, SC-004).

| Query | Type | Default | Notes |
|---|---|---|---|
| `cursor` | string | none | opaque cursor from the previous page's `nextCursor` |
| `limit` | int | 25 | 1–100 |
| `category` | `NotificationCategory` | all | may repeat: `?category=Agent&category=Workflow` |
| `state` | `all` \| `unread` \| `read` | `all` | |

**200**:
```json
{
  "items": [
    {
      "id": "0192…",
      "category": "Workflow",
      "type": "workflow.execution.failed",
      "title": "Workflow failed",
      "message": "\"Site survey import\" stopped at step 3.",
      "priority": "High",
      "status": "Delivered",
      "language": "en",
      "createdAtUtc": "2026-09-23T10:15:00Z",
      "readAtUtc": null,
      "expiresAtUtc": null,
      "action": { "label": "Open run", "route": "/workflows/executions/7b1…" },
      "relatedItem": { "type": "WorkflowExecution", "id": "7b1…", "available": true }
    }
  ],
  "nextCursor": "eyJjIjoi…"
}
```

- `title` and `message` are **plain text**. Clients MUST NOT render them as HTML (research R11).
- `relatedItem.available` is `false` when the item has been deleted. The client then shows "no longer available" instead of navigating (spec edge case).
- `action` is `null` when a type has no route.

## GET `/notifications/unread-count`

**200**: `{ "count": 12 }`. This is an index-only count (research R19). The client polls it only after a SignalR reconnect; otherwise it uses the pushed `unreadCountChanged` events.

## GET `/notifications/{id}`

**200**: the same item shape, plus `metadata` (a non-sensitive key/value object; FR-013). Returns **404** when not found, not owned, or deleted.

## POST `/notifications/{id}/actions/mark-read`

This call is idempotent. **204**. Returns **404** as above. The server pushes `notificationUpdated` and `unreadCountChanged` to the caller's other sessions.

## POST `/notifications/actions/mark-all-read`

| Body (optional) | Notes |
|---|---|
| `{ "category": "Document" }` | limits the operation to one category; omitted means all |

**200**: `{ "updated": 37 }`. This is a set-based update of the caller's rows only, followed by one `unreadCountChanged` push.

## DELETE `/notifications/{id}`

This is an owner soft delete (FR-016a). **204**. The notification disappears from the list, details, count and live updates, and the user can't restore it. Delivery and audit records are untouched. Returns **404** if it doesn't exist or isn't owned. Any category can be deleted, including approval and security notifications.

---

## GET `/users/me/notification-preferences`

This returns the **effective** preferences: the catalogue defaults merged with the user's overrides (FR-031–FR-034).

**200**:
```json
{
  "categories": [
    {
      "category": "Security",
      "channels": [
        { "channel": "InApp", "enabled": true, "locked": true },
        { "channel": "Email", "enabled": true, "locked": true }
      ],
      "frequency": "Immediate",
      "availableFrequencies": ["Immediate"]
    },
    {
      "category": "Memory",
      "channels": [
        { "channel": "InApp", "enabled": true, "locked": false },
        { "channel": "Email", "enabled": false, "locked": false }
      ],
      "frequency": "Immediate",
      "availableFrequencies": ["Immediate"]
    }
  ]
}
```

The response includes Security, Account, Agent, Workflow, Document, KnowledgeBase, Memory and System. Billing and Conversation are omitted until they are emitted.

## PUT `/users/me/notification-preferences`

Request:
```json
{ "changes": [ { "category": "Workflow", "channel": "Email", "enabled": false } ] }
```

- **Validation**: 1–40 changes. The category and channel must be known. `frequency`, when present, must be `Immediate`.
- **Mandatory pairs** (FR-032): if any change disables a mandatory pair, the request returns **422** with `errors["changes[i]"]` and nothing is applied, because the request is atomic.
- **200**: the full effective preferences, in the same shape as the GET.

---

## GET `/users/me/localization`

This endpoint returns the language state that drives the language switch and the frontend's `LocalizedSurface` (FR-044b).

**200**:
```json
{
  "localizationEnabled": true,
  "supportedLanguages": [ { "code": "en", "nativeName": "English" }, { "code": "ar", "nativeName": "العربية" } ],
  "preferredLanguage": "ar",
  "effectiveLanguage": "ar",
  "direction": "rtl"
}
```

When localization is disabled, the response is `supportedLanguages: [en]` and `effectiveLanguage: "en"`. `preferredLanguage` still echoes the retained choice, but the switch UI is hidden.

## PUT `/users/me/localization`

Request: `{ "preferredLanguage": "ar" }`

- **Validation**: returns **422** when localization is disabled or the code isn't in `supportedLanguages`.
- **200**: the same shape as the GET.
- **Effect**: the change applies to notifications rendered afterward, including ones still queued (spec edge case). Existing notifications keep their recorded language (FR-044c).

---

## Status codes summary

| Code | When |
|---|---|
| 200/204 | success |
| 400 | malformed cursor or unknown enum value |
| 401 | unauthenticated |
| 404 | not found, not owned, or owner-deleted |
| 409 | invalid state transition (rare, for example concurrent expiry) |
| 422 | validation: a mandatory preference, or an unsupported language |
| 429 | rate limited (`Retry-After` set) |
