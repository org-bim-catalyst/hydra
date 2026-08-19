using MediatR;

namespace AskLucy.Application.Mcp.Commands.EnableMcpServer;

/// <summary>spec.md FR-003.</summary>
public sealed record EnableMcpServerCommand(Guid Id) : IRequest<McpServerDto>;
