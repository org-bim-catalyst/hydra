using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.ExportKnowledgeBase;

public sealed class ExportKnowledgeBaseQueryHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseFolderRepository folderRepository,
    IKnowledgeBaseCategoryRepository categoryRepository,
    TimeProvider timeProvider,
    ICurrentUserAccessor currentUser) : IRequestHandler<ExportKnowledgeBaseQuery, KnowledgeBaseExportDto>
{
    public async Task<KnowledgeBaseExportDto> Handle(ExportKnowledgeBaseQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(
            await knowledgeBaseRepository.GetByIdAsync(request.Id, cancellationToken), userId);

        string? categoryName = null;
        if (knowledgeBase.CategoryId is { } categoryId)
        {
            categoryName = (await categoryRepository.GetByIdAsync(categoryId, cancellationToken))?.Name;
        }

        var folders = await folderRepository.ListByKnowledgeBaseIdAsync(knowledgeBase.Id, cancellationToken);

        return new KnowledgeBaseExportDto(
            knowledgeBase.Id,
            knowledgeBase.Name,
            knowledgeBase.Description,
            knowledgeBase.CategoryId,
            categoryName,
            [.. knowledgeBase.Tags.Select(t => t.Value)],
            [.. folders.Select(KnowledgeBaseFolderDto.FromEntity)],
            knowledgeBase.DocumentCount,
            knowledgeBase.TotalPageCount,
            knowledgeBase.StorageSizeBytes,
            knowledgeBase.Notes,
            knowledgeBase.CreatedAtUtc,
            timeProvider.GetUtcNow().UtcDateTime);
    }
}
