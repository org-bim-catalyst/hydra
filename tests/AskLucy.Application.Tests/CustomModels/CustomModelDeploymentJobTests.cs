using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.CustomModels.Jobs;
using AskLucy.Application.Options;
using AskLucy.Domain.CustomModels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AskLucy.Application.Tests.CustomModels;

public sealed class CustomModelDeploymentJobTests : IDisposable
{
    private const string Destination = "Models/supertonic-3";

    private readonly FakeCustomModelRepository _repository = new();
    private readonly FakeModelRepositorySource _source = new();
    private readonly FakeDeploymentFileUploader _uploader = new();
    private readonly TestTempStorage _tempStorage = new();
    private readonly IDeploymentTargetSettingsProvider _deploymentTarget = Substitute.For<IDeploymentTargetSettingsProvider>();
    private readonly ICustomModelDeploymentNotifier _notifier = Substitute.For<ICustomModelDeploymentNotifier>();
    private readonly ICustomModelDeploymentCancellationRegistry _cancellations = Substitute.For<ICustomModelDeploymentCancellationRegistry>();
    private readonly FakeLogger<CustomModelDeploymentJob> _logger = new();
    private readonly List<string> _notifiedStates = [];
    private readonly List<CustomModelProgressDto> _pushes = [];
    private CustomModelsOptions _options = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _services;

