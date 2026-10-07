using FluentValidation;

namespace AskLucy.Application.Notifications.Commands.UpdateTemplateDraft;

public sealed class UpdateTemplateDraftCommandValidator : AbstractValidator<UpdateTemplateDraftCommand>
{
    public UpdateTemplateDraftCommandValidator()
    {
        RuleFor(c => c.TemplateId).NotEmpty();
        RuleFor(c => c.VersionId).NotEmpty();
        RuleFor(c => c.Content).NotNull();
        RuleFor(c => c.ExpectedRowVersion).NotEmpty().WithMessage("The row version the edit is based on is required.");
    }
}
