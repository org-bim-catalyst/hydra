using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Commands.TestMcpServerConnection;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class TestMcpServerConnectionCommandHandlerTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IMcpClientFactory _clientFactory = Substitute.For<IMcpClientFactory>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private TestMcpServerConnectionCommandHandler CreateHandler(McpConnectionResiliencePolicy? policy = null) => new(
        _serverRepository, _auditLogRepository, _clientFactory,
        policy ?? new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new AskLucy.Application.Options.McpRuntimeOptions()), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
        _unitOfWork, _currentUser);

    private static McpServer RegisterServer() => McpServer.Register("Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);

    public TestMcpServerConnectionCommandHandlerTests() => _currentUser.UserId.Returns(AdminId);

    [Fact]
    public async Task Handle_ShouldReportHealthy_WhenPingSucceeds()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetHealthAsync(server.Id, Arg.Any<CancellationToken>()).Returns((McpServerHealth?)null);
        var client = Substitute.For<IMcpClient>();
        _clientFactory.GetOrCreateAsync(server.Id, Arg.Any<CancellationToken>()).Returns(client);
        var handler = CreateHandler();

        var result = await handler.Handle(new TestMcpServerConnectionCommand(server.Id), CancellationToken.None);

        result.Status.Should().Be(McpServerHealthStatus.Healthy);
        await client.Received(1).PingAsync(Arg.Any<CancellationToken>());
        _serverRepository.Received(1).AddHealth(Arg.Any<McpServerHealth>());
    }

    [Fact]
    public async Task Handle_ShouldReportUnavailable_WhenConnectionFails()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetHealthAsync(server.Id, Arg.Any<CancellationToken>()).Returns((McpServerHealth?)null);
        _clientFactory.GetOrCreateAsync(server.Id, Arg.Any<CancellationToken>())
            .Returns<IMcpClient>(_ => throw new InvalidOperationException("connection refused"));
        var handler = CreateHandler(new McpConnectionResiliencePolicy(
            Microsoft.Extensions.Options.Options.Create(new AskLucy.Application.Options.McpRuntimeOptions { MaxRetries = 0 }),
            Substitute.For<ILogger<McpConnectionResiliencePolicy>>()));

        var result = await handler.Handle(new TestMcpServerConnectionCommand(server.Id), CancellationToken.None);

        result.Status.Should().Be(McpServerHealthStatus.Unavailable);
        result.FailureCategory.Should().Be(McpFailureCategory.ConnectionFailure);
    }

    [Fact]
    public async Task Handle_ShouldNeverExposeRawExceptionMessage_InDetail()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetHealthAsync(server.Id, Arg.Any<CancellationToken>()).Returns((McpServerHealth?)null);
        _clientFactory.GetOrCreateAsync(server.Id, Arg.Any<CancellationToken>())
            .Returns<IMcpClient>(_ => throw new InvalidOperationException("secret-token-abc123 leaked in exception"));
        var handler = CreateHandler(new McpConnectionResiliencePolicy(
            Microsoft.Extensions.Options.Options.Create(new AskLucy.Application.Options.McpRuntimeOptions { MaxRetries = 0 }),
            Substitute.For<ILogger<McpConnectionResiliencePolicy>>()));

        var result = await handler.Handle(new TestMcpServerConnectionCommand(server.Id), CancellationToken.None);

        result.Detail.Should().NotContain("secret-token-abc123");
    }

    [Fact]
    public async Task Handle_ShouldRecordAuditLog_WithHealthStateChanged()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetHealthAsync(server.Id, Arg.Any<CancellationToken>()).Returns((McpServerHealth?)null);
        var client = Substitute.For<IMcpClient>();
        _clientFactory.GetOrCreateAsync(server.Id, Arg.Any<CancellationToken>()).Returns(client);
        var handler = CreateHandler();

        await handler.Handle(new TestMcpServerConnectionCommand(server.Id), CancellationToken.None);

        _auditLogRepository.Received(1).Add(Arg.Is<McpAuditLog>(a => a!.Action == McpAuditAction.HealthStateChanged));
    }
}
