using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Memory.Queries.GetMemoryReferences;

/// <summary>Owner-scoped via the parent chat (FR-018): a chat that doesn't exist or isn't the caller's own reports identically as not-found, mirroring <c>GetChatMessagesQueryHandler</c>.</summary>
public sealed class GetMemoryReferencesQueryHandler(
    IUserChatRepository chatRepository,
    IMemoryReferenceRepository referenceRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetMemoryReferencesQuery, IReadOnlyList<MemoryReferenceDto>>
{
    public async Task<IReadOnlyList<MemoryReferenceDto>> Handle(GetMemoryReferencesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        ChatOwnershipGuard.EnsureOwnedBy(await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        var references = await referenceRepository.GetByMessageIdAsync(request.MessageId, cancellationToken);

        return references.Select(r => new MemoryReferenceDto(r.MemoryId, r.ContentSnapshot, r.RelevanceScore)).ToList();
    }
}
