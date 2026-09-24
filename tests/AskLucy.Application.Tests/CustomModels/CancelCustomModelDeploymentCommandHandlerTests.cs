using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.CustomModels.Commands.CancelCustomModelDeployment;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.CustomModels;
using FluentAssertions;
using Hangfire;
using Hangfire.States;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AskLucy.Application.Tests.CustomModels;

public sealed class CancelCustomModelDeploymentCommandHandlerTests
{
    private const string AdminUserId = "admin-2";

    private readonly FakeCustomModelRepository _repository = new();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private readonly ICustomModelDeploymentCancellationRegistry _cancellations = Substitute.For<ICustomModelDeploymentCancellationRegistry>();
    private readonly ICustomModelDeploymentNotifier _notifier = Substitute.For<ICustomModelDeploymentNotifier>();
    private readonly FakeLogger<CancelCustomModelDeploymentCommandHandler> _logger = new();

    [Fact]
    public async Task Handle_Queued_CancelsAtOnce_AndDeletesTheHangfireJob()
    {
        var model = await SeedAsync();

        var result = await Cancel(model.Id);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Cancelled);
        model.CancelledByUserId.Should().Be(AdminUserId);
        result.DeploymentState.Should().Be("Cancelled");
        _jobs.Received(1).ChangeState("job-1", Arg.Any<DeletedState>(), Arg.Any<string>());
        _cancellations.DidNotReceiveWithAnyArgs().TryCancel(default);
        await _notifier.Received(1).NotifyStateChangedAsync(
            Arg.Is<CustomModelSummaryDto>(s => s!.Id == model.Id && s.DeploymentState == "Cancelled"),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_QueuedJobCantBeDeleted_StillCancels_AndLogsIt()
    {
        var model = await SeedAsync();
        _jobs.ChangeState(Arg.Any<string>(), Arg.Any<IState>(), Arg.Any<string>()).Throws(new InvalidOperationException("storage down"));

        var result = await Cancel(model.Id);

        result.DeploymentState.Should().Be("Cancelled");
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "LogQueuedJobDeleteFailed" && r.Exception != null);
    }

    [Fact]
    public async Task Handle_Transferring_SetsTheFlag_AndSignalsTheRegistry()
    {
        var model = await SeedAsync(CustomModelDeploymentState.Transferring);
        _cancellations.TryCancel(model.Id).Returns(true);

        var result = await Cancel(model.Id);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Transferring);
        model.CancellationRequestedAtUtc.Should().NotBeNull();
        result.DeploymentState.Should().Be("Transferring");
        _cancellations.Received(1).TryCancel(model.Id);
        _jobs.DidNotReceiveWithAnyArgs().ChangeState(default!, default!, default);
    }

    [Theory]
    [InlineData(CustomModelDeploymentState.Completed)]
    [InlineData(CustomModelDeploymentState.Failed)]
    [InlineData(CustomModelDeploymentState.Cancelled)]
    public async Task Handle_TerminalState_Conflicts_AndAuditsNothing(CustomModelDeploymentState state)
    {
        var model = await SeedAsync(state);

        var act = () => Cancel(model.Id);

        await act.Should().ThrowAsync<CustomModelNotInProgressException>();
        _logger.Collector.GetSnapshot().Should().NotContain(r => r.Id.Name == "CancelRequested");
        await _notifier.DidNotReceiveWithAnyArgs().NotifyStateChangedAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_Missing_NotFound()
    {
        var act = () => Cancel(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_AuditsCancelRequested_WithTheActor()
    {
        var model = await SeedAsync(CustomModelDeploymentState.Transferring);

        await Cancel(model.Id);

        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "CancelRequested" && r.Message.Contains(AdminUserId));
    }

    [Fact]
    public async Task Handle_OneConcurrencyConflict_RetriesAndSucceeds()
    {
        var model = await SeedAsync(CustomModelDeploymentState.Transferring);
        _repository.UpdateConflicts = 1;

        await Cancel(model.Id);

        _repository.UpdateApplyCount.Should().Be(2);
        model.CancellationRequestedAtUtc.Should().NotBeNull();
        _cancellations.Received(1).TryCancel(model.Id);
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "CancelRequested");
    }

    [Fact]
    public async Task Handle_SecondConcurrencyConflict_IsSurfaced_BeforeAnyAuditOrSignal()
    {
        var model = await SeedAsync(CustomModelDeploymentState.Transferring);
        _repository.UpdateConflicts = 2;

        var act = () => Cancel(model.Id);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        _logger.Collector.GetSnapshot().Should().NotContain(r => r.Id.Name == "CancelRequested");
        _cancellations.DidNotReceiveWithAnyArgs().TryCancel(default);
    }

    private Task<CustomModelSummaryDto> Cancel(Guid id) =>
        CreateHandler().Handle(new CancelCustomModelDeploymentCommand(id), TestContext.Current.CancellationToken);

    private async Task<CustomModel> SeedAsync(CustomModelDeploymentState state = CustomModelDeploymentState.Queued)
    {
        HuggingFaceModelSource.TryParse("https://huggingface.co/Supertone/supertonic-3", out var source, out _).Should().BeTrue();
        DeploymentDestination.TryCreate("Models/supertonic-3", CustomModelsOptions.DefaultAllowedDestinationPrefixes, out var destination, out _).Should().BeTrue();
        var model = CustomModel.Create("supertonic-3", source!, destination!, "admin-1");
        model.AssignBackgroundJob("job-1");

        var now = DateTime.UtcNow;
        if (state != CustomModelDeploymentState.Queued)
        {
            model.StartListing(now);
        }

        if (state is CustomModelDeploymentState.Transferring or CustomModelDeploymentState.Completed)
        {
            model.BeginTransfer(new string('a', 40), 10, 1, 100);
        }

        switch (state)
        {
            case CustomModelDeploymentState.Completed:
                model.RecordProgress(10, 1, null, null, null);
                model.Complete(now);
                break;
            case CustomModelDeploymentState.Failed:
                model.Fail(CustomModelFailureKind.SourceNotFound, "Not found.", now);
                break;
            case CustomModelDeploymentState.Cancelled:
                model.RequestCancellation("admin-1", now);
                model.MarkCancelled(now);
                break;
        }

        await _repository.AddAsync(model, TestContext.Current.CancellationToken);
        return model;
    }

    private CancelCustomModelDeploymentCommandHandler CreateHandler()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AdminUserId);

        return new CancelCustomModelDeploymentCommandHandler(
            _repository,
            _jobs,
            _cancellations,
            _notifier,
            new CustomModelSummaryBuilder(Substitute.For<IUserAdminRepository>(), []),
            currentUser,
            TimeProvider.System,
            _logger);
    }
}
