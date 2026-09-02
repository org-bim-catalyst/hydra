using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Mcp.Tools;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>
/// research.md Decision 17 — the two-level failure split: every MCP-side failure, regardless of
/// its specific cause, resolves to the same coarse <see cref="AgentExecutionErrorCategory.ToolFailure"/>
/// at the execution-history level (proven end-to-end for one cause by
/// <see cref="McpToolExecutionOrchestratorIntegrationTests"/>); the granular
/// <see cref="McpFailureCategory"/> distinguishing *why* it failed is preserved only in the
/// `[CategoryName]`-prefixed <see cref="AgentToolResult.FailureReason"/> text, never written to
/// `McpAuditLog` (data-model.md's non-duplication note).
/// </summary>
public sealed class McpFailureCategorizationTests
{
    private readonly IMcpClientFactory _clientFactory = Substitute.For<IMcpClientFactory>();
    private readonly IMcpRateLimiter _rateLimiter = Substitute.For<IMcpRateLimiter>();
    private readonly IJsonSchemaValidator _schemaValidator = Substitute.For<IJsonSchemaValidator>();
    private readonly IMcpClient _client = Substitute.For<IMcpClient>();

    private McpToolAdapter CreateAdapter(McpTool tool) => new(
        tool, "Test Server", _clientFactory, _rateLimiter, _schemaValidator,
        new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxRetries = 0 }), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
        Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()));

    public McpFailureCategorizationTests()
    {
        _rateLimiter.TryAcquireAsync(Arg.Any<McpRateLimitKey>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        _clientFactory.GetOrCreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_client);
        _schemaValidator.Validate(Arg.Any<JsonElement>(), Arg.Any<JsonElement>(), Arg.Any<long>()).Returns([]);
    }

    [Theory]
    [InlineData(McpFailureCategory.RateLimit)]
    [InlineData(McpFailureCategory.ServerError)]
    [InlineData(McpFailureCategory.ConnectionFailure)]
    public async Task ExecuteAsync_ShouldEmbedTheGranularCategory_InFailureReason_ForEveryDistinctFailureCause(McpFailureCategory expectedCategory)
    {
        var tool = McpTool.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "desc", "{}", "{}", null, null, "[]", null, null);
        var adapter = CreateAdapter(tool);

        switch (expectedCategory)
        {
            case McpFailureCategory.RateLimit:
                _rateLimiter.TryAcquireAsync(Arg.Any<McpRateLimitKey>(), Arg.Any<CancellationToken>()).Returns((IAsyncDisposable?)null);
                break;
            case McpFailureCategory.ServerError:
                _client.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
                    .Returns(new McpToolCallResult(true, null, "downstream error"));
                break;
            case McpFailureCategory.ConnectionFailure:
                _client.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
                    .Returns<McpToolCallResult>(_ => throw new InvalidOperationException("refused"));
                break;
        }

        var result = await adapter.ExecuteAsync(new AgentToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), null), JsonDocument.Parse("{}"), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().StartWith($"[{expectedCategory}]");
    }

    [Fact]
    public void McpToolAdapter_ShouldHaveNoMcpAuditLogRepositoryDependency_ProvingNoFailureEverWritesToMcpAuditLog()
    {
        // The constructor signature itself is the proof every failure path stays within
        // FailureReason, never an McpAuditLog write (data-model.md's explicit "does not
        // duplicate AgentToolCall" rule) — there is no repository to write through.
        typeof(McpToolAdapter).GetConstructors().Should().ContainSingle()
            .Which.GetParameters().Should().NotContain(p => p.ParameterType == typeof(IMcpAuditLogRepository));
    }
}
