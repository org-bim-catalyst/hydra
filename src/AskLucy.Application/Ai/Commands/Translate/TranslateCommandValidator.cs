using FluentValidation;

namespace AskLucy.Application.Ai.Commands.Translate;

public sealed class TranslateCommandValidator : AbstractValidator<TranslateCommand>
{
    public TranslateCommandValidator()
    {
        RuleFor(c => c.Text).NotEmpty();
        RuleFor(c => c.TargetLanguage).NotEmpty();
    }
}
