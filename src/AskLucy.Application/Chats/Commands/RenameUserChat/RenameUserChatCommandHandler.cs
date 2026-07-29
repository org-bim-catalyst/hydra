using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Commands.RenameUserChat;

/// <summary>
/// Owner-scoped: a chat that doesn't exist or doesn't belong to the caller is reported
/// identically (404) so existence can't be inferred by a non-owner (FR-018, User Story 3).
/// </summary>
public sealed class RenameUserChatCommandHandler(
    IUserChatRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RenameUserChatCommand, UserChatDto>
{
    public async Task<UserChatDto> Handle(RenameUserChatCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(
            await repository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        chat.Rename(request.NewTitle, userId);
        chat.MarkTitleManuallySet();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UserChatDto(chat.Id, chat.Title, chat.CreatedAtUtc, chat.ModifiedAtUtc);
    }
}
