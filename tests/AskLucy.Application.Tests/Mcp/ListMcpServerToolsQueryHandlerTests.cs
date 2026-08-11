using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Queries.ListMcpServerTools;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class ListMcpServerToolsQueryHandlerTests
{
    private readonly IMcpToolRepository _toolRepository = Substitute.For<IMcpToolRepository>();

    [Fact]
    public async Task Handle_ShouldReturnAllToolsRegardlessOfActivationStatus()
    {
        var serverId = Guid.NewGuid();
        var pendingTool = McpTool.CreateFromDiscovery(serverId, Guid.NewGuid(), "search", "Search", "desc", "{}", "{}", null, null, "[]", null, null);
        _toolRepository.ListByServerIdAsync(serverId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpTool>)[pendingTool]);
        var handler = new ListMcpServerToolsQueryHandler(_toolRepository);

        var result = await handler.Handle(new ListMcpServerToolsQuery(serverId), CancellationToken.None);

        result.Should().ContainSingle(t => t.ActivationStatus == McpToolActivationStatus.PendingReview);
    }
}
