using MediatR;

namespace AskLucy.Application.OperationalFailures.Commands.ResolveRootCause;

/// <summary>specs/074 FR-026b, SC-011 — resolves every unresolved incident sharing a root cause in one action.</summary>
public sealed record ResolveRootCauseCommand(string RootCauseKey, string? Note) : IRequest<BulkTransitionResultDto>;
