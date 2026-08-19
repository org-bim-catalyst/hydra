# Feature Specification: Workflow & Tool Orchestration Engine

**Feature Branch**: `022-workflow-orchestration-engine`

**Created**: 2026-08-11

**Status**: Draft

**Input**: User description: "Build a production-ready Workflow & Tool Orchestration Engine for Ask Lucy. The workflow engine must allow users to visually and programmatically construct reusable workflows that combine AI models, Prompts, Agents, RAG, Memory, Documents, Files, MCP tools, Native tools, Human approval, Conditional logic, Parallel execution, Sequential execution, Transformations, Validation, Notifications, and future external integrations, with deterministic orchestration and AI-powered steps only where explicitly configured. It must coexist with — not replace — the existing AI Agent Runtime: agents are goal-driven with dynamic AI planning, workflows are explicit, predefined, deterministic processes, and an Agent may itself be one step inside a workflow."

## Clarifications

### Session 2026-08-11

- Q: Should the workflow engine enforce a per-user cap on concurrent workflow executions, the way the existing Agent Framework caps concurrent agent executions? → A: Yes, admin-configurable cap, consistent with the Agent Framework precedent (spec 020 FR-042/043).
- Q: The original request lists "Autonomous financial transactions" as out of scope but also lists "Financial operations" as an example sensitive operation requiring approval (FR-061) — how should this be resolved? → A: Approval-gated, not blocked — "autonomous" is read as "without approval," which FR-061 already prevents; no categorical prohibition on financial-operation nodes/tools is introduced.
- Q: Who should be able to create, publish, and execute workflows in this release? → A: Any authenticated user, with no role or subscription-tier restriction, consistent with the Agent Framework precedent (spec 020 FR-047).
- Q: Should a workflow's name be required to be unique? → A: Unique per owner, case-insensitive, consistent with the Prompt Library precedent (spec 019).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create and Run a Simple Deterministic Workflow (Priority: P1)

A user builds a workflow with a Start step, one processing step, and an End step; defines its inputs and outputs; saves it as a draft; and runs it by supplying input values, receiving a final output.

**Why this priority**: This is the smallest slice that proves the engine's core promise — an explicit, predefined sequence of steps executes deterministically end to end, producing a typed result. Without it, nothing else in the engine has value.

**Independent Test**: Can be fully tested by creating a workflow with Start → Transform → End, defining one input and one output, saving it, running it with a supplied input value, and confirming the execution produces the expected output value and is retrievable afterward.

**Acceptance Scenarios**:

1. **Given** a user has added a Start step, one step, and an End step and connected them in order, **When** they save the workflow, **Then** it is created in Draft status and is not yet runnable by other users.
2. **Given** a saved draft workflow with declared inputs, **When** the user provides input values and starts execution, **Then** the system runs each step in order and produces output values matching the workflow's declared Output schema.
3. **Given** a workflow step, **When** it references an earlier step's output (e.g. `{{steps.extract_document.text}}`), **Then** the runtime resolves that reference to the actual value produced during that specific execution.

---

### User Story 2 - Visually Design a Multi-Step Workflow (Priority: P2)

A user opens the visual workflow designer, drags nodes from a searchable palette onto a canvas, connects them, configures each node's settings, and saves the result as a draft — without writing code.

**Why this priority**: The visual designer is the primary way most users will construct workflows; it depends on Story 1's execution model already existing but delivers the product's signature "build it, don't code it" experience.

**Independent Test**: Can be fully tested by opening an empty canvas, adding at least three nodes of different types via the palette, connecting them, configuring each node's inputs, saving as a draft, and confirming the saved definition reopens with the same layout, connections, and configuration.

**Acceptance Scenarios**:

1. **Given** an empty canvas, **When** the user searches the node palette and drags a node onto the canvas, **Then** the node appears configurable with its inputs, outputs, and settings panel.
2. **Given** two nodes on the canvas, **When** the user draws a connection from one node's output to another's input, **Then** the connection is validated for type compatibility and persisted with the workflow.
3. **Given** unsaved changes on the canvas, **When** the user attempts to navigate away, **Then** the system shows an unsaved-changes indicator and the user can save, discard, or cancel.
4. **Given** a workflow with an editing history, **When** the user performs undo or redo, **Then** the canvas state (nodes, connections, configuration) reverts or reapplies accordingly.
5. **Given** a change that produces a validation error (e.g., a disconnected node or an invalid variable reference), **When** the user views the validation panel, **Then** the specific error and its location are shown before the user attempts to publish.

---

### User Story 3 - Publish, Version, and Execute Against Immutable Versions (Priority: P3)

A user iterates on a draft workflow, publishes it as Version 1, keeps editing, publishes Version 2, and confirms that an execution started against Version 1 continues to report Version 1 even after Version 2 exists.

**Why this priority**: Reproducibility and safe iteration are essential for a production orchestration engine, but a user can already get value from Stories 1-2 while working only against an unversioned draft; versioning formalizes change management once the core mechanics are proven.

**Independent Test**: Can be fully tested by publishing a workflow as Version 1, running it, publishing an edited Version 2, and confirming the Version 1 execution's history still references Version 1's exact definition while new executions default to Version 2.

**Acceptance Scenarios**:

