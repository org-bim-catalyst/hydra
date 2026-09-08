using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// One conversation turn: everything that happens between a user's message and the end of Lucy's
/// response (specs/045-conversational-agent-runtime).
/// <para>
/// Deliberately <b>not</b> the existing <c>AgentExecutionOrchestrator</c>, which is structurally
/// wrong here in three ways that cannot be configured away (research.md D1): it runs inside a
/// Hangfire job with no HTTP context, it does not stream, and it suspends across processes to
/// wait for approval. A chat turn is a live request that must emit messages <i>during</i> its
/// work and can never suspend. What the two share — the budget guard, duplicate-call detection,
/// policy evaluation and the <c>AgentExecution</c> record model — is reused rather than
/// reimplemented.
/// </para>
/// </summary>
public interface IConversationTurnOrchestrator
{
    /// <summary>
    /// Runs the turn, yielding chunks as they become available. Never throws for a failure of the
    /// turn's own work: every such path yields user-visible text and continues (constitution
    /// §2.VIII). Caller cancellation propagates, so the iterator terminates cleanly.
    /// </summary>
    IAsyncEnumerable<ChatStreamChunk> RunAsync(ConversationTurnRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Everything the orchestrator needs, with resolution already done by the caller.
/// <para>
/// Takes the resolved <see cref="IAIProvider"/> and model key rather than their ids: deciding
/// <i>which</i> provider serves a request is the command handler's job (and the validator has
/// already confirmed the pair is valid and enabled), while running the turn is this type's. Two
/// responsibilities, one seam between them.
/// </para>
/// </summary>
public sealed record ConversationTurnRequest(
    Guid ChatId,
    IReadOnlyList<ChatMessageDto> Messages,
    IAIProvider Provider,
    string ModelKey,
    GenerationParametersDto? GenerationParameters);
