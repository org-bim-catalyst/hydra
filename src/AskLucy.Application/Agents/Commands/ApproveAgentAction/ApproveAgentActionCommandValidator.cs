using FluentValidation;

namespace AskLucy.Application.Agents.Commands.ApproveAgentAction;

public sealed class ApproveAgentActionCommandValidator : AbstractValidator<ApproveAgentActionCommand>
{
    public ApproveAgentActionCommandValidator()
    {
        RuleFor(c => c.AgentExecutionId).NotEqual(Guid.Empty);
        RuleFor(c => c.ApprovalId).NotEqual(Guid.Empty);
    }
}