1. **Given** a draft workflow that passes validation, **When** the user publishes it, **Then** an immutable version snapshot (nodes, connections, variables, configuration, inputs, outputs) is created and the workflow's status becomes Published.
2. **Given** a published version, **When** the user attempts to modify its definition directly, **Then** the system rejects the change; the user must edit the draft and publish a new version instead.
3. **Given** a workflow with multiple published versions, **When** the user starts a new execution, **Then** the execution records exactly which version it ran under, and that reference never changes even as newer versions are published.
4. **Given** a published workflow, **When** the user duplicates, archives, or restores it, **Then** the resulting workflow behaves as an independent entity (duplicate) or transitions status (archive/restore) without altering any other version's immutable content.

---

### User Story 4 - Conditional Branching and Parallel Execution with Merge (Priority: P4)

A user builds a workflow where a Condition node routes execution down different branches depending on a prior step's result, and a separate section runs independent branches in parallel before a Merge node combines their outputs.

**Why this priority**: Non-linear control flow is what distinguishes an orchestration engine from a simple pipeline; it depends on the linear execution model (Story 1) already working correctly.

**Independent Test**: Can be fully tested by building a workflow with a Condition node whose branches produce different outputs depending on input, running it with inputs that trigger each branch, and separately building a workflow with two parallel branches feeding a Merge node, confirming the merged output reflects the configured merge strategy.

**Acceptance Scenarios**:

1. **Given** a Condition node configured with a boolean expression, **When** the workflow executes, **Then** only the branch matching the evaluated condition runs; the other branch is skipped and recorded as such.
2. **Given** a Parallel node with multiple independent branches, **When** the workflow executes, **Then** all branches run concurrently within configured concurrency limits, and each branch's steps only see the outputs available to it.
3. **Given** a Merge node configured with a specific strategy (All Completed, First Completed, Any Completed, or Collect All), **When** its upstream parallel branches finish, **Then** the merge behaves exactly per that strategy and the choice is visible in the execution's history.
4. **Given** a workflow containing a bounded loop (e.g., "process each item in a collection"), **When** the loop executes, **Then** it stops at its configured maximum iteration count even if its exit condition is never satisfied, and this is recorded as the stop reason.
5. **Given** a workflow definition with a circular dependency that the engine does not support, **When** the user attempts to validate or publish it, **Then** publishing is blocked with a specific error identifying the cycle.

---

### User Story 5 - Human Approval Gate for Sensitive Steps (Priority: P5)

A user builds a workflow that generates a report and, before publishing it, pauses for a human to approve, reject, or request changes; the workflow only proceeds to the next step based on that decision.

**Why this priority**: Human-in-the-loop control is the primary safety mechanism that makes it acceptable to let a workflow take real, potentially sensitive actions; it must exist before any high-risk step type is usable in a production workflow.

**Independent Test**: Can be fully tested by placing a Human Approval node before a sensitive step, running the workflow to that point, confirming execution pauses in WaitingForApproval with the pending action and its parameters visible, and confirming an Approve, Reject, or Request Changes decision drives the subsequent behavior deterministically.

**Acceptance Scenarios**:

1. **Given** a running execution reaches a Human Approval node, **When** it pauses, **Then** the system persists the execution's state, notifies the authorized approver, and displays the requested action for review.
2. **Given** a paused execution awaiting approval, **When** the approver approves, **Then** execution resumes at the next node; the decision (who, when, what) is recorded in the audit trail.
3. **Given** a paused execution awaiting approval, **When** the approver rejects or requests changes, **Then** execution follows the workflow's configured rejection path (terminate, branch, or return to a prior step) rather than silently continuing.
4. **Given** a workflow step classified as a sensitive operation (e.g., sending communication, modifying external data, deleting data, executing code), **When** the workflow's approval policy does not explicitly and specifically authorize it to bypass interactive approval, **Then** the system enforces approval regardless of the workflow author's node configuration.
5. **Given** a Human Approval node with a configured timeout, **When** that timeout elapses without a decision, **Then** the system applies the node's configured timeout failure policy, records the timeout, and notifies the initiating user.

---

### User Story 6 - Real-Time Monitoring, Pause, Resume, and Cancel (Priority: P6)

While a workflow executes, a user watches the current node, per-node status, and running usage/cost update in near real time on an execution monitor, and can pause, resume, or cancel the run at any point.

**Why this priority**: Long-running, multi-node workflows are otherwise an opaque black box; visibility and control build user trust, but the engine is functionally complete without live streaming (a user could poll for status instead), so this follows the control-flow mechanics it depends on.

**Independent Test**: Can be fully tested by starting a multi-node execution and confirming the monitoring UI reflects node transitions as they occur, then issuing pause, resume, and cancel actions and confirming each takes effect at the next safe point.

**Acceptance Scenarios**:

1. **Given** a running execution, **When** a node starts, completes, fails, or retries, **Then** the user sees that change reflected without manually refreshing.
2. **Given** a running execution, **When** the user pauses it, **Then** the current node finishes (or reaches a safe checkpoint), no further nodes start, and the execution enters Paused status until resumed.
3. **Given** a paused execution, **When** the user resumes it, **Then** execution continues from exactly where it left off, using the same persisted variables and node outputs.
4. **Given** a running or paused execution, **When** the user cancels it, **Then** the execution stops at the next safe point, no further nodes execute, and the execution is marked Cancelled.
5. **Given** an execution in progress, **When** the user inspects it, **Then** they see which workflow, version, and current node are running, along with running token usage and estimated cost, without needing implementation-level detail.

---

### User Story 7 - Error Handling, Retry, and Timeout Recovery (Priority: P7)

A user configures a node with a retry policy and a workflow-level failure strategy; when a transient failure occurs, the engine retries per policy, and if retries are exhausted, applies the configured failure strategy (stop, continue, fallback, or compensate) rather than leaving the execution in an ambiguous state.

