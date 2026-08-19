# Contract: MCP REST API

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Follows `docs/API_GUIDELINES.md`/constitution §6 exactly: nouns, plural, kebab/lowercase, `/api/v1/...`; actions that don't map to CRUD are `POST .../actions/{verb}`; every error is RFC 7807 Problem Details with a `traceId`; every endpoint is `[Authorize]` by default; list endpoints are cursor-paginated; authorization is enforced in Application-layer handlers, not controller `if` checks.

Three controllers: `McpServersController` (administrator-only registry/lifecycle management, FR-001–FR-018/FR-045–FR-066), `McpCatalogController` (any authenticated user browsing available tools/resources/prompts, FR-036/FR-042/FR-062), and `McpAgentToolsController`'s surface is actually just the *existing* `PUT /agents/{id}` body (spec 020) accepting `mcp:...`-namespaced strings in its `tools` array — no new endpoint needed for "enable/disable an MCP tool for an agent" (FR-063), per research.md Decision 3's zero-schema-change design.

## McpServersController — `/api/v1/admin/mcp/servers` (Administrator/Super User only, `[EnableRateLimiting("mcp-admin-endpoints")]`)

> Implementation-time correction: every other admin-only controller in this codebase
> (`AgentPoliciesController`, `AdminAiProvidersController`, `AdminDashboardController`) uses an
> `api/v1/admin/...` route prefix, not a bare `api/v1/...` one — this table's paths below (shown
> without the `/admin` segment for brevity) are mounted under `/api/v1/admin` to match that
> established convention, not `/api/v1` as originally drafted.

| Method | Path | Command/Query | Notes |
|---|---|---|---|
| `POST` | `/mcp/servers` | `RegisterMcpServerCommand` | Body: `name`, `description?`, `endpoint`, `transport`, `authenticationType`, `credential` (write-only, never echoed back), `requiresUnauthenticatedConfirmation?`, `allowInsecureTransport?` + `insecureTransportJustification?`. Runs `IMcpEndpointValidator` (research.md Decision 8) synchronously before insert — `422` (`type: mcp-endpoint-not-allowed`) on an unresolved private/loopback/link-local/cloud-metadata destination. `409` (`type: mcp-server-endpoint-conflict`) if `(endpoint, transport)` already exists (clarification). `201` + `Location`, server starts disabled-until-verified. |
| `GET` | `/mcp/servers` | `ListMcpServersQuery` | Cursor-paginated; filters: `status`, `transport`, `enabled`. |
| `GET` | `/mcp/servers/{id}` | `GetMcpServerQuery` | Never includes credential material (FR-045). |
| `PUT` | `/mcp/servers/{id}` | `UpdateMcpServerCommand` | Increments `ConfigurationVersion` (FR-007); `409` on `RowVersion` mismatch. |
| `POST` | `/mcp/servers/{id}/actions/enable` | `EnableMcpServerCommand` | FR-003 |
| `POST` | `/mcp/servers/{id}/actions/disable` | `DisableMcpServerCommand` | FR-004 — immediately excludes all child tools/resources/prompts from `IMcpToolRegistry` |
| `DELETE` | `/mcp/servers/{id}` | `DeleteMcpServerCommand` | `422` (`type: mcp-server-has-references`, body lists referencing `agentId`/`toolName` pairs) if `CountReferencingAgentToolsAsync > 0` (clarification, data-model.md). Otherwise soft delete. |
| `POST` | `/mcp/servers/{id}/actions/test-connection` | `TestMcpServerConnectionCommand` | Synchronous, on-demand (FR-008) — calls the same Application service the recurring health-check job calls (research.md Decision 10). Returns the resulting `McpServerHealthStatus` without waiting for the next scheduled cycle. |
| `POST` | `/mcp/servers/{id}/actions/refresh-capabilities` | `RefreshMcpCapabilitiesCommand` | FR-013 — returns the new `McpCapabilitySnapshot`'s `changeSummaryJson` (FR-015) in the response body so the admin sees what changed immediately, not just on next poll. |
| `POST` | `/mcp/servers/{id}/actions/rotate-credential` | `RotateMcpServerCredentialCommand` | Body: new `credential` (write-only). FR-047 — never interrupts in-flight calls beyond the rotation itself. |
| `GET` | `/mcp/servers/{id}/health` | `GetMcpServerHealthQuery` | Current `McpServerHealthStatus` + `FailureCategory`/`Detail` (FR-055/FR-056). |
| `GET` | `/mcp/servers/{id}/references` | `ListMcpServerReferencesQuery` | Which agents/tools currently reference this server (FR-065) — same data `DeleteMcpServerCommand`'s `422` body surfaces, exposed proactively so an admin can check before attempting removal. |
| `GET` | `/mcp/servers/{id}/tools` | `ListMcpServerToolsQuery` (admin view) | Includes `PendingReview`/`Deactivated` tools, unlike the user-facing catalog below. |
| `POST` | `/mcp/servers/{id}/tools/{toolId}/actions/activate` | `ActivateMcpToolCommand` | Research.md Decision 4/clarification — the mandatory admin gate. Body: optionally overrides `EffectiveRiskLevel`/`RequiredPermissionsJson` before activating (FR-021/FR-022). |
| `POST` | `/mcp/servers/{id}/tools/{toolId}/actions/deactivate` | `DeactivateMcpToolCommand` | |
| `GET` | `/mcp/servers/{id}/audit-log` | `ListMcpAuditLogQuery` | Cursor-paginated `McpAuditLog` rows for this server (FR-058). |

