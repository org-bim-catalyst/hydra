using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.RecordPromptExecution;

/// <summary>
/// Persists the outcome of one prompt execution — called by the controller once the SSE stream
/// completes (success) or fails (provider error/timeout), mirroring how <c>AppendMessageCommand</c>
/// persists a chat turn after <c>SendChatMessageCommand</c>'s pure streaming completes. Only
/// <see cref="Outcome"/> = <see cref="PromptExecutionOutcome.Success"/> increments
/// <c>PromptUsageStatistics</c> (spec.md Clarifications 2026-08-10).
/// </summary>
public sealed record RecordPromptExecutionCommand(
    Guid PromptId,
    Guid PromptVersionId,
    PromptExecutionOrigin Origin,
    Guid ModelId,
    string ProviderKey,
    string ModelKey,
    decimal? Temperature,
    int? MaxOutputTokens,
    bool StructuredOutputRequested,
    string ResolvedVariableValuesJson,
    bool RequestedRagContext,
    bool RequestedMemoryContext,
    PromptExecutionOutcome Outcome,
    string? ErrorDetail,
    int? LatencyMs,
    string? OutputText,
    int? InputTokenCount,
    int? OutputTokenCount,
    string? RagCitationsJson,
    string? MemoryReferencesJson,
    Guid? ResultMessageId = null) : IRequest<Guid>;