**Why this priority**: Reliable failure handling is necessary for any workflow that touches real external systems (AI providers, tools, files), but the workflow must already be able to execute normally (Story 1) before its failure paths are meaningful to specify.

**Independent Test**: Can be fully tested by configuring a node to fail deterministically on its first N attempts, confirming the engine retries per the configured backoff up to the maximum, and then confirming the workflow's configured failure strategy (stop/continue/fallback/compensate) is applied exactly once retries are exhausted.

**Acceptance Scenarios**:

1. **Given** a node with a configured retry policy (maximum attempts, initial/maximum delay, backoff strategy, retryable error types), **When** the node fails with a retryable error, **Then** the system retries up to the configured maximum before treating it as a final failure.
2. **Given** a node explicitly marked as non-idempotent or unsafe to retry, **When** it fails, **Then** the system does not blindly retry it, regardless of the node's retry policy.
3. **Given** a node or workflow that exceeds its configured timeout, **When** the timeout elapses, **Then** the system persists the current state, applies the configured failure policy, records the timeout as the reason, and notifies the user where appropriate.
4. **Given** a workflow-level failure strategy of Continue, **When** a non-critical node fails, **Then** the workflow proceeds with subsequent nodes that do not depend on the failed node's output, and the failure is recorded against the execution.
5. **Given** a workflow-level failure strategy of Compensate, **When** a later step fails after an earlier step already modified an external system, **Then** the system invokes that earlier step's explicitly configured compensating action; compensation is never inferred automatically.
6. **Given** an operation configured with an idempotency key, **When** a retry (automatic or user-initiated) resubmits that operation, **Then** the external system is not modified a second time as a result of the retry.

---

### User Story 8 - Execution History, Audit, Usage, and Cost Review (Priority: P8)

A user reviews a list of past workflow executions and drills into any one to see its inputs, outputs, per-node results, errors, approvals, token usage, and estimated cost.

**Why this priority**: Historical visibility and auditability are essential for trust, troubleshooting, and compliance, but they build on data already captured during execution (Stories 1-7) rather than being required for a single run to succeed.

**Independent Test**: Can be fully tested by running a workflow to completion (and, separately, to failure), then opening its execution history and confirming every recorded field — per-node results, errors, approvals, usage, and cost — is present and matches what happened during the run.

**Acceptance Scenarios**:

1. **Given** a completed execution, **When** the user opens its history entry, **Then** they see the full node-by-node timeline, inputs/outputs at each node, any approval decisions, and the final output.
2. **Given** a failed execution, **When** the user opens its history entry, **Then** they see which node failed, the recorded error, and how many retries were attempted.
3. **Given** an execution that consumed AI tokens or invoked paid tools, **When** the user views its usage/cost, **Then** the figures reflect the actual model, provider, token counts, and tool calls used during that specific run.
4. **Given** a user without access to a particular workflow or execution, **When** they attempt to view it, **Then** access is denied and the attempt is recorded as a security event.
5. **Given** the workflow monitoring dashboard, **When** an administrator or owning user opens it, **Then** they see active, queued, failed, and completed execution counts, average duration, failure rate, and aggregate AI usage/cost across their workflows.

---

### User Story 9 - Event-Driven Workflow Trigger (Priority: P9)

A user configures a workflow to start automatically when a specific application event occurs — for example, a document being uploaded to a knowledge base — rather than requiring a manual start.

**Why this priority**: Automatic triggering is what makes the platform's stated vision ("whenever a project document is uploaded...") real, but it is additive to manual execution (Story 1) and depends on every other execution mechanic already working reliably.

**Independent Test**: Can be fully tested by publishing a workflow configured to trigger on "document uploaded" for a specific knowledge base, uploading a document that matches the trigger's scope, and confirming an execution starts automatically with the triggering event's data bound to the workflow's inputs — without any manual start action.

**Acceptance Scenarios**:

1. **Given** a published workflow configured with an event trigger and a matching scope (e.g., a specific knowledge base), **When** the matching application event occurs, **Then** the system automatically starts an execution with the event's relevant data bound to the workflow's declared inputs.
2. **Given** an event-triggered workflow, **When** the triggering event occurs but the initiating user's permissions no longer allow the workflow to run (e.g., their access to the target knowledge base was revoked), **Then** the execution is not started and the omission is recorded.
3. **Given** a workflow with an event trigger, **When** the user disables or archives the workflow, **Then** subsequent matching events no longer start new executions.
4. **Given** a burst of matching events in a short period, **When** each would start a new execution, **Then** the system enforces the workflow's configured concurrency and rate limits rather than starting unbounded concurrent executions.

---

### Edge Cases

