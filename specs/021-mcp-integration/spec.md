# Feature Specification: MCP (Model Context Protocol) Integration

**Feature Branch**: `021-mcp-integration`

**Created**: 2026-08-10

**Status**: Draft

**Input**: User description: "Integrate the Model Context Protocol (MCP) into Ask Lucy as an extensible external-tool and context integration layer. Administrators register, configure, test, and monitor MCP servers; discovered tools, resources, and prompts are normalized and exposed to Lucy agents through the existing Agent Tool abstraction and Agent Runtime — never a second, parallel tool execution framework. Users see which MCP servers/tools/resources/prompts are available to them and enable/disable MCP tools per agent. Agents discover and call MCP tools through the same permission checks, risk classification, approval gates, execution history, and audit trail already used for native tools. MCP is treated as a high-risk external integration boundary: credentials remain server-side, remote connections require TLS and are protected against SSRF, all tool/resource/prompt content is treated as untrusted data (never elevated to instructions), and MCP must not bypass existing RAG, Memory, or authorization boundaries."

## Clarifications

### Session 2026-08-10

- Q: Should every newly discovered MCP tool require explicit administrator review/activation before any user can enable it for an agent, or should it become available automatically upon discovery (gated only by per-call risk-based approval at execution time)? → A: Require admin activation — every discovered tool starts inactive until an administrator explicitly activates it, regardless of the risk level the server itself declares.
- Q: Can the same MCP server endpoint be registered more than once, or must each endpoint be unique across the registry? → A: Endpoint (endpoint + transport) must be unique platform-wide; a duplicate registration attempt is rejected, pointing at the existing entry.
- Q: When an administrator tries to remove an MCP server that agents currently reference, should removal be strictly blocked until all references are cleared, or allowed after an explicit confirmation? → A: Strictly blocked — removal is refused, listing the referencing agents/tools, until those references are cleared first.
- Q: Once an MCP-sourced prompt is integrated into the Prompt Library, is it a read-only mirror of the server's definition, or can a user save an independently editable copy? → A: Read-only mirror — it re-syncs on capability refresh; a user who wants to customize it duplicates it into a new, independent native prompt.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register and Connect an MCP Server (Priority: P1)

An administrator registers a new MCP server by providing its name, endpoint, transport, and authentication details, tests connectivity, and triggers capability discovery so its tools, resources, and prompts become known to the platform.

**Why this priority**: Nothing else in this feature has value until a server can be registered, connected to, and its capabilities discovered — this is the foundation every other story depends on.

**Independent Test**: Can be fully tested by registering a server with valid connection details, running a connectivity test, and confirming the system successfully discovers and lists that server's tools, resources, and prompts with a Healthy status — without involving any agent.

**Acceptance Scenarios**:

1. **Given** an administrator provides a valid endpoint, transport, and authentication configuration for a new MCP server, **When** they save the registration, **Then** the server appears in the registry in a disabled-until-verified state and is not yet usable by any agent.
2. **Given** a registered MCP server, **When** the administrator tests connectivity, **Then** the system attempts a secure connection and authentication and reports success or a specific, actionable failure reason without exposing credential values.
3. **Given** a successfully connected MCP server, **When** the administrator triggers capability discovery, **Then** the system retrieves and normalizes its tools, resources, and prompts, stores that metadata, and marks the server Healthy and available — with every newly discovered tool starting in an inactive, not-yet-usable state pending administrator activation.
4. **Given** an administrator attempts to register a server with an endpoint that resolves to a private, loopback, link-local, or cloud-metadata network address, **When** they save the registration, **Then** the system rejects it unless that destination has been explicitly allow-listed.
5. **Given** an administrator attempts to register a server whose endpoint and transport combination already exists in the registry, **When** they save the registration, **Then** the system rejects it and points to the existing entry.

---

### User Story 2 - Agent Executes an MCP Tool (Priority: P2)

A user has an agent configured with an enabled MCP tool. The user gives the agent an objective that requires that tool, and the agent discovers, selects, and calls it as part of its plan, incorporating the result into its final output.

**Why this priority**: This is the feature's core value proposition — an agent using an external capability exactly as it would a native tool — and depends on Story 1's registry and discovery already existing.

**Independent Test**: Can be fully tested by enabling one low-risk MCP tool for an agent, giving it an objective that requires that tool, and confirming the execution's history shows the MCP tool call alongside any native tool calls, with a result that reflects the MCP tool's output.

**Acceptance Scenarios**:

