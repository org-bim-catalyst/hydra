using FluentValidation;

namespace AskLucy.Application.Notifications.Commands.PublishTemplateVersion;

public sealed class PublishTemplateVersionCommandValidator : AbstractValidator<PublishTemplateVersionCommand>
{
    public PublishTemplateVersionCommandValidator()
    {
        RuleFor(c => c.TemplateId).NotEmpty();
        RuleFor(c => c.VersionId).NotEmpty();
        RuleFor(c => c.ExpectedRowVersion).NotEmpty().WithMessage("The row version the action is based on is required.");
    }
}