- What happens when a workflow has no Start node, no End node, or contains disconnected nodes at publish time? Publishing is blocked with a specific validation error identifying the missing or disconnected element.
- What happens when a node references a variable or an earlier step's output that does not exist or has not yet executed? Validation catches unresolved references before publish; if a reference somehow fails to resolve at runtime, the node fails with a standardized, actionable error rather than silently substituting an empty or default value.
- What happens when a workflow's configured expression cannot be evaluated to the expected type (e.g., a condition compares incompatible types)? The expression engine rejects it at validation time with a specific type error; if it occurs at runtime despite validation, the node fails rather than coercing silently.
- What happens when a Parallel node's branches exceed the configured maximum parallel node limit? Excess branches queue behind the limit rather than all starting simultaneously; the limit is never silently exceeded.
- What happens when two of a user's executions attempt to write to the same protected external resource at the same time? The conflicting write is rejected with an actionable error rather than silently overwriting or queuing indefinitely, consistent with the existing Agent Framework's conflict-handling precedent.
- What happens when a workflow step invokes an MCP tool, Agent, Knowledge Base, or file the executing user is not (or is no longer) authorized to access? The step fails immediately with a clear permission/availability error; the workflow does not silently skip it or substitute default access.
- What happens when external content retrieved during a workflow (a document, RAG result, MCP tool output, or web content) contains text that looks like instructions to the AI? That content is always treated as untrusted data for a node to process, never as new system-level or workflow-level instructions, and it cannot alter approval requirements or bypass security policy.
- What happens when a workflow execution reaches its configured maximum duration, node count, AI token budget, cost, tool-call count, or loop-iteration limit? The execution stops or pauses per its budget policy, the specific limit that was hit is recorded, and the initiating user is notified.
- What happens when a user attempts to view or control (pause/resume/cancel/approve) another user's workflow or execution? Access is denied and the attempt is recorded as a security event.
- What happens when a workflow references a Prompt, Agent, Knowledge Base, or MCP tool that is later deleted or unpublished? Already-published workflow versions retain their reference and fail clearly at the point of use if the referenced capability is no longer available; the validation panel flags the same condition for draft workflows before they can be published.
- What happens when a Human Approval node's execution is left pending indefinitely? It remains paused (consuming no further budget) until a decision is made or the node's configured timeout — if any — elapses; it never silently expires with no record.
- What happens when a user attempts to start a new workflow execution while already at their per-user concurrency cap? The new execution is rejected with an actionable error; it is never silently queued indefinitely or allowed to exceed the cap.

## Requirements *(mandatory)*

### Functional Requirements — Workflow Definition & Lifecycle

- **FR-001**: Users MUST be able to create a workflow with a name, description, typed inputs, typed outputs, nodes, connections, variables, an error policy, an execution policy, and a security policy. A workflow's name MUST be unique per owner (case-insensitive); different owners MAY reuse the same name.
- **FR-002**: System MUST support workflow statuses: Draft, Published, Archived, Disabled, and Deprecated.
- **FR-003**: Users MUST be able to update, duplicate, archive, restore, and delete their own workflows.
- **FR-004**: System MUST record who created and who last updated each workflow, and when.

### Functional Requirements — Visual Designer

- **FR-005**: System MUST provide a visual canvas on which users can add, configure, connect, multi-select, copy, paste, and delete nodes.
- **FR-006**: System MUST provide a searchable node palette organized by category (AI, Knowledge, Documents, Files, Tools, Control Flow, Human Interaction, Data Transformation, Integration).
- **FR-007**: System MUST support undo and redo of designer actions (node add/remove/move, connection add/remove, configuration changes).
- **FR-008**: System MUST validate node connections for type compatibility as they are made and MUST reject connections between incompatible types.
- **FR-009**: System MUST indicate unsaved changes and MUST let the user save a draft independently of publishing.
- **FR-010**: System MUST provide zoom, pan, and a minimap for navigating large workflow graphs, and an auto-layout capability for arranging nodes.
- **FR-011**: Selecting a node MUST display its name, description, inputs, outputs, configuration, required permissions, timeout, retry policy, approval policy, advanced settings, and any validation errors specific to that node.

### Functional Requirements — Versioning & Publishing

- **FR-012**: System MUST create an immutable version snapshot — including nodes, connections, variables, inputs, outputs, and configuration — whenever a user publishes a workflow.
- **FR-013**: System MUST record the version number, author, creation date, and an optional change description for every published version.
- **FR-014**: System MUST prevent modification of a published version's content; changes require editing the draft and publishing a new version.
- **FR-015**: Every execution MUST reference the exact workflow version it ran under, and that reference MUST remain accurate even after newer versions are published.
- **FR-016**: System MUST block publishing when critical validation errors exist, including but not limited to: disconnected nodes, missing Start node, missing End node, invalid connections, unsupported circular dependencies, missing required inputs, invalid variable references, invalid expressions, missing required permissions, invalid node configuration, unbounded loops, missing error policy, and missing approval policy for operations that require one.

### Functional Requirements — Node System

- **FR-017**: System MUST expose a common node abstraction where every node declares a type, name, description, typed input schema, typed output schema, configuration, and required permissions.
- **FR-018**: System MUST provide, at minimum, the following node types: Start, End, AI Prompt, AI Agent, RAG Search, Memory Search, Document Processing, File Operation, MCP Tool, Native Tool, Transform, Condition, Parallel, Merge, Human Approval, Validation, and Delay (an architectural placeholder for future scheduling).
- **FR-019**: The AI Prompt node MUST execute an existing Prompt (by selection and version) exclusively through the existing Prompt Engine's abstraction and MUST NOT call an AI provider directly.
- **FR-020**: The AI Agent node MUST invoke an existing Agent (by selection and version) exclusively through the existing Agent Runtime, treating the agent as an external execution component, and MUST NOT duplicate agent planning logic.
- **FR-021**: The RAG Search node MUST query a selected Knowledge Base exclusively through the existing RAG abstraction (query, filters, top K, similarity threshold, retrieval mode, maximum context) and MUST return retrieved chunks, citations, scores, and metadata.
- **FR-022**: The Memory Search node MUST query and, where configured, create memory candidates exclusively through the existing Memory abstraction, honoring existing Memory security and privacy rules.
- **FR-023**: The MCP Tool node MUST invoke an existing MCP-integrated tool exclusively through the platform's existing Agent Tool abstraction; the workflow engine MUST NOT communicate directly with an MCP server.
- **FR-024**: The Native Tool node MUST invoke an existing platform tool through the same tool abstraction used by the Agent Framework, without a parallel implementation.
- **FR-025**: Nodes MUST be able to reference the outputs of previously executed nodes and workflow/user-supplied variables using a defined reference syntax (e.g., `{{steps.extract_document.text}}`).

