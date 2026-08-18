using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

public sealed record PromptVersionRefDto(Guid Id, int VersionNumber);

/// <summary>Full detail view of a <see cref="Prompt"/> (contracts/prompts-api.md `PromptDetailDto`), assembled from the aggregate plus its current version and usage statistics.</summary>
public sealed record PromptDetailDto(
    Guid Id,
    string Name,
    string? Description,
    PromptType PromptType,
    PromptStatus Status,
    string? SystemInstructions,
    string? DeveloperInstructions,
    string UserInstructions,
    string? ContextText,
    string? ExamplesText,
    string? OutputInstructions,
    string? Constraints,
    Guid? CategoryId,
    Guid? FolderId,
    bool IsFavorite,
    bool IsPinned,
    PromptCapabilityRequirements RequiredCapabilities,
    string? PreferredModelKey,
    PromptVersionRefDto CurrentVersion,
    IReadOnlyList<PromptVariableDto> Variables,
    IReadOnlyList<string> Tags,
    int UsageCount,
    DateTime? LastSuccessfulUseAtUtc,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc)
{
    public static PromptDetailDto Create(Prompt prompt, PromptVersion currentVersion, PromptUsageStatistics? usageStatistics) => new(
        prompt.Id,
        prompt.Name,
        prompt.Description,
        prompt.PromptType,
        prompt.Status,
        prompt.SystemInstructions,
        prompt.DeveloperInstructions,
        prompt.UserInstructions,
        prompt.ContextText,
        prompt.ExamplesText,
        prompt.OutputInstructions,
        prompt.Constraints,
        prompt.CategoryId,
        prompt.FolderId,
        prompt.IsFavorite,
        prompt.IsPinned,
        prompt.RequiredCapabilities,
        prompt.PreferredModelKey,
        new PromptVersionRefDto(currentVersion.Id, currentVersion.VersionNumber),
        [.. currentVersion.Variables.OrderBy(v => v.OrderIndex).Select(PromptVariableDto.FromEntity)],
        [.. prompt.Tags.Select(t => t.Value)],
        usageStatistics?.SuccessfulExecutionCount ?? 0,
        usageStatistics?.LastSuccessfulUseAtUtc,
        prompt.CreatedAtUtc,
        prompt.ModifiedAtUtc);
}
