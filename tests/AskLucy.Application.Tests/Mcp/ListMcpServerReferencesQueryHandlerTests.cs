using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Queries.ListMcpServerReferences;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class ListMcpServerReferencesQueryHandlerTests
{
    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();

    [Fact]
    public async Task Handle_ShouldReturnReferencingAgentTools()
    {
        var serverId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        _serverRepository.ListReferencingAgentToolsAsync(serverId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<(Guid AgentId, string ToolName)>)[(agentId, "mcp:server:search")]);
        var handler = new ListMcpServerReferencesQueryHandler(_serverRepository);

        var result = await handler.Handle(new ListMcpServerReferencesQuery(serverId), CancellationToken.None);

        result.Should().ContainSingle(r => r.AgentId == agentId && r.ToolName == "mcp:server:search");
    }
}
