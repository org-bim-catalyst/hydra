using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

public sealed record PromptCategoryDto(Guid Id, string Name, bool IsPredefined)
{
    public static PromptCategoryDto FromEntity(PromptCategory category) => new(category.Id, category.Name, category.IsPredefined);
}
