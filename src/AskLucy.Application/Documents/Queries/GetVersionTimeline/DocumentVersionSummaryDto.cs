using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Queries.GetVersionTimeline;

/// <summary>contracts/document-versions-folders-api.md's version timeline shape (FR-040). <see cref="VersionLabel"/> is "{Major}.{Minor}".</summary>
public sealed record DocumentVersionSummaryDto(
    Guid Id, string VersionLabel, long SizeBytes, DateTime CreatedAtUtc, string CreatedByUserId, bool IsCurrent)
{
    public static DocumentVersionSummaryDto FromEntity(DocumentVersion version, Guid currentVersionId) => new(
        version.Id,
        $"{version.VersionMajor}.{version.VersionMinor}",
        version.SizeBytes,
        version.CreatedAtUtc,
        version.CreatedBy,
        version.Id == currentVersionId);
}
