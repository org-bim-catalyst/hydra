using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Analytics.Commands.RecordFunnelEvent;

internal static partial class RecordFunnelEventCommandHandlerLog
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Funnel event recorded: {EventType} session={SessionId} cta={CtaId} funnel={FunnelType} occurredAtUtc={OccurredAtUtc}")]
    public static partial void FunnelEventRecorded(
        ILogger logger, FunnelEventType eventType, Guid sessionId, FunnelCtaId? ctaId, FunnelKind? funnelType, DateTime occurredAtUtc);
}

/// <summary>
/// Recorded via structured Serilog logging, not a database table (research.md Topic 4;
/// constitution §14 Observability already provides the sink) — funnel events are ephemeral
/// telemetry, not core business data requiring relational querying or GDPR-erasure
/// semantics. No repository, no <c>DbContext</c>, nothing to commit.
/// </summary>
public sealed class RecordFunnelEventCommandHandler(ILogger<RecordFunnelEventCommandHandler> logger)
    : IRequestHandler<RecordFunnelEventCommand>
{
    public Task Handle(RecordFunnelEventCommand request, CancellationToken cancellationToken)
    {
        RecordFunnelEventCommandHandlerLog.FunnelEventRecorded(
            logger, request.EventType, request.SessionId, request.CtaId, request.FunnelType, request.OccurredAtUtc);
        return Task.CompletedTask;
    }
}