    public CustomModelDeploymentJobTests()
    {
        _deploymentTarget.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new DeploymentTargetSettings("ftp.example.test", 21, "deployer", "not-a-real-password", "/site", AllowPlainFtp: false));
        _cancellations.Register(Arg.Any<Guid>()).Returns(CancellationToken.None);
        _notifier.NotifyStateChangedAsync(Arg.Do<CustomModelSummaryDto>(s => _notifiedStates.Add(s.DeploymentState)), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _notifier.NotifyProgressAsync(Arg.Do<CustomModelProgressDto>(RecordPush), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _source.Files["config.json"] = Encoding.UTF8.GetBytes("{\"a\":1}");
        _source.Files["onnx/model.onnx"] = new byte[4096];

        _services = new ServiceCollection()
            .AddSingleton<ICustomModelRepository>(_repository)
            .AddSingleton(Substitute.For<IUserAdminRepository>())
            .AddScoped<CustomModelSummaryBuilder>()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _tempStorage.Dispose();
    }

    [Fact]
    public async Task RunAsync_HappyPath_GoesQueuedListingTransferringCompleted()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        _notifiedStates.Should().Equal("Listing", "Transferring", "Completed");
        model.DeploymentState.Should().Be(CustomModelDeploymentState.Completed);
        model.IsInProgress.Should().BeFalse();
        model.TotalFileCount.Should().Be(2);
        model.CompletedFileCount.Should().Be(2);
        model.TotalBytes.Should().Be(_source.Files.Values.Sum(f => f.Length));
        model.TransferredBytes.Should().Be(model.TotalBytes);
        model.ResolvedCommitSha.Should().Be(FakeModelRepositorySource.CommitSha);
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "Completed");
    }

    [Fact]
    public async Task RunAsync_DownloadsEveryFileFromTheResolvedCommit_NotTheBranch()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3/tree/main");

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        _source.Resolutions.Should().ContainSingle().Which.Revision.Should().Be("main");
        _source.Downloads.Should().HaveCount(2).And.OnlyContain(d => d.CommitSha == FakeModelRepositorySource.CommitSha);
    }

    [Fact]
    public async Task RunAsync_StoresTheCanonicalRepositoryId()
    {
        var model = await SeedAsync("https://huggingface.co/supertone/Supertonic-3");
        _source.Revision = new ResolvedRevision("Supertone/supertonic-3", FakeModelRepositorySource.CommitSha, IsPrivate: false, IsGated: false);

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        model.RepositoryId.Should().Be("Supertone/supertonic-3");
        _source.Downloads.Should().OnlyContain(d => d.RepositoryId == "Supertone/supertonic-3");
    }

    [Fact]
    public async Task RunAsync_UploadsEachFileToDestinationSlashRelativePath()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        _uploader.Uploads.Should().BeEquivalentTo([$"{Destination}/config.json", $"{Destination}/onnx/model.onnx"]);
        _uploader.OpenedWith!.RootPath.Should().Be("/site");
    }

    [Fact]
    public async Task RunAsync_RemoteSizeDiffers_FailsWithTargetSizeMismatch()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _uploader.ReportedSizeOverride = 1;

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DeploymentTargetException>();
        model.DeploymentState.Should().Be(CustomModelDeploymentState.Failed);
        model.FailureKind.Should().Be(CustomModelFailureKind.TargetSizeMismatch);
        _notifiedStates.Should().EndWith("Failed");
    }

    [Fact]
    public async Task RunAsync_DeletesEachTempFileBeforeTheNext_AndTheFolderAtTheEnd()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        _uploader.StagedFileCounts.Should().Equal(1, 1);
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RunAsync_PrivateOrGated_FailsWithSourceGatedOrPrivate(bool isPrivate, bool isGated)
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _source.Revision = new ResolvedRevision("Supertone/supertonic-3", FakeModelRepositorySource.CommitSha, isPrivate, isGated);

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CustomModelDeploymentFailedException>();
        model.FailureKind.Should().Be(CustomModelFailureKind.SourceGatedOrPrivate);
        _source.Downloads.Should().BeEmpty();
        _uploader.Uploads.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ExistingRemoteFile_RecordsTheOverwriteAndReportsIt()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _uploader.RemoteFiles[$"{Destination}/config.json"] = 3;

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        _repository.OverwrittenFiles.Should().ContainSingle(f => f.RelativePath == $"{Destination}/config.json" && f.PreviousSizeBytes == 3);
        model.OverwrittenFileCount.Should().Be(1);
        await _notifier.Received(1).NotifyProgressAsync(
            Arg.Is<CustomModelProgressDto>(p => p!.Overwrote != null && p.Overwrote.RelativePath == $"{Destination}/config.json"),
            Arg.Any<CancellationToken>());
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "FileOverwritten");
    }

    [Fact]
    public async Task RunAsync_NotConfigured_FailsWithTargetNotConfigured()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _deploymentTarget.GetAsync(Arg.Any<CancellationToken>()).Returns((DeploymentTargetSettings?)null);

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CustomModelDeploymentFailedException>();
        model.FailureKind.Should().Be(CustomModelFailureKind.TargetNotConfigured);
    }

    [Fact]
    public async Task RunAsync_NeverPutsThePasswordInTheFailureReasonOrLog()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _uploader.UploadException = new DeploymentTargetException(DeploymentTargetFailureKind.AuthRejected, "The deployment server rejected the login.");

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DeploymentTargetException>();
        model.FailureKind.Should().Be(CustomModelFailureKind.TargetAuthRejected);
        model.FailureReason.Should().NotContain("not-a-real-password");
        _logger.Collector.GetSnapshot().Should().NotContain(r => r.Message.Contains("not-a-real-password"));
    }

    [Fact]
    public async Task RunAsync_PersistsAndPushesProgressAtEveryFileBoundary()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");

        // The clock never moves, so the pump never ticks: everything seen here is a forced flush.
        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        _repository.ProgressWrites.Select(w => w.CompletedFileCount).Should().Equal(1, 2);
        Pushes().Select(p => p.CompletedFileCount).Should().Equal(1, 2);
        Pushes().Should().OnlyContain(p => p.DeploymentState == "Transferring");
    }

    [Fact]
    public async Task RunAsync_BetweenFileBoundaries_PushesEvery500Ms_ButPersistsOnlyEvery2S()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        var observed = new List<(int Pushes, int Persists, long TransferredBytes)>();
        _uploader.OnUpload = async _ =>
        {
            if (observed.Count > 0)
            {
                return;
            }

            // Each step moves the fake clock and waits for the pump's push to land.
            foreach (var step in new[] { 500, 500, 500, 1000 })
            {
                var before = Pushes().Count;
                _time.Advance(TimeSpan.FromMilliseconds(step));
                await WaitForPushesAsync(before + 1);
                observed.Add((Pushes().Count, _repository.ProgressWrites.Count, Pushes()[^1].TransferredBytes));
            }
        };

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        // The first tick persists (nothing saved yet); the next two fall within 2 s of it; the fourth is 2 s after it.
        observed.Select(o => o.Persists).Should().Equal(1, 1, 1, 2);
        observed.Select(o => o.Pushes).Should().Equal(1, 2, 3, 4);

        // Mid-upload of the first file: it is fully downloaded, but none of it counts until it is on the target.
        observed.Should().OnlyContain(o => o.TransferredBytes == 0);
        Pushes().Select(p => p.TransferredBytes).Should().BeInAscendingOrder();
        _repository.ProgressWrites.Select(w => w.TransferredBytes).Should().BeInAscendingOrder();
        Pushes().Should().OnlyContain(p => p.DeploymentState == "Transferring");
        model.DeploymentState.Should().Be(CustomModelDeploymentState.Completed);
    }

    [Fact]
    public async Task RunAsync_RegistryTokenCancelledMidFile_EndsCancelledAndDeletesTheTempFolder()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        using var registry = new CancellationTokenSource();
        _cancellations.Register(model.Id).Returns(registry.Token);
        _uploader.OnUpload = _ =>
        {
            // What the cancel command does in this process: flag the record, then signal the job.
            model.RequestCancellation("admin-2", _time.GetUtcNow().UtcDateTime);
            registry.Cancel();
            throw new OperationCanceledException(registry.Token);
        };

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Cancelled);
        _uploader.Uploads.Should().BeEmpty();
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeFalse();
        _notifiedStates.Should().Equal("Listing", "Transferring", "Cancelled");
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "Cancelled");
        _cancellations.Received(1).Unregister(model.Id);
    }

    [Fact]
    public async Task RunAsync_CancellationFlagSeenOnFlush_EndsCancelled()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");

        // A cancel handled by another process: only the persisted flag reaches this job.
        _uploader.OnUpload = _ =>
        {
            model.RequestCancellation("admin-2", _time.GetUtcNow().UtcDateTime);
            return Task.CompletedTask;
        };

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Cancelled);
        _uploader.Uploads.Should().ContainSingle();
        _source.Downloads.Should().ContainSingle();
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeFalse();
        _notifiedStates.Should().Equal("Listing", "Transferring", "Cancelled");
    }

    [Fact]
    public async Task RunAsync_RecordFailedBySweepMidRun_StopsWithoutWritingOverIt()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _uploader.OnUpload = _ =>
        {
            model.Fail(CustomModelFailureKind.InterruptedByRestart, "Interrupted.", _time.GetUtcNow().UtcDateTime);
            return Task.CompletedTask;
        };

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Failed);
        model.FailureKind.Should().Be(CustomModelFailureKind.InterruptedByRestart);
        _uploader.Uploads.Should().ContainSingle();
        _notifiedStates.Should().Equal("Listing", "Transferring");
        _logger.Collector.GetSnapshot().Should().NotContain(r => r.Id.Name == "Completed" || r.Id.Name == "Cancelled");
    }

    [Fact]
    public async Task RunAsync_RecordAlreadyCancelled_DoesNothing()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        model.RequestCancellation("admin-2", _time.GetUtcNow().UtcDateTime);

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Cancelled);
        _source.Resolutions.Should().BeEmpty();
        _uploader.Uploads.Should().BeEmpty();
        _notifiedStates.Should().BeEmpty();
        Pushes().Should().BeEmpty();
        await _deploymentTarget.DidNotReceiveWithAnyArgs().GetAsync(default);
    }

    private void RecordPush(CustomModelProgressDto progress)
    {
        lock (_pushes)
        {
            _pushes.Add(progress);
        }
    }

    [Fact]
    public async Task RunAsync_ListingOverTheSizeCap_FailsWhileListing_QuotingSizeAndLimit()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _options = new CustomModelsOptions { MaxDeploymentBytes = 2048 };

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CustomModelDeploymentFailedException>();
        model.FailureKind.Should().Be(CustomModelFailureKind.SizeLimitExceeded);
        model.FailureReason.Should().Contain("4.01 KB").And.Contain("2 KB");
        AssertFailedWhileListing(model);
    }

    [Theory]
    [InlineData("../x", CustomModelFailureKind.UnsafeRepositoryPath)]
    [InlineData("sub/web.config", CustomModelFailureKind.ReservedFileName)]
    public async Task RunAsync_UnsafeListedPath_FailsWhileListing_BeforeAnyUpload(string path, CustomModelFailureKind expected)
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _source.Files[path] = [1, 2, 3];

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CustomModelDeploymentFailedException>();
        model.FailureKind.Should().Be(expected);
        AssertFailedWhileListing(model);
    }

    /// <summary>Failed with nothing planned, downloaded, staged or uploaded (FR-006, FR-008).</summary>
    private void AssertFailedWhileListing(CustomModel model)
    {
        model.DeploymentState.Should().Be(CustomModelDeploymentState.Failed);
        model.TotalFileCount.Should().BeNull();
        _source.Downloads.Should().BeEmpty();
        _uploader.OpenedWith.Should().BeNull();
        _uploader.Uploads.Should().BeEmpty();
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeFalse();
    }

    public static TheoryData<ModelRepositorySourceFailureKind, CustomModelFailureKind> SourceFailures => new()
    {
        { ModelRepositorySourceFailureKind.NotFound, CustomModelFailureKind.SourceNotFound },
        { ModelRepositorySourceFailureKind.Unavailable, CustomModelFailureKind.SourceUnavailable },
        { ModelRepositorySourceFailureKind.GatedOrPrivate, CustomModelFailureKind.SourceGatedOrPrivate },
        { ModelRepositorySourceFailureKind.IntegrityMismatch, CustomModelFailureKind.IntegrityMismatch },
        { ModelRepositorySourceFailureKind.DownloadStalled, CustomModelFailureKind.DownloadStalled },
    };

    public static TheoryData<DeploymentTargetFailureKind, CustomModelFailureKind> TargetFailures => new()
    {
        { DeploymentTargetFailureKind.AuthRejected, CustomModelFailureKind.TargetAuthRejected },
        { DeploymentTargetFailureKind.TlsNotAccepted, CustomModelFailureKind.TargetTlsNotAccepted },
        { DeploymentTargetFailureKind.CertificateInvalid, CustomModelFailureKind.TargetCertificateInvalid },
        { DeploymentTargetFailureKind.ConnectionLost, CustomModelFailureKind.TargetConnectionLost },
        { DeploymentTargetFailureKind.WriteRejected, CustomModelFailureKind.TargetWriteRejected },
        { DeploymentTargetFailureKind.SizeMismatch, CustomModelFailureKind.TargetSizeMismatch },
    };

    [Theory]
    [MemberData(nameof(SourceFailures))]
    public async Task RunAsync_SourceFailure_FailsWithItsKind_AuditsNotifiesAndRethrows(ModelRepositorySourceFailureKind sourceKind, CustomModelFailureKind expected)
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _source.DownloadException = new ModelRepositorySourceException(sourceKind, $"Hugging Face said {sourceKind}.");

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ModelRepositorySourceException>();
        AssertFailedVisibly(model, expected, $"Hugging Face said {sourceKind}.");
    }

    [Theory]
    [MemberData(nameof(TargetFailures))]
    public async Task RunAsync_TargetFailure_FailsWithItsKind_AuditsNotifiesAndRethrows(DeploymentTargetFailureKind targetKind, CustomModelFailureKind expected)
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");

        // The library's exception rides as the inner exception; the uploader never interpolates it.
        _uploader.UploadException = new DeploymentTargetException(targetKind, $"The deployment server said {targetKind}.", new IOException("reset"));

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DeploymentTargetException>();
        AssertFailedVisibly(model, expected, $"The deployment server said {targetKind}.");
    }

    [Fact]
    public async Task RunAsync_UnexpectedException_FailsAsUnexpected_WithoutLeakingItsMessage()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _uploader.UploadException = new InvalidOperationException("internal detail");

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        AssertFailedVisibly(model, CustomModelFailureKind.Unexpected, "The deployment failed unexpectedly. The server log has the details.");
        model.FailureReason.Should().NotContain("internal detail");
    }

    [Fact]
    public async Task RunAsync_DeliveredWhileTransferring_FailsAsInterruptedByRestart_WithoutRethrowing()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        model.StartListing(_time.GetUtcNow().UtcDateTime);
        model.BeginTransfer(FakeModelRepositorySource.CommitSha, 4103, 2, _options.MaxDeploymentBytes);
        _tempStorage.PrepareJobDirectory(model.Id);

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Failed);
        model.FailureKind.Should().Be(CustomModelFailureKind.InterruptedByRestart);
        _notifiedStates.Should().Equal("Failed");
        _source.Resolutions.Should().BeEmpty();
        _uploader.OpenedWith.Should().BeNull();
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeFalse();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "Failed");
        _cancellations.DidNotReceiveWithAnyArgs().Register(default);
    }

    [Fact]
    public async Task RunAsync_FailureAfterTheSweepFailedTheRecord_LeavesItAndReturns()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _uploader.OnUpload = _ =>
        {
            model.Fail(CustomModelFailureKind.InterruptedByRestart, "Interrupted.", _time.GetUtcNow().UtcDateTime);
            throw new DeploymentTargetException(DeploymentTargetFailureKind.ConnectionLost, "The connection dropped.");
        };

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        model.FailureKind.Should().Be(CustomModelFailureKind.InterruptedByRestart);
        model.FailureReason.Should().Be("Interrupted.");
        _notifiedStates.Should().Equal("Listing", "Transferring");
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeFalse();

        // The job's own failure is still audited, so nothing it saw is lost.
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "Failed" && r.Exception is DeploymentTargetException);
    }

    [Fact]
    public async Task RunAsync_ConflictOnTheFailureWrite_ReloadsAndRetriesOnce()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _uploader.OnUpload = _ =>
        {
            // A progress-only write from elsewhere bumps the RowVersion under the job.
            _repository.UpdateConflicts = 1;
            throw new DeploymentTargetException(DeploymentTargetFailureKind.WriteRejected, "The deployment server refused the write.");
        };
        var appliesBefore = 0;
        _repository.OnConflict = _ => appliesBefore = _repository.UpdateApplyCount;

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DeploymentTargetException>();
        _repository.UpdateApplyCount.Should().Be(appliesBefore + 1);
        AssertFailedVisibly(model, CustomModelFailureKind.TargetWriteRejected, "The deployment server refused the write.");
    }

    [Fact]
    public async Task RunAsync_ConflictWithTheSweep_ReEvaluatesAndLeavesTheSweepsFailure()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _uploader.OnUpload = _ =>
        {
            _repository.UpdateConflicts = 1;
            _repository.OnConflict = m => m.Fail(CustomModelFailureKind.InterruptedByRestart, "Interrupted.", _time.GetUtcNow().UtcDateTime);
            throw new DeploymentTargetException(DeploymentTargetFailureKind.ConnectionLost, "The connection dropped.");
        };

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        model.FailureKind.Should().Be(CustomModelFailureKind.InterruptedByRestart);
        _notifiedStates.Should().NotContain("Failed");
    }

    [Fact]
    public async Task RunAsync_SecondConflictOnTheFailureWrite_LogsBothErrors_AndSurfacesTheWriteFailure()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _uploader.OnUpload = _ =>
        {
            _repository.UpdateConflicts = 2;
            throw new DeploymentTargetException(DeploymentTargetFailureKind.ConnectionLost, "The connection dropped.");
        };

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<AskLucy.Domain.Common.ConcurrencyConflictException>();
        model.IsInProgress.Should().BeTrue("the startup sweep fails it later");
        var records = _logger.Collector.GetSnapshot();
        records.Should().ContainSingle(r => r.Id.Name == "Failed" && r.Exception is DeploymentTargetException);
        records.Should().ContainSingle(r => r.Id.Name == "LogTerminalWriteFailed");
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_NotEnoughFreeDiskSpace_FailsWithDiskSpaceExhausted_BeforeDownloading()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _tempStorage.FreeSpace = CustomModelDeploymentJob.DiskSpaceHeadroomBytes;

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CustomModelDeploymentFailedException>();
        model.FailureKind.Should().Be(CustomModelFailureKind.DiskSpaceExhausted);
        _source.Downloads.Should().BeEmpty();
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_DiskFullWhileStaging_FailsWithDiskSpaceExhausted()
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _source.DownloadException = new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));

        var act = () => CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IOException>();
        model.FailureKind.Should().Be(CustomModelFailureKind.DiskSpaceExhausted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_PlainFtp_LogsAWarningAtTheStart(bool allowPlainFtp)
    {
        var model = await SeedAsync("https://huggingface.co/Supertone/supertonic-3");
        _deploymentTarget.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new DeploymentTargetSettings("ftp.example.test", 21, "deployer", "not-a-real-password", "/site", allowPlainFtp));

        await CreateJob().RunAsync(model.Id, TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot().Count(r => r.Id.Name == "PlainFtpInUse" && r.Level == Microsoft.Extensions.Logging.LogLevel.Warning)
            .Should().Be(allowPlainFtp ? 1 : 0);
    }

    /// <summary>Failed with the kind and reason, audited once, pushed to admins, staging gone, and no log record carries the password.</summary>
    private void AssertFailedVisibly(CustomModel model, CustomModelFailureKind kind, string reason)
    {
        model.DeploymentState.Should().Be(CustomModelDeploymentState.Failed);
        model.FailureKind.Should().Be(kind);
        model.FailureReason.Should().Be(reason);
        _notifiedStates.Should().EndWith("Failed");
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeFalse();

        var records = _logger.Collector.GetSnapshot();
        records.Should().ContainSingle(r => r.Id.Name == "Failed" && r.Message.Contains(kind.ToString()));
        records.Should().NotContain(r => r.Message.Contains("not-a-real-password") || (r.Exception != null && r.Exception.ToString().Contains("not-a-real-password")));
    }

    private List<CustomModelProgressDto> Pushes()
    {
        lock (_pushes)
        {
            return [.. _pushes];
        }
    }

    private async Task WaitForPushesAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Pushes().Count < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Expected {count} progress pushes, saw {Pushes().Count}.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private async Task<CustomModel> SeedAsync(string sourceUrl)
    {
        HuggingFaceModelSource.TryParse(sourceUrl, out var source, out _).Should().BeTrue();
        DeploymentDestination.TryCreate(Destination, CustomModelsOptions.DefaultAllowedDestinationPrefixes, out var destination, out _).Should().BeTrue();
        var model = CustomModel.Create("supertonic-3", source!, destination!, "admin-1");
        await _repository.AddAsync(model, TestContext.Current.CancellationToken);
        return model;
    }

    private CustomModelDeploymentJob CreateJob()
    {
        var options = Substitute.For<IOptionsMonitor<CustomModelsOptions>>();
        options.CurrentValue.Returns(_options);

        return new CustomModelDeploymentJob(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _source,
            _uploader,
            _deploymentTarget,
            _notifier,
            _cancellations,
            _tempStorage,
            options,
            _time,
            _logger);
    }
}
