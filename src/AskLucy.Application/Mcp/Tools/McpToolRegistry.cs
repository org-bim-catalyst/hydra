using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Mcp.Tools;

/// <summary>
/// Singleton in-memory cache of every currently-active MCP tool, adapted into <see cref="IAgentTool"/>
/// (research.md Decision 1/4). Depends on <see cref="IServiceScopeFactory"/> rather than
/// <see cref="IMcpToolRepository"/> directly — the repository is Scoped (EF Core-backed); this
/// registry is a singleton, so it resolves the repository from a short-lived scope only for the
/// duration of <see cref="InvalidateAsync"/>, never holding it (constitution §3, same captive-
/// dependency concern <c>McpClientFactory</c> resolves the same way).
/// </summary>
public sealed class McpToolRegistry(
    IServiceScopeFactory scopeFactory,
    IMcpClientFactory clientFactory,
    IMcpRateLimiter rateLimiter,
    IJsonSchemaValidator schemaValidator,
    McpConnectionResiliencePolicy resiliencePolicy,
    IOptions<McpRuntimeOptions> options) : IMcpToolRegistry
{
    private volatile IReadOnlyCollection<IAgentTool> _activeTools = [];

    public IReadOnlyCollection<IAgentTool> ActiveTools => _activeTools;

    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var toolRepository = scope.ServiceProvider.GetRequiredService<IMcpToolRepository>();

        var tools = await toolRepository.ListActiveAvailableAsync(cancellationToken);
        _activeTools = tools
            .Select(row => (IAgentTool)new McpToolAdapter(row.Tool, row.ServerName, clientFactory, rateLimiter, schemaValidator, resiliencePolicy, options))
            .ToList();
    }
}
