using MediatR;

namespace AskLucy.Application.Workflows.Commands.DeprecateWorkflow;

/// <summary>FR-002 — a one-way lifecycle stage: no new manual or event-triggered executions start; already-running executions are unaffected.</summary>
public sealed record DeprecateWorkflowCommand(Guid Id) : IRequest<WorkflowDetailDto>;
