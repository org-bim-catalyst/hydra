namespace AskLucy.Domain.CustomModels;

/// <summary>
/// specs/072 FR-010a. A file the deployment replaced on the target. Rows are appended straight to
/// the table by the job (data-model.md), so the aggregate has no navigation to them and never loads
/// thousands of rows just to change its state.
/// </summary>
public sealed class CustomModelOverwrittenFile
{
    public const int MaxRelativePathLength = 1024;

    public long Id { get; private set; }

    public Guid CustomModelId { get; private set; }

    /// <summary>Relative to the deployment root, for example <c>Models/supertonic-3/onnx/vocoder.onnx</c>. Never includes the root itself.</summary>
    public string RelativePath { get; private set; } = string.Empty;

    public long PreviousSizeBytes { get; private set; }

    public DateTime OverwrittenAtUtc { get; private set; }

    private CustomModelOverwrittenFile()
    {
        // Required by EF Core materialization.
    }

    internal static CustomModelOverwrittenFile Create(Guid customModelId, string relativePath, long previousSizeBytes, DateTime utcNow) => new()
    {
        CustomModelId = customModelId,
        RelativePath = relativePath,
        PreviousSizeBytes = previousSizeBytes,
        OverwrittenAtUtc = utcNow,
    };
}
