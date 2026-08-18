# Contract: Agent Execution Real-Time Events

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decision 9)

A new SignalR hub, `AgentExecutionHub` (`src/AskLucy.Infrastructure/Agents/AgentExecutionHub.cs`), mirroring `MemoryHub`/`DocumentProcessingHub`/`RetrievalIndexingHub` exactly.

## Connection & groups

- Hub route: `/hubs/agent-execution` (matching `/hubs/memory`, `/hubs/document-processing` naming).
- On connect, the hub adds the caller to a group keyed by their own user id (`ClaimTypes.NameIdentifier`), same as `MemoryHub` — **not** a per-execution group, so a client with multiple executions running gets every one's events on a single connection and filters client-side by `executionId`, matching how `useDocumentProcessingHub.ts` already consumes multi-item progress on one connection.
- Unauthenticated connections are rejected (`[Authorize]` on the hub class).

## Server → client events

One SignalR method per `AgentExecutionEventType` (data-model.md), invoked via `IAgentExecutionNotifier` (Application interface) → `AgentExecutionNotifier` (Infrastructure, wraps `IHubContext<AgentExecutionHub>`):

| Hub method | Payload (matches `AgentExecutionEvent` fields, FR-034) |
|---|---|
| `ExecutionStarted` | `{ executionId, agentId, agentVersionNumber, objective, occurredAtUtc }` |
| `PlanCreated` | `{ executionId, stepCount, occurredAtUtc }` — never the raw plan reasoning, only the count and step descriptions already persisted to `AgentExecutionStep` |
| `StepStarted` / `StepCompleted` / `StepFailed` | `{ executionId, stepId, stepIndex, description, status, occurredAtUtc, errorMessage? }` |
| `ToolCallStarted` / `ToolCallCompleted` | `{ executionId, stepId, toolName, riskLevel, status, occurredAtUtc }` — never raw tool input/output content, only what `AgentToolCall`'s summary fields already expose |
| `ApprovalRequested` | `{ executionId, approvalId, intendedActionDescription, riskLevel, occurredAtUtc }` |
| `ApprovalGranted` / `ApprovalRejected` | `{ executionId, approvalId, decidedByUserId?, wasPolicyBased, occurredAtUtc }` |
| `ExecutionCompleted` / `ExecutionFailed` / `ExecutionCancelled` | `{ executionId, status, terminationReason?, occurredAtUtc }` |
| `UsageUpdated` | `{ executionId, inputTokenCount, outputTokenCount, estimatedCost }` — pushed opportunistically (not one event per token) so SC-002's "current step and tool activity... within 2 seconds" and the spec's cost-visibility requirement (FR-036) are both satisfied without a token-by-token firehose |

Every payload's `occurredAtUtc` matches the corresponding `AgentExecutionEvent.OccurredAtUtc` row, so a client that missed a live push (reconnect gap) can reconcile via `GET /agent-executions/{id}/events?since=...` ([agents-api.md](./agents-api.md)) — REST is the reconciliation fallback, the hub is the primary path, same convention as `DocumentProcessingHub`.

## What is never sent

Per FR-035/constitution §9: no hub payload ever contains the model's raw reasoning/chain-of-thought, the full prompt sent to the provider, or unredacted tool input/output beyond what `AgentToolCall`'s already-summarized fields expose. `PlanCreated` sends a step count and the already-persisted step descriptions, never the model's planning "thoughts."

## Client hook

Frontend consumes this via `useAgentExecutionHub.ts` (`src/AskLucy.Web/ClientApp/src/features/agents/hooks/`), mirroring `useDocumentProcessingHub.ts`/`useMemoryNotificationsHub.ts`'s existing shape (connect once per session, dispatch into TanStack Query cache updates for the relevant `agent-executions` query keys rather than local component state, so REST-fetched and live-pushed data never diverge).
