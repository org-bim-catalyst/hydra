using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.UnpinKnowledgeBase;

public sealed class UnpinKnowledgeBaseCommandHandler(
    IKnowledgeBaseRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<UnpinKnowledgeBaseCommand, KnowledgeBaseSummaryDto>
{
    public async Task<KnowledgeBaseSummaryDto> Handle(UnpinKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.Id, cancellationToken), userId);

        knowledgeBase.Unpin(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseSummaryDto.FromEntity(knowledgeBase);
    }
}
