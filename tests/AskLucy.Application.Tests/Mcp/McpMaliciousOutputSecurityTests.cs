using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Mcp.Tools;
using AskLucy.Application.Mcp.Validation;
using AskLucy.Application.Options;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>
/// spec.md Security Tests — malicious/oversized MCP tool output. Most of this matrix is already
/// covered elsewhere and cross-referenced here rather than duplicated: schema-violating payloads
/// (<c>McpToolAdapterTests.ExecuteAsync_ShouldFailWithInvalidResponseCategory_WhenOutputFailsSchemaValidation</c>,
/// <c>JsonSchemaValidatorTests</c>), malformed/unparseable schema documents
/// (<c>JsonSchemaValidatorTests</c>'s malformed-schema case), and prompt-injection text embedded in
/// output (<c>McpUntrustedContentFramingTests</c>, <c>McpHighRiskApprovalTests</c>'s injection
/// scenario). This file fills the one genuine gap: <see cref="McpToolAdapter"/> had no test proving
/// an oversized-but-otherwise-schema-valid tool response is rejected (only
/// <c>McpResourceReadToolTests</c> had this for resource reads).
/// </summary>
public sealed class McpMaliciousOutputSecurityTests
{
    private readonly IMcpClientFactory _clientFactory = Substitute.For<IMcpClientFactory>();
    private readonly IMcpRateLimiter _rateLimiter = Substitute.For<IMcpRateLimiter>();
    private readonly IMcpClient _client = Substitute.For<IMcpClient>();

    private McpToolAdapter CreateAdapter(McpTool tool, long maxResponseSizeBytes) => new(
        tool, "Test Server", _clientFactory, _rateLimiter, new JsonSchemaValidator(),
        new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxRetries = 0 }), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
        Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxResponseSizeBytes = maxResponseSizeBytes }));

    public McpMaliciousOutputSecurityTests()
    {
        _rateLimiter.TryAcquireAsync(Arg.Any<McpRateLimitKey>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        _clientFactory.GetOrCreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_client);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectAnOversizedResponse_EvenWhenItIsOtherwiseSchemaValid()
    {
        var tool = McpTool.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "desc", "{}", "{}", null, null, "[]", null, null);
        var hugePayload = JsonDocument.Parse($$"""{"result":"{{new string('a', 10_000)}}"}""");
        _client.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>()).Returns(new McpToolCallResult(false, hugePayload, null));
        var adapter = CreateAdapter(tool, maxResponseSizeBytes: 1024);

        var result = await adapter.ExecuteAsync(new AgentToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), null), JsonDocument.Parse("{}"));

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("InvalidResponse");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAccept_AResponseAtExactlyTheSizeLimit()
    {
        // Boundary check — the limit itself is inclusive, not off-by-one.
        var tool = McpTool.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "desc", "{}", "{}", null, null, "[]", null, null);
        var payload = JsonDocument.Parse("""{}""");
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(payload.RootElement.GetRawText());
        _client.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>()).Returns(new McpToolCallResult(false, payload, null));
        var adapter = CreateAdapter(tool, maxResponseSizeBytes: byteCount);

        var result = await adapter.ExecuteAsync(new AgentToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), null), JsonDocument.Parse("{}"));

        result.Succeeded.Should().BeTrue();
    }
}
