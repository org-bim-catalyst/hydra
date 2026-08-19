using MediatR;

namespace AskLucy.Application.Workflows.Commands.PublishWorkflowVersion;

/// <summary>Publishes an immutable snapshot of the current draft (spec.md FR-012-FR-016, contracts/workflows-api.md). Blocked (422) if any FR-016 validation rule fails (SC-009).</summary>
public sealed record PublishWorkflowVersionCommand(Guid WorkflowId, string? ChangeDescription) : IRequest<WorkflowVersionDto>;
