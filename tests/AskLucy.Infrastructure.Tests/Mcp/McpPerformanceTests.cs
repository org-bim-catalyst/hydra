using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Options;
using AskLucy.Domain.Mcp;
using AskLucy.Infrastructure.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Mcp;

/// <summary>spec.md Performance Tests — genuine concurrency (not just sequential calls), many servers, and failure isolation so one bad response/server never blocks the rest.</summary>
public sealed class McpPerformanceTests
{
    private const string AdminId = "admin-1";

    [Fact]
    public async Task McpRateLimiter_ShouldAdmitExactlyTheConcurrencyLimit_UnderGenuineConcurrentLoad()
    {
        using var limiter = new McpRateLimiter(
            Options.Create(new McpRuntimeOptions { MaxRequestsPerMinute = 1000, MaxConcurrentRequestsPerServer = 5 }),
            Substitute.For<ILogger<McpRateLimiter>>());
        var serverId = Guid.NewGuid();

        var leases = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            limiter.TryAcquireAsync(new McpRateLimitKey(serverId, $"tool{i}", "user-1", Guid.NewGuid())).AsTask()));

        leases.Count(l => l is not null).Should().Be(5);
        leases.Count(l => l is null).Should().Be(15);

        foreach (var lease in leases)
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task McpRateLimiter_ShouldIsolateDifferentServers_UnderConcurrentLoad_NeitherBlockingTheOther()
    {
        using var limiter = new McpRateLimiter(
            Options.Create(new McpRuntimeOptions { MaxRequestsPerMinute = 1000, MaxConcurrentRequestsPerServer = 2 }),
            Substitute.For<ILogger<McpRateLimiter>>());
        var serverA = Guid.NewGuid();
        var serverB = Guid.NewGuid();

        var results = await Task.WhenAll(
            Task.Run(async () => await Task.WhenAll(Enumerable.Range(0, 5).Select(i => limiter.TryAcquireAsync(new McpRateLimitKey(serverA, $"a{i}", "user-1", Guid.NewGuid())).AsTask()))),
            Task.Run(async () => await Task.WhenAll(Enumerable.Range(0, 5).Select(i => limiter.TryAcquireAsync(new McpRateLimitKey(serverB, $"b{i}", "user-1", Guid.NewGuid())).AsTask()))));

        // Server A being at its own concurrency cap never reduces what Server B can admit.
        results[0].Count(l => l is not null).Should().Be(2);
        results[1].Count(l => l is not null).Should().Be(2);
    }

    [Fact]
    public async Task McpCapabilityRefreshJob_ShouldProcessManyServers_WithOneFailureNeverBlockingTheRest()
    {
        var serverRepository = Substitute.For<IMcpServerRepository>();
        var toolRepository = Substitute.For<IMcpToolRepository>();
        var resourceRepository = Substitute.For<IMcpResourceRepository>();
        var promptRepository = Substitute.For<IMcpPromptRepository>();
        var clientFactory = Substitute.For<IMcpClientFactory>();
        var client = Substitute.For<IMcpClient>();
        clientFactory.GetOrCreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(client);
        client.ListResourcesAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredResource>)[]);
        client.ListPromptsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredPrompt>)[]);
        toolRepository.ListByServerIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpTool>)[]);
        client.ListToolsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredTool>)[]);

        const int serverCount = 50;
        var serverIds = Enumerable.Range(0, serverCount).Select(_ => Guid.NewGuid()).ToList();
        serverRepository.ListServersDueForCapabilityRefreshAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(serverIds);

        foreach (var (serverId, index) in serverIds.Select((id, i) => (id, i)))
        {
            // Every 10th server fails to resolve — isolated failures must never stop the sweep.
            var server = index % 10 == 0
                ? null
                : McpServer.Register("Test", null, $"https://mcp-{index}.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);
            serverRepository.GetByIdAsync(serverId, Arg.Any<CancellationToken>()).Returns(server);
            if (server is not null)
            {
                serverRepository.GetLatestCapabilitySnapshotVersionAsync(serverId, Arg.Any<CancellationToken>()).Returns(0);
            }
        }

        var job = new McpCapabilityRefreshJob(
            serverRepository, toolRepository, resourceRepository, promptRepository, Substitute.For<IMcpAuditLogRepository>(), clientFactory,
            new McpConnectionResiliencePolicy(Options.Create(new McpRuntimeOptions { MaxRetries = 0 }), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
            Substitute.For<IUnitOfWork>(), TimeProvider.System, Substitute.For<ILogger<McpCapabilityRefreshJob>>());

        var act = async () => await job.RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        // 45 of the 50 servers (every 10th excluded) successfully produced a snapshot.
        serverRepository.Received(45).AddCapabilitySnapshot(Arg.Any<McpCapabilitySnapshot>());
    }
}
