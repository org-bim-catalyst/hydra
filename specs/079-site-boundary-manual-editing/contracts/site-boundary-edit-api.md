# Contract: Site Boundary Edit API

All routes are under the existing `ChatsController`:

- `[Authorize]`, rate policy `chat-endpoints`;
- ownership enforced by `ChatOwnershipGuard` in the Application handler;
- errors as RFC 7807 Problem Details with `traceId`;
- listed in the OpenAPI document with every status below.

A non-owner gets **404**, the same as a missing chat. This hides the chat's existence, as every
other chat route does.

Coordinates are WGS84 degrees. Rings are open: the first corner is not repeated at the end. If it
is repeated, it is dropped.

---

## `PUT /api/v1/chats/{chatId}/site-boundary`

Saves a hand edit of the outline in force (FR-015 – FR-020). It creates or updates the user's
site correction, links the chat, and appends the chat line.

### Request

```json
{
  "expectedRevision": "0199a0c4-…",
  "rings": [
    [ { "latitude": 23.5862, "longitude": 58.3921 }, { "latitude": 23.5871, "longitude": 58.3940 }, { "latitude": 23.5850, "longitude": 58.3948 } ],
    [ { "latitude": 23.5881, "longitude": 58.3955 }, "…" ]
  ]
}
```

- `rings[0]` is the ring holding the site. Its order must match the outline in force: the
  the ring count may change (amended: circle Add/Cut, see `actions/combine`).
- The validator (400) checks:
  - 1–20 rings;
  - 3–2,000 corners per ring and at most 5,000 in total;
  - finite coordinates within range;
  - (amended) the ring count is NOT required to match the outline in force: adding or cutting a circle may add a ring or split one.

### 200 OK

```json
{
  "activeBoundary": { "…ChatActiveBoundaryDto…": "", "source": "UserCorrected", "isHandEdited": true, "revision": "0199a0c5-…", "areaSquareMeters": 35210.4 },
  "message": { "id": "…", "role": "Assistant", "kind": "Text", "content": "You edited the outline of Muscat Grand Mall — now 35,210 m².", "createdAtUtc": "…" }
}
```

`activeBoundary` has the same shape as `ChatDetailDto.activeBoundary`, so the client stores it
directly and draws it with `SiteBoundaryRenderer`, which gives the animated border.

### Errors

| Status | When | Extensions |
|--------|------|------------|
| 400 | Validator failures | `errors` (field → messages) |
| 404 | Chat missing or not owned, or the chat has no outline | |
| 409 | `expectedRevision` ≠ the effective revision, or a row-version race | `currentRevision` |
| 422 | Geometry refused: self-crossing, area ≤ 1 m², duplicate corners, a ring not intersecting the found outline grown by 25 m, or union area > 3× the found area | `ringIndex`, `reason` (`selfCrossing` \| `degenerate` \| `duplicateCorner` \| `driftedAway` \| `tooLarge` \| `holeNotSupported` \| `nothingLeft` \| `tooManyRings`) |
| 429 | Rate limit | |

---

## `POST /api/v1/chats/{chatId}/site-boundary/actions/combine` (amendment)

A calculation only; nothing is saved. Adds a circle to the rings being edited (union) or cuts it out
(difference). A circle touching nothing becomes a new ring of its own; a cut that splits a ring
yields two rings.

### Request

```json
{ "rings": [[{ "latitude": 0, "longitude": 0 }]], "operation": "Add", "centre": { "latitude": 0, "longitude": 0 }, "radiusMeters": 40 }
```

`operation` is `Add` or `Cut`; `radiusMeters` is 1-5000.

### 200 OK

`{ "rings": [[…]] }` - open rings, the one holding the original first ring first. Slivers under 1 m² are dropped.

### Errors

400 validator; 404 chat missing or not owned; 422 with `reason` `holeNotSupported` (the result would contain a hole),
`nothingLeft` (the cut removes everything), `tooManyRings`, or any geometry reason of the save endpoint.

---

## `POST /api/v1/chats/{chatId}/site-boundary/actions/reset`

Goes back to the found outline (FR-027). It soft-deletes the user's correction, unlinks this
chat, and appends the chat line. Other chats linked to the same correction fall back to their own
found outline on their next read.

### Request

```json
{ "expectedRevision": "0199a0c5-…" }
```

### 200 OK

```json
{
  "activeBoundary": { "…found outline…": "", "isHandEdited": false, "revision": "0199a0c6-…" },
  "message": { "…": "", "content": "The outline of Muscat Grand Mall is back to the one I found — 34,065 m²." }
}
```

### Errors

| Status | When |
|--------|------|
| 404 | Chat missing or not owned, no outline, or the outline in force isn't hand-edited (FR-028) |
| 409 | Revision mismatch (`currentRevision` extension) |
| 429 | Rate limit |

---

## `GET /api/v1/chats/{chatId}` (existing): additive change

`activeBoundary` is now the **effective** outline, meaning the hand-edited rings when a live
correction is linked. It gains:

| Field | Type | Notes |
|-------|------|-------|
| `revision` | `string` (Guid) | Send as `expectedRevision` |
| `isHandEdited` | `boolean` | Drives the "hand-edited" badge, the Reset control, and suppressing the edit offer |

This is additive under `/api/v1` (§6 Versioning).

## Client (`chatsApi.ts`)

- `saveSiteBoundaryEdit(chatId, { expectedRevision, rings })` and `resetSiteBoundary(chatId,
  { expectedRevision })`. Both are TanStack Query mutations, with zod-validated responses.
- On success: update `activeSiteBoundaryStore`, append `message` to the chat's messages cache,
  and invalidate the chat detail query.
- On 409: set the session to `conflict`. On any other failure: set `error` with the Problem
  Details `detail`. Both are visible, and neither is silent (§2 VIII).
