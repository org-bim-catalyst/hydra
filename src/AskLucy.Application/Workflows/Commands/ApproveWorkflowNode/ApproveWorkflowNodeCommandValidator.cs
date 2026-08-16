using FluentValidation;

namespace AskLucy.Application.Workflows.Commands.ApproveWorkflowNode;

public sealed class ApproveWorkflowNodeCommandValidator : AbstractValidator<ApproveWorkflowNodeCommand>
{
    public ApproveWorkflowNodeCommandValidator()
    {
        RuleFor(c => c.WorkflowExecutionId).NotEqual(Guid.Empty);
        RuleFor(c => c.ApprovalId).NotEqual(Guid.Empty);
    }
}
