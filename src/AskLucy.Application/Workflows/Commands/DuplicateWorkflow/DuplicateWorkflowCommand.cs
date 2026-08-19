using MediatR;

namespace AskLucy.Application.Workflows.Commands.DuplicateWorkflow;

/// <summary>Copies the current draft only, never version/execution history (spec.md User Story 3, FR-003).</summary>
public sealed record DuplicateWorkflowCommand(Guid Id) : IRequest<WorkflowDetailDto>;
