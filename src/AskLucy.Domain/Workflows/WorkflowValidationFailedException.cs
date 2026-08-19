namespace AskLucy.Domain.Workflows;

public sealed record WorkflowValidationFailure(string? NodeKey, string Message);

/// <summary>
/// Thrown by <see cref="Application.Workflows.Commands.PublishWorkflowVersion.PublishWorkflowVersionCommandHandler"/>
/// when <c>WorkflowGraphValidator</c> reports any FR-016 violation — maps to 422 with every
/// violation surfaced as a machine-readable extension (SC-009: 0 workflows publish with a
/// critical validation error present).
/// </summary>
public sealed class WorkflowValidationFailedException(IReadOnlyList<WorkflowValidationFailure> violations)
    : Exception("The workflow failed validation and cannot be published.")
{
    public IReadOnlyList<WorkflowValidationFailure> Violations { get; } = violations;
}
