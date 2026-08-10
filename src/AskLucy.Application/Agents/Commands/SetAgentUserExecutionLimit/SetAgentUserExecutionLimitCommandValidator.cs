using FluentValidation;

namespace AskLucy.Application.Agents.Commands.SetAgentUserExecutionLimit;

public sealed class SetAgentUserExecutionLimitCommandValidator : AbstractValidator<SetAgentUserExecutionLimitCommand>
{
    public SetAgentUserExecutionLimitCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.MaxConcurrentExecutions).GreaterThanOrEqualTo(1);
    }
}
