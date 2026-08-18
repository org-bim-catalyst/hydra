using AskLucy.Domain.Memory;

namespace AskLucy.Application.Abstractions;

public interface IMemoryAuditLogRepository
{
    void Add(MemoryAuditLog entry);

    /// <summary>spec.md FR-026, research.md Decision 19 — anonymizes every row's <see cref="MemoryAuditLog.UserId"/> for a hard-deleted account; the rows themselves survive (deliberately no FK/cascade to <c>ApplicationUser</c>, see the entity's doc comment) since the audit trail's existence is the point.</summary>
    Task AnonymizeUserAsync(string userId, CancellationToken cancellationToken = default);
}
