using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.FavoriteKnowledgeBase;

public sealed class FavoriteKnowledgeBaseCommandHandler(
    IKnowledgeBaseRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<FavoriteKnowledgeBaseCommand, KnowledgeBaseSummaryDto>
{
    public async Task<KnowledgeBaseSummaryDto> Handle(FavoriteKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.Id, cancellationToken), userId);

        knowledgeBase.MarkFavorite(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseSummaryDto.FromEntity(knowledgeBase);
    }
}
