using FluentValidation;

namespace AskLucy.Application.Localization.Commands.UpdateLocalizationSettings;

public sealed class UpdateLocalizationSettingsCommandValidator : AbstractValidator<UpdateLocalizationSettingsCommand>
{
    public UpdateLocalizationSettingsCommandValidator()
    {
        RuleFor(c => c.SupportedLanguages).NotNull();
        RuleFor(c => c.ExpectedRowVersion).NotEmpty().WithMessage("The row version the change is based on is required.");
    }
}
