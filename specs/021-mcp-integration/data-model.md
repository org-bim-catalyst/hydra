# Data Model: MCP (Model Context Protocol) Integration

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Every entity below inherits `BaseEntity` (`src/AskLucy.Domain/Common/BaseEntity.cs`: `Id` (Guid v7), `CreatedAtUtc`/`CreatedBy`, `ModifiedAtUtc`/`ModifiedBy`, `DeletedAtUtc`/`DeletedBy`, `RowVersion`) unless noted, with soft delete enforced the same way as every other feature (EF Core query filter + `AuditSaveChangesInterceptor`).

This feature makes **zero schema changes** to spec 020's existing tables (`Agent`, `AgentVersion`, `AgentTool`, `AgentExecution`, `AgentToolCall`, `AgentApproval`, `AgentPolicy`, `AgentAuditLog`, etc.) — MCP tools plug into them via the namespaced string identifier (research.md Decision 3). The only change to existing code outside the new `Mcp` aggregate cluster is an additive extension of the `AgentToolPermission` enum (research.md Decision 5).

## Aggregate: McpServer

### McpServer (aggregate root)

The administrator-registered external MCP server (FR-001–FR-010).

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | Required, admin-facing display name |
| `Description` | `string?` | |
| `Endpoint` | `string` | The server's URL (remote) or launch command reference (local, FR-009/FR-010) |
| `Transport` | `McpServerTransport` enum | `StreamableHttp` \| `Stdio` — `Stdio` registration is rejected server-side unless `McpRuntimeOptions.AllowLocalTransport` is enabled for the deployment (Assumptions, FR-009) |
| `AuthenticationType` | `McpAuthenticationType` enum | `None` \| `ApiKey` \| `BearerToken` \| `OAuth2ClientCredentials` (FR-048) — `None` on a remote server requires `RequiresUnauthenticatedConfirmation` below to be `true` |
| `RequiresUnauthenticatedConfirmation` | `bool` | Set only when an administrator explicitly confirmed an unauthenticated remote connection is intended (FR-048) |
| `AllowInsecureTransport` | `bool` | Default `false`; sets to `true` only via explicit administrator override with `InsecureTransportJustification` populated (FR-049) |
| `InsecureTransportJustification` | `string?` | Required when `AllowInsecureTransport == true` |
| `EndpointValidationOverride` | `bool` | Default `false`; allow-lists an otherwise-rejected private/loopback/link-local/cloud-metadata destination (FR-050, research.md Decision 8) |
| `EndpointValidationJustification` | `string?` | Required when `EndpointValidationOverride == true` |
| `IsEnabled` | `bool` | FR-003/FR-004 — disabling immediately hides all child tools/resources/prompts (enforced by `IMcpToolRegistry` reading this flag, research.md Decision 1/4) |
| `OwnerUserId` | `string` (FK → `ApplicationUser.Id`) | The administrator who registered it (FR-066) — administrative ownership, not usage scoping (any authorized user may use it, FR-062) |
| `ConfigurationVersion` | `int` | Incremented on every admin config change (FR-007), starts at `1` |
| `CapabilityRefreshIntervalMinutes` | `int` | Admin-configurable; default from `McpRuntimeOptions.DefaultCapabilityRefreshIntervalMinutes` (FR-014) |
| `LastHealthCheckAtUtc` | `DateTime?` | |
| `LastCapabilityDiscoveryAtUtc` | `DateTime?` | |

**Navigation**: `Credential` (`McpServerCredential`, 1–1), `Health` (`McpServerHealth`, 1–1 current + historical rows), `CapabilitySnapshots` (`McpCapabilitySnapshot`, 1–N), `Tools`/`Resources`/`Prompts` (via the latest `McpCapabilitySnapshot`, not a direct FK — see below).

