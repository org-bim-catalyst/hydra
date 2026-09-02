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
/// Adapts one <see cref="McpTool"/> into the existing <c>IAgentTool</c> contract (research.md
/// Decision 1, contracts/mcp-tool-adapter.md) — one instance per discovered, currently-active MCP
/// tool, constructed by <see cref="IMcpToolRegistry"/>. Every runtime step
/// contracts/agent-tool-contract.md already documents (input validation, permission check,
/// approval gate, output validation, duplicate-call detection) applies unchanged; this class only
/// adds what native tools never needed: rate limiting, connection acquisition, and a
/// defense-in-depth output re-check across a genuine trust boundary.
/// </summary>
public sealed class McpToolAdapter(
    McpTool tool,
    string serverName,
    IMcpClientFactory clientFactory,
    IMcpRateLimiter rateLimiter,
    IJsonSchemaValidator schemaValidator,
    McpConnectionResiliencePolicy resiliencePolicy,
    IOptions<McpRuntimeOptions> options) : IAgentTool
{
    public string Name => tool.NamespacedName;

    /// <summary>FR-029 — embeds the source MCP server's name so an approval request naming this tool already shows the "target MCP server" without any <c>AgentExecutionOrchestrator</c> change (research.md's "Agent Runtime remains MCP-agnostic").</summary>
    public string Description => $"{tool.Description} (MCP server: {serverName})";

    public AgentToolRiskLevel RiskLevel => tool.EffectiveRiskLevel;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions { get; } = ParsePermissions(tool.RequiredPermissionsJson);

    public string InputSchemaJson => tool.InputSchemaJson;

    public string OutputSchemaJson => tool.OutputSchemaJson;

    /// <summary>
    /// research.md Decision 17 / data-model.md's <c>McpAuditLog</c> non-duplication note — an
    /// ordinary MCP-side call failure is recorded only here, in the same <see
    /// cref="AgentToolResult.FailureReason"/> text the existing <c>AgentToolCall.FailureReason</c>
    /// column already stores for every tool (FR-032's "normalized... rather than a distinct
    /// MCP-specific failure path"); it is never additionally written to <c>McpAuditLog</c>, which
    /// data-model.md scopes to administrative/security events and explicitly does not duplicate
    /// per-execution tool-call activity. The bracketed <see cref="McpFailureCategory"/> prefix is
    /// what satisfies FR-033's "record a failure category for every failed MCP interaction"
    /// without a schema change to the existing, unmodified <c>AgentToolCall</c> entity.
    /// </summary>
    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        var rateLimitKey = new McpRateLimitKey(tool.McpServerId, tool.NamespacedName, context.UserId, context.AgentId);
        await using var lease = await rateLimiter.TryAcquireAsync(rateLimitKey, cancellationToken);
        if (lease is null)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.RateLimit}] Too many requests to this MCP tool right now. Please try again shortly.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.Value.MaxCallDurationSeconds));

        McpToolCallResult callResult;
        try
        {
            callResult = await resiliencePolicy.ExecuteAsync(tool.McpServerId, isIdempotent: false, async ct =>
            {
                var client = await clientFactory.GetOrCreateAsync(tool.McpServerId, ct);
                return await client.CallToolAsync(tool.ToolName, input, ct);
            }, timeoutCts.Token);
        }
        catch (McpCircuitOpenException)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.ServerUnavailable}] The MCP server is temporarily unavailable after repeated failures.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.Timeout}] The MCP tool call did not complete within {options.Value.MaxCallDurationSeconds} seconds.");
        }
        catch (UnauthorizedAccessException)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.AuthenticationFailure}] Authentication with the MCP server failed.");
        }
        catch (Exception)
        {
            // FR-046/FR-059 — never the raw exception message, which could contain credential
            // material or an internal connection detail (same posture as
            // TestMcpServerConnectionCommandHandler's catch).
            return AgentToolResult.Failure($"[{McpFailureCategory.ConnectionFailure}] The MCP server could not be reached.");
        }

        if (callResult.IsError)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.ServerError}] {callResult.ErrorSummary ?? "The MCP tool reported an error."}");
        }

        var output = callResult.Output ?? JsonDocument.Parse("{}");
        var outputSchema = JsonDocument.Parse(tool.OutputSchemaJson).RootElement;
        var validationErrors = schemaValidator.Validate(outputSchema, output.RootElement, options.Value.MaxResponseSizeBytes);
        if (validationErrors.Count > 0)
        {
            return AgentToolResult.Failure($"[{McpFailureCategory.InvalidResponse}] The MCP tool's response did not match its declared output schema: {validationErrors[0]}");
        }

        return AgentToolResult.Success(output);
    }

    private static List<AgentToolPermission> ParsePermissions(string requiredPermissionsJson)
    {
        using var document = JsonDocument.Parse(requiredPermissionsJson);
        var permissions = new List<AgentToolPermission>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.GetString() is { } name && Enum.TryParse<AgentToolPermission>(name, out var permission))
            {
                permissions.Add(permission);
            }
        }

        return permissions;
    }
}
