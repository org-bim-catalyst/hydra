using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Memory.Authorization;

/// <summary>Centralizes the "does this memory belong to this caller" check (spec.md FR-027), mirrors <c>ChatOwnershipGuard</c>. Denial looks like not-found — a request naming a memory the caller doesn't own returns 404, never 403, avoiding existence disclosure.</summary>
public static class MemoryOwnershipGuard
{
    public static MemoryEntity EnsureOwnedBy(MemoryEntity? memory, string userId)
    {
        if (memory is null || !memory.IsOwnedBy(userId))
        {
            throw new KeyNotFoundException("Memory not found.");
        }

        return memory;
    }
}
