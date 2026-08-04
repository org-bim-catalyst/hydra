# API Contract: Knowledge Bases

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `KnowledgeBasesController`, `[Authorize]`, `[EnableRateLimiting("knowledge-base-endpoints")]`
(new policy, non-AI-cost-tiered, same generous shape as `chat-endpoints`). Route base:
`/api/v1/knowledge-bases`. Shape mirrors `ChatsController` (specs/002-chat-history-management)
exactly, since it is the closest-matching existing feature (search/create/rename/archive/
restore/pin/favorite/duplicate/purge/export). Every response is scoped to the caller's own
knowledge bases; a knowledge base the caller doesn't own returns `404`, identical to a
nonexistent one (FR-010, `KnowledgeBaseOwnershipGuard`).

## List / search the caller's knowledge bases

`GET /api/v1/knowledge-bases`

Query parameters:

| Param | Type | Notes |
|---|---|---|
| `view` | `Active` \| `Archived` \| `Deleted` (default `Active`) | FR-023, FR-027. `Active` means "not archived, not soft-deleted" — i.e. it includes both `Status: Draft` and `Status: Active` knowledge bases, so a newly-created Draft knowledge base appears immediately on the default dashboard view (spec.md User Story 1, Acceptance Scenario 1). `Archived` means `Status: Archived` (soft-deleted excluded). `Deleted` shows the caller's own soft-deleted (not-yet-purged) knowledge bases regardless of their prior `Status`. |
| `q` | `string?` | FR-022 — matches name, description, tags only (not owner/dates — those are sort-only, FR-024). |
| `categoryId` | `guid?` | FR-023. |
| `tag` | `string?` | FR-023 — single tag filter; repeatable query param for multiple (`&tag=a&tag=b`, all must match). |
| `favorite` | `bool?` | FR-023/FR-027. |
| `pinned` | `bool?` | FR-023/FR-027. |
| `sort` | `Name` \| `RecentlyUpdated` \| `Created` \| `DocumentCount` \| `StorageSize` (default `RecentlyUpdated`) | FR-024. |
| `sortDescending` | `bool` (default varies by `sort`) | FR-024. |
| `cursor` | `string?` | Opaque pagination cursor (constitution §6). |
| `pageSize` | `int` (default 50, max 200) | FR-034. |

Response (`200 OK`): `PagedResult<KnowledgeBaseSummaryDto>` —

```json
{
  "items": [
    {
      "id": "...",
      "name": "BIM Standards",
      "description": "...",
      "status": "Active",
      "color": "#4F46E5",
      "icon": "folder-open",
      "categoryId": "...",
      "categoryName": "Engineering",
      "tags": ["revit", "standards"],
      "isFavorite": true,
      "isPinned": false,
      "documentCount": 42,
      "totalPageCount": 812,
      "storageSizeBytes": 15728640,
      "createdAtUtc": "...",
      "lastUpdatedAtUtc": "..."
    }
  ],
  "nextCursor": null
}
```

An empty `items` array (with a normal `200`) is the contract for "no results" — the frontend
renders the empty state (FR spec Edge Cases), never treats it as an error.

## Dashboard summary

`GET /api/v1/knowledge-bases/dashboard-summary`

Cached per-user (research.md Decision 7, 60s TTL, invalidated on mutation). Response
(`200 OK`):

```json
{
  "totalKnowledgeBases": 128,
  "totalDocuments": 4310,
  "totalStorageBytes": 5368709120,
  "recentCount": 5,
  "favoritesCount": 12,
  "pinnedCount": 3,
  "archivedCount": 20
}
```

## "Recent" dashboard section

FR-027's Recent section is `GET /api/v1/knowledge-bases?view=Active&sort=RecentlyUpdated&pageSize=N`
— the same search endpoint as above, not a separate resource or query. There is no dedicated
"recent items" endpoint; `dashboard-summary.recentCount` (above) is a display count only, not
a list.

## Create

`POST /api/v1/knowledge-bases`

Request:

```json
{ "name": "BIM Standards", "description": "...", "color": "#4F46E5", "icon": "folder-open",
  "categoryId": null, "tags": [] }
```

`201 Created` with the new `KnowledgeBaseSummaryDto` (`status: "Draft"`, per FR-002). `400` on
missing/blank `name` (FR-001/FR-007).

## Get, edit, delete

- `GET /api/v1/knowledge-bases/{id}` → `200` `KnowledgeBaseDetailDto` (adds `notes`,
  `folderCount`, `ownerId`) or `404`.
- `PATCH /api/v1/knowledge-bases/{id}` → partial update of `name`/`description`/`color`/
  `icon`/`categoryId`/`tags`/`notes` (FR-003); `200` with the updated summary. Omitted fields
  are left unchanged (same partial-update semantics as spec 005's preferences endpoint).
- `DELETE /api/v1/knowledge-bases/{id}` → soft delete (FR-005); `204`. Sets
  `PurgeScheduledAtUtc` to +30 days (FR-036).

## Lifecycle actions

All `POST`, all return `200` with the updated `KnowledgeBaseSummaryDto` unless noted, all
`404` if not found/not owned:

| Endpoint | Effect |
|---|---|
| `POST /{id}/actions/activate` | `Draft` → `Active` (research.md Decision 1). `409` if not currently `Draft`. |
| `POST /{id}/actions/archive` | `Active` → `Archived` (FR-004). `409` if not currently `Active`. |
| `POST /{id}/actions/restore` | Clears soft-delete **or** un-archives — restores to whatever `Status` already holds (FR-004, research.md Decision 2); also cancels a pending purge if the knowledge base was soft-deleted (spec.md Edge Cases). |
| `POST /{id}/actions/favorite` / `unfavorite` | FR-028. Idempotent. |
| `POST /{id}/actions/pin` / `unpin` | FR-028. Idempotent. |
| `POST /{id}/actions/duplicate` | FR-032/FR-037 — deep copy (folder tree + independent physical file copies). Returns `201 Created` with the new knowledge base (`status: "Draft"`, its own fresh id, name `"Copy of {original}"`). |

## Permanent deletion

`DELETE /api/v1/knowledge-bases/{id}/actions/purge`

Request body (mirrors `PurgeUserChatCommand`'s confirm-flag shape):

```json
{ "confirm": true }
```

`400` if `confirm` is not `true` (FR-036, constitution §2.VIII — confirmation enforced at the
Application boundary, not only in the UI). `409` if the knowledge base is not currently
soft-deleted (spec.md Edge Cases — "must be soft-deleted first"). `204` on success — cascades
to permanently delete every associated document's file (FR-036, data-model.md) and records
`KnowledgeBaseAuditLog` (`Action: PermanentlyDeleted`) before those file deletions run.

## Export metadata

`GET /api/v1/knowledge-bases/{id}/export`

Downloads a structured JSON file (FR-033, spec.md Assumptions) containing name, description,
category, tags, folder structure (names + hierarchy, not document contents), statistics, and
notes — mirrors `ChatsController.Export`'s `File(...)` response shape. Content-Disposition
filename is the sanitized knowledge base name + `.json`.
