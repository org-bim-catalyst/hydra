# API Contract: Chat History & Conversation Management

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Extends the existing `/api/v1/chats` resource (`src/AskLucy.Web/Controllers/v1/ChatsController.cs`)
rather than introducing a parallel `/conversations` resource — consistent with
research.md Topic 1. All endpoints are `[Authorize]`; every response is implicitly
scoped to the caller's own conversations (FR-026/FR-027). Errors follow RFC 7807 Problem
Details (constitution §6). Non-CRUD state changes are modeled as sub-resource actions
(constitution §6), under `/actions/{verb}`.

## List & discover conversations

`GET /api/v1/chats`

Query parameters (constitution §6 — documented, server-validated, never free-form
query-to-SQL):

| Param | Values | Notes |
|---|---|---|
| `view` | `active` (default) \| `archived` \| `deleted` \| `all` | `deleted` bypasses the soft-delete filter for the Recently Deleted view (FR-020); `active` excludes archived and deleted. |
| `pinned` | `true` | Filter to pinned only (FR-020) |
| `favorite` | `true` | Filter to favorites only (FR-020) |
| `q` | string | Free-text search across title + message content (FR-019); matches messages sent up to a few seconds ago (research.md Topic 5) |
| `sort` | `newest` \| `oldest` \| `recently-updated` \| `alphabetical` | (FR-021); pinned conversations always sort ahead of unpinned regardless of `sort` (FR-008) |
| `cursor` | opaque string | Keyset pagination cursor (FR-022, research.md Topic 6) |
| `pageSize` | int, default/max per config | Page size |

Response: `200 OK`, `PagedResult<ConversationSummaryDto>` — `items[]` (each carrying id,
title, timestamps, archived/pinned/favorite/deleted state, provider/model last used) plus
`nextCursor`.

## Create, rename, standard delete (existing, unchanged)

- `POST /api/v1/chats` — create (FR-001). Title may be omitted; auto-title generation
  fills it in on the first exchange (FR-013) if not manually set.
- `PATCH /api/v1/chats/{id}` — rename (FR-002); sets `IsTitleManuallySet=true` so
  auto-title generation no longer overwrites it (FR-014). Rejects blank/whitespace title
  (`400`).
- `DELETE /api/v1/chats/{id}` — regular delete (FR-003): sets `DeletedAtUtc`; the
  conversation now appears only under `view=deleted`.

## Trash (Recently Deleted) actions

- `POST /api/v1/chats/{id}/actions/restore` — restores from either Archived or Recently
  Deleted back to Active, preserving prior pin/favorite state (FR-005a/FR-007). `404` if
  the conversation is not the caller's or does not exist.
- `DELETE /api/v1/chats/{id}/actions/purge` — **permanent delete** (FR-004/FR-005).
  Request body: `{ "confirm": true }` — the handler rejects (`400`) unless `confirm` is
  explicitly `true`, enforcing confirmation at the API boundary in addition to any
  client-side confirmation UI. Hard-deletes the row and cascades to `Message`/
  `Attachment`/`Citation`, via the constitution's existing GDPR-erasure-style audited
  hard-delete command (research.md Topic 2). Irreversible; logged as a security/audit
  event (FR-028).

## Archive / Pin / Favorite actions

- `POST /api/v1/chats/{id}/actions/archive` — sets `ArchivedAtUtc` (FR-006).
- `POST /api/v1/chats/{id}/actions/pin` — sets `PinnedAtUtc = now` (FR-008).
- `POST /api/v1/chats/{id}/actions/unpin` — clears `PinnedAtUtc`.
- `POST /api/v1/chats/{id}/actions/favorite` — sets `IsFavorite = true` (FR-009).
- `POST /api/v1/chats/{id}/actions/unfavorite` — sets `IsFavorite = false`.

All five return `200 OK` with the updated `ConversationSummaryDto`. A `RowVersion`
mismatch (concurrent edit) returns `409 Conflict` Problem Details (research.md Topic 10).

## Duplicate

`POST /api/v1/chats/{id}/actions/duplicate` — creates a new conversation containing a
full copy of the source's messages as of the call (FR-010, research.md Topic 3). Returns
`201 Created` with the new `ConversationSummaryDto`; `Location` header points at the new
conversation.

## Clear messages

`POST /api/v1/chats/{id}/actions/clear` — request body: `{ "confirm": true }` (`400` if
missing/false, mirroring the permanent-delete confirmation pattern). Deletes all
`Message`/`Attachment`/`Citation` rows under the conversation; the conversation and its
title remain (FR-011). Returns `204 No Content`.

## Messages

`GET /api/v1/chats/{id}/messages` — existing endpoint, extended with cursor-based
pagination (research.md Topic 6): `cursor`/`pageSize` query params, response becomes
`PagedResult<MessageDto>`. `MessageDto` gains `provider`, `model`, `generationParameters`,
`inputTokenCount`, `outputTokenCount`, `attachments[]`, `citations[]` (FR-016/FR-017).

## Export

`GET /api/v1/chats/{id}/export` — returns `200 OK`, `application/json`,
`Content-Disposition: attachment`, body per the schema in research.md Topic 7: title,
timestamps, ordered messages with attachment/citation references (not embedded file
content, per the export clarification). Works for a conversation with zero messages
(FR-025, empty-history edge case) — returns a valid, empty `messages: []` array rather
than an error.

## Security & error shape (applies to every endpoint above)

- `401` if unauthenticated (FR-027).
- `404` if the conversation does not exist or does not belong to the caller — the
  existing `ChatOwnershipGuard` convention deliberately does not distinguish "not yours"
  from "doesn't exist" (avoids leaking existence of other users' data), unchanged by this
  feature (FR-026); repeated denials are logged as a security event (FR-028).
- All error bodies are RFC 7807 Problem Details (constitution §6) — no ad hoc `{ "error"
  ": "..." }` shapes.
