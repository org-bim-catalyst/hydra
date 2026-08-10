using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

/// <summary>
/// The portable export/import file shape (spec.md FR-070–FR-072, research.md Decision 13,
/// contracts/prompts-api.md) — a single-prompt export is simply a one-element <see cref="Prompts"/>
/// array, so export and bulk-export share this one shape with no separate "bundle" schema.
/// </summary>
public sealed record PromptExportFile(int SchemaVersion, IReadOnlyList<PromptExportEntry> Prompts)
{
    /// <summary>The only schema version this build understands — an unrecognized value is rejected outright (spec.md Edge Cases).</summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary>One prompt's full, losslessly-recreatable content (current version + variables + model settings + tags) — deliberately omits ownership/id/timestamps/usage/version-history, since import always creates a brand-new, independent prompt with its own fresh version-1 history (FR-072).</summary>
public sealed record PromptExportEntry(
    string Name,
    string? Description,
    PromptType PromptType,
    string? SystemInstructions,
    string? DeveloperInstructions,
    string UserInstructions,
    string? ContextText,
    string? ExamplesText,
    string? OutputInstructions,
    string? Constraints,
    PromptCapabilityRequirements RequiredCapabilities,
    string? PreferredModelKey,
    IReadOnlyList<PromptVariableDto> Variables,
    IReadOnlyList<string> Tags)
{
    public static PromptExportEntry FromEntity(Prompt prompt, PromptVersion currentVersion) => new(
        prompt.Name,
        prompt.Description,
        prompt.PromptType,
        currentVersion.SystemInstructions,
        currentVersion.DeveloperInstructions,
        currentVersion.UserInstructions,
        currentVersion.ContextText,
        currentVersion.ExamplesText,
        currentVersion.OutputInstructions,
        currentVersion.Constraints,
        prompt.RequiredCapabilities,
        prompt.PreferredModelKey,
        [.. currentVersion.Variables.OrderBy(v => v.OrderIndex).Select(PromptVariableDto.FromEntity)],
        [.. prompt.Tags.Select(t => t.Value)]);
}
