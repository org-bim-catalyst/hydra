using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Queries.ExportUserChat;

/// <summary>Owner-scoped (FR-026). Works for a conversation with zero messages, returning an empty `messages` array rather than an error (FR-025 edge case).</summary>
public sealed class ExportUserChatQueryHandler(
    IUserChatRepository chatRepository,
    IMessageRepository messageRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<ExportUserChatQuery, ConversationExportDto>
{
    public async Task<ConversationExportDto> Handle(ExportUserChatQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        var messages = await messageRepository.ListByChatIdAsync(request.ChatId, cancellationToken);

        var exportedMessages = messages.Select(m => new ExportedMessageDto(
            m.Role.ToString(),
            m.Kind.ToString(),
            m.Content,
            m.SourceText,
            m.CreatedAtUtc,
            m.Provider,
            m.Model,
            m.InputTokenCount,
            m.OutputTokenCount,
            [.. m.Attachments.Select(a => new ExportedAttachmentDto(a.FileName, a.ContentType, a.AccessLocation))],
            [.. m.Citations.Select(c => new ExportedCitationDto(c.SourceLabel, c.SourceReference))])).ToList();

        return new ConversationExportDto(chat.Id, chat.Title, chat.CreatedAtUtc, chat.ModifiedAtUtc, exportedMessages);
    }
}
