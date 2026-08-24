using AskLucy.Application.Ai.Commands.SendChatMessage;
using MediatR;

namespace AskLucy.Application.Chats.Commands.RecordActiveLocation;

/// <summary>
/// specs/037-location-query-resolution — persists the agent-confirmed location onto the
/// <c>UserChat</c> row so that back-references in subsequent turns can resolve it without
/// issuing a new geocoding lookup (FR-014). Dispatched from <c>AiController</c> post-stream,
/// mirroring <c>RecordMemoryReferencesCommand</c>'s existing post-stream dispatch pattern.
/// </summary>
public sealed record RecordActiveLocationCommand(
    Guid UserChatId,
    ConfirmedLocationData ConfirmedLocation) : IRequest;
