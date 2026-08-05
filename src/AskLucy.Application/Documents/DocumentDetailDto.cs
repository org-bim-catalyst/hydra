namespace AskLucy.Application.Documents;

/// <summary>
/// contracts/documents-api.md's detail shape. <see cref="RowVersion"/> is the parent
/// <c>Document</c> row's own concurrency token (document-level edits like rename) — distinct from
/// <see cref="DocumentMetadataDto.RowVersion"/> inside <see cref="Metadata"/>, used specifically
/// for metadata edits (US3, tasks.md T086).
/// </summary>
public sealed record DocumentDetailDto(
    DocumentSummaryDto Summary,
    string OriginalFileName,
    string VersionLabel,
    byte[] RowVersion,
    string? ExtractedText,
    string? ExtractedStructure,
    DocumentMetadataDto? Metadata,
    IReadOnlyList<DocumentLanguageDto> Languages,
    DocumentClassificationDto? Classification)
{
    public static DocumentDetailDto FromEntity(
        Domain.Documents.Document document,
        Domain.Documents.DocumentVersion currentVersion,
        DocumentMetadataDto? metadata,
        IReadOnlyList<DocumentLanguageDto> languages,
        DocumentClassificationDto? classification) => new(
        DocumentSummaryDto.FromEntity(document),
        currentVersion.OriginalFileName,
        $"{currentVersion.VersionMajor}.{currentVersion.VersionMinor}",
        document.RowVersion,
        currentVersion.ExtractedText,
        currentVersion.ExtractedStructureJson,
        metadata,
        languages,
        classification);
}
