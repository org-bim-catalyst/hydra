using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Processing;
using AskLucy.Application.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Notifications.Workers;

/// <summary>
/// The delivery relay loop (research R4, R5). Runs a delivery pass, then waits for the wake signal or
/// the poll interval: the short one while there is work, the idle one when a pass found nothing, and the
/// channel's own wait when its send limiter ran dry. A failed pass is logged and backed off, never
/// allowed to end the loop. The same loop, scope, heartbeat and error rules as
/// <see cref="NotificationOutboxDispatcher"/>.
/// </summary>
public sealed class NotificationDeliveryWorker(
    DeliveryProcessingService processingService,
    INotificationWakeSignal wakeSignal,
    NotificationWorkerHeartbeats heartbeats,
    IOptionsMonitor<NotificationsOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationDeliveryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan MaxFailureBackoff = TimeSpan.FromMinutes(1);

    /// <summary>Unique per process, so a restarted host never finishes deliveries leased to its previous life.</summary>
    public string WorkerId { get; } = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop the loop first so nothing new is claimed, then hand back what this worker still holds
        // unsent. ExecuteAsync may never have run on a fast start/stop, so this can't live only there
        // (standing rule 10). A delivery interrupted mid-send is deliberately not released: its outcome
        // is unknown, and the lease sweeper records it as ambiguous.
        await base.StopAsync(cancellationToken);
        await processingService.ReleaseUnstartedAsync(WorkerId, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DeliveryWorkerLog.Started(logger, WorkerId);
        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var dispatch = options.CurrentValue.Dispatch;
            TimeSpan wait;
            try
            {
                var batch = await processingService.ProcessBatchAsync(WorkerId, stoppingToken);
                consecutiveFailures = 0;
                wait = batch.DeferredFor
                    ?? (batch.Claimed > 0
                        ? TimeSpan.FromSeconds(Math.Max(0, dispatch.PollIntervalSeconds))
                        : TimeSpan.FromSeconds(Math.Max(1, dispatch.IdlePollIntervalSeconds)));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                wait = Backoff(consecutiveFailures, dispatch);
                DeliveryWorkerLog.PassFailed(logger, ex, WorkerId, consecutiveFailures, wait);
            }

            heartbeats.RecordDeliveryWorker(timeProvider.GetUtcNow());

            try
            {
                if (wait > TimeSpan.Zero)
                {
                    await wakeSignal.WaitForDeliveryWorkerAsync(wait, stoppingToken);
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

internal static partial class DeliveryWorkerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Notification delivery worker {WorkerId} started.")]
    public static partial void Started(ILogger logger, string workerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification delivery pass failed on {WorkerId} ({ConsecutiveFailures} in a row); retrying in {Backoff}.")]
    public static partial void PassFailed(ILogger logger, Exception exception, string workerId, int consecutiveFailures, TimeSpan backoff);
}
