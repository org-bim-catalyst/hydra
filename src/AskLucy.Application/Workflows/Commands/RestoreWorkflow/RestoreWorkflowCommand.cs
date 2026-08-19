using MediatR;

namespace AskLucy.Application.Workflows.Commands.RestoreWorkflow;

public sealed record RestoreWorkflowCommand(Guid Id) : IRequest<WorkflowDetailDto>;
