namespace AskLucy.Infrastructure.Files;

public sealed class LocalFileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Absolute path outside the web root (constitution &#167;8 file-upload guidance).</summary>
    public required string RootPath { get; init; }
}
