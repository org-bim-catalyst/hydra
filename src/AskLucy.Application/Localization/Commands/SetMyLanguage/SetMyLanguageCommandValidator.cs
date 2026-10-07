using FluentValidation;

namespace AskLucy.Application.Localization.Commands.SetMyLanguage;

public sealed class SetMyLanguageCommandValidator : AbstractValidator<SetMyLanguageCommand>
{
    public SetMyLanguageCommandValidator() =>
        RuleFor(c => c.PreferredLanguage).NotEmpty().MaximumLength(10);
}
