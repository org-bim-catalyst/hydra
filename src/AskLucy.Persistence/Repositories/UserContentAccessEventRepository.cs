using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// specs/074 FR-016c. Saves immediately: the event is written synchronously before the content is
/// loaded (research D15), so an unrecorded view is impossible. There is deliberately no delete.
/// </summary>
public sealed class UserContentAccessEventRepository(AskLucyDbContext dbContext) : IUserContentAccessEventRepository
{
    public async Task AddAsync(UserContentAccessEvent accessEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessEvent);

        dbContext.UserContentAccessEvents.Add(accessEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The owner's erasure (FR-029a): who viewed what stays, whose and which item goes.</summary>
    public Task AnonymizeOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);

        return dbContext.UserContentAccessEvents
            .Where(e => e.OwnerUserId == ownerUserId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.OwnerUserId, (string?)null)
                    .SetProperty(e => e.ItemId, (Guid?)null)
                    .SetProperty(e => e.IsOwnerErased, true),
                cancellationToken);
    }
}
