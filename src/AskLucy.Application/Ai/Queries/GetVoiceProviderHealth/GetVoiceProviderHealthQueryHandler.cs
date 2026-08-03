using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetVoiceProviderHealth;

public sealed class GetVoiceProviderHealthQueryHandler(IVoiceProviderFailoverEventRepository failoverEvents)
    : IRequestHandler<GetVoiceProviderHealthQuery, VoiceProviderHealthDto>
{
    public async Task<VoiceProviderHealthDto> Handle(GetVoiceProviderHealthQuery request, CancellationToken cancellationToken)
    {
        var toUtc = request.ToUtc ?? DateTime.UtcNow;
        var fromUtc = request.FromUtc ?? toUtc.AddHours(-24);

        var events = await failoverEvents.GetEventsAsync(fromUtc, toUtc, cancellationToken);
        var ordered = events.OrderBy(e => e.OccurredAtUtc).ToList();

        var failoverCount = ordered.Count(e => e.Direction == VoiceProviderFailoverDirection.FailedOverToFallback);
        var recoveryCount = ordered.Count(e => e.Direction == VoiceProviderFailoverDirection.RecoveredToPrimary);

        // contracts/voice-provider-health.md: degraded only if the most recent event in the
        // window is an unresolved failover — a failover immediately followed by a recovery is
        // a healthy blip, not an ongoing outage.
        var currentStatus = ordered.Count > 0 && ordered[^1].Direction == VoiceProviderFailoverDirection.FailedOverToFallback
            ? "degraded"
            : "healthy";

        return new VoiceProviderHealthDto(
            currentStatus,
            failoverCount,
            recoveryCount,
            [.. ordered.Select(e => new VoiceProviderFailoverEventDto(e.OccurredAtUtc, e.Direction.ToString(), e.Reason))]);
    }
}
