using AskLucy.Domain.Memory;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Detects contradictions/ambiguity between a newly classified memory candidate and the user's
/// existing active memories (spec.md FR-015, FR-016; research.md Decision 10). Vector-candidate
/// retrieval via <see cref="IMemoryVectorStore"/> narrows the comparison pool; a single
/// <see cref="IAIProvider"/> classification call judges each pooled memory as
/// <c>NoConflict</c>/<c>DirectContradiction</c>/<c>AmbiguousSupersedeOrSupplement</c>.
/// </summary>
public interface IMemoryConflictDetectionService
{
    /// <summary>
    /// Checks <paramref name="candidateMemory"/> (already persisted, not yet saved via
    /// <see cref="IUnitOfWork"/> by the caller) against the same user's existing active memories.
    /// A <see cref="MemoryConflictType.DirectContradiction"/> auto-merges into the existing memory
    /// (appending a <see cref="AskLucy.Domain.Memory.MemoryVersion"/>) and soft-deletes
    /// <paramref name="candidateMemory"/> rather than keeping both — the return value indicates
    /// whether this happened, so the caller knows not to treat <paramref name="candidateMemory"/>
    /// as a distinct, independently-usable memory going forward. An
    /// <see cref="MemoryConflictType.AmbiguousSupersedeOrSupplement"/> creates a
    /// <see cref="MemoryConflict"/> row and raises a notification instead — both memories remain,
    /// but the conflicted one is excluded from retrieval until resolved.
    /// </summary>
    /// <returns><c>true</c> if <paramref name="candidateMemory"/> was consumed by a direct-contradiction auto-merge.</returns>
    Task<bool> DetectAndResolveAsync(MemoryEntity candidateMemory, CancellationToken cancellationToken = default);
}
