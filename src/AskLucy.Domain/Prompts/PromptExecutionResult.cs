using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

/// <summary>
/// The AI output and usage data for a <see cref="PromptExecutionOrigin.TestingWorkspace"/>
/// execution (spec.md FR-042, data-model.md). Not created for
/// <see cref="PromptExecutionOrigin.ConversationInsertion"/> executions — that origin's output
/// already lives on the referenced <c>Chats.Message</c> (<see cref="PromptExecution.ResultMessageId"/>).
/// </summary>
public sealed class PromptExecutionResult : BaseEntity
{
    public Guid PromptExecutionId { get; private set; }

    public string OutputText { get; private set; } = string.Empty;

    public int? InputTokenCount { get; private set; }

    public int? OutputTokenCount { get; private set; }

    public decimal? EstimatedCostUsd { get; private set; }

    public string? RagCitationsJson { get; private set; }

    public string? MemoryReferencesJson { get; private set; }

    private PromptExecutionResult()
    {
        // Required by EF Core materialization.
    }

    public static PromptExecutionResult Create(
        Guid promptExecutionId, string outputText, int? inputTokenCount, int? outputTokenCount,
        decimal? estimatedCostUsd, string? ragCitationsJson, string? memoryReferencesJson, string actor)
    {
        return new PromptExecutionResult
        {
            Id = Guid.CreateVersion7(),
            PromptExecutionId = promptExecutionId,
            OutputText = outputText,
            InputTokenCount = inputTokenCount,
            OutputTokenCount = outputTokenCount,
            EstimatedCostUsd = estimatedCostUsd,
            RagCitationsJson = ragCitationsJson,
            MemoryReferencesJson = memoryReferencesJson,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
