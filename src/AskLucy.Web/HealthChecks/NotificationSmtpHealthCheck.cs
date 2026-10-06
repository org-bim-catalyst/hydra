using AskLucy.Application.Options;
using AskLucy.Infrastructure.Email;
using AskLucy.Infrastructure.Notifications;
using AskLucy.Infrastructure.Notifications.HealthChecks;
using AskLucy.Infrastructure.Notifications.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AskLucy.Web.HealthChecks;

/// <summary>
/// Connects, upgrades to TLS and logs in to the mail host, without sending (FR-057, research R21). The result is cached
/// for <see cref="NotificationHealthCheckOptions.SmtpProbeCacheMinutes"/>, so readiness polling never hammers the host, and a
/// failure is only ever <b>Degraded</b>: a mail outage must not take the whole site out of rotation. The description is a safe
/// summary: never the host's banner, never a credential.
/// </summary>
public sealed class NotificationSmtpHealthCheck(
    ISmtpProbe probe,
    IOptions<SmtpOptions> smtpOptions,
    IOptionsMonitor<NotificationsOptions> options,
    TimeProvider timeProvider) : IHealthCheck, IDisposable
{
    public const string Name = "notifications-smtp";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private HealthCheckResult? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            var ttl = TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.HealthChecks.SmtpProbeCacheMinutes));
            if (_cached is { } cached && now - _cachedAt < ttl)
            {
                return cached;
            }

            var result = await ProbeAsync(now, cancellationToken);
            _cached = result;
            _cachedAt = now;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HealthCheckResult> ProbeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, object> { ["checkedAtUtc"] = now.UtcDateTime };
        var smtp = smtpOptions.Value;
        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            return HealthCheckResult.Degraded("No mail server is configured.", data: data);
        }

        try
        {
            var summary = await probe.ProbeAsync(cancellationToken);
            return HealthCheckResult.Healthy(summary, data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never the exception's message: a provider can put the server banner or an account name in it. The kind says enough.
            return HealthCheckResult.Degraded($"The mail server probe failed ({ex.GetType().Name}).", data: data);
        }
    }

    public void Dispose() => _gate.Dispose();
}
