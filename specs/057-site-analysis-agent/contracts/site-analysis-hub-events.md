# Contract: Site Analysis Hub Events

**Spec**: SPEC-057 | **Requirements**: FR-008, FR-009, FR-012, FR-025, FR-026

Server → browser push for the chat-side half of delivery. The **panel** half continues to use the existing
`IPanelNotifier`/`PanelHub` unchanged; this hub exists only because chat has no SignalR hub of its own — it
streams over SSE per turn, so a background job has no way to make a message appear in an open conversation
(research D5).

## Hub

| | |
|---|---|
| Route | `/hubs/site-analysis` |
| Auth | Authenticated users only; access token supplied the same way every other hub in this codebase does |
| Delivery | To the **initiating user's** connections only (FR-012) |
| Application abstraction | `ISiteAnalysisNotifier` — Application never references SignalR (constitution §3) |

```csharp
public interface ISiteAnalysisNotifier
{
    Task ResultReceivedAsync(string userId, SiteAnalysisResultReceivedDto payload, CancellationToken ct = default);
    Task AnalysisCompletedAsync(string userId, SiteAnalysisCompletedDto payload, CancellationToken ct = default);
}
```

## Event: `SiteAnalysisResultReceived`

Fired once per **validated** finding, immediately after it is persisted. Never fired for a failed or rejected
result.

```jsonc
{
  "analysisId":   "0193f2a1-…",   // guid
  "resultId":     "0193f2a3-…",   // guid — idempotency key for the client
  "userChatId":   "0193f29e-…",   // guid — which conversation to post the notice into
  "analysisType": "SchematicImage",
  "noticeText":   "Receiving the schematic site map for Al Barsha South…",
  "generatedAtUtc": "2026-09-17T09:14:22Z"
}
```

Client behavior: render `noticeText` as an assistant message in `userChatId`, then open the finding's panel. The
panel arrives separately as a `PanelRequested` event on the existing panel hub; the two are correlated by
`resultId`, carried as the panel request id.

`resultId` is the idempotency key — a client that has already rendered it MUST ignore a repeat (SignalR
reconnection can redeliver).

## Event: `SiteAnalysisCompleted`

Fired **exactly once** per analysis (FR-027), when the last specialist settles.

```jsonc
{
  "analysisId":       "0193f2a1-…",
  "userChatId":       "0193f29e-…",
  "status":           "Completed",     // "Completed" | "Failed"
  "succeededCount":   1,
  "expectedCount":    1,
  "noticeText":       null             // null when nothing needs saying
}
```

`noticeText` is `null` when every specialist succeeded — the findings are their own report, and FR-024's quiet
is preserved. It carries copy only when some or all specialists failed:

| Situation | `status` | `noticeText` |
|---|---|---|
| All succeeded | `Completed` | `null` |
| Partial | `Completed` | e.g. "Site analysis finished — 4 of 5 completed." |
| None succeeded | `Failed` | e.g. "I couldn't complete the site analysis." |

The client renders `noticeText` as an assistant message when non-null, and does nothing when null.

## Ordering and reliability

- No ordering guarantee between a `SiteAnalysisResultReceived` and its `PanelRequested`. The client MUST NOT
  block one on the other; each is independently renderable.
- `SiteAnalysisCompleted` is always the last event for an analysis.
- Events are **not** a persistence mechanism. A disconnected client recovers by fetching
  `GET /api/v1/site-analyses/{id}` (see `site-analysis-api.md`) — that endpoint, not the hub, is what satisfies
  FR-017.

## Error handling (constitution §2 VIII)

Client-side: every hub handler wraps its rendering in an explicit error path, and a failure to render a finding
surfaces visibly to the user — never a console-only log, and never an unhandled promise rejection. A dropped
connection surfaces as a visible degraded state with the rehydration fetch as its recovery path.

Server-side: a push failure is logged with structured context and never rolls back the persisted result — the
finding stays retrievable (see `result-relay.md`).
