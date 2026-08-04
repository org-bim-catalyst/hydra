using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.KnowledgeBases.Commands.PurgeKnowledgeBase;

internal static partial class PurgeKnowledgeBaseCommandHandlerLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Knowledge base {KnowledgeBaseId} permanently deleted by {UserId} (FR-011 audit event)")]
    public static partial void KnowledgeBasePurged(ILogger logger, Guid knowledgeBaseId, string userId);
}

/// <summary>
/// Owner-scoped (FR-010). Looks the knowledge base up bypassing the soft-delete filter since
/// it must already be soft-deleted to be purged (FR-036). The audit log entry is committed
/// (its own <see cref="IUnitOfWork.SaveChangesAsync"/> call) BEFORE any document file is
/// deleted, per spec.md's edge case: the audit trail must durably record that the purge
/// happened even if a later file-deletion step fails — a deliberate two-checkpoint sequence,
/// not an accidental extra commit.
/// </summary>
public sealed class PurgeKnowledgeBaseCommandHandler(
    IKnowledgeBaseRepository repository,
    IKnowledgeBaseDocumentRepository documentRepository,
    IKnowledgeBaseAuditLogRepository auditLogRepository,
    KnowledgeBaseDashboardSummaryCache dashboardSummaryCache,
    IFileStorage fileStorage,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<PurgeKnowledgeBaseCommandHandler> logger) : IRequestHandler<PurgeKnowledgeBaseCommand>
{
    public async Task Handle(PurgeKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(
            await repository.GetByIdIncludingDeletedAsync(request.Id, cancellationToken), userId);

        if (knowledgeBase.DeletedAtUtc is null)
        {
            throw new DomainRuleViolationException("A knowledge base must be soft-deleted before it can be permanently purged.");
        }

        auditLogRepository.Add(KnowledgeBaseAuditLog.Create(
            knowledgeBase.Id, userId, KnowledgeBaseAuditAction.PermanentlyDeleted, $"Permanently deleted knowledge base '{knowledgeBase.Name}'", userId));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var documents = await documentRepository.ListByKnowledgeBaseIdIncludingDeletedAsync(knowledgeBase.Id, cancellationToken);
        foreach (var document in documents)
        {
            await fileStorage.DeleteAsync(document.StoredFileName, cancellationToken);
        }

        await repository.PurgeAsync(knowledgeBase.Id, cancellationToken);
        dashboardSummaryCache.Invalidate(userId);

        PurgeKnowledgeBaseCommandHandlerLog.KnowledgeBasePurged(logger, knowledgeBase.Id, userId);
    }
}
