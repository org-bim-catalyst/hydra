using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Mcp.Tools;
using AskLucy.Application.Options;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class McpToolAdapterTests
{
    private readonly IMcpClientFactory _clientFactory = Substitute.For<IMcpClientFactory>();
    private readonly IMcpRateLimiter _rateLimiter = Substitute.For<IMcpRateLimiter>();
    private readonly IJsonSchemaValidator _schemaValidator = Substitute.For<IJsonSchemaValidator>();
    private readonly IMcpClient _client = Substitute.For<IMcpClient>();

    private static McpTool CreateTool(string outputSchemaJson = "{}") => McpTool.CreateFromDiscovery(
        Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "Searches things.", "{}", outputSchemaJson, null, null, "[]", null, null);

    private McpToolAdapter CreateAdapter(McpTool tool, int maxCallDurationSeconds = 30) => new(
        tool, "Test Server", _clientFactory, _rateLimiter, _schemaValidator,
        new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxRetries = 0 }), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
        Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxCallDurationSeconds = maxCallDurationSeconds }));

    private static AgentToolExecutionContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), null);

    private static JsonDocument EmptyInput() => JsonDocument.Parse("{}");

    public McpToolAdapterTests()
    {
        _rateLimiter.TryAcquireAsync(Arg.Any<McpRateLimitKey>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        _clientFactory.GetOrCreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_client);
        _schemaValidator.Validate(Arg.Any<JsonElement>(), Arg.Any<JsonElement>(), Arg.Any<long>()).Returns([]);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnSuccess_WhenCallSucceedsAndOutputMatchesSchema()
    {
        var tool = CreateTool();
        _client.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(new McpToolCallResult(false, JsonDocument.Parse("""{"result":"ok"}"""), null));
        var adapter = CreateAdapter(tool);

        var result = await adapter.ExecuteAsync(Context(), EmptyInput());

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("result").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWithRateLimitCategory_WhenRateLimiterDeniesAcquisition_AndNeverCallTheClient()
    {
        var tool = CreateTool();
        _rateLimiter.TryAcquireAsync(Arg.Any<McpRateLimitKey>(), Arg.Any<CancellationToken>()).Returns((IAsyncDisposable?)null);
        var adapter = CreateAdapter(tool);

        var result = await adapter.ExecuteAsync(Context(), EmptyInput());

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("[RateLimit]");
        await _client.DidNotReceive().CallToolAsync(Arg.Any<string>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWithServerErrorCategory_WhenTheMcpServerReportsAnError()
    {
        var tool = CreateTool();
        _client.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(new McpToolCallResult(true, null, "boom"));
        var adapter = CreateAdapter(tool);

        var result = await adapter.ExecuteAsync(Context(), EmptyInput());

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("[ServerError]").And.Contain("boom");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWithInvalidResponseCategory_WhenOutputFailsSchemaValidation()
    {
        var tool = CreateTool();
        _client.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(new McpToolCallResult(false, JsonDocument.Parse("""{"result":123}"""), null));
        _schemaValidator.Validate(Arg.Any<JsonElement>(), Arg.Any<JsonElement>(), Arg.Any<long>()).Returns(["result must be a string"]);
        var adapter = CreateAdapter(tool);

        var result = await adapter.ExecuteAsync(Context(), EmptyInput());

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("[InvalidResponse]");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWithTimeoutCategory_WhenTheCallExceedsMaxCallDurationSeconds()
    {
        var tool = CreateTool();
        _client.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => DelayForeverAsync(callInfo.ArgAt<CancellationToken>(2)));
        var adapter = CreateAdapter(tool, maxCallDurationSeconds: 1);

        var result = await adapter.ExecuteAsync(Context(), EmptyInput());

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("[Timeout]");
    }

    private static async Task<McpToolCallResult> DelayForeverAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new McpToolCallResult(false, null, null);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWithConnectionFailureCategory_AndNeverLeakTheRawExceptionMessage_WhenTheClientThrows()
    {
        var tool = CreateTool();
        _client.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns<McpToolCallResult>(_ => throw new InvalidOperationException("secret-token-abc123 leaked"));
        var adapter = CreateAdapter(tool);

        var result = await adapter.ExecuteAsync(Context(), EmptyInput());

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("[ConnectionFailure]");
        result.FailureReason.Should().NotContain("secret-token-abc123");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNeverThrow_ForAnOrdinaryMcpSideFailure()
    {
        var tool = CreateTool();
        _client.CallToolAsync(tool.ToolName, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns<McpToolCallResult>(_ => throw new InvalidOperationException("connection refused"));
        var adapter = CreateAdapter(tool);

        var act = async () => await adapter.ExecuteAsync(Context(), EmptyInput());

        await act.Should().NotThrowAsync();
    }
}
