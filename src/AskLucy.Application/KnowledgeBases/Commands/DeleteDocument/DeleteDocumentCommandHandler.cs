using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.DeleteDocument;

public sealed class DeleteDocumentCommandHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseDocumentRepository documentRepository,
    KnowledgeBaseDashboardSummaryCache dashboardSummaryCache,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeleteDocumentCommand>
{
    public async Task Handle(DeleteDocumentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(
            await knowledgeBaseRepository.GetByIdAsync(request.KnowledgeBaseId, cancellationToken), userId);
        var document = KnowledgeBaseDocumentGuard.EnsureBelongsTo(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), request.KnowledgeBaseId);

        document.SoftDelete(userId);
        knowledgeBase.ApplyDocumentRemoved(document.PageCount, document.SizeBytes, userId);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        dashboardSummaryCache.Invalidate(userId);
    }
}
