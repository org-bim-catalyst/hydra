using FluentValidation;

namespace AskLucy.Application.Projects.Commands.RenameProject;

public sealed class RenameProjectCommandValidator : AbstractValidator<RenameProjectCommand>
{
    public RenameProjectCommandValidator()
    {
        RuleFor(c => c.ProjectId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
    }
}
