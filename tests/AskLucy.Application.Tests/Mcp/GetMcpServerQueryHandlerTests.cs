using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Queries.GetMcpServer;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class GetMcpServerQueryHandlerTests
{
    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();

    [Fact]
    public async Task Handle_ShouldReturnServer_WhenFound()
    {
        var server = McpServer.Register("Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, "admin-1", 60);
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        var handler = new GetMcpServerQueryHandler(_serverRepository);

        var result = await handler.Handle(new GetMcpServerQuery(server.Id), CancellationToken.None);

        result.Id.Should().Be(server.Id);
        result.Name.Should().Be("Test");
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenNotFound()
    {
        _serverRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((McpServer?)null);
        var handler = new GetMcpServerQueryHandler(_serverRepository);

        var act = async () => await handler.Handle(new GetMcpServerQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
