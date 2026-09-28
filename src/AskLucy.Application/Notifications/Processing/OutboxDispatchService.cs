using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using AskLucy.Domain.OperationalFailures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Notifications.Processing;

/// <summary>
/// One pass of the outbox relay (research R3, R4): claim a batch, then process each event in its
/// own scope. A failed event is released with exponential backoff and, after
/// <see cref="NotificationOutboxEvent.MaxDispatchAttempts"/> attempts, put on the operational
/// failure trail. Called by the dispatcher <c>BackgroundService</c>.
/// </summary>
public sealed class OutboxDispatchService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationsOptions> options,
    IOperationalFailureRecorder failureRecorder,
    TimeProvider timeProvider,
    ILogger<OutboxDispatchService> logger)
{
    private const int MaxBatchSize = 500;
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(15);

    /// <summary>Returns how many events were claimed, so the caller can poll faster while there is work.</summary>
    public async Task<int> DispatchBatchAsync(string workerId, CancellationToken cancellationToken)
    {
        var dispatch = options.Value.Dispatch;
        var leaseExpires = Now().AddMinutes(Math.Max(1, dispatch.LeaseMinutes));

        IReadOnlyList<Guid> claimed;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            claimed = await scope.ServiceProvider.GetRequiredService<INotificationOutboxStore>()
                .ClaimBatchAsync(workerId, leaseExpires, Math.Clamp(dispatch.BatchSize, 1, MaxBatchSize), cancellationToken);
        }

        // On shutdown the loop stops between events; the dispatcher's StopAsync releases the rest.
        foreach (var eventId in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchOneAsync(eventId, workerId, cancellationToken);
        }

        return claimed.Count;
    }

    private async Task DispatchOneAsync(Guid eventId, string workerId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OutboxEventProcessor>().ProcessAsync(eventId, workerId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await ReleaseAsync(eventId, workerId, ex, cancellationToken);
        }
    }

    /// <summary>In a fresh scope, because the failed scope's tracked changes must not be saved.</summary>
    private async Task ReleaseAsync(Guid eventId, string workerId, Exception failure, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutboxStore>();
            var outboxEvent = await outbox.GetClaimedAsync(eventId, workerId, cancellationToken);
            if (outboxEvent is null)
            {
                // The lease expired mid-processing and another worker owns the event now.
                NotificationDispatchLog.DispatchFailedLeaseLost(logger, failure, eventId, workerId);
                return;
            }

            NotificationDispatchLog.DispatchFailed(
                logger, failure, outboxEvent.Type, outboxEvent.EventKey, outboxEvent.CorrelationId, outboxEvent.Id, outboxEvent.Attempts);

            var now = Now();
            outboxEvent.Release($"{failure.GetType().Name}: {failure.Message}", now + BackoffFor(outboxEvent.Attempts), now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);

            if (outboxEvent.Status == OutboxEventStatus.Failed)
            {
                NotificationDispatchLog.DispatchGaveUp(logger, outboxEvent.Type, outboxEvent.EventKey, outboxEvent.CorrelationId, outboxEvent.Id);
                failureRecorder.Record(new OperationalFailureReport
                {
                    Engine = OperationalFailureEngine.BackgroundJob,
                    Operation = "Notification dispatch",
                    Kind = OperationalFailureKind.JobFailedAfterRetries,
                    Reason = $"A {outboxEvent.Type} notification could not be dispatched after {NotificationOutboxEvent.MaxDispatchAttempts} attempts.",
                    Exception = failure,
                    CorrelationId = outboxEvent.CorrelationId,
                    OccurredAtUtc = now,
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception releaseFailure)
        {
            // The lease still expires, so the event is reclaimed later; nothing is lost, and both failures are logged.
            NotificationDispatchLog.ReleaseFailed(logger, releaseFailure, failure.GetType().Name, eventId);
        }
    }

    /// <summary>10 s, 20 s, 40 s … capped at 15 minutes.</summary>
    internal static TimeSpan BackoffFor(int attempts)
    {
        var exponent = Math.Clamp(attempts - 1, 0, 16);
        var delay = FirstRetryDelay * Math.Pow(2, exponent);
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
