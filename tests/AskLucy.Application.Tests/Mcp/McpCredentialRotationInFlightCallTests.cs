using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp.Commands.RotateMcpServerCredential;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Mcp.Tools;
using AskLucy.Application.Options;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>
/// spec.md User Story 7 Acceptance Scenario 3/SC-009 — an MCP tool call already holding a
/// connection opened with the *old* credential at the moment that credential is rotated still
/// resolves to a definite, recorded <see cref="AgentToolResult"/> (never silently disappears);
/// the next call afterward reconnects and uses the new credential.
/// </summary>
public sealed class McpCredentialRotationInFlightCallTests
{
    private const string AdminId = "admin-1";

    /// <summary>Models the real <c>McpClientFactory</c>'s cache-invalidation contract without the actual MCP SDK/network layer: <see cref="InvalidateConnectionAsync"/> forces the next <see cref="GetOrCreateAsync"/> call to hand out a different (freshly "connected") client.</summary>
    private sealed class FakeClientFactory : IMcpClientFactory
    {
        private IMcpClient _current;

        public FakeClientFactory(IMcpClient initialClient) => _current = initialClient;

        public IMcpClient CurrentClient => _current;

        public void SetNextClient(IMcpClient client) => _current = client;

        public Task<IMcpClient> GetOrCreateAsync(Guid mcpServerId, CancellationToken cancellationToken = default) => Task.FromResult(_current);

        public Task InvalidateConnectionAsync(Guid mcpServerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnARecordedFailure_ForACallHeldOnTheConnectionOpenedWithTheOldCredential_AfterRotation()
    {
        var server = McpServer.Register("Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);
        var tool = McpTool.CreateFromDiscovery(server.Id, Guid.NewGuid(), "search", "Search", "desc", "{}", "{}", null, null, "[]", null, null);

        var staleClient = Substitute.For<IMcpClient>();
        // The old connection's baked-in Authorization header is now rejected by the server —
        // this is what "the old credential no longer works" looks like from the adapter's side.
        staleClient.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns<McpToolCallResult>(_ => throw new UnauthorizedAccessException("authentication rejected"));

        var clientFactory = new FakeClientFactory(staleClient);
        var rateLimiter = Substitute.For<IMcpRateLimiter>();
        rateLimiter.TryAcquireAsync(Arg.Any<McpRateLimitKey>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        var adapter = new McpToolAdapter(
            tool, "Test Server", clientFactory, rateLimiter, Substitute.For<IJsonSchemaValidator>(),
            new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxRetries = 0 }), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
            Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()));

        // Credential rotation happens concurrently — this adapter already resolved `staleClient`
        // above (as if mid-call) and proceeds to use it regardless.
        var serverRepository = Substitute.For<IMcpServerRepository>();
        serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        serverRepository.GetCredentialAsync(server.Id, Arg.Any<CancellationToken>()).Returns((McpServerCredential?)null);
        var credentialProtector = Substitute.For<IMcpCredentialProtector>();
        credentialProtector.Protect(Arg.Any<string>()).Returns("new-ciphertext");
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AdminId);
        var rotateHandler = new RotateMcpServerCredentialCommandHandler(
            serverRepository, Substitute.For<IMcpAuditLogRepository>(), credentialProtector, clientFactory, Substitute.For<IUnitOfWork>(), currentUser);
        await rotateHandler.Handle(new RotateMcpServerCredentialCommand(server.Id, "new-secret"), CancellationToken.None);

        var context = new AgentToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), AdminId, Guid.NewGuid(), Guid.NewGuid(), null);
        var result = await adapter.ExecuteAsync(context, JsonDocument.Parse("{}"));

        // Never silently disappears — a definite, categorized, recorded outcome.
        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("AuthenticationFailure");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSucceed_ForTheNextCallAfterRotation_UsingTheNewConnection()
    {
        var server = McpServer.Register("Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);
        var tool = McpTool.CreateFromDiscovery(server.Id, Guid.NewGuid(), "search", "Search", "desc", "{}", "{}", null, null, "[]", null, null);

        var freshClient = Substitute.For<IMcpClient>();
        freshClient.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(new McpToolCallResult(false, JsonDocument.Parse("{}"), null));

        var clientFactory = new FakeClientFactory(Substitute.For<IMcpClient>());
        clientFactory.SetNextClient(freshClient); // simulates a reconnect after rotation
        var rateLimiter = Substitute.For<IMcpRateLimiter>();
        rateLimiter.TryAcquireAsync(Arg.Any<McpRateLimitKey>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        var adapter = new McpToolAdapter(
            tool, "Test Server", clientFactory, rateLimiter, Substitute.For<IJsonSchemaValidator>(),
            new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxRetries = 0 }), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
            Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()));

        var context = new AgentToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), AdminId, Guid.NewGuid(), Guid.NewGuid(), null);
        var result = await adapter.ExecuteAsync(context, JsonDocument.Parse("{}"));

        result.Succeeded.Should().BeTrue();
    }
}
