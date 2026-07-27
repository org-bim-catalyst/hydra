using AskLucy.Application.Abstractions;
using AskLucy.Domain.Chats;
using MediatR;

namespace AskLucy.Application.Chats.Commands.CreateUserChat;

public sealed class CreateUserChatCommandHandler(
    IUserChatRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateUserChatCommand, UserChatDto>
{
    public async Task<UserChatDto> Handle(CreateUserChatCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var chat = UserChat.Create(request.Title, userId, request.SessionId, userId);
        repository.Add(chat);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UserChatDto(chat.Id, chat.Title, chat.CreatedAtUtc, chat.ModifiedAtUtc);
    }
}
