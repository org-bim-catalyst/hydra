using System.Text.Json;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>Per-call context passed to every <see cref="IWorkflowNodeExecutor"/> (contracts/workflow-node-contract.md).</summary>
public sealed record WorkflowNodeExecutionContext(Guid WorkflowExecutionId, Guid WorkflowExecutionNodeId, string UserId, Guid WorkflowId, Guid WorkflowVersionId, WorkflowNode Node);

/// <summary>Result of one node execution — exactly one of <see cref="Output"/>/<see cref="FailureReason"/> is set.</summary>
public sealed record WorkflowNodeExecutionResult(bool Succeeded, JsonDocument? Output, string? FailureReason)
{
    public static WorkflowNodeExecutionResult Success(JsonDocument output) => new(true, output, null);

    public static WorkflowNodeExecutionResult Failure(string failureReason) => new(false, null, failureReason);
}

/// <summary>
/// The node-executor abstraction (FR-017, contracts/workflow-node-contract.md, research.md
/// Decision 1). One DI-registered class per <see cref="WorkflowNodeType"/> — input validation,
/// permission checks, the approval gate, output validation, retry, timeout, idempotency, and
/// compensation are all enforced by <c>WorkflowExecutionOrchestrator</c> around every call, never
/// by the executor itself (constitution §2.II OCP: a new node type is a new class, never an edit
/// to the orchestrator).
/// </summary>
public interface IWorkflowNodeExecutor
{
    WorkflowNodeType NodeType { get; }

    Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default);
}
