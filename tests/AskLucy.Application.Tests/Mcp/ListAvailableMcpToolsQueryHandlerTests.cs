using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Queries.ListAvailableMcpTools;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>spec.md FR-062 — the catalog list is exactly what <c>IMcpToolRegistry.ActiveTools</c> would resolve; both read the same <c>IMcpToolRepository.ListActiveAvailableAsync</c> filter, so there is no drift between "what's shown" and "what's callable".</summary>
public sealed class ListAvailableMcpToolsQueryHandlerTests
{
    private readonly IMcpToolRepository _toolRepository = Substitute.For<IMcpToolRepository>();

    [Fact]
    public async Task Handle_ShouldReturnEveryActiveAvailableTool_WithItsSourceServerName()
    {
        var tool = McpTool.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "Searches things.", "{}", "{}", null, null, "[]", null, null);
        tool.Activate("admin-1", null, null);
        _toolRepository.ListActiveAvailableAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<(McpTool Tool, string ServerName)>)[(tool, "Acme Docs")]);
        var handler = new ListAvailableMcpToolsQueryHandler(_toolRepository);

        var result = await handler.Handle(new ListAvailableMcpToolsQuery(), CancellationToken.None);

        result.Should().ContainSingle(t => t.NamespacedName == tool.NamespacedName && t.SourceServerName == "Acme Docs");
    }

    [Fact]
    public async Task Handle_ShouldReturnEmpty_WhenNoToolIsActiveAndAvailable()
    {
        _toolRepository.ListActiveAvailableAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<(McpTool Tool, string ServerName)>)[]);
        var handler = new ListAvailableMcpToolsQueryHandler(_toolRepository);

        var result = await handler.Handle(new ListAvailableMcpToolsQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
