using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Commands.UnfavoriteUserChat;

public sealed class UnfavoriteUserChatCommandHandler(
    IUserChatRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<UnfavoriteUserChatCommand, UserChatSummaryDto>
{
    public async Task<UserChatSummaryDto> Handle(UnfavoriteUserChatCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        chat.UnmarkFavorite(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserChatSummaryDto.FromEntity(chat);
    }
}
