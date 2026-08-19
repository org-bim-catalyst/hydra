# Quickstart: Validating the Workflow & Tool Orchestration Engine

**Feature**: [spec.md](./spec.md) | **API**: [contracts/workflows-api.md](./contracts/workflows-api.md) | **Events**: [contracts/workflow-execution-events.md](./contracts/workflow-execution-events.md) | **Nodes**: [contracts/workflow-node-contract.md](./contracts/workflow-node-contract.md) | **Expressions**: [contracts/workflow-expression-engine.md](./contracts/workflow-expression-engine.md)

This is a validation/run guide, not an implementation guide — it proves each user story in spec.md end-to-end against the running API. It assumes the feature is implemented per plan.md/data-model.md/contracts/.

## Prerequisites

- Backend running locally (`dotnet run` in `src/AskLucy.Web`) with a migrated database (`dotnet ef database update` against `AskLucyDbContext`).
- An authenticated test user with a valid JWT — every request below carries `Authorization: Bearer <token>`.
- At least one enabled `AIProvider`/`AIModel`, one `KnowledgeBase` with an indexed document (specs 014–016), one saved `Prompt` (spec 019), and one published `Agent` (spec 020) for the capability-node scenarios.
- Hangfire dashboard at `/hangfire` to observe the `WorkflowExecutionRunnerJob` directly if needed.
- Frontend (`ClientApp`) running for Scenario 2's Designer walkthrough; every other scenario is REST-only.

## Scenario 1 — Create and run a simple deterministic workflow (User Story 1, P1)

1. `POST /api/v1/workflows` with `{ name: "Echo Workflow", description: "..." }` → expect `201`, `status: "Draft"`.
2. `PUT /api/v1/workflows/{id}` with a `draftDefinitionJson` describing `Start → Transform (uppercase the input text) → End`, one declared input (`text: String`) and one output (`result: String`).
3. `POST /api/v1/workflows/{id}/actions/validate` → expect an empty violations list.
4. `POST /api/v1/workflows/{id}/versions` with `{}` → expect `201`, `versionNumber: 1`.
5. `POST /api/v1/workflow-executions` with `{ workflowId, inputsJson: { "text": "hello" } }` → expect `202` with `executionId`.
6. Poll `GET /api/v1/workflow-executions/{executionId}` until `status: "Completed"` → expect `finalOutputJson.result == "HELLO"`.
7. **Pass criterion**: re-fetching step 6's `GET` afterward still returns the same result (FR-051 durability), and `GET .../nodes` shows exactly three `WorkflowExecutionNode` rows (`Start`, `Transform`, `End`), all `Completed`.

## Scenario 2 — Visually design a multi-step workflow in the Designer (User Story 2, P2)

1. Open the Workflow Designer (`ClientApp`, `features/workflows/pages/WorkflowDesignerPage.tsx`) on a new, empty workflow.
2. Search the node palette for "RAG Search," drag it onto the canvas; configure it with the Knowledge Base from Prerequisites.
3. Connect `Start → RAG Search → End`; observe the connection is accepted (matching declared types) and a mismatched connection attempt (e.g., a `Number` output into a `String`-typed input) is visibly rejected before it can be drawn (FR-008).
4. Trigger undo (remove the RAG Search node), then redo — expect the node and its configuration to reappear exactly as configured (FR-007).
5. Save as draft (`Ctrl+S` or the Save button) — expect the unsaved-changes indicator to clear (FR-009); reload the page and confirm the same layout/connections/configuration reload from `GET /api/v1/workflows/{id}`.
6. Delete the connection from RAG Search to End, leaving it disconnected — expect the validation panel to surface "disconnected node" before Publish is enabled (FR-016).
7. **Pass criterion**: Publish is blocked (button disabled or 422 on attempt) while the disconnected-node violation is present; reconnecting and re-validating clears it.

## Scenario 3 — Publish, version, and execute against immutable versions (User Story 3, P3)

1. Publish Scenario 1's workflow again after editing the Transform node's configuration (`PUT` then `POST .../versions`) → expect `versionNumber: 2`.
2. `GET /api/v1/workflow-executions/{executionId}` for the Scenario 1 execution (which ran under version 1) → expect `workflowVersionNumber: 1`, unchanged, and its recorded node behavior still matches version 1's original configuration.
3. `POST /api/v1/workflow-executions` explicitly targeting `workflowVersionNumber: 1` → expect it to still run version 1's (older) Transform behavior, even though version 2 now exists.
4. `POST /api/v1/workflows/{id}/actions/duplicate` → expect a new `Workflow` in `Draft` with the current draft copied, `publishedVersionNumber: null`, and zero shared version history with the original.
5. **Pass criterion**: `GET /api/v1/workflows/{id}/versions/1` still returns version 1's frozen node/connection/variable rows byte-for-byte identical to what step 1 originally published (FR-014).

