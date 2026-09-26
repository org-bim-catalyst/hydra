using FluentValidation;

namespace AskLucy.Application.OperationalFailures.Commands.AcknowledgeIncident;

public sealed class AcknowledgeIncidentCommandValidator : AbstractValidator<AcknowledgeIncidentCommand>
{
    public AcknowledgeIncidentCommandValidator()
    {
        RuleFor(c => c.IncidentId).NotEmpty();
    }
}
