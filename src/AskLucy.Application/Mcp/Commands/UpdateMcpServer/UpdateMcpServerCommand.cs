using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.UpdateMcpServer;

/// <summary>spec.md FR-007/FR-049 — increments <see cref="McpServer.ConfigurationVersion"/>.</summary>
public sealed record UpdateMcpServerCommand(
    Guid Id,
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
    int CapabilityRefreshIntervalMinutes) : IRequest<McpServerDto>;
