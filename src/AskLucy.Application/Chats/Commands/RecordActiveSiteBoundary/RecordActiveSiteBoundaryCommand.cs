using AskLucy.Application.Ai.Commands.SendChatMessage;
using MediatR;

namespace AskLucy.Application.Chats.Commands.RecordActiveSiteBoundary;

/// <summary>
/// specs/042-site-boundary-resolution — persists the resolved site boundary onto the
/// <c>UserChat</c> row so a repeated reference to the same site in a later turn reuses it
/// instead of forcing a fresh resolution (FR-009). Mirrors <c>RecordActiveLocationCommand</c>
/// exactly — dispatched from <c>AiController</c> post-stream.
/// </summary>
public sealed record RecordActiveSiteBoundaryCommand(
    Guid UserChatId,
    ConfirmedSiteBoundaryData ConfirmedBoundary) : IRequest;
