namespace AskLucy.Application.Abstractions;

public enum McpEndpointValidationResult
{
    Allowed,
    RejectedPrivateOrLoopback,
    RejectedLinkLocalOrCloudMetadata,
    RejectedUnresolvable,
    RejectedInsecureScheme,
}

/// <summary>
/// SSRF protection for remote MCP server endpoints (spec.md FR-050, research.md Decision 8,
/// contracts/mcp-security-model.md). Built from scratch — no prior utility of this kind exists
/// elsewhere in the codebase. Called at registration time <em>and</em> again before every
/// connection (<c>IMcpClientFactory</c>) to close the DNS-rebinding gap.
/// </summary>
public interface IMcpEndpointValidator
{
    /// <summary>Resolves the endpoint's host via DNS and checks every resolved address against private/loopback/link-local/cloud-metadata ranges. <paramref name="allowOverride"/> (an administrator's explicit, justified override) bypasses the range check but never the "must resolve" check.</summary>
    Task<McpEndpointValidationResult> ValidateAsync(string endpoint, bool allowOverride, CancellationToken cancellationToken = default);
}
