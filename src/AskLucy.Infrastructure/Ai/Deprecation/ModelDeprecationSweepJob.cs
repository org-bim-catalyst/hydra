using AskLucy.Application.Abstractions;
using Hangfire;

namespace AskLucy.Infrastructure.Ai.Deprecation;

/// <summary>
/// specs/069 research D4: re-enqueues batches left Pending by a crash between the confirming save
/// and the enqueue. The job itself is idempotent, so a duplicate enqueue is harmless.
/// </summary>
public sealed class ModelDeprecationSweepJob(
    IModelDeprecationRepository deprecations,
    Hangfire.IBackgroundJobClient backgroundJobClient)
{
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(2);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var batchIds = await deprecations.ListStuckPendingBatchIdsAsync(DateTime.UtcNow - StuckAfter, cancellationToken);
        foreach (var batchId in batchIds)
        {
            backgroundJobClient.Enqueue<IModelDeprecationUserImpactJob>(j => j.RunAsync(batchId, CancellationToken.None));
        }
    }
}
