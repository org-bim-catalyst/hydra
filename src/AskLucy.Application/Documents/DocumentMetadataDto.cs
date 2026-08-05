using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents;

/// <summary>
/// contracts/documents-api.md's <c>DocumentMetadataDto</c> shape (FR-023). <see cref="RowVersion"/>
/// is the optimistic-concurrency token for <c>PATCH .../metadata</c> — distinct from
/// <see cref="DocumentDetailDto.RowVersion"/> (the parent <c>Document</c> row's own token, used
/// for document-level edits like rename), since metadata lives in its own child row
/// (data-model.md).
/// </summary>
public sealed record DocumentMetadataDto(
    string? Title,
    string? Author,
    DateTime? CreationDate,
    DateTime? ModificationDate,
    string? Keywords,
    string? Encoding,
    bool IsAutoExtracted,
    byte[] RowVersion)
{
    public static DocumentMetadataDto FromEntity(DocumentMetadata metadata) => new(
        metadata.Title,
        metadata.Author,
        metadata.CreationDate,
        metadata.ModificationDate,
        metadata.Keywords,
        metadata.Encoding,
        metadata.IsAutoExtracted,
        metadata.RowVersion);
}

/// <summary>contracts/documents-api.md's per-language entry in <c>DocumentDetailDto.languages</c> (FR-024).</summary>
public sealed record DocumentLanguageDto(string LanguageCode, DocumentLanguageRole Role, decimal ConfidenceScore)
{
    public static DocumentLanguageDto FromEntity(DocumentLanguage language) => new(
        language.LanguageCode, language.Role, language.ConfidenceScore);
}

/// <summary>contracts/documents-api.md's <c>classification</c> shape (FR-025, FR-026).</summary>
public sealed record DocumentClassificationDto(Guid CategoryId, string CategoryName, DocumentClassificationSource Source, decimal? ConfidenceScore)
{
    public static DocumentClassificationDto FromEntity(DocumentClassification classification, string categoryName) => new(
        classification.CategoryId, categoryName, classification.Source, classification.ConfidenceScore);
}
