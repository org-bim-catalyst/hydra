using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.CreateCustomCategory;

public sealed class CreateCustomCategoryCommandHandler(
    IKnowledgeBaseCategoryRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateCustomCategoryCommand, KnowledgeBaseCategoryDto>
{
    public async Task<KnowledgeBaseCategoryDto> Handle(CreateCustomCategoryCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        if (await repository.ExistsByNameForOwnerAsync(userId, request.Name.Trim(), cancellationToken))
        {
            throw new DuplicateResourceException($"You already have a category named '{request.Name.Trim()}'.");
        }

        var category = KnowledgeBaseCategory.CreateCustom(request.Name, userId, userId);
        repository.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseCategoryDto.FromEntity(category);
    }
}
