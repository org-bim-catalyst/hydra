# Contract: MCP Server & Tool Lifecycle

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md) | **Research**: [../research.md](../research.md) (Decisions 4, 10, 17)

## `McpServer` state machine

```
                 register
                    │
                    ▼
         ┌─────────────────────┐
         │  Registered/Disabled │  ◄────────────────────────────┐
         │  (IsEnabled=false)   │                                │
         └──────────┬───────────┘                                │
                     │ test-connection succeeds + admin enables   │ disable
                     ▼                                            │
              ┌─────────────┐   health check fails    ┌───────────┴────────┐
              │   Enabled    │ ───────────────────────►│ Enabled + Unhealthy │
              │ (Healthy)    │ ◄─────────────────────── │ (Degraded/Unavail/  │
              └──────┬───────┘   health check recovers  │  AuthFailed/Config) │
                     │                                   └─────────────────────┘
                     │ delete (blocked if referenced — see below)
                     ▼
                Soft-deleted
```

An `McpServer` starts `IsEnabled=false` on registration (spec.md User Story 1, Scenario 1: "not yet usable by any agent") regardless of connectivity — an administrator must explicitly enable it after a successful test connection and at least one successful capability discovery. `IsEnabled=true` with a non-`Healthy` `McpServerHealth.Status` still blocks new tool calls (FR-056) without disabling the server outright — the two flags are independent, matching data-model.md's `McpServer.IsEnabled` vs. `McpServerHealth.Status`.

**Removal**: `DELETE /mcp/servers/{id}` is rejected with `422 mcp-server-has-references` whenever `CountReferencingAgentToolsAsync(serverId) > 0` (clarification). There is no forced/override removal path — an administrator must first have every referencing agent's `AgentTool` row removed (via `PUT /agents/{id}` on each referencing agent), then retry the deletion.

## `McpTool` activation state machine (research.md Decision 4, clarification)

```
   discovered (every capability refresh)
        │
        ▼
 ┌───────────────┐   admin activates   ┌────────┐   admin deactivates   ┌─────────────┐
 │ PendingReview  │ ───────────────────►│ Active │ ───────────────────►│ Deactivated  │
 └───────────────┘                      └────────┘                      └──────┬───────┘
        ▲                                    │                                  │
        │                                    │ admin re-activates               │
        └────────────────────────────────────┴──────────────────────────────────┘
```

A tool re-discovered after having been `Active` does **not** silently return to `Active` — every newly-created `McpTool` row (data-model.md: a changed tool produces a new row, not an in-place edit) starts `PendingReview` again, requiring re-confirmation, *unless* the refresh determined the tool is unchanged from the prior snapshot (`McpCapabilitySnapshot.ChangeSummaryJson` shows no diff for that tool), in which case its `ActivationStatus` carries forward from the prior row by `NamespacedName` continuity — an administrator is asked to re-review only when something about the tool actually changed, not on every routine refresh. This keeps FR-022's "every newly discovered MCP tool" requirement literal (a genuinely new/changed tool always needs review) without imposing needless re-review churn on a stable, previously-approved tool.

## Capability discovery flow (FR-011–FR-018)

1. `RegisterMcpServerCommand` inserts `McpServer` (`IsEnabled=false`) + `McpServerCredential`. No discovery yet.
2. `TestMcpServerConnectionCommand` (admin-triggered, FR-008): `IMcpEndpointValidator` → `IMcpClientFactory.GetOrCreateAsync` → `IMcpClient.PingAsync`. Writes an `McpServerHealth` row. Does **not** discover capabilities — connectivity and capability discovery are separate steps so an admin can confirm reachability before triggering a full discovery.
3. `RefreshMcpCapabilitiesCommand` (admin-triggered the first time; then automatic per `CapabilityRefreshIntervalMinutes`, FR-013/FR-014): connect → authenticate → `ListToolsAsync`/`ListResourcesAsync`/`ListPromptsAsync` → write a new `McpCapabilitySnapshot` (`WasSuccessful=true`, `DeclaredCapabilitiesJson` set to whichever of Tools/Resources/Prompts the server actually returned — FR-017) → write/carry-forward `McpTool`/`McpResource`/`McpPrompt` rows → write `McpAuditLog(Action=CapabilityDiscoverySucceeded)`.
4. On failure at step 3: write `McpCapabilitySnapshot(WasSuccessful=false, FailureCategory=...)` + `McpAuditLog(Action=CapabilityDiscoveryFailed)`; the **prior** successful snapshot's `McpTool`/`McpResource`/`McpPrompt` rows remain the active set unchanged (FR-016 — a failed refresh never clears working state).
5. An administrator reviews `McpTool` rows still `PendingReview` and activates the ones they intend to allow (FR-021/FR-022, state machine above). Only now do a server's tools appear in `IMcpToolRegistry.ActiveTools` (research.md Decision 1) and in `GET /mcp/catalog/tools` (contracts/mcp-api.md).
6. An administrator enables the server (`EnableMcpServerCommand`). This is the point at which `FR-024`'s full availability condition (`server enabled AND tool activated AND tool available AND user permission held`) can first be satisfied for any of its tools.

## `McpAuditAction` → FR cross-reference

| Action | Written by | FR |
|---|---|---|
| `ServerRegistered` / `ServerUpdated` | `RegisterMcpServerCommand` / `UpdateMcpServerCommand` | FR-007, FR-066 |
| `ServerEnabled` / `ServerDisabled` | `EnableMcpServerCommand` / `DisableMcpServerCommand` | FR-003, FR-004 |
| `ServerRemovalBlocked` | `DeleteMcpServerCommand` (rejected path) | FR-005 |
| `ServerRemoved` | `DeleteMcpServerCommand` (accepted path) | FR-005 |
| `CredentialRotated` | `RotateMcpServerCredentialCommand` | FR-047 |
| `CapabilityDiscoveryStarted` / `Succeeded` / `Failed` | `RefreshMcpCapabilitiesCommand` / `McpCapabilityRefreshJob` | FR-011, FR-015, FR-016 |
| `HealthStateChanged` | `McpServerHealthCheckJob` / `TestMcpServerConnectionCommand` | FR-055 |
| `ToolActivated` / `ToolDeactivated` | `ActivateMcpToolCommand` / `DeactivateMcpToolCommand` | FR-021/FR-022 (clarification) |
| `UnauthorizedAccessAttempted` | Any denied admin action or denied cross-user access | FR-060 |
