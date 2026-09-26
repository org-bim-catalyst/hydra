using MediatR;

namespace AskLucy.Application.OperationalFailures.Commands.AcknowledgeRootCause;

public sealed class AcknowledgeRootCauseCommandHandler(IncidentTriageService triage) : IRequestHandler<AcknowledgeRootCauseCommand, BulkTransitionResultDto>
{
    public Task<BulkTransitionResultDto> Handle(AcknowledgeRootCauseCommand request, CancellationToken cancellationToken) =>
        triage.AcknowledgeRootCauseAsync(request.RootCauseKey, cancellationToken);
}
