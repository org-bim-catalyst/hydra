using FluentValidation;

namespace AskLucy.Application.Workflows.Commands.CreateWorkflow;

public sealed class CreateWorkflowCommandValidator : AbstractValidator<CreateWorkflowCommand>
{
    public CreateWorkflowCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(1000);
        RuleFor(c => c.WorkflowType).IsInEnum();
    }
}
