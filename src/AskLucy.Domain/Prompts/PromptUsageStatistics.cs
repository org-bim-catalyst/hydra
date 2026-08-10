using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

/// <summary>
/// Aggregated, successful-execution-only usage data for a <see cref="Prompt"/> (spec.md FR-051,
/// Clarifications 2026-08-10 — only successful executions count toward usage/recency).
/// </summary>
public sealed class PromptUsageStatistics : BaseEntity
{
    public Guid PromptId { get; private set; }

    public int SuccessfulExecutionCount { get; private set; }

    public DateTime? LastSuccessfulUseAtUtc { get; private set; }

    private PromptUsageStatistics()
    {
        // Required by EF Core materialization.
    }

    public static PromptUsageStatistics CreateEmpty(Guid promptId, string actor)
    {
        return new PromptUsageStatistics
        {
            Id = Guid.CreateVersion7(),
            PromptId = promptId,
            SuccessfulExecutionCount = 0,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void RecordSuccessfulUse()
    {
        SuccessfulExecutionCount++;
        LastSuccessfulUseAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
