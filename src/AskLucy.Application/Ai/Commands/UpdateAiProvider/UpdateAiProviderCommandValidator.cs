using FluentValidation;

namespace AskLucy.Application.Ai.Commands.UpdateAiProvider;

public sealed class UpdateAiProviderCommandValidator : AbstractValidator<UpdateAiProviderCommand>
{
    public UpdateAiProviderCommandValidator()
    {
        RuleFor(c => c.ProviderId).NotEmpty();
    }
}
