using AskLucy.Application.Analytics.Commands.RecordFunnelEvent;

namespace AskLucy.Web.Contracts;

/// <summary>
/// One consent-gated funnel/CTA analytics event (contracts/analytics-funnel-events-api.md).
/// Anonymous-allowed — this is called from the public landing/auth pages before any session
/// exists, so no auth-derived identity is available or expected in the payload.
/// </summary>
public sealed record RecordFunnelEventRequest(
    FunnelEventType EventType,
    FunnelCtaId? CtaId,
    FunnelKind? FunnelType,
    Guid SessionId,
    DateTime OccurredAtUtc);