1. **Given** an agent configured with an enabled, Low-risk MCP tool, **When** the user gives an objective requiring that tool, **Then** the Agent Runtime calls it through the same tool-execution path used for native tools, and the execution's history shows the call with no visible distinction in how it was orchestrated.
2. **Given** an MCP tool call whose input does not match the tool's declared input schema, **When** the agent attempts the call, **Then** the system rejects the call before contacting the MCP server and records a standardized validation failure.
3. **Given** an MCP tool call returns a response that fails schema validation or exceeds the configured size limit, **When** the response is received, **Then** the system rejects it as a failed tool call rather than passing it to the agent.
4. **Given** an MCP tool's source server is disabled or unavailable, **When** an agent attempts to call that tool, **Then** the call fails with a clear, actionable error and the execution proceeds per its failure-handling policy rather than hanging indefinitely.

---

### User Story 3 - Approval for High-Risk MCP Actions (Priority: P3)

A user runs an agent whose plan calls an MCP tool classified as High or Critical risk (e.g., modifying or deleting an external record). Before that call executes, the system pauses and requests the user's approval, exactly as it does for native high-risk tools.

**Why this priority**: This is the safety mechanism that makes it acceptable to let MCP-connected agents take real actions on external systems; it must exist before any high-risk MCP tool is usable, but it builds directly on Story 2's execution path.

**Independent Test**: Can be fully tested by enabling a High- or Critical-risk MCP tool for an agent, triggering a plan that calls it, and confirming execution pauses in a Waiting-for-Approval state showing the intended action, target server, and parameters, proceeding or stopping strictly based on the user's decision.

**Acceptance Scenarios**:

1. **Given** an agent's plan includes a High- or Critical-risk MCP tool call, **When** execution reaches that step, **Then** the system pauses, shows the intended action, the target MCP server, and the call's parameters, and waits for an explicit decision.
2. **Given** a paused execution awaiting approval for an MCP tool call, **When** the user approves, **Then** the call executes and the decision is recorded in the audit trail alongside the platform's existing approval records.
3. **Given** an administrator has published an auto-approval policy that covers a specific MCP tool action, **When** an agent's plan calls that action under conditions the policy covers, **Then** the system proceeds without an interactive prompt and records that the action was policy-approved.
4. **Given** MCP tool output returned during a prior step contains text instructing the agent to bypass approval or ignore its instructions, **When** the agent reasons over that output, **Then** the approval requirement for any subsequent high-risk action is unaffected — the content is treated strictly as data.

---

### User Story 4 - Discover and Configure MCP Tools for an Agent (Priority: P4)

A user browses the MCP servers and tools available to them, reviews each tool's description, risk level, and required permissions, and enables or disables specific tools for one of their agents.

**Why this priority**: Users need to make an informed choice before an agent gains a new capability; this configuration experience is required before Story 2/3 executions can happen for any given agent, but the underlying registry (Story 1) must exist first.

**Independent Test**: Can be fully tested by opening an agent's tool configuration, confirming every enabled MCP server's available tools are listed with description/risk/permissions, enabling one, and confirming it now appears among the agent's active tools without affecting any other user's agents.

**Acceptance Scenarios**:

1. **Given** one or more MCP servers are enabled, **When** a user configures an agent's tools, **Then** they see each administrator-activated MCP tool's name, description, source server, risk level, and required permissions before deciding whether to enable it; tools awaiting administrator activation are not shown as selectable.
2. **Given** a user enables an MCP tool for their agent, **When** they save the configuration, **Then** enabling it does not grant that user any permission they did not already hold — only tools the user is independently authorized to use actually become callable at execution time.
3. **Given** a user disables an MCP tool for their agent, **When** the agent is next executed, **Then** the agent's plan does not call that tool even if it would otherwise have been selected.
4. **Given** a user without administrative rights, **When** they attempt to register, edit, or remove an MCP server, **Then** the action is denied.

---

### User Story 5 - Agent Uses MCP Resources and Prompts (Priority: P5)

A user configures an agent to use resources and prompts exposed by an enabled MCP server. During execution, the agent retrieves a resource's content on demand and uses an MCP-sourced prompt as reusable prompt content, both subject to the same authorization the agent already operates under.

**Why this priority**: Resources and prompts extend MCP's value beyond tool calls, but they are a smaller, independent slice layered on the tool-execution and permission machinery already proven in Stories 2-4.

**Independent Test**: Can be fully tested by enabling one MCP resource and one MCP prompt for an agent, running an objective that uses both, and confirming the resource fetch and prompt usage each appear in execution history with the correct source server attribution.

**Acceptance Scenarios**:

1. **Given** an agent configured with an enabled MCP resource, **When** its execution retrieves that resource's content, **Then** the retrieval is authorized, subject to the same limits as a tool call, and recorded in execution history.
2. **Given** an MCP prompt from an enabled server, **When** a user selects it for their agent, **Then** it appears alongside the platform's other reusable prompts rather than in a separate, disconnected list.
3. **Given** an MCP resource is retrieved during an execution, **When** the platform processes it, **Then** its content is not automatically ingested into any Knowledge Base — ingestion only happens if a user explicitly submits it through the existing document pipeline.
4. **Given** the server that supplied a prompt is later disabled, **When** a user views an agent that references that prompt, **Then** the prompt is clearly shown as unavailable rather than silently failing at execution time.
5. **Given** an MCP-sourced prompt is later updated upstream and re-discovered, **When** the next capability refresh completes, **Then** the platform's copy re-syncs to match the server's current definition; **given** a user wants a customized variant, **When** they attempt to edit it directly, **Then** they are instead offered to duplicate it into a new, independent native prompt that a later refresh will not overwrite.

