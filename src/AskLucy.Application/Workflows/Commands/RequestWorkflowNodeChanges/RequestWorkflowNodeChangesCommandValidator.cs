using FluentValidation;

namespace AskLucy.Application.Workflows.Commands.RequestWorkflowNodeChanges;

public sealed class RequestWorkflowNodeChangesCommandValidator : AbstractValidator<RequestWorkflowNodeChangesCommand>
{
    public RequestWorkflowNodeChangesCommandValidator()
    {
        RuleFor(c => c.WorkflowExecutionId).NotEqual(Guid.Empty);
        RuleFor(c => c.ApprovalId).NotEqual(Guid.Empty);
        RuleFor(c => c.Comments).NotEmpty().MaximumLength(2000);
    }
}
