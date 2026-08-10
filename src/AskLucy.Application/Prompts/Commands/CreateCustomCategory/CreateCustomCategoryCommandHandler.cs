using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.CreateCustomCategory;

public sealed class CreateCustomCategoryCommandHandler(
    IPromptCategoryRepository repository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<CreateCustomCategoryCommand, PromptCategoryDto>
{
    public async Task<PromptCategoryDto> Handle(CreateCustomCategoryCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        if (await repository.GetCustomByOwnerAndNameAsync(userId, request.Name.Trim(), cancellationToken) is not null)
        {
            throw new DuplicateResourceException($"You already have a category named '{request.Name.Trim()}'.");
        }

        var category = PromptCategory.CreateCustom(request.Name, userId, userId);
        repository.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PromptCategoryDto.FromEntity(category);
    }
}
