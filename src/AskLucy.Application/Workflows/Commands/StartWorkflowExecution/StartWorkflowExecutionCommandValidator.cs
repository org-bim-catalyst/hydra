using FluentValidation;

namespace AskLucy.Application.Workflows.Commands.StartWorkflowExecution;

public sealed class StartWorkflowExecutionCommandValidator : AbstractValidator<StartWorkflowExecutionCommand>
{
    public StartWorkflowExecutionCommandValidator()
    {
        RuleFor(c => c.InputsJson).NotEmpty();
        RuleFor(c => c.WorkflowVersionNumber).GreaterThan(0).When(c => c.WorkflowVersionNumber is not null);
        RuleFor(c => c.TriggerType).IsInEnum();
    }
}
