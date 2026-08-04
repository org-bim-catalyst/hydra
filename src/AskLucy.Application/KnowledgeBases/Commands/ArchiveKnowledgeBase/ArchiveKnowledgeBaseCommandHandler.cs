using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using AskLucy.Domain.KnowledgeBases;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.ArchiveKnowledgeBase;

public sealed class ArchiveKnowledgeBaseCommandHandler(
    IKnowledgeBaseRepository repository,
    IKnowledgeBaseAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<ArchiveKnowledgeBaseCommand, KnowledgeBaseSummaryDto>
{
    public async Task<KnowledgeBaseSummaryDto> Handle(ArchiveKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.Id, cancellationToken), userId);

        knowledgeBase.Archive(userId);
        auditLogRepository.Add(KnowledgeBaseAuditLog.Create(
            knowledgeBase.Id, userId, KnowledgeBaseAuditAction.Archived, $"Archived knowledge base '{knowledgeBase.Name}'", userId));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseSummaryDto.FromEntity(knowledgeBase);
    }
}
