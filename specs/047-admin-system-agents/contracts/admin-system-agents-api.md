# Contract: Admin System Agents API

## `GET /api/v1/admin/agents/system`

Lists every system-owned agent. Read-only, unpaginated (see research.md Decision 3).

**Authorization**: `AdministratorOrSuperUser` policy (same as every endpoint on
`AdminAiProvidersController`). Anonymous → `401`. Authenticated, non-admin → `403`.

**Request**: none (no query parameters, no body).

**Response**: `200 OK`

```json
[
  {
    "id": "018f...-....-....-....-............",
    "name": "Lucy",
    "systemKey": "lucy.orchestrator",
    "status": "Published",
    "publishedVersionNumber": 2,
    "lastUpdatedAtUtc": "2026-09-01T12:34:56Z"
  }
]
```

- `status` is one of `"Draft" | "Published" | "Archived"` (serialized as string — matches the
  app-wide enum-as-string convention, see the memory note on enum JSON serialization).
- `publishedVersionNumber` is `null` only if a system agent record exists but has never been
  published (should not occur in normal operation — `SystemAgentProvisioner` always publishes on
  create).
- An empty array `[]` is a valid, non-error response (FR-006's empty state — no system agents
  provisioned yet, e.g. a fresh environment where provisioning is still pending).

**Errors**:
- `401 Unauthorized` — no/invalid bearer token.
- `403 Forbidden` — authenticated but lacking the Administrator/Super User role.
- No other error path — this is a pure read over always-available local data (no external
  provider call, no user input to validate).

## Non-goals (explicitly out of scope, per spec Assumptions)

- No endpoint to view a system agent's full configuration/instructions or execution history.
- No create/update/delete/publish endpoint for system agents from this surface — provisioning
  remains exclusively `SystemAgentProvisioner`'s responsibility (specs/045).
- No change to `GET /agents` (the existing owner-scoped personal list) — this is an additive,
  separate endpoint.
