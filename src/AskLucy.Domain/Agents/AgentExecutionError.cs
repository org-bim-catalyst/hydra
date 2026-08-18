using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

public enum AgentExecutionErrorCategory
{
    ToolFailure,
    ProviderFailure,
    InvalidToolOutput,
    InvalidModelResponse,
    ContextLimitExceeded,
    BudgetExceeded,
    UserCancellation,
    ExecutionTimeout,
}

/// <summary>
/// A structured failure record (spec.md's Failure Handling requirements, data-model.md) — never
/// a raw provider stack trace, always an actionable, user-safe message (constitution &#167;2.VIII
/// No Silent Failures).
/// </summary>
public sealed class AgentExecutionError : BaseEntity
{
    public Guid AgentExecutionId { get; private set; }

    public Guid? AgentExecutionStepId { get; private set; }

    public AgentExecutionErrorCategory Category { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public int RetryCount { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    private AgentExecutionError()
    {
        // Required by EF Core materialization.
    }

    internal static AgentExecutionError Create(
        Guid agentExecutionId, Guid? agentExecutionStepId, AgentExecutionErrorCategory category,
        string message, int retryCount) => new()
        {
            Id = Guid.CreateVersion7(),
            AgentExecutionId = agentExecutionId,
            AgentExecutionStepId = agentExecutionStepId,
            Category = category,
            Message = message,
            RetryCount = retryCount,
            OccurredAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
        };
}
