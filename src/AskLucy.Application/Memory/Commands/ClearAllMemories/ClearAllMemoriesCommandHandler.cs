using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Memory.Commands.ClearAllMemories;

/// <summary>
/// spec.md FR-023, User Story 4 AC2. Soft-deletes every one of the caller's memories
/// synchronously — the standard <c>DeletedAtUtc</c> query filter (constitution §5) means this is
/// already the full user-visible effect (excluded from all future retrieval/ranking) by the time
/// this handler returns, satisfying contracts/memory-privacy-api.md's "guaranteed immediate at
/// the point of response" framing even though the controller still answers <c>202 Accepted</c>.
/// </summary>
public sealed class ClearAllMemoriesCommandHandler(
    IMemoryRepository memoryRepository, IMemoryAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser) : IRequestHandler<ClearAllMemoriesCommand>
{
    public async Task Handle(ClearAllMemoriesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var memories = await memoryRepository.GetAllByUserAsync(userId, cancellationToken);

        foreach (var memory in memories)
        {
            memory.SoftDelete(userId);
            auditLogRepository.Add(MemoryAuditLog.Create(memory.Id, memory.UserId, userId, MemoryAuditAction.Deleted, null, userId));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
