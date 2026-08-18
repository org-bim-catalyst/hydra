# Feature Specification: AI Agent Framework & Agent Runtime

**Feature Branch**: `020-ai-agent-framework`

**Created**: 2026-08-10

**Status**: Draft

**Input**: User description: "Build a production-ready AI Agent Framework for Ask Lucy. The framework must allow users to create and execute reusable AI agents capable of understanding objectives, planning multi-step tasks, selecting available tools, calling the AI Provider Engine, retrieving Knowledge Base information through RAG, accessing approved Memory, executing tools, maintaining task state, handling failures, requesting user approval when required, producing structured results, and maintaining an auditable execution history. The agent framework must orchestrate existing platform capabilities without duplicating Conversation Management, the Multi-Provider AI Engine, Knowledge Bases, Document Intelligence, RAG, Memory, or Prompt Management."

## Clarifications

### Session 2026-08-10

- Q: What is the maximum number of agent executions a single user may have running (Queued/Running/Paused/WaitingForApproval) at the same time? → A: Configurable per user/tier (admin-settable), defaulting to a modest cap
- Q: For v1, which conversation-integration modes must the Agent Framework support when starting an execution? → A: Support all three modes — existing conversation, new conversation, and standalone background task — with automatic linking when started from a conversation
- Q: Do administrator-defined auto-approval policies (AgentPolicy) apply per organization/tenant, or platform-wide across all tenants? → A: Per-tenant/organization — an admin's policy only auto-approves actions for users within their own organization

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create and Run a Simple Agent (Priority: P1)

A user creates an agent by giving it a name, description, instructions, a model, and an output format, then gives it an objective and receives a final result.

**Why this priority**: This is the smallest possible slice that proves the framework works end to end: definition, execution, and result. Without it, nothing else in the framework has value.

**Independent Test**: Can be fully tested by creating an agent with only instructions and a model (no tools, no knowledge bases), giving it a simple objective, and confirming it returns a final structured result that is persisted and retrievable afterward.

**Acceptance Scenarios**:

1. **Given** a user has filled in an agent's name, description, instructions, and model, **When** they save the agent, **Then** the agent is created in Draft status and is not yet executable by other users.
2. **Given** a saved agent, **When** the user provides an objective and starts execution, **Then** the system produces a final result in the agent's declared output format and stores it against that execution.
3. **Given** an agent with no tools configured, **When** it is executed, **Then** the system does not attempt any tool calls and completes using only the model's response.

---

### User Story 2 - Multi-Step Task Execution with Tools (Priority: P2)

A user configures a Task Agent with a set of tools (e.g., Knowledge Search, Document Search, Memory Search, File Read) and Knowledge Bases, then gives it an objective that requires several steps to complete.

**Why this priority**: This delivers the framework's core differentiator — planning and orchestrating multiple tool calls toward a goal — but depends on User Story 1's execution mechanics already existing.

**Independent Test**: Can be fully tested by creating an agent with at least one tool and one knowledge base, giving it an objective that requires retrieving information and synthesizing it, and confirming the execution history shows a plan with multiple steps, at least one tool call, and a final result that reflects retrieved content.

**Acceptance Scenarios**:

1. **Given** an agent configured with the Knowledge Search tool and a Knowledge Base, **When** the user gives an objective requiring information from that Knowledge Base, **Then** the agent produces a plan, executes a Knowledge Search step, and incorporates the retrieved content — with citations — into its final result.
2. **Given** a running multi-step execution, **When** one step's output is required as input to a later step, **Then** the later step only starts after the dependency step completes successfully.
3. **Given** a tool call that fails, **When** the agent's execution policy allows retries, **Then** the system retries the step up to its configured limit before marking it failed and adjusting the plan or surfacing the failure in the final result.

---

### User Story 3 - Approval for Sensitive Actions (Priority: P3)

A user runs an agent whose plan includes a tool call classified as High or Critical risk (e.g., sending an email, deleting a file). Before that action executes, the system pauses and asks the user to approve or reject it.

**Why this priority**: This is the primary safety mechanism that makes it acceptable to let agents take real actions on a user's behalf; it must exist before any high-risk tool is enabled for general use.

