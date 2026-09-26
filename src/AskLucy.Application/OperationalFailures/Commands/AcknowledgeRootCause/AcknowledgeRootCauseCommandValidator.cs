using FluentValidation;

namespace AskLucy.Application.OperationalFailures.Commands.AcknowledgeRootCause;

public sealed class AcknowledgeRootCauseCommandValidator : AbstractValidator<AcknowledgeRootCauseCommand>
{
    public AcknowledgeRootCauseCommandValidator()
    {
        RuleFor(c => c.RootCauseKey).NotEmpty().MaximumLength(64);
    }
}
