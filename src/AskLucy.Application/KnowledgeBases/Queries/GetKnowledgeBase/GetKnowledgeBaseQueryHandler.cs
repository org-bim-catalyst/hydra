using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.GetKnowledgeBase;

public sealed class GetKnowledgeBaseQueryHandler(
    IKnowledgeBaseRepository repository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetKnowledgeBaseQuery, KnowledgeBaseDetailDto>
{
    public async Task<KnowledgeBaseDetailDto> Handle(GetKnowledgeBaseQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.Id, cancellationToken), userId);

        return KnowledgeBaseDetailDto.FromEntity(knowledgeBase);
    }
}
