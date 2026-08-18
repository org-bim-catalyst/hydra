using MediatR;

namespace AskLucy.Application.Agents.Commands.DeleteAgentPolicy;

public sealed record DeleteAgentPolicyCommand(Guid Id) : IRequest;
