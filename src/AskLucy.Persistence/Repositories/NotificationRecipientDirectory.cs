using AskLucy.Application.Notifications.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// Reads the few account facts routing needs. An id with no row is left out of the result, which
/// the processor reports as an unknown recipient; a soft-deleted account comes back inactive (R24).
/// </summary>
public sealed class NotificationRecipientDirectory(AskLucyDbContext dbContext) : INotificationRecipientDirectory
{
    public async Task<IReadOnlyDictionary<string, NotificationRecipientInfo>> GetAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, NotificationRecipientInfo>(StringComparer.Ordinal);
        }

        var users = await dbContext.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.EmailConfirmed, u.IsDeleted })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u => new NotificationRecipientInfo(
                u.Id,
                DisplayName(u.FirstName, u.LastName),
                u.Email,
                u.EmailConfirmed,
                IsActive: !u.IsDeleted),
            StringComparer.Ordinal);
    }

    private static string? DisplayName(string? firstName, string? lastName)
    {
        var name = $"{firstName} {lastName}".Trim();
        return name.Length == 0 ? null : name;
    }
}
