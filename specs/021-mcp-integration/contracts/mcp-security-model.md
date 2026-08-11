# Contract: MCP Security Model

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decisions 7, 8, 9, 11, 12)

MCP is a high-risk external integration boundary (spec.md "Security Requirements"). This contract collects every security control into one place so implementation and review can check them off directly against FR numbers.

## Credential handling (FR-045–FR-047, research.md Decision 7)

| Control | Mechanism |
|---|---|
| Encryption at rest | `IMcpCredentialProtector` (ASP.NET Core Data Protection, purpose `"AskLucy.McpServerCredentials"`) — same primitive as `AiCredentialProtector`, distinct purpose string |
| Never sent to the browser | `McpServerCredential` has no DTO mapping in any `Get`/`List` response; write-only in `Register`/`Update`/`RotateCredential` request bodies |
| Never logged | `ILogger` calls in `Infrastructure/Mcp/*` never interpolate `CiphertextBlob` or a decrypted value — enforced by code review, not tooling, matching the existing convention for `AIProvider.CredentialCiphertext` |
| Never in audit/execution history | `McpAuditLog.DetailsJson`/`AgentToolCall.ValidatedInputJson`/`ValidatedOutputJson` are populated from the *tool call's* input/output, never from `McpServerCredential` — the credential is applied by `IMcpClientFactory` at the transport layer, below anything that gets persisted |
| Rotation | `RotateMcpServerCredentialCommand` replaces `CiphertextBlob` in place (data-model.md); the next call picks up the new value, no server re-registration |

## SSRF protection (FR-050, research.md Decision 8)

`IMcpEndpointValidator.ValidateAsync(endpoint, ct)` runs at **two** points, not one:

1. **Registration/update time** (`RegisterMcpServerCommand`/`UpdateMcpServerCommand`) — rejects (`422 mcp-endpoint-not-allowed`) unless `EndpointValidationOverride` is set.
2. **Every connection attempt** (`IMcpClientFactory.GetOrCreateAsync`, called by health checks, capability discovery, and every tool/resource/prompt call) — re-resolves DNS and re-checks, closing the DNS-rebinding gap where a hostname was safe at registration but now resolves elsewhere.

Rejected ranges (unless `EndpointValidationOverride` + justification): RFC 1918 private ranges (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`), loopback (`127.0.0.0/8`, `::1`), link-local (`169.254.0.0/16`, `fe80::/10` — includes the `169.254.169.254` cloud-metadata endpoint shared by AWS/Azure/GCP instance metadata services), and any hostname that fails to resolve at all.

## Transport security (FR-049)

`Transport == StreamableHttp` requires `Endpoint` to be `https://` unless `AllowInsecureTransport == true` with `InsecureTransportJustification` populated (both admin-only, both audited via `McpAuditLog.Action == ServerUpdated`). Certificate validation is never disabled programmatically — no `ServerCertificateCustomValidationCallback` bypass anywhere in `Infrastructure/Mcp/*`, matching constitution §8's "certificate validation must not be disabled in production."

## Local (stdio) transport gating (FR-009/FR-010)

`McpRuntimeOptions.AllowLocalTransport` (an `IOptions<T>`-bound, deployment-level config flag, default `false`) gates whether `Transport == Stdio` is even accepted by `RegisterMcpServerCommand`'s validator — checked before any per-server logic runs. When permitted, the registered "command" is an administrator-supplied, pre-approved launch configuration (never a string a non-administrator user can influence) — there is no code path from any user-facing input to process launch arguments.

## Input/output validation (FR-025/FR-026, research.md Decision 9)

`IJsonSchemaValidator` (backed by `JsonSchema.Net`) validates:

- **Input**, before the request leaves the process — a violation is recorded as a failed `AgentToolCall` and the MCP server is never contacted (protects the external server from malformed traffic and avoids wasting a rate-limit slot on a request that was always going to fail).
- **Output**, after the response returns — a violation is recorded as a failed `AgentToolCall` even if the MCP server itself reported success; the response is never forwarded to the Agent Runtime unvalidated.

Both checks additionally enforce a maximum payload size (`McpRuntimeOptions.MaxResponseSizeBytes`, FR-051) independent of schema conformance — an oversized-but-schema-valid response is still rejected.

## Untrusted content (FR-030/FR-035) — see `mcp-tool-adapter.md`'s "Untrusted-content framing" section; not duplicated here.

## Rate limiting & concurrency (FR-052/FR-053, research.md Decision 12)

Two independent layers — do not confuse them:

| Layer | Mechanism | Governs |
|---|---|---|
| Inbound REST | ASP.NET Core `RateLimiting` middleware, named policies `mcp-admin-endpoints`/`mcp-endpoints` | Browser/API calls to `McpServersController`/`McpCatalogController` |
| Outbound MCP calls | `IMcpRateLimiter`/`IMcpConcurrencyLimiter` (`System.Threading.RateLimiting` primitives, in-process) | Every `McpToolAdapter.ExecuteAsync`/`McpResourceReadTool.ExecuteAsync`, keyed `(serverId, toolName, userId, agentId)` |

A rejection at either layer returns an actionable error (`429` inbound; a failed `AgentToolCall` with `McpFailureCategory.RateLimit` outbound) — never a silent drop or an indefinite queue (FR-053).

## Resilience (FR-037/FR-054, research.md Decision 11)

`McpConnectionResiliencePolicy` wraps every `IMcpClient` call: retry with exponential backoff up to `McpRuntimeOptions.MaxRetries`, but **only** for calls the caller marks idempotent (health checks, capability discovery, resource reads) — a tool call (`CallToolAsync`) is retried only if the specific `McpTool` is flagged safe-to-retry by its declared MCP annotations; an ambiguous-outcome failure (e.g., a dropped connection mid-call with no confirmed response) is recorded as failed, not retried, and not assumed successful (edge case in spec.md). A per-server circuit breaker opens after `McpRuntimeOptions.CircuitBreakerFailureThreshold` consecutive failures within a rolling window and half-opens on the next scheduled health-check tick (research.md Decision 10) — no calls are attempted against an open-circuit server in between.

## Access & audit (FR-060, FR-066)

Every admin action (`McpServersController`) requires the `AdministratorOrSuperUser` authorization policy — the same one `AgentPoliciesController` (spec 020) and `GetOrganizationDashboardSummaryQuery` already use. Every action, successful or denied, that touches `McpServersController` or attempts to bypass an `McpToolAdapter`'s permission check writes an `McpAuditLog` row; a denied attempt specifically sets `Action = UnauthorizedAccessAttempted` (FR-060).
