using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents;

/// <summary>contracts/documents-api.md's list/detail summary shape. <c>CategoryName</c>/<c>LanguagePrimary</c> are populated once US2/US3 processing has run — null on a freshly uploaded document.</summary>
public sealed record DocumentSummaryDto(
    Guid Id,
    string FileName,
    DocumentFileType FileType,
    long SizeBytes,
    DocumentProcessingStatus ProcessingStatus,
    Guid? FolderId,
    string? CategoryName,
    string? LanguagePrimary,
    IReadOnlyList<string> Tags,
    bool IsArchived,
    DateTime CreatedAtUtc,
    DateTime? LastUpdatedAtUtc)
{
    public static DocumentSummaryDto FromEntity(Document document) => new(
        document.Id,
        document.FileName,
        document.FileType,
        document.SizeBytes,
        document.ProcessingStatus,
        document.FolderId,
        CategoryName: null,
        LanguagePrimary: null,
        document.Tags.Select(t => t.Name).ToList(),
        document.ArchivedAtUtc is not null,
        document.CreatedAtUtc,
        document.ModifiedAtUtc);
}
