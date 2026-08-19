using MediatR;

namespace AskLucy.Application.Workflows.Commands.ValidateWorkflow;

/// <summary>Runs every FR-016 publish-blocking rule against the current draft without publishing — backs the Designer's live validation panel (FR-011) and is re-run automatically before Publish.</summary>
public sealed record ValidateWorkflowCommand(Guid Id) : IRequest<IReadOnlyList<WorkflowValidationIssueDto>>;
