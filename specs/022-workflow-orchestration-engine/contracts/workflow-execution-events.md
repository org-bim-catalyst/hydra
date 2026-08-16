# Contract: Workflow Execution Real-Time Events

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decision 8)

`WorkflowExecutionHub` (SignalR, `src/AskLucy.Infrastructure/Workflows/WorkflowExecutionHub.cs`) — structurally identical to `AgentExecutionHub`: one group per authenticated user (`user:{userId}`), joined on connection via the existing JWT-authenticated hub pipeline (no new auth mechanism). `WorkflowExecutionNotifier` (`IWorkflowExecutionNotifier`, `Application/Abstractions`) is the Application-facing interface `WorkflowExecutionOrchestrator` calls; `Infrastructure`'s hub-backed implementation is the only thing that knows SignalR exists (constitution §3 — Infrastructure implements an Application-owned interface, never the reverse).

## `IWorkflowExecutionNotifier`

```csharp
public interface IWorkflowExecutionNotifier
{
    Task NotifyWorkflowStartedAsync(string userId, Guid executionId, Guid workflowId, int versionNumber, DateTime atUtc, CancellationToken ct);
    Task NotifyNodeStartedAsync(string userId, Guid executionId, Guid nodeExecutionId, string nodeKey, DateTime atUtc, CancellationToken ct);
    Task NotifyNodeCompletedAsync(string userId, Guid executionId, Guid nodeExecutionId, string nodeKey, DateTime atUtc, CancellationToken ct);
    Task NotifyNodeFailedAsync(string userId, Guid executionId, Guid nodeExecutionId, string nodeKey, string reason, DateTime atUtc, CancellationToken ct);
    Task NotifyNodeRetryingAsync(string userId, Guid executionId, Guid nodeExecutionId, string nodeKey, int attempt, DateTime atUtc, CancellationToken ct);
    Task NotifyApprovalRequestedAsync(string userId, Guid executionId, Guid approvalId, string intendedAction, DateTime atUtc, CancellationToken ct);
    Task NotifyApprovalGrantedAsync(string userId, Guid executionId, Guid approvalId, string? decidedByUserId, bool wasPolicyBased, DateTime atUtc, CancellationToken ct);
    Task NotifyApprovalRejectedAsync(string userId, Guid executionId, Guid approvalId, string? reason, DateTime atUtc, CancellationToken ct);
    Task NotifyWorkflowPausedAsync(string userId, Guid executionId, DateTime atUtc, CancellationToken ct);
    Task NotifyWorkflowResumedAsync(string userId, Guid executionId, DateTime atUtc, CancellationToken ct);
    Task NotifyWorkflowCompletedAsync(string userId, Guid executionId, DateTime atUtc, CancellationToken ct);
    Task NotifyWorkflowFailedAsync(string userId, Guid executionId, string reason, DateTime atUtc, CancellationToken ct);
    Task NotifyWorkflowCancelledAsync(string userId, Guid executionId, DateTime atUtc, CancellationToken ct);
    Task NotifyUsageUpdatedAsync(string userId, Guid executionId, int? inputTokens, int? outputTokens, decimal? estimatedCost, CancellationToken ct);
}
```

Method names use the `Workflow*`/`Node`/`Approval*` prefixes matching FR-049's literal event-type naming (`WorkflowStarted`, `WorkflowCompleted`, `WorkflowFailed`, `WorkflowCancelled`) — not an `Execution*` prefix, to avoid the two reading as different events.

Every method corresponds 1:1 to a `WorkflowExecutionEventType` value (FR-049) and is always preceded by persisting the matching `WorkflowExecutionEvent` row (`WorkflowExecutionOrchestrator.RecordAndNotifyAsync`, mirroring `AgentExecutionOrchestrator`'s identical helper) — **the persisted row is authoritative; the push is a best-effort convenience**. `GET /workflow-executions/{id}/events` (workflows-api.md) is the reconciliation path if a client misses a push (reconnect, tab backgrounded, etc.).

## Frontend contract

`ClientApp/src/features/workflows/hooks/useWorkflowExecutionHub.ts` subscribes to the same fourteen event names as method names above (camelCase), matching `useAgentExecutionHub.ts`'s existing shape exactly — a `WorkflowExecutionMonitor` component (User Story 6) renders node-by-node state transitions and a running usage/cost readout from this stream, falling back to polling `GET /workflow-executions/{id}` if the hub connection drops (constitution §2.VIII — no silent failure: a dropped hub connection surfaces a visible "reconnecting" indicator, never a frozen-looking UI with no explanation).

## Payload shape (safe-metadata-only, FR-053)

```json
{
  "eventType": "NodeCompleted",
  "executionId": "0192b3c1-...",
  "workflowId": "0192b3c1-...",
  "workflowVersionNumber": 3,
  "nodeExecutionId": "0192b3c2-...",
  "nodeKey": "extract_document",
  "status": "Completed",
  "atUtc": "2026-08-11T14:02:03Z"
}
```

Never includes: raw AI model output beyond what the owning `WorkflowExecutionNode.OutputJson` already stores for the *user's own* execution history read, any other user's data, or model chain-of-thought/reasoning (FR-053 — same restriction `AgentExecutionEvent` already enforces for agents).
