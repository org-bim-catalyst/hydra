using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

public enum AgentToolRiskLevel
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>A specific tool invocation within a step (spec.md FR-020/FR-021, data-model.md, contracts/agent-tool-contract.md).</summary>
public sealed class AgentToolCall : BaseEntity
{
    public Guid AgentExecutionStepId { get; private set; }

    public string ToolName { get; private set; } = string.Empty;

    public AgentToolRiskLevel RiskLevel { get; private set; }

    public string RequiredPermissionsJson { get; private set; } = "[]";

    public string ValidatedInputJson { get; private set; } = "{}";

    public string? ValidatedOutputJson { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public bool WasApprovalRequired { get; private set; }

    private AgentToolCall()
    {
        // Required by EF Core materialization.
    }

    public static AgentToolCall Create(
        Guid agentExecutionStepId, string toolName, AgentToolRiskLevel riskLevel,
        string requiredPermissionsJson, string validatedInputJson, bool wasApprovalRequired) => new()
        {
            Id = Guid.CreateVersion7(),
            AgentExecutionStepId = agentExecutionStepId,
            ToolName = toolName,
            RiskLevel = riskLevel,
            RequiredPermissionsJson = requiredPermissionsJson,
            ValidatedInputJson = validatedInputJson,
            WasApprovalRequired = wasApprovalRequired,
            StartedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
        };

    public void Complete(string validatedOutputJson)
    {
        ValidatedOutputJson = validatedOutputJson;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Fail(string failureReason)
    {
        FailureReason = failureReason;
        CompletedAtUtc = DateTime.UtcNow;
    }
}
