using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Commands.CreateAgent;
using AskLucy.Domain.Agents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

public sealed class CreateAgentCommandHandlerTests
{
    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private CreateAgentCommandHandler CreateHandler() => new(_agentRepository, _unitOfWork, _currentUser);

    private static CreateAgentCommand ValidCommand(string name = "My Agent") => new(
        name, "desc", AgentType.Task,
        new AgentInstructionsDto("You are a helpful assistant.", null, null, null, null, null, null),
        Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText,
        new AgentExecutionPolicyDto(null, null, null, null, null, null));

    [Fact]
    public async Task Handle_ShouldCreateAnAgentOwnedByTheCaller_InDraftStatus()
    {
        _currentUser.UserId.Returns("user-1");

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.Name.Should().Be("My Agent");
        result.Status.Should().Be(nameof(AgentStatus.Draft));
        _agentRepository.Received(1).Add(Arg.Is<Agent>(a => a.OwnerId == "user-1" && a.Status == AgentStatus.Draft));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenNoCurrentUser()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _agentRepository.DidNotReceive().Add(Arg.Any<Agent>());
    }
}
