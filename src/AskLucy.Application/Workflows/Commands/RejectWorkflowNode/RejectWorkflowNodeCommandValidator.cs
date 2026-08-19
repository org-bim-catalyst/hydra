using FluentValidation;

namespace AskLucy.Application.Workflows.Commands.RejectWorkflowNode;

public sealed class RejectWorkflowNodeCommandValidator : AbstractValidator<RejectWorkflowNodeCommand>
{
    public RejectWorkflowNodeCommandValidator()
    {
        RuleFor(c => c.WorkflowExecutionId).NotEqual(Guid.Empty);
        RuleFor(c => c.ApprovalId).NotEqual(Guid.Empty);
        RuleFor(c => c.Reason).MaximumLength(2000);
    }
}
