using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Authorization;

/// <summary>Centralizes the "does this execution belong to this caller" check (FR-059), mirrors <c>AgentExecutionOwnershipGuard</c>. Denial looks like not-found — a request naming an execution the caller doesn't own returns 404, never 403, avoiding existence disclosure.</summary>
public static class WorkflowExecutionOwnershipGuard
{
    public static WorkflowExecution EnsureOwnedBy(WorkflowExecution? execution, string userId)
    {
        if (execution is null || execution.RunByUserId != userId)
        {
            throw new KeyNotFoundException("Workflow execution not found.");
        }

        return execution;
    }
}