---

### User Story 6 - Monitor Server Health and Refresh Capabilities (Priority: P6)

An administrator monitors the health of registered MCP servers over time, sees when one becomes degraded or unavailable, and refreshes its capabilities after the server's tools change upstream.

**Why this priority**: Ongoing operational visibility keeps the integration trustworthy over time, but the platform is functionally complete without it — administrators could otherwise only learn about problems from failed executions.

**Independent Test**: Can be fully tested by simulating a server becoming unreachable, confirming its health status changes and new tool calls against it are blocked, then restoring it and confirming health recovers and calls resume — all independent of any specific agent execution.

**Acceptance Scenarios**:

1. **Given** a registered MCP server, **When** its connection or authentication starts failing, **Then** its health status changes to reflect the specific failure category (Degraded, Unavailable, AuthenticationFailed, or ConfigurationError) without requiring a manual check.
2. **Given** a server in a non-Healthy state, **When** an agent execution would otherwise call one of its tools, **Then** that call is blocked with a clear error rather than being attempted against a known-bad server.
3. **Given** a server's upstream tools have changed since the last discovery, **When** an administrator refreshes its capabilities, **Then** the system reports what changed (added, removed, modified) rather than silently applying the change to agents already configured against the prior capability set.
4. **Given** a capability refresh fails, **When** the administrator checks the server, **Then** the previously cached, working capability set remains intact and usable rather than being cleared.

---

### User Story 7 - Rotate MCP Server Credentials (Priority: P7)

An administrator rotates a registered MCP server's credentials — for example, after a scheduled security rotation or a suspected leak — without needing to remove and re-register the server.

**Why this priority**: Credential hygiene is a security requirement rather than a feature users interact with directly; it is the lowest-priority story because the platform functions without frequent rotation, but it must exist before the integration can be considered production-ready.

**Independent Test**: Can be fully tested by rotating a server's stored credential to a new value and confirming subsequent tool calls authenticate with the new credential while the old one is no longer usable, with no plaintext credential ever visible in logs, audit records, or the administration interface.

**Acceptance Scenarios**:

1. **Given** a registered MCP server, **When** an administrator rotates its credentials, **Then** the server's stored credential is replaced, the change is attributed and timestamped, and no part of the credential value is exposed in the confirmation, logs, or audit trail.
2. **Given** credentials were just rotated, **When** the next tool call against that server is made, **Then** it authenticates using the new credential without requiring the server to be re-registered or its tools reconfigured.
3. **Given** an in-flight, already-approved MCP tool call is executing at the moment credentials are rotated, **When** that call completes or fails, **Then** the outcome and reason are recorded rather than the call silently disappearing.

---

### Edge Cases

- What happens when an MCP server returns content designed to manipulate the agent (indirect prompt injection)? It is treated strictly as untrusted data the agent reasons about — never as an instruction, permission grant, or approval override (Story 3, Scenario 4).
- What happens when two agents from different users call the same MCP server simultaneously in high volume? Per-server and per-tool concurrency and rate limits apply, and requests beyond the limit are rejected with an actionable error rather than silently queued indefinitely or allowed to overwhelm the external server.
- What happens when an MCP tool call cannot be confirmed as having succeeded or failed (e.g., the connection drops mid-call)? The system does not blindly retry a non-idempotent action; it is recorded as an ambiguous/failed outcome and surfaced to the user rather than silently assumed successful.
- What happens when an administrator removes a server that agents currently reference? The removal is blocked, listing which agents/tools still reference it, until those references are cleared; prior execution history that used that server remains intact and inspectable regardless.
- What happens when a discovered tool's risk level or permission requirements are missing or unclear from the server's own metadata? The tool defaults to the platform's most restrictive risk level until an administrator reviews and confirms its classification — and, per the activation requirement below, it is unusable by any user until that review happens regardless of what the server itself claims.
- What happens when a user tries to enable a newly discovered MCP tool that no administrator has yet reviewed? It does not appear as selectable — every discovered tool starts inactive and requires explicit administrator activation before any user can enable it, regardless of the risk level the server declares for it.
- What happens when an administrator attempts to register a server whose endpoint and transport are already registered? The registration is rejected and the administrator is pointed to the existing entry; endpoint plus transport must be unique platform-wide.
- What happens when a user attempts to directly edit the content of an MCP-sourced prompt? Direct edits are not permitted — the prompt remains a read-only mirror of the server's definition; the user instead duplicates it into an independent, editable native prompt.
- What happens when a user who previously had access to an MCP tool loses the underlying permission it requires (e.g., role change)? Subsequent executions can no longer call that tool for that user, even though the agent configuration still lists it as enabled.
- What happens when an MCP server's response is enormous or malformed? It is rejected as a failed call under the configured size/schema limits rather than being partially processed or passed through to the agent.
- What happens when a user attempts to view another user's agent's MCP tool configuration, execution history, or a server's stored credential? Access is denied and the attempt is recorded as a security event.
- What happens when an MCP server's declared capabilities change between two calls within the same execution (e.g., a tool is removed mid-run)? The call fails cleanly with an availability error rather than being attempted against stale, no-longer-valid metadata.
- What happens when local (stdio) MCP server support has not been explicitly enabled for a deployment? No local server registration is possible, and any attempt to register one is rejected — only remote, network-accessible servers are usable in that deployment.

