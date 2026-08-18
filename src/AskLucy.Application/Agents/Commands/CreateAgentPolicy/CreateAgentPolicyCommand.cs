using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.CreateAgentPolicy;

/// <summary>Administrator-managed auto-approval rule (spec.md FR-025/FR-026) — Administrator/Super User only (research.md Decision 1), enforced by the controller's <c>AdministratorOrSuperUser</c> authorization policy.</summary>
public sealed record CreateAgentPolicyCommand(string Name, string? Description, string ToolName, string? ConditionsJson) : IRequest<AgentPolicyDto>;
