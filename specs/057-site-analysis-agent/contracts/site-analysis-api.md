# Contract: Site Analysis Retrieval API

**Spec**: SPEC-057 | **Requirements**: FR-016, FR-017, FR-018, FR-019

The rehydration path. This endpoint — not the SignalR hub — is what makes findings survive navigation and
reload (FR-017).

## `GET /api/v1/site-analyses/{id}`

Returns one analysis and all its settled results.

**Auth**: `[Authorize]`. Ownership enforced server-side in the query handler (FR-019): an analysis belonging to
another user returns **404**, not 403 — existence is not disclosed.

### 200 OK

```jsonc
{
  "id":            "0193f2a1-…",
  "userChatId":    "0193f29e-…",
  "siteName":      "Al Barsha South",
  "latitude":      25.0912,
  "longitude":     55.2094,
  "status":        "Completed",        // Running | Completed | Failed
  "expectedResultCount": 1,
  "startedAtUtc":  "2026-09-17T09:13:58Z",
  "completedAtUtc":"2026-09-17T09:14:22Z",
  "results": [
    {
      "id":             "0193f2a3-…",
      "analysisType":   "SchematicImage",
      "status":         "Completed",   // Completed | Failed | Rejected
      "dataSource":     "openai:gpt-image-1",
      "confidenceLevel":"Medium",
      "documentId":     "0193f2a4-…",
      "content":        { "blocks": [ /* content-block document */ ] },
      "completedAtUtc": "2026-09-17T09:14:22Z"
    }
  ]
}
```

Enums serialize as **strings** (existing API-wide converter). Do not compare numerically on the client.

### Field rules

- `results` includes only **settled** results — a specialist still running is simply absent. Compare
  `results.length` against `expectedResultCount` to know whether more are coming.
- `content` is populated only when `status` is `Completed`; it is `null` for `Failed`/`Rejected`.
- **`failureReason` is deliberately NOT exposed.** It is operator diagnostic detail (FR-022), persisted and
  logged, not part of the user-facing contract — consistent with FR-024's per-specialist silence. Operators read
  it from the record and the structured logs.
- `documentId` is a platform document id, rendered through the existing `/documents/{id}/download` path. No
  external URL ever appears here (FR-029).

### Errors (RFC 7807 Problem Details, constitution §6)

| Status | When |
|---|---|
| 401 | Unauthenticated |
| 404 | No such analysis, or it belongs to another user |
| 400 | `id` is not a valid identifier |

All carry `type`, `title`, `status`, `detail`, and the correlation id extension member.

## Client usage

On opening a conversation, the client fetches any analyses for that chat and reopens the panels of their
completed results. Requirements:

- Rehydrated content MUST render identically to the originally delivered panel (FR-018) — the same content
  document composed server-side is replayed, not recomposed.
- A `Running` analysis rehydrates its completed findings and continues listening on the hub for the rest.
- Fetch failures surface visibly to the user via TanStack Query's error state with a retry affordance, never a
  console-only failure (constitution §2 VIII, §4).

## Not in this contract

- No list-all endpoint. Rehydration is per-conversation and bounded; an unbounded analyses list is not a
  requirement of this release (constitution §6 forbids unbounded result sets).
- No delete endpoint. Findings live as long as their conversation (spec Assumptions).
- No re-run endpoint. A fresh analysis is requested through conversation, exactly like the first.
