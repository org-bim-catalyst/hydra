# API Contract: Chat Detail (current provider/model)

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Extends the existing `/api/v1/chats` resource
(`src/AskLucy.Web/Controllers/v1/ChatsController.cs`) with the one route it is currently
missing: fetching a single chat's own detail. `[Authorize]`, scoped to the caller's own
chats via `ChatOwnershipGuard` (same pattern as `PATCH /{id}/model-selection`). Errors
follow RFC 7807 Problem Details (constitution §6).

## Get chat detail

`GET /api/v1/chats/{id}`

Response: `200 OK`

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Steel connection tolerances",
  "providerId": "b1f0c1a2-...-000000000001",
  "modelId": "b1f0c1a2-...-000000000002",
  "activeLocation": {
    "latitude": 25.1558, "longitude": 55.2218, "locationName": "Al Safa Park 2", "confidence": 0.9,
    "confidenceLevel": "high"
  },
  "activeBoundary": {
    "siteName": "Al Safa Park 2",
    "centroid": { "latitude": 25.156, "longitude": 55.222 },
    "polygon": [{ "latitude": 25.15, "longitude": 55.22 }, "..."],
    "areaSquareMeters": 15146.14,
    "confidence": 0.92,
    "confidenceLevel": "high",
    "source": "OsmBoundary",
    "sourceDetail": "OpenStreetMap (leisure=park)"
  }
}
```

- `providerId`/`modelId` are `null` when the conversation has never had a model selection
  persisted (e.g., a brand-new chat before the first message is sent) — FR-004's edge case
  ("no conversation currently open" / "nothing to change yet") is distinguished client-side
  by the *absence* of an `activeChatId` at all (research.md Decision 1), not by this field
  being null; a chat that exists but has no selection yet still returns `200` with null
  ids, and the current-conversation control renders its own "choose a model" empty state,
  consistent with how the in-chat switcher already behaved before relocation.
- `activeLocation`/`activeBoundary` (added 2026-09-25) — the site the conversation last
  confirmed and outlined (`UserChat.ActiveLocation`/`ActiveBoundary`, specs/037/042), `null` when
  it has none. `activeBoundary` has the same shape as the live `__SITE_BOUNDARY__` event, with
  `confidenceLevel` lower-case, minus `alternativeCandidateNames` (not persisted). The client
  (`useRestoreChatSite`) puts them back on the viewer when the chat is opened, unless the viewer
  already shows a site Lucy confirmed this session.
- `404 Not Found` (Problem Details) if the chat does not exist or does not belong to the
  caller — identical semantics to every other single-chat route on this controller.

## Update current conversation's model selection (existing, unchanged)

`PATCH /api/v1/chats/{id}/model-selection` — no contract change. Continues to accept
`{ providerId, modelId, generationParameters? }` and return `204 No Content`
(`UpdateChatModelSelectionCommand`, specs/005-multi-provider-ai-engine FR-009). The
relocated current-conversation control in Chat Configuration calls this exact endpoint.

## Everything else (unchanged)

All other `/api/v1/chats` routes — list/search, create, rename, delete, archive/pin/
favorite (+ inverses), duplicate, clear, purge, restore, export, messages, memory-references,
project assignment, prompt insertion — are unmodified by this feature. The relocated Chat
History Settings tab calls `GET /api/v1/chats` (search/list) and the action routes exactly
as the in-workspace conversation list already does today.
