using AskLucy.Domain.OperationalFailures;
using FluentValidation;

namespace AskLucy.Application.OperationalFailures.Commands.ResolveRootCause;

public sealed class ResolveRootCauseCommandValidator : AbstractValidator<ResolveRootCauseCommand>
{
    public ResolveRootCauseCommandValidator()
    {
        RuleFor(c => c.RootCauseKey).NotEmpty().MaximumLength(64);
        RuleFor(c => c.Note).MaximumLength(OperationalFailureIncident.MaxResolutionNoteLength);
    }
}
