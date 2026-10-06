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

    public async Task<NotificationRecipientInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        // Identity stores the upper-cased address in NormalizedEmail (its default normalizer); the lookup
        // matches how sign-in finds the account. An account that isn't deleted wins over one that is.
        var normalized = email.Trim().ToUpperInvariant();
        var user = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized)
            .OrderBy(u => u.IsDeleted)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.EmailConfirmed, u.IsDeleted })
            .FirstOrDefaultAsync(cancellationToken);

        return user is null
            ? null
            : new NotificationRecipientInfo(user.Id, DisplayName(user.FirstName, user.LastName), user.Email, user.EmailConfirmed, IsActive: !user.IsDeleted);
    }

    public async Task<IReadOnlyList<string>> GetActiveUserIdsAfterAsync(
        IReadOnlyCollection<string>? roleIds, string? afterUserId, int take, CancellationToken cancellationToken)
    {
        var users = ActiveUsers(roleIds, verifiedEmailOnly: false);
        if (afterUserId is not null)
        {
            users = users.Where(u => u.Id.CompareTo(afterUserId) > 0);
        }

        return await users.OrderBy(u => u.Id).Select(u => u.Id).Take(take).ToListAsync(cancellationToken);
    }

    public Task<int> CountActiveAsync(IReadOnlyCollection<string>? roleIds, bool verifiedEmailOnly, CancellationToken cancellationToken) =>
        ActiveUsers(roleIds, verifiedEmailOnly).CountAsync(cancellationToken);

    private IQueryable<AskLucy.Persistence.Identity.ApplicationUser> ActiveUsers(IReadOnlyCollection<string>? roleIds, bool verifiedEmailOnly)
    {
        var users = dbContext.Users.AsNoTracking().Where(u => !u.IsDeleted);
        if (verifiedEmailOnly)
        {
            users = users.Where(u => u.EmailConfirmed && u.Email != null);
        }

        return roleIds is { Count: > 0 }
            ? users.Where(u => dbContext.UserRoles.Any(ur => ur.UserId == u.Id && roleIds.Contains(ur.RoleId)))
            : users;
    }

    private static string? DisplayName(string? firstName, string? lastName)
    {
        var name = $"{firstName} {lastName}".Trim();
        return name.Length == 0 ? null : name;
    }
}
