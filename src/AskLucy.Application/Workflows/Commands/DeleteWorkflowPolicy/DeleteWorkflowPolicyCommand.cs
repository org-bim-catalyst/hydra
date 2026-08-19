using MediatR;

namespace AskLucy.Application.Workflows.Commands.DeleteWorkflowPolicy;

public sealed record DeleteWorkflowPolicyCommand(Guid Id) : IRequest;
