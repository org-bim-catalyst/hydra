using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory.Authorization;
using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Memory.Commands.ResolveMemoryConflict;

/// <summary>
/// spec.md FR-016, User Story 6 AC2/AC3 (clarified 2026-08-09 — resolved asynchronously via the
/// Memory Center, never blocking the live conversation turn that surfaced it). Beyond flipping
/// <see cref="MemoryConflict.ResolutionStatus"/> away from <see cref="MemoryConflictResolutionStatus.PendingUserConfirmation"/>
/// (which alone makes both memories eligible for retrieval again — <c>IMemoryRepository.GetActiveByIdsAsync</c>
/// excludes only *open* conflicts), the losing side of a one-sided resolution is discarded:
/// <see cref="AskLucy.Application.Memory.Commands.ResolveMemoryConflict.MemoryConflictResolution.KeepExisting"/>
/// discards the new candidate, <c>KeepNew</c> discards the prior memory, and <c>KeepBoth</c> discards neither.
/// </summary>
public sealed class ResolveMemoryConflictCommandHandler(
    IMemoryRepository memoryRepository, IMemoryConflictRepository conflictRepository,
    IMemoryAuditLogRepository auditLogRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<ResolveMemoryConflictCommand>
{
    public async Task Handle(ResolveMemoryConflictCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var memory = MemoryOwnershipGuard.EnsureOwnedBy(await memoryRepository.GetByIdAsync(request.MemoryId, cancellationToken), userId);

        var conflict = await conflictRepository.GetOpenByMemoryIdAsync(memory.Id, cancellationToken);
        if (conflict is null)
        {
            throw new MemoryConflictNotPendingException();
        }

        var resolutionStatus = request.Resolution switch
        {
            MemoryConflictResolution.KeepExisting => MemoryConflictResolutionStatus.ResolvedKeepExisting,
            MemoryConflictResolution.KeepNew => MemoryConflictResolutionStatus.ResolvedKeepNew,
            MemoryConflictResolution.KeepBoth => MemoryConflictResolutionStatus.ResolvedKeepBoth,
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unknown resolution."),
        };

        conflict.Resolve(resolutionStatus, userId);

        Guid? discardedMemoryId = null;
        if (request.Resolution == MemoryConflictResolution.KeepExisting && conflict.NewMemoryId is { } newMemoryId)
        {
            var newMemory = await memoryRepository.GetByIdAsync(newMemoryId, cancellationToken);
            newMemory?.SoftDelete(userId);
            discardedMemoryId = newMemoryId;
        }
        else if (request.Resolution == MemoryConflictResolution.KeepNew)
        {
            var existingMemory = await memoryRepository.GetByIdAsync(conflict.ExistingMemoryId, cancellationToken);
            existingMemory?.SoftDelete(userId);
            discardedMemoryId = conflict.ExistingMemoryId;
        }

        auditLogRepository.Add(MemoryAuditLog.Create(
            memory.Id, memory.UserId, userId, MemoryAuditAction.ConflictResolved,
            JsonSerializer.Serialize(new { conflictId = conflict.Id, resolution = request.Resolution.ToString(), discardedMemoryId }),
            userId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
