using AskLucy.Domain.Common;

namespace AskLucy.Domain.Memory;

/// <summary>
/// Usage trace — which memories were used to produce a given assistant response (spec.md FR-014;
/// research.md Decision 16). Structurally parallel to <c>Chats.Citation</c>'s role for RAG.
/// <see cref="ContentSnapshot"/> preserves the memory's text *as it was when used*, so the trace
/// stays meaningful even if the memory is later edited/archived/deleted — no cascade delete.
/// </summary>
public sealed class MemoryReference : BaseEntity
{
    public Guid MessageId { get; private set; }

    public Guid MemoryId { get; private set; }

    public decimal RelevanceScore { get; private set; }

    /// <summary>Encrypted at rest — same converter as <see cref="Memory.Content"/> (research.md Decision 12).</summary>
    public string ContentSnapshot { get; private set; } = string.Empty;

    private MemoryReference()
    {
        // Required by EF Core materialization.
    }

    public static MemoryReference Create(Guid messageId, Guid memoryId, decimal relevanceScore, string contentSnapshot, string actor) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            MessageId = messageId,
            MemoryId = memoryId,
            RelevanceScore = relevanceScore,
            ContentSnapshot = contentSnapshot,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
}
