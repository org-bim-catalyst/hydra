using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.OverrideClassification;

/// <summary>FR-026 — sets <see cref="DocumentClassificationSource.UserOverride"/>, permanently distinguishing it from an automatic assignment even after the override.</summary>
public sealed class OverrideClassificationCommandHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<OverrideClassificationCommand, DocumentClassificationDto>
{
    public async Task<DocumentClassificationDto> Handle(OverrideClassificationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        var category = await documentRepository.GetCategoryByIdAsync(request.CategoryId, cancellationToken)
            ?? throw new KeyNotFoundException("Category not found.");

        var classification = await documentRepository.GetClassificationByDocumentIdAsync(request.DocumentId, cancellationToken);
        if (classification is null)
        {
            classification = DocumentClassification.CreateUserOverride(request.DocumentId, request.CategoryId, userId);
            documentRepository.AddClassification(classification);
        }
        else
        {
            classification.ApplyOverride(request.CategoryId, userId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DocumentClassificationDto.FromEntity(classification, category.Name);
    }
}
