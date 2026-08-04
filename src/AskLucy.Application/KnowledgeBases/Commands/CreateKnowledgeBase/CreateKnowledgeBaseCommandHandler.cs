using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.CreateKnowledgeBase;

public sealed class CreateKnowledgeBaseCommandHandler(
    IKnowledgeBaseRepository repository,
    IKnowledgeBaseAuditLogRepository auditLogRepository,
    KnowledgeBaseDashboardSummaryCache dashboardSummaryCache,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateKnowledgeBaseCommand, KnowledgeBaseSummaryDto>
{
    public async Task<KnowledgeBaseSummaryDto> Handle(CreateKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var knowledgeBase = KnowledgeBase.Create(request.Name, userId, userId);
        knowledgeBase.UpdateDetails(request.Name, request.Description, request.Color, request.Icon, request.CategoryId, notes: null, userId);

        foreach (var tag in request.Tags ?? [])
        {
            knowledgeBase.AddTag(tag, userId, userId);
        }

        repository.Add(knowledgeBase);
        auditLogRepository.Add(KnowledgeBaseAuditLog.Create(
            knowledgeBase.Id, userId, KnowledgeBaseAuditAction.Created, $"Created knowledge base '{knowledgeBase.Name}'", userId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        dashboardSummaryCache.Invalidate(userId);

        return KnowledgeBaseSummaryDto.FromEntity(knowledgeBase);
    }
}
