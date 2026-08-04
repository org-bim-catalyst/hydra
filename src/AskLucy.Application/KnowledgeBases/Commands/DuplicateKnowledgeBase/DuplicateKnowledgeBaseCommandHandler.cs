using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using AskLucy.Application.Options;
using AskLucy.Domain.KnowledgeBases;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.KnowledgeBases.Commands.DuplicateKnowledgeBase;

/// <summary>
/// Every document's file is re-saved through <see cref="IFileStorage"/> (open the source, save
/// as a new stored name) rather than the copy referencing the source's `StoredFileName` — an
/// independent physical file, not a shared reference, so purging either copy never affects the
/// other (research.md Decision 4, spec.md Clarifications). Folders are re-created in ascending
/// `Depth` order (a parent's `Depth` is always less than its children's) so each child's new
/// parent already exists when it's created, using an old-id -> new-id map to remap both
/// `ParentFolderId` and each document's `FolderId`.
/// </summary>
public sealed class DuplicateKnowledgeBaseCommandHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseFolderRepository folderRepository,
    IKnowledgeBaseDocumentRepository documentRepository,
    IKnowledgeBaseAuditLogRepository auditLogRepository,
    IFileStorage fileStorage,
    IOptions<KnowledgeBaseFolderOptions> folderOptions,
    KnowledgeBaseDashboardSummaryCache dashboardSummaryCache,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DuplicateKnowledgeBaseCommand, KnowledgeBaseSummaryDto>
{
    public async Task<KnowledgeBaseSummaryDto> Handle(DuplicateKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var source = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(
            await knowledgeBaseRepository.GetByIdAsync(request.Id, cancellationToken), userId);

        var duplicate = KnowledgeBase.Create($"Copy of {source.Name}", userId, userId);
        duplicate.UpdateDetails(duplicate.Name, source.Description, source.Color, source.Icon, source.CategoryId, source.Notes, userId);
        foreach (var tag in source.Tags)
        {
            duplicate.AddTag(tag.Value, userId, userId);
        }

        knowledgeBaseRepository.Add(duplicate);

        var sourceFolders = await folderRepository.ListByKnowledgeBaseIdAsync(source.Id, cancellationToken);
        var folderIdMap = new Dictionary<Guid, Guid>();
        var newFoldersByOldId = new Dictionary<Guid, KnowledgeBaseFolder>();

        foreach (var sourceFolder in sourceFolders.OrderBy(f => f.Depth))
        {
            var newParentId = sourceFolder.ParentFolderId is { } oldParentId ? folderIdMap[oldParentId] : (Guid?)null;
            var parentDepth = sourceFolder.ParentFolderId is { } parentId ? newFoldersByOldId[parentId].Depth : 0;

            var newFolder = KnowledgeBaseFolder.Create(
                duplicate.Id, sourceFolder.Name, newParentId, parentDepth, folderOptions.Value.MaxNestingDepth, userId);
            folderRepository.Add(newFolder);
            folderIdMap[sourceFolder.Id] = newFolder.Id;
            newFoldersByOldId[sourceFolder.Id] = newFolder;
        }

        var sourceDocuments = (await documentRepository.ListByKnowledgeBaseIdIncludingDeletedAsync(source.Id, cancellationToken))
            .Where(d => !d.IsDeleted);

        foreach (var sourceDocument in sourceDocuments)
        {
            await using var sourceStream = await fileStorage.OpenReadAsync(sourceDocument.StoredFileName, cancellationToken);
            var newStoredFileName = await fileStorage.SaveAsync(sourceStream, sourceDocument.FileName, cancellationToken);
            var newFolderId = sourceDocument.FolderId is { } oldFolderId ? folderIdMap[oldFolderId] : (Guid?)null;

            var newDocument = KnowledgeBaseDocument.Create(
                duplicate.Id, newFolderId, sourceDocument.FileName, newStoredFileName, sourceDocument.ContentType,
                sourceDocument.SizeBytes, sourceDocument.PageCount, userId);
            documentRepository.Add(newDocument);
            duplicate.ApplyDocumentAdded(sourceDocument.PageCount, sourceDocument.SizeBytes, userId);
        }

        auditLogRepository.Add(KnowledgeBaseAuditLog.Create(
            source.Id, userId, KnowledgeBaseAuditAction.Duplicated, $"Duplicated into '{duplicate.Name}'", userId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        dashboardSummaryCache.Invalidate(userId);

        return KnowledgeBaseSummaryDto.FromEntity(duplicate);
    }
}
