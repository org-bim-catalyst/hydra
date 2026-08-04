using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.KnowledgeBases;

/// <summary>A category as returned by `GET /knowledge-bases/categories` (FR-017/FR-018/FR-038, contracts/knowledge-base-taxonomy-api.md).</summary>
public sealed record KnowledgeBaseCategoryDto(Guid Id, string Name, bool IsPredefined)
{
    public static KnowledgeBaseCategoryDto FromEntity(KnowledgeBaseCategory category) => new(category.Id, category.Name, category.IsPredefined);
}
