using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Queries.GetMcpTool;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class GetMcpToolQueryHandlerTests
{
    private readonly IMcpToolRepository _toolRepository = Substitute.For<IMcpToolRepository>();

    [Fact]
    public async Task Handle_ShouldReturnFullDetail_WhenTheToolIsActiveAndAvailable()
    {
        var tool = McpTool.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "Searches things.", "{}", "{}", null, null, "[]", "1.0", null);
        tool.Activate("admin-1", null, null);
        _toolRepository.GetActiveAvailableByNamespacedNameAsync(tool.NamespacedName, Arg.Any<CancellationToken>()).Returns((tool, "Acme Docs"));
        var handler = new GetMcpToolQueryHandler(_toolRepository);

        var result = await handler.Handle(new GetMcpToolQuery(tool.NamespacedName), CancellationToken.None);

        result.NamespacedName.Should().Be(tool.NamespacedName);
        result.SourceServerName.Should().Be("Acme Docs");
        result.Version.Should().Be("1.0");
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenTheToolIsNotActiveOrAvailable()
    {
        _toolRepository.GetActiveAvailableByNamespacedNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(((McpTool Tool, string ServerName)?)null);
        var handler = new GetMcpToolQueryHandler(_toolRepository);

        var act = async () => await handler.Handle(new GetMcpToolQuery("mcp:nonexistent:search"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
