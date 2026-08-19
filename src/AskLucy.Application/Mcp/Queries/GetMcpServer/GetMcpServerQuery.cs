using AskLucy.Application.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.GetMcpServer;

/// <summary>spec.md FR-045 — never includes credential material. Admin-only, enforced at the endpoint.</summary>
public sealed record GetMcpServerQuery(Guid Id) : IRequest<McpServerDto>;
