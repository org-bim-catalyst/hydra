using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>Preferences are sparse: a missing row means the catalogue default applies (FR-031).</summary>
public sealed class NotificationPreferenceRepository(AskLucyDbContext dbContext) : INotificationPreferenceRepository
{
    public async Task<IReadOnlyList<PreferenceOverride>> GetOverridesAsync(string userId, CancellationToken cancellationToken) =>
        await dbContext.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new PreferenceOverride(p.Category, p.Channel, p.IsEnabled))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<PreferenceOverride>>> GetOverridesForUsersAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<PreferenceOverride>>(StringComparer.Ordinal);
        }

        var rows = await dbContext.NotificationPreferences
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.Category, p.Channel, p.IsEnabled })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.UserId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PreferenceOverride>)[.. g.Select(r => new PreferenceOverride(r.Category, r.Channel, r.IsEnabled))],
                StringComparer.Ordinal);
    }
}
