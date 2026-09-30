using AskLucy.Application.Chats.Queries.GetChatById;
using AskLucy.Domain.SiteBoundaries;
using MediatR;

namespace AskLucy.Application.Chats.Commands.SaveSiteBoundaryEdit;

/// <summary>
/// specs/079 (FR-015 to FR-020) - saves the rings the user edited. <paramref name="ExpectedRevision"/> is
/// the revision the editor started from; a different one in force now is a conflict (409), so a
/// stale editor can never overwrite a newer outline.
/// </summary>
public sealed record SaveSiteBoundaryEditCommand(
    Guid ChatId,
    string ExpectedRevision,
    IReadOnlyList<IReadOnlyList<GeoPoint>> Rings) : IRequest<SaveSiteBoundaryEditResult>;

/// <summary>The outline now in force, and the chat line recording the edit (FR-017).</summary>
public sealed record SaveSiteBoundaryEditResult(ChatActiveBoundaryDto ActiveBoundary, MessageDto Message);
