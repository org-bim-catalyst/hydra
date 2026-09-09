# Quickstart: Admin Visibility of System Agents

Validates the feature end-to-end once implemented. See [contracts/admin-system-agents-api.md](./contracts/admin-system-agents-api.md)
for the exact request/response shape and [data-model.md](./data-model.md) for field sources.

## Prerequisites

- Local dev environment running (`dotnet run` for the API, `npm run dev` for `ClientApp`), or
  the deployed site4now.net environment.
- A user account with the Administrator or Super User role.
- At least one system agent provisioned (true on any environment where `SystemAgentProvisioner`
  has run past its migration gate — check via the existing
  `SystemAgentProvisioningHealthCheck` `/health` output if unsure).

## Scenario 1 — Administrator sees the system agents list (Story 1)

1. Sign in as an Administrator or Super User.
2. Navigate to **Admin → System Agents** (`/admin/system-agents`).
3. **Expected**: a row per provisioned system agent (today: `lucy.orchestrator`, `lucy.site`,
   `lucy.knowledge`, `lucy.memory`, `lucy.viewer` — per `SystemAgentDefinitions.All`), each
   showing name, status (`Published`), current version number, and a last-updated timestamp.

## Scenario 2 — Version bump reflects after a definition change (Story 1)

1. (Dev only) Modify a `SystemAgentDefinition` entry's instructions and restart the app so
   `SystemAgentProvisioner` republishes it as a new version.
2. Reload the System Agents admin screen.
3. **Expected**: that agent's version number is incremented and its last-updated timestamp
   reflects the new publish time.

## Scenario 3 — Empty state before provisioning (Story 1, edge case)

1. Against a freshly-migrated, never-provisioned database (or by temporarily querying with no
   system agents present).
2. Open the System Agents admin screen.
3. **Expected**: a clear "no system agents provisioned yet" message — not an error, not a blank
   page (FR-006).

## Scenario 4 — Read-only, clearly marked (Story 2)

1. As an Administrator, open the System Agents screen.
2. **Expected**: every row carries the same system-owned/read-only visual marker already used in
   the Agent Library (T108). No edit, delete, duplicate, or publish control is present anywhere
   on the screen.

## Scenario 5 — Access is denied to non-admins (Story 3)

1. As an anonymous (signed-out) visitor, attempt `GET /api/v1/admin/agents/system` directly (or
   navigate to `/admin/system-agents`).
   **Expected**: `401 Unauthorized` / redirected to sign-in, no data shown.
2. Sign in as a regular (non-admin) user and repeat.
   **Expected**: `403 Forbidden` / access-denied UI, no data shown.
3. Sign in as an Administrator or Super User and repeat.
   **Expected**: `200 OK`, data loads normally.

## Scenario 6 — Personal Agents page is unaffected (FR-007 regression check)

1. As any user (admin or not) with zero or more of their own agents, open the existing personal
   **Agents** page (`/agents`).
2. **Expected**: behavior is identical to before this feature — only the caller's own agents are
   listed; no system agent ever appears here, regardless of role.
