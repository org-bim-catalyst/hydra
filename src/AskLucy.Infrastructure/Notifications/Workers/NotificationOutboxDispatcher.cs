using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Processing;
using AskLucy.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Notifications.Workers;

/// <summary>
/// The outbox relay loop (research R3, R4). Runs a dispatch pass, then waits for the wake signal or
/// the poll interval: the short one while there is work, the idle one when a pass found nothing. A
/// failed pass is logged and backed off, never allowed to end the loop.
/// </summary>
public sealed class NotificationOutboxDispatcher(
    OutboxDispatchService dispatchService,
    INotificationWakeSignal wakeSignal,
    NotificationWorkerHeartbeats heartbeats,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<NotificationsOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationOutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan MaxFailureBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Unique per process, so a restarted host never finishes events leased to its previous life.</summary>
    public string WorkerId { get; } = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop the loop first so nothing new is claimed, then hand back what this worker still
        // holds. ExecuteAsync may never have run on a fast start/stop, so this can't live there.
        await base.StopAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReleaseTimeout);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var released = await scope.ServiceProvider.GetRequiredService<INotificationOutboxStore>()
                .ReleaseClaimsAsync(WorkerId, timeout.Token);
            if (released > 0)
            {
                OutboxDispatcherLog.ClaimsReleased(logger, released, WorkerId);
            }
        }
        catch (Exception ex)
        {
            // The leases still expire on their own, so another worker picks the events up late.
            OutboxDispatcherLog.ReleaseFailed(logger, ex, WorkerId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OutboxDispatcherLog.Started(logger, WorkerId);
        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var dispatch = options.CurrentValue.Dispatch;
            TimeSpan wait;
            try
            {
                var claimed = await dispatchService.DispatchBatchAsync(WorkerId, stoppingToken);
                consecutiveFailures = 0;
                wait = claimed > 0
                    ? TimeSpan.FromSeconds(Math.Max(0, dispatch.PollIntervalSeconds))
                    : TimeSpan.FromSeconds(Math.Max(1, dispatch.IdlePollIntervalSeconds));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                wait = Backoff(consecutiveFailures, dispatch);
                OutboxDispatcherLog.PassFailed(logger, ex, WorkerId, consecutiveFailures, wait);
            }

            heartbeats.RecordDispatcher(timeProvider.GetUtcNow());

            try
            {
                if (wait > TimeSpan.Zero)
                {
                    await wakeSignal.WaitForDispatcherAsync(wait, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static TimeSpan Backoff(int consecutiveFailures, NotificationDispatchOptions dispatch)
    {
        var baseDelay = TimeSpan.FromSeconds(Math.Max(1, dispatch.IdlePollIntervalSeconds));
        var factor = Math.Pow(2, Math.Min(consecutiveFailures - 1, 6));
        var delay = TimeSpan.FromTicks((long)(baseDelay.Ticks * factor));
        return delay < MaxFailureBackoff ? delay : MaxFailureBackoff;
    }
}

internal static partial class OutboxDispatcherLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Notification outbox dispatcher {WorkerId} started.")]
    public static partial void Started(ILogger logger, string workerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification outbox dispatch pass failed on {WorkerId} ({ConsecutiveFailures} in a row); retrying in {Backoff}.")]
    public static partial void PassFailed(ILogger logger, Exception exception, string workerId, int consecutiveFailures, TimeSpan backoff);

    [LoggerMessage(Level = LogLevel.Information, Message = "Released {Count} claimed notification outbox event(s) held by {WorkerId} on shutdown.")]
    public static partial void ClaimsReleased(ILogger logger, int count, string workerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't release the notification outbox events held by {WorkerId} on shutdown; their leases will expire instead.")]
    public static partial void ReleaseFailed(ILogger logger, Exception exception, string workerId);
}
