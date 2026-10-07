using FluentValidation;

namespace AskLucy.Application.Notifications.Commands.ArchiveTemplateVersion;

public sealed class ArchiveTemplateVersionCommandValidator : AbstractValidator<ArchiveTemplateVersionCommand>
{
    public ArchiveTemplateVersionCommandValidator()
    {
        RuleFor(c => c.TemplateId).NotEmpty();
        RuleFor(c => c.VersionId).NotEmpty();
        RuleFor(c => c.ExpectedRowVersion).NotEmpty().WithMessage("The row version the action is based on is required.");
    }
}
