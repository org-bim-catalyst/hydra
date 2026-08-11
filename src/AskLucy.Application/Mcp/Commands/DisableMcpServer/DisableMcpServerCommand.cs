using MediatR;

namespace AskLucy.Application.Mcp.Commands.DisableMcpServer;

/// <summary>spec.md FR-004 — immediately makes every tool/resource/prompt from this server unavailable to every agent.</summary>
public sealed record DisableMcpServerCommand(Guid Id) : IRequest<McpServerDto>;
