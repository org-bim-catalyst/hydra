using AskLucy.Domain.Memory;

namespace AskLucy.Application.Abstractions;

public interface IMemoryNotificationRepository
{
    Task<MemoryNotification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryNotification>> GetByUserAsync(string userId, Guid? afterId, int pageSize, CancellationToken cancellationToken = default);

    void Add(MemoryNotification notification);

    /// <summary>spec.md FR-026, research.md Decision 19 — same reasoning as <see cref="IMemoryAuditLogRepository.AnonymizeUserAsync"/>.</summary>
    Task AnonymizeUserAsync(string userId, CancellationToken cancellationToken = default);
}
