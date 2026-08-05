# API Contract: Processing Status, Dashboard & Notifications

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `DocumentProcessingController` (`/api/v1/documents/processing`) plus a SignalR hub
(research.md Decision 7). Per-user endpoints scoped to the caller; admin endpoints additionally
require the existing administrator role (research.md Decision 11, mirrors
`AdminDashboardController`'s authorization).

## Processing status & history for one document

`GET /api/v1/documents/{id}/processing` → `DocumentProcessingStatusDto`:

```json
{
  "documentId": "...",
  "processingStatus": "Processing",
  "currentStage": "Ocr",
  "stages": [
    { "stageType": "Validation", "status": "Completed", "startedAtUtc": "...", "completedAtUtc": "..." },
    { "stageType": "Ocr", "status": "InProgress", "startedAtUtc": "...", "completedAtUtc": null },
    { "stageType": "TextExtraction", "status": "Pending", "startedAtUtc": null, "completedAtUtc": null }
  ],
  "failureReason": null
}
```

(FR-027, US2 AC1). `failureReason` is populated (and `processingStatus: "Failed"`) with a
specific, actionable message when the job stopped (FR-028).

`GET /api/v1/documents/{id}/processing/history` → the append-only
`DocumentProcessingLog` entries for this document, newest-first (FR-013, US2 AC5).

## Retry

`POST /api/v1/documents/{id}/processing/actions/retry` (FR-029). If the document's current job
is not in a `Failed` state, returns `409 Conflict` with `{ "reason": "NotInFailedState" }` rather
than starting a second, concurrent processing attempt (Edge Cases).

## Real-time push (SignalR)

Hub route: `/hubs/document-processing` (research.md Decision 7). On connect, the client joins a
group keyed by its own user id (server-assigned from the auth token, never client-supplied) so a
user only ever receives their own documents' events — administrators additionally join an
`admin-dashboard` group for the aggregate view.

Server → client events:

| Event | Payload | Fires when |
|---|---|---|
| `documentStageChanged` | `{ documentId, stageType, status }` | A `DocumentProcessingStage` transitions (US2 AC1). |
| `documentProcessingCompleted` | `{ documentId }` | `Document.ProcessingStatus` becomes `Completed`. |
| `documentProcessingFailed` | `{ documentId, failureReason }` | `Document.ProcessingStatus` becomes `Failed`. |

The client also polls `GET /api/v1/documents/{id}/processing` on a 5-second interval via
TanStack Query as a reconciliation fallback (research.md Decision 7) — a missed push event is
never the sole source of truth.

## Dashboard (per-user)

`GET /api/v1/documents/dashboard` → `DocumentDashboardSummaryDto`:

```json
{
  "queueDepth": 3,
  "inProgressCount": 2,
  "completedTodayCount": 14,
  "failedCount": 1,
  "retryQueue": [ { "documentId": "...", "fileName": "...", "failureReason": "..." } ],
  "statistics": {
    "totalDocuments": 128,
    "totalStorageBytes": 943718400,
    "averageProcessingDurationMs": 41230,
    "fileTypeDistribution": { "Pdf": 60, "Docx": 40, "Png": 28 },
    "languageDistribution": { "en": 100, "ar": 28 }
  }
}
```

(FR-045, FR-046, US6 AC1). Backed by `DocumentStatistics` (`Scope: User`), refreshed on the
interval described in data-model.md, satisfying SC-011's 5-second accuracy budget.

## Dashboard (organization-wide, administrator only)

`GET /api/v1/documents/dashboard/organization` — same shape as above, but aggregated across all
users' documents (`DocumentStatistics`, `Scope: Organization`). Requires the administrator role;
a non-administrator caller receives `403 Forbidden` (FR-045a, US6 AC6). This endpoint never
returns individual document content or per-user document listings — only aggregate counts and
statistics (FR-045a's "does not itself grant... access to open, download, or edit").

## Notifications

No platform-wide in-app notification mechanism exists yet to reuse (data-model.md
`DocumentNotification` note) — this feature ships a minimal, feature-scoped one.

`GET /api/v1/documents/notifications?unreadOnly=false&cursor=...&pageSize=50` → paginated
`DocumentNotificationDto[]` (`id, documentId, eventType, message, isRead, createdAtUtc`),
newest-first.

`POST /api/v1/documents/notifications/{id}/actions/mark-read`

Server → client SignalR event (same hub as `document-processing-api.md`'s processing events):

| Event | Payload |
|---|---|
| `notificationCreated` | `{ id, documentId, eventType, message, createdAtUtc }` |

Fired for all six event types (FR-047): `UploadCompleted`, `ProcessingCompleted`,
`ProcessingFailed`, `OcrFailed`, `VersionCreated`, `StorageLimitReached`. The client renders it as
a toast on receipt and adds it to the notification inbox; the inbox query above is the fallback
for anything missed while disconnected (same reconciliation principle as research.md Decision 7).
