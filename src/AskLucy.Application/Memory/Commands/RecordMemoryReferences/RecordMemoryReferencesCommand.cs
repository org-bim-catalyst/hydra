using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Memory.Commands.RecordMemoryReferences;

/// <summary>
/// spec.md FR-014 — persists the "why does Lucy know this" trace for one assistant message,
/// mirroring how RAG citations are attached: the message id only exists once the controller
/// persists the assistant's response after streaming completes, so this runs as its own
/// follow-up command rather than inside <c>SendChatMessageCommandHandler</c> itself (which only
/// has the not-yet-persisted memory selection to offer via <c>ChatStreamChunk</c>).
/// </summary>
public sealed record RecordMemoryReferencesCommand(Guid MessageId, IReadOnlyList<MemoryReferenceContext> UsedMemories) : IRequest;
