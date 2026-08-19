using System.Text.Json;
using AskLucy.Application.Abstractions;
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
/// spec.md FR-013/FR-016, User Story 6 — only servers past their own <c>CapabilityRefreshIntervalMinutes</c>
/// are refreshed; a failed refresh preserves the prior working capability set (verified by the
/// unmodified <c>RefreshMcpCapabilitiesCommandHandler</c> the job reuses, already proven by
/// <c>RefreshMcpCapabilitiesCommandHandlerTests</c>) — this suite verifies the job's own
/// server-selection/error-isolation behavior around that handler.
/// </summary>
public sealed class McpCapabilityRefreshJobTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();
    private readonly IMcpToolRepository _toolRepository = Substitute.For<IMcpToolRepository>();
    private readonly IMcpResourceRepository _resourceRepository = Substitute.For<IMcpResourceRepository>();
    private readonly IMcpPromptRepository _promptRepository = Substitute.For<IMcpPromptRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IMcpClientFactory _clientFactory = Substitute.For<IMcpClientFactory>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMcpClient _client = Substitute.For<IMcpClient>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc));

    private McpCapabilityRefreshJob CreateJob() => new(
        _serverRepository, _toolRepository, _resourceRepository, _promptRepository, _auditLogRepository, _clientFactory,
        new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxRetries = 0 }), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
        _unitOfWork, _timeProvider, Substitute.For<ILogger<McpCapabilityRefreshJob>>());

    private static McpServer RegisterServer() => McpServer.Register(
        "Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);

    public McpCapabilityRefreshJobTests()
    {
        _clientFactory.GetOrCreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_client);
        _client.ListResourcesAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredResource>)[]);
        _client.ListPromptsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredPrompt>)[]);
        _toolRepository.ListByServerIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpTool>)[]);
    }

    [Fact]
    public async Task RunAsync_ShouldOnlyRefreshServersDueForRefresh_AsDeterminedByTheRepository()
    {
        var dueServer = RegisterServer();
        _serverRepository.ListServersDueForCapabilityRefreshAsync(_timeProvider.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Guid>)[dueServer.Id]);
        _serverRepository.GetByIdAsync(dueServer.Id, Arg.Any<CancellationToken>()).Returns(dueServer);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(dueServer.Id, Arg.Any<CancellationToken>()).Returns(0);
        _client.ListToolsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredTool>)[]);

        await CreateJob().RunAsync(CancellationToken.None);

        _serverRepository.Received(1).AddCapabilitySnapshot(Arg.Is<McpCapabilitySnapshot>(s => s.McpServerId == dueServer.Id));
    }

    [Fact]
    public async Task RunAsync_ShouldContinueRefreshingRemainingServers_WhenOneServersRefreshThrows()
    {
        var serverA = RegisterServer();
        var serverB = RegisterServer();
        _serverRepository.ListServersDueForCapabilityRefreshAsync(_timeProvider.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Guid>)[serverA.Id, serverB.Id]);
        _serverRepository.GetByIdAsync(serverA.Id, Arg.Any<CancellationToken>()).Returns((McpServer?)null); // triggers KeyNotFoundException inside the handler
        _serverRepository.GetByIdAsync(serverB.Id, Arg.Any<CancellationToken>()).Returns(serverB);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(serverB.Id, Arg.Any<CancellationToken>()).Returns(0);
        _client.ListToolsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredTool>)[]);

        var act = async () => await CreateJob().RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        _serverRepository.Received(1).AddCapabilitySnapshot(Arg.Is<McpCapabilitySnapshot>(s => s.McpServerId == serverB.Id));
    }

    [Fact]
    public async Task RunAsync_ShouldDoNothing_WhenNoServerIsDueForRefresh()
    {
        _serverRepository.ListServersDueForCapabilityRefreshAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Guid>)[]);

        await CreateJob().RunAsync(CancellationToken.None);

        _serverRepository.DidNotReceive().AddCapabilitySnapshot(Arg.Any<McpCapabilitySnapshot>());
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
