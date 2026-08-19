using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using AskLucy.Application.Workflows.Validation;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.ValidateWorkflow;

public sealed class ValidateWorkflowCommandHandler(
    IWorkflowRepository workflowRepository, WorkflowGraphValidator validator, ICurrentUserAccessor currentUser)
    : IRequestHandler<ValidateWorkflowCommand, IReadOnlyList<WorkflowValidationIssueDto>>
{
    public async Task<IReadOnlyList<WorkflowValidationIssueDto>> Handle(ValidateWorkflowCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        if (!WorkflowDraftDefinition.TryParse(workflow.DraftDefinitionJson, out var draft, out var parseError))
        {
            return [new WorkflowValidationIssueDto(null, parseError!)];
        }

        return validator.Validate(draft!).Select(i => new WorkflowValidationIssueDto(i.NodeKey, i.Message)).ToList();
    }
}
