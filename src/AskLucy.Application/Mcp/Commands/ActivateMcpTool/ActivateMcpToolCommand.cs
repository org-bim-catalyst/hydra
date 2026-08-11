using AskLucy.Application.Mcp;
using AskLucy.Domain.Agents;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.ActivateMcpTool;

/// <summary>spec.md FR-021/FR-022 — an administrator's explicit review gate; moves a <c>PendingReview</c>
/// or previously <c>Deactivated</c> tool to <c>Active</c>, optionally overriding the risk level and/or
/// required permissions the server itself declared (which are advisory only).</summary>
public sealed record ActivateMcpToolCommand(Guid McpServerId, Guid ToolId, AgentToolRiskLevel? EffectiveRiskLevelOverride, string? RequiredPermissionsJsonOverride) : IRequest<McpToolDto>;
