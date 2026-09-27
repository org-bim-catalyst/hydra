# Contract: `NotificationHub` (SignalR)

**Endpoint**: `/hubs/notifications` | **Auth**: `[Authorize]`, using the same cookie-delivered access token as the other hubs (this avoids the frozen-token bug fixed in 1205b21) | **Direction**: server → client only (research R1)

## Connection

- `OnConnectedAsync` adds the connection to the group `user:{userId}`. There is no admin group, because admin screens poll their REST views.
- The hub has **no client-invocable methods**. Every mutation goes through the REST API ([notifications-api.md](notifications-api.md)), so rate limiting, validation and audit apply uniformly.
- Pushes are **best-effort**. The notification center is the source of truth: on `onreconnected` the client MUST refetch `GET /notifications/unread-count` and invalidate the first page of the list (edge case: offline users lose nothing).

## Server → client events

### `notificationCreated`

This event is sent after the notification's row commits (research R3). It fires only for `ShowInCenter = true` notifications.

```json
{
  "id": "0192…",
  "category": "Document",
  "type": "document.processing.completed",
  "title": "Document ready",
  "message": "\"Tower A specs.pdf\" finished processing.",
  "priority": "Normal",
  "createdAtUtc": "2026-09-23T10:15:00Z",
  "action": { "label": "Open document", "route": "/documents/…" },
  "unreadCount": 13
}
```

The item has the same shape as the list items in `GET /notifications`, plus the new `unreadCount`. The client prepends the item to the cached first page, updates the badge, and for `High` and `Critical` priority raises a polite toast and a screen-reader announcement (FR-020).

### `notificationUpdated`

Sent when a notification is read or deleted in another session, or when it expires.

```json
{ "id": "0192…", "change": "Read" | "Deleted" | "Expired", "unreadCount": 12 }
```

### `unreadCountChanged`

Sent after mark-all-read, and after bulk expiry or retention changes.

```json
{ "unreadCount": 0 }
```

## Removed events (FR-009a)

These events are removed in this release, and so are their frontend handlers:

| Hub | Event | Replacement |
|---|---|---|
| `DocumentProcessingHub` | `notificationCreated` | `NotificationHub.notificationCreated` |
| memory hub (`IMemoryNotifier` push) | `notificationCreated` | `NotificationHub.notificationCreated` |

`DocumentProcessingHub` keeps its **stage and progress** events unchanged. Those are live progress updates, not notifications.

## Failure semantics

- A push that throws is logged at Warning with the correlation id and the notification id. It does not fail materialization and is not retried, because the reconnect refetch covers the gap. The failure is captured in the log, so it is not a silent failure under principle VIII.
- Fan-out pushes for announcements are sent per user group in batches of 500, the same as materialization.
