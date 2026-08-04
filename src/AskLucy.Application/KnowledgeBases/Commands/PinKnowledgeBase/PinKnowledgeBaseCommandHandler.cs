using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.PinKnowledgeBase;

public sealed class PinKnowledgeBaseCommandHandler(
    IKnowledgeBaseRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<PinKnowledgeBaseCommand, KnowledgeBaseSummaryDto>
{
    public async Task<KnowledgeBaseSummaryDto> Handle(PinKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.Id, cancellationToken), userId);

        knowledgeBase.Pin(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseSummaryDto.FromEntity(knowledgeBase);
    }
}
