using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

/// <summary>A lighter projection than <see cref="PromptDetailDto"/>, used by list/search views (contracts/prompts-api.md).</summary>
public sealed record PromptListItemDto(
    Guid Id,
    string Name,
    string? Description,
    PromptType PromptType,
    PromptStatus Status,
    Guid? CategoryId,
    IReadOnlyList<string> Tags,
    bool IsFavorite,
    bool IsPinned,
    int UsageCount,
    DateTime? LastSuccessfulUseAtUtc,
    DateTime? ModifiedAtUtc)
{
    public static PromptListItemDto FromEntity(Prompt prompt, int usageCount, DateTime? lastSuccessfulUseAtUtc) => new(
        prompt.Id, prompt.Name, prompt.Description, prompt.PromptType, prompt.Status, prompt.CategoryId,
        [.. prompt.Tags.Select(t => t.Value)], prompt.IsFavorite, prompt.IsPinned,
        usageCount, lastSuccessfulUseAtUtc, prompt.ModifiedAtUtc);
}
