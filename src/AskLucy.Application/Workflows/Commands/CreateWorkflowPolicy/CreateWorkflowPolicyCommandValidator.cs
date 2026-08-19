using FluentValidation;

namespace AskLucy.Application.Workflows.Commands.CreateWorkflowPolicy;

public sealed class CreateWorkflowPolicyCommandValidator : AbstractValidator<CreateWorkflowPolicyCommand>
{
    public CreateWorkflowPolicyCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.UnderlyingToolName).MaximumLength(200);
        RuleFor(c => c.ConditionsJson).MaximumLength(4000);
        RuleFor(c => c)
            .Must(c => c.WorkflowNodeType is not null || !string.IsNullOrWhiteSpace(c.UnderlyingToolName))
            .WithMessage("A policy must target either a node type or an underlying tool.");
    }
}
