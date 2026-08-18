using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory.Authorization;
using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Memory.Commands.DeleteMemory;

/// <summary>spec.md FR-020, User Story 2 AC3 — soft-deletes; the standard <c>DeletedAtUtc</c> query filter (constitution §5) immediately excludes it from every future retrieval/ranking query, no separate "deactivate" step needed.</summary>
public sealed class DeleteMemoryCommandHandler(
    IMemoryRepository memoryRepository, IMemoryAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser) : IRequestHandler<DeleteMemoryCommand>
{
    public async Task Handle(DeleteMemoryCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var memory = MemoryOwnershipGuard.EnsureOwnedBy(await memoryRepository.GetByIdAsync(request.MemoryId, cancellationToken), userId);

        memory.SoftDelete(userId);
        auditLogRepository.Add(MemoryAuditLog.Create(memory.Id, memory.UserId, userId, MemoryAuditAction.Deleted, null, userId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
