using FluentValidation;

namespace AskLucy.Application.Agents.Commands.RejectAgentAction;

public sealed class RejectAgentActionCommandValidator : AbstractValidator<RejectAgentActionCommand>
{
    public RejectAgentActionCommandValidator()
    {
        RuleFor(c => c.AgentExecutionId).NotEqual(Guid.Empty);
        RuleFor(c => c.ApprovalId).NotEqual(Guid.Empty);
        RuleFor(c => c.Reason).MaximumLength(2000);
    }
}
