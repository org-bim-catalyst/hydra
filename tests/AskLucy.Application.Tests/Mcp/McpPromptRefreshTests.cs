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

/// <summary>
/// spec.md FR-044/clarification — an `McpPrompt.ContentTemplate` re-syncs in place on every
/// successful capability refresh (never a new row per snapshot, unlike `McpTool`/`McpResource`).
/// The "disabled/removed source server shows the prompt as unavailable" half of FR-044 is enforced
/// by <c>IMcpPromptRepository.ListAvailableAsync</c>'s join against <c>McpServer.IsEnabled</c>
/// (`McpPromptRepository.cs`) — a SQL-level concern this Application-layer suite cannot exercise
/// without a live database (see `docs/TESTING.md` §13); the join itself mirrors
/// `IMcpToolRepository.ListActiveAvailableAsync`'s already-established, identically-shaped filter.
/// </summary>
public sealed class McpPromptRefreshTests
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

    private RefreshMcpCapabilitiesCommandHandler CreateHandler() => new(
        _serverRepository, _toolRepository, _resourceRepository, _promptRepository, _auditLogRepository, _clientFactory,
        new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxRetries = 0 }), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
        _unitOfWork, _currentUser);

    private static McpServer RegisterServer() => McpServer.Register("Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);

    public McpPromptRefreshTests()
    {
        _currentUser.UserId.Returns(AdminId);
        _clientFactory.GetOrCreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_client);
        _resourceRepository.GetLatestByServerAndUriAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((McpResource?)null);
        _toolRepository.ListByServerIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpTool>)[]);
        _toolRepository.GetLatestByServerAndToolNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((McpTool?)null);
        _client.ListToolsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredTool>)[]);
        _client.ListResourcesAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<McpDiscoveredResource>)[]);
    }

    [Fact]
    public async Task Handle_ShouldResyncTheContentTemplate_ForAnExistingPrompt_OnASuccessfulRefresh()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(server.Id, Arg.Any<CancellationToken>()).Returns(1);
        _client.ListPromptsAsync(Arg.Any<CancellationToken>()).Returns([new McpDiscoveredPrompt("summarize", "Summarizes text.")]);
        _client.GetPromptAsync("summarize", null, Arg.Any<CancellationToken>()).Returns("Summarize the following text concisely, v2.");

        var existingPrompt = McpPrompt.CreateFromDiscovery(server.Id, Guid.NewGuid(), "summarize", "Summarizes text.", "Summarize the following text concisely, v1.");
        _promptRepository.GetByNamespacedNameAsync(existingPrompt.NamespacedName, Arg.Any<CancellationToken>()).Returns(existingPrompt);
        var handler = CreateHandler();

        var result = await handler.Handle(new RefreshMcpCapabilitiesCommand(server.Id), CancellationToken.None);

        result.WasSuccessful.Should().BeTrue();
        existingPrompt.ContentTemplate.Should().Be("Summarize the following text concisely, v2.");
        _promptRepository.DidNotReceive().Add(Arg.Any<McpPrompt>());
    }

    [Fact]
    public async Task Handle_ShouldCreateANewPromptRow_WhenDiscoveredForTheFirstTime()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(server.Id, Arg.Any<CancellationToken>()).Returns(0);
        _client.ListPromptsAsync(Arg.Any<CancellationToken>()).Returns([new McpDiscoveredPrompt("summarize", "Summarizes text.")]);
        _client.GetPromptAsync("summarize", null, Arg.Any<CancellationToken>()).Returns("Summarize the following text concisely.");
        _promptRepository.GetByNamespacedNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((McpPrompt?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new RefreshMcpCapabilitiesCommand(server.Id), CancellationToken.None);

        result.WasSuccessful.Should().BeTrue();
        result.PromptCount.Should().Be(1);
        _promptRepository.Received(1).Add(Arg.Is<McpPrompt>(p => p!.Name == "summarize" && p.ContentTemplate == "Summarize the following text concisely."));
    }

    [Fact]
    public async Task Handle_ShouldNeverTouchAnyPrompt_WhenDiscoveryFails()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetLatestCapabilitySnapshotVersionAsync(server.Id, Arg.Any<CancellationToken>()).Returns(1);
        _client.ListToolsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<McpDiscoveredTool>>(_ => throw new InvalidOperationException("discovery failed"));
        var handler = CreateHandler();

        var result = await handler.Handle(new RefreshMcpCapabilitiesCommand(server.Id), CancellationToken.None);

        result.WasSuccessful.Should().BeFalse();
        _promptRepository.DidNotReceive().Add(Arg.Any<McpPrompt>());
        await _client.DidNotReceive().GetPromptAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }
}
