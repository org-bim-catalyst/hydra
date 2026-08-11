using System.Text.Json;

namespace AskLucy.Application.Abstractions;

/// <summary>Normalized tool metadata returned by <see cref="IMcpClient.ListToolsAsync"/> (spec.md FR-020) — no MCP SDK type crosses this boundary (constitution §3).</summary>
public sealed record McpDiscoveredTool(string Name, string? Title, string? Description, JsonElement InputSchema, JsonElement? OutputSchema);

public sealed record McpDiscoveredResource(string Uri, string? Name, string? Description, string? MimeType);

public sealed record McpDiscoveredPrompt(string Name, string? Description);

/// <summary>Exactly one of <see cref="Output"/> is meaningful when <see cref="IsError"/> is <see langword="false"/> — the caller (<c>McpToolAdapter</c>) still validates it against the tool's declared output schema regardless (FR-026).</summary>
public sealed record McpToolCallResult(bool IsError, JsonDocument? Output, string? ErrorSummary);

/// <summary>
/// The Agent Runtime's only way to speak to one connected MCP server (research.md Decision 2).
/// Application/Domain depend only on this interface — <c>Infrastructure</c> wraps the official
/// MCP C# SDK behind it. Every method already assumes the caller has separately applied
/// <c>IMcpEndpointValidator</c>/<c>IMcpRateLimiter</c>/call-duration bounding — this interface is
/// the protocol operation only, not the security/resilience wrapper (contracts/mcp-tool-adapter.md).
/// </summary>
public interface IMcpClient : IAsyncDisposable
{
    Task<IReadOnlyList<McpDiscoveredTool>> ListToolsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpDiscoveredResource>> ListResourcesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpDiscoveredPrompt>> ListPromptsAsync(CancellationToken cancellationToken = default);

    Task<McpToolCallResult> CallToolAsync(string toolName, JsonDocument input, CancellationToken cancellationToken = default);

    Task<JsonDocument> ReadResourceAsync(string uri, CancellationToken cancellationToken = default);

    Task<string> GetPromptAsync(string name, IReadOnlyDictionary<string, string>? arguments, CancellationToken cancellationToken = default);

    Task PingAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves/creates a connected <see cref="IMcpClient"/> for a given <see cref="Domain.Mcp.McpServer"/>
/// (research.md Decision 2). Implementations MUST re-run SSRF endpoint validation on every new
/// connection, not only at server registration (FR-050, contracts/mcp-security-model.md) — this
/// closes the DNS-rebinding gap where a hostname was safe at registration but resolves elsewhere now.
/// </summary>
public interface IMcpClientFactory
{
    Task<IMcpClient> GetOrCreateAsync(Guid mcpServerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// spec.md FR-047 — discards any cached connection for this server so the next
    /// <see cref="GetOrCreateAsync"/> call reconnects from scratch. Credential rotation does not
    /// change <see cref="Domain.Mcp.McpServer.ConfigurationVersion"/> (only <c>UpdateMcpServerCommand</c>
    /// does), so without this the connection cache — keyed only on that version — would keep
    /// reusing a connection opened with the old credential indefinitely. A call already in flight
    /// on the old connection is unaffected (it already holds its own <see cref="IMcpClient"/>
    /// reference) and completes/fails on its own terms.
    /// </summary>
    Task InvalidateConnectionAsync(Guid mcpServerId, CancellationToken cancellationToken = default);
}
