using FluentValidation;

namespace AskLucy.Application.Agents.Commands.CreateAgentPolicy;

public sealed class CreateAgentPolicyCommandValidator : AbstractValidator<CreateAgentPolicyCommand>
{
    public CreateAgentPolicyCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.ToolName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ConditionsJson).MaximumLength(4000);
    }
}
