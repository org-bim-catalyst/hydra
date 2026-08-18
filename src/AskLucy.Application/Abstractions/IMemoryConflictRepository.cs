using AskLucy.Domain.Memory;

namespace AskLucy.Application.Abstractions;

public interface IMemoryConflictRepository
{
    Task<MemoryConflict?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The open (<see cref="MemoryConflictResolutionStatus.PendingUserConfirmation"/>) conflict for a memory, on either side (<c>ExistingMemoryId</c> or <c>NewMemoryId</c>) — at most one per memory (research.md Decision 10).</summary>
    Task<MemoryConflict?> GetOpenByMemoryIdAsync(Guid memoryId, CancellationToken cancellationToken = default);

    void Add(MemoryConflict conflict);
}
