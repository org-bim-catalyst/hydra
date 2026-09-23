using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.CustomModels;
using Hangfire;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.CustomModels.Jobs;

/// <summary>
/// specs/072 research D6 "Startup sweep" (FR-013). Once per boot, fails every deployment this
/// process can't still be running and deletes the temp folders nothing owns, so no record is left
/// in progress forever:
/// <list type="bullet">
/// <item><c>Listing</c> or <c>Transferring</c>: the worker that ran it is gone.</item>
/// <item><c>Queued</c> with no Hangfire job, or one in no live state: nothing will ever pick it up.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>Only records created before this process started are swept; anything newer belongs to
/// this process and may be mid-submit. The failure write is conditional and the repository retries
/// it once on a RowVersion conflict, so a record that ended meanwhile is left as it ended.</para>
/// <para>During an IIS overlapped recycle this can fail a deployment the old worker is still
/// finishing; that worker's next flush sees the record is no longer in progress and stops (research D6).</para>
/// </remarks>
public sealed partial class CustomModelDeploymentRecovery(
    IServiceScopeFactory scopeFactory,
    JobStorage jobStorage,
    ICustomModelTempStorage tempStorage,
    TimeProvider timeProvider,
    ILogger<CustomModelDeploymentRecovery> logger)
{
    public const string InterruptedReason = "The deployment was interrupted by a server restart. Remove it and deploy the model again.";

    public const string LostJobReason = "The deployment's background job was lost in a server restart before it started. Remove it and deploy the model again.";

    private static readonly HashSet<string> LiveJobStates = new(StringComparer.OrdinalIgnoreCase)
    {
        EnqueuedState.StateName,
        ScheduledState.StateName,
        ProcessingState.StateName,
    };

    /// <summary>Sweeps once. Records created at or after <paramref name="bootUtc"/> belong to this process and are left alone.</summary>
    public async Task RunAsync(DateTime bootUtc, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomModelRepository>();

        var inProgress = await repository.ListInProgressAsync(cancellationToken);
        var keep = new HashSet<Guid>();
        var failedCount = 0;
        foreach (var model in inProgress)
        {
            bool failed;
            if (model.CreatedAtUtc >= bootUtc)
            {
                failed = false;
            }
            else if (IsRunning(model))
            {
                failed = await TryFailAsync(repository, model.Id, IsRunning, InterruptedReason, cancellationToken);
            }
            else if (!HasLiveJob(model))
            {
                failed = await TryFailAsync(repository, model.Id, m => m.DeploymentState == CustomModelDeploymentState.Queued, LostJobReason, cancellationToken);
            }
            else
            {
                failed = false;
            }

            if (failed)
            {
                failedCount++;
            }
            else
            {
                // Still someone's to run (a live queued job, or one submitted since boot), or it ended
                // on its own meanwhile and its job cleans up after itself.
                keep.Add(model.Id);
            }
        }

        var deletedCount = 0;
        foreach (var id in tempStorage.ListJobDirectories().Where(id => !keep.Contains(id)))
        {
            try
            {
                tempStorage.DeleteJobDirectory(id);
                deletedCount++;
            }
            catch (Exception ex)
            {
                LogTempCleanupFailed(logger, ex, id);
            }
        }

        LogSweepCompleted(logger, failedCount, deletedCount);
    }

    private static bool IsRunning(CustomModel model) =>
        model.DeploymentState is CustomModelDeploymentState.Listing or CustomModelDeploymentState.Transferring;

    /// <param name="stillApplies">Re-evaluated against each reload, so a record that moved on since the listing is left alone.</param>
    private async Task<bool> TryFailAsync(ICustomModelRepository repository, Guid customModelId, Func<CustomModel, bool> stillApplies, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var failed = await repository.UpdateAsync(
                customModelId,
                m =>
                {
                    if (!m.IsInProgress || !stillApplies(m))
                    {
                        return false;
                    }

                    m.Fail(CustomModelFailureKind.InterruptedByRestart, reason, timeProvider.GetUtcNow().UtcDateTime);
                    return true;
                },
                cancellationToken);

            if (failed is null)
            {
                return false;
            }

            CustomModelAdminActionLog.Failed(logger, null, failed.Id, failed.Name, failed.DeploymentState, failed.FailureKind, reason);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One row that can't be saved must not stop the others; it is retried on the next boot.
            LogRecordFailFailed(logger, ex, customModelId);
            return false;
        }
    }

    private bool HasLiveJob(CustomModel model)
    {
        if (string.IsNullOrEmpty(model.BackgroundJobId))
        {
            return false;
        }

        try
        {
            using var connection = jobStorage.GetConnection();
            var state = connection.GetStateData(model.BackgroundJobId);
            return state is not null && LiveJobStates.Contains(state.Name);
        }
        catch (Exception ex)
        {
            // Unverifiable is treated as live: failing a deployment that may still run is worse
            // than leaving it queued until the next restart.
            LogJobStateUnreadable(logger, ex, model.Id);
            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Custom model {CustomModelId} Hangfire job state couldn't be read by the startup sweep; the record is left queued")]
    private static partial void LogJobStateUnreadable(ILogger logger, Exception exception, Guid customModelId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Custom model startup sweep failed {FailedCount} interrupted deployment(s) and deleted {DeletedCount} temp folder(s)")]
    private static partial void LogSweepCompleted(ILogger logger, int failedCount, int deletedCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Custom model {CustomModelId} couldn't be failed by the startup sweep; it stays in progress until the next restart")]
    private static partial void LogRecordFailFailed(ILogger logger, Exception exception, Guid customModelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom model temp folder {CustomModelId} couldn't be deleted by the startup sweep")]
    private static partial void LogTempCleanupFailed(ILogger logger, Exception exception, Guid customModelId);
}