## Requirements *(mandatory)*

### Functional Requirements — MCP Server Registry & Lifecycle

- **FR-001**: Administrators MUST be able to register a new MCP server by providing a name, description, endpoint, transport, authentication type, and credentials.
- **FR-002**: System MUST deny any connection to an MCP server that has not been explicitly registered and enabled by an administrator; no automatic or ad hoc discovery of unregistered servers is permitted.
- **FR-003**: Administrators MUST be able to update an MCP server's configuration, enable it, disable it, and remove it.
- **FR-004**: Disabling an MCP server MUST immediately make its tools, resources, and prompts unavailable to every agent, without requiring individual agents to be reconfigured.
- **FR-005**: Removing an MCP server that one or more agents currently reference MUST be blocked, with the referencing agents/tools listed to the administrator, until those references are cleared; prior execution history that used the server MUST remain intact and inspectable regardless.
- **FR-006**: System MUST assign every registered MCP server a stable identity so tool/resource/prompt references, audit records, and execution history remain resolvable even if the server's endpoint or configuration later changes. The combination of endpoint and transport MUST be unique across the registry; a registration attempt that duplicates an existing server's endpoint and transport MUST be rejected and point to the existing entry.
- **FR-007**: System MUST version an MCP server's configuration, incrementing a configuration version whenever an administrator changes it, and MUST record who changed what and when.
- **FR-008**: Administrators MUST be able to test connectivity to an MCP server on demand, independent of the scheduled health-check cycle, and see the outcome immediately.
- **FR-009**: System MUST support both remote MCP servers and local MCP servers, with local server support available only where an administrator has explicitly enabled local execution for the deployment.
- **FR-010**: System MUST NOT execute an arbitrary, user-supplied local command as an MCP server; only administrator-registered, pre-approved local server configurations may run.

### Functional Requirements — Capability Discovery & Caching

- **FR-011**: When a server is registered or its capabilities are manually refreshed, system MUST connect, authenticate, discover the server's available tools, resources, and prompts, normalize them into the platform's own metadata model, and record the outcome.
- **FR-012**: System MUST cache discovered capability metadata rather than rediscovering it on every agent execution.
- **FR-013**: Administrators MUST be able to manually trigger a capability refresh for a given server at any time.
- **FR-014**: System MUST support automatic periodic capability refresh on an administrator-configurable interval.
- **FR-015**: System MUST detect when a server's capability set has changed since the last discovery (tools/resources/prompts added, removed, or modified) and MUST surface that change rather than silently applying it to agents already configured against the prior set.
- **FR-016**: A capability discovery failure MUST NOT remove or corrupt the previously cached, working capability set — the server is marked unavailable/degraded while the last-known-good metadata remains visible for historical and audit purposes.
- **FR-017**: System MUST record, per capability snapshot, which protocol capabilities (tools/resources/prompts, and future capabilities as they are added) the server actually declared, so agents cannot be configured against capabilities the server never advertised.
- **FR-018**: System MUST make each discovered tool's version identifiable, so an agent or execution can be traced to the specific tool definition it used even after the tool later changes upstream.

### Functional Requirements — Tool Integration with the Existing Agent Framework

