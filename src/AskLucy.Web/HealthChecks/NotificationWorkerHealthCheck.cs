using AskLucy.Application.Options;
using AskLucy.Infrastructure.Notifications.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AskLucy.Web.HealthChecks;

/// <summary>
/// Shared body of the two worker checks (FR-057, research R21): a worker that hasn't finished a loop iteration within the
/// stale window is unhealthy. A worker that hasn't had a first iteration yet is given that same window from the moment the
/// process started, so a deployment doesn't flap.
/// </summary>
public abstract class NotificationWorkerHealthCheck(
    string worker,
    NotificationWorkerHeartbeats heartbeats,
    IOptionsMonitor<NotificationsOptions> options,
    TimeProvider timeProvider) : IHealthCheck
{
    protected abstract DateTimeOffset? LastBeat(NotificationWorkerHeartbeats heartbeats);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var stale = TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.HealthChecks.HeartbeatStaleSeconds));
        var data = new Dictionary<string, object> { ["checkedAtUtc"] = now.UtcDateTime };

        var last = LastBeat(heartbeats);
        if (last is null)
        {
            return Task.FromResult(now - heartbeats.StartedAt < stale
                ? HealthCheckResult.Healthy($"The {worker} is starting.", data)
                : HealthCheckResult.Unhealthy($"The {worker} hasn't completed a single pass since the application started.", data: data));
        }

        var age = now - last.Value;
        data["lastHeartbeatAgeSeconds"] = (int)age.TotalSeconds;
        return Task.FromResult(age <= stale
            ? HealthCheckResult.Healthy($"The {worker} is running.", data)
            : HealthCheckResult.Unhealthy($"The {worker} last completed a pass {(int)age.TotalSeconds} s ago.", data: data));
    }
}

public sealed class NotificationDispatcherHealthCheck(
    NotificationWorkerHeartbeats heartbeats, IOptionsMonitor<NotificationsOptions> options, TimeProvider timeProvider)
    : NotificationWorkerHealthCheck("notification dispatcher", heartbeats, options, timeProvider)
{
    public const string Name = "notifications-dispatcher";

    protected override DateTimeOffset? LastBeat(NotificationWorkerHeartbeats heartbeats) => heartbeats.Dispatcher;
}

public sealed class NotificationDeliveryWorkerHealthCheck(
    NotificationWorkerHeartbeats heartbeats, IOptionsMonitor<NotificationsOptions> options, TimeProvider timeProvider)
    : NotificationWorkerHealthCheck("notification delivery worker", heartbeats, options, timeProvider)
{
    public const string Name = "notifications-delivery-worker";

    protected override DateTimeOffset? LastBeat(NotificationWorkerHeartbeats heartbeats) => heartbeats.DeliveryWorker;
}
