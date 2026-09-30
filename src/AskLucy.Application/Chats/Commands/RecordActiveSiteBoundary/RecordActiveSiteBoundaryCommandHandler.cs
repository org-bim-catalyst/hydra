using AskLucy.Application.Abstractions;
using AskLucy.Application.SiteBoundaries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Chats.Commands.RecordActiveSiteBoundary;

internal static partial class RecordActiveSiteBoundaryLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Chat {ChatId} was shown correction {CorrectionId}, but it no longer exists for this user; the chat keeps its previous outline")]
    public static partial void CorrectionGone(ILogger logger, Guid chatId, Guid correctionId);
}

public sealed class RecordActiveSiteBoundaryCommandHandler(
    IUserChatRepository userChatRepository,
    ISiteBoundaryCorrectionRepository correctionRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<RecordActiveSiteBoundaryCommandHandler> logger) : IRequestHandler<RecordActiveSiteBoundaryCommand>
{
    private const string SystemActor = "system:boundary-resolution";

    public async Task Handle(RecordActiveSiteBoundaryCommand request, CancellationToken cancellationToken)
    {
        var chat = await userChatRepository.GetByIdAsync(request.UserChatId, cancellationToken);
        if (chat is null)
        {
            return; // Chat deleted before this ran — nothing to update.
        }

        var actor = currentUser.UserId ?? SystemActor;
        var boundary = request.ConfirmedBoundary;

        // specs/079 - a reused hand-edited outline is not something Lucy found. The chat keeps the
        // outline she found (from the correction's snapshot) and links to the correction, so a
        // reset can put the found one back. Storing these rings as found would make that impossible.
        if (boundary.CorrectionId is { } correctionId)
        {
            var correction = currentUser.UserId is { } userId
                ? await correctionRepository.GetByIdAsync(correctionId, userId, cancellationToken)
                : null;
            if (correction is null)
            {
                RecordActiveSiteBoundaryLog.CorrectionGone(logger, request.UserChatId, correctionId);
                return;
            }

            var found = correction.FoundSnapshot;
            chat.SetActiveBoundary(
                correction.SiteName,
                correction.FoundCentroidLatitude,
                correction.FoundCentroidLongitude,
                found.Polygon,
                found.AreaSquareMeters,
                found.Confidence,
                found.ConfidenceLevel,
                found.Source,
                found.SourceDetail,
                actor,
                found.CorePolygon,
                found.AdditionalPolygons,
                found.Members);
            chat.LinkSiteBoundaryCorrection(correction.Id, actor);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        chat.SetActiveBoundary(
            boundary.SiteName,
            boundary.CentroidLatitude,
            boundary.CentroidLongitude,
            boundary.Polygon,
            boundary.AreaSquareMeters,
            boundary.Confidence,
            boundary.ConfidenceLevel,
            boundary.Source,
            boundary.SourceDetail,
            actor,
            boundary.CorePolygon,
            boundary.AdditionalPolygons,
            boundary.Members);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