**Independent Test**: Can be fully tested by configuring an agent with a tool marked High or Critical risk, triggering a plan that calls it, and confirming execution pauses in a "Waiting for Approval" state, displays the intended action and its parameters, and only proceeds (or is cancelled) based on the user's decision — with that decision recorded.

**Acceptance Scenarios**:

1. **Given** an agent's plan includes a High-risk or Critical-risk tool call, **When** execution reaches that step, **Then** the system pauses execution, shows the user the intended action and its parameters, and waits for an explicit decision.
2. **Given** a paused execution awaiting approval, **When** the user approves, **Then** the tool call executes and the execution continues; the approval decision is recorded in the audit trail.
3. **Given** a paused execution awaiting approval, **When** the user rejects, **Then** the tool call does not execute, the step is marked accordingly, and the agent either adjusts its plan or ends the execution with an explanation.
4. **Given** an administrator has published a policy that pre-approves a specific tool action, **When** an agent's plan calls that action under conditions the policy covers, **Then** the system executes it without an interactive approval prompt and records that the action was auto-approved under that policy.

---

### User Story 4 - Real-Time Execution Visibility (Priority: P4)

While an agent is running, a user watches its current step, tool activity, token usage, and estimated cost update in near real time, and can pause or cancel it.

**Why this priority**: Long-running, multi-step, tool-using executions are otherwise a black box; visibility and control are necessary for user trust, but the framework is functionally complete without live streaming (users could still poll for status).

**Independent Test**: Can be fully tested by starting a multi-step execution and confirming the user interface reflects step transitions, tool call start/completion, and running usage/cost totals as they occur, and that a cancel action stops the execution promptly.

**Acceptance Scenarios**:

1. **Given** a running execution, **When** it moves from one step to the next or starts/finishes a tool call, **Then** the user sees that change reflected without needing to manually refresh.
2. **Given** a running execution, **When** the user chooses to cancel it, **Then** the execution stops at the next safe point, no further tool calls are made, and the execution is marked Cancelled.
3. **Given** a running execution, **When** the user chooses to pause it, **Then** the execution suspends after completing its current step and can later be resumed from that point.
4. **Given** an execution in progress, **When** the user inspects it, **Then** they can see which agent, version, and model are running, but cannot see private model reasoning — only step descriptions, tool calls, decisions, and results.

---

### User Story 5 - Execution History & Audit (Priority: P5)

A user reviews a list of past agent executions and drills into any one of them to see its objective, status, duration, model, provider, token usage, cost, steps, tool calls, errors, approvals, and final output.

**Why this priority**: Historical visibility is essential for trust and troubleshooting but is not required for a single execution to succeed; it builds on data already captured during execution (Stories 1-4).

**Independent Test**: Can be fully tested by running an agent to completion and then, independently of that run, opening the execution history and confirming every recorded field (steps, tool calls, cost, approvals, citations, final output) is present and matches what happened during the run.

**Acceptance Scenarios**:

1. **Given** a completed execution, **When** the user opens its history entry, **Then** they see the full step-by-step timeline, every tool call with its inputs/outputs, any approval decisions, and the final result with citations where applicable.
2. **Given** a failed execution, **When** the user opens its history entry, **Then** they see which step failed, the recorded error, and how many retries were attempted.
3. **Given** a user without access to a particular execution, **When** they attempt to view it, **Then** access is denied.

---

### User Story 6 - Agent Versioning & Testing (Priority: P6)

A user iterates on an agent's instructions, tools, or model in a test environment, confirms it behaves correctly, and then publishes an immutable version. Executions always record which exact version they used.

**Why this priority**: Safe iteration and reproducibility matter for a production framework, but a user can create value from Stories 1-5 even while working only against a single unversioned draft; versioning formalizes change management once the framework is otherwise proven.

**Independent Test**: Can be fully tested by editing a draft agent, running it in the test console, publishing it as Version 1, editing it again, publishing Version 2, and confirming that an execution started against Version 1 still reports Version 1 in its history even after Version 2 exists.

**Acceptance Scenarios**:

