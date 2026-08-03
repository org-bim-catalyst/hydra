# API Contract: Voice Provider Health (admin)

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md) |
**Research**: [../research.md](../research.md) Decision 5

New admin-only action, extending the existing provider-health admin surface from
specs/005-multi-provider-ai-engine / specs/007-admin-ai-provider-ui (FR-039/SC-011) rather
than introducing a new admin page — reuses the existing component library and access-control
convention, not a bespoke voice-specific admin feature.

## View voice provider failover activity

`GET /api/v1/ai/voice/health`

`[Authorize(Roles = "Administrator")]` (same authorization convention as the existing
provider-health admin endpoints from spec 005) — not on the general `ai-endpoints` policy
since this is an admin read, not an AI-invoking call.

Query parameters: `from` (ISO 8601, optional, default = last 24h), `to` (optional, default =
now) — same pagination/filtering convention as other admin list endpoints (constitution §6).

Response (`200 OK`):
```json
{
  "currentStatus": "healthy",
  "failoverCount": 3,
  "recoveryCount": 3,
  "events": [
    {
      "occurredAtUtc": "2026-08-02T09:41:12Z",
      "direction": "FailedOverToFallback",
      "reason": "stt-session request timed out"
    },
    {
      "occurredAtUtc": "2026-08-02T09:43:50Z",
      "direction": "RecoveredToPrimary",
      "reason": null
    }
  ]
}
```

`currentStatus` is derived, not stored: `degraded` if the most recent event in the requested
window is a `FailedOverToFallback` with no subsequent `RecoveredToPrimary`, `healthy`
otherwise — giving an administrator the same at-a-glance signal `ProviderHealthCheck`-backed
chat-provider health already provides (FR-039: "so repeated failovers can be identified as a
possible primary-provider outage without a user needing to report it").

This response deliberately does **not** include `userId` per event in the default payload
(aggregate view for spotting a platform-wide outage) — an admin drilling into "is this one
user's network or everyone's" is a `/speckit-tasks`-time UI decision (e.g., a `groupBy=user`
query parameter), not a new contract; the underlying `VoiceProviderFailoverEvent.UserId`
column already supports it when needed.
