using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

/// <summary>
/// A directed link between two <see cref="WorkflowNode"/>s, or a node and a labeled branch of a
/// Condition/Parallel/Merge node (FR-018, data-model.md). A bounded loop (FR-032, research.md
/// Decision 20) is an ordinary connection whose <see cref="BranchLabel"/> is the reserved value
/// <c>"loop-back"</c>, pointing from the loop body's last node back to its first node; the loop's
/// bounds live on that first node's <see cref="WorkflowNode.ConfigurationJson"/>, not on this
/// connection. Immutable once created — belongs to exactly one <see cref="WorkflowVersion"/>.
/// </summary>
public sealed class WorkflowConnection : BaseEntity
{
    /// <summary>Reserved <see cref="BranchLabel"/> value marking a bounded-loop back-edge (research.md Decision 20) — excluded from the unsupported-cycle check at publish time.</summary>
    public const string LoopBackBranchLabel = "loop-back";

    public Guid WorkflowVersionId { get; private set; }

    public Guid SourceNodeId { get; private set; }

    public Guid TargetNodeId { get; private set; }

    /// <summary>e.g. <c>"true"</c>/<c>"false"</c> for a Condition node's two edges, a Parallel node's branch name, or <see cref="LoopBackBranchLabel"/>; null for an unconditional edge.</summary>
    public string? BranchLabel { get; private set; }

    public string? TypeContract { get; private set; }

    private WorkflowConnection()
    {
        // Required by EF Core materialization.
    }

    internal static WorkflowConnection Create(Guid workflowVersionId, Guid sourceNodeId, Guid targetNodeId, string? branchLabel, string? typeContract)
    {
        if (sourceNodeId == targetNodeId && branchLabel != LoopBackBranchLabel)
        {
            throw new DomainRuleViolationException("A node cannot connect to itself except as a bounded-loop back-edge.");
        }

        return new WorkflowConnection
        {
            Id = Guid.CreateVersion7(),
            WorkflowVersionId = workflowVersionId,
            SourceNodeId = sourceNodeId,
            TargetNodeId = targetNodeId,
            BranchLabel = branchLabel,
            TypeContract = typeContract,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }
}
