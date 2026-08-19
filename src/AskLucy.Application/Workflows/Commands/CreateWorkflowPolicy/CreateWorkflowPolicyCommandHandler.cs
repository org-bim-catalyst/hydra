using AskLucy.Application.Abstractions;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.CreateWorkflowPolicy;

public sealed class CreateWorkflowPolicyCommandHandler(IWorkflowPolicyRepository policyRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<CreateWorkflowPolicyCommand, WorkflowPolicyDto>
{
    public async Task<WorkflowPolicyDto> Handle(CreateWorkflowPolicyCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var policy = WorkflowPolicy.Create(request.Name, request.Description, request.WorkflowNodeType, request.UnderlyingToolName, request.ConditionsJson, userId);
        policyRepository.Add(policy);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowPolicyDto.Create(policy);
    }
}
