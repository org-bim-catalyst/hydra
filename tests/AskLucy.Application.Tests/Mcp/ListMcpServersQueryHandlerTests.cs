using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Queries.ListMcpServers;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class ListMcpServersQueryHandlerTests
{
    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();

    [Fact]
    public async Task Handle_ShouldReturnPagedServers_WithNextCursor()
    {
        var server = McpServer.Register("Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, "admin-1", 60);
        _serverRepository.ListAsync(null, null, null, null, 20, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<McpServer>)[server], "next-cursor"));
        var handler = new ListMcpServersQueryHandler(_serverRepository);

        var result = await handler.Handle(new ListMcpServersQuery(null, null, null, null, 20), CancellationToken.None);

        result.Items.Should().ContainSingle(s => s.Id == server.Id);
        result.NextCursor.Should().Be("next-cursor");
    }
}
