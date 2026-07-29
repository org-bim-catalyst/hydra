using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Commands.ArchiveUserChat;

/// <summary>Owner-scoped (FR-026): a chat that doesn't exist or isn't the caller's own reports identically as not-found.</summary>
public sealed class ArchiveUserChatCommandHandler(
    IUserChatRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<ArchiveUserChatCommand, UserChatSummaryDto>
{
    public async Task<UserChatSummaryDto> Handle(ArchiveUserChatCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        chat.Archive(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserChatSummaryDto.FromEntity(chat);
    }
}
