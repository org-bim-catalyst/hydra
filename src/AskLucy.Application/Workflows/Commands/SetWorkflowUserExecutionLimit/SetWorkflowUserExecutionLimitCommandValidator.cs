using FluentValidation;

namespace AskLucy.Application.Workflows.Commands.SetWorkflowUserExecutionLimit;

public sealed class SetWorkflowUserExecutionLimitCommandValidator : AbstractValidator<SetWorkflowUserExecutionLimitCommand>
{
    public SetWorkflowUserExecutionLimitCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.MaxConcurrentExecutions).GreaterThanOrEqualTo(1);
    }
}
