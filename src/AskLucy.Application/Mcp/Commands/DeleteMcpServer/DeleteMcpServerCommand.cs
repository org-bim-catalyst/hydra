using MediatR;

namespace AskLucy.Application.Mcp.Commands.DeleteMcpServer;

/// <summary>spec.md FR-005 — blocked while any agent still references this server's tools (research.md Decision 15).</summary>
public sealed record DeleteMcpServerCommand(Guid Id) : IRequest;
