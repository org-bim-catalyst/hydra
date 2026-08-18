using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

public enum AgentApprovalDecision
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>A pause-for-approval request (spec.md FR-025-FR-028, data-model.md) — mirrors <c>MemoryApproval</c>'s Pending/Approved/Rejected shape (research.md Decision 5).</summary>
public sealed class AgentApproval : BaseEntity
{
    public Guid AgentExecutionId { get; private set; }

    public Guid? AgentToolCallId { get; private set; }

    public string IntendedActionDescription { get; private set; } = string.Empty;

    public string IntendedParametersJson { get; private set; } = "{}";

    public AgentApprovalDecision Decision { get; private set; } = AgentApprovalDecision.Pending;

    public string? DecidedByUserId { get; private set; }

    public bool WasPolicyBased { get; private set; }

    public Guid? MatchedAgentPolicyId { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    private AgentApproval()
    {
        // Required by EF Core materialization.
    }

    internal static AgentApproval CreatePending(Guid agentExecutionId, Guid? agentToolCallId, string intendedActionDescription, string intendedParametersJson) => new()
    {
        Id = Guid.CreateVersion7(),
        AgentExecutionId = agentExecutionId,
        AgentToolCallId = agentToolCallId,
        IntendedActionDescription = intendedActionDescription,
        IntendedParametersJson = intendedParametersJson,
        Decision = AgentApprovalDecision.Pending,
        CreatedAtUtc = DateTime.UtcNow,
    };

    public void Approve(string userId)
    {
        Decision = AgentApprovalDecision.Approved;
        DecidedByUserId = userId;
        WasPolicyBased = false;
        DecidedAtUtc = DateTime.UtcNow;
    }

    public void Reject(string userId)
    {
        Decision = AgentApprovalDecision.Rejected;
        DecidedByUserId = userId;
        WasPolicyBased = false;
        DecidedAtUtc = DateTime.UtcNow;
    }

    public void ApproveByPolicy(Guid policyId)
    {
        Decision = AgentApprovalDecision.Approved;
        WasPolicyBased = true;
        MatchedAgentPolicyId = policyId;
        DecidedAtUtc = DateTime.UtcNow;
    }
}
