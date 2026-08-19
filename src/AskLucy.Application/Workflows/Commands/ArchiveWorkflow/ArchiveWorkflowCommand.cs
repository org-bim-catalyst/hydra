using MediatR;

namespace AskLucy.Application.Workflows.Commands.ArchiveWorkflow;

public sealed record ArchiveWorkflowCommand(Guid Id) : IRequest<WorkflowDetailDto>;