1. **Given** a draft agent, **When** the user runs it from the testing console, **Then** the execution behaves like a normal run (including approval gates for high-risk actions) but is flagged as a test execution and never publishes a new agent version by itself.
2. **Given** a published agent version, **When** the user edits the agent and publishes again, **Then** a new version is created, the prior version remains unchanged, and both versions remain individually inspectable.
3. **Given** an execution that started under a specific agent version, **When** a newer version is later published, **Then** the execution's history continues to reference the original version it ran under.
4. **Given** a user duplicates an agent, **When** the duplicate is saved, **Then** it exists as an independent Draft agent that can be modified without affecting the original.
5. **Given** an archived agent, **When** the user restores it, **Then** it becomes available for execution again with its version history intact.

---

### Edge Cases

- What happens when an execution reaches its maximum step count, maximum execution duration, maximum token budget, or maximum cost limit? The execution stops (or pauses, per its execution policy), the user is notified, and the specific limit that was hit is recorded as the reason.
- What happens when an agent repeatedly issues the same tool call with the same inputs? The runtime detects the duplicate pattern and halts the execution rather than looping indefinitely.
- What happens when a tool's output fails schema validation? The tool call is recorded as failed with a standardized error; the step is retried (if the policy allows) or the execution fails that step with an actionable message.
- What happens when the underlying AI provider is unavailable or returns an error mid-execution? The execution treats it as a provider failure, applies retry/backoff per policy, and surfaces a clear error if retries are exhausted.
- What happens when retrieved document or tool-output content contains instructions aimed at the agent (prompt injection)? That content is treated strictly as data for the agent to reason about, never as new system-level instructions, and cannot override the agent's configured instructions or safety rules.
- What happens when two of a user's executions attempt to modify the same protected resource at the same time? The second conflicting action is rejected with an actionable error rather than silently overwriting or queuing indefinitely; the user or agent must retry.
- What happens when a user starts an execution against an agent, tool, or Knowledge Base they no longer have access to (e.g., it was unshared or deleted after the agent was configured)? The execution fails that step with a clear permission/availability error rather than silently skipping it.
- What happens when a user attempts to view or control (cancel/pause/approve) another user's execution? Access is denied and the attempt is recorded as a security event.
- What happens when an execution is left "Waiting for Approval" and the user never responds? It remains paused indefinitely (consuming no further budget) until the user approves, rejects, or cancels the overall execution; it does not silently expire.
- What happens when an agent's configured Knowledge Base, Memory, or file access differs from what the executing user is currently permitted to see? The agent's effective access is the intersection of what it's configured for and what the executing user is authorized for — it is never broader.

## Requirements *(mandatory)*

### Functional Requirements — Agent Definition & Lifecycle

- **FR-001**: Users MUST be able to create an agent with a name, description, instructions, a selected model, selected tools, selected Knowledge Bases, a memory access configuration, execution limits, and a declared output format.
- **FR-002**: System MUST support agent status transitions: Draft, Published, Archived, and Restored (from Archived back to an executable state).
- **FR-003**: Users MUST be able to update, duplicate, archive, restore, and delete their own agents.
- **FR-004**: System MUST support agent instructions composed of distinct categories: system instructions, objectives, constraints, behavioral rules, output requirements, tool usage rules, and safety rules.
- **FR-005**: System MUST ensure user-provided variables and any externally retrieved content can never override or be treated as system-level agent instructions.
- **FR-006**: System MUST allow a user to test an agent (draft or published) in an isolated test mode before relying on it, without that test run itself publishing a new version.

### Functional Requirements — Versioning

- **FR-007**: System MUST create an immutable version snapshot — including instructions, model, tools, Knowledge Bases, memory configuration, and execution policy — whenever a user publishes an agent.
- **FR-008**: System MUST record who published each version, when, and an optional change description.
- **FR-009**: Every execution MUST reference the exact agent version it ran under, and that reference MUST remain accurate even after newer versions are published.
- **FR-010**: System MUST prevent modification of a published version's content; changes require publishing a new version.

### Functional Requirements — Execution & Planning

