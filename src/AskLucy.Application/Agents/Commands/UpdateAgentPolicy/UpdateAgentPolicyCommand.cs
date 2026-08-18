using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.UpdateAgentPolicy;

public sealed record UpdateAgentPolicyCommand(Guid Id, string Name, string? Description, string? ConditionsJson, bool IsEnabled) : IRequest<AgentPolicyDto>;
