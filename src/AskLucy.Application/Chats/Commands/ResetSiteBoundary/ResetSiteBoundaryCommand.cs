using AskLucy.Application.Chats.Queries.GetChatById;
using MediatR;

namespace AskLucy.Application.Chats.Commands.ResetSiteBoundary;

/// <summary>
/// specs/079 (FR-025 to FR-028) - throws away the user's hand edits, so the chat shows the outline Lucy
/// found again. <paramref name="ExpectedRevision"/> is the revision the user was looking at; a different
/// one in force now is a conflict (409), so a stale screen can never reset a newer edit.
/// </summary>
public sealed record ResetSiteBoundaryCommand(Guid ChatId, string ExpectedRevision) : IRequest<ResetSiteBoundaryResult>;

/// <summary>The outline Lucy found, now in force, and the chat line recording the reset.</summary>
public sealed record ResetSiteBoundaryResult(ChatActiveBoundaryDto ActiveBoundary, MessageDto Message);
