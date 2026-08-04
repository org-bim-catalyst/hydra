using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.KnowledgeBases;

/// <summary>Adds fields not needed on the list/search view (FR-003) — notes, owner, folder count.</summary>
public sealed record KnowledgeBaseDetailDto(
    Guid Id,
    string OwnerId,
    string Name,
    string? Description,
    KnowledgeBaseStatus Status,
    string? Color,
    string? Icon,
    Guid? CategoryId,
    IReadOnlyList<string> Tags,
    string? Notes,
    bool IsFavorite,
    bool IsPinned,
    int DocumentCount,
    int TotalPageCount,
    long StorageSizeBytes,
    DateTime CreatedAtUtc,
    DateTime LastUpdatedAtUtc,
    bool IsDeleted)
{
    public static KnowledgeBaseDetailDto FromEntity(KnowledgeBase knowledgeBase) => new(
        knowledgeBase.Id,
        knowledgeBase.OwnerId,
        knowledgeBase.Name,
        knowledgeBase.Description,
        knowledgeBase.Status,
        knowledgeBase.Color,
        knowledgeBase.Icon,
        knowledgeBase.CategoryId,
        [.. knowledgeBase.Tags.Select(t => t.Value)],
        knowledgeBase.Notes,
        knowledgeBase.IsFavorite,
        IsPinned: knowledgeBase.PinnedAtUtc is not null,
        knowledgeBase.DocumentCount,
        knowledgeBase.TotalPageCount,
        knowledgeBase.StorageSizeBytes,
        knowledgeBase.CreatedAtUtc,
        LastUpdatedAtUtc: knowledgeBase.ModifiedAtUtc ?? knowledgeBase.CreatedAtUtc,
        IsDeleted: knowledgeBase.DeletedAtUtc is not null);
}