- **FR-011**: Users MUST be able to start an execution by supplying an objective to a chosen agent (and version, in test mode).
- **FR-012**: System MUST produce a plan consisting of goal, steps, dependencies, and expected outputs before or as part of executing multi-step objectives.
- **FR-013**: Each plan step MUST track an identifier, description, type, status, input, output, associated tool (if any), start/end time, and error (if any).
- **FR-014**: System MUST support step statuses: Pending, Running, Completed, Failed, Skipped, Cancelled, and WaitingForApproval.
- **FR-015**: System MUST support execution statuses: Queued, Running, Paused, WaitingForApproval, Completed, Failed, and Cancelled.
- **FR-016**: Users MUST be able to pause, resume, and cancel a running execution.
- **FR-017**: System MUST execute long-running agent tasks without holding the initiating request open; execution MUST continue in the background and be resumable through status queries or event streams.
- **FR-018**: System MUST support step dependencies such that a step does not start until the steps it depends on have completed.
- **FR-019**: System MUST support conditional execution, where a step's execution depends on the outcome of a prior step.

### Functional Requirements — Tools & Permissions

- **FR-020**: System MUST expose a common tool abstraction where every tool declares a name, description, typed input schema, typed output schema, required permissions, and a risk level (Low, Medium, High, or Critical).
- **FR-021**: System MUST validate tool inputs before execution and tool outputs after execution against their declared schemas; invalid inputs/outputs MUST be rejected with a standardized error rather than passed through.
- **FR-022**: System MUST verify that the executing user holds every permission a tool call requires before that call is allowed to run.
- **FR-023**: An agent's effective tool permissions MUST never exceed the permissions of the user on whose behalf it is executing.
- **FR-024**: System MUST provide initial tools that expose existing platform capabilities — conversation access, Knowledge Search, Document Search, Memory Search, Memory Write (creates a proposal only, subject to the approval policy in FR-031), Prompt execution, File Read, and File Metadata — without re-implementing the underlying capability.

### Functional Requirements — Approval

- **FR-025**: System MUST pause execution and request explicit user approval before any High-risk or Critical-risk tool call executes, unless a published administrator policy — scoped to the executing user's own organization/tenant — specifically authorizes that action to proceed without interactive approval.
- **FR-026**: System MUST support an administrator-managed policy mechanism that pre-approves specific tool actions under defined conditions, and MUST record whenever an action proceeds under such a policy instead of interactive approval. A policy published by one organization's administrator MUST NOT affect approval behavior for users in a different organization/tenant.
- **FR-027**: Every approval request MUST display the intended action and its relevant parameters to the approving user before a decision is made.
- **FR-028**: Every approval decision (who, what, when, approved/rejected, and whether it was interactive or policy-based) MUST be permanently recorded in the audit trail.

### Functional Requirements — Memory & Knowledge Integration

- **FR-029**: Agents MUST access Knowledge Bases, semantic/hybrid search, and citation retrieval exclusively through the existing RAG Engine's abstraction; the Agent Framework MUST NOT implement its own retrieval or vector search logic.
- **FR-030**: Agents MUST access long-term and short-term memory exclusively through the existing Memory Engine's abstraction.
- **FR-031**: System MUST NOT allow an agent to write to long-term memory automatically; every memory write MUST be governed by a configurable memory policy, and writes outside that policy require explicit user approval.
- **FR-032**: Agents MUST resolve model and provider selection exclusively through the existing AI Provider Engine's abstraction; the Agent Framework MUST NOT call any AI provider directly.
- **FR-033**: Agents MUST consume existing Prompt Management capabilities (e.g., reusable prompts) through their existing abstraction rather than duplicating prompt storage or templating.

### Functional Requirements — Observability & Events

- **FR-034**: System MUST emit a standardized set of execution events (including, at minimum: execution started, plan created, step started/completed/failed, tool call started/completed, approval requested/granted/rejected, execution completed/failed/cancelled), each carrying execution ID, agent ID, agent version, step ID (where applicable), timestamp, event type, status, and safe metadata.
- **FR-035**: System MUST NOT expose or persist private model chain-of-thought or hidden reasoning in any event, step record, or execution history; only concise summaries, decisions, tool calls, and results are shown.
- **FR-036**: Users MUST be able to see, for a running or completed execution: which agent and version are/were running, which model, which tools were called, which Knowledge Bases were queried, which memories were used, status, duration, token usage, estimated cost, errors, and approval requests.

### Functional Requirements — Failure Handling, Loop Protection & Budgets

