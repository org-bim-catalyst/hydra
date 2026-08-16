using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.UpdateWorkflowPolicy;

public sealed class UpdateWorkflowPolicyCommandHandler(IWorkflowPolicyRepository policyRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<UpdateWorkflowPolicyCommand, WorkflowPolicyDto>
{
    public async Task<WorkflowPolicyDto> Handle(UpdateWorkflowPolicyCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var policy = await policyRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow policy not found.");

        policy.Update(request.Name, request.Description, request.ConditionsJson, userId);
        policy.SetEnabled(request.IsEnabled, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowPolicyDto.Create(policy);
    }
}
