using MediatR;

namespace AskLucy.Application.Workflows.Commands.UpdateWorkflow;

/// <summary>
/// Draft-field edit (FR-001/FR-003/FR-009) — never touches published version history. A concurrent
/// stale write surfaces as a 409 via EF Core's <c>RowVersion</c> optimistic-concurrency token.
/// <see cref="EventTriggerConfigurationJson"/> (FR-063/FR-064) follows the same "<c>null</c> means
/// leave unchanged" convention <c>UpdateKnowledgeBaseDetailsCommand.Tags</c> already uses for an
/// optional replace-if-provided field — the designer's canvas-save flow always omits it, and the
/// event-trigger configuration UI passes an explicit (possibly empty-object) value to change it.
/// </summary>
public sealed record UpdateWorkflowCommand(Guid Id, string Name, string? Description, string DraftDefinitionJson, string? EventTriggerConfigurationJson = null)
    : IRequest<WorkflowDetailDto>;
