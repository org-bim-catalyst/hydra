using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

/// <summary>
/// Pure, dependency-free JSON-shape builder (research.md Decision 13) — just assembling the
/// portable export shape from already-loaded aggregates, no file-system or network I/O. A plain
/// static class, not an injected service behind an interface, mirroring
/// <see cref="PromptContentAnalyzer"/>/<see cref="PromptVariableResolver"/>/<see cref="PromptCapabilityChecker"/>'s
/// identical "no external dependency, so no DI indirection" convention.
/// </summary>
public static class PromptExportFileBuilder
{
    public static PromptExportFile Build(IReadOnlyList<(Prompt Prompt, PromptVersion CurrentVersion)> prompts) =>
        new(PromptExportFile.CurrentSchemaVersion, [.. prompts.Select(p => PromptExportEntry.FromEntity(p.Prompt, p.CurrentVersion))]);
}
