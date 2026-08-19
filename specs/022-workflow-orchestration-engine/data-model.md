# Data Model: Workflow & Tool Orchestration Engine

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Every entity below inherits `BaseEntity` (`src/AskLucy.Domain/Common/BaseEntity.cs`: `Id` (Guid v7), `CreatedAtUtc`/`CreatedBy`, `ModifiedAtUtc`/`ModifiedBy`, `DeletedAtUtc`/`DeletedBy`, `RowVersion`) unless noted. Soft delete is enforced via the existing EF Core query filter + `AuditSaveChangesInterceptor` — no entity below needs its own delete-tracking logic.

## Aggregate: Workflow

### Workflow (aggregate root)

The reusable, user-owned orchestration definition a user builds in the Workflow Designer.

| Field | Type | Notes |
|---|---|---|
| `OwnerId` | `string` (FK → `ApplicationUser.Id`) | Sole owner; private workflows only this release (FR-059, Assumptions) |
| `Name` | `string` | Unique per owner, case-insensitive (FR-001, Clarifications) |
| `Description` | `string?` | |
| `WorkflowType` | `WorkflowType` enum | `Manual` / `EventDriven` / `AgentAssisted` (FR-063); `Scheduled` reserved, not selectable this release (FR-065) |
| `Status` | `WorkflowStatus` enum | `Draft` / `Published` / `Archived` / `Disabled` / `Deprecated` (FR-002) |
| `DraftDefinitionJson` | `string` | The mutable canvas document — nodes, connections, variables, layout (research.md Decision 19). Only this field changes while editing; published versions never read it. |
| `PublishedVersionNumber` | `int?` | Null until first publish |
| `EventTriggerConfigurationJson` | `string?` | Populated only when `WorkflowType == EventDriven`: event type + scope (e.g., target Knowledge Base id) — FR-064. Re-validated against the owner's live authorization at dispatch time, never cached as a standing grant. |
| `PreArchiveStatus` | `WorkflowStatus?` | Mirrors `Agent.PreArchiveStatus` — set on `Archive`, restored on `Restore` (FR-003) |

**Navigation**: `Versions` (`WorkflowVersion`, 0–N), `Executions` (`WorkflowExecution`, 0–N, via `WorkflowVersionId`, not directly).

**Business rules**: A workflow with any execution history is never hard-deleted (soft delete only, FR-052 audit trail). `Draft → Published` requires passing every FR-016 validation rule; publishing is rejected otherwise (SC-009). Duplicating a workflow (FR-003) copies `DraftDefinitionJson` only into a new `Workflow` in `Draft` status — never prior version history.

