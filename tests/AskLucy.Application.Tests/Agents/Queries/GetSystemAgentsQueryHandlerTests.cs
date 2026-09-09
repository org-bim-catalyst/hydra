using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Queries.GetSystemAgents;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents.Queries;

/// <summary>
/// specs/047 FR-001/FR-002 — the repository already does the ownership filtering
/// (<see cref="IAgentRepository.ListSystemOwnedAsync"/>); this handler's own job is only the
/// entity-to-DTO mapping, including the two timestamp edge cases data-model.md calls out.
/// </summary>
public sealed class GetSystemAgentsQueryHandlerTests
{
    private readonly IAgentRepository _repository = Substitute.For<IAgentRepository>();

    private GetSystemAgentsQueryHandler BuildHandler() => new(_repository);

    private static Agent CreateProvisioned(string systemKey, string name) =>
        Agent.CreateSystemProvisioned(
            systemKey, name, description: null, AgentType.Conversational,
            AgentInstructions.Empty, AiCapability.Chat, AgentExecutionPolicy.Empty, actor: "system");

    [Fact]
    public async Task Handle_ShouldReturnEmptyList_WhenNoSystemAgentsAreProvisioned()
    {
        _repository.ListSystemOwnedAsync(Arg.Any<CancellationToken>()).Returns([]);

        var result = await BuildHandler().Handle(new GetSystemAgentsQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldUseTheNewestVersionsTimestamp_WhenMultipleVersionsExist()
    {
        var agent = CreateProvisioned("lucy.orchestrator", "Lucy");
        agent.PublishSystemVersion(["resolve_location"], definitionHash: "hash-1", actor: "system");
        var secondVersion = agent.PublishSystemVersion(["resolve_location", "resolve_site_boundary"], definitionHash: "hash-2", actor: "system");
        _repository.ListSystemOwnedAsync(Arg.Any<CancellationToken>()).Returns([agent]);

        var result = await BuildHandler().Handle(new GetSystemAgentsQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(agent.Id);
        result[0].Name.Should().Be("Lucy");
        result[0].SystemKey.Should().Be("lucy.orchestrator");
        result[0].Status.Should().Be(AgentStatus.Published);
        result[0].PublishedVersionNumber.Should().Be(2);
        result[0].LastUpdatedAtUtc.Should().Be(secondVersion.CreatedAtUtc);
    }

    [Fact]
    public async Task Handle_ShouldFallBackToAgentAuditTimestamp_WhenNoVersionHasBeenPublished()
    {
        // Should not occur in practice (CreateSystemProvisioned never publishes a version on its
        // own), but the mapping must stay total rather than throwing — data-model.md's documented
        // edge case.
        var agent = CreateProvisioned("lucy.orchestrator", "Lucy");
        _repository.ListSystemOwnedAsync(Arg.Any<CancellationToken>()).Returns([agent]);

        var result = await BuildHandler().Handle(new GetSystemAgentsQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].PublishedVersionNumber.Should().BeNull();
        result[0].LastUpdatedAtUtc.Should().Be(agent.ModifiedAtUtc ?? agent.CreatedAtUtc);
    }
}