- **FR-019**: Every MCP tool MUST be exposed to the Agent Runtime through the same tool abstraction used by native platform tools; the Agent Runtime MUST NOT contain logic that branches on whether a tool is native or MCP-sourced.
- **FR-020**: An MCP tool's metadata exposed to agents and users MUST include, at minimum: tool name, display name, description, source server, input schema, output schema, declared capabilities, risk level, required permissions, current availability, and version.
- **FR-021**: System MUST classify every discovered MCP tool into the platform's existing Low/Medium/High/Critical risk levels and MUST map its declared requirements onto the platform's existing tool-permission vocabulary, extending that vocabulary only where an MCP tool's requirement has no existing equivalent.
- **FR-022**: Every newly discovered MCP tool MUST start in an inactive state and MUST NOT be selectable by any user until an administrator explicitly reviews and activates it — regardless of the risk level or permissions the tool's own server declares for it. A tool with no risk classification supplied by its server MUST default to the platform's most restrictive risk level pending that same administrator review.
- **FR-023**: Agents MUST be configurable to use specific MCP tools from specific servers exactly as they are configured to use native tools today, without a separate parallel configuration mechanism.
- **FR-024**: An MCP tool MUST NOT be available for an agent to use unless the tool's source server is enabled, the tool has been activated by an administrator, the tool itself is currently available, and the requesting user holds every permission the tool requires.
- **FR-025**: System MUST validate every MCP tool call's input against the tool's declared input schema before sending the request to the MCP server, rejecting invalid input (invalid JSON, missing required fields, wrong types, unexpected fields, oversized payloads) without contacting the server.
- **FR-026**: System MUST validate every MCP tool call's response against the tool's declared output schema and general safety limits (size, structure) before returning it to the Agent Runtime, rejecting malformed or oversized responses as a failed tool call rather than passing them through.

### Functional Requirements — Tool Execution, Approval & Untrusted Output

- **FR-027**: MCP tool execution MUST follow the same permission validation, approval gating, timeout, retry, and duplicate-call detection rules already enforced by the Agent Runtime for native tools, rather than a separate execution path with its own rules.
- **FR-028**: High-risk and Critical-risk MCP tool calls MUST pause execution and require explicit user approval before executing, unless covered by an existing administrator-published auto-approval policy — using the same approval mechanism, records, and audit trail already defined for native tools.
- **FR-029**: Every approval request for an MCP tool call MUST display the intended operation, the target MCP server, and the relevant parameters before the approving user decides.
- **FR-030**: System MUST treat all content returned by an MCP tool call, resource fetch, or prompt retrieval strictly as untrusted data for the agent to reason about; it MUST NOT be elevated to a system instruction, developer instruction, or authorization rule regardless of its content.
- **FR-031**: System MUST record every MCP tool call's execution metadata (server, tool, timing, status, approval outcome) in the same execution-history/audit mechanism used for native tool calls, so a user reviewing an execution sees native and MCP tool activity in one unified timeline.
- **FR-032**: A failed MCP tool call MUST be normalized into the platform's existing tool-failure/error handling (retry per policy, standardized error surfaced to the agent and to execution history) rather than a distinct MCP-specific failure path visible to the agent.
- **FR-033**: System MUST record a failure category (connection failure, authentication failure, authorization failure, timeout, rate limit, invalid request, invalid response, server error, protocol error, capability-discovery failure, server unavailable) for every failed MCP interaction, without leaking credentials or raw secret material into any failure record.
- **FR-034**: An MCP tool call MUST inherit the requesting user's own authorization for any underlying data it touches; it MUST NOT be usable to reach Knowledge Base content, files, memory, or other platform data the user is not already authorized to access.
- **FR-035**: A prompt-injection attempt embedded in MCP tool output, resource content, or prompt content MUST NOT change the effective permissions, risk level, or approval requirement of any subsequent action the agent takes.

### Functional Requirements — Resources

- **FR-036**: Users MUST be able to see which resources are available from MCP servers enabled for their agents, including each resource's name, description, and source server.
- **FR-037**: An agent MUST be able to retrieve the content of an MCP resource it is configured to use, subject to the same permission and approval rules that govern MCP tool calls.
- **FR-038**: System MUST NOT automatically ingest or index MCP resource content into the Knowledge Base/RAG system; any such ingestion requires the resource to be explicitly submitted through the existing document ingestion pipeline as a separate, deliberate action.
- **FR-039**: A resource fetched during an agent execution MUST be recorded in that execution's history in the same manner as a tool call, so its retrieval is auditable.
- **FR-040**: System MUST NOT allow resource retrieval to bypass Knowledge Base, file, or organizational authorization the executing user would otherwise be subject to.

### Functional Requirements — Prompts

- **FR-041**: MCP prompts discovered from an enabled server MUST be made available as reusable prompt resources through the platform's existing prompt management capability, rather than a separate MCP-only prompt list disconnected from it. An MCP-sourced prompt MUST remain a read-only mirror of its source server's definition, re-synchronized whenever that server's capabilities are refreshed; direct editing is not permitted — a user who wants a customized variant MUST duplicate it into a new, independent native prompt that subsequent refreshes do not overwrite.
- **FR-042**: Users MUST be able to see an MCP prompt's name, description, and source server before using it.
- **FR-043**: System MUST NOT allow an MCP prompt's content to override an agent's configured system instructions, constraints, or safety rules; a retrieved prompt is used as prompt content, not as elevated instruction.
- **FR-044**: If an MCP server that supplied a prompt is later disabled or removed, agents referencing that prompt MUST clearly show that the prompt is no longer available rather than silently failing.

### Functional Requirements — Security, Authentication & Credentials

