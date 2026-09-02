using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Commands.RefreshMcpCapabilities;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Options;
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

    [Fact]
    public async Task Handle_ShouldPreserveExistingActivation_WhenToolIsUnchanged()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(server.Id, Arg.Any<CancellationToken>()).Returns(1);
        var schema = Schema();
        _client.ListToolsAsync(Arg.Any<CancellationToken>()).Returns([new McpDiscoveredTool("search", "Search", "Searches things.", schema, null)]);
        _client.ListResourcesAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredResource>)[]);
        _client.ListPromptsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredPrompt>)[]);

        var priorTool = McpTool.CreateFromDiscovery(server.Id, Guid.NewGuid(), "search", "Search", "Searches things.", schema.GetRawText(), "{}", null, null, "[]", null, null);
        priorTool.Activate(AdminId, null, null);
        _toolRepository.GetLatestByServerAndToolNameAsync(server.Id, "search", Arg.Any<CancellationToken>()).Returns(priorTool);
        var handler = CreateHandler();

        await handler.Handle(new RefreshMcpCapabilitiesCommand(server.Id), CancellationToken.None);

        _toolRepository.Received(1).Add(Arg.Is<McpTool>(t => t!.ActivationStatus == McpToolActivationStatus.Active));
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
