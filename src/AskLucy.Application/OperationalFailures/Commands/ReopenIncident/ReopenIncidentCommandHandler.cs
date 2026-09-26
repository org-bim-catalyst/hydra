using MediatR;

namespace AskLucy.Application.OperationalFailures.Commands.ReopenIncident;

public sealed class ReopenIncidentCommandHandler(IncidentTriageService triage) : IRequestHandler<ReopenIncidentCommand>
{
    public Task Handle(ReopenIncidentCommand request, CancellationToken cancellationToken) =>
        triage.ReopenAsync(request.IncidentId, cancellationToken);
}
