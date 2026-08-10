using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Agents.Commands.DeleteAgentPolicy;

public sealed class DeleteAgentPolicyCommandHandler(IAgentPolicyRepository policyRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<DeleteAgentPolicyCommand>
{
    public async Task Handle(DeleteAgentPolicyCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var policy = await policyRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Agent policy not found.");

        policy.SoftDelete(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
