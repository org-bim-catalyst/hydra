using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.StartWorkflowExecution;

/// <summary>
/// Starts a new execution (spec.md FR-047, contracts/workflows-api.md). Never finishes
/// synchronously — the caller gets a summary back immediately; the run continues in the
/// background via <see cref="Abstractions.IWorkflowExecutionRunner"/>.
/// </summary>
public sealed record StartWorkflowExecutionCommand(
    Guid WorkflowId, int? WorkflowVersionNumber, string InputsJson, WorkflowExecutionTriggerType TriggerType) : IRequest<WorkflowExecutionSummaryDto>;
