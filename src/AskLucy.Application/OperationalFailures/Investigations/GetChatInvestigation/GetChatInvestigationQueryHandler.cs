using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Chats;
using AskLucy.Domain.OperationalFailures;
using MediatR;

namespace AskLucy.Application.OperationalFailures.Investigations.GetChatInvestigation;

/// <summary>
/// research D15. The incident's reference to the chat is the only gate past the chat's ownership
/// check, and it is enforced first: an unreferenced chat is a 404 whatever the caller holds.
/// Metadata is always returned. The transcript needs the content permission, and when the viewer
/// is not the owner the access event is committed <em>before</em> any message content is read —
/// if that write fails, the request fails and no content leaves the server (FR-016c).
/// </summary>
public sealed class GetChatInvestigationQueryHandler(
    IOperationalFailureStore store,
    OperationalFailureReadModelBuilder readModels,
    IUserChatRepository chats,
    IMessageRepository messages,
    IUserContentAccessEventRepository accessEvents,
    IUnitOfWork unitOfWork,
    IEffectivePermissionResolver permissionResolver,
    ICurrentUserAccessor currentUser,
    ICorrelationIdAccessor correlation,
    TimeProvider timeProvider)
    : IRequestHandler<GetChatInvestigationQuery, ChatInvestigationDto>
{
    public async Task<ChatInvestigationDto> Handle(GetChatInvestigationQuery request, CancellationToken cancellationToken)
    {
        var references = await store.FindItemReferencesAsync(request.IncidentId, InvestigatedItemType.Chat, request.ChatId, cancellationToken)
            ?? throw new KeyNotFoundException($"Incident {request.IncidentId} does not reference chat {request.ChatId}.");

        var chat = await chats.GetByIdIncludingDeletedAsync(request.ChatId, cancellationToken);
        var outline = chat is null ? [] : await messages.ListOutlineByChatIdAsync(chat.Id, cancellationToken);
        var failedMessageIds = references.Select(o => o.MessageId).OfType<Guid>().ToHashSet();

        var failurePoints = references
            .Select(o => new ChatFailurePointDto(o.Id, TurnNumber(outline, o.MessageId), o.OccurredAtUtc, o.MessageId))
            .ToList();

        var ownerId = chat?.UserId;
        var users = await readModels.UsersAsync([ownerId], cancellationToken);
        var deleted = chat is null || chat.DeletedAtUtc is not null;

        var summary = new ChatInvestigationChatDto(
            request.ChatId,
            chat?.Title ?? string.Empty,
            ownerId is null ? UserRefDto.Erased : OperationalFailureReadModelBuilder.UserRef(ownerId, users),
            chat?.CreatedAtUtc ?? references.Select(o => o.OccurredAtUtc).DefaultIfEmpty().Min(),
            chat is null ? references.Select(o => o.OccurredAtUtc).DefaultIfEmpty().Max() : chat.ModifiedAtUtc ?? chat.CreatedAtUtc,
            outline.Count,
            deleted);

        var viewerId = currentUser.UserId;
        if (deleted || viewerId is null || !await CanViewContentAsync(viewerId, cancellationToken))
        {
            return new ChatInvestigationDto(summary, failurePoints, Transcript: null);
        }

        if (!string.Equals(viewerId, ownerId, StringComparison.Ordinal))
        {
            await accessEvents.AddAsync(
                UserContentAccessEvent.Record(
                    viewerId,
                    ownerId!,
                    InvestigatedItemType.Chat,
                    request.ChatId,
                    request.IncidentId,
                    correlation.Current ?? Guid.NewGuid().ToString("N"),
                    timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var transcript = (await messages.ListByChatIdAsync(request.ChatId, cancellationToken))
            .Select(m => new ChatTranscriptMessageDto(
                m.Id, m.Role.ToString().ToLowerInvariant(), m.CreatedAtUtc, m.Content, failedMessageIds.Contains(m.Id)))
            .ToList();

        return new ChatInvestigationDto(summary, failurePoints, transcript);
    }

    private async Task<bool> CanViewContentAsync(string viewerId, CancellationToken cancellationToken) =>
        (await permissionResolver.ResolveAsync(viewerId, cancellationToken)).Contains(AdminPermissionCatalog.OperationalFailuresContentView);

    /// <summary>The failed reply's turn: how many user messages precede it. Null when the reply is not in the chat.</summary>
    private static int? TurnNumber(IReadOnlyList<MessageOutline> outline, Guid? messageId)
    {
        if (messageId is null)
        {
            return null;
        }

        var userMessages = 0;
        foreach (var message in outline)
        {
            if (message.Id == messageId)
            {
                return message.Role == MessageRole.User ? userMessages + 1 : Math.Max(userMessages, 1);
            }

            if (message.Role == MessageRole.User)
            {
                userMessages++;
            }
        }

        return null;
    }
}
