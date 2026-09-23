using System.Globalization;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.CustomModels;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.CustomModels.Jobs;

/// <summary>
/// specs/072 plan.md "Key flows" step 2. Pins the revision to a commit, validates the whole listing,
/// then moves each file through the job's temp folder to the deployment target: download, record an
/// overwrite, upload, verify the remote size, delete the temp file.
/// </summary>
/// <remarks>
/// <para>Never retried by Hangfire: a second attempt would re-upload over files the first already
/// wrote, and every failure here is reported to the admin as a state instead (research D6).</para>
/// <para>Every database step opens its own scope, because the progress pump writes concurrently with
/// the transfer loop and a DbContext can't be shared across them. Terminal writes reload the record
/// and apply only while it is still in progress, so a record the startup sweep already failed is
/// never overwritten.</para>
/// </remarks>
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed partial class CustomModelDeploymentJob(
    IServiceScopeFactory scopeFactory,
    IModelRepositorySource modelSource,
    IDeploymentFileUploader uploader,
    IDeploymentTargetSettingsProvider deploymentTarget,
    ICustomModelDeploymentNotifier notifier,
    ICustomModelDeploymentCancellationRegistry cancellations,
    ICustomModelTempStorage tempStorage,
    IOptionsMonitor<CustomModelsOptions> options,
    TimeProvider timeProvider,
    ILogger<CustomModelDeploymentJob> logger) : ICustomModelDeploymentJob
{
    /// <summary>Research D6. Free space the temp volume must keep beyond the file being staged.</summary>
    public const long DiskSpaceHeadroomBytes = 256L * 1024 * 1024;

    public async Task RunAsync(Guid customModelId, CancellationToken cancellationToken)
    {
        var model = await InScopeAsync(r => r.GetByIdAsync(customModelId, CancellationToken.None));
        if (model is null)
        {
            LogRecordMissing(logger, customModelId);
            return;
        }

        switch (model.DeploymentState)
        {
            case CustomModelDeploymentState.Queued:
                break;

            // Hangfire re-delivers a job whose worker died mid-run. Nothing that run staged or
            // half-wrote can be trusted, so the record fails rather than resuming.
            case CustomModelDeploymentState.Listing or CustomModelDeploymentState.Transferring:
                await FailInterruptedAsync(model);
                return;

            default:
                LogNothingToDo(logger, customModelId, model.DeploymentState);
                return;
        }

        var registryToken = cancellations.Register(customModelId);
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, registryToken);
        var run = new RunState(model, runCancellation);
        try
        {
            await DeployAsync(run);
        }
        catch (OperationCanceledException) when (run.StopReason == StopReason.None && cancellationToken.IsCancellationRequested && !registryToken.IsCancellationRequested)
        {
            // The server is shutting down. Rethrowing leaves the job for Hangfire to re-deliver
            // after the restart, which fails the record as InterruptedByRestart above.
            LogInterruptedByShutdown(logger, customModelId);
            throw;
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            await StopAsync(run);
        }
        catch (Exception ex)
        {
            if (await FailAsync(run, ex))
            {
                throw;
            }
        }
        finally
        {
            DeleteTempDirectory(customModelId);
            cancellations.Unregister(customModelId);
        }
    }

    private async Task DeployAsync(RunState run)
    {
        var settings = await deploymentTarget.GetAsync(run.Token)
            ?? throw new CustomModelDeploymentFailedException(
                CustomModelFailureKind.TargetNotConfigured,
                "Deployment isn't configured on this server. Ask a server administrator to configure the deployment target.");

        if (settings.AllowPlainFtp)
        {
            CustomModelAdminActionLog.PlainFtpInUse(logger, run.Id);
        }

        var customModelsOptions = options.CurrentValue;
        if (!DeploymentDestination.TryCreate(run.Model.Destination, customModelsOptions.GetAllowedDestinationPrefixes(), out var destination, out var destinationError))
        {
            throw new CustomModelDeploymentFailedException(
                CustomModelFailureKind.Unexpected,
                $"The destination '{run.Model.Destination}' is no longer allowed on this server: {destinationError}");
        }

        await UpdateOrStopAsync(run, m => m.StartListing(Now()), requireQueued: true);
        await NotifyStateAsync(run.Model);

        var revision = await modelSource.ResolveRevisionAsync(run.Model.RepositoryId, run.Model.Revision, run.Token);
        if (revision.IsPrivate || revision.IsGated)
        {
            throw new CustomModelDeploymentFailedException(
                CustomModelFailureKind.SourceGatedOrPrivate,
                $"'{revision.RepositoryId}' is private or gated on Hugging Face. Only public repositories can be deployed.");
        }

        await UpdateOrStopAsync(run, m => m.CanonicaliseRepositoryId(revision.RepositoryId));

        var listing = await modelSource.ListFilesAsync(run.Model.RepositoryId, revision.CommitSha, run.Token);
        var plan = PlanTransfer(destination, listing);

        // BeginTransfer enforces the size cap; its CustomModelDeploymentFailedException leaves the
        // lambda unsaved and fails the record through the catch in RunAsync.
        await UpdateOrStopAsync(run, m => m.BeginTransfer(revision.CommitSha, plan.TotalBytes, plan.Files.Count, customModelsOptions.MaxDeploymentBytes));
        await NotifyStateAsync(run.Model);

        run.Progress.Begin(plan.TotalBytes, plan.Files.Count);
        run.TempDirectory = tempStorage.PrepareJobDirectory(run.Id);

        await using (var session = await uploader.OpenSessionAsync(settings, run.Token))
        {
            using var pumpStop = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
            var pump = PumpProgressAsync(run, pumpStop.Token);
            try
            {
                for (var index = 0; index < plan.Files.Count; index++)
                {
                    await TransferFileAsync(run, session, revision.CommitSha, plan.Files[index], index);
                }
            }
            finally
            {
                await pumpStop.CancelAsync();
                await pump;
            }
        }

        await CompleteAsync(run);
    }

    private static TransferPlan PlanTransfer(DeploymentDestination destination, IReadOnlyList<ModelRepositoryFile> listing)
    {
        // The whole listing is checked before a single byte moves (FR-006, FR-008): one unsafe
        // path fails the deployment with nothing written to the target.
        var files = new List<PlannedFile>(listing.Count);
        long totalBytes = 0;
        foreach (var file in listing)
        {
            if (DeploymentDestination.IsReservedFileName(file.Path))
            {
                throw new CustomModelDeploymentFailedException(
                    CustomModelFailureKind.ReservedFileName,
                    $"The repository contains '{file.Path}', which would replace a file that controls the web application. It can't be deployed.");
            }

            if (!destination.TryCombine(file.Path, out var remotePath, out var pathError))
            {
                throw new CustomModelDeploymentFailedException(CustomModelFailureKind.UnsafeRepositoryPath, pathError);
            }

            if (file.Size < 0)
            {
                throw new CustomModelDeploymentFailedException(
                    CustomModelFailureKind.SourceUnavailable,
                    $"Hugging Face listed '{file.Path}' with an invalid size.");
            }

            totalBytes = checked(totalBytes + file.Size);
            files.Add(new PlannedFile(file, remotePath));
        }

        return new TransferPlan(files, totalBytes);
    }

    private async Task TransferFileAsync(RunState run, IDeploymentUploadSession session, string commitSha, PlannedFile planned, int index)
    {
        var file = planned.File;
        EnsureDiskSpace(run.Id, file);

        run.Progress.StartFile(file.Path, file.Size);
        var previousSize = await session.GetRemoteFileSizeAsync(planned.RemotePath, run.Token);

        // The local name is the job's own, never the repository path (research D6).
        var localPath = Path.Combine(run.TempDirectory!, index.ToString("D6", CultureInfo.InvariantCulture) + ".part");
        await using (var stream = new FileStream(localPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        }))
        {
            await modelSource.DownloadAsync(run.Model.RepositoryId, commitSha, file, stream, new CallbackProgress(run.Progress.SetCurrentFileBytes), run.Token);
        }

        // Recorded once the file is staged and about to be written, so a failed download never
        // reports an overwrite that didn't happen (FR-010a).
        OverwroteFileDto? overwrote = null;
        if (previousSize is { } previousSizeBytes)
        {
            var row = run.Model.RecordOverwrite(planned.RemotePath, previousSizeBytes, Now());
            await InScopeAsync(r => r.AddOverwrittenFileAsync(row, CancellationToken.None));
            CustomModelAdminActionLog.FileOverwritten(logger, run.Id, run.Name, planned.RemotePath, previousSizeBytes);
            overwrote = new OverwroteFileDto(planned.RemotePath, previousSizeBytes);
        }

        run.Progress.SetPhase(CustomModelTransferPhase.Uploading);

        await session.UploadAsync(localPath, planned.RemotePath, new CallbackProgress(run.Progress.SetCurrentFileBytes), run.Token);

        run.Progress.SetPhase(CustomModelTransferPhase.Verifying);
        var uploadedSize = await session.GetRemoteFileSizeAsync(planned.RemotePath, run.Token);
        if (uploadedSize != file.Size)
        {
            throw new DeploymentTargetException(
                DeploymentTargetFailureKind.SizeMismatch,
                $"'{planned.RemotePath}' is {(uploadedSize is { } size ? $"{size} bytes" : "missing")} on the deployment target after upload, but {file.Size} bytes were sent.");
        }

        File.Delete(localPath);
        run.Progress.CompleteFile(file.Size);

        // Always at a file boundary: the terminal write reloads the record, and the persisted
        // count must be current (FR-021). The overwrite rides on the event that finished it.
        await FlushAsync(run, force: true, overwrote);
        run.Token.ThrowIfCancellationRequested();
    }

    private void EnsureDiskSpace(Guid customModelId, ModelRepositoryFile file)
    {
        if (tempStorage.GetAvailableFreeSpace(customModelId) is { } free && free < file.Size + DiskSpaceHeadroomBytes)
        {
            throw new CustomModelDeploymentFailedException(
                CustomModelFailureKind.DiskSpaceExhausted,
                $"The server doesn't have enough free disk space to stage '{file.Path}'. Free up space on the server and deploy again.");
        }
    }

    private async Task PumpProgressAsync(RunState run, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.CurrentValue.ProgressPushIntervalMilliseconds), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                await FlushAsync(run, force: false, overwrote: null, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The transfer loop finished or stopped; the pump stops with it.
        }
    }

    /// <summary>
    /// Persists progress at most every <see cref="CustomModelsOptions.ProgressPersistIntervalSeconds"/>
    /// and pushes it at most every <see cref="CustomModelsOptions.ProgressPushIntervalMilliseconds"/>,
    /// both immediately when <paramref name="force"/> is set. Each persist also reads the run
    /// signal, which is how a cancel from another process reaches this job (research D7).
    /// </summary>
    private async Task FlushAsync(RunState run, bool force, OverwroteFileDto? overwrote, CancellationToken cancellationToken = default)
    {
        await run.FlushGate.WaitAsync(cancellationToken);
        try
        {
            var customModelsOptions = options.CurrentValue;
            var now = timeProvider.GetUtcNow();
            var snapshot = run.Progress.Snapshot();

            if (force || now - run.LastPersistedAt >= TimeSpan.FromSeconds(customModelsOptions.ProgressPersistIntervalSeconds))
            {
                try
                {
                    var signal = await InScopeAsync(async r =>
                    {
                        await r.UpdateProgressAsync(
                            run.Id,
                            new CustomModelProgress(snapshot.TransferredBytes, snapshot.CompletedFileCount, snapshot.CurrentFilePath, snapshot.CurrentFileBytes, snapshot.CurrentFileTotalBytes),
                            CancellationToken.None);
                        return await r.GetRunSignalAsync(run.Id, CancellationToken.None);
                    });
                    run.LastPersistedAt = now;

                    if (signal != CustomModelRunSignal.Continue)
                    {
                        run.Stop(signal == CustomModelRunSignal.CancellationRequested ? StopReason.CancellationRequested : StopReason.NoLongerInProgress);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // A missed progress write costs a stale bar, not the deployment; the next
                    // flush retries it and the terminal write doesn't depend on it.
                    LogProgressWriteFailed(logger, ex, run.Id);
                }
            }

            if (force || now - run.LastPushedAt >= TimeSpan.FromMilliseconds(customModelsOptions.ProgressPushIntervalMilliseconds))
            {
                await notifier.NotifyProgressAsync(
                    new CustomModelProgressDto(
                        run.Id,
                        CustomModelDeploymentState.Transferring.ToString(),
                        snapshot.TransferredBytes,
                        snapshot.TotalBytes,
                        snapshot.CompletedFileCount,
                        snapshot.TotalFileCount,
                        snapshot.CurrentFilePath,
                        snapshot.CurrentFileBytes,
                        snapshot.CurrentFileTotalBytes,
                        snapshot.Phase.ToString(),
                        overwrote,
                        now.UtcDateTime),
                    CancellationToken.None);
                run.LastPushedAt = now;
            }
        }
        finally
        {
            run.FlushGate.Release();
        }
    }

    private async Task CompleteAsync(RunState run)
    {
        var snapshot = run.Progress.Snapshot();
        var completed = await UpdateTerminalAsync(run.Id, m =>
        {
            // The last progress flush may have failed; the loop's own counts are authoritative.
            m.RecordProgress(snapshot.TransferredBytes, snapshot.CompletedFileCount, null, null, null);
            m.Complete(Now());
        });

        if (completed is null)
        {
            LogNoLongerInProgress(logger, run.Id);
            return;
        }

        CustomModelAdminActionLog.Completed(logger, completed.Id, completed.Name, completed.CompletedFileCount, completed.TotalBytes ?? 0, completed.OverwrittenFileCount);
        await NotifyStateAsync(completed);
    }

    private async Task StopAsync(RunState run)
    {
        if (run.StopReason == StopReason.NoLongerInProgress)
        {
            LogNoLongerInProgress(logger, run.Id);
            return;
        }

        var cancelled = await UpdateTerminalAsync(
            run.Id,
            m => m.MarkCancelled(Now()),
            canApply: m => m.CancellationRequestedAtUtc is not null && m.DeploymentState is CustomModelDeploymentState.Listing or CustomModelDeploymentState.Transferring);

        if (cancelled is null)
        {
            LogNoLongerInProgress(logger, run.Id);
            return;
        }

        CustomModelAdminActionLog.Cancelled(logger, cancelled.Id, cancelled.Name, cancelled.CompletedFileCount);
        await NotifyStateAsync(cancelled);
    }

    /// <summary>Returns <see langword="true"/> when this job wrote the failure, so the caller rethrows for Hangfire's record.</summary>
    private async Task<bool> FailAsync(RunState run, Exception exception)
    {
        var (kind, reason) = Classify(exception);

        CustomModel? failed;
        try
        {
            failed = await UpdateTerminalAsync(run.Id, m => m.Fail(kind, reason, Now()));
        }
        catch (Exception writeException)
        {
            // The record stays in progress until the startup sweep fails it; both errors are kept.
            CustomModelAdminActionLog.Failed(logger, exception, run.Id, run.Name, run.Model.DeploymentState, kind, reason);
            LogTerminalWriteFailed(logger, writeException, run.Id);
            throw;
        }

        if (failed is null)
        {
            CustomModelAdminActionLog.Failed(logger, exception, run.Id, run.Name, run.Model.DeploymentState, kind, reason);
            LogNoLongerInProgress(logger, run.Id);
            return false;
        }

        CustomModelAdminActionLog.Failed(logger, exception, failed.Id, failed.Name, failed.DeploymentState, kind, reason);
        await NotifyStateAsync(failed);
        return true;
    }

    private async Task FailInterruptedAsync(CustomModel model)
    {
        const string reason = "The deployment was interrupted by a server restart. Remove it and deploy the model again.";
        var failed = await UpdateTerminalAsync(model.Id, m => m.Fail(CustomModelFailureKind.InterruptedByRestart, reason, Now()));
        DeleteTempDirectory(model.Id);

        if (failed is null)
        {
            LogNoLongerInProgress(logger, model.Id);
            return;
        }

        CustomModelAdminActionLog.Failed(logger, null, failed.Id, failed.Name, failed.DeploymentState, CustomModelFailureKind.InterruptedByRestart, reason);
        await NotifyStateAsync(failed);
    }

    private static (CustomModelFailureKind Kind, string Reason) Classify(Exception exception) => exception switch
    {
        CustomModelDeploymentFailedException failed => (failed.Kind, failed.Message),
        ModelRepositorySourceException source => (source.Kind switch
        {
            ModelRepositorySourceFailureKind.NotFound => CustomModelFailureKind.SourceNotFound,
            ModelRepositorySourceFailureKind.GatedOrPrivate => CustomModelFailureKind.SourceGatedOrPrivate,
            ModelRepositorySourceFailureKind.IntegrityMismatch => CustomModelFailureKind.IntegrityMismatch,
            ModelRepositorySourceFailureKind.DownloadStalled => CustomModelFailureKind.DownloadStalled,
            _ => CustomModelFailureKind.SourceUnavailable,
        }, source.Message),
        DeploymentTargetException target => (target.Kind switch
        {
            DeploymentTargetFailureKind.AuthRejected => CustomModelFailureKind.TargetAuthRejected,
            DeploymentTargetFailureKind.TlsNotAccepted => CustomModelFailureKind.TargetTlsNotAccepted,
            DeploymentTargetFailureKind.CertificateInvalid => CustomModelFailureKind.TargetCertificateInvalid,
            DeploymentTargetFailureKind.WriteRejected => CustomModelFailureKind.TargetWriteRejected,
            DeploymentTargetFailureKind.SizeMismatch => CustomModelFailureKind.TargetSizeMismatch,
            _ => CustomModelFailureKind.TargetConnectionLost,
        }, target.Message),
        IOException io when IsDiskFull(io) => (CustomModelFailureKind.DiskSpaceExhausted, "The server ran out of disk space while staging the files. Free up space on the server and deploy again."),
        _ => (CustomModelFailureKind.Unexpected, "The deployment failed unexpectedly. The server log has the details."),
    };

    /// <summary>ERROR_HANDLE_DISK_FULL / ERROR_DISK_FULL on Windows, ENOSPC on Unix.</summary>
    private static bool IsDiskFull(IOException exception) =>
        exception.HResult is unchecked((int)0x80070027) or unchecked((int)0x80070070) or 28;

    /// <summary>
    /// A non-terminal transition. Declined when the record left the expected state (a cancel, or the
    /// startup sweep), in which case the run stops through the cancel path.
    /// </summary>
    private async Task UpdateOrStopAsync(RunState run, Action<CustomModel> apply, bool requireQueued = false)
    {
        var declined = StopReason.None;
        var saved = await InScopeAsync(r => r.UpdateAsync(
            run.Id,
            m =>
            {
                if (!m.IsInProgress || (requireQueued && m.DeploymentState != CustomModelDeploymentState.Queued))
                {
                    declined = StopReason.NoLongerInProgress;
                    return false;
                }

                if (m.CancellationRequestedAtUtc is not null)
                {
                    declined = StopReason.CancellationRequested;
                    return false;
                }

                apply(m);
                return true;
            },
            CancellationToken.None));

        if (saved is null)
        {
            run.Stop(declined == StopReason.None ? StopReason.NoLongerInProgress : declined);
            run.Token.ThrowIfCancellationRequested();
        }

        run.Model = saved!;
    }

    /// <summary>Reloads in a fresh scope and applies only while the record is still in progress; the repository retries once on a RowVersion conflict.</summary>
    private Task<CustomModel?> UpdateTerminalAsync(Guid customModelId, Action<CustomModel> apply, Func<CustomModel, bool>? canApply = null) =>
        InScopeAsync(r => r.UpdateAsync(
            customModelId,
            m =>
            {
                if (!m.IsInProgress || (canApply is not null && !canApply(m)))
                {
                    return false;
                }

                apply(m);
                return true;
            },
            CancellationToken.None));

    private async Task NotifyStateAsync(CustomModel model)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var summary = await scope.ServiceProvider.GetRequiredService<CustomModelSummaryBuilder>().BuildAsync(model, CancellationToken.None);
            await notifier.NotifyStateChangedAsync(summary, cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The state is saved; clients refetch on reconnect, so a dropped event isn't a failure of the deployment.
            LogStateNotificationFailed(logger, ex, model.Id);
        }
    }

    private void DeleteTempDirectory(Guid customModelId)
    {
        try
        {
            tempStorage.DeleteJobDirectory(customModelId);
        }
        catch (Exception ex)
        {
            LogTempCleanupFailed(logger, ex, customModelId);
        }
    }

    private async Task<T> InScopeAsync<T>(Func<ICustomModelRepository, Task<T>> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICustomModelRepository>());
    }

    private async Task InScopeAsync(Func<ICustomModelRepository, Task> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<ICustomModelRepository>());
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom model deployment job for {CustomModelId} found no record; nothing to do")]
    private static partial void LogRecordMissing(ILogger logger, Guid customModelId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Custom model deployment job for {CustomModelId} delivered in state {DeploymentState}; nothing to do")]
    private static partial void LogNothingToDo(ILogger logger, Guid customModelId, CustomModelDeploymentState deploymentState);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom model {CustomModelId} deployment stopped: the record is no longer in progress, so it was left as it is")]
    private static partial void LogNoLongerInProgress(ILogger logger, Guid customModelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom model {CustomModelId} deployment interrupted by server shutdown; it will be failed as interrupted when the job is re-delivered")]
    private static partial void LogInterruptedByShutdown(ILogger logger, Guid customModelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom model {CustomModelId} progress couldn't be saved; the next flush retries")]
    private static partial void LogProgressWriteFailed(ILogger logger, Exception exception, Guid customModelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom model {CustomModelId} state change couldn't be pushed to admins")]
    private static partial void LogStateNotificationFailed(ILogger logger, Exception exception, Guid customModelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom model {CustomModelId} temp folder couldn't be deleted")]
    private static partial void LogTempCleanupFailed(ILogger logger, Exception exception, Guid customModelId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Custom model {CustomModelId} failure couldn't be saved; the record stays in progress until the startup sweep fails it")]
    private static partial void LogTerminalWriteFailed(ILogger logger, Exception exception, Guid customModelId);

    private enum StopReason
    {
        None,
        CancellationRequested,
        NoLongerInProgress,
    }

    private sealed record PlannedFile(ModelRepositoryFile File, string RemotePath);

    private sealed record TransferPlan(IReadOnlyList<PlannedFile> Files, long TotalBytes);

    private sealed class RunState(CustomModel model, CancellationTokenSource cancellation)
    {
        public Guid Id { get; } = model.Id;

        public string Name { get; } = model.Name;

        /// <summary>The last saved copy. Its in-memory transitions (<see cref="CustomModel.RecordOverwrite"/>) are never saved through the aggregate.</summary>
        public CustomModel Model { get; set; } = model;

        public CancellationToken Token => cancellation.Token;

        public StopReason StopReason { get; private set; }

        public string? TempDirectory { get; set; }

        public TransferProgress Progress { get; } = new();

        public SemaphoreSlim FlushGate { get; } = new(1, 1);

        public DateTimeOffset LastPersistedAt { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset LastPushedAt { get; set; } = DateTimeOffset.MinValue;

        public void Stop(StopReason reason)
        {
            if (StopReason == StopReason.None)
            {
                StopReason = reason;
            }

            cancellation.Cancel();
        }
    }

    private sealed record ProgressSnapshot(
        long TotalBytes,
        int TotalFileCount,
        long TransferredBytes,
        int CompletedFileCount,
        string? CurrentFilePath,
        long? CurrentFileBytes,
        long? CurrentFileTotalBytes,
        CustomModelTransferPhase Phase);

    /// <summary>
    /// Written by the source's and uploader's progress callbacks, read by the pump. Overall bytes
    /// count uploaded bytes only, so the bar never runs ahead of what is on the target.
    /// </summary>
    private sealed class TransferProgress
    {
        private readonly Lock gate = new();
        private long totalBytes;
        private int totalFileCount;
        private long completedBytes;
        private int completedFileCount;
        private string? currentFilePath;
        private long currentFileBytes;
        private long currentFileTotalBytes;
        private CustomModelTransferPhase phase;

        public void Begin(long total, int fileCount)
        {
            lock (gate)
            {
                totalBytes = total;
                totalFileCount = fileCount;
            }
        }

        public void StartFile(string path, long size)
        {
            lock (gate)
            {
                currentFilePath = path;
                currentFileBytes = 0;
                currentFileTotalBytes = size;
                phase = CustomModelTransferPhase.Downloading;
            }
        }

        public void SetPhase(CustomModelTransferPhase value)
        {
            lock (gate)
            {
                phase = value;
                if (value == CustomModelTransferPhase.Uploading)
                {
                    currentFileBytes = 0;
                }
            }
        }

        /// <summary>Cumulative bytes of the current file in the current phase.</summary>
        public void SetCurrentFileBytes(long bytes)
        {
            lock (gate)
            {
                currentFileBytes = Math.Clamp(bytes, 0, currentFileTotalBytes);
            }
        }

        public void CompleteFile(long size)
        {
            lock (gate)
            {
                completedBytes += size;
                completedFileCount++;
                currentFilePath = null;
                currentFileBytes = 0;
                currentFileTotalBytes = 0;
            }
        }

        public ProgressSnapshot Snapshot()
        {
            lock (gate)
            {
                var inFile = currentFilePath is not null;
                var uploadedOfCurrent = inFile && phase != CustomModelTransferPhase.Downloading ? currentFileBytes : 0;
                return new ProgressSnapshot(
                    totalBytes,
                    totalFileCount,
                    completedBytes + uploadedOfCurrent,
                    completedFileCount,
                    currentFilePath,
                    inFile ? currentFileBytes : null,
                    inFile ? currentFileTotalBytes : null,
                    phase);
            }
        }
    }

    /// <summary>Reports on the caller's thread; <see cref="Progress{T}"/> would post to the thread pool and reorder reports.</summary>
    private sealed class CallbackProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
