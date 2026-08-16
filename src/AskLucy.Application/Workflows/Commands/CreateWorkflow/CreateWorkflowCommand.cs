using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.CreateWorkflow;

/// <summary>
/// Creates a new <see cref="Workflow"/> in Draft status (spec.md FR-001, contracts/workflows-api.md).
/// <see cref="EventTriggerConfigurationJson"/> (FR-063/FR-064) is only meaningful when
/// <paramref name="WorkflowType"/> is <see cref="Workflows.WorkflowType.EventDriven"/> — the handler
/// enforces this via <see cref="Workflow.SetEventTriggerConfiguration"/>, same as
/// <c>UpdateWorkflowCommand</c>.
/// </summary>
public sealed record CreateWorkflowCommand(string Name, string? Description, WorkflowType WorkflowType, string? EventTriggerConfigurationJson = null)
    : IRequest<WorkflowDetailDto>;