### Functional Requirements — Variables & Expressions

- **FR-026**: System MUST support typed workflow variables, node outputs, user inputs, environment configuration, and system context, with supported types at minimum: String, Number, Boolean, Date, JSON, Text, File, Document, and Collection.
- **FR-027**: System MUST provide a sandboxed expression mechanism for conditions, variable references, transformations, and mappings that MUST NOT execute arbitrary user-supplied C# or JavaScript.
- **FR-028**: The expression engine MUST perform strict type validation on every expression, including nested AND/OR/NOT conditions, and MUST reject expressions that cannot be validated against their declared types before publish.

### Functional Requirements — Control Flow

- **FR-029**: A Condition node MUST route execution to exactly one matching branch based on its evaluated expression, and MUST record which branch(es) were skipped.
- **FR-030**: A Parallel node MUST run its independent branches concurrently, subject to configured concurrency limits, permission checks per branch, and dependency constraints between branches.
- **FR-031**: A Merge node MUST combine parallel branch outputs using an explicitly configured strategy — All Completed, First Completed, Any Completed, or Collect All — and MUST record which strategy applied.
- **FR-032**: Bounded loops (e.g., "process each item," "retry until condition") MUST declare a maximum iteration count, a timeout, and a failure policy; the system MUST NEVER allow an unbounded loop, and MUST stop a loop that reaches its maximum iterations regardless of whether its exit condition was met.

### Functional Requirements — Human Approval

- **FR-033**: When execution reaches a Human Approval node, the system MUST pause execution, persist the execution's state, notify the authorized approver, and display the requested action for review.
- **FR-034**: System MUST support approval decisions of Approve, Reject, Request Changes, and Cancel, and MUST route execution according to the workflow's configured handling for each outcome.
- **FR-035**: System MUST support approval policies of Always Require Approval, Never Require Approval, Require Approval Above a configured Risk Level, and Require Approval For Specific Node Types.
- **FR-036**: A workflow's approval policy configuration MUST NEVER bypass a mandatory platform-level security approval requirement (e.g., a High or Critical risk operation that the platform requires interactive or policy-based approval for, independent of workflow authoring).
- **FR-037**: A Human Approval node MAY declare a timeout; when it elapses without a decision, the system MUST apply the node's configured timeout failure policy, record the timeout, and notify the initiating user. A node without a configured timeout MUST remain paused indefinitely rather than silently expiring.

### Functional Requirements — Error Handling, Retry & Recovery

- **FR-038**: Each node execution MUST resolve to one of: Succeed, Fail, Retry, Skip, Wait, or Cancel.
- **FR-039**: System MUST support workflow-level failure strategies: Stop, Continue, Retry, Fallback, and Compensate, and MUST apply the workflow's configured strategy when a node's own retries are exhausted.
- **FR-040**: System MUST support configurable retry policies per node (maximum attempts, initial delay, maximum delay, backoff strategy, retryable error types, non-retryable error types) and MUST NOT retry an operation explicitly marked non-idempotent/unsafe regardless of policy.
- **FR-041**: System MUST support configurable timeouts at the node, workflow, human-approval, tool, and AI-execution level; when any elapses, the system MUST persist state, apply the configured failure policy, record the timeout, and notify where appropriate.
- **FR-042**: System MUST support explicitly configured compensating actions for a node (e.g., undo an external create on later failure); compensation logic MUST NEVER be automatically inferred.
- **FR-043**: System MUST support idempotency keys for operations that modify external systems, preventing an automatic or user-initiated retry from duplicating the underlying effect.

### Functional Requirements — Execution Runtime & State

- **FR-044**: System MUST execute workflows through a dedicated runtime that persists execution state after every significant transition (node start/complete/fail, pause, resume, approval, cancel).
- **FR-045**: Persisted execution state MUST include, at minimum: workflow ID, workflow version, execution ID, initiating user, status, current node(s), completed nodes, variables, node outputs, errors, approvals, timestamps, usage, and cost.
- **FR-046**: System MUST support execution statuses: Queued, Running, Paused, WaitingForApproval, Completed, Failed, Cancelled, and TimedOut.
- **FR-047**: Long-running workflow executions MUST run asynchronously in the background; the system MUST NOT hold an HTTP request open for the duration of an execution.
- **FR-048**: Users MUST be able to pause, resume, and cancel a running or paused execution they are authorized to control; pause MUST take effect at the next safe checkpoint and resume MUST continue from exactly the persisted state.

### Functional Requirements — Real-Time Events & Monitoring

- **FR-049**: System MUST emit a standardized set of execution events — at minimum WorkflowStarted, NodeStarted, NodeCompleted, NodeFailed, NodeRetrying, ApprovalRequested, ApprovalGranted, ApprovalRejected, WorkflowPaused, WorkflowResumed, WorkflowCompleted, WorkflowFailed, and WorkflowCancelled — each carrying execution ID, workflow ID, workflow version, node ID (where applicable), timestamp, event type, status, and safe metadata.
- **FR-050**: System MUST provide a monitoring view showing active, queued, failed, and completed executions, average duration, failure rate, node-level performance, AI usage, and estimated cost, scoped to workflows the viewing user is authorized to see.

### Functional Requirements — Execution History & Audit

