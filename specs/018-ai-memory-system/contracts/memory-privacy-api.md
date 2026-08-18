# API Contract: Memory Privacy & Preferences

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Sub-resources of `/api/v1/memories`, same `MemoriesController`/`memory-endpoints` rate limit as
[memories-api.md](./memories-api.md). Account-level and category-level controls (FR-007, FR-022–
FR-026, User Story 4).

## Get preferences

`GET /api/v1/memories/preferences` → `MemoryPreferencesDto`:

```json
{
  "memoryEnabled": true,
  "categories": [
    { "category": "UserPreference", "approvalMode": "Automatic", "isEnabled": true },
    { "category": "PersonalFact", "approvalMode": "Automatic", "isEnabled": true },
    { "category": "ProjectContext", "approvalMode": "Automatic", "isEnabled": true },
    { "category": "ConversationDerived", "approvalMode": "Automatic", "isEnabled": true }
  ]
}
```

(FR-007, FR-022, FR-025). Rows are materialized with defaults on first access if not already present
(data-model.md `MemoryCategoryPreference`).

## Update preferences

`PUT /api/v1/memories/preferences`

```json
{
  "memoryEnabled": true,
  "categories": [
    { "category": "ProjectContext", "approvalMode": "Manual", "isEnabled": true }
  ]
}
```

(FR-007, FR-022, FR-025). `categories` entries are partial updates — only listed categories change;
omitted categories keep their current settings. Setting `memoryEnabled: false` takes effect
immediately for future conversations (existing memories are retained, not deleted — FR-022). `204 No
Content` on success.

## Clear all memories

`POST /api/v1/memories/actions/clear-all`

```json
{ "confirm": true }
```

(FR-023, User Story 4 AC2, SC-003). Requires `confirm: true` — an explicit confirmation step, not a
bare `DELETE`, since this is irreversible. `202 Accepted` (the underlying purge may be processed
asynchronously for large memory counts, but the effect — memories excluded from all future use — is
guaranteed immediate at the point of response, matching FR-022's "immediate effect" framing; the
`202` reflects the physical row-deletion work, not a delay in the user-visible outcome).

## Export memories

`POST /api/v1/memories/actions/export` → `202 Accepted` with `{ "exportJobId": "..." }`, then

`GET /api/v1/memories/exports/{exportJobId}` → `{ "status": "Ready", "downloadUrl": "https://.../signed-url?..." }`

(FR-024, User Story 4 AC3, research.md Decision 14). A complete, human-readable JSON file grouped by
category, served via a signed, expiring URL — never a direct physical file path (CLAUDE.md File
Management convention). An account with zero memories still produces a valid, empty export (spec.md
Edge Cases), not an error.

## Notifications (FR-006a signal)

`GET /api/v1/memories/notifications?cursor=&pageSize=20` → paginated `MemoryNotificationDto[]`
(`id, memoryId, eventType, message, createdAtUtc, readAtUtc`), newest-first, scoped to the caller.

`POST /api/v1/memories/notifications/{id}/actions/mark-read` → `204 No Content`.

(FR-006a, research.md Decision 11). Also pushed live via the `memoryNotificationCreated` SignalR
event on `/hubs/memory` (research.md Decision 11) — this endpoint is the reconciliation/poll
fallback for a missed live event, same dual-path convention as the existing document-processing
notification hub.
