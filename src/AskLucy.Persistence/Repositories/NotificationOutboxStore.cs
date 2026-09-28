using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// specs/067 research R4. A claim is a keyset read of candidates followed by one conditional
/// <c>UPDATE</c> per row; an affected count of 1 means this worker won the row, so two dispatchers
/// never process the same event. The lease makes a crashed worker's rows claimable again.
/// </summary>
public sealed class NotificationOutboxStore(AskLucyDbContext dbContext, TimeProvider timeProvider) : INotificationOutboxStore
{
    public void Add(NotificationOutboxEvent outboxEvent) => dbContext.NotificationOutboxEvents.Add(outboxEvent);

    public async Task<IReadOnlyList<Guid>> ClaimBatchAsync(string workerId, DateTime leaseExpiresAtUtc, int batchSize, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var candidates = await Claimable(dbContext.NotificationOutboxEvents.AsNoTracking(), now)
            .OrderBy(e => e.NextAttemptAtUtc)
            .ThenBy(e => e.OccurredAtUtc)
            .Select(e => e.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var claimed = new List<Guid>(candidates.Count);
        foreach (var id in candidates)
        {
            // Re-checks the claimable predicate, so a row another worker took since the read is left alone.
            var affected = await Claimable(dbContext.NotificationOutboxEvents.Where(e => e.Id == id), now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, OutboxEventStatus.Processing)
                    .SetProperty(e => e.Attempts, e => e.Attempts + 1)
                    .SetProperty(e => e.LeaseOwner, workerId)
                    .SetProperty(e => e.LeaseExpiresAtUtc, leaseExpiresAtUtc), cancellationToken);

            if (affected == 1)
            {
                claimed.Add(id);
            }
        }

        return claimed;
    }

    public Task<NotificationOutboxEvent?> GetClaimedAsync(Guid id, string workerId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return dbContext.NotificationOutboxEvents.SingleOrDefaultAsync(
            e => e.Id == id
                && e.Status == OutboxEventStatus.Processing
                && e.LeaseOwner == workerId
                && e.LeaseExpiresAtUtc > now,
            cancellationToken);
    }

    /// <summary>
    /// A graceful shutdown hands this worker's rows straight back. It is not a failed attempt, so the
    /// attempt the claim counted is returned too.
    /// </summary>
    public Task<int> ReleaseClaimsAsync(string workerId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return dbContext.NotificationOutboxEvents
            .Where(e => e.Status == OutboxEventStatus.Processing && e.LeaseOwner == workerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, OutboxEventStatus.Pending)
                .SetProperty(e => e.Attempts, e => e.Attempts > 0 ? e.Attempts - 1 : 0)
                .SetProperty(e => e.LeaseOwner, (string?)null)
                .SetProperty(e => e.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(e => e.NextAttemptAtUtc, now), cancellationToken);
    }

    private static IQueryable<NotificationOutboxEvent> Claimable(IQueryable<NotificationOutboxEvent> source, DateTime now) =>
        source.Where(e =>
            (e.Status == OutboxEventStatus.Pending && e.NextAttemptAtUtc <= now)
            || (e.Status == OutboxEventStatus.Processing && e.LeaseExpiresAtUtc <= now));
}
