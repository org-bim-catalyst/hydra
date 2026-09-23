using FluentValidation;

namespace AskLucy.Application.Ai.Commands.AddVoiceProvider;

public sealed class AddVoiceProviderCommandValidator : AbstractValidator<AddVoiceProviderCommand>
{
    public AddVoiceProviderCommandValidator()
    {
        RuleFor(c => c.ProviderKey).NotEmpty().MaximumLength(50);
        RuleFor(c => c.ApiKey).MaximumLength(500);
    }
}
