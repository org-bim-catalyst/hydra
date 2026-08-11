namespace AskLucy.Domain.Mcp;

/// <summary>
/// Thrown when an MCP server's configured endpoint fails SSRF validation (spec.md FR-050,
/// research.md Decision 8) — mapped to <c>422 Unprocessable Entity</c>, distinct from
/// <see cref="Common.DomainRuleViolationException"/>'s generic 400: the request is well-formed,
/// the destination is simply not allowed.
/// </summary>
public sealed class McpEndpointNotAllowedException(string endpoint, string reason)
    : Exception($"The endpoint '{endpoint}' is not allowed: {reason}. Set an explicit administrator override with a justification if this is intentional.")
{
    public string Endpoint { get; } = endpoint;
}