## Scenario 4 — Conditional branching, parallel execution, and merge (User Story 4, P4)

1. Build a new workflow: `Start → Classify (Transform) → Condition (category == "urgent")`, with a `true` branch to a `Parallel` node fanning into two branches (a `RagSearch` and a `MemorySearch`) feeding a `Merge (Collect All)` node, and a `false` branch going straight to `End`.
2. Publish; run with an input that evaluates the condition to `true` — `GET .../nodes` shows both parallel branch nodes `Completed` and the `false`-branch path entirely absent (not merely `Skipped` — never instantiated) or explicitly `Skipped` per the Condition's routing (Acceptance Scenario 4.1).
3. Run again with an input that evaluates to `false` — expect the Parallel/Merge nodes' `WorkflowExecutionNode` rows marked `Skipped`, and the execution completing via the `false` branch only.
4. Configure a bounded loop (e.g., "process each item," max iterations 3) over a 5-item `Collection` input; run it — expect exactly 3 iterations to execute and the stop reason recorded as "maximum iterations reached," not an error (FR-032).
5. Attempt to publish a workflow with an unsupported circular dependency → expect `422` with a violation identifying the specific cycle (FR-016).
6. **Pass criterion**: the `Merge` node's `WorkflowExecutionNode.OutputJson` for the `Collect All` strategy contains both branches' outputs, and this is visible in the execution history without needing to inspect either branch node individually.

## Scenario 5 — Human approval gate (User Story 5, P5)

1. Build a workflow with a `NativeTool` node wrapping a tool the underlying `IAgentTool` catalog marks `High`/`Critical` risk (the same test-only `FakeHighRiskTool` fixture `agent-tool-contract.md` uses), followed by `End`.
2. Publish and start an execution.
3. Poll `GET /api/v1/workflow-executions/{id}` — expect `status: "WaitingForApproval"`; `GET .../approvals/{approvalId}` shows `decision: null`, `intendedActionDescription`/`parametersJson` populated (FR-033).
4. `POST .../approvals/{approvalId}/actions/approve` → expect `202`; poll until `Completed`; confirm the approval row now shows `decision: "Approve"`, `decidedByUserId` set (FR-034 audit trail).
5. Repeat from step 2, this time `.../actions/reject` → expect execution to follow the node's configured rejection path (terminate or branch) rather than silently continuing (Acceptance Scenario 5.3).
6. As an Administrator, `POST /api/v1/workflow-policies` covering the same tool/node type; repeat step 2 → expect the execution to reach `Completed` *without* ever entering `WaitingForApproval`, and the approval row to show `wasPolicyBased: true` (FR-035/FR-036).
7. Configure a `HumanApproval` node with a short `timeoutSeconds`; trigger it and let the timeout elapse without deciding → expect the node to apply its configured timeout failure policy and the timeout to be recorded (FR-037).
8. **Pass criterion**: at no point does any workflow-level approval-policy setting cause a `High`/`Critical` node to skip approval outright — only an explicit, recorded policy match ever does (research.md Decision 5, FR-036).

## Scenario 6 — Real-time monitoring, pause, resume, cancel (User Story 6, P6)

