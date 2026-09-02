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
/// spec.md FR-037-FR-039 — an agent reads MCP resource content through the exact same
/// runtime-enforced tool-call path (authorization, execution-history recording via the returned
/// `AgentToolResult`) as any other tool call; size/time limits apply identically to
/// <see cref="McpToolAdapter"/>. Never automatically indexes into RAG — this tool has no
/// dependency on any Knowledge Base/RAG abstraction at all, so "no automatic ingestion" is
/// structurally guaranteed, not merely a behavioral choice (FR-039).
/// </summary>
public sealed class McpResourceReadToolTests
{
    private readonly IMcpResourceRepository _resourceRepository = Substitute.For<IMcpResourceRepository>();
    private readonly IMcpClientFactory _clientFactory = Substitute.For<IMcpClientFactory>();
    private readonly IMcpRateLimiter _rateLimiter = Substitute.For<IMcpRateLimiter>();
    private readonly IMcpClient _client = Substitute.For<IMcpClient>();

    private McpResourceReadTool CreateTool(int maxCallDurationSeconds = 30, long maxResponseSizeBytes = 5_000_000) => new(
        _resourceRepository, _clientFactory, _rateLimiter, new JsonSchemaValidator(),
        new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxRetries = 0 }), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
        Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions { MaxCallDurationSeconds = maxCallDurationSeconds, MaxResponseSizeBytes = maxResponseSizeBytes }));

    private static McpResource CreateResource(Guid? serverId = null) =>
        McpResource.CreateFromDiscovery(serverId ?? Guid.NewGuid(), Guid.NewGuid(), "file:///report.txt", "Report", "A report.", "text/plain");

    private static AgentToolExecutionContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), null);

    private static JsonDocument InputFor(string namespacedName) => JsonDocument.Parse($$"""{"resourceUri":"{{namespacedName}}"}""");

    public McpResourceReadToolTests()
    {
        _rateLimiter.TryAcquireAsync(Arg.Any<McpRateLimitKey>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        _clientFactory.GetOrCreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_client);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnSuccess_ForAnAuthorizedAvailableResource()
    {
        var resource = CreateResource();
        _resourceRepository.GetAvailableByNamespacedNameAsync(resource.NamespacedName, Arg.Any<CancellationToken>()).Returns(resource);
        _client.ReadResourceAsync(resource.Uri, Arg.Any<CancellationToken>()).Returns(JsonDocument.Parse("""{"content":"report text"}"""));
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(Context(), InputFor(resource.NamespacedName), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("content").GetString().Should().Be("report text");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTheResourceIsNotAvailable_AndNeverCallTheClient()
    {
        _resourceRepository.GetAvailableByNamespacedNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((McpResource?)null);
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(Context(), InputFor("mcp:some-server:file:///missing.txt"), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        await _client.DidNotReceive().ReadResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTheRateLimitIsExhausted()
    {
        var resource = CreateResource();
        _resourceRepository.GetAvailableByNamespacedNameAsync(resource.NamespacedName, Arg.Any<CancellationToken>()).Returns(resource);
        _rateLimiter.TryAcquireAsync(Arg.Any<McpRateLimitKey>(), Arg.Any<CancellationToken>()).Returns((IAsyncDisposable?)null);
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(Context(), InputFor(resource.NamespacedName), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("RateLimit");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTheResponseExceedsTheConfiguredSizeLimit()
    {
        var resource = CreateResource();
        _resourceRepository.GetAvailableByNamespacedNameAsync(resource.NamespacedName, Arg.Any<CancellationToken>()).Returns(resource);
        _client.ReadResourceAsync(resource.Uri, Arg.Any<CancellationToken>())
            .Returns(JsonDocument.Parse($$"""{"content":"{{new string('a', 200)}}"}"""));
        var tool = CreateTool(maxResponseSizeBytes: 50);

        var result = await tool.ExecuteAsync(Context(), InputFor(resource.NamespacedName), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("InvalidResponse");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTheCallExceedsMaxCallDurationSeconds()
    {
        var resource = CreateResource();
        _resourceRepository.GetAvailableByNamespacedNameAsync(resource.NamespacedName, Arg.Any<CancellationToken>()).Returns(resource);
        _client.ReadResourceAsync(resource.Uri, Arg.Any<CancellationToken>())
            .Returns(callInfo => DelayForeverAsync(callInfo.ArgAt<CancellationToken>(1)));
        var tool = CreateTool(maxCallDurationSeconds: 1);

        var result = await tool.ExecuteAsync(Context(), InputFor(resource.NamespacedName), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("Timeout");
    }

    private static async Task<JsonDocument> DelayForeverAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return JsonDocument.Parse("{}");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNeverThrow_ForAnOrdinaryMcpSideFailure()
    {
        var resource = CreateResource();
        _resourceRepository.GetAvailableByNamespacedNameAsync(resource.NamespacedName, Arg.Any<CancellationToken>()).Returns(resource);
        _client.ReadResourceAsync(resource.Uri, Arg.Any<CancellationToken>())
            .Returns<JsonDocument>(_ => throw new InvalidOperationException("connection refused"));
        var tool = CreateTool();

        var act = async () => await tool.ExecuteAsync(Context(), InputFor(resource.NamespacedName));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void McpResourceReadTool_ShouldHaveNoKnowledgeBaseOrRagDependency_ProvingNoAutomaticIngestionIsPossible()
    {
        // FR-039 — structurally guaranteed: there is no repository/service this class could call
        // to index a fetched resource into a Knowledge Base even if it wanted to.
        var parameterTypeNames = typeof(McpResourceReadTool).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name);
        parameterTypeNames.Should().NotContain(name => name.Contains("KnowledgeBase") || name.Contains("Rag") || name.Contains("Retrieval"));
    }
}
