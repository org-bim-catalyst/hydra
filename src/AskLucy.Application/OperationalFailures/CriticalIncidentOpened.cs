using AskLucy.Domain.OperationalFailures;
using MediatR;

namespace AskLucy.Application.OperationalFailures;

/// <summary>
/// A Critical incident was just opened (specs/074 research D18, FR-030). Published from the
/// background writer's scope, never from a user's request. No handler is registered yet: this is
/// the seam a future email or paging notifier subscribes to without touching any recording site.
/// </summary>
public sealed record CriticalIncidentOpened(
    Guid IncidentId,
    OperationalFailureEngine Engine,
    OperationalFailureKind Kind,
    string? ProviderName,
    string CorrelationId) : INotification;
