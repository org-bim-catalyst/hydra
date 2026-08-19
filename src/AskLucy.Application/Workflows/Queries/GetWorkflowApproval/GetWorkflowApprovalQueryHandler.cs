using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowApproval;

public sealed class GetWorkflowApprovalQueryHandler(IWorkflowExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetWorkflowApprovalQuery, WorkflowApprovalDto>
{
    public async Task<WorkflowApprovalDto> Handle(GetWorkflowApprovalQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = WorkflowExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.WorkflowExecutionId, userId, cancellationToken), userId);

        var approval = execution.Approvals.FirstOrDefault(a => a.Id == request.ApprovalId)
            ?? throw new KeyNotFoundException("Approval not found.");

        return WorkflowApprovalDto.Create(approval);
    }
}
