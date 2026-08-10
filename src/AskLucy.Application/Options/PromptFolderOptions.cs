using System.ComponentModel.DataAnnotations;

namespace AskLucy.Application.Options;

/// <summary>Bound from configuration (constitution §4), mirrors <see cref="KnowledgeBaseFolderOptions"/> exactly (research.md Decision 5).</summary>
public sealed class PromptFolderOptions
{
    public const string SectionName = "PromptFolders";

    /// <summary>System-wide default maximum folder nesting depth (spec.md FR-054).</summary>
    [Range(1, 100)]
    public int MaxNestingDepth { get; init; } = 10;
}
