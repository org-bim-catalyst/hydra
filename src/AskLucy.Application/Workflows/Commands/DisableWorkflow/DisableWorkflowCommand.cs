using MediatR;

namespace AskLucy.Application.Workflows.Commands.DisableWorkflow;

/// <summary>FR-002 — stops event-trigger dispatch (Acceptance Scenario 9.3) without discarding the published definition; reversible.</summary>
public sealed record DisableWorkflowCommand(Guid Id) : IRequest<WorkflowDetailDto>;
