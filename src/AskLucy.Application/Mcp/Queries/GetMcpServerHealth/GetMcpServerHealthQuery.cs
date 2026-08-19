using AskLucy.Application.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.GetMcpServerHealth;

/// <summary>spec.md FR-055/FR-056 — current health status, without waiting for the next scheduled check.</summary>
public sealed record GetMcpServerHealthQuery(Guid Id) : IRequest<McpServerHealthDto>;
