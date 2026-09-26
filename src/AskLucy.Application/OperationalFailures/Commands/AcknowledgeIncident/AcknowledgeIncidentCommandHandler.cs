using MediatR;

namespace AskLucy.Application.OperationalFailures.Commands.AcknowledgeIncident;

public sealed class AcknowledgeIncidentCommandHandler(IncidentTriageService triage) : IRequestHandler<AcknowledgeIncidentCommand>
{
    public Task Handle(AcknowledgeIncidentCommand request, CancellationToken cancellationToken) =>
        triage.AcknowledgeAsync(request.IncidentId, cancellationToken);
}
