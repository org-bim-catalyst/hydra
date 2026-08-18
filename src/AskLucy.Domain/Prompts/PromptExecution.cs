using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

public enum PromptExecutionOrigin
{
    TestingWorkspace,
    ConversationInsertion,
}

public enum PromptExecutionOutcome
{
    Success,
    Failed,
}

/// <summary>
/// One run of a <see cref="Prompt"/>/<see cref="PromptVersion"/> against a provider/model (spec.md
/// FR-040-FR-046, FR-051, FR-080, data-model.md). Immutable after creation — outcome/error/latency/
/// result-message fields are set once, at completion, never mutated afterward.
/// </summary>
public sealed class PromptExecution : BaseEntity
{
    public Guid PromptId { get; private set; }

    public Guid PromptVersionId { get; private set; }

    public PromptExecutionOrigin Origin { get; private set; }

    public string ProviderKey { get; private set; } = string.Empty;

    public string ModelKey { get; private set; } = string.Empty;

    public decimal? Temperature { get; private set; }

    public int? MaxOutputTokens { get; private set; }

    public bool StructuredOutputRequested { get; private set; }

    public string ResolvedVariableValuesJson { get; private set; } = string.Empty;

    public bool RequestedRagContext { get; private set; }

    public bool RequestedMemoryContext { get; private set; }

    public PromptExecutionOutcome Outcome { get; private set; }

    public string? ErrorDetail { get; private set; }

    public int? LatencyMs { get; private set; }

    public Guid? ResultMessageId { get; private set; }

    private PromptExecution()
    {
        // Required by EF Core materialization.
    }

    public static PromptExecution CreatePending(
        Guid promptId, Guid promptVersionId, PromptExecutionOrigin origin, string providerKey, string modelKey,
        decimal? temperature, int? maxOutputTokens, bool structuredOutputRequested,
        string resolvedVariableValuesJson, bool requestedRagContext, bool requestedMemoryContext, string actor)
    {
        return new PromptExecution
        {
            Id = Guid.CreateVersion7(),
            PromptId = promptId,
            PromptVersionId = promptVersionId,
            Origin = origin,
            ProviderKey = providerKey,
            ModelKey = modelKey,
            Temperature = temperature,
            MaxOutputTokens = maxOutputTokens,
            StructuredOutputRequested = structuredOutputRequested,
            ResolvedVariableValuesJson = resolvedVariableValuesJson,
            RequestedRagContext = requestedRagContext,
            RequestedMemoryContext = requestedMemoryContext,
            Outcome = PromptExecutionOutcome.Failed,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void MarkSucceeded(int? latencyMs, Guid? resultMessageId)
    {
        Outcome = PromptExecutionOutcome.Success;
        LatencyMs = latencyMs;
        ResultMessageId = resultMessageId;
    }

    public void MarkFailed(string errorDetail, int? latencyMs)
    {
        Outcome = PromptExecutionOutcome.Failed;
        ErrorDetail = errorDetail;
        LatencyMs = latencyMs;
    }
}