- **FR-051**: Users MUST be able to inspect, for any execution they are authorized to view: workflow, version, start/end time, duration, status, inputs, outputs, per-node results, errors, approvals, usage, and cost.
- **FR-052**: System MUST record, in a tamper-resistant audit trail, workflow creation, modification, publication, execution, node execution, approval decisions, cancellation, failure, and permission decisions, without logging sensitive content unnecessarily.
- **FR-053**: System MUST NOT expose or persist private AI model chain-of-thought or hidden reasoning in any execution event, node record, or execution history; only node inputs/outputs, decisions, and results are shown.

### Functional Requirements — Budgets & Usage Tracking

- **FR-054**: System MUST integrate with existing AI usage tracking to record input tokens, output tokens, model, provider, and AI execution cost for every AI Prompt or AI Agent node invocation within a workflow execution.
- **FR-055**: System MUST enforce configurable budgets for maximum workflow duration, maximum node count, maximum AI tokens, maximum cost, maximum tool calls, maximum parallel nodes, and maximum loop iterations.
- **FR-056**: When any budget limit is reached, the system MUST stop or pause the execution per its configured policy, record the specific limit that was hit, and notify the initiating user.

### Functional Requirements — Security & Permission Inheritance

- **FR-057**: Every workflow execution MUST run under the authenticated identity of the user who started it (or, for an event-triggered execution, the user whose authorization the trigger is scoped to), and every node action MUST be authorized as if that user performed it directly.
- **FR-058**: A workflow MUST NEVER grant privileges beyond what the initiating user is independently authorized for; each node's security constraints MUST be inherited from its underlying capability (RAG node → Knowledge Base permissions; Memory node → Memory permissions; File node → File permissions; MCP node → MCP/tool permissions; Agent node → Agent permissions).
- **FR-059**: A workflow, its versions, its executions, and its history MUST be visible and controllable only by the user who owns them (or an authorized administrator); cross-user access MUST be denied and logged as a security event.
- **FR-060**: External content encountered during execution (documents, RAG results, MCP tool output, web content, user-provided text, other tool output) MUST always be treated as untrusted data and MUST NEVER be capable of overriding system-level or workflow-level security instructions or approval requirements.
- **FR-061**: System MUST require approval — interactive or policy-based, per FR-035/FR-036 — before any node performs a sensitive operation, including at minimum: sending communication, modifying external data, deleting data, financial operations, executing code, or making external system changes. Financial operations MAY occur within a workflow only under this mandatory approval gate; this requirement is what makes such operations non-autonomous, and no financial-operation node or tool is categorically prohibited so long as it is approval-gated.
- **FR-062**: System MUST NOT permit execution of arbitrary user-supplied C# or JavaScript at any point in a workflow (consistent with FR-027); all custom logic MUST route through the sandboxed expression engine, a Transform node's declared transformation types, or an existing tool/prompt/agent abstraction.

### Functional Requirements — Triggers

- **FR-063**: System MUST support Manual workflows (started explicitly by a user), Event-Driven workflows (started by a defined application event such as document uploaded, document processed, or knowledge base updated), and Agent-Assisted workflows (one or more nodes use an AI Agent).
- **FR-064**: An event-driven trigger MUST be scoped (e.g., to a specific knowledge base or document type) and MUST only start an execution when the triggering event matches that scope and the relevant user's authorization still permits it.
- **FR-065**: System architecture MUST accommodate a future Scheduled workflow type (time-based triggering) without requiring a redesign of the workflow definition or execution model; scheduling itself is not implemented in this release.

### Functional Requirements — API

- **FR-066**: System MUST provide operations to create, update, delete, archive, restore, validate, publish, list, and retrieve workflows and workflow versions.
- **FR-067**: System MUST provide operations to execute a workflow, retrieve an execution, cancel an execution, pause an execution, resume an execution, approve a node, reject a node, retry a node, retrieve execution events, retrieve execution history, and retrieve workflow statistics — each enforcing the authorization and inheritance rules in FR-057 through FR-059.

### Functional Requirements — Access & Concurrency

- **FR-068**: Any authenticated user MUST be able to create, publish, and execute workflows; the system MUST NOT restrict workflow creation, publishing, or execution by role or subscription tier in this release.
- **FR-069**: System MUST enforce a maximum number of concurrent workflow executions (status Queued, Running, Paused, or WaitingForApproval) a single user may have at once. This cap MUST be configurable per user or subscription tier by an administrator and MUST default to a modest platform-provided value out of the box, consistent with the existing Agent Framework's per-user execution cap.
- **FR-070**: When a user attempts to start a new workflow execution while already at their concurrency cap, the system MUST reject the new execution with an actionable error rather than silently exceeding the cap or queuing indefinitely.

### Key Entities *(include if feature involves data)*

