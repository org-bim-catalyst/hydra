using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.CustomModels;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.CustomModels.Commands.CancelCustomModelDeployment;

/// <summary>specs/072 research D7.</summary>
/// <remarks>
/// The save goes through <see cref="ICustomModelRepository.UpdateAsync"/>, which reloads and applies
/// once more when a progress write bumped the RowVersion (standing rule 11), so the lambda must stay
/// safe to run twice. A second conflict propagates as a 409.
/// </remarks>
public sealed partial class CancelCustomModelDeploymentCommandHandler(
    ICustomModelRepository customModels,
    IBackgroundJobClient backgroundJobs,
    ICustomModelDeploymentCancellationRegistry cancellations,
    ICustomModelDeploymentNotifier notifier,
    CustomModelSummaryBuilder summaries,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<CancelCustomModelDeploymentCommandHandler> logger) : IRequestHandler<CancelCustomModelDeploymentCommand, CustomModelSummaryDto>
{
    public async Task<CustomModelSummaryDto> Handle(CancelCustomModelDeploymentCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var found = false;
        CustomModelDeploymentState? declinedState = null;
        var wasQueued = false;
        var saved = await customModels.UpdateAsync(
            request.Id,
            m =>
            {
                found = true;
                if (!m.IsInProgress)
                {
                    declinedState = m.DeploymentState;
                    return false;
                }

                declinedState = null;
                wasQueued = m.DeploymentState == CustomModelDeploymentState.Queued;
                m.RequestCancellation(actorUserId, timeProvider.GetUtcNow().UtcDateTime);
                return true;
            },
            cancellationToken);

        if (saved is null)
        {
            throw found
                ? new CustomModelNotInProgressException($"This deployment is already {declinedState?.ToString().ToLowerInvariant()} and can't be cancelled.")
                : new KeyNotFoundException("The custom model wasn't found.");
        }

        CustomModelAdminActionLog.CancelRequested(logger, actorUserId, saved.Id, saved.Name);

        if (wasQueued)
        {
            DeleteQueuedJob(saved);
        }
        else if (!cancellations.TryCancel(saved.Id))
        {
            // The job runs in another process (or is between steps); the persisted flag reaches it on its next flush.
            LogCancelFlagged(logger, saved.Id);
        }

        var summary = await summaries.BuildAsync(saved, cancellationToken);
        await notifier.NotifyStateChangedAsync(summary, cancellationToken: cancellationToken);
        return summary;
    }

    private void DeleteQueuedJob(CustomModel model)
    {
        if (model.BackgroundJobId is null)
        {
            return;
        }

        try
        {
            backgroundJobs.Delete(model.BackgroundJobId);
        }
        catch (Exception ex)
        {
            // The record is already Cancelled, so a job that is still delivered finds nothing to do.
            LogQueuedJobDeleteFailed(logger, ex, model.Id, model.BackgroundJobId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Custom model {CustomModelId} cancellation flagged; its job will stop on its next progress flush")]
    private static partial void LogCancelFlagged(ILogger logger, Guid customModelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom model {CustomModelId} was cancelled, but its queued job {BackgroundJobId} couldn't be deleted; it will do nothing when delivered")]
    private static partial void LogQueuedJobDeleteFailed(ILogger logger, Exception exception, Guid customModelId, string backgroundJobId);
}
