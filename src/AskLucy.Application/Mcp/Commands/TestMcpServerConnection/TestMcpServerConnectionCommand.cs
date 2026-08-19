using MediatR;

namespace AskLucy.Application.Mcp.Commands.TestMcpServerConnection;

/// <summary>spec.md FR-008 — on-demand, independent of the scheduled health-check cycle. Calls the same Application service <c>McpServerHealthCheckJob</c> (US6) calls, not a duplicate code path (research.md Decision 10).</summary>
public sealed record TestMcpServerConnectionCommand(Guid Id) : IRequest<McpServerHealthDto>;
