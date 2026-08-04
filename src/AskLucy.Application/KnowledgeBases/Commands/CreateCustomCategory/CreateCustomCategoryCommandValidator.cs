using FluentValidation;

namespace AskLucy.Application.KnowledgeBases.Commands.CreateCustomCategory;

public sealed class CreateCustomCategoryCommandValidator : AbstractValidator<CreateCustomCategoryCommand>
{
    public CreateCustomCategoryCommandValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
}
