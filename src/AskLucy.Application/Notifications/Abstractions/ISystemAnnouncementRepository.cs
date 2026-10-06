using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>Persistence for <see cref="SystemAnnouncement"/> (FR-004a). Announcements are immutable once published; only the fan-out total is recorded later.</summary>
public interface ISystemAnnouncementRepository
{
    void Add(SystemAnnouncement announcement);

    /// <summary>The announcement, tracked, so completing its fan-out can record the recipient count.</summary>
    Task<SystemAnnouncement?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Which of <paramref name="roleIds"/> exist.</summary>
    Task<IReadOnlySet<string>> GetExistingRoleIdsAsync(IReadOnlyCollection<string> roleIds, CancellationToken cancellationToken);

    /// <summary>How many notifications the announcement has created so far, one per recipient.</summary>
    Task<int> CountNotificationsAsync(Guid announcementId, CancellationToken cancellationToken);
}
