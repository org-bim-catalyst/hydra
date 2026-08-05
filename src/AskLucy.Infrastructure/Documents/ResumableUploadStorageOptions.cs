namespace AskLucy.Infrastructure.Documents;

public sealed class ResumableUploadStorageOptions
{
    public const string SectionName = "ResumableUploadStorage";

    /// <summary>Absolute path outside the web root, distinct from <c>LocalFileStorageOptions.RootPath</c> (research.md — in-progress chunked uploads never share a directory with permanent stored files).</summary>
    public required string RootPath { get; init; }
}
