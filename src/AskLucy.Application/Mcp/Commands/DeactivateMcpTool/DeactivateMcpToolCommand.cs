using AskLucy.Application.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.DeactivateMcpTool;

/// <summary>spec.md FR-021 — an administrator revoking a previously-active tool; the tool remains
/// discoverable on the next capability refresh (re-review still starts from <c>PendingReview</c>
/// unless unchanged, per the carry-forward rule) but stops appearing in <c>IMcpToolRegistry.ActiveTools</c>
/// immediately.</summary>
public sealed record DeactivateMcpToolCommand(Guid McpServerId, Guid ToolId) : IRequest<McpToolDto>;
