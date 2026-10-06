using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AskLucy.Web.HealthChecks;

/// <summary>
/// A channel's health is the worst of the checks behind it: email is the mail host and the delivery worker, in-app is
/// the dispatcher. The mail probe caches its own result, so reading this never opens a connection to the host.
/// </summary>
public sealed class NotificationChannelHealthReader(HealthCheckService healthChecks, TimeProvider timeProvider) : INotificationChannelHealthReader
{
    public async Task<NotificationChannelHealth> GetAsync(NotificationChannel channel, CancellationToken cancellationToken)
    {
        var names = channel == NotificationChannel.Email
            ? new HashSet<string> { NotificationSmtpHealthCheck.Name, NotificationDeliveryWorkerHealthCheck.Name }
            : [NotificationDispatcherHealthCheck.Name];

        var report = await healthChecks.CheckHealthAsync(entry => names.Contains(entry.Name), cancellationToken);
        var entries = report.Entries.Values.ToList();
        if (entries.Count == 0)
        {
            return new NotificationChannelHealth("Healthy", null, null);
        }

        var worst = entries.OrderBy(e => e.Status).First(); // Unhealthy < Degraded < Healthy
        var checkedAt = entries
            .Select(e => e.Data.TryGetValue("checkedAtUtc", out var at) && at is DateTime when ? when : (DateTime?)null)
            .Where(d => d is not null).DefaultIfEmpty(timeProvider.GetUtcNow().UtcDateTime).Min();

        // The descriptions are written to be safe: never a credential and never a server banner.
        return new NotificationChannelHealth(worst.Status.ToString(), checkedAt, worst.Description);
    }
}
