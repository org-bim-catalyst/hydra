using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Chats.Commands.RecordActiveLocation;

public sealed class RecordActiveLocationCommandHandler(
    IUserChatRepository userChatRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RecordActiveLocationCommand>
{
    private const string SystemActor = "system:location-resolution";

    public async Task Handle(RecordActiveLocationCommand request, CancellationToken cancellationToken)
    {
        var chat = await userChatRepository.GetByIdAsync(request.UserChatId, cancellationToken);
        if (chat is null)
        {
            return; // Chat deleted before this ran — nothing to update.
        }

        var actor = currentUser.UserId ?? SystemActor;
        chat.SetActiveLocation(
            request.ConfirmedLocation.Latitude,
            request.ConfirmedLocation.Longitude,
            request.ConfirmedLocation.LocationName,
            request.ConfirmedLocation.Confidence,
            actor);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
