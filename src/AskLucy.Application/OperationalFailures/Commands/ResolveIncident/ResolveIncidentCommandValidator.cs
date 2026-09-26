using AskLucy.Domain.OperationalFailures;
using FluentValidation;

namespace AskLucy.Application.OperationalFailures.Commands.ResolveIncident;

public sealed class ResolveIncidentCommandValidator : AbstractValidator<ResolveIncidentCommand>
{
    public ResolveIncidentCommandValidator()
    {
        RuleFor(c => c.IncidentId).NotEmpty();
        RuleFor(c => c.Note).MaximumLength(OperationalFailureIncident.MaxResolutionNoteLength);
    }
}
