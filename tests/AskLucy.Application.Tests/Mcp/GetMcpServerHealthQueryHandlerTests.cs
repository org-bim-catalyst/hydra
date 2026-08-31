using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Queries.GetMcpServerHealth;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class GetMcpServerHealthQueryHandlerTests
{
    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();

    private static McpServer RegisterServer() => McpServer.Register("Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, "admin-1", 60);

    [Fact]
    public async Task Handle_ShouldReturnExistingHealth_WhenPresent()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        var health = McpServerHealth.CreateUnknown(server.Id);
        health.RecordCheck(McpServerHealthStatus.Healthy, null, null);
        _serverRepository.GetHealthAsync(server.Id, Arg.Any<CancellationToken>()).Returns(health);
        var handler = new GetMcpServerHealthQueryHandler(_serverRepository);

        var result = await handler.Handle(new GetMcpServerHealthQuery(server.Id), CancellationToken.None);

        result.Status.Should().Be(McpServerHealthStatus.Healthy);
    }

    [Fact]
    public async Task Handle_ShouldSynthesizeUnknownHealth_WhenNeverChecked()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetHealthAsync(server.Id, Arg.Any<CancellationToken>()).Returns((McpServerHealth?)null);
        var handler = new GetMcpServerHealthQueryHandler(_serverRepository);

        var result = await handler.Handle(new GetMcpServerHealthQuery(server.Id), CancellationToken.None);

        result.Status.Should().Be(McpServerHealthStatus.Unknown);
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenServerNotFound()
    {
        _serverRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((McpServer?)null);
        var handler = new GetMcpServerHealthQueryHandler(_serverRepository);

        var act = async () => await handler.Handle(new GetMcpServerHealthQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