- **FR-037**: System MUST support configurable retry with exponential backoff for transient tool and provider failures, up to a maximum retry count.
- **FR-038**: System MUST support configurable timeouts at both the step and overall execution level.
- **FR-039**: System MUST detect and halt duplicate/repeated identical tool calls to prevent infinite loops.
- **FR-040**: System MUST enforce configurable maximums for step count, execution duration, token consumption, cost, tool call count, and retry count; exceeding any limit MUST stop or pause the execution, notify the user, and record which limit was exceeded.
- **FR-041**: When a conflicting action targets a protected resource that another of the user's in-flight executions is already modifying, the system MUST reject the newer conflicting action with an actionable error rather than allowing both to proceed silently.

### Functional Requirements — Concurrency & Rate Limits

- **FR-042**: System MUST enforce a maximum number of concurrent executions (status Running, Paused, or WaitingForApproval) a single user may have at once. This cap MUST be configurable per user or subscription tier by an administrator and MUST default to a modest platform-provided value out of the box.
- **FR-043**: When a user attempts to start a new execution while already at their concurrency cap, the system MUST reject the new execution with an actionable error — consistent with FR-041's reject-on-conflict precedent — rather than silently exceeding the cap or queuing indefinitely.

### Functional Requirements — Output & Citations

- **FR-044**: Agents MUST declare their output format (at minimum: Plain Text, Markdown, JSON, Structured Output, or Files) as part of their configuration, and execution results MUST conform to that declared format.
- **FR-045**: When an execution's result draws on Knowledge Base or RAG content, the system MUST preserve and expose citations to the original source so the user can inspect them.

### Functional Requirements — Security & Access

- **FR-046**: Every agent execution MUST run under the authenticated identity of the user who started it, and every action it takes MUST be authorized as if that user performed it directly.
- **FR-047**: Any authenticated user MUST be able to create and execute agents; the system MUST NOT restrict agent creation or execution by role or subscription tier in this release.
- **FR-048**: An agent, its tools, its executions, and its history MUST be visible and controllable only by the user who owns them (or an authorized administrator); cross-user access MUST be denied and logged as a security event.
- **FR-049**: An agent's access to a Knowledge Base, memory, or file MUST always be constrained to what the executing user is independently authorized to access, regardless of how the agent itself is configured.
- **FR-050**: System MUST record, for every execution: the agent, the agent version, the user, every tool call, every approval decision, every permission decision, every error, and the final completion status, in a tamper-resistant audit trail that avoids storing unnecessary sensitive content.

### Functional Requirements — Conversation Integration

- **FR-051**: Users MUST be able to start an agent execution in any of three modes: inside an existing conversation, as a new conversation created for the run, or as a standalone background task with no associated conversation.
- **FR-052**: When an execution is started from within an existing conversation, or creates a new conversation for itself, the system MUST link that execution to the resulting conversation so the conversation's history and the execution's history remain cross-referenceable.
- **FR-053**: A standalone background-task execution MUST NOT require or implicitly create a conversation; its results remain fully inspectable through execution history alone.

### Key Entities *(include if feature involves data)*

