using AskLucy.Application.Abstractions;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.SetWorkflowUserExecutionLimit;

public sealed class SetWorkflowUserExecutionLimitCommandHandler(IWorkflowPolicyRepository policyRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<SetWorkflowUserExecutionLimitCommand, WorkflowUserExecutionLimitDto>
{
    public async Task<WorkflowUserExecutionLimitDto> Handle(SetWorkflowUserExecutionLimitCommand request, CancellationToken cancellationToken)
    {
        var adminUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var existing = await policyRepository.GetUserExecutionLimitAsync(request.UserId, cancellationToken);

        if (existing is null)
        {
            var limit = WorkflowUserExecutionLimit.Create(request.UserId, request.MaxConcurrentExecutions, adminUserId);
            policyRepository.AddUserExecutionLimit(limit);
        }
        else
        {
            existing.Update(request.MaxConcurrentExecutions, adminUserId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new WorkflowUserExecutionLimitDto(request.UserId, request.MaxConcurrentExecutions);
    }
}
