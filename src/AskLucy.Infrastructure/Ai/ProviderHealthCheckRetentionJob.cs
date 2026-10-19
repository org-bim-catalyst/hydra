using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
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
/// Daily Hangfire job that removes provider health checks older than the stored retention window, falling back to
/// <see cref="ProviderHealthCheckOptions.RetentionDays"/> when the singleton row is missing (specs/082). Hangfire records a
/// failed run, and the next day's run tries again.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public sealed class ProviderHealthCheckRetentionJob(
    IProviderHealthCheckRepository checks,
    IProviderHealthRetentionRepository retention,
    IOptions<ProviderHealthCheckOptions> options,
    TimeProvider timeProvider,
    ILogger<ProviderHealthCheckRetentionJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var setting = await retention.GetAsync(cancellationToken);
        var retentionDays = setting?.RetentionDays ?? options.Value.RetentionDays;
        var cutoffUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var deleted = await checks.DeleteCheckedBeforeAsync(cutoffUtc, cancellationToken);
        ProviderHealthCheckRetentionLog.Removed(logger, deleted, cutoffUtc);
    }
}
