using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

/// <summary>Aggregated token/tool-call usage for one execution (FR-054, data-model.md) — accumulated across every AI Prompt/AI Agent node invocation, mirrors <c>AgentExecutionUsage</c>'s shape.</summary>
public sealed class WorkflowExecutionUsage : BaseEntity
{
    public Guid WorkflowExecutionId { get; private set; }

    public int? InputTokenCount { get; private set; }

    public int? OutputTokenCount { get; private set; }

    public int? ReasoningTokenCount { get; private set; }

    public int ToolCallCount { get; private set; }

    private WorkflowExecutionUsage()
    {
        // Required by EF Core materialization.
    }

    public static WorkflowExecutionUsage CreateEmpty(Guid workflowExecutionId) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowExecutionId = workflowExecutionId,
        CreatedAtUtc = DateTime.UtcNow,
    };

    public void Accumulate(int? inputTokens, int? outputTokens, int? reasoningTokens, int additionalToolCalls)
    {
        InputTokenCount = (InputTokenCount ?? 0) + (inputTokens ?? 0);
        OutputTokenCount = (OutputTokenCount ?? 0) + (outputTokens ?? 0);
        ReasoningTokenCount = (ReasoningTokenCount ?? 0) + (reasoningTokens ?? 0);
        ToolCallCount += additionalToolCalls;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
