# Contract: Agents REST API

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Follows `docs/API_GUIDELINES.md`/constitution §6 exactly: nouns, plural, kebab/lowercase, `/api/v1/...`; actions that don't map to CRUD are `POST .../actions/{verb}`; every error is RFC 7807 Problem Details (`application/problem+json`) with a `traceId`; every endpoint is `[Authorize]` by default; list endpoints are cursor-paginated; authorization (ownership, role) is enforced in Application-layer handlers, not ad hoc controller `if` checks — controllers stay thin, matching `PromptsController`/`MemoriesController`.

Two controllers, matching the two aggregates in data-model.md that have independent lifecycles: `AgentsController` (definition/lifecycle/versioning) and `AgentExecutionsController` (runtime). A third, small `AgentPoliciesController` covers the admin-only policy surface.

## AgentsController — `/api/v1/agents`

| Method | Path | Command/Query | Notes |
|---|---|---|---|
| `POST` | `/agents` | `CreateAgentCommand` | Creates in `Draft`. 201 + `Location`. |
| `GET` | `/agents` | `ListAgentsQuery` | Cursor-paginated; filters: `status`, `agentType`, `search`. Owner-scoped. |
| `GET` | `/agents/{id}` | `GetAgentQuery` | 404 (never 403) if not owned — `AgentOwnershipGuard`, same shape as `PromptOwnershipGuard`. |
| `PUT` | `/agents/{id}` | `UpdateAgentCommand` | Draft fields only; 409 (Problem Details, `RowVersion` mismatch) on concurrent edit. |
| `DELETE` | `/agents/{id}` | `DeleteAgentCommand` | Soft delete; blocked (422) if the agent has any non-`Draft` `AgentExecution` and the caller didn't pass `?force=true`... **no** — per data-model.md's Delete Behavior table this is a soft delete that never cascades, so it is always allowed; executions simply keep referencing the (now soft-deleted) `AgentVersion`, consistent with `Agent → AgentVersion: Restrict`. |
| `POST` | `/agents/{id}/actions/archive` | `ArchiveAgentCommand` | FR-002/FR-003 |
| `POST` | `/agents/{id}/actions/restore` | `RestoreAgentCommand` | FR-002/FR-003 |
| `POST` | `/agents/{id}/actions/duplicate` | `DuplicateAgentCommand` | Returns the new `Agent` id (201) |
| `POST` | `/agents/{id}/versions` | `PublishAgentVersionCommand` | Body: `changeDescription?`. Validates the draft is publishable (FR-001's mandatory fields) before snapshotting; 422 with field-level Problem Details extension on failure. |
| `GET` | `/agents/{id}/versions` | `ListAgentVersionsQuery` | |
| `GET` | `/agents/{id}/versions/{versionNumber}` | `GetAgentVersionQuery` | Immutable snapshot, safe to cache client-side indefinitely per version |

## AgentExecutionsController — `/api/v1/agent-executions`

| Method | Path | Command/Query | Notes |
|---|---|---|---|
| `POST` | `/agent-executions` | `StartAgentExecutionCommand` | Body: `agentId`, `agentVersionNumber?` (defaults to `Agent.PublishedVersionNumber`; required and caller-chosen in test mode, FR-011), `objective`, `conversationIntegrationMode`, `userChatId?`, `isTestExecution?`. Enforces FR-042/FR-043 (concurrency cap) — 429 Problem Details (`type: agent-execution-concurrency-limit-reached`) if the caller is already at their cap. 202 Accepted + `Location` (execution never finishes synchronously — FR-017). |
| `GET` | `/agent-executions` | `ListAgentExecutionsQuery` | Cursor-paginated; filters: `agentId`, `status`, `isTestExecution`. Owner-scoped (FR-046/SC-010). |
| `GET` | `/agent-executions/{id}` | `GetAgentExecutionQuery` | Full history read (US5) — includes steps/tool calls/approvals/usage/cost/final output per FR-036/FR-050, assembled server-side from the child tables in data-model.md. |
| `GET` | `/agent-executions/{id}/events` | `GetAgentExecutionEventsQuery` | Cursor-paginated `AgentExecutionEvent` replay — the REST fallback/reconciliation path behind the live SignalR feed (research.md Decision 9), same "SSE/hub primary, REST polling fallback" convention as `DocumentProcessingHub`. |
| `GET` | `/agent-executions/{id}/steps` | `GetAgentExecutionStepsQuery` | |
| `GET` | `/agent-executions/{id}/tool-calls` | `GetAgentToolCallsQuery` | |
| `GET` | `/agent-executions/{id}/usage` | `GetAgentExecutionUsageQuery` | Returns `AgentExecutionUsage` + `AgentExecutionCost` together |
| `POST` | `/agent-executions/{id}/actions/pause` | `PauseAgentExecutionCommand` | 409 if not `Running` |
| `POST` | `/agent-executions/{id}/actions/resume` | `ResumeAgentExecutionCommand` | 409 if not `Paused`; re-enqueues the Hangfire runner (research.md Decision 8) |
| `POST` | `/agent-executions/{id}/actions/cancel` | `CancelAgentExecutionCommand` | Allowed from any non-terminal status; runner observes at next step boundary (SC-009: ≤5s) |
| `GET` | `/agent-executions/{id}/approvals/{approvalId}` | `GetAgentApprovalQuery` | |
| `POST` | `/agent-executions/{id}/approvals/{approvalId}/actions/approve` | `ApproveAgentActionCommand` | 409 if `Decision != Pending`; re-enqueues the runner |
| `POST` | `/agent-executions/{id}/approvals/{approvalId}/actions/reject` | `RejectAgentActionCommand` | Body: `reason?` |

## AgentPoliciesController — `/api/v1/agent-policies` (Administrator/Super User only)

| Method | Path | Command/Query | Notes |
|---|---|---|---|
| `POST` | `/agent-policies` | `CreateAgentPolicyCommand` | `[Authorize(Policy = "AdministratorOrSuperUser")]`, same policy `GetOrganizationDashboardSummaryQuery` already uses |
| `GET` | `/agent-policies` | `ListAgentPoliciesQuery` | |
| `PUT` | `/agent-policies/{id}` | `UpdateAgentPolicyCommand` | Includes `isEnabled` toggle |
| `DELETE` | `/agent-policies/{id}` | `DeleteAgentPolicyCommand` | |
| `PUT` | `/agent-policies/user-limits/{userId}` | `SetAgentUserExecutionLimitCommand` | FR-042's per-user concurrency override (research.md Decision 2) |

## Error shape (every endpoint)

Standard Problem Details, matching every other controller in the codebase:

```json
{
  "type": "https://asklucy.io/problems/agent-execution-concurrency-limit-reached",
  "title": "Concurrent execution limit reached",
  "status": 429,
  "detail": "You have 3 executions already running (limit: 3). Cancel or wait for one to finish before starting another.",
  "traceId": "00-4bf9...-00"
}
```

Notable `type` values beyond generic validation/not-found/conflict: `agent-execution-concurrency-limit-reached` (429), `agent-tool-permission-denied` (403 — the one deliberate exception to the "404 not 403" convention, because a permission *shape* mismatch, unlike ownership, is not something to hide: the caller legitimately owns the agent, they just lack a specific permission), `agent-budget-exceeded` (200 on the execution resource itself — this is a normal terminal state, not an HTTP error; surfaced via `AgentExecution.TerminationReason`, not a 4xx/5xx).
