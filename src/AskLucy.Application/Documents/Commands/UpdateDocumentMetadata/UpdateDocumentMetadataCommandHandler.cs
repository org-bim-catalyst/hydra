using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Commands.UpdateDocumentMetadata;

/// <summary>
/// FR-031, FR-031a — edits are last-write-wins with a stale-data warning, not a hard reject
/// (research.md Decision 9): a concurrent edit never loses data, the second caller just learns
/// their view was out of date after the fact.
/// </summary>
public sealed class UpdateDocumentMetadataCommandHandler(
    IDocumentRepository documentRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<UpdateDocumentMetadataCommand, UpdateDocumentMetadataResult>
{
    public async Task<UpdateDocumentMetadataResult> Handle(UpdateDocumentMetadataCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        var metadata = await documentRepository.GetMetadataByDocumentIdAsync(request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException("No metadata exists yet for this document — it may still be processing.");

        void ApplyEdit(Domain.Documents.DocumentMetadata m) =>
            m.ApplyUserEdit(request.Title, request.Author, request.CreationDate, request.ModificationDate, request.Keywords, userId);

        ApplyEdit(metadata);
        var wasStale = await documentRepository.SaveMetadataResolvingStalenessAsync(metadata, request.RowVersion, ApplyEdit, cancellationToken);

        return new UpdateDocumentMetadataResult(DocumentMetadataDto.FromEntity(metadata), wasStale);
    }
}
