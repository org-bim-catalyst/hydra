using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Prompts.Commands.ExecutePrompt;

/// <summary>
/// One piece of a streamed prompt execution (spec.md FR-041, contracts/prompt-execution-api.md).
/// Mirrors <c>ChatStreamChunk</c>'s shape — most chunks carry only <see cref="ContentDelta"/>; a
/// trailing chunk carries <see cref="Usage"/> plus <see cref="ResolvedVariableValuesJson"/> (the
/// values actually used, for the caller's persistence step — see
/// <c>RecordPromptExecutionCommand</c>, research.md Decision 2's "controller persists after the
/// stream" pattern). <see cref="RetrievalOutcome"/>/<see cref="MemoryOutcome"/> (User Story 6, FR-081/
/// FR-082) ride the same trailing chunk, for the caller to persist onto
/// <c>PromptExecutionResult.RagCitationsJson</c>/<c>MemoryReferencesJson</c>.
/// </summary>
public sealed record PromptStreamChunk(
    string? ContentDelta,
    ChatUsage? Usage,
    Guid? PromptVersionId = null,
    string? ResolvedVariableValuesJson = null,
    RagRetrievalOutcome? RetrievalOutcome = null,
    MemoryRetrievalOutcome? MemoryOutcome = null);
