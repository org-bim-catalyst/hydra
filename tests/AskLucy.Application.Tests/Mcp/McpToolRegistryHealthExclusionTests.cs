using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Mcp.Tools;
using AskLucy.Application.Options;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>
/// spec.md FR-056 — <c>McpToolRegistry.ActiveTools</c> excludes a tool the moment its server's
/// health leaves `Healthy` and re-includes it on recovery. The `Unavailable`/`AuthenticationFailed`
/// exclusion itself is <c>IMcpToolRepository.ListActiveAvailableAsync</c>'s existing filter (built
/// in US1, unmodified for US6); what US6 actually adds is <c>McpServerHealthCheckJob</c> calling
/// <c>InvalidateAsync</c> after every health-check sweep so the cached snapshot picks up a health
/// transition immediately rather than waiting for an unrelated activate/deactivate trigger — this
/// test proves the registry has no stale-merge bug across two different <c>InvalidateAsync</c>
/// results (i.e., a fresh, empty result actually clears a previously-active tool).
/// </summary>
public sealed class McpToolRegistryHealthExclusionTests
{
    private static McpToolRegistry CreateRegistry(IMcpToolRepository toolRepository)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IMcpToolRepository)).Returns(toolRepository);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return new McpToolRegistry(
            scopeFactory,
            Substitute.For<IMcpClientFactory>(), Substitute.For<IMcpRateLimiter>(), Substitute.For<IJsonSchemaValidator>(),
            new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
            Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()));
    }

    [Fact]
    public async Task InvalidateAsync_ShouldExcludeTheTool_WhenTheLatestQueryNoLongerReturnsIt_SimulatingHealthLeavingHealthy()
    {
        var tool = McpTool.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "desc", "{}", "{}", null, null, "[]", null, null);
        var toolRepository = Substitute.For<IMcpToolRepository>();
        toolRepository.ListActiveAvailableAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<(McpTool Tool, string ServerName)>)[(tool, "Acme Docs")]);
        var registry = CreateRegistry(toolRepository);

        await registry.InvalidateAsync();
        registry.ActiveTools.Should().ContainSingle(t => t.Name == tool.NamespacedName);

        // Simulates the server's health leaving Healthy — the repository's own join (already
        // Active/Available/enabled/healthy filtered) simply stops returning this tool.
        toolRepository.ListActiveAvailableAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<(McpTool Tool, string ServerName)>)[]);
        await registry.InvalidateAsync();

        registry.ActiveTools.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidateAsync_ShouldReincludeTheTool_WhenTheServerRecovers()
    {
        var tool = McpTool.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "desc", "{}", "{}", null, null, "[]", null, null);
        var toolRepository = Substitute.For<IMcpToolRepository>();
        toolRepository.ListActiveAvailableAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<(McpTool Tool, string ServerName)>)[]);
        var registry = CreateRegistry(toolRepository);

        await registry.InvalidateAsync();
        registry.ActiveTools.Should().BeEmpty();

        toolRepository.ListActiveAvailableAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<(McpTool Tool, string ServerName)>)[(tool, "Acme Docs")]);
        await registry.InvalidateAsync();

        registry.ActiveTools.Should().ContainSingle(t => t.Name == tool.NamespacedName);
    }
}
