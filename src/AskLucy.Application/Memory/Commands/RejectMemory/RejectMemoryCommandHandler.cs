using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory.Authorization;
using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Memory.Commands.RejectMemory;

/// <summary>spec.md FR-021, User Story 3 AC3 — a rejected candidate is discarded (soft-deleted by <c>Memory.Reject</c>), never used.</summary>
public sealed class RejectMemoryCommandHandler(
    IMemoryRepository memoryRepository, IMemoryApprovalRepository approvalRepository,
    IMemoryAuditLogRepository auditLogRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<RejectMemoryCommand>
{
    public async Task Handle(RejectMemoryCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var memory = MemoryOwnershipGuard.EnsureOwnedBy(await memoryRepository.GetByIdAsync(request.MemoryId, cancellationToken), userId);

        if (memory.State != MemoryLifecycleState.PendingApproval)
        {
            throw new MemoryNotPendingApprovalException();
        }

        memory.Reject(userId);

        var approval = await approvalRepository.GetByMemoryIdAsync(memory.Id, cancellationToken);
        if (approval is null)
        {
            approvalRepository.Add(MemoryApproval.CreateDecided(memory.Id, MemoryApprovalDecision.Rejected, userId));
        }
        else
        {
            approval.Reject(userId);
        }

        auditLogRepository.Add(MemoryAuditLog.Create(memory.Id, memory.UserId, userId, MemoryAuditAction.Rejected, null, userId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
