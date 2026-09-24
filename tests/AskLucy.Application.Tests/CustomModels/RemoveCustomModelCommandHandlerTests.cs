using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.CustomModels.Commands.RemoveCustomModel;
using AskLucy.Domain.Common;
using AskLucy.Domain.CustomModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;

namespace AskLucy.Application.Tests.CustomModels;

/// <summary>specs/072 T073 (FR-031, FR-032, FR-027).</summary>
public sealed class RemoveCustomModelCommandHandlerTests
{
    private const string AdminUserId = "admin-2";

    private readonly FakeCustomModelRepository _repository = new();
    private readonly ICustomModelDeploymentNotifier _notifier = Substitute.For<ICustomModelDeploymentNotifier>();
    private readonly IDeploymentFileUploader _uploader = Substitute.For<IDeploymentFileUploader>();
    private readonly FakeLogger<RemoveCustomModelCommandHandler> _logger = new();

    [Theory]
    [InlineData(CustomModelDeploymentState.Failed)]
    [InlineData(CustomModelDeploymentState.Cancelled)]
    public async Task Handle_FailedOrCancelled_SoftDeletes_FreesTheName_AndAudits(CustomModelDeploymentState state)
    {
        var model = await SeedAsync(state);

        await Remove(model.Id);

        model.IsDeleted.Should().BeTrue();
        model.DeletedBy.Should().Be(AdminUserId);
        (await _repository.NameExistsAsync("supertonic-3", TestContext.Current.CancellationToken)).Should().BeFalse();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "Removed" && r.Message.Contains(AdminUserId));
        await _notifier.Received(1).NotifyStateChangedAsync(
            Arg.Is<CustomModelSummaryDto>(s => s!.Id == model.Id),
            true,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CustomModelDeploymentState.Completed)]
    [InlineData(CustomModelDeploymentState.Queued)]
    [InlineData(CustomModelDeploymentState.Listing)]
    [InlineData(CustomModelDeploymentState.Transferring)]
    public async Task Handle_CompletedOrInProgress_IsRejected(CustomModelDeploymentState state)
    {
        var model = await SeedAsync(state);

        var act = () => Remove(model.Id);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        model.IsDeleted.Should().BeFalse();
        _logger.Collector.GetSnapshot().Should().NotContain(r => r.Id.Name == "Removed");
        await _notifier.DidNotReceiveWithAnyArgs().NotifyStateChangedAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_NeverTouchesTheDeploymentTarget()
    {
        var model = await SeedAsync(CustomModelDeploymentState.Failed);

        await Remove(model.Id);

        _uploader.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Missing_NotFound()
    {
        var act = () => Remove(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private Task Remove(Guid id) =>
        CreateHandler().Handle(new RemoveCustomModelCommand(id), TestContext.Current.CancellationToken);

    private async Task<CustomModel> SeedAsync(CustomModelDeploymentState state)
    {
        var model = CustomModelSeed.InState(state);
        await _repository.AddAsync(model, TestContext.Current.CancellationToken);
        return model;
    }

    private RemoveCustomModelCommandHandler CreateHandler()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AdminUserId);

        // The uploader is deliberately not a dependency: removal leaves the files on the target (FR-031).
        return new RemoveCustomModelCommandHandler(
            _repository,
            _notifier,
            new CustomModelSummaryBuilder(Substitute.For<IUserAdminRepository>(), []),
            currentUser,
            TimeProvider.System,
            _logger);
    }
}
