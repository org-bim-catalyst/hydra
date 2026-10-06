using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <param name="Health">Healthy, Degraded or Unhealthy.</param>
/// <param name="Detail">A safe summary: never a credential, and never the server's banner.</param>
public sealed record NotificationChannelHealth(string Health, DateTime? CheckedAtUtc, string? Detail);

/// <summary>
/// The latest health of a delivery channel, from the cached health-check results (contracts/admin-notifications-api.md).
/// It never opens a new connection to a provider: the probes cache their own results (research R21).
/// </summary>
public interface INotificationChannelHealthReader
{
    Task<NotificationChannelHealth> GetAsync(NotificationChannel channel, CancellationToken cancellationToken);
}
