using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Mcp.Tools;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Mcp;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AskLucy.Application.Tests.Mcp.Fixtures;

/// <summary>
/// Test-only fixture (mirrors <c>FakeHighRiskTool</c>'s spirit, spec.md User Story 3) that builds a
/// real, <c>Active</c>, High/Critical-risk <see cref="McpTool"/> and a fully-wired
/// <see cref="McpToolAdapter"/> around it, so the approval-gate flow (FR-025-FR-030) is exercisable
/// in tests without a real destructive external MCP action. Unlike <c>FakeHighRiskTool</c> — a
/// compile-time <c>IAgentTool</c> DI-registered for Development/Testing — an MCP tool is always
/// data (research.md Decision 10), so this fixture is plain test-project code, never a DI
/// registration; the caller supplies/configures the <see cref="IMcpClient"/> substitute so it can
/// control the simulated server's responses per test.
/// </summary>
public static class McpHighRiskToolFixture
{
    public static McpTool CreateTool(AgentToolRiskLevel riskLevel, string toolName = "highRiskAction", Guid? serverId = null, string actor = "admin-1")
    {
        var tool = McpTool.CreateFromDiscovery(
            serverId ?? Guid.NewGuid(), Guid.NewGuid(), toolName, toolName, $"A simulated {riskLevel} MCP action (test/dev only). Performs no real effect.",
            "{}", "{}", null, riskLevel, "[]", null, null);
        tool.Activate(actor, null, null);
        return tool;
    }

    public static McpToolAdapter CreateAdapter(McpTool tool, IMcpClient client, string serverName = "Test Server")
    {
        var clientFactory = Substitute.For<IMcpClientFactory>();
        clientFactory.GetOrCreateAsync(tool.McpServerId, Arg.Any<CancellationToken>()).Returns(client);
        var rateLimiter = Substitute.For<IMcpRateLimiter>();
        rateLimiter.TryAcquireAsync(Arg.Any<McpRateLimitKey>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        var schemaValidator = Substitute.For<IJsonSchemaValidator>();
        schemaValidator.Validate(default, default, Arg.Any<long>()).ReturnsForAnyArgs([]);

        return new McpToolAdapter(
            tool, serverName, clientFactory, rateLimiter, schemaValidator,
            new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
            Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()));
    }
}
