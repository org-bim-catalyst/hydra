using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Domain.Chats;
using MediatR;

namespace AskLucy.Application.Chats.Commands.DuplicateUserChat;

/// <summary>
/// Owner-scoped (FR-026). Bulk-copies every message (with its attachments/citations) into
/// a brand-new conversation, committed through a single <see cref="IUnitOfWork.SaveChangesAsync"/>
/// call (constitution &#167;5 — one business transaction per commit). The duplicate always starts
/// unpinned/unfavorited/unarchived regardless of the source's state (edge case, spec.md).
/// </summary>
public sealed class DuplicateUserChatCommandHandler(
    IUserChatRepository chatRepository,
    IMessageRepository messageRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DuplicateUserChatCommand, UserChatSummaryDto>
{
    public async Task<UserChatSummaryDto> Handle(DuplicateUserChatCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var source = ChatOwnershipGuard.EnsureOwnedBy(await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        var duplicate = UserChat.Create(source.Title, userId, null, userId);
        chatRepository.Add(duplicate);

        var sourceMessages = await messageRepository.ListByChatIdAsync(request.ChatId, cancellationToken);
        foreach (var sourceMessage in sourceMessages)
        {
            var copy = Message.Create(
                duplicate.Id, sourceMessage.Role, sourceMessage.Kind, sourceMessage.Content, sourceMessage.SourceText, userId,
                sourceMessage.Provider, sourceMessage.Model, sourceMessage.GenerationParametersJson,
                sourceMessage.InputTokenCount, sourceMessage.OutputTokenCount);

            foreach (var attachment in sourceMessage.Attachments)
            {
                copy.AddAttachment(attachment.FileName, attachment.ContentType, attachment.AccessLocation, userId);
            }

            foreach (var citation in sourceMessage.Citations)
            {
                copy.AddCitation(citation.SourceLabel, citation.SourceReference, userId);
            }

            messageRepository.Add(copy);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserChatSummaryDto.FromEntity(duplicate);
    }
}
