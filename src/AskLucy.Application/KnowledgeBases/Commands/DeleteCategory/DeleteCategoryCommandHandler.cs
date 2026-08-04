using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.DeleteCategory;

/// <summary>
/// A category that's predefined, doesn't exist, or isn't owned by the caller all surface as
/// 404 (indistinguishable denial, same convention as <c>KnowledgeBaseOwnershipGuard</c>, FR-010
/// pattern). Clearing the FK on every referencing knowledge base and removing the category
/// happen in the same <see cref="IUnitOfWork.SaveChangesAsync"/> call (FR-021).
/// </summary>
public sealed class DeleteCategoryCommandHandler(
    IKnowledgeBaseCategoryRepository categoryRepository,
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeleteCategoryCommand>
{
    public async Task Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var category = await categoryRepository.GetByIdAsync(request.Id, cancellationToken);

        if (category is null || category.IsPredefined || category.OwnerId != userId)
        {
            throw new KeyNotFoundException("Category not found.");
        }

        var referencingKnowledgeBases = await knowledgeBaseRepository.ListByCategoryIdAsync(request.Id, cancellationToken);
        foreach (var knowledgeBase in referencingKnowledgeBases)
        {
            knowledgeBase.ClearCategory(userId);
        }

        categoryRepository.Remove(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
