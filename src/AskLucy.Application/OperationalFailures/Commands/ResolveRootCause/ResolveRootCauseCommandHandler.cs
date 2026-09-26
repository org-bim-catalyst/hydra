using MediatR;

namespace AskLucy.Application.OperationalFailures.Commands.ResolveRootCause;

public sealed class ResolveRootCauseCommandHandler(IncidentTriageService triage) : IRequestHandler<ResolveRootCauseCommand, BulkTransitionResultDto>
{
    public Task<BulkTransitionResultDto> Handle(ResolveRootCauseCommand request, CancellationToken cancellationToken) =>
        triage.ResolveRootCauseAsync(request.RootCauseKey, request.Note, cancellationToken);
}
