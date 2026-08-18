using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory.Authorization;
using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Memory.Commands.ApproveMemory;

public sealed class ApproveMemoryCommandHandler(
    IMemoryRepository memoryRepository, IMemoryApprovalRepository approvalRepository,
    IMemoryAuditLogRepository auditLogRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<ApproveMemoryCommand>
{
    public async Task Handle(ApproveMemoryCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var memory = MemoryOwnershipGuard.EnsureOwnedBy(await memoryRepository.GetByIdAsync(request.MemoryId, cancellationToken), userId);

        if (memory.State != MemoryLifecycleState.PendingApproval)
        {
            throw new MemoryNotPendingApprovalException();
        }

        memory.Approve(userId);

        var approval = await approvalRepository.GetByMemoryIdAsync(memory.Id, cancellationToken);
        if (approval is null)
        {
            approvalRepository.Add(MemoryApproval.CreateDecided(memory.Id, MemoryApprovalDecision.Approved, userId));
        }
        else
        {
            approval.Approve(userId);
        }

        auditLogRepository.Add(MemoryAuditLog.Create(memory.Id, memory.UserId, userId, MemoryAuditAction.Approved, null, userId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
