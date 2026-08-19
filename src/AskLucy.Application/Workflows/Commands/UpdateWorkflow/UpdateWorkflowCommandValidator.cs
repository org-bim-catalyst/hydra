using FluentValidation;

namespace AskLucy.Application.Workflows.Commands.UpdateWorkflow;

public sealed class UpdateWorkflowCommandValidator : AbstractValidator<UpdateWorkflowCommand>
{
    public UpdateWorkflowCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(1000);
        RuleFor(c => c.DraftDefinitionJson).NotEmpty();
    }
}
