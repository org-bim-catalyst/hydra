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

        // specs/044-location-viewer-regression FR-009a/FR-009b: a stored boundary must never
        // outlive the site it names. Cleared here — atomically with the location write, in the
        // same unit of work — rather than wherever a boundary happens to be recorded, because the
        // case that matters is precisely the one where NO boundary command ever arrives: the new
        // site's resolution failed or timed out. Same OrdinalIgnoreCase comparison the handler's
        // reuse guard uses, so "clear it" and "reuse it" can never disagree.
        if (chat.ActiveBoundary is not null &&
            !string.Equals(chat.ActiveBoundary.SiteName, request.ConfirmedLocation.LocationName, StringComparison.OrdinalIgnoreCase))
        {
            chat.ClearActiveBoundary(actor);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
