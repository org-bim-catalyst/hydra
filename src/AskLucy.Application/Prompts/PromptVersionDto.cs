using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

public sealed record PromptVersionSummaryDto(
    Guid Id, int VersionNumber, string? ChangeDescription, string CreatedBy, DateTime CreatedAtUtc)
{
    public static PromptVersionSummaryDto FromEntity(PromptVersion version) => new(
        version.Id, version.VersionNumber, version.ChangeDescription, version.CreatedBy, version.CreatedAtUtc);
}

public sealed record PromptVersionDetailDto(
    Guid Id,
    int VersionNumber,
    string? SystemInstructions,
    string? DeveloperInstructions,
    string UserInstructions,
    string? ContextText,
    string? ExamplesText,
    string? OutputInstructions,
    string? Constraints,
    string? ProviderKey,
    string? ModelKey,
    decimal? Temperature,
    int? MaxOutputTokens,
    bool StructuredOutputRequested,
    IReadOnlyList<PromptVariableDto> Variables,
    string? ChangeDescription,
    string CreatedBy,
    DateTime CreatedAtUtc)
{
    public static PromptVersionDetailDto FromEntity(PromptVersion version) => new(
        version.Id,
        version.VersionNumber,
        version.SystemInstructions,
        version.DeveloperInstructions,
        version.UserInstructions,
        version.ContextText,
        version.ExamplesText,
        version.OutputInstructions,
        version.Constraints,
        version.ProviderKey,
        version.ModelKey,
        version.Temperature,
        version.MaxOutputTokens,
        version.StructuredOutputRequested,
        [.. version.Variables.OrderBy(v => v.OrderIndex).Select(PromptVariableDto.FromEntity)],
        version.ChangeDescription,
        version.CreatedBy,
        version.CreatedAtUtc);
}

/// <summary>Field-by-field diff between two versions (spec.md FR-032, User Story 3 AC2).</summary>
public sealed record PromptVersionFieldDiff(string FieldName, string? FromValue, string? ToValue);

public sealed record PromptVersionComparisonDto(
    PromptVersionSummaryDto From, PromptVersionSummaryDto To, IReadOnlyList<PromptVersionFieldDiff> Differences);
