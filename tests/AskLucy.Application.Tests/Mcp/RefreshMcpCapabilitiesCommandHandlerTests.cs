using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Commands.RefreshMcpCapabilities;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class RefreshMcpCapabilitiesCommandHandlerTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();
    private readonly IMcpToolRepository _toolRepository = Substitute.For<IMcpToolRepository>();
    private readonly IMcpResourceRepository _resourceRepository = Substitute.For<IMcpResourceRepository>();
    private readonly IMcpPromptRepository _promptRepository = Substitute.For<IMcpPromptRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IMcpClientFactory _clientFactory = Substitute.For<IMcpClientFactory>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IMcpClient _client = Substitute.For<IMcpClient>();

    private RefreshMcpCapabilitiesCommandHandler CreateHandler(int maxRetries = 0) => new(
        _serverRepository, _toolRepository, _resourceRepository, _promptRepository, _auditLogRepository, _clientFactory,
        new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxRetries = maxRetries }), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
        _unitOfWork, _currentUser);

    private static McpServer RegisterServer() => McpServer.Register("Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);

    public RefreshMcpCapabilitiesCommandHandlerTests()
    {
        _currentUser.UserId.Returns(AdminId);
        _clientFactory.GetOrCreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_client);
        _resourceRepository.GetLatestByServerAndUriAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((McpResource?)null);
        _promptRepository.GetByNamespacedNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((McpPrompt?)null);
        _toolRepository.ListByServerIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpTool>)[]);
    }

    private static JsonElement Schema() => JsonDocument.Parse("""{"type":"object"}""").RootElement;

    [Fact]
    public async Task Handle_ShouldCreateSuccessfulSnapshot_AndNormalizeTools()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(server.Id, Arg.Any<CancellationToken>()).Returns(0);
        _client.ListToolsAsync(Arg.Any<CancellationToken>()).Returns([new McpDiscoveredTool("search", "Search", "Searches things.", Schema(), null)]);
        _client.ListResourcesAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredResource>)[]);
        _client.ListPromptsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredPrompt>)[]);
        _toolRepository.GetLatestByServerAndToolNameAsync(server.Id, "search", Arg.Any<CancellationToken>()).Returns((McpTool?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new RefreshMcpCapabilitiesCommand(server.Id), CancellationToken.None);

        result.WasSuccessful.Should().BeTrue();
        result.ToolCount.Should().Be(1);
        _serverRepository.Received(1).AddCapabilitySnapshot(Arg.Is<McpCapabilitySnapshot>(s => s!.WasSuccessful && s.SnapshotVersion == 1));
        _toolRepository.Received(1).Add(Arg.Is<McpTool>(t => t!.ToolName == "search" && t.ActivationStatus == McpToolActivationStatus.PendingReview));
    }

    /// <summary>
    /// A tool the server still publishes is refreshed in place, never re-added:
    /// <c>NamespacedName</c> is <c>mcp:{serverId}:{toolName}</c> on every discovery and carries an
    /// unfiltered unique index, so inserting a second row for this snapshot throws a duplicate-key
    /// error against the row it was meant to replace. The administrator's review — activation,
    /// overridden risk level and permission set — survives the refresh intact.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldRefreshAnUnchangedToolInPlace_KeepingItsReview()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(server.Id, Arg.Any<CancellationToken>()).Returns(1);
        var schema = Schema();
        _client.ListToolsAsync(Arg.Any<CancellationToken>()).Returns([new McpDiscoveredTool("search", "Search", "Searches things.", schema, null)]);
        _client.ListResourcesAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredResource>)[]);
        _client.ListPromptsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredPrompt>)[]);

        var priorTool = McpTool.CreateFromDiscovery(server.Id, Guid.NewGuid(), "search", "Search", "Searches things.", schema.GetRawText(), "{}", null, null, "[]", null);
        priorTool.Activate(AdminId, AgentToolRiskLevel.Medium, "[\"ReadExternalData\"]");
        _toolRepository.GetLatestByServerAndToolNameAsync(server.Id, "search", Arg.Any<CancellationToken>()).Returns(priorTool);
        var handler = CreateHandler();

        await handler.Handle(new RefreshMcpCapabilitiesCommand(server.Id), CancellationToken.None);

        _toolRepository.DidNotReceive().Add(Arg.Any<McpTool>());
        priorTool.ActivationStatus.Should().Be(McpToolActivationStatus.Active);
        priorTool.EffectiveRiskLevel.Should().Be(AgentToolRiskLevel.Medium);
        priorTool.RequiredPermissionsJson.Should().Be("[\"ReadExternalData\"]");
    }

    /// <summary>FR-022's re-review rule: a changed definition sends the tool back to PendingReview.</summary>
    [Fact]
    public async Task Handle_ShouldReturnAToolToPendingReview_WhenItsDefinitionChanged()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(server.Id, Arg.Any<CancellationToken>()).Returns(1);
        _client.ListToolsAsync(Arg.Any<CancellationToken>())
            .Returns([new McpDiscoveredTool("search", "Search", "Searches things, but differently now.", Schema(), null)]);
        _client.ListResourcesAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredResource>)[]);
        _client.ListPromptsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredPrompt>)[]);

        var priorTool = McpTool.CreateFromDiscovery(server.Id, Guid.NewGuid(), "search", "Search", "Searches things.", Schema().GetRawText(), "{}", null, null, "[]", null);
        priorTool.Activate(AdminId, AgentToolRiskLevel.Low, "[\"ExternalNetwork\"]");
        _toolRepository.GetLatestByServerAndToolNameAsync(server.Id, "search", Arg.Any<CancellationToken>()).Returns(priorTool);
        var handler = CreateHandler();

        var result = await handler.Handle(new RefreshMcpCapabilitiesCommand(server.Id), CancellationToken.None);

        _toolRepository.DidNotReceive().Add(Arg.Any<McpTool>());
        priorTool.ActivationStatus.Should().Be(McpToolActivationStatus.PendingReview);
        priorTool.EffectiveRiskLevel.Should().Be(AgentToolRiskLevel.Critical);
        priorTool.RequiredPermissionsJson.Should().Be("[]");
        result.ChangeSummaryJson.Should().Contain("\"changed\":1");
    }

    /// <summary>A known resource has the same problem as a known tool — refreshed, never duplicated.</summary>
    [Fact]
    public async Task Handle_ShouldRefreshAKnownResourceInPlace()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(server.Id, Arg.Any<CancellationToken>()).Returns(1);
        _client.ListToolsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredTool>)[]);
        _client.ListResourcesAsync(Arg.Any<CancellationToken>())
            .Returns([new McpDiscoveredResource("file:///notes.txt", "Notes", "Renamed", "text/plain")]);
        _client.ListPromptsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredPrompt>)[]);

        var priorResource = McpResource.CreateFromDiscovery(server.Id, Guid.NewGuid(), "file:///notes.txt", "Notes", null, "text/plain");
        priorResource.MarkUnavailable();
        _resourceRepository.GetLatestByServerAndUriAsync(server.Id, "file:///notes.txt", Arg.Any<CancellationToken>()).Returns(priorResource);
        var handler = CreateHandler();

        await handler.Handle(new RefreshMcpCapabilitiesCommand(server.Id), CancellationToken.None);

        _resourceRepository.DidNotReceive().Add(Arg.Any<McpResource>());
        priorResource.Description.Should().Be("Renamed");
        priorResource.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldPreserveTheGranularChangeSummary_ReflectingAddedChangedRemoved()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(server.Id, Arg.Any<CancellationToken>()).Returns(0);
        _client.ListToolsAsync(Arg.Any<CancellationToken>()).Returns([new McpDiscoveredTool("newTool", "New", "desc", Schema(), null)]);
        _client.ListResourcesAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredResource>)[]);
        _client.ListPromptsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredPrompt>)[]);
        _toolRepository.GetLatestByServerAndToolNameAsync(server.Id, "newTool", Arg.Any<CancellationToken>()).Returns((McpTool?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new RefreshMcpCapabilitiesCommand(server.Id), CancellationToken.None);

        result.ChangeSummaryJson.Should().Contain("\"added\":1");
    }

    [Fact]
    public async Task Handle_ShouldNotTouchPriorSnapshot_WhenDiscoveryFails()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(server.Id, Arg.Any<CancellationToken>()).Returns(1);
        _client.ListToolsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<McpDiscoveredTool>>(_ => throw new InvalidOperationException("discovery failed"));
        var handler = CreateHandler();

        var result = await handler.Handle(new RefreshMcpCapabilitiesCommand(server.Id), CancellationToken.None);

        result.WasSuccessful.Should().BeFalse();
        _toolRepository.DidNotReceive().Add(Arg.Any<McpTool>());
        _serverRepository.Received(1).AddCapabilitySnapshot(Arg.Is<McpCapabilitySnapshot>(s => !s!.WasSuccessful));
        _auditLogRepository.Received(1).Add(Arg.Is<McpAuditLog>(a => a!.Action == McpAuditAction.CapabilityDiscoveryFailed));
    }
}