## McpCatalogController — `/api/v1/mcp/catalog` (any authenticated user, `[EnableRateLimiting("mcp-endpoints")]`)

| Method | Path | Command/Query | Notes |
|---|---|---|---|
| `GET` | `/mcp/catalog/tools` | `ListAvailableMcpToolsQuery` | Only `ActivationStatus == Active`, `IsAvailable == true`, source server `IsEnabled == true` and healthy (FR-062) — the same filter `IMcpToolRegistry.ActiveTools` applies at execution time, so what a user sees here is exactly what an agent can actually call. Includes `name`, `description`, `sourceServer`, `effectiveRiskLevel`, `requiredPermissions`. |
| `GET` | `/mcp/catalog/tools/{namespacedName}` | `GetMcpToolQuery` | Full detail (FR-020) — input/output schema, capabilities, version, last-updated. |
| `GET` | `/mcp/catalog/resources` | `ListAvailableMcpResourcesQuery` | FR-036 |
| `GET` | `/mcp/catalog/prompts` | `ListAvailableMcpPromptsQuery` | FR-042 — merged client-side (or via one query spanning both `Prompt` and `McpPrompt`, research.md Decision 16) with the user's native prompts wherever a unified prompt picker is shown; this endpoint alone returns MCP-sourced prompts only. |
| `POST` | `/mcp/catalog/prompts/{namespacedName}/actions/duplicate` | `DuplicateMcpPromptCommand` | Research.md Decision 16 — creates a new, independent, user-owned native `Prompt`. `201` + `Location` pointing at the new native prompt. |

## Enabling/disabling an MCP tool for an agent (FR-063) — no new endpoint

`PUT /api/v1/agents/{id}` (spec 020's existing endpoint) already accepts a `tools: [{ toolName }]` array; an MCP tool is added/removed from an agent's configuration by including/excluding its `mcp:{serverId}:{toolName}` string, exactly like a native tool name. `UpdateAgentCommand`'s existing validation (spec 020) is extended to also accept a `toolName` resolvable via `AgentToolCatalog.Find` (research.md Decision 1) — which already covers both sources — rather than only the native DI-registered set.

## Error shape

Standard Problem Details, matching every other controller. Notable new `type` values: `mcp-endpoint-not-allowed` (422, SSRF rejection), `mcp-server-endpoint-conflict` (409, duplicate registration), `mcp-server-has-references` (422, blocked removal), `mcp-tool-not-activated` (403 — an agent tries to use a `PendingReview`/`Deactivated` tool; deliberately 403 not 404, the same "shape mismatch, not ownership" exception `agent-tool-permission-denied` already established in spec 020's contract), `mcp-server-unhealthy` (503 — a call attempted against a non-`Healthy` server).

```json
{
  "type": "https://asklucy.io/problems/mcp-endpoint-not-allowed",
  "title": "Endpoint not allowed",
  "status": 422,
  "detail": "The configured endpoint resolves to a private network address (10.0.0.0/8). Set endpointValidationOverride with a justification if this is intentional.",
  "traceId": "00-4bf9...-00"
}
```
