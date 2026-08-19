using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Options;
using AskLucy.Domain.Mcp;
using AskLucy.Infrastructure.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Mcp;

/// <summary>
/// spec.md User Story 6 — the recurring health-check sweep calls the exact same
/// <c>TestMcpServerConnectionCommandHandler</c> the on-demand admin action uses, for every
/// enabled server, and invalidates <see cref="IMcpToolRegistry"/> once afterward (FR-056).
/// </summary>
public sealed class McpServerHealthCheckJobTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IMcpClientFactory _clientFactory = Substitute.For<IMcpClientFactory>();
    private readonly IMcpToolRegistry _mcpToolRegistry = Substitute.For<IMcpToolRegistry>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private McpServerHealthCheckJob CreateJob() => new(
        _serverRepository, _auditLogRepository, _clientFactory,
        new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
        _unitOfWork, _mcpToolRegistry, Substitute.For<ILogger<McpServerHealthCheckJob>>());

    private static McpServer RegisterServer() => McpServer.Register(
        "Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);

    [Fact]
    public async Task RunAsync_ShouldCheckEveryEnabledServer_AndInvalidateTheRegistryAfterward()
    {
        var serverA = RegisterServer();
        var serverB = RegisterServer();
        _serverRepository.ListEnabledServerIdsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Guid>)[serverA.Id, serverB.Id]);
        _serverRepository.GetByIdAsync(serverA.Id, Arg.Any<CancellationToken>()).Returns(serverA);
        _serverRepository.GetByIdAsync(serverB.Id, Arg.Any<CancellationToken>()).Returns(serverB);
        _serverRepository.GetHealthAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((McpServerHealth?)null);
        var client = Substitute.For<IMcpClient>();
        _clientFactory.GetOrCreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(client);

        await CreateJob().RunAsync(CancellationToken.None);

        await client.Received(2).PingAsync(Arg.Any<CancellationToken>());
        _serverRepository.Received(2).AddHealth(Arg.Is<McpServerHealth>(h => h.Status == McpServerHealthStatus.Healthy));
        await _mcpToolRegistry.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldContinueCheckingRemainingServers_WhenOneServerCheckThrows()
    {
        var serverA = RegisterServer();
        var serverB = RegisterServer();
        _serverRepository.ListEnabledServerIdsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Guid>)[serverA.Id, serverB.Id]);
        _serverRepository.GetByIdAsync(serverA.Id, Arg.Any<CancellationToken>()).Returns((McpServer?)null); // triggers KeyNotFoundException inside the handler
        _serverRepository.GetByIdAsync(serverB.Id, Arg.Any<CancellationToken>()).Returns(serverB);
        _serverRepository.GetHealthAsync(serverB.Id, Arg.Any<CancellationToken>()).Returns((McpServerHealth?)null);
        var client = Substitute.For<IMcpClient>();
        _clientFactory.GetOrCreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(client);

        var act = async () => await CreateJob().RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        await client.Received(1).PingAsync(Arg.Any<CancellationToken>());
        await _mcpToolRegistry.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldStillInvalidateTheRegistry_WhenNoServersAreEnabled()
    {
        _serverRepository.ListEnabledServerIdsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Guid>)[]);

        await CreateJob().RunAsync(CancellationToken.None);

        await _mcpToolRegistry.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }
}
