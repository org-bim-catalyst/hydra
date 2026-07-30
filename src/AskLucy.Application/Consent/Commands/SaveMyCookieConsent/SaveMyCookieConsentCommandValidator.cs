using FluentValidation;

namespace AskLucy.Application.Consent.Commands.SaveMyCookieConsent;

public sealed class SaveMyCookieConsentCommandValidator : AbstractValidator<SaveMyCookieConsentCommand>
{
    public SaveMyCookieConsentCommandValidator()
    {
        RuleFor(c => c.Functional).NotNull();
        RuleFor(c => c.Analytics).NotNull();
        RuleFor(c => c.Marketing).NotNull();
    }
}
