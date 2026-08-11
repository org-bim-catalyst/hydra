using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Mcp;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Mcp.Tools;

/// <summary>
/// contracts/mcp-tool-adapter.md — one built-in adapter for every MCP resource (unlike
/// <see cref="McpToolAdapter"/>, instantiated once per discovered tool), so an agent that must
/// fetch resource content goes through the exact same tool-call runtime path (permission check,
/// approval gate, output validation, duplicate-call detection) as any other tool — no
/// resource-specific execution path (FR-037). Fixed <see cref="RiskLevel"/> (read-only by MCP
/// protocol definition) and <see cref="RequiredPermissions"/>, unlike <see cref="McpToolAdapter"/>
/// whose values come from the discovered <see cref="McpTool"/> row.
/// </summary>
public sealed class McpResourceReadTool(
    IMcpResourceRepository resourceRepository,
    IMcpClientFactory clientFactory,
    IMcpRateLimiter rateLimiter,
    IJsonSchemaValidator schemaValidator,
    McpConnectionResiliencePolicy resiliencePolicy,
    IOptions<McpRuntimeOptions> options) : IAgentTool
{
    public string Name => "McpResourceReadTool";

    public string Description => "Reads the content of an MCP resource by its namespaced name (from the MCP catalog). Read-only.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ReadExternalData];

    public string InputSchemaJson => """{"type":"object","required":["resourceUri"],"properties":{"resourceUri":{"type":"string"}}}""";

    public string OutputSchemaJson => "{}";

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("resourceUri", out var resourceUriElement) || resourceUriElement.GetString() is not { Length: > 0 } namespacedName)
        {
            return AgentToolResult.Failure("A resourceUri (the resource's namespaced name from the MCP catalog) is required.");
        }

        var resource = await resourceRepository.GetAvailableByNamespacedNameAsync(namespacedName, cancellationToken);
        if (resource is null)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.InvalidRequest}] Resource '{namespacedName}' was not found or is no longer available.");
        }

        var rateLimitKey = new McpRateLimitKey(resource.McpServerId, Name, context.UserId, context.AgentId);
        await using var lease = await rateLimiter.TryAcquireAsync(rateLimitKey, cancellationToken);
        if (lease is null)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.RateLimit}] Too many requests to this MCP server right now. Please try again shortly.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.Value.MaxCallDurationSeconds));

        JsonDocument content;
        try
        {
            content = await resiliencePolicy.ExecuteAsync(resource.McpServerId, isIdempotent: true, async ct =>
            {
                var client = await clientFactory.GetOrCreateAsync(resource.McpServerId, ct);
                return await client.ReadResourceAsync(resource.Uri, ct);
            }, timeoutCts.Token);
        }
        catch (McpCircuitOpenException)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.ServerUnavailable}] The MCP server is temporarily unavailable after repeated failures.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.Timeout}] The resource read did not complete within {options.Value.MaxCallDurationSeconds} seconds.");
        }
        catch (Exception)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.ConnectionFailure}] The MCP server could not be reached.");
        }

        // FR-051 — size-independent of any schema (a resource has no declared output schema);
        // Validate's own size check (research.md Decision 9) still applies against an
        // unconstrained "{}" schema.
        var sizeErrors = schemaValidator.Validate(JsonDocument.Parse("{}").RootElement, content.RootElement, options.Value.MaxResponseSizeBytes);
        if (sizeErrors.Count > 0)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.InvalidResponse}] {sizeErrors[0]}");
        }

        return AgentToolResult.Success(content);
    }
}
