using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Domain.Chats;
using MediatR;

namespace AskLucy.Application.Chats.Commands.AppendMessage;

public sealed class AppendMessageCommandHandler(
    IUserChatRepository chatRepository,
    IMessageRepository messageRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<AppendMessageCommand, MessageDto>
{
    public async Task<MessageDto> Handle(AppendMessageCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        ChatOwnershipGuard.EnsureOwnedBy(await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        var message = Message.Create(request.ChatId, request.Role, request.Kind, request.Content, request.SourceText, userId);
        messageRepository.Add(message);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new MessageDto(message.Id, message.Role.ToString(), message.Kind.ToString(), message.Content, message.SourceText, message.CreatedAtUtc);
    }
}
