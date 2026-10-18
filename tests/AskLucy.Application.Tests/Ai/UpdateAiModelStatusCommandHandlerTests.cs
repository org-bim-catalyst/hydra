using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.UpdateAiModelStatus;
using AskLucy.Application.Ai.Deprecation;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// specs/008 T010 — FR-002: Available and Unavailable toggle freely.
/// specs/069 FR-021/FR-022: Deprecated is terminal and only the sync review may enter it.
/// </summary>
public sealed class UpdateAiModelStatusCommandHandlerTests
{
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly UpdateAiModelStatusCommandHandler _handler;

    public UpdateAiModelStatusCommandHandlerTests()
    {
        _currentUser.UserId.Returns("admin-1");
        _handler = new UpdateAiModelStatusCommandHandler(
            _models, _unitOfWork, _currentUser, Substitute.For<ILogger<UpdateAiModelStatusCommandHandler>>());
    }

    [Theory]
    [InlineData(AIModelStatus.Available, AIModelStatus.Unavailable)]
    [InlineData(AIModelStatus.Unavailable, AIModelStatus.Available)]
    public async Task Handle_ShouldAllowAvailableAndUnavailableTransitions(AIModelStatus from, AIModelStatus to)
    {
        var model = CreateModel(from);
        _models.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);

        await _handler.Handle(new UpdateAiModelStatusCommand(model.Id, to), TestContext.Current.CancellationToken);

        model.Status.Should().Be(to);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AIModelStatus.Available, AIModelStatus.Deprecated)]
    [InlineData(AIModelStatus.Deprecated, AIModelStatus.Unavailable)]
    [InlineData(AIModelStatus.Deprecated, AIModelStatus.Available)]
    public async Task Handle_ShouldRefuseAnyTransitionInvolvingDeprecated(AIModelStatus from, AIModelStatus to)
    {
        var model = CreateModel(from);
        if (from == AIModelStatus.Deprecated)
        {
            model.Deprecate("test");
        }

        _models.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);

        var act = () => _handler.Handle(new UpdateAiModelStatusCommand(model.Id, to), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ModelStatusLockedException>();
        model.Status.Should().Be(from);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static AIModel CreateModel(AIModelStatus status)
    {
        var model = AIModel.Create(Guid.NewGuid(), "gpt-4.1", "GPT-4.1", 128000, 16384,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");
        if (status == AIModelStatus.Unavailable)
        {
            model.SetStatus(AIModelStatus.Unavailable, "test");
        }

        return model;
    }

    [Fact]
    public async Task Handle_ShouldThrowKeyNotFound_WhenModelDoesNotExist()
    {
        _models.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((AIModel?)null);

        var act = () => _handler.Handle(new UpdateAiModelStatusCommand(Guid.NewGuid(), AIModelStatus.Deprecated), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
