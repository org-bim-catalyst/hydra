using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Commands.PinUserChat;

public sealed class PinUserChatCommandHandler(
    IUserChatRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<PinUserChatCommand, UserChatSummaryDto>
{
    public async Task<UserChatSummaryDto> Handle(PinUserChatCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        chat.Pin(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserChatSummaryDto.FromEntity(chat);
    }
}
