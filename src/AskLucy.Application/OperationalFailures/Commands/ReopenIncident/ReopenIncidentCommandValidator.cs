using FluentValidation;

namespace AskLucy.Application.OperationalFailures.Commands.ReopenIncident;

public sealed class ReopenIncidentCommandValidator : AbstractValidator<ReopenIncidentCommand>
{
    public ReopenIncidentCommandValidator()
    {
        RuleFor(c => c.IncidentId).NotEmpty();
    }
}
