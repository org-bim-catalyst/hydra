using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.DeleteWorkflowPolicy;

public sealed class DeleteWorkflowPolicyCommandHandler(IWorkflowPolicyRepository policyRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<DeleteWorkflowPolicyCommand>
{
    public async Task Handle(DeleteWorkflowPolicyCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var policy = await policyRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow policy not found.");

        policy.SoftDelete(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
