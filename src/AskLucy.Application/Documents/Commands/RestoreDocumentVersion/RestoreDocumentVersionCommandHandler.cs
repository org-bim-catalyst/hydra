using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.RestoreDocumentVersion;

/// <summary>FR-041 — repoints `Document.CurrentVersionId` to an earlier version; never deletes a version row. Rejects with <see cref="VersionUploadInProgressException"/> (409) while a replace-version upload is in flight for this document (Edge Cases).</summary>
public sealed class RestoreDocumentVersionCommandHandler(
    IDocumentRepository documentRepository,
    IDocumentUploadSessionRepository sessionRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RestoreDocumentVersionCommand, DocumentSummaryDto>
{
    public async Task<DocumentSummaryDto> Handle(RestoreDocumentVersionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        if (await sessionRepository.GetInProgressForDocumentAsync(document.Id, cancellationToken) is not null)
        {
            throw new VersionUploadInProgressException();
        }

        var version = await documentRepository.GetVersionByIdAsync(request.VersionId, cancellationToken);
        if (version is null || version.DocumentId != document.Id)
        {
            throw new KeyNotFoundException("Version not found.");
        }

        document.SetCurrentVersion(version.Id, version.SizeBytes, document.FileType, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DocumentSummaryDto.FromEntity(document);
    }
}
