using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using AskLucy.Domain.KnowledgeBases;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.DeleteKnowledgeBase;

public sealed class DeleteKnowledgeBaseCommandHandler(
    IKnowledgeBaseRepository repository,
    IKnowledgeBaseAuditLogRepository auditLogRepository,
    KnowledgeBaseDashboardSummaryCache dashboardSummaryCache,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeleteKnowledgeBaseCommand>
{
    public async Task Handle(DeleteKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.Id, cancellationToken), userId);

        knowledgeBase.SoftDelete(userId);
        auditLogRepository.Add(KnowledgeBaseAuditLog.Create(
            knowledgeBase.Id, userId, KnowledgeBaseAuditAction.Deleted, $"Deleted knowledge base '{knowledgeBase.Name}'", userId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        dashboardSummaryCache.Invalidate(userId);
    }
}
