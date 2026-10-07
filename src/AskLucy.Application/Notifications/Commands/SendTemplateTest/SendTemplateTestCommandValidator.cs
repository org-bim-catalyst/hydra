using FluentValidation;

namespace AskLucy.Application.Notifications.Commands.SendTemplateTest;

public sealed class SendTemplateTestCommandValidator : AbstractValidator<SendTemplateTestCommand>
{
    public SendTemplateTestCommandValidator()
    {
        RuleFor(c => c.TemplateId).NotEmpty();
        RuleFor(c => c.VersionId).NotEmpty();
    }
}
