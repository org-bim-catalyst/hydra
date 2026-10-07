using FluentValidation;

namespace AskLucy.Application.Notifications.Commands.CreateTemplateDraft;

public sealed class CreateTemplateDraftCommandValidator : AbstractValidator<CreateTemplateDraftCommand>
{
    public CreateTemplateDraftCommandValidator()
    {
        RuleFor(c => c.TemplateId).NotEmpty();
        RuleFor(c => c).Must(c => c.Content is not null || c.CopyFromVersionId is not null)
            .WithName("content")
            .WithMessage("Send the version's fields, or the version to copy.");
    }
}
