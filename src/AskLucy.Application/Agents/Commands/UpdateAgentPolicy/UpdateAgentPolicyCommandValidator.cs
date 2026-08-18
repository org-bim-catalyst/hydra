using FluentValidation;

namespace AskLucy.Application.Agents.Commands.UpdateAgentPolicy;

public sealed class UpdateAgentPolicyCommandValidator : AbstractValidator<UpdateAgentPolicyCommand>
{
    public UpdateAgentPolicyCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.ConditionsJson).MaximumLength(4000);
    }
}
