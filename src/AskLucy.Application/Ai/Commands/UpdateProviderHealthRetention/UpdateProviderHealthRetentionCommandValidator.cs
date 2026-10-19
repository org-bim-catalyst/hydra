using AskLucy.Domain.Ai;
using FluentValidation;

namespace AskLucy.Application.Ai.Commands.UpdateProviderHealthRetention;

public sealed class UpdateProviderHealthRetentionCommandValidator : AbstractValidator<UpdateProviderHealthRetentionCommand>
{
    public UpdateProviderHealthRetentionCommandValidator()
    {
        RuleFor(c => c.RetentionDays)
            .InclusiveBetween(ProviderHealthRetentionSetting.MinRetentionDays, ProviderHealthRetentionSetting.MaxRetentionDays)
            .WithMessage($"retentionDays must be between {ProviderHealthRetentionSetting.MinRetentionDays} and {ProviderHealthRetentionSetting.MaxRetentionDays}.");

        RuleFor(c => c.ExpectedRowVersion)
            .NotEmpty()
            .WithMessage("The row version the change is based on is required.");
    }
}
