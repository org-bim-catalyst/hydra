using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class SystemAnnouncementRepository(AskLucyDbContext dbContext) : ISystemAnnouncementRepository
{
    public void Add(SystemAnnouncement announcement) => dbContext.SystemAnnouncements.Add(announcement);

    public Task<SystemAnnouncement?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.SystemAnnouncements.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlySet<string>> GetExistingRoleIdsAsync(IReadOnlyCollection<string> roleIds, CancellationToken cancellationToken) =>
        (await dbContext.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

    public Task<int> CountNotificationsAsync(Guid announcementId, CancellationToken cancellationToken)
    {
        var id = announcementId.ToString();
        return dbContext.Notifications.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(n => n.RelatedItemType == AnnouncementKeys.RelatedItemType && n.RelatedItemId == id, cancellationToken);
    }
}
