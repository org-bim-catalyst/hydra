using FluentValidation;

namespace AskLucy.Application.Workflows.Commands.UpdateWorkflowPolicy;

public sealed class UpdateWorkflowPolicyCommandValidator : AbstractValidator<UpdateWorkflowPolicyCommand>
{
    public UpdateWorkflowPolicyCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.ConditionsJson).MaximumLength(4000);
    }
}
