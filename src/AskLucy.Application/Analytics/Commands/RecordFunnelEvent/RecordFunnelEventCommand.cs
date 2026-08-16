using MediatR;

namespace AskLucy.Application.Analytics.Commands.RecordFunnelEvent;

public enum FunnelEventType
{
    CtaClicked,
    FunnelCompleted,
}

public enum FunnelCtaId
{
    SignIn,
    SignUp,
    TryPlatform,
}

public enum FunnelKind
{
    SignUp,
    SignIn,
}

/// <summary>
/// Records one consent-gated funnel/CTA analytics event from the public landing/auth pages
/// (specs/023-flumeria-landing-experience, contracts/analytics-funnel-events-api.md).
/// Write-only telemetry — no query surface, no persisted entity (data-model.md
/// FunnelAnalyticsEvent; research.md Topic 4). <paramref name="SessionId"/> is a
/// client-generated, ephemeral correlation id, never derived from or linked to a
/// <c>UserId</c> — this event carries no PII.
/// </summary>
public sealed record RecordFunnelEventCommand(
    FunnelEventType EventType,
    FunnelCtaId? CtaId,
    FunnelKind? FunnelType,
    Guid SessionId,
    DateTime OccurredAtUtc) : IRequest;
