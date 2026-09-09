using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Commands.ArchiveAgent;
using AskLucy.Application.Agents.Commands.DeleteAgent;
using AskLucy.Application.Agents.Commands.PublishAgentVersion;
using AskLucy.Application.Agents.Commands.RestoreAgent;
using AskLucy.Application.Agents.Commands.UpdateAgent;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents.Authorization;

/// <summary>
/// specs/045 T110, contracts/system-agent-provisioning.md §3/§4 — one theory over every agent
/// mutation command (update, publish, archive, restore, delete), asserting each rejects a system
/// agent with <see cref="SystemAgentImmutableException"/> and leaves an ordinary user agent's own
/// behaviour untouched. A future sixth mutation command joining <see cref="MutationCases"/> is
/// what keeps this test from silently stopping at five forever.
/// </summary>
public sealed class SystemAgentMutationGuardTheoryTests
{
    private static readonly Guid AgentId = Guid.CreateVersion7();
    private const string UserOwnerId = "user-1";

    public static TheoryData<string, Func<IAgentRepository, IUnitOfWork, ICurrentUserAccessor, Task>> MutationCases() => new()
    {
        {
            "UpdateAgent",
            (repo, uow, user) => new UpdateAgentCommandHandler(repo, uow, user).Handle(
                new UpdateAgentCommand(
                    AgentId, "New Name", null, AgentType.Task,
                    new AgentInstructionsDto(null, null, null, null, null, null, null),
                    null, null, AgentOutputFormat.PlainText,
                    new AgentExecutionPolicyDto(null, null, null, null, null, null)),
                CancellationToken.None)
        },
        {
            "PublishAgentVersion",
            (repo, uow, user) => new PublishAgentVersionCommandHandler(repo, uow, user).Handle(
                new PublishAgentVersionCommand(AgentId, null), CancellationToken.None)
        },
        {
            "ArchiveAgent",
            (repo, uow, user) => new ArchiveAgentCommandHandler(repo, uow, user).Handle(
                new ArchiveAgentCommand(AgentId), CancellationToken.None)
        },
        {
            "RestoreAgent",
            (repo, uow, user) => new RestoreAgentCommandHandler(repo, uow, user).Handle(
                new RestoreAgentCommand(AgentId), CancellationToken.None)
        },
        {
            "DeleteAgent",
            (repo, uow, user) => new DeleteAgentCommandHandler(repo, uow, user).Handle(
                new DeleteAgentCommand(AgentId), CancellationToken.None)
        },
    };

    [Theory]
    [MemberData(nameof(MutationCases))]
    public async Task Handle_ShouldThrowSystemAgentImmutable_ForASystemAgent(
        string caseName, Func<IAgentRepository, IUnitOfWork, ICurrentUserAccessor, Task> invoke)
    {
        _ = caseName;
        var agent = BuildSystemAgent();
        var repository = Substitute.For<IAgentRepository>();
        repository.GetByIdForOwnerAsync(AgentId, Agent.SystemOwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Agent.SystemOwnerId);
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var act = () => invoke(repository, unitOfWork, currentUser);

        await act.Should().ThrowAsync<SystemAgentImmutableException>();
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(MutationCases))]
    public async Task Handle_ShouldSucceed_ForAnOrdinaryUserAgent(
        string caseName, Func<IAgentRepository, IUnitOfWork, ICurrentUserAccessor, Task> invoke)
    {
        _ = caseName;
        var agent = BuildUserAgent();
        var repository = Substitute.For<IAgentRepository>();
        repository.GetByIdForOwnerAsync(AgentId, UserOwnerId, Arg.Any<CancellationToken>()).Returns(agent);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(UserOwnerId);
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var act = () => invoke(repository, unitOfWork, currentUser);

        await act.Should().NotThrowAsync();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // Every command in MutationCases is built against the same fixed AgentId so one TheoryData
    // row can drive both the system-agent and the user-agent test; BaseEntity.Id has a public
    // setter, so the factory-assigned id is simply overwritten to match.
    private static Agent BuildSystemAgent()
    {
        var agent = Agent.CreateSystemProvisioned(
            "lucy.orchestrator", "Lucy", null, AgentType.Conversational,
            AgentInstructions.Empty, AiCapability.TurnOrchestration, AgentExecutionPolicy.Empty, "system:test");
        agent.Id = AgentId;
        return agent;
    }

    private static Agent BuildUserAgent()
    {
        var agent = Agent.Create(
            UserOwnerId, "My Agent", null, AgentType.Task,
            new AgentInstructions("Be helpful.", null, null, null, null, null, null),
            Guid.CreateVersion7(), Guid.CreateVersion7(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, UserOwnerId);
        agent.Id = AgentId;
        return agent;
    }
}
