using System.ComponentModel.DataAnnotations;

namespace AskLucy.Application.Options;

/// <summary>Bound from configuration (constitution §4) — read directly by Application-layer command handlers (`CreateFolderCommandHandler`, `MoveFolderCommandHandler`), not just Infrastructure, so this lives in Application rather than Infrastructure (mirrors <see cref="AppOptions"/>, constitution §3 Dependency Rule).</summary>
public sealed class KnowledgeBaseFolderOptions
{
    public const string SectionName = "KnowledgeBaseFolders";

    /// <summary>System-wide default maximum folder nesting depth (spec.md Assumptions, FR-012).</summary>
    [Range(1, 100)]
    public int MaxNestingDepth { get; init; } = 10;
}
