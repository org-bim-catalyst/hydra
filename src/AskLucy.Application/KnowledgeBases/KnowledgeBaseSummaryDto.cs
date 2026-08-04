using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.KnowledgeBases;

/// <summary>A knowledge base as listed on the dashboard/search results (FR-026) — deliberately lighter than the full detail view.</summary>
public sealed record KnowledgeBaseSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    KnowledgeBaseStatus Status,
    string? Color,
    string? Icon,
    Guid? CategoryId,
    IReadOnlyList<string> Tags,
    bool IsFavorite,
    bool IsPinned,
    int DocumentCount,
    int TotalPageCount,
    long StorageSizeBytes,
    DateTime CreatedAtUtc,
    DateTime LastUpdatedAtUtc,
    bool IsDeleted)
{
    public static KnowledgeBaseSummaryDto FromEntity(KnowledgeBase knowledgeBase) => new(
        knowledgeBase.Id,
        knowledgeBase.Name,
        knowledgeBase.Description,
        knowledgeBase.Status,
        knowledgeBase.Color,
        knowledgeBase.Icon,
        knowledgeBase.CategoryId,
        [.. knowledgeBase.Tags.Select(t => t.Value)],
        knowledgeBase.IsFavorite,
        IsPinned: knowledgeBase.PinnedAtUtc is not null,
        knowledgeBase.DocumentCount,
        knowledgeBase.TotalPageCount,
        knowledgeBase.StorageSizeBytes,
        knowledgeBase.CreatedAtUtc,
        LastUpdatedAtUtc: knowledgeBase.ModifiedAtUtc ?? knowledgeBase.CreatedAtUtc,
        IsDeleted: knowledgeBase.DeletedAtUtc is not null);
}
