# API Contract: Readiness Health Check — `/health/ready`

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md) | Implements FR-012.

New endpoint. Does not modify `/health` (liveness, `Program.cs:588`, unchanged).

## Check readiness

`GET /health/ready`

No authentication required (matches the existing `/health` endpoint's convention —
consumed by deployment/orchestration tooling, not end users).

**Response `200 OK`** — no migrations pending:

```json
{
  "status": "Healthy",
  "checks": [
    { "name": "pending-migrations", "status": "Healthy", "description": null }
  ]
}
```

**Response `503 Service Unavailable`** — one or more migrations pending (this is the
signal FR-012 exists to produce — it means the deployed schema has drifted from what the
application code expects, the exact condition that caused Bug 1):

```json
{
  "status": "Unhealthy",
  "checks": [
    {
      "name": "pending-migrations",
      "status": "Unhealthy",
      "description": "1 pending migration(s): 20260817110019_AddUserVoicePreferenceDefaultLanguage"
    }
  ]
}
```

## Contract guarantees

- This check MUST NOT mutate the database (no auto-apply) — it only reports state
  (research.md Decision 2).
- The pending-migration names MUST be included in the unhealthy response so an operator
  can act without cross-referencing source control blind.
- This endpoint is additive — it MUST NOT be substituted for `/health` in any existing
  deployment-gate or load-balancer liveness configuration; it is a new, separate signal
  for readiness gates to opt into.

## Everything else (unchanged)

`/health` (liveness) and every other endpoint are unmodified by this feature.