- **FR-045**: MCP server credentials MUST be stored server-side only and MUST NEVER be transmitted to, rendered in, or retrievable by any frontend client.
- **FR-046**: MCP credentials MUST NEVER appear in plaintext in database records viewable outside the credential store, application logs, audit events, or execution history.
- **FR-047**: Administrators MUST be able to rotate an MCP server's credentials without requiring the server to be removed and re-registered, and without interrupting agent executions already in progress beyond what the rotation itself requires.
- **FR-048**: System MUST support, at minimum, API key, bearer token, and OAuth 2.0 client-credentials authentication to MCP servers; System MUST reject a remote server registration that specifies no authentication unless an administrator explicitly confirms an unauthenticated connection is intended.
- **FR-049**: Remote MCP connections MUST use an encrypted transport (TLS) by default; System MUST reject a remote server registration that would communicate over an unencrypted channel unless an administrator explicitly overrides that default for a documented reason.
- **FR-050**: System MUST validate a remote MCP server's configured endpoint before allowing connections to it, rejecting endpoints that resolve to private, loopback, link-local, or cloud-metadata network ranges unless an administrator has explicitly allow-listed that destination.
- **FR-051**: System MUST enforce a maximum response size and a maximum execution time for every MCP tool call, resource fetch, and prompt retrieval, terminating and failing the call cleanly if either is exceeded.
- **FR-052**: System MUST enforce configurable concurrency limits per MCP server so a single server cannot be overwhelmed by simultaneous requests from many executions at once.
- **FR-053**: System MUST enforce configurable rate limits on MCP requests per user, per agent, per server, and per tool, rejecting requests that exceed the applicable limit with an actionable error.
- **FR-054**: System MUST NOT automatically retry a non-idempotent MCP tool call after a failure whose success/failure state is ambiguous; only tool calls declared or known to be safely retryable MAY be retried automatically.

### Functional Requirements — Health, Observability & Audit

- **FR-055**: System MUST continuously and asynchronously monitor each enabled MCP server's health, reporting one of: Healthy, Degraded, Unavailable, AuthenticationFailed, ConfigurationError, or Unknown.
- **FR-056**: A server in a non-Healthy state MUST be clearly indicated to administrators and MUST prevent new tool calls from starting against that server until it recovers, without failing executions that do not use that server.
- **FR-057**: System MUST record connection latency, tool-call latency, request counts, failure counts, timeout counts, and rate-limit events per MCP server and per tool, discoverable through the platform's existing observability capability.
- **FR-058**: System MUST maintain an audit trail of MCP-specific administrative and security-relevant events (server registration/changes, enable/disable, credential rotation, health-state transitions, capability-discovery runs, cross-user access attempts) distinct from but cross-referenceable with per-execution tool-call audit records.
- **FR-059**: Every MCP-related audit record MUST exclude credential material and MUST cap how much of a request/response payload it retains, favoring safe metadata (identifiers, sizes, status, timing) over full payload capture.
- **FR-060**: A user attempting to view, configure, or control an MCP server, tool, resource, or prompt they are not authorized to manage MUST be denied, and that attempt MUST be recorded as a security event.

### Functional Requirements — Access, Permissions & Administration

- **FR-061**: Only administrators MUST be able to register, configure, enable, disable, remove, or rotate credentials for MCP servers; regular users MUST NOT have these capabilities.
- **FR-062**: Any authenticated user MUST be able to view which MCP servers, tools, resources, and prompts are available to them, including each tool's description, risk level, and required permissions, before choosing to enable it for their agent.
- **FR-063**: Users MUST be able to enable and disable specific MCP tools for their own agents, independent of other users' agent configurations.
- **FR-064**: Enabling an MCP tool for an agent MUST NOT itself grant the executing user any permission they do not already hold; the agent's effective access to that tool remains bounded by the executing user's own permissions at execution time.
- **FR-065**: System MUST provide a way for administrators to see, for a given MCP server, which agents and tools currently reference it before removing or substantially reconfiguring that server.
- **FR-066**: System MUST require every MCP-related administrative action (register, update, enable, disable, remove, rotate credentials, refresh capabilities) to be attributable to the authenticated administrator who performed it.

### Key Entities *(include if feature involves data)*