**Domain events**: `WorkflowCreated`, `WorkflowPublished`, `WorkflowArchived`, `WorkflowRestored`, `WorkflowDeleted` (published via the existing `IPublisher`/MediatR mechanism, consistent with research.md Decision 12's precedent — audit-log writes and `WorkflowAuditLog` rows are the durable record; these are for any future in-process subscriber, e.g. the event-trigger handler itself for `WorkflowCreated`/`WorkflowPublished` doesn't apply here since those aren't external triggers).

### WorkflowVersion

An immutable, published snapshot (FR-012–FR-016).

| Field | Type | Notes |
|---|---|---|
| `WorkflowId` | `Guid` (FK) | |
| `VersionNumber` | `int` | Sequential per workflow, starting at 1 |
| `InputsSchemaJson` / `OutputsSchemaJson` | `string` | Typed input/output contract (FR-001, FR-026) — frozen |
| `ErrorPolicyJson` / `ExecutionPolicyJson` / `SecurityPolicyJson` | `string` | Workflow-level failure strategy (FR-039), budget policy (FR-055, research.md Decision 10), and any security-policy overrides — frozen |
| `PublishedBy` | `string` (FK → user) | |
| `ChangeDescription` | `string?` | |

**Navigation**: `Nodes` (`WorkflowNode`, 1–N), `Connections` (`WorkflowConnection`, 0–N), `Variables` (`WorkflowVariable`, 0–N).

**Business rules**: Never updated after insert (FR-014). `VersionNumber` is immutable once assigned. Materialized from `Workflow.DraftDefinitionJson` at publish time (research.md Decision 19) — the source of truth for every execution from this point on.

**Domain events**: `WorkflowVersionPublished`.

### WorkflowNode

A single step within a `WorkflowVersion`'s graph (FR-017, FR-018).

| Field | Type | Notes |
|---|---|---|
| `WorkflowVersionId` | `Guid` (FK) | |
| `NodeKey` | `string` | Stable identifier from the draft canvas (e.g. `extract_document`) — what `{{steps.extract_document.text}}` references (FR-025); unique within a version |
| `NodeType` | `WorkflowNodeType` enum | `Start` / `End` / `AiPrompt` / `AiAgent` / `RagSearch` / `MemorySearch` / `DocumentProcessing` / `FileOperation` / `McpTool` / `NativeTool` / `Transform` / `Condition` / `Parallel` / `Merge` / `HumanApproval` / `Validation` / `Delay` (FR-018) |
| `Name` / `Description` | `string` / `string?` | |
| `InputSchemaJson` / `OutputSchemaJson` | `string` | Declared per FR-017; for capability-wrapping node types this is inherited from the underlying `IAgentTool.InputSchemaJson`/`OutputSchemaJson` at publish time (research.md Decision 1), not re-authored |
| `ConfigurationJson` | `string` | Node-type-specific settings: selected Prompt/Agent/Knowledge Base/MCP tool id, Condition expression, Merge strategy, loop bounds, Delay placeholder metadata, etc. |
| `RequiredPermissionsJson` | `string` | For capability-wrapping nodes, mirrors the underlying `IAgentTool.RequiredPermissions` (FR-058 inheritance); empty for pure control-flow nodes |
| `TimeoutSeconds` | `int?` | Null falls back to `WorkflowRuntimeOptions` default (FR-041) |
| `RetryPolicyJson` | `string?` | Max attempts/delay/backoff/retryable-error-types (FR-040); null = not retried |
| `ApprovalPolicy` | `WorkflowNodeApprovalPolicy` enum | `AlwaysRequire` / `NeverRequire` / `AboveRiskLevel` / `ForThisNodeType` (FR-035) — never overrides the platform-mandatory baseline (FR-036, research.md Decision 5) |
| `IdempotencyKeyExpression` | `string?` | Evaluated by the expression engine before a mutating retry (FR-043, research.md Decision 13) |
| `CompensatingNodeId` | `Guid?` (FK → `WorkflowNode`, same version) | FR-042, research.md Decision 14; validated at publish time to not be reachable from itself |
| `CanvasPositionJson` | `string` | `{x, y}` — designer layout only, no execution semantics |

**Business rules**: `NodeKey` uniqueness and `CompensatingNodeId` non-self-reachability are FR-016 publish-time validation rules. Never updated after insert (belongs to an immutable `WorkflowVersion`).

### WorkflowConnection

A directed link between two `WorkflowNode`s, or a node and a labeled branch of a Condition/Parallel/Merge node (FR-018, Acceptance Scenario 4.1).

| Field | Type | Notes |
|---|---|---|
| `WorkflowVersionId` | `Guid` (FK) | |
| `SourceNodeId` / `TargetNodeId` | `Guid` (FK → `WorkflowNode`) | |
| `BranchLabel` | `string?` | e.g. `"true"`/`"false"` for a Condition node's two edges, or a Parallel node's branch name; null for an unconditional edge |
| `TypeContract` | `string?` | The declared output/input type the connection satisfies (FR-008) — validated at connect-time client-side and again at publish-time server-side |

**Business rules**: `SourceNodeId != TargetNodeId`. A bounded loop (FR-032) is modeled as an ordinary `WorkflowConnection` whose `BranchLabel` is the reserved value `"loop-back"`, pointing from a loop body's last node back to its first node; `MaxIterations`/`TimeoutSeconds`/failure policy live on that first node's `ConfigurationJson` (research.md Decision 20). Cycle detection across the full graph runs once at publish time (FR-016) and treats any `"loop-back"`-labeled connection as an intentional, bounded construct — never an unsupported cycle — while still rejecting every other cycle. The runtime itself never re-detects cycles (research.md Decision 7); it enforces the iteration bound by counting traversals of each `"loop-back"` connection during execution.

### WorkflowVariable

A typed value scoped to a `WorkflowVersion` (FR-026).

| Field | Type | Notes |
|---|---|---|
| `WorkflowVersionId` | `Guid` (FK) | |
| `Name` | `string` | Unique within a version |
| `Kind` | `WorkflowVariableKind` enum | `WorkflowVariable` / `NodeOutputReference` / `UserInput` / `EnvironmentConfiguration` / `SystemContext` |
| `ValueType` | `WorkflowVariableType` enum | `String` / `Number` / `Boolean` / `Date` / `Json` / `Text` / `File` / `Document` / `Collection` (FR-026) |
| `DefaultValueJson` | `string?` | |
| `IsRequired` | `bool` | For `UserInput` kind — enforced when starting an execution |

## Aggregate: WorkflowExecution

### WorkflowExecution (aggregate root)

One run of a specific `WorkflowVersion` (FR-044, FR-045).

| Field | Type | Notes |
|---|---|---|
| `WorkflowId` / `WorkflowVersionId` | `Guid` (FK) | Immutable reference — never repointed after a newer version publishes (FR-015, SC-010) |
| `RunByUserId` | `string` (FK) | The authenticated user the execution runs *as* (FR-057); for an event-triggered run, the user the trigger's authorization is scoped to (FR-064) |
| `Status` | `WorkflowExecutionStatus` enum | `Queued` / `Running` / `Paused` / `WaitingForApproval` / `Completed` / `Failed` / `Cancelled` / `TimedOut` (FR-046) |
| `TriggerType` | `WorkflowExecutionTriggerType` enum | `Manual` / `EventDriven` / `Test` |
| `TriggeringEventReferenceJson` | `string?` | Set only for `EventDriven` — the source event type + entity id (FR-063's "event's relevant data bound to the workflow's declared inputs") |
| `InputsJson` | `string` | User- or event-supplied values for the version's declared inputs |
| `VariablesJson` | `string` | Live variable state, updated as nodes complete (FR-045) |
| `FinalOutputJson` | `string?` | Conforms to the version's declared output schema on `Completed` |
| `StartedAtUtc` / `CompletedAtUtc` | `DateTime?` | |
| `TerminationReason` | `string?` | Populated on budget-limit stop (FR-056), timeout (FR-041), cancellation, or failure |

**Navigation**: `Nodes` (`WorkflowExecutionNode`, 1–N), `Events` (`WorkflowExecutionEvent`, 1–N), `Approvals` (`WorkflowApproval`, 0–N), `Errors` (`WorkflowError`, 0–N), `Usage` (`WorkflowExecutionUsage`, 0–1), `Cost` (`WorkflowExecutionCost`, 0–1).

**State transitions** (FR-046): `Queued → Running → {Completed | Failed | Cancelled | TimedOut}`; `Running → Paused → Running` (resume, FR-048) or `→ Cancelled`; `Running → WaitingForApproval → Running` (approved) or `→ Cancelled`/`→ Failed` (rejected, per the node's configured rejection path, User Story 5 Acceptance Scenario 3). No transition ever leaves a terminal status.

**Business rules**: Never hard-deleted (FR-052 audit trail). `Nodes`/`Events` rows are append-only once written. FR-069/FR-070's concurrency cap is checked *before* this row is created (research.md Decision 11) — a rejected attempt never produces a `WorkflowExecution` row at all.

**Domain events**: `WorkflowExecutionStarted`, `WorkflowExecutionCompleted`, `WorkflowExecutionFailed`, `WorkflowExecutionCancelled`.

### WorkflowExecutionNode

The record of one node's execution within a `WorkflowExecution` (FR-045, FR-051).

| Field | Type | Notes |
|---|---|---|
| `WorkflowExecutionId` | `Guid` (FK) | |
| `WorkflowNodeId` | `Guid` (FK → `WorkflowNode`) | |
| `Status` | `WorkflowExecutionNodeStatus` enum | `Pending` / `Running` / `Completed` / `Failed` / `Skipped` / `Cancelled` / `WaitingForApproval` (mirrors `AgentExecutionStepStatus`). Maps to FR-038's outcome vocabulary as: Succeed→`Completed`, Fail→`Failed`, Skip→`Skipped`, Cancel→`Cancelled`; Wait→`WaitingForApproval` (approval-specific; a node awaiting a plain retry backoff stays `Running`); Retry is not a distinct status — it is tracked via `RetryCount` on this same row. |
| `InputJson` / `OutputJson` | `string?` | Resolved input actually sent; produced output |
| `RetryCount` | `int` | |
| `ResolvedIdempotencyKey` | `string?` | Set when `WorkflowNode.IdempotencyKeyExpression` is configured; checked before a mutating retry (research.md Decision 13) |
| `StartedAtUtc` / `CompletedAtUtc` | `DateTime?` | |
| `SkippedReason` | `string?` | e.g. "unmatched Condition branch," "budget limit reached before this node started" |

**Business rules**: One row per `(WorkflowExecutionId, WorkflowNodeId)` pair — a resumed execution reuses its existing row rather than inserting a duplicate (mirrors `AgentExecutionOrchestrator`'s resume logic exactly).

### WorkflowExecutionEvent

A timestamped, typed event emitted during an execution (FR-049).

| Field | Type | Notes |
|---|---|---|
| `WorkflowExecutionId` | `Guid` (FK) | |
| `EventType` | `WorkflowExecutionEventType` enum | `WorkflowStarted` / `NodeStarted` / `NodeCompleted` / `NodeFailed` / `NodeRetrying` / `ApprovalRequested` / `ApprovalGranted` / `ApprovalRejected` / `WorkflowPaused` / `WorkflowResumed` / `WorkflowCompleted` / `WorkflowFailed` / `WorkflowCancelled` (FR-049) |
| `WorkflowNodeId` | `Guid?` (FK) | Null for workflow-level events |
| `Status` | `string` | Short status label, e.g. `"Completed"` |
| `SafeMetadataJson` | `string?` | Never chain-of-thought/reasoning (FR-053) |

**Business rules**: Append-only. The persisted row is the reconciliation source of truth if a live SignalR push is missed (research.md Decision 8).

### WorkflowApproval

A record of a pause-for-approval request at a Human Approval node (FR-033, FR-034, research.md Decision 5).

| Field | Type | Notes |
|---|---|---|
| `WorkflowExecutionId` | `Guid` (FK) | |
| `WorkflowExecutionNodeId` | `Guid` (FK) | |
| `IntendedActionDescription` | `string` | |
| `ParametersJson` | `string?` | |
| `Decision` | `WorkflowApprovalDecision?` enum | `Approve` / `Reject` / `RequestChanges` / `Cancel` (FR-034); null while pending |
| `WasPolicyBased` | `bool` | True when matched by a `WorkflowPolicy` rather than an interactive decision (FR-035) |
| `MatchedWorkflowPolicyId` | `Guid?` (FK) | Set only when `WasPolicyBased` |
| `DecidedByUserId` | `string?` (FK) | Null when policy-based |
| `DecidedAtUtc` | `DateTime?` | |
| `TimeoutSeconds` | `int?` | Copied from the node's config at request time (FR-037); null = waits indefinitely |

**Business rules**: Never deleted (audit trail, FR-052). A pending approval with `TimeoutSeconds` set and elapsed transitions per the node's configured timeout failure policy (FR-037) — the row itself gets `Decision = Cancel` with a distinct `SkippedReason`-style note recorded on the owning `WorkflowExecutionNode`, never silently left ambiguous.

### WorkflowError

A structured record of a failure encountered during execution (FR-051).

| Field | Type | Notes |
|---|---|---|
| `WorkflowExecutionId` | `Guid` (FK) | |
| `WorkflowExecutionNodeId` | `Guid?` (FK) | Null for a workflow-level failure (e.g., budget exceeded before any node ran) |
| `Category` | `WorkflowErrorCategory` enum | `NodeExecutionFailure` / `BudgetExceeded` / `Timeout` / `ValidationFailure` / `PermissionDenied` / `ProviderFailure` (mirrors `AgentExecutionErrorCategory`'s shape) |
| `Message` | `string` | User-safe (never a raw exception message/stack trace, constitution §8) |
| `RetryCount` | `int` | |

### WorkflowExecutionUsage

Recorded AI token consumption for an execution (FR-054), one row per `WorkflowExecution`.

| Field | Type | Notes |
|---|---|---|
| `WorkflowExecutionId` | `Guid` (FK, unique) | |
| `InputTokenCount` / `OutputTokenCount` / `ReasoningTokenCount` | `int?` | Accumulated across every AI Prompt / AI Agent node in the execution — mirrors `AgentExecutionUsage.Accumulate` exactly |
| `ToolCallCount` | `int` | |

### WorkflowExecutionCost

The estimated monetary cost derived from `WorkflowExecutionUsage` (FR-054), one row per `WorkflowExecution`.

| Field | Type | Notes |
|---|---|---|
| `WorkflowExecutionId` | `Guid` (FK, unique) | |
| `EstimatedCost` | `decimal` | |
| `CurrencyCode` | `string` | `"USD"`, mirrors `AgentExecutionCost` |

## Aggregate: WorkflowPolicy

### WorkflowPolicy

An administrator- or owner-defined rule that pre-approves specific node actions under defined conditions (FR-035, research.md Decision 5).

| Field | Type | Notes |
|---|---|---|
| `WorkflowNodeType` | `WorkflowNodeType?` enum | Scopes the policy to a node type (FR-035's "Require Approval For Specific Node Types"); null = applies by risk level instead |
| `UnderlyingToolName` | `string?` | For capability-wrapping node types, matches the underlying `IAgentTool.Name` (same shape `AgentPolicy.ToolName` already uses) |
| `ConditionsJson` | `string?` | Flat JSON parameter constraints — evaluated by the shared `PolicyConditionMatcher` (research.md Decision 5); empty = matches unconditionally |
| `IsEnabled` | `bool` | |
| `CreatedByUserId` | `string` (FK) | |

### WorkflowUserExecutionLimit

Per-user override of the system-wide concurrent-execution cap (FR-069/FR-070, research.md Decision 11) — field-for-field mirror of `AgentUserExecutionLimit`.

| Field | Type | Notes |
|---|---|---|
| `UserId` | `string` (FK, unique) | |
| `MaxConcurrentExecutions` | `int` | |
| `SetByUserId` | `string` (FK) | |

## Aggregate: WorkflowAuditLog

### WorkflowAuditLog

The tamper-resistant record of workflow lifecycle and security-relevant events (FR-052).

| Field | Type | Notes |
|---|---|---|
| `WorkflowId` | `Guid?` (FK) | Null for an execution-scoped entry without a resolvable workflow (defensive) |
| `WorkflowExecutionId` | `Guid?` (FK) | |
| `ActorUserId` | `string` (FK) | |
| `Action` | `WorkflowAuditAction` enum | `WorkflowCreated` / `WorkflowModified` / `WorkflowPublished` / `ExecutionStarted` / `ExecutionCompleted` / `ExecutionFailed` / `ExecutionCancelled` / `ApprovalDecided` / `PermissionDenied` (mirrors `AgentAuditAction`'s shape) |
| `DetailsJson` | `string` | Never sensitive content (FR-052) |

## Enumerations

`WorkflowType`, `WorkflowStatus`, `WorkflowNodeType`, `WorkflowNodeApprovalPolicy`, `WorkflowVariableKind`, `WorkflowVariableType`, `WorkflowExecutionStatus`, `WorkflowExecutionTriggerType`, `WorkflowExecutionNodeStatus`, `WorkflowExecutionEventType`, `WorkflowApprovalDecision`, `WorkflowErrorCategory`, `WorkflowAuditAction` — values enumerated in each entity's table above.

**Reused, not redefined**: `AgentToolPermission`, `AgentToolRiskLevel` (from `Domain.Agents` / `Application.Agents.Tools`) — a `WorkflowNode`'s `RequiredPermissionsJson` and effective risk level (for the FR-036 approval baseline) are expressed in this same existing vocabulary, per research.md Decision 1/5, never a parallel workflow-specific permission enum.

## Value objects (owned types, no separate table)

- `WorkflowExecutionPolicy` (on `WorkflowVersion.ExecutionPolicyJson`, deserialized) — `MaxNodeCount`, `MaxExecutionDurationSeconds`, `MaxTokens`, `MaxCost`, `MaxToolCalls`, `MaxParallelNodes`, `MaxLoopIterations` (FR-055) — every field nullable, falling back to `WorkflowRuntimeOptions` system defaults, mirroring `AgentExecutionPolicy` exactly (research.md Decision 10).
- `WorkflowRetryPolicy` (on `WorkflowNode.RetryPolicyJson`) — `MaxAttempts`, `InitialDelaySeconds`, `MaxDelaySeconds`, `BackoffStrategy`, `RetryableErrorTypes`, `NonRetryableErrorTypes` (FR-040).

FR-041 names five configurable timeout levels (node/workflow/human-approval/tool/AI-execution); only three fields actually exist. "Tool timeout" and "AI-execution timeout" are not separately modeled — they are both expressed through the wrapping node's own `WorkflowNode.TimeoutSeconds` (a RAG/MCP/Native-Tool node's timeout *is* its tool's timeout; an AI Prompt/AI Agent node's timeout *is* its AI-execution timeout). "Node timeout" and "workflow timeout" are `WorkflowNode.TimeoutSeconds` and `WorkflowExecutionPolicy.MaxExecutionDurationSeconds` respectively; "human-approval timeout" is `WorkflowApproval.TimeoutSeconds`.

## Delete behavior

| Entity | On owner delete/soft-delete | Notes |
|---|---|---|
| `Workflow` | Soft delete only; blocked from hard delete while `Executions.Any()` | Mirrors `Agent`'s "never hard-deleted with history" rule |
| `WorkflowVersion` / `WorkflowNode` / `WorkflowConnection` / `WorkflowVariable` | Cascade soft-delete with `Workflow` | Immutable history, never independently deleted |
| `WorkflowExecution` and all its children (`WorkflowExecutionNode`, `WorkflowExecutionEvent`, `WorkflowApproval`, `WorkflowError`, `WorkflowExecutionUsage`, `WorkflowExecutionCost`) | `Restrict` against `Workflow` hard-delete; soft-delete cascades | An execution survives its workflow's soft delete — audit trail (FR-052) outlives the definition, matching `Agent`/`AgentExecution`'s existing FK-cascade shape in `ENTITY_MODEL.md` |
| `WorkflowPolicy` | Independent lifecycle (administrator/owner managed) | Not owned by any single `Workflow` |
| `WorkflowUserExecutionLimit` | Independent lifecycle | Administrator-managed |
| `WorkflowAuditLog` | Never deleted | Append-only, outlives every other row it references |

## Explicitly not modeled (deferred, per research.md / spec Out of Scope)

- `WorkflowSchedule` — Scheduled workflow type is a reserved enum value only (`WorkflowType.Scheduled` is not selectable this release); no scheduling entity, consistent with FR-065 and spec.md's explicit deferral.
- `WorkflowPermission` / any sharing-role table (Viewer/Editor/Executor/Owner/Administrator) — private-only ownership this release (FR-059); `Workflow.OwnerId` is sufficient, no ACL table.
- `WorkflowTemplate` — no template/marketplace entity; explicitly out of scope.
- A separate `Tool`/`WorkflowToolCall` table — capability-wrapping node execution reuses `IAgentTool`/`AgentToolCall`'s existing shape via the underlying `AgentExecutionOrchestrator`/tool-catalog path (research.md Decision 1); a `WorkflowExecutionNode` row already carries the same input/output/status fields an `AgentToolCall` would, so no duplicate table is introduced.
