using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

public sealed record PromptExecutionSummaryDto(
    Guid Id,
    int VersionNumber,
    PromptExecutionOrigin Origin,
    string ProviderKey,
    string ModelKey,
    PromptExecutionOutcome Outcome,
    int? LatencyMs,
    decimal? EstimatedCostUsd,
    DateTime CreatedAtUtc)
{
    public static PromptExecutionSummaryDto FromEntity(PromptExecution execution, int versionNumber, decimal? estimatedCostUsd) => new(
        execution.Id, versionNumber, execution.Origin, execution.ProviderKey, execution.ModelKey,
        execution.Outcome, execution.LatencyMs, estimatedCostUsd, execution.CreatedAtUtc);
}

public sealed record PromptExecutionDetailDto(
    Guid Id,
    Guid PromptId,
    int VersionNumber,
    PromptExecutionOrigin Origin,
    string ProviderKey,
    string ModelKey,
    decimal? Temperature,
    int? MaxOutputTokens,
    string ResolvedVariableValuesJson,
    PromptExecutionOutcome Outcome,
    string? ErrorDetail,
    int? LatencyMs,
    string? OutputText,
    int? InputTokenCount,
    int? OutputTokenCount,
    decimal? EstimatedCostUsd,
    string? RagCitationsJson,
    string? MemoryReferencesJson,
    PromptRatingValue? Rating,
    DateTime CreatedAtUtc)
{
    public static PromptExecutionDetailDto FromEntities(PromptExecution execution, int versionNumber, PromptExecutionResult? result, PromptRating? rating) => new(
        execution.Id,
        execution.PromptId,
        versionNumber,
        execution.Origin,
        execution.ProviderKey,
        execution.ModelKey,
        execution.Temperature,
        execution.MaxOutputTokens,
        execution.ResolvedVariableValuesJson,
        execution.Outcome,
        execution.ErrorDetail,
        execution.LatencyMs,
        result?.OutputText,
        result?.InputTokenCount,
        result?.OutputTokenCount,
        result?.EstimatedCostUsd,
        result?.RagCitationsJson,
        result?.MemoryReferencesJson,
        rating?.RatingValue,
        execution.CreatedAtUtc);
}
