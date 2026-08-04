using FluentValidation;

namespace AskLucy.Application.KnowledgeBases.Commands.CreateFolder;

public sealed class CreateFolderCommandValidator : AbstractValidator<CreateFolderCommand>
{
    public CreateFolderCommandValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
}
