using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Queries.GetDocument;

public sealed class GetDocumentQueryHandler(
    IDocumentRepository documentRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetDocumentQuery, DocumentDetailDto>
{
    public async Task<DocumentDetailDto> Handle(GetDocumentQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);
        var currentVersion = await documentRepository.GetVersionByIdAsync(document.CurrentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Current version not found.");

        var metadataEntity = await documentRepository.GetMetadataByDocumentIdAsync(document.Id, cancellationToken);
        var languages = await documentRepository.GetLanguagesByDocumentIdAsync(document.Id, cancellationToken);
        var classificationEntity = await documentRepository.GetClassificationByDocumentIdAsync(document.Id, cancellationToken);

        DocumentClassificationDto? classification = null;
        if (classificationEntity is not null)
        {
            var category = await documentRepository.GetCategoryByIdAsync(classificationEntity.CategoryId, cancellationToken);
            classification = DocumentClassificationDto.FromEntity(classificationEntity, category?.Name ?? "Unknown");
        }

        return DocumentDetailDto.FromEntity(
            document,
            currentVersion,
            metadataEntity is null ? null : DocumentMetadataDto.FromEntity(metadataEntity),
            languages.Select(DocumentLanguageDto.FromEntity).ToList(),
            classification);
    }
}
