# Contract: Funnel/CTA Analytics Event Endpoint

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md) (`FunnelAnalyticsEvent`) | **Requirement**: FR-021

## `POST /api/v1/analytics/funnel-events`

Records a single consent-gated funnel/CTA analytics event (research.md Topic 4). Anonymous-allowed by explicit, reviewed opt-in (constitution §6); rate-limited (constitution §6/§8, research.md Topic 8).

### AuthN/AuthZ

- `[AllowAnonymous]` — this endpoint MUST be reachable by signed-out visitors on the landing/auth pages. Also callable by authenticated sessions (e.g. the `FunnelCompleted` event fired at the moment of redirect into the workspace, which may race with token issuance) — no `[Authorize]` requirement either way.
- Rate limit: fixed-window, partitioned per client IP, matching the convention of other anonymous endpoints already registered in `Program.cs`.

### Request

```
POST /api/v1/analytics/funnel-events
Content-Type: application/json

{
  "eventType": "CtaClicked" | "FunnelCompleted",
  "ctaId": "SignIn" | "SignUp" | "TryPlatform",     // required iff eventType = CtaClicked
  "funnelType": "SignUp" | "SignIn",                 // required iff eventType = FunnelCompleted
  "sessionId": "b3f1c2b0-...-guid",                   // client-generated, ephemeral, not tied to UserId
  "occurredAtUtc": "2026-08-16T14:32:01.000Z"
}
```

No cookies, headers, or fields carrying PII are required or accepted. The frontend MUST NOT call this endpoint unless `PublicConsentState.analytics === true` (or, for an authenticated visitor, their existing consent preferences already permit analytics) — this is enforced client-side by `useFunnelAnalytics()`; the endpoint itself does not re-verify consent (research.md Topic 2/4 — standard trust boundary for anonymous telemetry).

### Responses

| Status | When |
|---|---|
| `202 Accepted` | Event accepted and logged. No response body needed — this is fire-and-forget telemetry (plan.md Constitution Check, Principle VIII note). |
| `400 Bad Request` (Problem Details, RFC 7807) | `eventType`/`ctaId`/`funnelType` fails the closed-enum validation, the required companion field for the given `eventType` is missing, `sessionId` isn't a valid GUID, or `occurredAtUtc` is outside the accepted clock-skew/staleness window. |
| `429 Too Many Requests` | Rate limit exceeded for the calling IP. |

The frontend's `useFunnelAnalytics()` hook treats **any** non-2xx response (or a network failure) as non-fatal: caught, logged client-side at `warn`, and never surfaced to the user or allowed to block/delay the CTA's real navigation action (plan.md Constitution Check, Principle VIII).

### Backend flow

`AnalyticsController.RecordFunnelEvent` (thin, `Controllers/v1/AnalyticsController.cs`) → dispatches `RecordFunnelEventCommand` via `ISender` → `RecordFunnelEventCommandHandler` validates (via the FluentValidation pipeline behavior, not inline) and calls `ILogger<RecordFunnelEventCommandHandler>.LogInformation` with the named properties from `data-model.md`'s `FunnelAnalyticsEvent` shape → returns `202 Accepted`. No repository, no `DbContext`, no Unit of Work — nothing to commit.
