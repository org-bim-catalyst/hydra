using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Commands.FavoriteUserChat;

public sealed class FavoriteUserChatCommandHandler(
    IUserChatRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<FavoriteUserChatCommand, UserChatSummaryDto>
{
    public async Task<UserChatSummaryDto> Handle(FavoriteUserChatCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        chat.MarkFavorite(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserChatSummaryDto.FromEntity(chat);
    }
}
