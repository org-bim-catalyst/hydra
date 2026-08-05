using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>
/// An immutable snapshot of a <see cref="Document"/>'s file at a point in time (FR-038–FR-042,
/// data-model.md). The original file/size/checksum never change after creation — "replace"
/// always creates a new row (FR-038's "every version keeps its original file"); the derived
/// extracted-content fields start null and are populated progressively as the processing
/// pipeline runs, via <see cref="ApplyExtractedText"/>/<see cref="ApplyOcrText"/>. Who created
/// this version (FR-040) is <see cref="BaseEntity.CreatedBy"/> — no separate field is needed.
/// </summary>
public sealed class DocumentVersion : BaseEntity
{
    public Guid DocumentId { get; private set; }

    public int VersionMajor { get; private set; }

    public int VersionMinor { get; private set; }

    /// <summary>The <c>IFileStorage</c>-minted name — never the original file name (constitution §8).</summary>
    public string StoredFileName { get; private set; } = string.Empty;

    /// <summary>The name the file had at upload time (FR-014) — distinct from <see cref="Document.FileName"/>, which can be renamed independently.</summary>
    public string OriginalFileName { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public Guid ChecksumId { get; private set; }

    public string? ExtractedText { get; private set; }

    public string? ExtractedStructureJson { get; private set; }

    /// <summary>FR-021 OCR output, kept distinct from <see cref="ExtractedText"/> so a document with both an existing text layer and an OCR pass never conflates the two sources.</summary>
    public string? OcrTextRaw { get; private set; }

    public int? PageCount { get; private set; }

    private DocumentVersion()
    {
        // Required by EF Core materialization.
    }

    public static DocumentVersion Create(
        Guid documentId, int versionMajor, int versionMinor, string storedFileName, string originalFileName,
        long sizeBytes, Guid checksumId, string actor)
    {
        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            throw new DomainRuleViolationException("A stored file name is required.");
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new DomainRuleViolationException("An original file name is required.");
        }

        return new DocumentVersion
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            VersionMajor = versionMajor,
            VersionMinor = versionMinor,
            StoredFileName = storedFileName,
            OriginalFileName = originalFileName,
            SizeBytes = sizeBytes,
            ChecksumId = checksumId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>Populated by the Text Extraction processing stage (FR-022).</summary>
    public void ApplyExtractedText(string? extractedText, string? extractedStructureJson, int? pageCount, string actor)
    {
        ExtractedText = extractedText;
        ExtractedStructureJson = extractedStructureJson;
        PageCount = pageCount;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Populated by the OCR processing stage (FR-021); left null when OCR is skipped (an existing text layer was found).</summary>
    public void ApplyOcrText(string? ocrText, string actor)
    {
        OcrTextRaw = ocrText;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
