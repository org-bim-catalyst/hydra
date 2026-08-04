using System.ComponentModel.DataAnnotations;

namespace AskLucy.Application.Options;

/// <summary>Bound from configuration (constitution §4) — read directly by `UploadDocumentCommandHandler` (Application layer), not just Infrastructure, so this lives in Application rather than Infrastructure (mirrors <see cref="AppOptions"/>, constitution §3 Dependency Rule).</summary>
public sealed class KnowledgeBaseDocumentOptions
{
    public const string SectionName = "KnowledgeBaseDocuments";

    /// <summary>Rejected with a specific 400 above this size (constitution §8 — size-limited before persisting). Defaults to 50 MB.</summary>
    [Range(1, long.MaxValue)]
    public long MaxFileSizeBytes { get; init; } = 50 * 1024 * 1024;
}
