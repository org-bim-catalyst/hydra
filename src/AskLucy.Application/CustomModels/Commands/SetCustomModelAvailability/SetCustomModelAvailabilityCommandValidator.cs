using FluentValidation;

namespace AskLucy.Application.CustomModels.Commands.SetCustomModelAvailability;

public sealed class SetCustomModelAvailabilityCommandValidator : AbstractValidator<SetCustomModelAvailabilityCommand>
{
    public SetCustomModelAvailabilityCommandValidator()
    {
        RuleFor(c => c.Availability).IsInEnum().OverridePropertyName("availability");
    }
}
