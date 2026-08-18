using MediatR;

namespace AskLucy.Application.Agents.Commands.CancelAgentExecution;

public sealed record CancelAgentExecutionCommand(Guid ExecutionId) : IRequest;
