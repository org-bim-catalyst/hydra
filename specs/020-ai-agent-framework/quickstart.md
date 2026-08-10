# Quickstart: Validating the AI Agent Framework

**Feature**: [spec.md](./spec.md) | **API contract**: [contracts/agents-api.md](./contracts/agents-api.md) | **Events**: [contracts/agent-execution-events.md](./contracts/agent-execution-events.md)

This is a validation/run guide, not an implementation guide — it proves each user story in spec.md end-to-end against the running API. It assumes the feature is implemented per plan.md/data-model.md/contracts/.

## Prerequisites

- Backend running locally (`dotnet run` in `src/AskLucy.Web`, or the repo's existing local dev flow) with a migrated database (`dotnet ef database update` against `AskLucyDbContext`).
- An authenticated test user (existing login flow) with a valid JWT — every request below carries `Authorization: Bearer <token>`.
- At least one enabled `AIProvider`/`AIModel` (existing seed data) and one `KnowledgeBase` with at least one indexed document (from specs/014/015/016) for the tool-using scenarios.
- Hangfire dashboard reachable at `/hangfire` (Administrator/Super User login) to observe the background runner job directly if needed.

## Scenario 1 — Create and run a simple agent (User Story 1, P1)

1. `POST /api/v1/agents` with `{ name, description, instructions: { systemInstructions: "You are a concise assistant." }, modelProviderId, modelId, outputFormat: "PlainText" }` → expect `201` with the new `agentId`, `status: "Draft"`.
2. `POST /api/v1/agents/{agentId}/versions` with `{}` → expect `201`, `versionNumber: 1`.
3. `POST /api/v1/agent-executions` with `{ agentId, objective: "Say hello in one sentence.", conversationIntegrationMode: "Standalone" }` → expect `202` with `executionId`.
4. Poll `GET /api/v1/agent-executions/{executionId}` until `status: "Completed"` → expect `finalOutputText` populated, `steps` containing at least one `ModelReasoning`-type step, zero `ToolCall` steps (no tools configured — Acceptance Scenario 3).
5. **Pass criterion**: end-to-end latency from step 3 to `Completed` is reasonable for a single-turn call, and the result is persisted and re-fetchable (re-run step 4's `GET` after the fact — must still return the same `finalOutputText`).

## Scenario 2 — Multi-step execution with tools and citations (User Story 2, P2)

1. `PUT /api/v1/agents/{agentId}` adding `tools: [{ toolName: "KnowledgeSearchTool" }]`, `knowledgeBases: [{ knowledgeBaseId }]`, `agentType: "Task"`.
2. Publish a new version (`POST .../versions`).
3. Start an execution with an objective that requires the indexed document's content (e.g., "Summarize what our onboarding guide says about X.").
4. Watch `GET /api/v1/agent-executions/{id}/steps` — expect ≥2 steps, one with `stepType: "ToolCall"`, `toolName: "KnowledgeSearchTool"`, `status` progressing `Pending → Running → Completed`.
5. `GET /api/v1/agent-executions/{id}` on completion — expect `finalOutputText`/`finalOutputJson` to include citation references sourced from `AgentToolCall.ValidatedOutputJson` (per FR-045).
6. **Pass criterion**: the step with `dependsOnStepId` pointing at the tool-call step never has a `startedAtUtc` earlier than the tool-call step's `completedAtUtc` (FR-018 dependency ordering, Acceptance Scenario 2).

## Scenario 3 — Approval gate for a high-risk action (User Story 3, P3)

Since no built-in tool ships at `High`/`Critical` risk this release ([contracts/agent-tool-contract.md](./contracts/agent-tool-contract.md)), this scenario runs against the test fixture tool (`FakeHighRiskTool`, test-only, registered only in the `Testing`/`Development` environment) so the approval gate is exercised without a real destructive action.

1. Configure a test agent with `FakeHighRiskTool` attached; publish.
2. Start an execution whose objective causes the planner to select that tool.
3. Poll `GET /api/v1/agent-executions/{id}` — expect `status: "WaitingForApproval"`, and `GET .../approvals/{approvalId}` returns `decision: "Pending"` with `intendedActionDescription`/`intendedParametersJson` populated (FR-027).
4. `POST /api/v1/agent-executions/{id}/approvals/{approvalId}/actions/approve` → expect `202`; poll until the execution reaches `Completed`, and confirm `GET .../approvals/{approvalId}` now shows `decision: "Approved"`, `decidedByUserId` set (FR-028 audit trail).
5. Repeat from step 2 with `.../actions/reject` instead → expect the step to end `Failed`/`Skipped` per the agent's plan, execution reaching a terminal state without ever calling the tool.
6. **Policy path**: as an Administrator, `POST /api/v1/agent-policies` covering `FakeHighRiskTool`; repeat step 2 → expect the execution to reach `Completed` *without* ever entering `WaitingForApproval`, and `GET .../approvals` to show one row with `wasPolicyBased: true`, `decidedByUserId: null`.

## Scenario 4 — Real-time visibility and cancellation (User Story 4, P4)

1. Open a SignalR connection to `/hubs/agent-execution` per [contracts/agent-execution-events.md](./contracts/agent-execution-events.md) (a small script/Postman WS client is sufficient — no need for the full frontend).
2. Start a multi-step execution (reuse Scenario 2's agent).
3. Observe `StepStarted`/`StepCompleted`/`ToolCallStarted`/`ToolCallCompleted` events arriving live; cross-check each against the corresponding REST resource (`GET .../steps`, `GET .../tool-calls`) to confirm they agree.
4. Mid-execution, `POST .../actions/cancel` → expect an `ExecutionCancelled` push within 5 seconds (SC-009) and `GET /api/v1/agent-executions/{id}` to show `status: "Cancelled"`.
5. **Pass criterion**: no event payload contains anything beyond what [contracts/agent-execution-events.md](./contracts/agent-execution-events.md) declares — no raw prompt text, no chain-of-thought (FR-035).

## Scenario 5 — Execution history (User Story 5, P5)

1. `GET /api/v1/agent-executions?agentId={agentId}` → expect every execution from Scenarios 1–4 listed, cursor-paginated.
2. `GET /api/v1/agent-executions/{id}` for a completed one → expect every field spec.md's "Execution History" section lists (agent, version, objective, status, duration, model, provider, token usage, cost, steps, tool calls, errors, approvals, final output) present in one response.
3. As a second test user, `GET /api/v1/agent-executions/{id}` for the first user's execution → expect `404` (FR-046/SC-010), and confirm a corresponding `AgentAuditLog` row with `action: "CrossUserAccessAttempted"` was written (Application-layer test, not REST-visible).

## Scenario 6 — Versioning and testing isolation (User Story 6, P6)

1. Edit the agent from Scenario 1 (`PUT /api/v1/agents/{agentId}`, change instructions), then `POST .../versions` → expect `versionNumber: 2`.
2. `GET /api/v1/agent-executions/{executionId}` for the Scenario 1 execution (which ran under version 1) → expect `agentVersionNumber: 1` still, unchanged.
3. `POST /api/v1/agent-executions` with `{ agentId, agentVersionNumber: 1, objective: "...", isTestExecution: true }` → run against the *old* version deliberately; expect `isTestExecution: true` on the result and no new `AgentVersion` created as a side effect.
4. `POST /api/v1/agents/{agentId}/actions/duplicate` → expect a new `agentId` in `Draft` with the version-2 content but no `versions`/`executions` of its own.
5. `POST /api/v1/agents/{agentId}/actions/archive` then `.../actions/restore` → expect `status` round-trips `Published → Archived → Published` (or the pre-archive status) with `versions` untouched throughout.

## Cleanup

None required — soft-deleted/archived test data is inert and matches the same retention convention as every other feature (no destructive step in this guide touches another user's data).
