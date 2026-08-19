# Contract: Workflows REST API

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Follows `docs/API_GUIDELINES.md`/constitution §6 exactly: nouns, plural, kebab/lowercase, `/api/v1/...`; actions that don't map to CRUD are `POST .../actions/{verb}`; every error is RFC 7807 Problem Details with a `traceId`; every endpoint is `[Authorize]` by default, no role/tier restriction (FR-068); list endpoints are cursor-paginated; ownership/authorization is enforced in Application-layer handlers (`WorkflowOwnershipGuard`, same shape as `AgentOwnershipGuard`), never ad hoc controller `if` checks.

Three controllers, matching the three aggregates in data-model.md with independent lifecycles: `WorkflowsController` (definition/lifecycle/versioning), `WorkflowExecutionsController` (runtime), and a small `WorkflowPoliciesController` (admin/owner policy surface, mirrors `AgentPoliciesController`).

## WorkflowsController — `/api/v1/workflows`

| Method | Path | Command/Query | Notes |
|---|---|---|---|
| `POST` | `/workflows` | `CreateWorkflowCommand` | Creates in `Draft` with an empty `DraftDefinitionJson`. 201 + `Location`. 409 (Problem Details, `type: workflow-name-already-exists`) if the name collides with another of the caller's own workflows (FR-001, case-insensitive). |
| `GET` | `/workflows` | `ListWorkflowsQuery` | Cursor-paginated; filters: `status`, `workflowType`, `search`. Owner-scoped. |
| `GET` | `/workflows/{id}` | `GetWorkflowQuery` | 404 (never 403) if not owned — `WorkflowOwnershipGuard`. Returns `DraftDefinitionJson` for the designer to load. |
| `PUT` | `/workflows/{id}` | `UpdateWorkflowCommand` | Body: name/description/`draftDefinitionJson` (the full canvas document, FR-009's "save a draft"). 409 (`RowVersion` mismatch) on concurrent edit — the designer's own unsaved-changes flow (FR-009) surfaces this as a merge/reload prompt, not a silent overwrite. |
| `DELETE` | `/workflows/{id}` | `DeleteWorkflowCommand` | Soft delete; always allowed per data-model.md's Delete Behavior table — executions keep referencing the (now soft-deleted) `WorkflowVersion`, mirroring `Agent → AgentVersion: Restrict`. |
| `POST` | `/workflows/{id}/actions/archive` | `ArchiveWorkflowCommand` | FR-002/FR-003 |
| `POST` | `/workflows/{id}/actions/restore` | `RestoreWorkflowCommand` | FR-002/FR-003 |
| `POST` | `/workflows/{id}/actions/duplicate` | `DuplicateWorkflowCommand` | Copies `DraftDefinitionJson` only into a new `Workflow` (Draft). Returns the new id (201). |
| `POST` | `/workflows/{id}/actions/validate` | `ValidateWorkflowCommand` | FR-016 — runs every publish-blocking rule (disconnected nodes, missing Start/End, invalid connections, unsupported cycles, missing required inputs, invalid variable references, invalid expressions, missing permissions, invalid config, unbounded loops, missing error/approval policy) against the *current draft* without publishing. Returns a list of `{ nodeKey?, severity, message }` — used by the Designer's live validation panel (FR-011) and re-run automatically before `Publish`. |
| `POST` | `/workflows/{id}/versions` | `PublishWorkflowVersionCommand` | Body: `changeDescription?`. Runs `ValidateWorkflowCommand`'s rule set first; 422 with field-level Problem Details extension listing every violation if any critical rule fails (SC-009 — 0 workflows publish with a critical error present). On success, materializes `DraftDefinitionJson` into `WorkflowNode`/`WorkflowConnection`/`WorkflowVariable` rows (research.md Decision 19). |
| `GET` | `/workflows/{id}/versions` | `ListWorkflowVersionsQuery` | |
| `GET` | `/workflows/{id}/versions/{versionNumber}` | `GetWorkflowVersionQuery` | Immutable snapshot, safe to cache client-side indefinitely per version |
| `GET` | `/workflows/{id}/statistics` | `GetWorkflowStatisticsQuery` | FR-050 — active/queued/failed/completed execution counts, average duration, failure rate, node-level performance, AI usage, estimated cost, scoped to workflows the caller owns |

## WorkflowExecutionsController — `/api/v1/workflow-executions`

| Method | Path | Command/Query | Notes |
|---|---|---|---|
| `POST` | `/workflow-executions` | `StartWorkflowExecutionCommand` | Body: `workflowId`, `workflowVersionNumber?` (defaults to `Workflow.PublishedVersionNumber`), `inputsJson`, `isTestExecution?`. Enforces FR-069/FR-070 (concurrency cap) — 429 Problem Details (`type: workflow-execution-concurrency-limit-reached`) if the caller is already at their cap. 202 Accepted + `Location` (never finishes synchronously — FR-047). |
| `GET` | `/workflow-executions` | `ListWorkflowExecutionsQuery` | Cursor-paginated; filters: `workflowId`, `status`, `triggerType`. Owner-scoped (FR-059/SC-008). |
| `GET` | `/workflow-executions/{id}` | `GetWorkflowExecutionQuery` | Full history read (User Story 8) — nodes/approvals/errors/usage/cost/final output, assembled server-side from data-model.md's child tables (FR-051). |
| `GET` | `/workflow-executions/{id}/events` | `GetWorkflowExecutionEventsQuery` | Cursor-paginated `WorkflowExecutionEvent` replay — REST fallback behind the live SignalR feed (research.md Decision 8), same convention as `AgentExecutionsController`'s equivalent endpoint. |
| `GET` | `/workflow-executions/{id}/nodes` | `GetWorkflowExecutionNodesQuery` | Per-node results (FR-051) |
| `GET` | `/workflow-executions/{id}/usage` | `GetWorkflowExecutionUsageQuery` | Returns `WorkflowExecutionUsage` + `WorkflowExecutionCost` together |
| `POST` | `/workflow-executions/{id}/actions/pause` | `PauseWorkflowExecutionCommand` | 409 if not `Running` |
| `POST` | `/workflow-executions/{id}/actions/resume` | `ResumeWorkflowExecutionCommand` | 409 if not `Paused`; re-enqueues the Hangfire runner (research.md Decision 7) |
| `POST` | `/workflow-executions/{id}/actions/cancel` | `CancelWorkflowExecutionCommand` | Allowed from any non-terminal status; runner observes at next node boundary (SC-007: ≤5s) |
| `POST` | `/workflow-executions/{id}/nodes/{nodeExecutionId}/actions/retry` | `RetryWorkflowExecutionNodeCommand` | 409 if the node isn't `Failed`; re-enqueues the runner starting from this node |
| `GET` | `/workflow-executions/{id}/approvals/{approvalId}` | `GetWorkflowApprovalQuery` | |
| `POST` | `/workflow-executions/{id}/approvals/{approvalId}/actions/approve` | `ApproveWorkflowNodeCommand` | 409 if `Decision` already set; re-enqueues the runner |
| `POST` | `/workflow-executions/{id}/approvals/{approvalId}/actions/reject` | `RejectWorkflowNodeCommand` | Body: `reason?` |
| `POST` | `/workflow-executions/{id}/approvals/{approvalId}/actions/request-changes` | `RequestWorkflowNodeChangesCommand` | Body: `notes?` — routes per the node's configured rejection path (User Story 5 Acceptance Scenario 3) |

## WorkflowPoliciesController — `/api/v1/workflow-policies` (Administrator/Super User, or workflow owner for owner-scoped policies)

| Method | Path | Command/Query | Notes |
|---|---|---|---|
| `POST` | `/workflow-policies` | `CreateWorkflowPolicyCommand` | Mirrors `AgentPoliciesController`'s `[Authorize(Policy = "AdministratorOrSuperUser")]` shape |
| `GET` | `/workflow-policies` | `ListWorkflowPoliciesQuery` | |
| `PUT` | `/workflow-policies/{id}` | `UpdateWorkflowPolicyCommand` | Includes `isEnabled` toggle |
| `DELETE` | `/workflow-policies/{id}` | `DeleteWorkflowPolicyCommand` | |
| `PUT` | `/workflow-policies/user-limits/{userId}` | `SetWorkflowUserExecutionLimitCommand` | FR-069's per-user concurrency override (research.md Decision 11) |

## Error shape (every endpoint)

Standard Problem Details, matching every other controller in the codebase:

```json
{
  "type": "https://asklucy.io/problems/workflow-execution-concurrency-limit-reached",
  "title": "Concurrent execution limit reached",
  "status": 429,
  "detail": "You have 3 workflow executions already running (limit: 3). Cancel or wait for one to finish before starting another.",
  "traceId": "00-4bf9...-00"
}
```

Notable `type` values beyond generic validation/not-found/conflict: `workflow-name-already-exists` (409, FR-001), `workflow-execution-concurrency-limit-reached` (429, FR-070), `workflow-validation-failed` (422 on publish, FR-016, with a `violations[]` extension member), `workflow-node-permission-denied` (403 — the same deliberate "not hidden as 404" exception `agents-api.md` documents, since the caller legitimately owns the workflow and only lacks a specific node's underlying permission), `workflow-budget-exceeded` (200 on the execution resource itself — a normal terminal state via `WorkflowExecution.TerminationReason`, not an HTTP error, mirroring `agent-budget-exceeded`).
