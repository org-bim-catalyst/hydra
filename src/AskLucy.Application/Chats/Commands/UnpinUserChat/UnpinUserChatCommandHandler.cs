using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Commands.UnpinUserChat;

public sealed class UnpinUserChatCommandHandler(
    IUserChatRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<UnpinUserChatCommand, UserChatSummaryDto>
{
    public async Task<UserChatSummaryDto> Handle(UnpinUserChatCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        chat.Unpin(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserChatSummaryDto.FromEntity(chat);
    }
}
