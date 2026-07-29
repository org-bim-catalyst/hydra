using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Commands.RestoreUserChat;

/// <summary>Owner-scoped (FR-026). Looks up the conversation bypassing the soft-delete filter (FR-005a) since it may currently be in Recently Deleted.</summary>
public sealed class RestoreUserChatCommandHandler(
    IUserChatRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RestoreUserChatCommand, UserChatSummaryDto>
{
    public async Task<UserChatSummaryDto> Handle(RestoreUserChatCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(
            await repository.GetByIdIncludingDeletedAsync(request.ChatId, cancellationToken), userId);

        chat.Restore(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserChatSummaryDto.FromEntity(chat);
    }
}
