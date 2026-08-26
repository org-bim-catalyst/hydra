using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Chats.Commands.RecordActiveSiteBoundary;

public sealed class RecordActiveSiteBoundaryCommandHandler(
    IUserChatRepository userChatRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RecordActiveSiteBoundaryCommand>
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
            actor);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
