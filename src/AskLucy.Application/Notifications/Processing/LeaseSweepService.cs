using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Notifications.Processing;

/// <summary>What one sweep found.</summary>
/// <param name="AmbiguousDeliveries">Deliveries left <c>Sending</c> past their lease, now failed as ambiguous.</param>
/// <param name="ReleasedEvents">Outbox events left <c>Processing</c> past their lease, returned to pending.</param>
public sealed record LeaseSweepResult(int AmbiguousDeliveries, int ReleasedEvents);

/// <summary>
/// The once-a-minute recovery pass (research R4, R5). A delivery still sending when its lease ran out
/// crashed mid-send, so it may or may not have reached the recipient: it is failed as
/// <c>AmbiguousOutcome</c> and never resent automatically, because a duplicate email is worse than one an
/// administrator retries on purpose. Outbox events past their lease are returned to pending so a crashed
/// dispatcher's work shows as waiting.
/// </summary>
public sealed class LeaseSweepService(
    IServiceScopeFactory scopeFactory,
    IOperationalFailureRecorder failureRecorder,
    TimeProvider timeProvider,
    ILogger<LeaseSweepService> logger)
{
    public async Task<LeaseSweepResult> SweepAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var scope = scopeFactory.CreateAsyncScope();
        var ambiguous = await scope.ServiceProvider.GetRequiredService<INotificationRepository>().SweepExpiredLeasesAsync(now, cancellationToken);
        var released = await scope.ServiceProvider.GetRequiredService<INotificationOutboxStore>().SweepExpiredLeasesAsync(now, cancellationToken);

        if (ambiguous > 0 || released > 0)
        {
            DeliveryLog.Swept(logger, ambiguous, released);
        }

        if (ambiguous > 0)
        {
            failureRecorder.Record(new OperationalFailureReport
            {
                Engine = OperationalFailureEngine.BackgroundJob,
                Operation = "Notification delivery",
                Kind = OperationalFailureKind.UnexpectedError,
                Reason = $"{ambiguous} notification delivery(ies) were left in an unknown state because a worker stopped mid-send; they need an administrator's decision.",
                CorrelationId = $"lease-sweep-{now:yyyyMMddHHmm}",
                OccurredAtUtc = now,
            });
        }

        return new LeaseSweepResult(ambiguous, released);
    }
}
