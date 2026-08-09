using FluentValidation;

namespace AskLucy.Application.Projects.Commands.CreateProject;

public sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator() =>
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
}