- **Agent**: A user-owned, reusable definition of an AI assistant with a purpose — name, description, current draft configuration, status (Draft/Published/Archived), and its type (Conversational, Research, Document, Knowledge, or Task, with room for future types).
- **AgentVersion**: An immutable, published snapshot of an Agent's instructions, model selection, tools, Knowledge Bases, memory configuration, and execution policy, along with who published it, when, and why (change description). Executions always reference a specific AgentVersion.
- **AgentTool**: The association between an Agent (or AgentVersion) and a tool it is allowed to use, including any tool-specific configuration.
- **AgentToolPermission**: The set of permissions (e.g., ReadKnowledge, ReadMemory, ReadFile, WriteFile, ExternalNetwork, SendEmail, ExecuteCode, ModifyData, HighRiskOperation) a given tool requires, and the risk level that grants it.
- **AgentKnowledgeBase**: The association between an Agent (or AgentVersion) and a Knowledge Base it is permitted to search, inheriting the underlying Knowledge Base's access rules.
- **AgentMemoryPolicy**: The configuration governing whether and how an agent may read, write, or update long-term memory, including any pre-approved write categories.
- **AgentExecution**: A single run of an AgentVersion against a user-supplied objective — its status, timing, its conversation-integration mode (existing conversation / new conversation / standalone background task) and linked conversation where applicable, and whether it was a test or production run.
- **AgentExecutionStep**: A single step within an execution's plan — its type, status, input, output, associated tool, timing, and error (if any).
- **AgentExecutionEvent**: A timestamped, typed event emitted during an execution for observability/streaming purposes, carrying safe metadata only.
- **AgentToolCall**: A specific invocation of a tool within a step — its validated input, validated output (or failure), timing, and outcome.
- **AgentApproval**: A record of a pause-for-approval request — the intended action and parameters, who decided, when, the decision, and whether it was interactive or policy-based.
- **AgentExecutionError**: A structured record of a failure encountered during execution — where it occurred, its category, and actionable detail.
- **AgentExecutionUsage**: Recorded token consumption (input, output, and reasoning tokens where available) and tool-call counts for an execution.
- **AgentExecutionCost**: The estimated monetary cost associated with an execution, derived from usage.
- **AgentPolicy**: An administrator-defined rule, scoped to a single organization/tenant, that pre-approves specific tool actions under defined conditions for users within that organization only, removing the need for interactive approval when matched.
- **AgentAuditLog**: The tamper-resistant record of security-relevant decisions (permission checks, approvals, cross-user access attempts) tied to agent executions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time user can create a working agent and receive a successful execution result within 5 minutes of opening the agent builder.
- **SC-002**: Users observe a running execution's current step and tool activity update within 2 seconds of the underlying change occurring, without manually refreshing.
- **SC-003**: 100% of High-risk and Critical-risk tool actions either pause for interactive user approval or proceed only under a recorded administrator policy — never silently.
- **SC-004**: 100% of completed or failed executions can be fully traced afterward — every step, tool call, approval, and cost figure is present in the execution's history with no gaps.
- **SC-005**: 0 executions access a Knowledge Base, memory record, or file the executing user is not independently authorized to access, verified across all executions.
- **SC-006**: 100% of executions that exceed a configured step, time, token, or cost limit are automatically stopped or paused, with the specific limit that was hit recorded and shown to the user.
- **SC-007**: Users can run an agent in test mode and confirm zero unintended changes to production data across all test executions.
- **SC-008**: 95% of executions whose result draws on Knowledge Base content present inspectable source citations to the user.
- **SC-009**: A user cancelling a running execution sees it reach a stopped state within 5 seconds.
- **SC-010**: 100% of attempts by a user to view or control another user's agent, execution, or execution history are denied and recorded as a security event.

## Assumptions

- The existing AI Provider Engine, RAG Engine, Knowledge Base Engine, Memory Engine, Prompt Engine, and Conversation Management capabilities are available, stable, and exposed through abstractions the Agent Framework can consume without modification to their internals.
- Background/asynchronous job processing infrastructure either already exists or will be introduced as part of this feature so that long-running executions never hold an HTTP request open; the specific mechanism is a planning-phase decision.
- Real-time progress updates will use a provider-independent streaming mechanism (Server-Sent Events preferred, per the platform's architecture conventions); the exact transport is a planning-phase decision.
- Agents and their executions are private to the user who created them in this release; no team/organization sharing of agent definitions is included (an `AgentShare` concept is reserved for a future release, per the explicit "Out of Scope" list).
- Recurring/scheduled agent execution, MCP tool integration, workflow automation, an agent marketplace, and billing enforcement are explicitly out of scope for this release, consistent with the feature request; the data model allows for their future addition without redesign.
- Default values for execution limits (max steps, max duration, max tokens, max cost, max retries) will be system-provided sane defaults that a user can tighten per agent; the exact default values are a planning-phase decision.
- The default per-user concurrent-execution cap (FR-042), and any tier-specific overrides, are a planning-phase decision; this spec only requires that such a cap exists, is enforced, and is administrator-configurable.
- A paused "Waiting for Approval" execution consumes no further token/tool budget while paused and remains paused indefinitely until the user approves, rejects, or cancels it — it does not silently expire.
- Initial tools are limited to wrapping existing internal platform capabilities (conversation, knowledge search, document search, memory search, prompt execution, file read/metadata); external/network-calling tools (web search, HTTP APIs, email, etc.) are reserved as future tools and are not required to ship working in this release.
- "Testing" an agent uses real, permission-scoped platform data for reads (e.g., real Knowledge Base search results) but is still subject to every approval gate and permission check that a production run would be subject to, so no production data is modified without the same explicit approval a live run would require.