**Business rules**: `(Endpoint, Transport)` is unique across the registry (clarification — a duplicate registration attempt is rejected, `409`, pointing at the existing entry). Removal (`DeleteMcpServerCommand`) is rejected (`422`) whenever `IMcpServerRepository.CountReferencingAgentToolsAsync(serverId) > 0` (clarification, research.md Decision 15) — an administrator must first have every referencing `AgentTool` removed from its owning agent(s). Never hard-deleted (soft delete only, audit trail — FR-005's "prior execution history... remains intact").

**Domain events**: `McpServerRegistered`, `McpServerUpdated`, `McpServerEnabled`, `McpServerDisabled`, `McpServerRemovalBlocked`, `McpServerRemoved`.

### McpServerCredential

One-to-one with `McpServer` (research.md Decision 7).

| Field | Type | Notes |
|---|---|---|
| `McpServerId` | `Guid` (FK, unique) | |
| `CiphertextBlob` | `string` | Encrypted via `IMcpCredentialProtector` (ASP.NET Core Data Protection, purpose `"AskLucy.McpServerCredentials"`) — API key/bearer token value, or a serialized `{clientId, clientSecret}` JSON blob for `OAuth2ClientCredentials`. Never plaintext at rest, never serialized to any DTO (FR-045/FR-046) |
| `RotatedAtUtc` | `DateTime` | Set on every rotation (FR-047) |
| `RotatedByUserId` | `string` (FK) | |

**Business rules**: `RotateMcpServerCredentialCommand` replaces `CiphertextBlob` and stamps `RotatedAtUtc`/`RotatedByUserId` in a single update — never deletes and re-inserts the row (so an in-flight call reading the credential mid-rotation sees either the old or the new value atomically, never a missing row).

### McpServerHealth

Current + historical health state (FR-055, one current row per server plus append-only history — modeled as a single mutable "current" row for `McpServer.CurrentHealthStatus`-style reads, with every transition also written to `McpAuditLog` for the historical trail, avoiding a second unbounded history table).

| Field | Type | Notes |
|---|---|---|
| `McpServerId` | `Guid` (FK, unique) | |
| `Status` | `McpServerHealthStatus` enum | `Healthy` \| `Degraded` \| `Unavailable` \| `AuthenticationFailed` \| `ConfigurationError` \| `Unknown` (FR-055) |
| `FailureCategory` | `McpFailureCategory?` enum | Set when `Status != Healthy` (research.md Decision 17) |
| `Detail` | `string?` | Actionable, safe (no credential material — FR-046) |
| `CheckedAtUtc` | `DateTime` | |
| `ConsecutiveFailureCount` | `int` | Feeds the circuit-breaker in `McpConnectionResiliencePolicy` (research.md Decision 11) |

**Business rules**: A server's `Status` transitioning away from `Healthy` blocks new tool calls against it (FR-056) — enforced by `IMcpToolRegistry` excluding its tools from `ActiveTools` (research.md Decision 1/4), not by a separate check in `McpToolAdapter`.

### McpCapabilitySnapshot

A versioned, point-in-time record of everything a server reported during one discovery run (FR-011, FR-015–FR-018).

| Field | Type | Notes |
|---|---|---|
| `McpServerId` | `Guid` (FK) | |
| `DiscoveredAtUtc` | `DateTime` | |
| `SnapshotVersion` | `int` | Sequential per server, starting at 1 |
| `DeclaredCapabilitiesJson` | `string` | Which protocol capabilities the server actually advertised this run — `["Tools","Resources","Prompts"]` subset (FR-017) — agents/tools cannot reference a capability type the server never declared |
| `ChangeSummaryJson` | `string?` | What changed vs. the prior snapshot (tools/resources/prompts added/removed/modified) — null on the first snapshot (FR-015) |
| `WasSuccessful` | `bool` | `false` on a failed discovery attempt — the prior successful snapshot's `McpTool`/`McpResource`/`McpPrompt` rows remain the active set regardless (FR-016) |
| `FailureCategory` | `McpFailureCategory?` enum | Set when `WasSuccessful == false` |

**Business rules**: `McpTool`/`McpResource`/`McpPrompt` rows (below) belong to exactly one `McpCapabilitySnapshot` and are never mutated in place — a changed tool on refresh produces a *new* `McpTool` row in the new snapshot (carrying forward `ActivationStatus`/administrator review state by `NamespacedName` continuity, per FR-018's "traceable to the specific tool definition it used"), not an in-place edit of the old row, so a completed execution's `AgentToolCall` (which references the tool by name, not by a snapshot-scoped id) always resolves to a stable historical identity even as the tool's schema evolves upstream.

## Aggregate members: McpTool, McpResource, McpPrompt

### McpTool

Normalized tool metadata from one capability snapshot, adapted into `IAgentTool` at runtime via `McpToolAdapter` (research.md Decision 1).

| Field | Type | Notes |
|---|---|---|
| `McpServerId` | `Guid` (FK) | |
| `McpCapabilitySnapshotId` | `Guid` (FK) | |
| `NamespacedName` | `string` | `mcp:{McpServerId}:{ToolName}` — unique, indexed (research.md Decision 3) |
| `ToolName` | `string` | The server's own tool name, unqualified |
| `DisplayName` | `string` | |
| `Description` | `string` | Shown to the planner model and to users (FR-020) |
| `InputSchemaJson` / `OutputSchemaJson` | `string` | Arbitrary, externally-supplied JSON Schema documents, validated via `IJsonSchemaValidator` (research.md Decision 9) before/after every call |
| `DeclaredCapabilitiesJson` | `string?` | Server-declared tool-level capability flags, if any |
| `ServerDeclaredRiskLevel` | `AgentToolRiskLevel?` enum | What the server itself claims, if anything — **advisory only**, never trusted directly (research.md Decision 4) |
| `EffectiveRiskLevel` | `AgentToolRiskLevel` enum | The platform's actual, administrator-confirmed classification (FR-021/FR-022) — defaults to `Critical` (the platform's most restrictive level) until reviewed |
| `RequiredPermissionsJson` | `string` | Mapped onto `AgentToolPermission` (research.md Decision 5), administrator-confirmable/editable at activation time |
| `ActivationStatus` | `McpToolActivationStatus` enum | `PendingReview` \| `Active` \| `Deactivated` (research.md Decision 4, clarification) |
| `ActivatedByUserId` / `ActivatedAtUtc` | `string?` / `DateTime?` | Set on transition to `Active` |
| `Version` | `string?` | Server-declared tool version, if any (FR-018) |
| `IsAvailable` | `bool` | `false` if this tool was absent from the most recent successful discovery (distinct from `ActivationStatus` — an admin-activated tool the server later stops advertising becomes unavailable without losing its activation history) |

**Business rules**: `NamespacedName` is globally unique. A tool call is only permitted when `ActivationStatus == Active AND IsAvailable == true` and its `McpServer.IsEnabled == true` and server health is not `Unavailable`/`AuthenticationFailed` (FR-024).

### McpResource

Normalized resource metadata (FR-036–FR-040).

| Field | Type | Notes |
|---|---|---|
| `McpServerId` | `Guid` (FK) | |
| `McpCapabilitySnapshotId` | `Guid` (FK) | |
| `NamespacedName` | `string` | `mcp:{McpServerId}:{ResourceUri}` |
| `Name` / `Description` | `string` | |
| `ContentType` | `string?` | |
| `IsAvailable` | `bool` | Same semantics as `McpTool.IsAvailable` |

**Business rules**: Retrieval is authorized/approval-gated identically to a tool call (FR-037) — `McpResourceReadTool : IAgentTool` (a single built-in adapter tool, not one class per resource) is how the Agent Runtime actually fetches a resource's content, keeping resource access inside the exact same runtime-enforced pipeline as Decision 6, rather than a second access path.

### McpPrompt

Normalized prompt metadata (FR-041–FR-044, research.md Decision 16). **Not** a row in the existing `Prompt` table.

| Field | Type | Notes |
|---|---|---|
| `McpServerId` | `Guid` (FK) | |
| `McpCapabilitySnapshotId` | `Guid` (FK) | |
| `NamespacedName` | `string` | `mcp:{McpServerId}:{PromptName}` |
| `Name` / `Description` | `string` | |
| `ContentTemplate` | `string` | Read-only mirror of the server's current definition, re-synced on every successful capability refresh (clarification) |
| `IsAvailable` | `bool` | Same semantics as above — `false` when the source server is disabled/removed (FR-044's "clearly shown as unavailable") |

**Business rules**: Never directly editable (no `Update` domain method) — the only mutation path is `RefreshFromSnapshot` (called by `McpCapabilityRefreshJob`). A user wanting a customized version uses `DuplicateMcpPromptCommand` (research.md Decision 16), which creates an independent `Prompt` row this entity has no further relationship to.

## Aggregate: McpAuditLog

### McpAuditLog

Tamper-resistant record of MCP-specific administrative/security events (FR-058–FR-060), distinct from but cross-referenceable with `AgentAuditLog`/`AgentToolCall` (which already capture per-execution tool-call activity per FR-031 — this table does not duplicate that).

| Field | Type | Notes |
|---|---|---|
| `McpServerId` | `Guid?` | Soft reference, no FK constraint (mirrors `AgentAuditLog.AgentExecutionId`'s pattern — an audit entry for a later-purged server is retained) |
| `UserId` | `string` | The acting user (administrator for admin actions; any user for an unauthorized-access attempt) |
| `Action` | `McpAuditAction` enum | See enumerations below |
| `FailureCategory` | `McpFailureCategory?` enum | Set for failure-related actions (research.md Decision 17) |
| `DetailsJson` | `string` | Short, sanitized summary — never credential material or full request/response payloads (FR-059) |
| `OccurredAtUtc` | `DateTime` | |

## Extension to an existing entity: `AgentToolPermission` (Application enum)

`src/AskLucy.Application/Agents/Tools/IAgentTool.cs` gains six additive members (research.md Decision 5) — no change to the entity/table this enum's values are serialized into (`AgentToolCall.RequiredPermissionsJson` already stores a JSON array of names, not ordinals):

```
ReadExternalData, WriteExternalData, SendCommunication, ModifyExternalSystem, DeleteExternalData, ExecuteOperation
```

This — together with `McpTool.RequiredPermissionsJson` above — is what satisfies spec.md's `McpToolPermission` Key Entity; no separate `McpToolPermission` table is created, since the mapping it describes is fully expressed by an `McpTool` field plus an enum extension.

## Enumerations

```
McpServerTransport:            StreamableHttp | Stdio
McpAuthenticationType:         None | ApiKey | BearerToken | OAuth2ClientCredentials
McpServerHealthStatus:         Healthy | Degraded | Unavailable | AuthenticationFailed | ConfigurationError | Unknown
McpToolActivationStatus:       PendingReview | Active | Deactivated
McpFailureCategory:            ConnectionFailure | AuthenticationFailure | AuthorizationFailure | Timeout |
                                RateLimit | InvalidRequest | InvalidResponse | ServerError | ProtocolError |
                                CapabilityDiscoveryFailure | ServerUnavailable
McpAuditAction:                ServerRegistered | ServerUpdated | ServerEnabled | ServerDisabled |
                                ServerRemovalBlocked | ServerRemoved | CredentialRotated |
                                CapabilityDiscoveryStarted | CapabilityDiscoverySucceeded | CapabilityDiscoveryFailed |
                                HealthStateChanged | ToolActivated | ToolDeactivated | UnauthorizedAccessAttempted
```

## Delete behavior

| Parent | Child | Behavior |
|---|---|---|
| McpServer | McpServerCredential | Cascade soft delete (no independent meaning without the server) |
| McpServer | McpServerHealth | Cascade soft delete |
| McpServer | McpCapabilitySnapshot | Restrict — never cascade; a removed server's discovery history is retained for audit (mirrors `Agent → AgentVersion: Restrict`) |
| McpCapabilitySnapshot | McpTool / McpResource / McpPrompt | Restrict — a tool's `NamespacedName` may still be referenced by a historical `AgentToolCall` even after its server is removed |
| McpServer | McpAuditLog | No FK (soft reference, per `AgentAuditLog`'s existing pattern — never cascades, never blocks) |

**Removal precondition** (clarification, research.md Decision 15): `DeleteMcpServerCommand` additionally requires `CountReferencingAgentToolsAsync(serverId) == 0` before it is allowed to soft-delete `McpServer` at all — this is a business-rule precondition enforced in the command handler, not a database-level cascade/restrict, since the "reference" being checked lives in a different aggregate (`AgentTool`, owned by spec 020's `Agent` aggregate) that this feature must not reach into directly for a write, only for this one read-only precondition check via `IMcpServerRepository`.

## Explicitly not modeled (deferred, per research.md / spec Out of Scope)

- **Per-organization/tenant scoping** — Assumptions; `McpServer` has no `OrganizationId` column at all this release (unlike spec 020's reserved-but-unused `AgentPolicy.OrganizationId` — there is no forward-compatible placeholder here since the spec's own Assumptions section treats this as following spec 020's actual precedent, not its literal spec wording).
- **`McpExecution`** (the placeholder entity sketched in `docs/ENTITY_MODEL.md` §11, predating this spec) — superseded; per-call execution detail is `AgentToolCall`/`AgentExecutionEvent` (spec 020, reused verbatim per FR-031), never duplicated into a second execution-detail table.
- **Sampling / Notifications capability rows** — Assumptions; `McpCapabilitySnapshot.DeclaredCapabilitiesJson` can record a server advertising them, but no `McpSampling`/`McpNotification` entity exists to act on that yet.
- **Interactive per-user OAuth (Authorization Code + PKCE)** — Assumptions/Open Questions; `McpAuthenticationType` has no such member this release.
