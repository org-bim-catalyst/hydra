using System.Globalization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Queries.CompareVersions;

public sealed class CompareVersionsQueryHandler(IDocumentRepository documentRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<CompareVersionsQuery, DocumentVersionCompareDto>
{
    public async Task<DocumentVersionCompareDto> Handle(CompareVersionsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        var fromVersion = await GetOwnedVersionAsync(documentRepository, document.Id, request.FromVersionId, cancellationToken);
        var toVersion = await GetOwnedVersionAsync(documentRepository, document.Id, request.ToVersionId, cancellationToken);

        var extractedTextDiff = LineDiff.Compute(
            fromVersion.ExtractedText ?? fromVersion.OcrTextRaw, toVersion.ExtractedText ?? toVersion.OcrTextRaw);

        var metadataDiff = new Dictionary<string, MetadataFieldDiff>();
        AddIfDifferent(metadataDiff, "originalFileName", fromVersion.OriginalFileName, toVersion.OriginalFileName);
        AddIfDifferent(metadataDiff, "sizeBytes", fromVersion.SizeBytes.ToString(CultureInfo.InvariantCulture), toVersion.SizeBytes.ToString(CultureInfo.InvariantCulture));
        AddIfDifferent(metadataDiff, "pageCount", fromVersion.PageCount?.ToString(CultureInfo.InvariantCulture), toVersion.PageCount?.ToString(CultureInfo.InvariantCulture));

        return new DocumentVersionCompareDto(extractedTextDiff, metadataDiff);
    }

    private static async Task<DocumentVersion> GetOwnedVersionAsync(
        IDocumentRepository documentRepository, Guid documentId, Guid versionId, CancellationToken cancellationToken)
    {
        var version = await documentRepository.GetVersionByIdAsync(versionId, cancellationToken);
        if (version is null || version.DocumentId != documentId)
        {
            throw new KeyNotFoundException("Version not found.");
        }

        return version;
    }

    private static void AddIfDifferent(Dictionary<string, MetadataFieldDiff> diff, string key, string? from, string? to)
    {
        if (from != to)
        {
            diff[key] = new MetadataFieldDiff(from, to);
        }
    }
}
