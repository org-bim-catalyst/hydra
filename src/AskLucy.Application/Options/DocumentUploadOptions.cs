using System.ComponentModel.DataAnnotations;

namespace AskLucy.Application.Options;

/// <summary>Bound from configuration (constitution §4) — read directly by upload command handlers (Application layer), mirrors <see cref="KnowledgeBaseDocumentOptions"/>'s placement here rather than Infrastructure.</summary>
public sealed class DocumentUploadOptions
{
    public const string SectionName = "DocumentUploads";

    /// <summary>Client-side chunk size for resumable uploads (FR-005). Defaults to 5 MB.</summary>
    [Range(1, long.MaxValue)]
    public long ChunkSizeBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>Rejected with a specific 400 above this size, before any processing resource is consumed (FR-011, constitution §8). Defaults to 500 MB.</summary>
    [Range(1, long.MaxValue)]
    public long MaxFileSizeBytes { get; init; } = 500 * 1024 * 1024;

    /// <summary>An abandoned upload session's chunks are eligible for cleanup after this long.</summary>
    public TimeSpan UploadSessionExpiry { get; init; } = TimeSpan.FromHours(24);
}
