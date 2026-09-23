using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.CustomModels.Commands.SetCustomModelAvailability;
using AskLucy.Domain.Common;
using AskLucy.Domain.CustomModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;

namespace AskLucy.Application.Tests.CustomModels;

/// <summary>specs/072 T072 (FR-030, FR-034, FR-027).</summary>
public sealed class SetCustomModelAvailabilityCommandHandlerTests
{
    private const string AdminUserId = "admin-2";

    private readonly FakeCustomModelRepository _repository = new();
    private readonly ICustomModelDeploymentNotifier _notifier = Substitute.For<ICustomModelDeploymentNotifier>();
    private readonly FakeLogger<SetCustomModelAvailabilityCommandHandler> _logger = new();

    [Fact]
    public async Task Handle_Completed_BecomesAvailable_AuditsAndNotifies()
    {
        var model = await SeedAsync(CustomModelDeploymentState.Completed);

        var result = await Set(model.Id, CustomModelAvailability.Available);

        model.Availability.Should().Be(CustomModelAvailability.Available);
        result.Availability.Should().Be("Available");
        _logger.Collector.GetSnapshot().Should().ContainSingle(r =>
            r.Id.Name == "AvailabilityChanged" && r.Message.Contains(AdminUserId) && r.Message.Contains("Unavailable -> Available"));
        await _notifier.Received(1).NotifyStateChangedAsync(
            Arg.Is<CustomModelSummaryDto>(s => s!.Id == model.Id && s.Availability == "Available"),
            false,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CustomModelDeploymentState.Queued)]
    [InlineData(CustomModelDeploymentState.Transferring)]
    [InlineData(CustomModelDeploymentState.Failed)]
    [InlineData(CustomModelDeploymentState.Cancelled)]
    public async Task Handle_NotCompleted_IsRejected_WithTheReason(CustomModelDeploymentState state)
    {
        var model = await SeedAsync(state);

        var act = () => Set(model.Id, CustomModelAvailability.Available);

        (await act.Should().ThrowAsync<DomainRuleViolationException>())
            .WithMessage(CustomModelDtoMapping.NotCompletedReason);
        model.Availability.Should().Be(CustomModelAvailability.Unavailable);
        _logger.Collector.GetSnapshot().Should().NotContain(r => r.Id.Name == "AvailabilityChanged");
        await _notifier.DidNotReceiveWithAnyArgs().NotifyStateChangedAsync(default!, default, default);
    }

    [Fact]
    public async Task Handle_AnotherModelOfTheRepositoryIsAvailable_ConflictsNamingIt()
    {
        var current = await SeedAsync(CustomModelDeploymentState.Completed, name: "supertonic-3-v1", destination: "Models/supertonic-3-v1");
        current.MakeAvailable();
        var model = await SeedAsync(CustomModelDeploymentState.Completed, name: "supertonic-3-v2", destination: "Models/supertonic-3-v2");

        var act = () => Set(model.Id, CustomModelAvailability.Available);

        (await act.Should().ThrowAsync<DuplicateResourceException>())
            .Which.Message.Should().Contain("supertonic-3-v1");
        model.Availability.Should().Be(CustomModelAvailability.Unavailable);
        current.Availability.Should().Be(CustomModelAvailability.Available);
    }

    [Fact]
    public async Task Handle_TheSameModelIsAlreadyAvailable_IsANoOp()
    {
        var model = await SeedAsync(CustomModelDeploymentState.Completed);
        model.MakeAvailable();

        var result = await Set(model.Id, CustomModelAvailability.Available);

        result.Availability.Should().Be("Available");
        _logger.Collector.GetSnapshot().Should().NotContain(r => r.Id.Name == "AvailabilityChanged");
        await _notifier.DidNotReceiveWithAnyArgs().NotifyStateChangedAsync(default!, default, default);
    }

    [Theory]
    [InlineData(CustomModelDeploymentState.Completed)]
    [InlineData(CustomModelDeploymentState.Failed)]
    [InlineData(CustomModelDeploymentState.Transferring)]
    public async Task Handle_MakingUnavailable_IsAlwaysAllowed(CustomModelDeploymentState state)
    {
        var model = await SeedAsync(state);
        if (state == CustomModelDeploymentState.Completed)
        {
            model.MakeAvailable();
        }

        var result = await Set(model.Id, CustomModelAvailability.Unavailable);

        model.Availability.Should().Be(CustomModelAvailability.Unavailable);
        result.Availability.Should().Be("Unavailable");
    }

    [Fact]
    public async Task Handle_MakingUnavailable_AuditsTheChange()
    {
        var model = await SeedAsync(CustomModelDeploymentState.Completed);
        model.MakeAvailable();

        await Set(model.Id, CustomModelAvailability.Unavailable);

        _logger.Collector.GetSnapshot().Should().ContainSingle(r =>
            r.Id.Name == "AvailabilityChanged" && r.Message.Contains("Available -> Unavailable"));
    }

    [Fact]
    public async Task Handle_Missing_NotFound()
    {
        var act = () => Set(Guid.NewGuid(), CustomModelAvailability.Available);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_SecondConcurrencyConflict_IsSurfaced_BeforeAnyAudit()
    {
        var model = await SeedAsync(CustomModelDeploymentState.Completed);
        _repository.UpdateConflicts = 2;

        var act = () => Set(model.Id, CustomModelAvailability.Available);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        _logger.Collector.GetSnapshot().Should().NotContain(r => r.Id.Name == "AvailabilityChanged");
    }

    private Task<CustomModelSummaryDto> Set(Guid id, CustomModelAvailability availability) =>
        CreateHandler().Handle(new SetCustomModelAvailabilityCommand(id, availability), TestContext.Current.CancellationToken);

    private async Task<CustomModel> SeedAsync(
        CustomModelDeploymentState state,
        string name = "supertonic-3",
        string destination = "Models/supertonic-3")
    {
        var model = CustomModelSeed.InState(state, name, destination: destination);
        await _repository.AddAsync(model, TestContext.Current.CancellationToken);
        return model;
    }

    private SetCustomModelAvailabilityCommandHandler CreateHandler()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AdminUserId);

        return new SetCustomModelAvailabilityCommandHandler(
            _repository,
            _notifier,
            new CustomModelSummaryBuilder(Substitute.For<IUserAdminRepository>(), []),
            currentUser,
            _logger);
    }
}
