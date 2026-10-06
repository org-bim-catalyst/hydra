using AskLucy.Application.Notifications.Processing;
using Hangfire;

namespace AskLucy.Infrastructure.Notifications.Jobs;

/// <summary>
/// The Hangfire recurring job that runs <see cref="LeaseSweepService"/> every minute (research R5).
/// Hangfire is kept for recurring jobs only (ADR 0018); the work is the Application service's, so this
/// shell has nothing to fail but the sweep itself, which Hangfire then records and retries.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public sealed class NotificationLeaseSweepJob(LeaseSweepService sweepService)
{
    public Task RunAsync(CancellationToken cancellationToken) => sweepService.SweepAsync(cancellationToken);
}
