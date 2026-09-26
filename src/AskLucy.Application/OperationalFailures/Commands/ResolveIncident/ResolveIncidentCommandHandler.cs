using MediatR;

namespace AskLucy.Application.OperationalFailures.Commands.ResolveIncident;

public sealed class ResolveIncidentCommandHandler(IncidentTriageService triage) : IRequestHandler<ResolveIncidentCommand>
{
    public Task Handle(ResolveIncidentCommand request, CancellationToken cancellationToken) =>
        triage.ResolveAsync(request.IncidentId, request.Note, cancellationToken);
}
