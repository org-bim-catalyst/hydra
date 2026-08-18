using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

/// <summary>
/// <see cref="SendChatMessageCommand"/>'s own stream element — wraps the shared
/// <see cref="StreamChunk"/> (content delta + optional usage, same as every other
/// <see cref="IAIProvider"/> consumer) plus an optional <see cref="RagRetrievalOutcome"/>
/// carried on the final chunk only, mirroring how <see cref="ChatUsage"/> already rides the
/// final chunk(s) rather than every one. Kept separate from <see cref="StreamChunk"/> itself
/// (rather than adding these fields there) so RAG stays a concern of this one command, not of
/// every <see cref="IAIProvider"/> implementation (OpenAI/Anthropic/Gemini/OpenRouter never
/// need to know about retrieval). <see cref="MemoryOutcome"/> (specs/018-ai-memory-system) rides
/// the final chunk the same way, for the same reason.
/// </summary>
public sealed record ChatStreamChunk(string? ContentDelta, ChatUsage? Usage, RagRetrievalOutcome? RetrievalOutcome = null, MemoryRetrievalOutcome? MemoryOutcome = null);
