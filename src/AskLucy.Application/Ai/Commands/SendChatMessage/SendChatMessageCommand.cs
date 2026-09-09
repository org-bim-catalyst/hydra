using AskLucy.Application.Abstractions;
using AskLucy.Domain.Conversations;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

/// <summary>
/// specs/045-conversational-agent-runtime FR-027, contracts/suggested-actions-api.md §1 — a
/// grounded offer row the user selected, already resolved against the offer it came from (staleness
/// and, for <see cref="SuggestedActionKind.Capability"/>/<see cref="SuggestedActionKind.FlowVariant"/>,
/// availability re-checked at dispatch time — FR-028/FR-029). <see cref="Key"/>/<see cref="ArgumentsJson"/>
/// are the grounded row's own values, never the client's raw request body: dispatch must run
/// exactly what was offered, not whatever a client happens to send alongside a familiar-looking id.
/// </summary>
public sealed record SelectedActionInput(
    Guid OfferedByMessageId,
    SuggestedActionKind Kind,
    string? Key,
    string? Text,
    string ArgumentsJson);

/// <summary>
/// Streams a chat completion, resolved to a specific provider/model
/// (specs/005-multi-provider-ai-engine contracts/chat.md). Yields <see cref="ChatStreamChunk"/>
/// rather than a plain string so the final usage/cost — and, since US1
/// (specs/016-rag-semantic-search), the RAG retrieval outcome — can ride the last chunk without
/// changing the SSE wire format the controller writes (only content deltas get written out as
/// plain text; RAG metadata is written as one distinguishable trailing JSON event).
/// <see cref="ChatId"/> (added for US1) is used to look up the conversation's attached knowledge
/// bases — retrieval only runs when at least one is attached (research.md Decision 8).
/// <see cref="SelectedAction"/> (specs/045 US3) is set only when this turn is dispatching a
/// selected offer row; the orchestrator then skips the decide step entirely and runs that
/// selection directly (FR-027).
/// </summary>
public sealed record SendChatMessageCommand(
    Guid ChatId,
    IReadOnlyList<ChatMessageDto> Messages,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters,
    SelectedActionInput? SelectedAction = null) : IStreamRequest<ChatStreamChunk>;