- **Workflow**: A user-owned, reusable orchestration definition — name, description, current draft configuration, status (Draft/Published/Archived/Disabled/Deprecated), and workflow type (Manual, Event-Driven, Agent-Assisted; Scheduled reserved for a future release).
- **WorkflowVersion**: An immutable, published snapshot of a Workflow's nodes, connections, variables, inputs, outputs, and configuration, along with who published it, when, and why (change description). Executions always reference a specific WorkflowVersion.
- **WorkflowNode**: A single step within a WorkflowVersion's graph — its type (e.g., AI Prompt, RAG Search, Condition), name, description, configuration, input/output schema, required permissions, timeout, and retry/approval policy.
- **WorkflowConnection**: A directed link between two WorkflowNodes (or a node and a branch of a Condition/Parallel/Merge node), including the type-compatibility contract it satisfies.
- **WorkflowVariable**: A typed value scoped to a Workflow — workflow variable, node output reference, user input, environment configuration, or system context — with a declared type from the supported type set.
- **WorkflowExecution**: A single run of a WorkflowVersion against user-supplied inputs — its status, timing, current/completed nodes, variables, node outputs, errors, approvals, usage, and cost, and (for event-triggered runs) the triggering event reference.
- **WorkflowExecutionNode**: The record of one node's execution within a WorkflowExecution — its status (Pending/Running/Completed/Failed/Skipped/Cancelled/WaitingForApproval), input, output, timing, retries, and error (if any).
- **WorkflowExecutionEvent**: A timestamped, typed event emitted during an execution for observability/streaming purposes, carrying safe metadata only.
- **WorkflowApproval**: A record of a pause-for-approval request at a Human Approval node — the intended action and parameters, who decided, when, the decision (Approve/Reject/Request Changes/Cancel), and whether it was interactive or policy-based.
- **WorkflowError**: A structured record of a failure encountered during execution — where it occurred, its category, and actionable detail.
- **WorkflowExecutionUsage**: Recorded AI token consumption (input/output) and tool-call counts for an execution, sourced from the existing AI usage tracking capability.
- **WorkflowExecutionCost**: The estimated monetary cost associated with an execution, derived from usage.
- **WorkflowAuditLog**: The tamper-resistant record of workflow lifecycle and security-relevant events (creation, modification, publication, execution, approval decisions, cancellation, failure, permission decisions).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time user can build a three-node workflow in the visual designer, save it, and receive a successful execution result within 10 minutes of opening the designer.
- **SC-002**: Users observe a running execution's current node update within 2 seconds of the underlying change occurring, without manually refreshing.
- **SC-003**: 100% of sensitive-operation nodes (as defined in FR-061) either pause for interactive approval or proceed only under a recorded policy-based authorization — never silently.
- **SC-004**: 100% of completed or failed executions can be fully traced afterward — every node result, approval, and cost figure is present in the execution's history with no gaps.
- **SC-005**: 0 executions access a Knowledge Base, memory record, file, MCP tool, or Agent the initiating user is not independently authorized to access, verified across all executions.
- **SC-006**: 100% of executions that exceed a configured duration, node-count, token, cost, tool-call, or loop-iteration limit are automatically stopped or paused, with the specific limit that was hit recorded and shown to the user.
- **SC-007**: A user cancelling a running execution sees it reach a stopped state within 5 seconds.
- **SC-008**: 100% of attempts by a user to view or control another user's workflow, execution, or execution history are denied and recorded as a security event.
- **SC-009**: 0 workflows can be published while a critical validation error (per FR-016) is present.
- **SC-010**: An execution started against a specific published version continues to report results consistent with that exact version 100% of the time, even after newer versions are published.
- **SC-011**: 0 workflow executions succeed in running arbitrary user-supplied code outside the sandboxed expression engine and existing tool/prompt/agent abstractions.
- **SC-012**: An event-driven workflow starts an execution within 1 minute of its matching triggering event occurring, for 95% of matching events.
- **SC-013**: 100% of attempts to start a new workflow execution while at the user's concurrency cap are rejected with an actionable error, with 0 executions silently exceeding the configured cap.

## Assumptions

- The existing AI Provider Engine, Prompt Engine, Agent Runtime, RAG Engine, Memory Engine, MCP Tool integration (spec 021), File Management, and Document processing capabilities are available, stable, and exposed through abstractions the Workflow Engine can consume without modification to their internals.
- Background/asynchronous job processing infrastructure either already exists (per the Agent Framework, spec 020) or is extended as part of this feature so long-running executions never hold an HTTP request open; the specific mechanism is a planning-phase decision.
- Real-time progress updates use a provider-independent streaming mechanism consistent with the platform's existing real-time infrastructure (per spec 020's assumption); the exact transport is a planning-phase decision.
- Workflows and their executions are private to the owning user in this release; no team/organization sharing of workflow definitions is included. Viewer/Editor/Executor/Owner/Administrator sharing roles are reserved for a future release, consistent with the explicit "Out of Scope" list.
- A visual graph/canvas library is selected during the planning phase, subject to compliance with the project's licensing and architectural standards; this specification does not mandate a specific library.
- Default numeric values for retry limits, timeouts, budget caps (duration, node count, tokens, cost, tool calls, parallel nodes, loop iterations), the per-user concurrent-execution cap (FR-069), and the event-trigger dispatch interval are system-provided, administrator-tunable defaults; exact values are a planning-phase decision.
- The workflow engine's risk-level and permission vocabulary reuses the existing Agent Framework's tool-permission and risk-level vocabulary (Low/Medium/High/Critical; per spec 020) rather than introducing a parallel taxonomy, extended only where a workflow-specific node type has no existing equivalent.
- Initial event triggers are limited to application-internal events already emitted by existing engines (document uploaded, document processed, knowledge base updated); external webhook-based triggers are explicitly out of scope for this release per the original request.
- A workflow's Human Approval node reuses the platform's existing notification mechanism to alert approvers; it does not introduce a separate notification channel.
- Workflow templates, an organization/team collaboration model, advanced (calendar-based) scheduling, and a workflow marketplace are explicitly out of scope for this release, consistent with the original request; the data model allows their future addition without redesign.

## Dependencies

