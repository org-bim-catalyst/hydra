using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.CustomModels.Commands.SubmitCustomModelDeployment;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.CustomModels;
using FluentAssertions;
using FluentValidation;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AskLucy.Application.Tests.CustomModels;

public sealed class SubmitCustomModelDeploymentCommandHandlerTests
{
    private const string AdminUserId = "admin-1";
    private const string Source = "https://huggingface.co/Supertone/supertonic-3";
    private const string Destination = "Models/supertonic-3";

    private readonly FakeCustomModelRepository _repository = new();
    private readonly IDeploymentTargetSettingsProvider _deploymentTarget = Substitute.For<IDeploymentTargetSettingsProvider>();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private readonly ICustomModelDeploymentNotifier _notifier = Substitute.For<ICustomModelDeploymentNotifier>();
    private readonly FakeLogger<SubmitCustomModelDeploymentCommandHandler> _logger = new();

    public SubmitCustomModelDeploymentCommandHandlerTests()
    {
        _deploymentTarget.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new DeploymentTargetSettings("ftp.example.test", 21, "deployer", "not-a-real-password", "/site", AllowPlainFtp: false));
        _jobs.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns("job-1");
    }

    [Fact]
    public async Task Handle_Valid_SavesQueuedRecord_EnqueuesJob_AuditsAndNotifies()
    {
        var result = await CreateHandler().Handle(new SubmitCustomModelDeploymentCommand(Source, Destination, null), TestContext.Current.CancellationToken);

        var saved = _repository.Models.Should().ContainSingle().Subject;
        saved.DeploymentState.Should().Be(CustomModelDeploymentState.Queued);
        saved.Name.Should().Be("supertonic-3");
        saved.RepositoryId.Should().Be("Supertone/supertonic-3");
        saved.Destination.Should().Be(Destination);
        saved.SubmittedByUserId.Should().Be(AdminUserId);
        saved.BackgroundJobId.Should().Be("job-1");

        _jobs.Received(1).Create(Arg.Is<Job>(j => j!.Method.Name == "RunAsync" && (Guid)j.Args[0] == saved.Id), Arg.Any<IState>());
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "Submitted");
        await _notifier.Received(1).NotifyStateChangedAsync(
            Arg.Is<CustomModelSummaryDto>(s => s!.Id == saved.Id && s.DeploymentState == "Queued"),
            false,
            Arg.Any<CancellationToken>());

        result.Id.Should().Be(saved.Id);
        result.IgnoredFilePath.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NotConfigured_ThrowsAndSavesNothing()
    {
        _deploymentTarget.GetAsync(Arg.Any<CancellationToken>()).Returns((DeploymentTargetSettings?)null);

        var act = () => CreateHandler().Handle(new SubmitCustomModelDeploymentCommand(Source, Destination, null), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CustomModelDeploymentNotConfiguredException>();
        _repository.Models.Should().BeEmpty();
        _jobs.DidNotReceiveWithAnyArgs().Create(default!, default!);
    }

    [Fact]
    public async Task Handle_DerivedNameTaken_AndNoNameGiven_ConflictNamesTheModel()
    {
        await SeedExistingAsync("Supertonic-3");

        var act = () => CreateHandler().Handle(new SubmitCustomModelDeploymentCommand(Source, "Models/other", null), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<DuplicateResourceException>()).WithMessage("*supertonic-3*");
        _repository.Models.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_ExplicitName_SucceedsWhenDerivedNameIsTaken()
    {
        await SeedExistingAsync("supertonic-3");

        var result = await CreateHandler().Handle(new SubmitCustomModelDeploymentCommand(Source, "Models/other", "  Supertonic staging "), TestContext.Current.CancellationToken);

        result.Name.Should().Be("Supertonic staging");
        _repository.Models.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_DestinationOverlapsActiveJob_ConflictNamesThatModel()
    {
        _repository.OverlappingActiveJob = CreateModel("Kokoro", "Models/A/b");

        var act = () => CreateHandler().Handle(new SubmitCustomModelDeploymentCommand(Source, "Models/a", null), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<DuplicateResourceException>()).WithMessage("*'Kokoro'*Models/A/b*Models/a*");
        _repository.Models.Should().BeEmpty();
        _jobs.DidNotReceiveWithAnyArgs().Create(default!, default!);
    }

    [Fact]
    public async Task Handle_ResolveUrl_ReturnsIgnoredFilePath()
    {
        var result = await CreateHandler().Handle(
            new SubmitCustomModelDeploymentCommand("https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/model.onnx", Destination, null),
            TestContext.Current.CancellationToken);

        result.IgnoredFilePath.Should().Be("onnx/model.onnx");
        result.Revision.Should().Be("main");
    }

    [Fact]
    public async Task Handle_InvalidSource_ThrowsValidationKeyedBySource()
    {
        var act = () => CreateHandler().Handle(new SubmitCustomModelDeploymentCommand("https://example.com/Supertone/supertonic-3", Destination, null), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "source");
    }

    [Fact]
    public async Task Handle_SaveThrows_NoNotification_NoAudit_NothingEnqueued()
    {
        _repository.AddException = new InvalidOperationException("database unavailable");

        var act = () => CreateHandler().Handle(new SubmitCustomModelDeploymentCommand(Source, Destination, null), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _notifier.DidNotReceiveWithAnyArgs().NotifyStateChangedAsync(default!, default, default);
        _logger.Collector.GetSnapshot().Should().NotContain(r => r.Id.Name == "Submitted");
        _jobs.DidNotReceiveWithAnyArgs().Create(default!, default!);
    }

    [Fact]
    public async Task Handle_EnqueueThrows_FailsTheRecordAndRethrows()
    {
        _jobs.Create(Arg.Any<Job>(), Arg.Any<IState>()).Throws(new InvalidOperationException("storage down"));

        var act = () => CreateHandler().Handle(new SubmitCustomModelDeploymentCommand(Source, Destination, null), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var saved = _repository.Models.Should().ContainSingle().Subject;
        saved.DeploymentState.Should().Be(CustomModelDeploymentState.Failed);
        saved.FailureKind.Should().Be(CustomModelFailureKind.Unexpected);
        saved.IsInProgress.Should().BeFalse();
    }

    private async Task SeedExistingAsync(string name) =>
        await _repository.AddAsync(CreateModel(name, "Models/existing"), TestContext.Current.CancellationToken);

    private static CustomModel CreateModel(string name, string destination)
    {
        HuggingFaceModelSource.TryParse(Source, out var source, out _).Should().BeTrue();
        DeploymentDestination.TryCreate(destination, CustomModelsOptions.DefaultAllowedDestinationPrefixes, out var target, out _).Should().BeTrue();
        return CustomModel.Create(name, source!, target!, "someone-else");
    }

    private SubmitCustomModelDeploymentCommandHandler CreateHandler()
    {
        var options = Substitute.For<IOptionsMonitor<CustomModelsOptions>>();
        options.CurrentValue.Returns(new CustomModelsOptions());
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AdminUserId);

        return new SubmitCustomModelDeploymentCommandHandler(
            _repository,
            _deploymentTarget,
            _jobs,
            _notifier,
            new CustomModelSummaryBuilder(Substitute.For<IUserAdminRepository>(), []),
            options,
            currentUser,
            TimeProvider.System,
            _logger);
    }
}
