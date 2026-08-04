using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using AskLucy.Domain.KnowledgeBases;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.UpdateKnowledgeBaseDetails;

/// <summary>Owner-scoped (FR-010): a knowledge base that doesn't exist or isn't the caller's own reports identically as not-found.</summary>
public sealed class UpdateKnowledgeBaseDetailsCommandHandler(
    IKnowledgeBaseRepository repository,
    IKnowledgeBaseAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<UpdateKnowledgeBaseDetailsCommand, KnowledgeBaseSummaryDto>
{
    public async Task<KnowledgeBaseSummaryDto> Handle(UpdateKnowledgeBaseDetailsCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await repository.GetByIdAsync(request.Id, cancellationToken), userId);

        knowledgeBase.UpdateDetails(request.Name, request.Description, request.Color, request.Icon, request.CategoryId, request.Notes, userId);

        if (request.Tags is not null)
        {
            ReplaceTags(knowledgeBase, request.Tags, userId);
        }

        auditLogRepository.Add(KnowledgeBaseAuditLog.Create(
            knowledgeBase.Id, userId, KnowledgeBaseAuditAction.Edited, $"Edited knowledge base '{knowledgeBase.Name}'", userId));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseSummaryDto.FromEntity(knowledgeBase);
    }

    private static void ReplaceTags(KnowledgeBase knowledgeBase, IReadOnlyList<string> desiredTags, string userId)
    {
        var toRemove = knowledgeBase.Tags
            .Where(existing => !desiredTags.Any(desired => string.Equals(desired, existing.Value, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var tag in toRemove)
        {
            knowledgeBase.RemoveTag(tag, userId);
        }

        foreach (var value in desiredTags)
        {
            knowledgeBase.AddTag(value, userId, userId);
        }
    }
}
