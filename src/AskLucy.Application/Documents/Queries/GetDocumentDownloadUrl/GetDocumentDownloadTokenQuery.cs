using MediatR;

namespace AskLucy.Application.Documents.Queries.GetDocumentDownloadUrl;

/// <summary>
/// Ownership-checked lookup of what to sign for a download link (FR-015, FR-018, FR-050) —
/// the actual <c>ISignedUrlService.Sign</c> call and URL construction happen in the controller,
/// mirroring <c>UsersController</c>'s avatar download pattern (signing/URL-building is a
/// Presentation-layer concern, not an Application one).
/// </summary>
public sealed record GetDocumentDownloadTokenQuery(Guid DocumentId, Guid? VersionId) : IRequest<DocumentDownloadTokenDto>;

public sealed record DocumentDownloadTokenDto(Guid VersionId, string OriginalFileName);
