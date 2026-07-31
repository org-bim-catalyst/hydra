using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.UpdateAiModelStatus;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/008-ai-model-catalog-management T010 — FR-002: any status transition is allowed.</summary>
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
    [InlineData(AIModelStatus.Available, AIModelStatus.Deprecated)]
    [InlineData(AIModelStatus.Deprecated, AIModelStatus.Unavailable)]
    [InlineData(AIModelStatus.Unavailable, AIModelStatus.Available)]
    public async Task Handle_ShouldAllowAnyStatusTransition(AIModelStatus from, AIModelStatus to)
    {
        var providerId = Guid.NewGuid();
        var model = AIModel.Create(providerId, "gpt-4.1", "GPT-4.1", 128000, 16384,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");
        model.SetStatus(from, "test");
        _models.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);

        await _handler.Handle(new UpdateAiModelStatusCommand(model.Id, to), CancellationToken.None);

        model.Status.Should().Be(to);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowKeyNotFound_WhenModelDoesNotExist()
    {
        _models.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((AIModel?)null);

        var act = () => _handler.Handle(new UpdateAiModelStatusCommand(Guid.NewGuid(), AIModelStatus.Deprecated), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