1. Open a SignalR connection to `/hubs/workflow-execution` per [contracts/workflow-execution-events.md](./contracts/workflow-execution-events.md).
2. Start a multi-node execution (reuse Scenario 4's workflow).
3. Observe `NodeStarted`/`NodeCompleted` events arriving live; cross-check each against `GET .../nodes` to confirm agreement.
4. Mid-execution, `POST .../actions/pause` → expect the current node to finish, no further node to start, `status: "Paused"`.
5. `POST .../actions/resume` → expect execution to continue from exactly where it left off (no re-run of already-`Completed` nodes).
6. Start a new execution and `POST .../actions/cancel` mid-run → expect a `WorkflowExecutionCancelled` push within 5 seconds (SC-007) and `status: "Cancelled"`.
7. **Pass criterion**: no event payload contains anything beyond what the events contract declares — no raw AI output beyond the node's own recorded `OutputJson`, no chain-of-thought (FR-053).

## Scenario 7 — Error handling, retry, and timeout recovery (User Story 7, P7)

1. Configure a node with a retry policy (`maxAttempts: 3`) pointing at a tool/target that fails deterministically for its first two attempts (test fixture) — run it; expect it to succeed on the third attempt, and `GET .../nodes` to show `retryCount: 2` on the eventually-`Completed` row (FR-040).
2. Configure the same node as explicitly non-idempotent with no `IdempotencyKeyExpression` — force a failure; expect at most one retry regardless of the configured `maxAttempts` (Acceptance Scenario 7.2).
3. Configure a node with a short `timeoutSeconds` against a deliberately slow test target — run it; expect the node to fail with `WorkflowErrorCategory.Timeout` and the workflow's configured failure policy to apply (FR-041).
4. Set the workflow-level failure strategy to `Continue`; fail a non-critical node whose output nothing downstream depends on — expect the workflow to reach `Completed` with the failure recorded against the execution, not blocking unrelated later nodes (Acceptance Scenario 7.4).
5. Configure a `CompensatingNodeId` on an earlier node that created an external record (test fixture); set the failure strategy to `Compensate`; fail a later node — expect the compensating node to execute before the execution finalizes `Failed` (FR-042).
6. Configure a node with an `IdempotencyKeyExpression`; force a retry after a partial success — expect the external side effect to occur exactly once, verified against the test fixture's own call-count assertion (FR-043).
7. **Pass criterion**: every failure path above results in a `WorkflowError` row and a terminal or continuing state per the configured policy — never an execution stuck in an ambiguous, non-terminal, non-progressing state.

## Scenario 8 — Execution history, audit, usage, and cost review (User Story 8, P8)

1. `GET /api/v1/workflow-executions?workflowId={id}` → expect every execution from Scenarios 1–7 (for that workflow) listed, cursor-paginated.
2. `GET /api/v1/workflow-executions/{id}` for a completed one with at least one AI Prompt/AI Agent node → expect `usage`/`cost` reflecting the actual model/provider/token counts used (FR-054).
3. As a second test user, `GET /api/v1/workflow-executions/{id}` for the first user's execution → expect `404` (FR-059/SC-008), and confirm a `WorkflowAuditLog` row with `action: "PermissionDenied"` was written (Application-layer test, not REST-visible).
4. `GET /api/v1/workflows/{id}/statistics` → expect active/queued/failed/completed counts, average duration, failure rate, and aggregate usage/cost across the workflow's executions (FR-050).
5. **Pass criterion**: every field spec.md's "Execution History" section lists (workflow, version, start/end time, duration, status, inputs, outputs, nodes, node results, errors, approvals, usage, cost) is present in one `GET .../{id}` response — no follow-up call needed to assemble the full picture.

## Scenario 9 — Event-driven workflow trigger (User Story 9, P9)

1. Create and publish a workflow with `workflowType: "EventDriven"`, `eventTriggerConfigurationJson` scoped to "document uploaded" for a specific Knowledge Base, with the uploaded document's id/title bound to the workflow's declared inputs.
2. Upload a document into that Knowledge Base through the existing upload flow (spec 014/015) — expect a `WorkflowExecution` to appear automatically (`GET /api/v1/workflow-executions?workflowId={id}`) within roughly a minute (SC-012), with `triggerType: "EventDriven"` and `triggeringEventReferenceJson` populated, and no manual `POST /workflow-executions` call was made.
3. Revoke the triggering user's access to that Knowledge Base, then upload another matching document — expect no new execution to start, and the omission to be recorded (Acceptance Scenario 9.2).
4. Archive the workflow, then upload another matching document — expect no new execution to start (Acceptance Scenario 9.3).
5. Upload a burst of several matching documents in quick succession — expect the number of concurrently `Running`/`Queued` executions this triggers to respect both the workflow's own concurrency/rate configuration and the user's overall concurrency cap (FR-069/FR-070) — never unbounded (Acceptance Scenario 9.4).
6. **Pass criterion**: disabling the workflow's event trigger (or the workflow itself) is the only way to stop new automatic executions — no lingering execution starts after disablement, verified by uploading one more matching document post-disable and confirming no new `WorkflowExecution` appears.

## Cleanup

Archive or delete every workflow created above (`POST .../actions/archive` or `DELETE`); executions remain queryable per data-model.md's Delete Behavior table even after their owning workflow is archived/soft-deleted, so no execution-history data is lost by cleaning up the definitions.
