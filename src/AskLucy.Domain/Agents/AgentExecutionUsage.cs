using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

/// <summary>Aggregated token/tool-call usage for one execution (spec.md FR-036, data-model.md) — mirrors <c>ChatUsage</c>'s shape.</summary>
public sealed class AgentExecutionUsage : BaseEntity
{
    public Guid AgentExecutionId { get; private set; }

    public int? InputTokenCount { get; private set; }

    public int? OutputTokenCount { get; private set; }

    public int? ReasoningTokenCount { get; private set; }

    public int ToolCallCount { get; private set; }

    public int StepCount { get; private set; }

    private AgentExecutionUsage()
    {
        // Required by EF Core materialization.
    }

    public static AgentExecutionUsage CreateEmpty(Guid agentExecutionId) => new()
    {
        Id = Guid.CreateVersion7(),
        AgentExecutionId = agentExecutionId,
        CreatedAtUtc = DateTime.UtcNow,
    };

    public void Accumulate(int? inputTokens, int? outputTokens, int? reasoningTokens, int additionalToolCalls, int additionalSteps)
    {
        InputTokenCount = (InputTokenCount ?? 0) + (inputTokens ?? 0);
        OutputTokenCount = (OutputTokenCount ?? 0) + (outputTokens ?? 0);
        ReasoningTokenCount = (ReasoningTokenCount ?? 0) + (reasoningTokens ?? 0);
        ToolCallCount += additionalToolCalls;
        StepCount += additionalSteps;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
