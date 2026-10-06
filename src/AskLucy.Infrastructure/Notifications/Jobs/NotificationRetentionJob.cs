using AskLucy.Application.Notifications.Processing;
using Hangfire;

namespace AskLucy.Infrastructure.Notifications.Jobs;

/// <summary>The daily Hangfire shell around <see cref="RetentionService"/> (research R20). Hangfire records a failed run; the next day's run tries again.</summary>
[AutomaticRetry(Attempts = 0)]
public sealed class NotificationRetentionJob(RetentionService retention)
{
    public async Task RunAsync(CancellationToken cancellationToken) => await retention.RunAsync(cancellationToken);
}
