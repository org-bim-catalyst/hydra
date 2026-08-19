using MediatR;

namespace AskLucy.Application.Workflows.Commands.EnableWorkflow;

/// <summary>Reverses <c>DisableWorkflowCommand</c> — not itself named as a task in tasks.md, but <see cref="Domain.Workflows.Workflow.Enable"/>'s own doc comment names this as Disable's designed reversal, so it gets the same thin command+endpoint rather than being a dead-end domain method.</summary>
public sealed record EnableWorkflowCommand(Guid Id) : IRequest<WorkflowDetailDto>;
