using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.UnfavoriteKnowledgeBase;

public sealed class UnfavoriteKnowledgeBaseCommandHandler(
    IKnowledgeBaseRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<UnfavoriteKnowledgeBaseCommand, KnowledgeBaseSummaryDto>
{
    public async Task<KnowledgeBaseSummaryDto> Handle(UnfavoriteKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.Id, cancellationToken), userId);

        knowledgeBase.UnmarkFavorite(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseSummaryDto.FromEntity(knowledgeBase);
    }
}