- **McpServer**: A registered external MCP server — name, description, endpoint, transport, authentication type, enabled/health status, owning administrator, configuration version, and discovery/health-check timestamps. The endpoint and transport combination is unique across the registry.
- **McpServerCredential**: The securely stored, server-side-only credential material for an `McpServer` (API key, bearer token, or OAuth client credentials), supporting rotation without re-registration; never exposed to any frontend client, log, or audit record in plaintext.
- **McpServerHealth**: The current and historical health state of an `McpServer` (Healthy, Degraded, Unavailable, AuthenticationFailed, ConfigurationError, Unknown), recorded asynchronously over time.
- **McpCapabilitySnapshot**: A versioned, cached record of everything an `McpServer` reported during a discovery run (its tools, resources, and prompts at that point in time), so execution and agent configuration reference a stable, point-in-time capability set rather than something that can shift mid-execution.
- **McpTool**: Normalized metadata for one tool from a capability snapshot — name, display name, description, source server, input/output schema, capabilities, risk level, required permissions, availability, version, and an administrator-controlled activation status (inactive-pending-review by default; unusable by any user until an administrator activates it, independent of the risk level the server itself declares) — exposed to the Agent Runtime through the platform's existing tool abstraction alongside native tools.
- **McpResource**: Normalized metadata for one resource from a capability snapshot — name, description, identifier, source server, content type, and availability — exposed through a normalized abstraction for on-demand retrieval during an execution.
- **McpPrompt**: Normalized metadata for one prompt from a capability snapshot, represented as a reusable, read-only prompt resource integrated with the platform's existing prompt management capability rather than a second, disconnected prompt catalog. It mirrors its source server's definition and re-syncs on capability refresh; a user wanting a customized variant duplicates it into an independent native prompt.
- **McpToolPermission**: The mapping of an `McpTool`'s declared requirements onto the platform's existing tool-permission and risk-level vocabulary, extended only where an MCP-specific requirement has no existing equivalent.
- **McpAuditLog**: The tamper-resistant record of MCP-specific administrative and security-relevant events (registration, configuration changes, enable/disable, credential rotation, health transitions, discovery runs, unauthorized-access attempts), distinct from but cross-referenceable with the existing per-execution tool-call audit trail.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can register a new MCP server, verify connectivity, and see its discovered tools available for agent configuration within 2 minutes, without engineering assistance.
- **SC-002**: 100% of MCP tool calls classified High or Critical risk either pause for interactive approval or execute only under a recorded administrator policy — never silently — matching the existing Agent Framework's guarantee for native tools.
- **SC-003**: 100% of completed or failed agent executions that used an MCP tool, resource, or prompt show that activity in the execution's unified history alongside native tool activity, with no gaps.
- **SC-004**: 0 MCP credentials are ever observable in application logs, audit records, execution history, or any frontend-delivered response, verified across all registered servers.
- **SC-005**: 0 agent executions access an MCP tool, resource, or prompt whose underlying data the executing user is not independently authorized to access, verified across all executions.
- **SC-006**: A server transitioning to Degraded or Unavailable health is reflected in its administration status within one health-check cycle, and no new tool call against that server is attempted while it remains unavailable.
- **SC-007**: 100% of MCP tool inputs and outputs that fail schema validation are rejected — inputs before being sent to the server, outputs before being trusted by the agent — with zero unvalidated payloads reaching agent reasoning.
- **SC-008**: Disabling an MCP server makes 100% of its tools, resources, and prompts unavailable to every agent immediately, with no agent able to invoke it afterward.
- **SC-009**: Rotating a server's credentials never causes an in-flight, already-approved tool call to silently fail without a recorded, user-visible reason.
- **SC-010**: Users can determine an MCP tool's risk level and required permissions before enabling it for their agent from a single view, without inspecting raw protocol metadata.
- **SC-011**: 0 newly discovered MCP tools are selectable by any user before an administrator explicitly activates them, verified across every discovery and capability-refresh event, regardless of the risk level the source server itself declares.

## Assumptions

- MCP server registration and management is administrator-only and platform-wide, not per-organization — consistent with how the existing Agent Framework's auto-approval policy mechanism is actually implemented today (organization/tenant scoping is reserved for a future multi-tenancy feature and is currently gated by the Administrator/Super User role platform-wide). True multi-tenant isolation for MCP is out of scope until platform-wide multi-tenancy ships.
- Local (stdio) MCP servers are supported only when an administrator has explicitly enabled local execution for the deployment; they are disabled by default and can never be registered from an arbitrary user-supplied command.
- v1 authentication support covers API keys, bearer tokens, and OAuth 2.0 client-credentials (machine-to-machine) grants, consistent with MCP servers being administrator-registered infrastructure integrations rather than per-user delegated connections. Interactive, per-user OAuth authorization-code flows are reserved as a future authentication mechanism, per the original request's "future authentication mechanisms defined by MCP."
- MCP tools are surfaced to agents through the same tool-selection mechanism users already use for native tools (the existing Agent-Tool association), keyed by a namespaced tool identifier, rather than a second, parallel per-agent enablement mechanism.
- The existing tool-permission vocabulary is extended with additional values where an MCP tool's declared requirement (e.g., deleting external data) has no existing equivalent, rather than introducing a second, disconnected permission system.
- MCP credentials are stored using the platform's existing secret-management approach, not a bespoke MCP-only secret store.
- The existing execution-history and audit infrastructure used for native tool calls is extended to also carry MCP tool/resource/prompt activity in one unified timeline; MCP-specific administrative events (server lifecycle, credential rotation, health transitions) are additionally captured in a dedicated MCP audit record, per the original request's explicit entity list.
- Default numeric limits (response size caps, timeouts, concurrency limits, rate limits, automatic capability-refresh interval) are system-provided, administrator-tunable defaults; exact values are a planning-phase decision.
- MCP Resources are exposed for discovery/browsing in v1; retrieving a resource's content happens on demand within an agent execution through the same permission/approval-gated path as a tool call, not a separate always-on synchronization process.
- MCP Prompts integrate with the platform's existing prompt management capability as sourced, reusable prompt resources rather than a second, disconnected prompt catalog.
- The supported MCP protocol version and its exact capability set (Tools, Resources, Prompts in this release; Sampling and Notifications deferred) follow whatever MCP specification version the platform targets at implementation time; future protocol-version changes are expected to be additive and isolated behind the MCP abstraction.
- Sampling, Notifications, an MCP marketplace, public MCP server hosting, automatic internet-wide server discovery, automatic RAG ingestion of MCP resources, automatic memory writes from MCP output, and multi-tenant collaboration are explicitly out of scope for this release, consistent with the original request; the data model allows their future addition without redesign.

