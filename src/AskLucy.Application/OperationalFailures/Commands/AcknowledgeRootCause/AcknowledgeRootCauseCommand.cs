using MediatR;

namespace AskLucy.Application.OperationalFailures.Commands.AcknowledgeRootCause;

/// <summary>specs/074 FR-026b — acknowledges every unresolved incident sharing a root cause.</summary>
public sealed record AcknowledgeRootCauseCommand(string RootCauseKey) : IRequest<BulkTransitionResultDto>;
