using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowApproval;

public sealed record GetWorkflowApprovalQuery(Guid WorkflowExecutionId, Guid ApprovalId) : IRequest<WorkflowApprovalDto>;
