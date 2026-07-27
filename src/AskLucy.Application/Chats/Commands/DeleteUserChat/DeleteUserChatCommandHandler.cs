using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Commands.DeleteUserChat;

public sealed class DeleteUserChatCommandHandler(
    IUserChatRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeleteUserChatCommand>
{
    public async Task Handle(DeleteUserChatCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(
            await repository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        chat.SoftDelete(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
