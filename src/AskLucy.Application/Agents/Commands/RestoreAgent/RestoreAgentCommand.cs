using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.RestoreAgent;

public sealed record RestoreAgentCommand(Guid Id) : IRequest<AgentDetailDto>;
