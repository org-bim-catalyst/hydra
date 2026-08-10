using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.ArchiveAgent;

public sealed record ArchiveAgentCommand(Guid Id) : IRequest<AgentDetailDto>;
