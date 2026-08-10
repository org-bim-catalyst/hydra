using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.SendChatMessage;

namespace AskLucy.Application.Prompts.Commands.InsertPromptIntoConversation;

/// <summary>
/// Wraps <see cref="ChatStreamChunk"/> unchanged (content delta, usage, RAG/memory outcome — the
/// existing chat pipeline's own concerns, untouched by this command per research.md Decision 4) and
/// adds one trailing, prompt-specific chunk carrying <see cref="PromptVersionId"/>/
/// <see cref="ResolvedVariableValuesJson"/> once the send completes — mirrors
/// <c>PromptStreamChunk</c>'s identical "trailing metadata chunk" convention.
/// </summary>
public sealed record PromptConversationInsertionStreamChunk(
    string? ContentDelta,
    ChatUsage? Usage,
    RagRetrievalOutcome? RetrievalOutcome = null,
    MemoryRetrievalOutcome? MemoryOutcome = null,
    Guid? PromptVersionId = null,
    string? ResolvedVariableValuesJson = null);
