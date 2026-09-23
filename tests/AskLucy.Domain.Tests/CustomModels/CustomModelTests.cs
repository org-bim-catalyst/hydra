using AskLucy.Domain.Common;
using AskLucy.Domain.CustomModels;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.CustomModels;

/// <summary>specs/072 data-model.md — the <see cref="CustomModel"/> state machine. An illegal transition throws <see cref="DomainRuleViolationException"/>.</summary>
public sealed class CustomModelTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const long Cap = 1_000;
    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    public enum Stage
    {
        Queued,
        Listing,
        Transferring,
        Completed,
        Failed,
        Cancelled,
    }

    [Fact]
    public void Create_ShouldStartQueued_Unavailable_AndInProgress()
    {
        var model = NewModel();

        model.Id.Should().NotBe(Guid.Empty);
        model.Name.Should().Be("supertonic-3");
        model.RepositoryId.Should().Be("Supertone/supertonic-3");
        model.Revision.Should().Be("main");
        model.SourceUrl.Should().Be("https://huggingface.co/Supertone/supertonic-3");
        model.Destination.Should().Be("Models/supertonic-3");
        model.SubmittedByUserId.Should().Be("admin-1");
        model.CreatedBy.Should().Be("admin-1");
        model.DeploymentState.Should().Be(CustomModelDeploymentState.Queued);
        model.Availability.Should().Be(CustomModelAvailability.Unavailable);
        model.IsInProgress.Should().BeTrue();
        model.TransferredBytes.Should().Be(0);
        model.CompletedFileCount.Should().Be(0);
        model.OverwrittenFileCount.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\u0001b")]
    public void Create_ShouldReject_AnInvalidName(string name)
    {
        var act = () => CustomModel.Create(name, Source(), Destination(), "admin-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Create_ShouldReject_ANameLongerThanTheColumn()
    {
        var act = () => CustomModel.Create(new string('a', CustomModel.MaxNameLength + 1), Source(), Destination(), "admin-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Create_ShouldTrimTheName()
    {
        CustomModel.Create("  my model  ", Source(), Destination(), "admin-1").Name.Should().Be("my model");
    }

    [Fact]
    public void AssignBackgroundJob_ShouldRecordTheJobId_WhileQueued()
    {
        var model = NewModel();

        model.AssignBackgroundJob("42");

        model.BackgroundJobId.Should().Be("42");
    }

    [Fact]
    public void StartListing_ShouldMoveQueuedToListing_AndStampTheStart()
    {
        var model = NewModel();

        model.StartListing(Now);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Listing);
        model.StartedAtUtc.Should().Be(Now);
        model.IsInProgress.Should().BeTrue();
    }

    [Fact]
    public void CanonicaliseRepositoryId_ShouldReplaceTheTypedCasing_WhileListing()
    {
        var model = At(Stage.Listing);

        model.CanonicaliseRepositoryId("supertone/SUPERTONIC-3");

        model.RepositoryId.Should().Be("supertone/SUPERTONIC-3");
    }

    [Fact]
    public void CanonicaliseRepositoryId_ShouldRefuse_ADifferentRepository()
    {
        var model = At(Stage.Listing);

        var act = () => model.CanonicaliseRepositoryId("Supertone/other");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void BeginTransfer_ShouldMoveListingToTransferring_AndRecordTheTotals()
    {
        var model = At(Stage.Listing);

        model.BeginTransfer(Sha, totalBytes: 900, fileCount: 3, maxDeploymentBytes: Cap);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Transferring);
        model.ResolvedCommitSha.Should().Be(Sha);
        model.TotalBytes.Should().Be(900);
        model.TotalFileCount.Should().Be(3);
        model.IsInProgress.Should().BeTrue();
    }

    [Fact]
    public void BeginTransfer_ShouldThrowSizeLimitExceeded_WhenTheTotalIsOverTheCap()
    {
        var model = At(Stage.Listing);

        var act = () => model.BeginTransfer(Sha, totalBytes: Cap + 1, fileCount: 3, maxDeploymentBytes: Cap);

        act.Should().Throw<CustomModelDeploymentFailedException>()
            .Which.Kind.Should().Be(CustomModelFailureKind.SizeLimitExceeded);
        model.DeploymentState.Should().Be(CustomModelDeploymentState.Listing);
    }

    [Fact]
    public void BeginTransfer_ShouldAcceptExactlyTheCap()
    {
        var model = At(Stage.Listing);

        model.BeginTransfer(Sha, totalBytes: Cap, fileCount: 1, maxDeploymentBytes: Cap);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Transferring);
    }

    [Theory]
    [InlineData("not-a-sha")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456")]
    public void BeginTransfer_ShouldReject_AMalformedCommitSha(string sha)
    {
        var model = At(Stage.Listing);

        var act = () => model.BeginTransfer(sha, 1, 1, Cap);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void RecordProgress_ShouldOnlyMoveForward()
    {
        var model = At(Stage.Transferring);

        model.RecordProgress(transferredBytes: 500, completedFileCount: 1, currentFilePath: "b.bin", currentFileBytes: 200, currentFileTotalBytes: 600);
        model.RecordProgress(transferredBytes: 400, completedFileCount: 0, currentFilePath: "a.bin", currentFileBytes: 100, currentFileTotalBytes: 300);

        model.TransferredBytes.Should().Be(500);
        model.CompletedFileCount.Should().Be(1);
        model.CurrentFilePath.Should().Be("b.bin");
    }

    [Fact]
    public void RecordOverwrite_ShouldReturnTheRow_AndIncrementTheCount_WhileTransferring()
    {
        var model = At(Stage.Transferring);

        var row = model.RecordOverwrite("Models/supertonic-3/onnx/a.onnx", 1234, Now);

        row.CustomModelId.Should().Be(model.Id);
        row.RelativePath.Should().Be("Models/supertonic-3/onnx/a.onnx");
        row.PreviousSizeBytes.Should().Be(1234);
        row.OverwrittenAtUtc.Should().Be(Now);
        model.OverwrittenFileCount.Should().Be(1);
    }

    [Theory]
    [InlineData(Stage.Queued)]
    [InlineData(Stage.Listing)]
    [InlineData(Stage.Completed)]
    [InlineData(Stage.Failed)]
    [InlineData(Stage.Cancelled)]
    public void RecordOverwrite_ShouldThrow_OutsideTransferring(Stage stage)
    {
        var model = At(stage);

        var act = () => model.RecordOverwrite("Models/x/a", 1, Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Complete_ShouldMoveTransferringToCompleted_WhenEveryFileIsDone()
    {
        var model = At(Stage.Transferring);
        model.RecordProgress(900, 3, "c.bin", 300, 300);

        model.Complete(Now);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Completed);
        model.IsInProgress.Should().BeFalse();
        model.FinishedAtUtc.Should().Be(Now);
        model.CurrentFilePath.Should().BeNull();
        model.CurrentFileBytes.Should().BeNull();
        model.CurrentFileTotalBytes.Should().BeNull();
        model.Availability.Should().Be(CustomModelAvailability.Unavailable);
    }

    [Fact]
    public void Complete_ShouldThrow_WhenAFileIsStillOutstanding()
    {
        var model = At(Stage.Transferring);
        model.RecordProgress(600, 2, "c.bin", 0, 300);

        var act = () => model.Complete(Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(Stage.Queued)]
    [InlineData(Stage.Listing)]
    [InlineData(Stage.Transferring)]
    public void Fail_ShouldEndAnInProgressDeployment(Stage stage)
    {
        var model = At(stage);

        model.Fail(CustomModelFailureKind.TargetConnectionLost, "The connection was lost.", Now);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Failed);
        model.FailureKind.Should().Be(CustomModelFailureKind.TargetConnectionLost);
        model.FailureReason.Should().Be("The connection was lost.");
        model.IsInProgress.Should().BeFalse();
        model.FinishedAtUtc.Should().Be(Now);
        model.CurrentFilePath.Should().BeNull();
    }

    [Fact]
    public void Fail_ShouldTruncateAReasonLongerThanTheColumn()
    {
        var model = At(Stage.Listing);

        model.Fail(CustomModelFailureKind.Unexpected, new string('x', 5_000), Now);

        model.FailureReason!.Length.Should().Be(CustomModel.MaxFailureReasonLength);
    }

    [Fact]
    public void RequestCancellation_ShouldCancelAQueuedModelStraightAway()
    {
        var model = NewModel();

        model.RequestCancellation("admin-2", Now);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Cancelled);
        model.CancellationRequestedAtUtc.Should().Be(Now);
        model.CancelledByUserId.Should().Be("admin-2");
        model.IsInProgress.Should().BeFalse();
        model.FinishedAtUtc.Should().Be(Now);
    }

    [Theory]
    [InlineData(Stage.Listing)]
    [InlineData(Stage.Transferring)]
    public void RequestCancellation_ShouldOnlyFlagARunningModel(Stage stage)
    {
        var model = At(stage);

        model.RequestCancellation("admin-2", Now);

        model.DeploymentState.Should().Be(stage == Stage.Listing ? CustomModelDeploymentState.Listing : CustomModelDeploymentState.Transferring);
        model.CancellationRequestedAtUtc.Should().Be(Now);
        model.CancelledByUserId.Should().Be("admin-2");
        model.IsInProgress.Should().BeTrue();
    }

    [Fact]
    public void RequestCancellation_ShouldKeepTheFirstRequest_WhenRepeated()
    {
        var model = At(Stage.Transferring);

        model.RequestCancellation("admin-2", Now);
        model.RequestCancellation("admin-3", Now.AddMinutes(1));

        model.CancellationRequestedAtUtc.Should().Be(Now);
        model.CancelledByUserId.Should().Be("admin-2");
    }

    [Theory]
    [InlineData(Stage.Completed)]
    [InlineData(Stage.Failed)]
    [InlineData(Stage.Cancelled)]
    public void RequestCancellation_ShouldThrow_OnceTheDeploymentHasEnded(Stage stage)
    {
        var act = () => At(stage).RequestCancellation("admin-2", Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(Stage.Listing)]
    [InlineData(Stage.Transferring)]
    public void MarkCancelled_ShouldEndARunningModel_ThatWasAskedToCancel(Stage stage)
    {
        var model = At(stage);
        model.RequestCancellation("admin-2", Now);

        model.MarkCancelled(Now.AddSeconds(3));

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Cancelled);
        model.IsInProgress.Should().BeFalse();
        model.FinishedAtUtc.Should().Be(Now.AddSeconds(3));
        model.CurrentFilePath.Should().BeNull();
    }

    [Fact]
    public void MarkCancelled_ShouldThrow_WhenNoCancellationWasRequested()
    {
        var act = () => At(Stage.Transferring).MarkCancelled(Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void MakeAvailable_ShouldBeAllowed_OnceCompleted()
    {
        var model = At(Stage.Completed);

        model.MakeAvailable();

        model.Availability.Should().Be(CustomModelAvailability.Available);
    }

    [Theory]
    [InlineData(Stage.Queued)]
    [InlineData(Stage.Listing)]
    [InlineData(Stage.Transferring)]
    [InlineData(Stage.Failed)]
    [InlineData(Stage.Cancelled)]
    public void MakeAvailable_ShouldThrow_UnlessCompleted(Stage stage)
    {
        var act = () => At(stage).MakeAvailable();

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(Stage.Queued)]
    [InlineData(Stage.Transferring)]
    [InlineData(Stage.Completed)]
    [InlineData(Stage.Failed)]
    public void MakeUnavailable_ShouldBeAllowed_InAnyState(Stage stage)
    {
        var model = At(stage);

        model.MakeUnavailable();

        model.Availability.Should().Be(CustomModelAvailability.Unavailable);
    }

    [Fact]
    public void MakeUnavailable_ShouldUndoMakeAvailable()
    {
        var model = At(Stage.Completed);
        model.MakeAvailable();

        model.MakeUnavailable();

        model.Availability.Should().Be(CustomModelAvailability.Unavailable);
    }

    [Theory]
    [InlineData(Stage.Failed)]
    [InlineData(Stage.Cancelled)]
    public void Remove_ShouldSoftDelete_AFailedOrCancelledModel(Stage stage)
    {
        var model = At(stage);

        model.Remove("admin-2", Now);

        model.IsDeleted.Should().BeTrue();
        model.DeletedAtUtc.Should().Be(Now);
        model.DeletedBy.Should().Be("admin-2");
    }

    [Theory]
    [InlineData(Stage.Queued)]
    [InlineData(Stage.Listing)]
    [InlineData(Stage.Transferring)]
    [InlineData(Stage.Completed)]
    public void Remove_ShouldThrow_UnlessFailedOrCancelled(Stage stage)
    {
        var act = () => At(stage).Remove("admin-2", Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(Stage.Listing)]
    [InlineData(Stage.Transferring)]
    [InlineData(Stage.Completed)]
    [InlineData(Stage.Failed)]
    [InlineData(Stage.Cancelled)]
    public void StartListing_ShouldThrow_UnlessQueued(Stage stage)
    {
        var act = () => At(stage).StartListing(Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(Stage.Queued)]
    [InlineData(Stage.Transferring)]
    [InlineData(Stage.Completed)]
    [InlineData(Stage.Failed)]
    [InlineData(Stage.Cancelled)]
    public void BeginTransfer_ShouldThrow_UnlessListing(Stage stage)
    {
        var act = () => At(stage).BeginTransfer(Sha, 1, 1, Cap);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(Stage.Queued)]
    [InlineData(Stage.Listing)]
    [InlineData(Stage.Completed)]
    [InlineData(Stage.Failed)]
    [InlineData(Stage.Cancelled)]
    public void Complete_ShouldThrow_UnlessTransferring(Stage stage)
    {
        var act = () => At(stage).Complete(Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(Stage.Completed)]
    [InlineData(Stage.Failed)]
    [InlineData(Stage.Cancelled)]
    public void Fail_ShouldThrow_OnceTheDeploymentHasEnded(Stage stage)
    {
        var act = () => At(stage).Fail(CustomModelFailureKind.Unexpected, "x", Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(Stage.Queued)]
    [InlineData(Stage.Completed)]
    [InlineData(Stage.Failed)]
    [InlineData(Stage.Cancelled)]
    public void MarkCancelled_ShouldThrow_UnlessRunning(Stage stage)
    {
        var act = () => At(stage).MarkCancelled(Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(Stage.Listing)]
    [InlineData(Stage.Transferring)]
    [InlineData(Stage.Completed)]
    [InlineData(Stage.Failed)]
    [InlineData(Stage.Cancelled)]
    public void AssignBackgroundJob_ShouldThrow_UnlessQueued(Stage stage)
    {
        var act = () => At(stage).AssignBackgroundJob("42");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(Stage.Queued, true)]
    [InlineData(Stage.Listing, true)]
    [InlineData(Stage.Transferring, true)]
    [InlineData(Stage.Completed, false)]
    [InlineData(Stage.Failed, false)]
    [InlineData(Stage.Cancelled, false)]
    public void IsInProgress_ShouldTrackTheState(Stage stage, bool expected) =>
        At(stage).IsInProgress.Should().Be(expected);

    private static HuggingFaceModelSource Source()
    {
        HuggingFaceModelSource.TryParse("https://huggingface.co/Supertone/supertonic-3", out var source, out _).Should().BeTrue();
        return source!;
    }

    private static DeploymentDestination Destination()
    {
        DeploymentDestination.TryCreate("Models/supertonic-3", ["Models"], out var destination, out _).Should().BeTrue();
        return destination!;
    }

    private static CustomModel NewModel() => CustomModel.Create("supertonic-3", Source(), Destination(), "admin-1");

    private static CustomModel At(Stage stage)
    {
        var model = NewModel();
        if (stage == Stage.Queued)
        {
            return model;
        }

        if (stage == Stage.Cancelled)
        {
            model.RequestCancellation("admin-2", Now);
            return model;
        }

        if (stage == Stage.Failed)
        {
            model.Fail(CustomModelFailureKind.SourceNotFound, "Not found.", Now);
            return model;
        }

        model.StartListing(Now);
        if (stage == Stage.Listing)
        {
            return model;
        }

        model.BeginTransfer(Sha, totalBytes: 900, fileCount: 3, maxDeploymentBytes: Cap);
        if (stage == Stage.Transferring)
        {
            return model;
        }

        model.RecordProgress(900, 3, null, null, null);
        model.Complete(Now);
        return model;
    }
}
