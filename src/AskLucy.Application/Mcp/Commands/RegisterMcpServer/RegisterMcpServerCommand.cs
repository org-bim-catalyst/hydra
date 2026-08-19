using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.RegisterMcpServer;

/// <summary>spec.md FR-001-FR-010 — Administrator-only (enforced by the controller's <c>AdministratorOrSuperUser</c> authorization policy). The server starts <c>IsEnabled: false</c> regardless of input.</summary>
public sealed record RegisterMcpServerCommand(
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
    int CapabilityRefreshIntervalMinutes) : IRequest<McpServerDto>;
