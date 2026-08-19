using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Authorization;

/// <summary>Centralizes the "does this workflow belong to this caller" check (FR-059), mirrors <c>AgentOwnershipGuard</c>. Denial looks like not-found — a request naming a workflow the caller doesn't own returns 404, never 403, avoiding existence disclosure.</summary>
public static class WorkflowOwnershipGuard
{
    public static Workflow EnsureOwnedBy(Workflow? workflow, string userId)
    {
        if (workflow is null || workflow.OwnerId != userId)
        {
            throw new KeyNotFoundException("Workflow not found.");
        }

        return workflow;
    }
}
