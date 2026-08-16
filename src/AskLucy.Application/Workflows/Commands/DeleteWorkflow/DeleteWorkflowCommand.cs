using MediatR;

namespace AskLucy.Application.Workflows.Commands.DeleteWorkflow;

/// <summary>Soft delete only — never cascades to versions/executions (data-model.md Delete Behavior; spec.md FR-052 audit trail requires they survive).</summary>
public sealed record DeleteWorkflowCommand(Guid Id) : IRequest;
