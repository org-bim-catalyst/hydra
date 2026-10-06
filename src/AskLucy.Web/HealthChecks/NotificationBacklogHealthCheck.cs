using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AskLucy.Web.HealthChecks;

/// <summary>
/// How long the oldest due outbox event or delivery has been waiting (FR-057, research R21): degraded past
/// <see cref="NotificationHealthCheckOptions.BacklogDegradedMinutes"/>, unhealthy past
/// <see cref="NotificationHealthCheckOptions.BacklogUnhealthyMinutes"/>. It also refreshes the backlog gauge, since it
/// already owns the query.
/// </summary>
public sealed class NotificationBacklogHealthCheck(
    IServiceScopeFactory scopeFactory,
    NotificationMetrics metrics,
    IOptionsMonitor<NotificationsOptions> options,
    TimeProvider timeProvider) : IHealthCheck
{
    public const string Name = "notifications-backlog";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var scope = scopeFactory.CreateAsyncScope();
        var outbox = await scope.ServiceProvider.GetRequiredService<INotificationOutboxStore>().GetDueBacklogAsync(now, cancellationToken);
        var deliveries = await scope.ServiceProvider.GetRequiredService<INotificationRepository>().GetDueBacklogAsync(now, cancellationToken);
        metrics.ReportBacklog(outbox.DueCount, deliveries.DueCount);

        var oldest = new[] { outbox.OldestDueAtUtc, deliveries.OldestDueAtUtc }.Where(d => d is not null).DefaultIfEmpty(null).Min();
        var thresholds = options.CurrentValue.HealthChecks;
        var data = new Dictionary<string, object>
        {
            ["checkedAtUtc"] = now,
            ["outboxPending"] = outbox.DueCount,
            ["deliveriesDue"] = deliveries.DueCount,
        };

        if (oldest is null)
        {
            return HealthCheckResult.Healthy("Nothing is waiting.", data);
        }

        var age = now - oldest.Value;
        data["oldestDueAgeSeconds"] = (int)age.TotalSeconds;
        var description = $"The oldest waiting item has been due for {(int)age.TotalMinutes} min.";
        if (age >= TimeSpan.FromMinutes(Math.Max(1, thresholds.BacklogUnhealthyMinutes)))
        {
            return HealthCheckResult.Unhealthy(description, data: data);
        }

        return age >= TimeSpan.FromMinutes(Math.Max(1, thresholds.BacklogDegradedMinutes))
            ? HealthCheckResult.Degraded(description, data: data)
            : HealthCheckResult.Healthy(description, data);
    }
}
