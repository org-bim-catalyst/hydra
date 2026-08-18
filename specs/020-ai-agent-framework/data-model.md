# Data Model: AI Agent Framework & Agent Runtime

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Every entity below inherits `BaseEntity` (`src/AskLucy.Domain/Common/BaseEntity.cs`: `Id` (Guid v7), `CreatedAtUtc`/`CreatedBy`, `ModifiedAtUtc`/`ModifiedBy`, `DeletedAtUtc`/`DeletedBy`, `RowVersion`) unless noted. Soft delete is enforced via an EF Core query filter (`DeletedAtUtc == null`) plus the existing `AuditSaveChangesInterceptor`, which also converts any accidental hard delete into a soft delete automatically — no entity below needs its own delete-tracking logic.

## Aggregate: Agent

### Agent (aggregate root)

The reusable, user-owned definition a user builds in the Agent Builder.

| Field | Type | Notes |
|---|---|---|
| `OwnerId` | `string` (FK → `ApplicationUser.Id`) | Sole owner; no sharing this release (FR-048) |
| `Name` | `string` | Required |
| `Description` | `string?` | |
| `AgentType` | `AgentType` enum | Conversational / Research / Document / Knowledge / Task (spec's "Agent Types"; extensible) |
| `Status` | `AgentStatus` enum | `Draft` / `Published` / `Archived` (FR-002) |
| `CurrentDraftInstructions` | owned type `AgentInstructions` | System/Objectives/Constraints/BehavioralRules/OutputRequirements/ToolUsageRules/SafetyRules (FR-004) — the *editable* draft; see `AgentVersion` for immutable published copies |
| `CurrentDraftModelProviderId` / `CurrentDraftModelId` | `Guid?` (FK → `AIProvider`/`AIModel`) | Draft-only; a version snapshots the resolved value |
| `OutputFormat` | `AgentOutputFormat` enum | PlainText / Markdown / Json / StructuredOutput / Files (FR-042/FR-044) |
| `PublishedVersionNumber` | `int?` | Null until first publish; the version currently considered "the" agent for new executions |
| `PreArchiveStatus` | `AgentStatus?` | Set to the current `Status` when transitioning to `Archived`; cleared on `Restore`. `Restore` sets `Status = PreArchiveStatus ?? AgentStatus.Draft` — handles an agent archived directly from `Draft`, since FR-003 doesn't restrict archiving to `Published` agents only. |

**Navigation**: `Versions` (`AgentVersion`, 1–N), `Tools` (`AgentTool`, draft-scoped, 0–N), `KnowledgeBases` (`AgentKnowledgeBase`, 0–N), `MemoryPolicy` (`AgentMemoryPolicy`, 0–1), `Executions` (`AgentExecution`, 1–N, via `AgentVersionId`, not directly).

**Business rules**: An agent with any `AgentExecution` history is never hard-deleted (soft delete only — mirrors `ENTITY_MODEL.md`'s existing `Agent → AgentRuns: Restrict` intent, expressed here as "soft delete never cascades to executions," consistent with FR-050's permanent audit trail). `Draft → Published` requires at least the fields FR-001 lists as mandatory. Duplicating an agent (FR-003) copies the current draft only, never version history, into a brand-new `Agent` in `Draft` status.

**Domain events**: `AgentCreated`, `AgentPublished`, `AgentArchived`, `AgentRestored`, `AgentDeleted`.

### AgentVersion

An immutable, published snapshot (FR-007–FR-010).

| Field | Type | Notes |
|---|---|---|
| `AgentId` | `Guid` (FK) | |
| `VersionNumber` | `int` | Sequential per agent, starting at 1 |
| `Instructions` | owned type `AgentInstructions` | Frozen copy at publish time |
| `ModelProviderId` / `ModelId` | `Guid` (FK) | Frozen |
| `ExecutionPolicy` | owned type `AgentExecutionPolicy` | Max steps/duration/tokens/cost/retries/tool-calls (FR-040, FR-042) — frozen |
| `OutputFormat` | `AgentOutputFormat` enum | Frozen |
| `ToolsSnapshotJson` / `KnowledgeBasesSnapshotJson` / `MemoryPolicySnapshotJson` | `string` | Denormalized frozen copies of the associated collections at publish time — an `AgentExecution` reads *only* from here, never from the mutable `Agent`/`AgentTool`/etc. rows, guaranteeing FR-009/FR-010 hold even if the draft changes seconds later |
| `PublishedBy` | `string` (FK → user) | |
| `ChangeDescription` | `string?` | |

**Business rules**: Never updated after insert (no `Modified*` values expected in practice; the base columns exist only for schema uniformity). `VersionNumber` is immutable once assigned.

**Domain events**: `AgentVersionPublished`.

### AgentTool

Draft-time association between an `Agent` and a tool it may use.

| Field | Type | Notes |
|---|---|---|
| `AgentId` | `Guid` (FK) | |
| `ToolName` | `string` | Matches an `IAgentTool.Name` from the compile-time catalog (research.md Decision 10) — no `Tool` table; tools are code, not data, for this release |
| `ConfigurationJson` | `string?` | Tool-specific settings (e.g., a default Knowledge Base filter) |

### AgentKnowledgeBase

| Field | Type | Notes |
|---|---|---|
| `AgentId` | `Guid` (FK) | |
| `KnowledgeBaseId` | `Guid` (FK) | Access still re-validated per-execution via `IKnowledgeBaseRepository.ResolveOwnedIdsAsync` (FR-049) — this row expresses *configuration*, not a standing grant |

### AgentMemoryPolicy

One-to-one with `Agent`.

| Field | Type | Notes |
|---|---|---|
| `AgentId` | `Guid` (FK, unique) | |
| `AllowRead` | `bool` | Gates whether the Memory Search tool is usable at all |
| `AllowWriteProposals` | `bool` | Gates whether the Memory Write tool may call `CreateMemoryCandidateCommand` (research.md Decision 5) — never a direct write |
| `PreApprovedCategoriesJson` | `string?` | Categories for which the *agent* doesn't need to ask before proposing — the Memory Engine's own `MemoryCategoryPreference`/`MemoryApprovalMode` still governs final admission (FR-031) |

## Aggregate: AgentExecution

### AgentExecution (aggregate root)

One run of a specific `AgentVersion` (FR-009, FR-011).

| Field | Type | Notes |
|---|---|---|
| `AgentId` / `AgentVersionId` | `Guid` (FK) | Immutable reference — never repointed even after a newer version publishes |
| `RunByUserId` | `string` (FK) | The authenticated user the execution runs *as* (FR-046) |
| `Objective` | `string` | User-supplied |
| `Status` | `AgentExecutionStatus` enum | `Queued` / `Running` / `Paused` / `WaitingForApproval` / `Completed` / `Failed` / `Cancelled` (FR-015) |
| `IsTestExecution` | `bool` | FR-006 — never publishes a version, flagged distinctly in history |
| `ConversationIntegrationMode` | `AgentConversationIntegrationMode` enum | `ExistingConversation` / `NewConversation` / `Standalone` (FR-051) |
| `UserChatId` | `Guid?` (FK → `UserChat`) | Set when the mode links to a conversation (FR-052); null for `Standalone` |
| `PlanJson` | `string?` | The planner's goal + ordered step list (research.md Decision 11); steps themselves are the child `AgentExecutionStep` rows — this column is the plan's own metadata (goal, dependency graph), not a duplicate of step content |
| `FinalOutputJson` / `FinalOutputText` | `string?` | Shape depends on `Agent.OutputFormat` at the version snapshot |
| `StartedAtUtc` / `CompletedAtUtc` | `DateTime?` | |
| `TerminationReason` | `string?` | Populated when a budget/loop limit stopped the run (FR-040), when cancelled, or when it failed |

**Navigation**: `Steps` (`AgentExecutionStep`, 1–N), `Events` (`AgentExecutionEvent`, 1–N), `Approvals` (`AgentApproval`, 0–N), `Errors` (`AgentExecutionError`, 0–N), `Usage` (`AgentExecutionUsage`, 0–1), `Cost` (`AgentExecutionCost`, 0–1).

**State transitions** (FR-015): `Queued → Running → {Completed | Failed | Cancelled}`; `Running → Paused → Running` (resume) or `→ Cancelled`; `Running → WaitingForApproval → Running` (approved) or `→ Cancelled`/`→ Failed` (rejected, per the agent's own plan-adjustment logic). No transition ever leaves `Completed`/`Failed`/`Cancelled` (terminal).

**Business rules**: Never hard-deleted (audit trail, FR-050). `PlanJson`/step rows/events are append-only once written — an execution's history is immutable after the fact, even though the execution's own `Status` mutates. When `IsTestExecution` is `true`, any step whose tool requires a mutating permission is never executed — it is recorded `Skipped` instead (research.md Decision 12), so SC-007's "zero unintended changes to production data" holds structurally, not just by convention.

**Domain events**: `AgentExecutionStarted`, `AgentExecutionCompleted`, `AgentExecutionFailed`, `AgentExecutionCancelled`.

### AgentExecutionStep

| Field | Type | Notes |
|---|---|---|
| `AgentExecutionId` | `Guid` (FK) | |
| `StepIndex` | `int` | Ordering within the plan |
| `Description` | `string` | |
| `StepType` | `AgentExecutionStepType` enum | e.g. `ToolCall`, `ModelReasoning`, `Validation` |
| `Status` | `AgentExecutionStepStatus` enum | `Pending` / `Running` / `Completed` / `Failed` / `Skipped` / `Cancelled` / `WaitingForApproval` (FR-014) |
| `DependsOnStepId` | `Guid?` (FK, self) | FR-018 |
| `InputJson` / `OutputJson` | `string?` | |
| `ToolName` | `string?` | Set when `StepType == ToolCall`; matches an `AgentToolCall.ToolName` |
| `StartedAtUtc` / `CompletedAtUtc` | `DateTime?` | |
| `ErrorId` | `Guid?` (FK → `AgentExecutionError`) | |

### AgentExecutionEvent

Append-only, safe-metadata-only event stream (FR-034/FR-035) — the persisted backing store the SignalR hub (research.md Decision 9) replays/pushes from.

| Field | Type | Notes |
|---|---|---|
| `AgentExecutionId` | `Guid` (FK) | |
| `AgentVersionId` | `Guid` | Denormalized for FR-034's "every event carries agent version" without a join |
| `StepId` | `Guid?` (FK) | |
| `EventType` | `AgentExecutionEventType` enum | `ExecutionStarted` / `PlanCreated` / `StepStarted` / `StepCompleted` / `StepFailed` / `ToolCallStarted` / `ToolCallCompleted` / `ApprovalRequested` / `ApprovalGranted` / `ApprovalRejected` / `ExecutionCompleted` / `ExecutionFailed` / `ExecutionCancelled` |
| `Status` | `string` | Short status label matching the entity it describes at the time of the event |
| `SafeMetadataJson` | `string?` | Never chain-of-thought (FR-035) — only what `AgentExecutionStepDto`/`AgentToolCallDto` already expose |
| `OccurredAtUtc` | `DateTime` | |

### AgentToolCall

| Field | Type | Notes |
|---|---|---|
| `AgentExecutionStepId` | `Guid` (FK) | |
| `ToolName` | `string` | |
| `RiskLevel` | `AgentToolRiskLevel` enum | `Low` / `Medium` / `High` / `Critical` (FR-020) — copied from the tool's declared risk at call time, so a later change to a tool's risk level never rewrites history |
| `RequiredPermissionsJson` | `string` | Snapshot of `IAgentTool.RequiredPermissions` at call time |
| `ValidatedInputJson` | `string` | Post-schema-validation input (FR-021) |
| `ValidatedOutputJson` / `FailureReason` | `string?` | Exactly one is set |
| `StartedAtUtc` / `CompletedAtUtc` | `DateTime?` | |
| `WasApprovalRequired` | `bool` | |

### AgentApproval

| Field | Type | Notes |
|---|---|---|
| `AgentExecutionId` | `Guid` (FK) | |
| `AgentToolCallId` | `Guid?` (FK) | Null for a memory-write-style approval not tied to a tool call, if that shape is ever needed — always set for FR-025's tool-approval gate |
| `IntendedActionDescription` | `string` | Shown to the user before a decision (FR-027) |
| `IntendedParametersJson` | `string` | |
| `Decision` | `AgentApprovalDecision` enum | `Pending` / `Approved` / `Rejected` — mirrors `MemoryApprovalDecision`'s existing shape (research.md Decision 5) |
| `DecidedByUserId` | `string?` | Null when `WasPolicyBased` |
| `WasPolicyBased` | `bool` | True when `AgentPolicy` auto-approved (FR-025/FR-026) |
| `MatchedAgentPolicyId` | `Guid?` (FK) | Set when `WasPolicyBased` |
| `DecidedAtUtc` | `DateTime?` | |

### AgentExecutionError

| Field | Type | Notes |
|---|---|---|
| `AgentExecutionId` | `Guid` (FK) | |
| `AgentExecutionStepId` | `Guid?` (FK) | Null for an execution-level failure not tied to one step |
| `Category` | `AgentExecutionErrorCategory` enum | `ToolFailure` / `ProviderFailure` / `InvalidToolOutput` / `InvalidModelResponse` / `ContextLimitExceeded` / `BudgetExceeded` / `UserCancellation` / `ExecutionTimeout` (spec's Failure Handling list) |
| `Message` | `string` | Actionable, user-safe (never a raw provider stack trace) |
| `RetryCount` | `int` | |
| `OccurredAtUtc` | `DateTime` | |

### AgentExecutionUsage

One row per execution (aggregated; per-call detail lives in `AgentToolCall`/provider call logs already captured by the existing AI usage tracking, not duplicated here).

| Field | Type | Notes |
|---|---|---|
| `AgentExecutionId` | `Guid` (FK, unique) | |
| `InputTokenCount` / `OutputTokenCount` / `ReasoningTokenCount` | `int?` | Mirrors `ChatUsage`'s shape (research.md Decision 3) |
| `ToolCallCount` | `int` | |
| `StepCount` | `int` | |

### AgentExecutionCost

| Field | Type | Notes |
|---|---|---|
| `AgentExecutionId` | `Guid` (FK, unique) | |
| `EstimatedCost` | `decimal` | Derived from `AgentExecutionUsage` via the same pricing lookup (`ModelPricing`) already used elsewhere — no new pricing logic |
| `Currency` | `string` | |

## Aggregate: AgentPolicy

### AgentPolicy

Administrator-managed auto-approval rule (FR-025/FR-026, research.md Decision 1).

| Field | Type | Notes |
|---|---|---|
| `OrganizationId` | `Guid?` | **Reserved for future multi-tenancy — always `null` this release** (research.md Decision 1) |
| `Name` / `Description` | `string` | |
| `ToolName` | `string` | Which tool this policy covers |
| `ConditionsJson` | `string?` | e.g. a parameter allow-list; empty means "always" |
| `CreatedByUserId` | `string` (FK, must hold `Administrator`/`Super User` role at creation time) | |
| `IsEnabled` | `bool` | |

**Business rules**: Only a user in the `Administrator`/`Super User` role may create/update/disable a policy — enforced the same way `AdministratorOrSuperUser` already gates `GetOrganizationDashboardSummaryQuery`.

### AgentUserExecutionLimit

Per-user override for FR-042 (research.md Decision 2) — not one of the spec's originally named entities, but required to implement FR-042/FR-043 without a `SubscriptionTier` concept that doesn't exist yet.

| Field | Type | Notes |
|---|---|---|
| `UserId` | `string` (FK, unique) | |
| `MaxConcurrentExecutions` | `int` | Overrides `AgentRuntimeOptions.DefaultMaxConcurrentExecutions` (system-wide config default) when present |
| `SetByUserId` | `string` (FK, must hold `Administrator`/`Super User` role) | |

## Aggregate: AgentAuditLog

### AgentAuditLog

Tamper-resistant security record (FR-050) — append-only, deliberately **not** hard-FK'd to `AgentExecution` (same rationale as `KnowledgeBaseAuditLogs`/`DocumentAuditLog`: an audit entry for a later-purged execution is retained).

| Field | Type | Notes |
|---|---|---|
| `AgentExecutionId` | `Guid` | Soft reference, no FK constraint |
| `UserId` | `string` | |
| `Action` | `AgentAuditAction` enum | `PermissionChecked` / `PermissionDenied` / `ApprovalDecided` / `CrossUserAccessAttempted` / `ExecutionCompleted` / `ExecutionFailed` |
| `DetailsJson` | `string` | Short, sanitized summary — never raw prompt/tool content (matches `KnowledgeBaseAuditLogs.DetailsJson` convention) |
| `OccurredAtUtc` | `DateTime` | |

## Enumerations

```
AgentType:                    Conversational | Research | Document | Knowledge | Task
AgentStatus:                  Draft | Published | Archived
AgentOutputFormat:             PlainText | Markdown | Json | StructuredOutput | Files
AgentExecutionStatus:          Queued | Running | Paused | WaitingForApproval | Completed | Failed | Cancelled
AgentExecutionStepStatus:      Pending | Running | Completed | Failed | Skipped | Cancelled | WaitingForApproval
AgentExecutionStepType:        ToolCall | ModelReasoning | Validation
AgentExecutionEventType:       ExecutionStarted | PlanCreated | StepStarted | StepCompleted | StepFailed |
                                ToolCallStarted | ToolCallCompleted | ApprovalRequested | ApprovalGranted |
                                ApprovalRejected | ExecutionCompleted | ExecutionFailed | ExecutionCancelled
AgentToolRiskLevel:             Low | Medium | High | Critical
AgentApprovalDecision:          Pending | Approved | Rejected
AgentExecutionErrorCategory:    ToolFailure | ProviderFailure | InvalidToolOutput | InvalidModelResponse |
                                ContextLimitExceeded | BudgetExceeded | UserCancellation | ExecutionTimeout
AgentConversationIntegrationMode: ExistingConversation | NewConversation | Standalone
AgentAuditAction:               PermissionChecked | PermissionDenied | ApprovalDecided |
                                CrossUserAccessAttempted | ExecutionCompleted | ExecutionFailed
```

## Value objects (owned types, no separate table)

- **AgentInstructions** — `SystemInstructions`, `Objectives`, `Constraints`, `BehavioralRules`, `OutputRequirements`, `ToolUsageRules`, `SafetyRules` (all `string?`) — FR-004.
- **AgentExecutionPolicy** — `MaxSteps`, `MaxExecutionDurationSeconds`, `MaxTokens`, `MaxCost` (`decimal?`), `MaxToolCalls`, `MaxRetries` — FR-040. Defaults come from `AgentRuntimeOptions` when an agent doesn't override a given limit.

## Delete behavior

| Parent | Child | Behavior |
|---|---|---|
| Agent | AgentVersion | Restrict (never cascade — published versions outlive a soft-deleted agent for audit purposes) |
| Agent | AgentTool / AgentKnowledgeBase / AgentMemoryPolicy | Cascade soft delete (draft-only configuration, no audit value once the agent itself is gone) |
| AgentVersion | AgentExecution | Restrict |
| AgentExecution | AgentExecutionStep / AgentExecutionEvent / AgentToolCall / AgentApproval / AgentExecutionError / AgentExecutionUsage / AgentExecutionCost | Cascade (child rows have no independent meaning outside their execution) |

## Explicitly not modeled (deferred, per research.md)

- **Organization / Tenant** — Decision 1; `AgentPolicy.OrganizationId` is reserved but unused.
- **SubscriptionTier** — Decision 2; `AgentUserExecutionLimit` is per-user only.
- **Tool** (as a data row) — tools are a compile-time `IAgentTool` catalog (Decision 10), not a database table; `AgentTool.ToolName` is a string key into that catalog, validated at save time against the registered set.
- **AgentSchedule / AgentShare / AgentMarketplaceItem** — explicitly out of scope (spec's "Design for future" list); no columns reserved for them since their shape is unknown until specified.
