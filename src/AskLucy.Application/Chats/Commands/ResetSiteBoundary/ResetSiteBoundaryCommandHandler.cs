using System.Globalization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Chats.Queries.GetChatById;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Chats.Commands.ResetSiteBoundary;

/// <summary>
/// specs/079 - the one place a hand-edited outline is given up. Checks the caller owns the chat (a denial
/// is audited), that there is a hand edit to reset (otherwise 404, FR-028) and that the revision the user
/// was looking at is still in force; then soft-deletes the correction, unlinks the chat and appends the
/// chat line - all in one <c>SaveChanges</c>. Other chats linked to the same correction show the found
/// outline again too, since the row is gone (US5 AS2).
/// </summary>
public sealed class ResetSiteBoundaryCommandHandler(
    IUserChatRepository chatRepository,
    ISiteBoundaryCorrectionRepository correctionRepository,
    IMessageRepository messageRepository,
    EffectiveSiteBoundary effectiveSiteBoundary,
    ChatOwnershipAuditor ownershipAuditor,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<ResetSiteBoundaryCommand, ResetSiteBoundaryResult>
{
    public async Task<ResetSiteBoundaryResult> Handle(ResetSiteBoundaryCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = await ownershipAuditor.EnsureOwnedByAsync(
            await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId, "reset-site-boundary", cancellationToken);

        var found = chat.ActiveBoundary ?? throw new KeyNotFoundException("Chat has no outlined site.");
        var effective = await effectiveSiteBoundary.ResolveAsync(chat, cancellationToken) ?? found;
        if (!effective.IsHandEdited || effective.CorrectionId is not { } correctionId)
        {
            throw new KeyNotFoundException("This outline hasn't been edited.");
        }

        if (!Guid.TryParse(request.ExpectedRevision, out var expected) || expected != effective.Revision)
        {
            throw new ConcurrencyConflictException(
                "The outline changed since you started looking at it.", effective.Revision.ToString());
        }

        var correction = await correctionRepository.GetByIdAsync(correctionId, userId, cancellationToken);
        correction?.Delete(userId);
        chat.UnlinkSiteBoundaryCorrection(userId);

        var content = string.Create(
            CultureInfo.InvariantCulture,
            $"The outline of {found.SiteName} is back to the one I found — {found.AreaSquareMeters:N0} m².");
        var message = Message.Create(chat.Id, MessageRole.Assistant, MessageKind.Text, content, sourceText: null, userId);
        messageRepository.Add(message);
        chat.TouchLastActivity(userId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ResetSiteBoundaryResult(ChatActiveBoundaryDto.FromEntity(chat.ActiveBoundary!), AppendMessageCommandHandler.ToDto(message));
    }
}
