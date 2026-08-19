using AskLucy.Domain.Agents;
using AskLucy.Domain.Mcp;

namespace AskLucy.Web.Contracts;

public sealed record RegisterMcpServerRequest(
    string Name,
    string? Description,
    string Endpoint,
    McpServerTransport Transport,
    McpAuthenticationType AuthenticationType,
    string? Credential,
    bool RequiresUnauthenticatedConfirmation,
    bool AllowInsecureTransport,
    string? InsecureTransportJustification,
    bool EndpointValidationOverride,
    string? EndpointValidationJustification,
    int CapabilityRefreshIntervalMinutes);

public sealed record UpdateMcpServerRequest(
    string Name,
    string? Description,
    string Endpoint,
    McpServerTransport Transport,
    McpAuthenticationType AuthenticationType,
    bool RequiresUnauthenticatedConfirmation,
    bool AllowInsecureTransport,
    string? InsecureTransportJustification,
    bool EndpointValidationOverride,
    string? EndpointValidationJustification,
    int CapabilityRefreshIntervalMinutes);

public sealed record ActivateMcpToolRequest(AgentToolRiskLevel? EffectiveRiskLevelOverride, string? RequiredPermissionsJsonOverride);

public sealed record RotateMcpServerCredentialRequest(string Credential);