- The existing AI Agent Framework (spec 020) — Agent, AgentVersion, AgentTool, AgentExecution, AgentApproval, AgentPolicy, and its Agent Runtime tool-execution pipeline — which the AI Agent node invokes rather than duplicates.
- The existing MCP Integration (spec 021) and its `IAgentTool`-based tool abstraction, which the MCP Tool node routes through rather than communicating with MCP servers directly.
- The existing Prompt Library (spec 019), which the AI Prompt node executes through rather than duplicating prompt storage or templating.
- The existing RAG & Semantic Search Engine (spec 016), which the RAG Search node queries through rather than reimplementing retrieval or vector search.
- The existing AI Memory System (spec 018), which the Memory Search node queries and writes through rather than reimplementing memory storage or ranking.
- The existing AI Provider abstraction, which any AI-powered node resolves model/provider selection through; the Workflow Engine never calls a provider directly.
- The existing Document Intelligence Pipeline and File Management capabilities, which the Document Processing and File Operation nodes invoke rather than reimplementing extraction or storage.
- The existing authentication/authorization infrastructure (roles, permission checks) and audit/observability infrastructure (structured logging, correlation IDs), extended to cover workflow-specific entities.
- Background job processing infrastructure for asynchronous, long-running execution and event-trigger dispatch without blocking API requests.
- The platform's existing real-time/notification infrastructure, extended to carry workflow execution events and approval notifications.

## Risks

- **Orchestration/agent boundary confusion**: without a crisp architectural line between "workflow calls an agent as one step" and "an agent could itself orchestrate a workflow," future changes risk collapsing the two models or duplicating planning logic; the planning phase must keep the Agent Runtime opaque to the Workflow Engine (per FR-020).
- **Expression engine as an injection surface**: a sandboxed expression language is still an attacker-reachable evaluation surface if type validation or sandboxing has gaps; insufficient rigor here could become a de facto arbitrary-code-execution path (violates FR-027, FR-062, and the explicit Out of Scope constraint).
- **Parallel execution resource exhaustion**: unless concurrency, budget, and permission checks are enforced consistently per branch, a wide Parallel node could exhaust AI provider rate limits, tool quotas, or compute resources system-wide.
- **Approval-policy bypass via node configuration**: a workflow author's approval-policy choice (FR-035) must never be able to weaken a platform-mandated approval requirement (FR-036, FR-061); getting this precedence wrong is a direct safety regression versus the existing Agent Framework's guarantee.
- **Cross-engine permission drift**: each node type inherits permissions from a different existing engine (RAG, Memory, MCP, Agent, File); if any inheritance path is implemented inconsistently, a workflow could become a privilege-escalation vector even though each underlying engine is individually secure.
- **Versioning/state coupling under long-running executions**: an execution that runs for hours or days while referencing an immutable version must not be affected by concurrent edits to the draft; state-persistence and optimistic-concurrency design must be validated under real long-running scenarios.

## Security Threats Addressed

- Arbitrary code execution via workflow authoring — mitigated by the sandboxed expression engine and prohibition on user-supplied C#/JavaScript (FR-027, FR-062).
- Privilege escalation through node configuration — mitigated by mandatory permission inheritance from each node's underlying capability (FR-058) and independent authorization enforcement per FR-057.
- Approval bypass via workflow-level policy — mitigated by the platform-mandatory-approval precedence rule (FR-036, FR-061).
- Prompt injection via external content (documents, RAG results, MCP output, web content, tool output) — mitigated by treating all such content as untrusted data that can never override system/workflow instructions or approval requirements (FR-060).
- Cross-user access to workflows, versions, or execution history — mitigated by ownership-scoped authorization and audit logging (FR-059).
- Unbounded resource consumption (infinite loops, unconstrained parallelism, runaway cost/token usage) — mitigated by mandatory bounded loops (FR-032) and enforced budgets (FR-055, FR-056).
- Duplicate side effects from retries against external systems — mitigated by idempotency-key support (FR-043) and the prohibition on blindly retrying non-idempotent operations (FR-040).
- Silent, unrecorded approval timeouts or expirations — mitigated by explicit timeout policy and notification requirements (FR-037, FR-041).

## Open Questions

- The exact default numeric values for budgets (max duration, node count, tokens, cost, tool calls, parallel nodes, loop iterations), node-level timeouts, and the per-user concurrent-execution cap (FR-069) — deferred to the planning phase as system-provided, admin-tunable defaults, consistent with spec 020/021 precedent.
- The specific set of application-internal events available as Event-Driven triggers beyond the three named in the original request (document uploaded, document processed, knowledge base updated) — deferred to the planning phase pending an inventory of events existing engines already emit.
- The precise mechanism by which a workflow's approval-policy configuration is reconciled against platform-mandatory approval rules at validation time versus runtime — a planning-phase architectural decision, not a specification-level one.
- Whether the visual designer's canvas/graph library selection has any implications for how node/connection data is persisted (e.g., layout metadata) — a planning-phase decision once a library is selected.

## Migration Considerations

- No existing Workflow data exists today; this feature is purely additive and requires no migration of existing records.
- The existing tool-permission and risk-level vocabulary (introduced by spec 020 and extended by spec 021) is extended again, not replaced, so that native tools, MCP tools, and workflow nodes share one consistent authorization model.
- The existing execution-history, audit, and real-time event infrastructure used by the Agent Framework and MCP Integration is extended to also carry workflow-specific event types, rather than introducing a parallel, disconnected history/audit system.
- Because a WorkflowVersion embeds references to Prompts, Agents, Knowledge Bases, and MCP tools by identifier, any future breaking change to those referenced entities' identifiers must preserve backward-compatible resolution for already-published workflow versions, or provide an explicit migration path for them.
