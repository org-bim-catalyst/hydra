using AskLucy.Application.Abstractions;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Ai;

internal static partial class ProviderHealthCheckRetentionLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Provider health check retention removed {Deleted} rows recorded before {CutoffUtc:o}")]
    public static partial void Removed(ILogger logger, int deleted, DateTime cutoffUtc);
}

/// <summary>
/// Daily Hangfire job that removes provider health checks older than <see cref="ProviderHealthCheckOptions.RetentionDays"/>.
/// Hangfire records a failed run, and the next day's run tries again.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public sealed class ProviderHealthCheckRetentionJob(
    IProviderHealthCheckRepository checks,
    IOptions<ProviderHealthCheckOptions> options,
    TimeProvider timeProvider,
    ILogger<ProviderHealthCheckRetentionJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoffUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(-options.Value.RetentionDays);
        var deleted = await checks.DeleteCheckedBeforeAsync(cutoffUtc, cancellationToken);
        ProviderHealthCheckRetentionLog.Removed(logger, deleted, cutoffUtc);
    }
}
