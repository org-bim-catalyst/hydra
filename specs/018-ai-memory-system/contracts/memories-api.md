# API Contract: Memories (Memory Center)

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `MemoriesController` (`/api/v1/memories`). Rate-limited via `memory-endpoints` (research.md
Decision 17). `[Authorize]` by default; every response is scoped to the caller's own memories — a
request naming a memory the caller does not own returns `404` (never `403`, to avoid confirming
existence of another user's memory, matching FR-027/§8 least-information-disclosure practice).

## List / search memories

`GET /api/v1/memories?category=&state=&projectId=&query=&cursor=&pageSize=50`

(FR-017, FR-018, User Story 2). Cursor-paginated (constitution §6). `category` filters by
`MemoryCategory`; `state` by `MemoryLifecycleState`; `projectId` (or the literal `general` to mean
"no project") scopes to one Project; `query` is a free-text search over `Content`.

Response → `MemoryListItemDto[]`:

```json
{
  "results": [
    {
      "id": "...",
      "category": "PersonalFact",
      "content": "Works on BIM coordination for a mechanical contractor",
      "state": "Active",
      "isSensitive": false,
      "projectId": null,
      "projectName": null,
      "sourceType": "PassiveConversationAnalysis",
      "sourceConversationId": "...",
      "importance": 0.72,
      "confidence": 0.9,
      "lastReinforcedAtUtc": "2026-08-08T14:03:00Z",
      "createdAtUtc": "2026-07-20T09:11:00Z"
    }
  ],
  "nextCursor": null,
  "totalCount": 1
}
```

(FR-017, US2 AC1). Each item shows exactly the fields FR-017 lists: content, category, source,
creation date, and lifecycle state.

## Get one memory (with history)

`GET /api/v1/memories/{id}` → `MemoryDetailDto`:

```json
{
  "id": "...",
  "category": "PersonalFact",
  "content": "Works on BIM coordination for a mechanical contractor",
  "state": "Active",
  "isSensitive": false,
  "projectId": null,
  "importance": 0.72,
  "confidence": 0.9,
  "history": [
    {
      "previousContent": "Works in BIM",
      "changeReason": "UserEdit",
      "changedAtUtc": "2026-08-01T10:00:00Z",
      "changedByActor": "user"
    }
  ],
  "openConflict": null
}
```

(FR-009, FR-019, User Story 2 AC2, User Story 6 AC3). `openConflict`, when present, carries the
`MemoryConflict` fields needed to render the asynchronous confirmation prompt (FR-016, clarified
2026-08-09).

## Edit a memory

`PUT /api/v1/memories/{id}`

```json
{ "content": "Works on BIM coordination for a mechanical contractor in Chicago" }
```

(FR-019, US2 AC2). Appends a `MemoryVersion` (`ChangeReason: UserEdit`), keeps `State` unchanged.
`204 No Content` on success.

## Delete a memory

`DELETE /api/v1/memories/{id}`

(FR-020, US2 AC3). Soft-deletes; immediately excluded from all future retrieval/ranking. `204 No
Content`.

## Approve / reject a pending candidate

`POST /api/v1/memories/{id}/actions/approve`
`POST /api/v1/memories/{id}/actions/reject`

(FR-021, User Story 3 AC2/AC3). Only valid while `State ∈ {Candidate, PendingApproval}` — otherwise
`409 Conflict`. `204 No Content` on success.

## Resolve an ambiguous conflict

`POST /api/v1/memories/{id}/actions/resolve-conflict`

```json
{ "resolution": "KeepNew" }
```

(FR-016, User Story 6 AC2, clarified 2026-08-09). `resolution` is one of `KeepExisting`, `KeepNew`,
`KeepBoth`. Only valid when the memory has an open `MemoryConflict` (`PendingUserConfirmation`) —
otherwise `409 Conflict`. On success the conflict's `ResolutionStatus` updates and the resolved
memory/memories become eligible for retrieval again.

## Why does Lucy know this (usage trace)

`GET /api/v1/chats/{chatId}/messages/{messageId}/memory-references` → `MemoryReferenceDto[]`:

```json
[
  {
    "memoryId": "...",
    "content": "Works on BIM coordination for a mechanical contractor",
    "relevanceScore": 0.83
  }
]
```

(FR-014, User Story 1). `content` is `MemoryReference.ContentSnapshot` — the trace remains
meaningful even if the source memory was later edited or deleted.