## Dependencies

- The existing Agent Framework (Agent, AgentVersion, AgentTool, AgentExecution, AgentToolCall, AgentApproval, AgentPolicy, AgentAuditLog) and its Agent Runtime tool-execution pipeline, which this feature extends rather than duplicates.
- The existing AI Provider abstraction, RAG/Knowledge Base Engine, and Memory Engine, which MCP Resources and tool calls must route through rather than reimplement.
- The existing Prompt Library, which MCP Prompts integrate into as reusable prompt resources.
- The existing authentication/authorization infrastructure (roles, permission checks) and audit/observability infrastructure (structured logging, correlation IDs).
- The platform's existing secret-management approach for storing MCP credentials server-side.
- Background job processing infrastructure for health checks, capability refresh, and connection maintenance without blocking API requests.

## Risks

- **Static-to-dynamic catalog shift**: today's tool catalog is fixed at deployment time; MCP tools change as servers are registered, refreshed, or go unhealthy, which is a materially different lifecycle the planning phase must design for without breaking existing native tool behavior.
- **SSRF and credential exposure**: MCP is explicitly a high-risk external network boundary — incomplete endpoint validation or logging discipline could expose internal infrastructure or leak credentials.
- **Prompt injection via external content**: an MCP server is not a trusted party; insufficiently strict separation between "data" and "instructions" could let external content influence agent behavior or approval outcomes.
- **Capability drift**: a server can change its tools/schemas between discoveries, potentially breaking an agent's configuration or a previously-granted policy without warning if changes aren't surfaced.
- **Cascading unavailability**: an unresponsive MCP server could degrade agent executions system-wide if per-server timeouts, concurrency limits, and circuit breakers aren't consistently enforced.

## Security Threats Addressed

- Unauthorized or unknown server connections — mitigated by deny-by-default registration (FR-002).
- Server-Side Request Forgery via malicious or misconfigured endpoints — mitigated by endpoint and network-range validation (FR-050).
- Credential exposure via logs, audit trails, or frontend responses — mitigated by server-side-only storage and redaction (FR-045, FR-046, FR-059).
- Prompt injection escalation — mitigated by treating all MCP content as untrusted data (FR-030, FR-035, FR-043).
- Cross-user or cross-authorization data access via MCP — mitigated by permission inheritance (FR-034, FR-040, FR-064).
- Resource exhaustion (oversized responses, unbounded concurrency, retry storms) — mitigated by size, timeout, concurrency, and rate limits (FR-051 through FR-054).
- Silent unauthorized administrative changes — mitigated by attribution and audit requirements (FR-058, FR-066).
- A compromised or misconfigured server self-declaring a falsely low risk level to bypass approval gates — mitigated by mandatory administrator activation review of every newly discovered tool, independent of what the server itself claims (FR-022, FR-024).

## Open Questions

- Exact default values for response-size limits, timeouts, concurrency caps, rate limits, and the automatic capability-refresh interval — deferred to the planning phase as system-provided, admin-tunable defaults.
- Whether and when interactive, per-user OAuth delegation (authorization code + PKCE) will be needed for a future MCP server requiring end-user consent, versus the admin-registered service-credential model assumed for this release.
- The exact mechanism for composing today's static, deployment-time tool catalog with a dynamic, admin-managed MCP tool set is a planning-phase architectural decision, not a specification-level one.

## Migration Considerations

- No existing MCP data exists today; this feature is purely additive and requires no migration of existing records.
- The existing tool-permission vocabulary and tool-catalog composition mechanism will need to be extended to accommodate a dynamic source; this must be a backward-compatible extension of spec 020's contracts, not a breaking change to existing native tool registrations.
- Existing agents and their published versions are unaffected until an administrator registers an MCP server and a user opts an agent into using it.
